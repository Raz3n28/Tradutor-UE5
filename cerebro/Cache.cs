using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Guarda cada traducao no disco (por idioma), para nunca pagar/pedir duas vezes o mesmo texto.
// Formato: chave<TAB>traducao  (com \\ \t \n \r escapados). So acrescenta no fim do arquivo.
internal static class TradCache
{
    private static readonly Dictionary<string, string> mapa = new Dictionary<string, string>();
    private static readonly List<string> pendentes = new List<string>();
    private static string idiomaCarregado;
    private static float ultimoFlush;

    // Traducoes "provisorias": vieram do endereco reserva do Google (pior qualidade).
    // Ficam num arquivo a parte e sao retraduzidas quando um endereco bom estiver livre.
    private static readonly HashSet<string> provisorios = new HashSet<string>();
    private static bool provSujo;

    public static int Total { get { return mapa.Count; } }
    public static int TotalProvisorios { get { return provisorios.Count; } }

    private static string Arquivo(string idioma)
    {
        return Path.Combine(LiveConfig.Pasta, "cache_" + idioma + ".txt");
    }

    private static string ArquivoProv(string idioma)
    {
        return Path.Combine(LiveConfig.Pasta, "provisorio_" + idioma + ".txt");
    }

    public static void Carregar()
    {
        Flush();
        mapa.Clear();
        provisorios.Clear();
        provSujo = false;
        idiomaCarregado = LiveConfig.Idioma;
        string arq = Arquivo(idiomaCarregado);
        if (!File.Exists(arq)) return;
        string[] linhas = File.ReadAllLines(arq, Encoding.UTF8);
        for (int i = 0; i < linhas.Length; i++)
        {
            string l = linhas[i];
            int tab = l.IndexOf('\t');
            if (tab <= 0) continue;
            mapa[Desescapar(l.Substring(0, tab))] = Provedores.Composto(Desescapar(l.Substring(tab + 1)));
        }
        try
        {
            string ap = ArquivoProv(idiomaCarregado);
            if (File.Exists(ap))
            {
                string[] ps = File.ReadAllLines(ap, Encoding.UTF8);
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].Length == 0) continue;
                    string k = Desescapar(ps[i]);
                    if (mapa.ContainsKey(k)) provisorios.Add(k);
                }
            }
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_ler", e, LiveLog.T("it_prov"));
        }
        LiveLog.Info("lg_cache", idiomaCarregado, mapa.Count, provisorios.Count);
    }

    public static void AddProvisorio(string chave)
    {
        if (provisorios.Add(chave)) provSujo = true;
    }

    public static void RemoverProvisorio(string chave)
    {
        if (provisorios.Remove(chave)) provSujo = true;
    }

    // Ate "max" provisorios (somando no maximo "maxChars" letras) que nao estejam em "fora" (ja na fila).
    public static List<string> ProximosProvisorios(int max, int maxChars, int maxTexto, HashSet<string> fora)
    {
        List<string> r = new List<string>();
        int chars = 0;
        foreach (string k in provisorios)
        {
            if (r.Count >= max || chars >= maxChars) break;
            if (fora.Contains(k) || k.Length > maxTexto) continue;
            r.Add(k);
            chars += k.Length;
        }
        // texto longo vai sozinho
        if (r.Count == 0)
        {
            foreach (string k in provisorios)
            {
                if (fora.Contains(k)) continue;
                r.Add(k);
                break;
            }
        }
        return r;
    }

    public static void TrocouIdioma()
    {
        if (idiomaCarregado != LiveConfig.Idioma) Carregar();
    }

    public static bool TryGet(string chave, out string valor)
    {
        return mapa.TryGetValue(chave, out valor);
    }

    public static void Add(string chave, string valor)
    {
        mapa[chave] = valor;
        pendentes.Add(Escapar(chave) + "\t" + Escapar(valor));
    }

    public static void FlushSePreciso(float agora)
    {
        if ((pendentes.Count == 0 && !provSujo) || agora - ultimoFlush < 5f) return;
        Flush();
        ultimoFlush = agora;
    }

    public static void Flush()
    {
        if (provSujo)
        {
            provSujo = false;
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string k in provisorios) sb.Append(Escapar(k)).Append('\n');
                File.WriteAllText(ArquivoProv(idiomaCarregado ?? LiveConfig.Idioma), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                LiveLog.Erro("le_gravar", e, LiveLog.T("it_prov"));
            }
        }
        if (pendentes.Count == 0) return;
        try
        {
            File.AppendAllText(Arquivo(idiomaCarregado ?? LiveConfig.Idioma), string.Join("\n", pendentes.ToArray()) + "\n", new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_gravar", e, LiveLog.T("it_cache"));
        }
        pendentes.Clear();
    }

    // Exportar (aba Avancado): o cache inteiro num arquivo, uma linha por texto (sem repeticoes).
    public static int Exportar(string arq)
    {
        Flush();
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, string> kv in mapa) sb.Append(Escapar(kv.Key)).Append('\t').Append(Escapar(kv.Value)).Append('\n');
        File.WriteAllText(arq, sb.ToString(), new UTF8Encoding(false));
        return mapa.Count;
    }

    // Importar: so entra o que ainda nao esta no cache (o que o jogador ja tem nao muda).
    public static int Importar(string arq)
    {
        string[] linhas = File.ReadAllLines(arq, Encoding.UTF8);
        int n = 0;
        for (int i = 0; i < linhas.Length; i++)
        {
            string l = linhas[i];
            int tab = l.IndexOf('\t');
            if (tab <= 0) continue;
            string k = Desescapar(l.Substring(0, tab));
            string v = Desescapar(l.Substring(tab + 1));
            if (v.Length == 0 || mapa.ContainsKey(k)) continue;
            Add(k, v);
            n++;
        }
        Flush();
        return n;
    }

    public static void Limpar()
    {
        pendentes.Clear();
        mapa.Clear();
        provisorios.Clear();
        provSujo = false;
        try { File.Delete(Arquivo(LiveConfig.Idioma)); } catch { }
        try { File.Delete(ArquivoProv(LiveConfig.Idioma)); } catch { }
    }

    private static string Escapar(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static string Desescapar(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char p = s[++i];
                if (p == 't') sb.Append('\t');
                else if (p == 'n') sb.Append('\n');
                else if (p == 'r') sb.Append('\r');
                else sb.Append(p);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
