using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// Gasto da chave: soma os tokens que cada resposta da IA informa ("usage"), por dia e por provedor,
// em dados\gasto.txt (dia<TAB>provedor<TAB>entrada<TAB>saida<TAB>cache). O custo e estimado com o preco que o
// jogador coloca em Configuracoes (US$ por 1 milhao de tokens) e a cotacao do dolar.
internal static class Gasto
{
    private sealed class Conta { public long Entrada, Saida, Cache; }

    private static readonly Dictionary<string, Conta> contas = new Dictionary<string, Conta>();   // "dia|provedor"
    private static bool carregado;
    private static DateTime ultimaGravacao = DateTime.MinValue;

    private static string Arquivo { get { return Path.Combine(LiveConfig.Pasta, "gasto.txt"); } }

    private static readonly Regex rxEntrada = new Regex("\"(prompt_tokens|input_tokens|cache_creation_input_tokens|cache_read_input_tokens)\"\\s*:\\s*(\\d+)");
    private static readonly Regex rxSaida = new Regex("\"(completion_tokens|output_tokens)\"\\s*:\\s*(\\d+)");
    private static readonly Regex rxCache = new Regex("\"(cache_read_input_tokens|prompt_cache_hit_tokens|cached_tokens)\"\\s*:\\s*(\\d+)");

    private static void Carregar()
    {
        if (carregado) return;
        carregado = true;
        try
        {
            if (!File.Exists(Arquivo)) return;
            foreach (string l in File.ReadAllLines(Arquivo, Encoding.UTF8))
            {
                string[] p = l.Split('\t');
                if (p.Length < 5) continue;
                Conta c = new Conta();
                long.TryParse(p[2], out c.Entrada);
                long.TryParse(p[3], out c.Saida);
                long.TryParse(p[4], out c.Cache);
                contas[p[0] + "|" + p[1]] = c;
            }
        }
        catch (Exception e) { LiveLog.Erro("le_ler", e, "gasto.txt"); }
    }

    // resposta 200 de uma IA: le o "usage" (OpenAI, DeepSeek, Gemini, Groq: prompt/completion; Claude: input/output
    // + cache). Entrada = tudo que foi mandado (com o que veio do cache); Cache = a parte lida do cache.
    public static void Registrar(string corpo)
    {
        if (string.IsNullOrEmpty(corpo) || corpo.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) return;
        try
        {
            Carregar();
            int u = corpo.LastIndexOf("\"usage\"", StringComparison.Ordinal);
            string trecho = corpo.Substring(u);
            long entrada = 0, saida = 0, cache = 0;
            bool claude = trecho.IndexOf("\"input_tokens\"", StringComparison.Ordinal) >= 0;
            HashSet<string> vistos = new HashSet<string>();
            foreach (Match m in rxEntrada.Matches(trecho))
            {
                string k = m.Groups[1].Value;
                if (!vistos.Add(k)) continue;
                // OpenAI: prompt_tokens ja inclui o cache; Claude: input_tokens e so o resto (soma as 3 partes)
                if (!claude && k != "prompt_tokens") continue;
                entrada += long.Parse(m.Groups[2].Value);
            }
            foreach (Match m in rxSaida.Matches(trecho)) { saida = long.Parse(m.Groups[2].Value); break; }
            foreach (Match m in rxCache.Matches(trecho)) { cache = long.Parse(m.Groups[2].Value); break; }
            if (entrada == 0 && saida == 0) return;
            string chave = DateTime.Now.ToString("yyyy-MM-dd") + "|" + LiveConfig.ProvedorId;
            Conta c;
            if (!contas.TryGetValue(chave, out c)) { c = new Conta(); contas[chave] = c; }
            c.Entrada += entrada;
            c.Saida += saida;
            c.Cache += cache;
            if ((DateTime.Now - ultimaGravacao).TotalSeconds > 10) Gravar();
        }
        catch (Exception e) { LiveLog.Erro("le_parte", e, "gasto"); }
    }

    public static void Gravar()
    {
        if (!carregado) return;
        ultimaGravacao = DateTime.Now;
        try
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, Conta> kv in contas)
            {
                string[] k = kv.Key.Split('|');
                sb.Append(k[0]).Append('\t').Append(k[1]).Append('\t').Append(kv.Value.Entrada).Append('\t')
                  .Append(kv.Value.Saida).Append('\t').Append(kv.Value.Cache).Append('\n');
            }
            File.WriteAllText(Arquivo, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e) { LiveLog.Erro("le_gravar", e, "gasto.txt"); }
    }

    // soma do provedor atual: hoje (mes = false) ou no mes (mes = true). [entrada, saida, cache]
    public static long[] Total(bool mes)
    {
        Carregar();
        string hoje = DateTime.Now.ToString("yyyy-MM-dd");
        string prefixo = mes ? hoje.Substring(0, 7) : hoje;
        long[] t = new long[3];
        foreach (KeyValuePair<string, Conta> kv in contas)
        {
            string[] k = kv.Key.Split('|');
            if (k[1] != LiveConfig.ProvedorId || !k[0].StartsWith(prefixo, StringComparison.Ordinal)) continue;
            t[0] += kv.Value.Entrada;
            t[1] += kv.Value.Saida;
            t[2] += kv.Value.Cache;
        }
        return t;
    }

    // custo em US$ (-1 = sem preco configurado). Leitura do cache custa ~10% da entrada.
    public static double Custo(long[] t)
    {
        double pe, ps;
        if (!LiveConfig.Preco(LiveConfig.ProvedorId, out pe, out ps)) return -1;
        double cache = Math.Min(t[2], t[0]);
        return ((t[0] - cache) * pe + cache * pe * 0.1 + t[1] * ps) / 1000000.0;
    }

    public static string Texto(long[] t)
    {
        string tokens = Fmt(t[0] + t[1]) + " tokens";
        double c = Custo(t);
        if (c < 0) return tokens;
        string f = c < 1 ? "0.000" : "0.00";
        return tokens + "  ·  US$ " + c.ToString(f, CultureInfo.InvariantCulture) + "  ·  R$ " + (c * LiveConfig.Cotacao).ToString(f, CultureInfo.InvariantCulture);
    }

    private static string Fmt(long n)
    {
        if (n >= 1000000) return (n / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        if (n >= 1000) return (n / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
        return n.ToString();
    }
}
