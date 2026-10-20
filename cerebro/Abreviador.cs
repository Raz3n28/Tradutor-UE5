using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Abreviacoes para as telas apertadas (so no modo com chave). Duas entradas:
//  - botao "Abreviar com IA" do glossario: os termos selecionados;
//  - tela marcada como apertada: todos os textos vistos nela (e os que aparecerem depois).
// Fila + lotes, um pedido por vez, no ritmo dos "pedidos por minuto" (Tick vem do relogio da janela).
// Texto que ainda nao e termo do glossario vira termo (nao travado) para guardar a abreviacao.
// Tipo do pedido: atributo -> sigla curta (STR, INS); menu/outros -> rotulo abreviado (Alloc.);
// nomes (cla, talento, titulo...) -> nome mais curto, sem virar sigla.
internal static class Abreviador
{
    private const int Lote = 40;
    private const int CatAtributo = 9;
    private const int CatMenu = 11;

    public static bool Rodando;
    public static string Status = "";
    public static bool StatusErro;

    private static readonly List<string> fila = new List<string>();
    private static readonly HashSet<string> naFila = new HashSet<string>();
    private static readonly HashSet<string> tentados = new HashSet<string>();   // nesta sessao (sem repetir pedido)
    private static DateTime proximo = DateTime.MinValue;

    private static bool ModoChave(bool avisar)
    {
        Provedor p = LiveConfig.Atual;
        string motivo;
        if (p.Tipo == 0) { if (avisar) { Status = LiveTxt.T("cia_gratis"); StatusErro = true; } return false; }
        if (!Provedores.Configurado(p, out motivo)) { if (avisar) { Status = motivo; StatusErro = true; } return false; }
        return true;
    }

    // botao do glossario
    public static void Iniciar(List<TermoGlossario> selecionados)
    {
        if (!ModoChave(true)) return;
        int n = 0;
        for (int i = 0; i < selecionados.Count; i++)
        {
            if (!string.IsNullOrEmpty(selecionados[i].Curta)) continue;
            tentados.Remove(selecionados[i].Original);   // o jogador pediu: tenta de novo
            if (Pedir(selecionados[i].Original)) n++;
        }
        if (n == 0 && !Rodando) { Status = LiveTxt.T("abr_nada"); StatusErro = false; }
    }

    // tela marcada: todos os textos vistos nela
    public static void Tela(string lugar)
    {
        if (!ModoChave(false)) return;
        List<string> t = Ponte.TextosDe(lugar);
        for (int i = 0; i < t.Count; i++) Pedir(t[i]);
    }

    // um texto (precisa ter traducao conhecida e ainda nao ter abreviacao)
    public static bool Pedir(string texto)
    {
        if (string.IsNullOrEmpty(texto) || naFila.Contains(texto) || tentados.Contains(texto)) return false;
        if (!ModoChave(false)) return false;
        TermoGlossario g = Glossario.Obter(texto);
        if (g != null && !string.IsNullOrEmpty(g.Curta)) return false;
        string trad = Traducao(texto);
        if (trad == null || Letras(trad) <= 4) return false;   // ja e curto
        fila.Add(texto);
        naFila.Add(texto);
        return true;
    }

    private static string Traducao(string texto)
    {
        TermoGlossario g = Glossario.Obter(texto);
        if (g != null) return g.Traducao;
        return Ponte.Traducao(texto);
    }

    private static int Letras(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++) if (char.IsLetterOrDigit(s[i])) n++;
        return n;
    }

    private static int Cat(string texto)
    {
        TermoGlossario g = Glossario.Obter(texto);
        return g != null ? g.Cat : Glossario.Categorizar(texto, "", -1);
    }

    // relogio da janela
    public static void Tick()
    {
        if (Rodando || fila.Count == 0 || DateTime.Now < proximo) return;
        if (!ModoChave(true)) { fila.Clear(); naFila.Clear(); return; }
        int n = Mathf.Min(Lote, fila.Count);
        List<string> parte = fila.GetRange(0, n);
        fila.RemoveRange(0, n);
        for (int i = 0; i < parte.Count; i++) { naFila.Remove(parte[i]); tentados.Add(parte[i]); }
        _ = Rodar(LiveConfig.Atual, parte);
    }

    private static async Task Rodar(Provedor p, List<string> parte)
    {
        Rodando = true;
        StatusErro = false;
        Status = LiveTxt.F("abr_andamento", parte.Count, parte.Count + fila.Count);
        int feitos = 0;
        try
        {
            List<string> trads = new List<string>();
            List<int> cats = new List<int>();
            List<string> itens = new List<string>();
            for (int i = 0; i < parte.Count; i++)
            {
                string t = Traducao(parte[i]) ?? "";
                int c = Cat(parte[i]);
                trads.Add(t);
                cats.Add(c);
                string tipo = c == CatAtributo ? "stat" : (c == CatMenu || c == 0 ? "label" : "name");
                itens.Add(parte[i] + " || " + t + " || " + tipo);
            }
            Resposta resp = await Http.Enviar(Provedores.MontarAbreviar(p, itens));
            if (resp.Codigo != 200)
            {
                Status = LiveTxt.F("cia_erro", resp.Codigo, string.IsNullOrEmpty(resp.Erro) ? "" : resp.Erro);
                StatusErro = true;
            }
            else
            {
                List<string> curtas = Provedores.InterpretarLista(p, resp.Corpo, itens.Count);
                if (curtas == null) { Status = LiveTxt.F("abr_fim", 0, parte.Count, parte.Count); }
                else
                {
                    for (int i = 0; i < parte.Count; i++)
                    {
                        string c = (curtas[i] ?? "").Trim();
                        if (c.Length == 0 || trads[i].Length == 0 || c.Length >= trads[i].Length) continue;   // nao ficou mais curta
                        TermoGlossario g = Glossario.Obter(parte[i]);
                        if (g == null)
                        {
                            Glossario.Definir(parte[i], trads[i], cats[i], false);
                            g = Glossario.Obter(parte[i]);
                        }
                        if (g == null || !string.IsNullOrEmpty(g.Curta)) continue;
                        g.Curta = c;
                        feitos++;
                    }
                    if (feitos > 0)
                    {
                        Glossario.Marcar();
                        Ponte.Reenviar = true;
                    }
                    Status = LiveTxt.F("abr_fim", feitos, parte.Count, 0);
                }
            }
        }
        catch (Exception e)
        {
            Status = LiveTxt.F("comp_erro", e.Message);
            StatusErro = true;
        }
        LiveLog.Info("lg_abr", feitos, parte.Count);
        proximo = DateTime.Now.AddMilliseconds(60000f / Mathf.Max(1, LiveConfig.Rpm));
        Rodando = false;
    }
}
