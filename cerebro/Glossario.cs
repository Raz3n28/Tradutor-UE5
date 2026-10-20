using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed class TermoGlossario
{
    public string Original;
    public string Traducao;
    public int Cat;
    public bool Travado;
}

// Glossario: so termos curtos (nomes, itens, armas, habilidades, estacoes, facoes, botoes, titulos).
// O que estiver aqui vale ANTES do cache e da IA. O jogador pode trocar a traducao, a categoria e travar o termo.
// Um arquivo por idioma de destino: glossario_<idioma>.txt  (original<TAB>traducao<TAB>categoria<TAB>travado)
internal static class Glossario
{
    // Categorias (nomes, palavras-chave e instrucoes): ver Categorias.cs

    private static readonly Dictionary<string, TermoGlossario> mapa = new Dictionary<string, TermoGlossario>();
    private static readonly List<TermoGlossario> lista = new List<TermoGlossario>();
    private static string idiomaCarregado;
    private static bool sujo;

    // sobe a cada mudanca (o menu usa para saber quando refazer o filtro)
    public static int Versao;

    public static int Total { get { return lista.Count; } }
    public static List<TermoGlossario> Lista { get { return lista; } }

    private static string Arquivo(string idioma)
    {
        return Path.Combine(LiveConfig.Pasta, "glossario_" + idioma + ".txt");
    }

    public static void Carregar()
    {
        SalvarSeSujo();
        mapa.Clear();
        lista.Clear();
        idiomaCarregado = LiveConfig.Idioma;
        Versao++;
        string arq = Arquivo(idiomaCarregado);
        if (!File.Exists(arq)) return;
        try
        {
            string[] linhas = File.ReadAllLines(arq, Encoding.UTF8);
            for (int i = 0; i < linhas.Length; i++)
            {
                string[] p = linhas[i].Split('\t');
                if (p.Length < 2) continue;
                string orig = Desescapar(p[0]);
                string trad = Desescapar(p[1]);
                if (orig.Length == 0 || trad.Length == 0) continue;
                int cat = 0;
                if (p.Length > 2) int.TryParse(p[2], out cat);
                if (!Categorias.Existe(cat)) cat = 0;
                bool trav = p.Length > 3 && p[3] == "1";
                Criar(orig, trad, cat, trav);
            }
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_ler", e, LiveLog.T("it_glos"));
        }
        LiveLog.Info("lg_glossario", idiomaCarregado, lista.Count);
    }

    public static void TrocouIdioma()
    {
        if (idiomaCarregado != LiveConfig.Idioma) Carregar();
    }

    private static TermoGlossario Criar(string orig, string trad, int cat, bool trav)
    {
        TermoGlossario t;
        if (mapa.TryGetValue(orig, out t))
        {
            t.Traducao = trad;
            t.Cat = cat;
            t.Travado = trav;
            return t;
        }
        t = new TermoGlossario();
        t.Original = orig;
        t.Traducao = trad;
        t.Cat = cat;
        t.Travado = trav;
        mapa[orig] = t;
        lista.Add(t);
        return t;
    }

    public static bool TryGet(string original, out string traducao)
    {
        TermoGlossario t;
        if (mapa.TryGetValue(original, out t))
        {
            traducao = t.Traducao;
            return true;
        }
        traducao = null;
        return false;
    }

    public static bool Existe(string original)
    {
        return mapa.ContainsKey(original);
    }

    // Chamado pelo motor quando uma traducao nova chega (ou sai do cache): se for um termo curto, entra no glossario.
    public static void Auto(string original, string traducao, string pista)
    {
        Auto(original, traducao, pista, -1);
    }

    // catIA = categoria que a IA escolheu (modo com chave), -1 = nao veio
    public static void Auto(string original, string traducao, string pista, int catIA)
    {
        try
        {
            if (!Termo(original) || string.IsNullOrEmpty(traducao)) return;
            if (traducao.Length > 80 || traducao == original) return;
            if (mapa.ContainsKey(original)) return;
            Criar(original, traducao, Categorizar(original, pista, catIA), false);
            Marcar();
        }
        catch { }
    }

    // Retraducao chegou (a anterior era provisoria, do endereco reserva): troca o termo SO se o jogador
    // nao travou. Termo travado e escolha do jogador e fica como esta.
    public static void TrocarAuto(string original, string traducao)
    {
        try
        {
            TermoGlossario t;
            if (!mapa.TryGetValue(original, out t))
            {
                Auto(original, traducao, null);
                return;
            }
            if (t.Travado || string.IsNullOrEmpty(traducao) || t.Traducao == traducao) return;
            if (traducao.Length > 80 || traducao == original) return;
            t.Traducao = traducao;
            Marcar();
        }
        catch { }
    }

    // Jogador adicionou/editou: fica travado.
    public static void Definir(string original, string traducao, int cat, bool travado)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(traducao)) return;
        if (!Categorias.Existe(cat)) cat = 0;
        Criar(original, traducao, cat, travado);
        Marcar();
    }

    public static void Remover(TermoGlossario t)
    {
        if (t == null) return;
        mapa.Remove(t.Original);
        lista.Remove(t);
        Marcar();
    }

    // Ao limpar o cache: so o que o jogador nao travou sai (o resto e o trabalho dele).
    public static void LimparNaoTravados()
    {
        for (int i = lista.Count - 1; i >= 0; i--)
        {
            if (!lista[i].Travado)
            {
                mapa.Remove(lista[i].Original);
                lista.RemoveAt(i);
            }
        }
        Marcar();
    }

    public static void Marcar()
    {
        sujo = true;
        Versao++;
    }

    public static void SalvarSeSujo()
    {
        if (sujo) Salvar();
        Categorias.SalvarSeSujo();
    }

    // categoria apagada: os termos dela vao para outra
    public static void MoverCategoria(int de, int para)
    {
        for (int i = 0; i < lista.Count; i++)
            if (lista[i].Cat == de) lista[i].Cat = para;
        Marcar();
    }

    // Refaz a categoria dos termos NAO travados pelas palavras-chave (so muda quando alguma palavra-chave casa).
    // Devolve quantos mudaram. Termos travados (inclusive os que o jogador categorizou na mao) nao mudam.
    public static int Reclassificar()
    {
        int n = 0;
        for (int i = 0; i < lista.Count; i++)
        {
            TermoGlossario t = lista[i];
            if (t.Travado) continue;
            int c = Categorias.PorTexto(t.Original, 0);
            if (c >= 0 && c != t.Cat)
            {
                t.Cat = c;
                n++;
            }
        }
        if (n > 0) Marcar();
        return n;
    }

    private static string Conteudo()
    {
        string[] linhas = new string[lista.Count];
        for (int i = 0; i < lista.Count; i++)
        {
            TermoGlossario t = lista[i];
            linhas[i] = Escapar(t.Original) + "\t" + Escapar(t.Traducao) + "\t" + t.Cat + "\t" + (t.Travado ? "1" : "0");
        }
        return string.Join("\n", linhas) + (linhas.Length > 0 ? "\n" : "");
    }

    // Exportar (aba Avancado): o glossario no mesmo formato do arquivo do mod.
    public static int Exportar(string arq)
    {
        File.WriteAllText(arq, Conteudo(), new UTF8Encoding(false));
        return lista.Count;
    }

    // Importar: so entram termos que o jogador ainda nao tem (os dele, travados ou nao, ficam como estao).
    public static int Importar(string arq)
    {
        string[] linhas = File.ReadAllLines(arq, Encoding.UTF8);
        int n = 0;
        for (int i = 0; i < linhas.Length; i++)
        {
            string[] p = linhas[i].Split('\t');
            if (p.Length < 2) continue;
            string orig = Desescapar(p[0]);
            string trad = Desescapar(p[1]);
            if (orig.Length == 0 || trad.Length == 0 || mapa.ContainsKey(orig)) continue;
            int cat = 0;
            if (p.Length > 2) int.TryParse(p[2], out cat);
            if (!Categorias.Existe(cat)) cat = 0;
            Criar(orig, trad, cat, p.Length > 3 && p[3] == "1");
            n++;
        }
        if (n > 0) Marcar();
        return n;
    }

    public static void Salvar()
    {
        sujo = false;
        try
        {
            string idioma = idiomaCarregado ?? LiveConfig.Idioma;
            File.WriteAllText(Arquivo(idioma), Conteudo(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_gravar", e, LiveLog.T("it_glos"));
        }
    }

    // Termos do glossario que aparecem DENTRO dos textos (vai no pedido para a IA, para o nome sair igual
    // em frases longas). Travados primeiro (sao escolha do jogador). Texto que e o proprio termo nao conta:
    // esse ja sai do glossario sem ir para a IA.
    public static List<TermoGlossario> TermosEm(List<string> textos, int max, int maxChars)
    {
        List<TermoGlossario> r = new List<TermoGlossario>();
        if (textos == null || textos.Count == 0 || lista.Count == 0) return r;
        int chars = 0;
        for (int passo = 0; passo < 2; passo++)
        {
            bool travados = passo == 0;
            for (int i = 0; i < lista.Count; i++)
            {
                if (r.Count >= max || chars >= maxChars) return r;
                TermoGlossario t = lista[i];
                if (t.Travado != travados || t.Original.Length < 2) continue;
                for (int k = 0; k < textos.Count; k++)
                {
                    string s = textos[k];
                    if (s.Length <= t.Original.Length) continue;
                    if (!Contem(s, t.Original)) continue;
                    r.Add(t);
                    chars += t.Original.Length + t.Traducao.Length + 6;
                    break;
                }
            }
        }
        return r;
    }

    // Procura o termo no texto. Termo que comeca/termina com letra latina precisa estar separado
    // (senao "Ion" acharia "Station"). Chines, japones e coreano nao tem espaco entre palavras.
    private static bool Contem(string s, string termo)
    {
        int ini = 0;
        while (true)
        {
            int p = s.IndexOf(termo, ini, StringComparison.Ordinal);
            if (p < 0) return false;
            int fim = p + termo.Length;
            bool antesOk = p == 0 || !Latina(termo[0]) || !char.IsLetterOrDigit(s[p - 1]);
            bool depoisOk = fim >= s.Length || !Latina(termo[termo.Length - 1]) || !char.IsLetterOrDigit(s[fim]);
            if (antesOk && depoisOk) return true;
            ini = p + 1;
        }
    }

    private static bool Latina(char c)
    {
        return char.IsLetterOrDigit(c) && c < '\u3000';
    }

    // ---------------- o que vale como "termo" ----------------

    // curto, sem frase (sem pontuacao de frase), sem marcadores {n}, ate 5 palavras
    public static bool Termo(string s)
    {
        if (s == null) return false;
        int n = s.Length;
        if (n < 2 || n > 40) return false;
        int palavras = 1;
        bool letra = false;
        for (int i = 0; i < n; i++)
        {
            char c = s[i];
            if (char.IsLetter(c)) letra = true;
            else if (c == ' ') palavras++;
            else if (c == '{' || c == '\n' || c == '\r' || c == '\t') return false;
            else if (c == '.' || c == '!' || c == '?' || c == ';' || c == ',' ||
                     c == '\u3002' || c == '\uFF01' || c == '\uFF1F' || c == '\uFF0C' || c == '\uFF1B') return false;
        }
        return letra && palavras <= 5;
    }

    // ---------------- categoria automatica (por pistas e palavras-chave, sem gastar token) ----------------

    // pista = nomes do objeto de texto e dos pais na tela, em minusculas (ex.: "btn_ok/menu/")
    public static int Categorizar(string texto, string pista)
    {
        return Categorizar(texto, pista, -1);
    }

    public static int Categorizar(string texto, string pista, int catIA)
    {
        string p = pista ?? "";
        // 0) palavras-chave das categorias criadas pelo jogador (ganham de tudo)
        int c = Categorias.PorTexto(texto, 1);
        if (c >= 0) return c;

        // 0b) modo com chave: a categoria que a IA escolheu (ela entende o que o termo e)
        if (catIA >= 0 && Categorias.Existe(catIA)) return catIA;

        // 1) pista "de significado" no nome do objeto
        if (Tem(p, "faction") || Tem(p, "sect")) return 1;
        if (Tem(p, "weapon")) return 3;
        if (Tem(p, "skill") || Tem(p, "ability") || Tem(p, "talent") || Tem(p, "gongfa")) return 4;
        if (Tem(p, "place") || Tem(p, "area") || Tem(p, "map")) return 5;
        if (Tem(p, "item") || Tem(p, "equip") || Tem(p, "inventory")) return 2;

        // 2) palavras-chave das categorias padrao (editaveis no menu)
        c = Categorias.PorTexto(texto, 2);
        if (c >= 0) return c;

        // 3) pista "de forma"
        if (Tem(p, "button") || Tem(p, "btn")) return 7;
        if (Tem(p, "title") || Tem(p, "header") || Tem(p, "caption")) return 8;
        if (Tem(p, "name")) return 6;
        return 0;
    }

    private static bool Tem(string s, string parte)
    {
        return s.IndexOf(parte, StringComparison.Ordinal) >= 0;
    }

    // ---------------- arquivo ----------------

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
