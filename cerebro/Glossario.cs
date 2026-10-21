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
    // quantas vezes a IA devolveu esta mesma traducao (Confirmado a partir de VistosConfirmar)
    public int Vistos;
    // outra traducao que a IA deu depois (conflito: o jogador escolhe). "" = sem conflito
    public string Alternativa = "";
    // frase do jogo onde o termo apareceu (para revisar com seguranca) e a tela (widget) onde foi visto
    public string Exemplo = "";
    public string Onde = "";
    // abreviacao para as telas apertadas (LiveConfig.TelasCurtas). "" = sem abreviacao
    public string Curta = "";
    // veio do pacote xianxia (grupo "x0".."x6"); "" = nao veio
    public string Pacote = "";

    public bool Confirmado { get { return Vistos >= Glossario.VistosConfirmar; } }
    public bool Conflito { get { return !string.IsNullOrEmpty(Alternativa); } }
}

// Glossario: termos curtos (nomes, lugares, seitas, reinos, tecnicas, pilulas, artefatos, itens, menus...).
// O que estiver aqui vale ANTES do cache e da IA. O jogador pode trocar a traducao, a categoria e travar o termo.
// Um arquivo por idioma de destino: glossario_<idioma>.txt
//   #v<TAB>2  e  original<TAB>traducao<TAB>categoria<TAB>travado<TAB>vistos<TAB>alternativa<TAB>exemplo<TAB>onde<TAB>curta<TAB>pacote
// (arquivo sem "#v 2" e do ModLive: so as 4 primeiras colunas e as categorias antigas, ver Categorias.Migrar)
//
// Niveis: novo (a IA traduziu uma vez) -> confirmado (a IA repetiu a mesma traducao 3 vezes) -> travado (jogador).
// No modo com chave, os confirmados e travados sao obrigatorios: a traducao que nao usou o termo e refeita uma vez.
internal static class Glossario
{
    // Categorias (nomes, palavras-chave e instrucoes): ver Categorias.cs

    public const int VistosConfirmar = 3;
    public const int MaxTermoCJK = 12;   // termo em chines/japones/coreano: no maximo isso de letras

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
            bool v2 = linhas.Length > 0 && linhas[0].StartsWith("#v\t2", StringComparison.Ordinal);
            for (int i = 0; i < linhas.Length; i++)
            {
                if (linhas[i].StartsWith("#", StringComparison.Ordinal)) continue;
                TermoGlossario t = Ler(linhas[i], v2);
                if (t != null) Criar(t);
            }
            if (!v2 && lista.Count > 0) sujo = true;   // grava ja no formato novo
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

    // uma linha do arquivo (v2 = formato novo; senao categoria antiga, migrada)
    private static TermoGlossario Ler(string linha, bool v2)
    {
        string[] p = linha.Split('\t');
        if (p.Length < 2) return null;
        TermoGlossario t = new TermoGlossario();
        t.Original = Desescapar(p[0]);
        t.Traducao = Desescapar(p[1]);
        if (t.Original.Length == 0 || t.Traducao.Length == 0) return null;
        int cat = 0;
        if (p.Length > 2) int.TryParse(p[2], out cat);
        if (!v2) cat = Categorias.Migrar(cat);
        t.Cat = Categorias.Existe(cat) ? cat : 0;
        t.Travado = p.Length > 3 && p[3] == "1";
        int vistos = 0;
        if (p.Length > 4) int.TryParse(p[4], out vistos);
        t.Vistos = Mathf.Clamp(vistos, 0, 999);
        if (p.Length > 5) t.Alternativa = Desescapar(p[5]);
        if (p.Length > 6) t.Exemplo = Desescapar(p[6]);
        if (p.Length > 7) t.Onde = Desescapar(p[7]);
        if (p.Length > 8) t.Curta = Desescapar(p[8]);
        if (p.Length > 9) t.Pacote = p[9];
        return t;
    }

    private static string Linha(TermoGlossario t)
    {
        return Escapar(t.Original) + "\t" + Escapar(t.Traducao) + "\t" + t.Cat + "\t" + (t.Travado ? "1" : "0")
            + "\t" + t.Vistos + "\t" + Escapar(t.Alternativa ?? "") + "\t" + Escapar(t.Exemplo ?? "") + "\t" + Escapar(t.Onde ?? "") + "\t" + Escapar(t.Curta ?? "") + "\t" + (t.Pacote ?? "");
    }

    private static void Criar(TermoGlossario novo)
    {
        TermoGlossario t = Criar(novo.Original, novo.Traducao, novo.Cat, novo.Travado);
        t.Vistos = novo.Vistos;
        t.Alternativa = novo.Alternativa ?? "";
        t.Exemplo = novo.Exemplo ?? "";
        t.Onde = novo.Onde ?? "";
        t.Curta = novo.Curta ?? "";
        t.Pacote = novo.Pacote ?? "";
    }

    public static TermoGlossario Obter(string original)
    {
        TermoGlossario t;
        return original != null && mapa.TryGetValue(original, out t) ? t : null;
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
        Visto(original, traducao, pista, catIA, null);
    }

    // A IA traduziu (ou achou dentro de uma frase) este termo com esta traducao.
    // Termo novo: entra como "novo". Ja existe: mesma traducao = +1 visto (3 = confirmado);
    // traducao diferente num termo nao travado = conflito (o jogador escolhe na aba Glossario).
    public static void Visto(string original, string traducao, string pista, int catIA, string exemplo)
    {
        try
        {
            if (!Termo(original) || string.IsNullOrEmpty(traducao)) return;
            traducao = traducao.Trim();
            if (traducao.Length == 0 || traducao.Length > 80 || traducao == original) return;
            TermoGlossario t;
            if (!mapa.TryGetValue(original, out t))
            {
                t = Criar(original, traducao, Categorizar(original, pista, catIA), false);
                t.Vistos = 1;
                t.Onde = Onde(pista);
                t.Exemplo = Exemplo(exemplo);
                Marcar();
                return;
            }
            bool mudou = false;
            if (string.Equals(t.Traducao, traducao, StringComparison.OrdinalIgnoreCase))
            {
                if (t.Vistos < 999) { t.Vistos++; mudou = t.Vistos <= VistosConfirmar; }
            }
            else if (!t.Travado && !string.Equals(t.Alternativa, traducao, StringComparison.OrdinalIgnoreCase))
            {
                t.Alternativa = traducao;
                mudou = true;
            }
            if (t.Onde.Length == 0 && !string.IsNullOrEmpty(pista)) { t.Onde = Onde(pista); mudou = true; }
            if (t.Exemplo.Length == 0 && !string.IsNullOrEmpty(exemplo)) { t.Exemplo = Exemplo(exemplo); mudou = true; }
            // categoria ainda "Outros" e a IA sabe qual e: corrige (sem mexer na escolha do jogador)
            if (!t.Travado && t.Cat == 0 && catIA > 0 && Categorias.Existe(catIA)) { t.Cat = catIA; mudou = true; }
            if (mudou) Marcar();
        }
        catch { }
    }

    // Conflito: o jogador escolheu. usarNova = fica a traducao alternativa. O termo fica travado.
    public static void Resolver(TermoGlossario t, bool usarNova)
    {
        if (t == null || !t.Conflito) return;
        if (usarNova) t.Traducao = t.Alternativa;
        t.Alternativa = "";
        t.Travado = true;
        Marcar();
    }

    public static int Conflitos
    {
        get
        {
            int n = 0;
            for (int i = 0; i < lista.Count; i++) if (lista[i].Conflito) n++;
            return n;
        }
    }

    private static string Exemplo(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>|\\{\\d+\\}", "");
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 120 ? s.Substring(0, 119) + "\u2026" : s;
    }

    // "Onde": a tela (widget blueprint) e o nome do texto, tirados da pista. Ex.: "bag / txt_itemname"
    public static string Onde(string pista)
    {
        if (string.IsNullOrEmpty(pista)) return "";
        string dono, widget;
        PartesPista(pista.ToLowerInvariant(), out dono, out widget);
        if (dono.Length == 0) return widget;
        return widget.Length == 0 ? dono : dono + " / " + widget;
    }

    // Pista da ponte = nome completo do UObject do texto, em minusculas:
    // "textblock /engine/transient.gameengine_1:bp_x_c_2.wbp_bag_c_0.widgettree.txt_itemname"
    // dono = o widget blueprint mais proximo (sem wbp_ e _c_0): "bag"; widget = o nome do texto: "txt_itemname"
    private static void PartesPista(string p, out string dono, out string widget)
    {
        dono = "";
        widget = "";
        int esp = p.IndexOf(' ');
        if (esp >= 0) p = p.Substring(esp + 1);
        string[] seg = p.Split(new char[] { '.', ':', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length == 0) return;
        widget = seg[seg.Length - 1];
        if (widget == "widgettree" || widget.EndsWith("_c", StringComparison.Ordinal)) widget = "";
        for (int i = seg.Length - 1; i >= 0; i--)
        {
            string s = seg[i];
            int c = s.LastIndexOf("_c", StringComparison.Ordinal);
            if (c <= 0) continue;
            string resto = s.Substring(c + 2);
            if (resto.Length > 0 && (resto[0] != '_' || !SoDigitos(resto.Substring(1)))) continue;
            string nome = s.Substring(0, c);
            string[] prefixos = new string[] { "wbp_", "wb_", "w_", "ui_", "umg_", "bp_" };
            for (int k = 0; k < prefixos.Length; k++)
                if (nome.StartsWith(prefixos[k], StringComparison.Ordinal)) { nome = nome.Substring(prefixos[k].Length); break; }
            dono = nome;
            return;
        }
    }

    private static bool SoDigitos(string s)
    {
        for (int i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
        return true;
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
        StringBuilder sb = new StringBuilder();
        sb.Append("#v\t2\n");
        for (int i = 0; i < lista.Count; i++) sb.Append(Linha(lista[i])).Append('\n');
        return sb.ToString();
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
        bool v2 = linhas.Length > 0 && linhas[0].StartsWith("#v\t2", StringComparison.Ordinal);
        int n = 0;
        for (int i = 0; i < linhas.Length; i++)
        {
            if (linhas[i].StartsWith("#", StringComparison.Ordinal)) continue;
            TermoGlossario t = Ler(linhas[i], v2);
            if (t == null || mapa.ContainsKey(t.Original)) continue;
            t.Alternativa = "";
            Criar(t);
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
        // travados, depois confirmados, depois novos
        for (int passo = 0; passo < 3; passo++)
        {
            for (int i = 0; i < lista.Count; i++)
            {
                if (r.Count >= max || chars >= maxChars) return r;
                TermoGlossario t = lista[i];
                int nivel = t.Travado ? 0 : (t.Confirmado ? 1 : 2);
                if (nivel != passo || t.Original.Length < 2) continue;
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

    // Termos que aparecem DENTRO do texto (o texto e maior que o termo) e que a traducao tem que usar
    // na forma do glossario (ver LiveMotor, verificacao):
    //  - travados: sempre;
    //  - confirmados e novos: so nomes proprios (personagem, lugar, seita, reino, tecnica, pilula, artefato,
    //    item, titulo). Menu, atributo, missao e "outros" sao palavras comuns que mudam com a frase.
    public static List<TermoGlossario> Obrigatorios(string texto)
    {
        List<TermoGlossario> r = new List<TermoGlossario>();
        if (string.IsNullOrEmpty(texto) || lista.Count == 0) return r;
        for (int i = 0; i < lista.Count; i++)
        {
            TermoGlossario t = lista[i];
            if (t.Original.Length < 2 || texto.Length <= t.Original.Length) continue;
            if (!t.Travado && !NomeProprio(t.Cat)) continue;
            if (Contem(texto, t.Original)) r.Add(t);
        }
        // termo dentro de outro termo da lista (师兄 dentro de 张师兄): so o maior conta
        for (int i = r.Count - 1; i >= 0; i--)
            for (int k = 0; k < r.Count; k++)
                if (k != i && r[k].Original.Length > r[i].Original.Length && r[k].Original.IndexOf(r[i].Original, StringComparison.Ordinal) >= 0)
                {
                    r.RemoveAt(i);
                    break;
                }
        return r;
    }

    // ---------------- memoria de exemplos ----------------

    // Termos travados (editados ou travados pelo jogador) que se PARECEM com os termos curtos do lote:
    // vao no pedido como modelo ("o jogador traduz assim"), para o termo novo sair no mesmo padrao.
    // Ex.: travou 青城派 -> "Seita Qingcheng"; chega 峨眉派 -> a IA ve o exemplo e faz "Seita Emei".
    // Parecido = mesmo fim em chines (派, 丹, 山...), mesma categoria, letras em comum.
    // Termo que ja esta DENTRO do texto nao conta (esse ja vai como glossario).
    public static int Travados
    {
        get
        {
            int n = 0;
            for (int i = 0; i < lista.Count; i++) if (lista[i].Travado) n++;
            return n;
        }
    }

    public static List<TermoGlossario> Exemplos(List<string> textos, int max, int maxChars)
    {
        List<TermoGlossario> r = new List<TermoGlossario>();
        if (textos == null || textos.Count == 0) return r;
        List<TermoGlossario> travados = new List<TermoGlossario>();
        for (int i = 0; i < lista.Count; i++) if (lista[i].Travado) travados.Add(lista[i]);
        if (travados.Count == 0) return r;

        // pontos de cada travado (o melhor ponto entre os textos do lote)
        Dictionary<TermoGlossario, int> pontos = new Dictionary<TermoGlossario, int>();
        for (int k = 0; k < textos.Count; k++)
        {
            string s = textos[k];
            if (!Termo(s) || mapa.ContainsKey(s)) continue;
            int cat = Categorizar(s, "", -1);
            char fim = UltimaLetra(s);
            for (int i = 0; i < travados.Count; i++)
            {
                TermoGlossario t = travados[i];
                if (t.Original == s || s.IndexOf(t.Original, StringComparison.Ordinal) >= 0) continue;
                int p = 0;
                if (fim != '\0' && fim == UltimaLetra(t.Original)) p += 3;
                if (cat > 0 && cat == t.Cat) p += 2;
                p += Math.Min(2, Comuns(s, t.Original));
                if (p < 2) continue;
                int antes;
                if (!pontos.TryGetValue(t, out antes) || p > antes) pontos[t] = p;
            }
        }
        List<KeyValuePair<TermoGlossario, int>> ordem = new List<KeyValuePair<TermoGlossario, int>>(pontos);
        ordem.Sort(delegate (KeyValuePair<TermoGlossario, int> a, KeyValuePair<TermoGlossario, int> b) { return b.Value.CompareTo(a.Value); });
        int chars = 0;
        for (int i = 0; i < ordem.Count && r.Count < max; i++)
        {
            TermoGlossario t = ordem[i].Key;
            int c = t.Original.Length + t.Traducao.Length + 6;
            if (chars + c > maxChars) break;
            chars += c;
            r.Add(t);
        }
        return r;
    }

    // ultima letra chinesa/japonesa/coreana do termo ('\0' se nao tiver)
    private static char UltimaLetra(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
        {
            char c = s[i];
            if (c >= '\u2E80' && c <= '\uFFEF' && char.IsLetter(c)) return c;
            if (char.IsLetter(c)) return '\0';
        }
        return '\0';
    }

    // quantas letras chinesas/japonesas/coreanas os dois termos tem em comum
    private static int Comuns(string a, string b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            char c = a[i];
            if (c < '\u2E80' || !char.IsLetter(c)) continue;
            if (b.IndexOf(c) >= 0) n++;
        }
        return n;
    }

    // categorias de nome proprio (o termo tem que sair igual dentro das frases)
    public static bool NomeProprio(int cat)
    {
        return cat != 0 && cat != 9 && cat != 10 && cat != 11;
    }

    // A traducao usou o termo? Sem diferenca de maiusculas, e aceitando a palavra flexionada
    // (russo, polones, alemao...): cada palavra do termo com 5+ letras vale pelo comeco (tira as 2 ultimas).
    public static bool Usou(string traducao, string termoTraduzido)
    {
        if (string.IsNullOrEmpty(termoTraduzido)) return true;
        if (string.IsNullOrEmpty(traducao)) return false;
        string tr = traducao.ToLowerInvariant();
        string tt = termoTraduzido.ToLowerInvariant().Trim();
        if (tr.IndexOf(tt, StringComparison.Ordinal) >= 0) return true;
        string[] palavras = tt.Split(new char[] { ' ', '-', '\'', '\u2019' }, StringSplitOptions.RemoveEmptyEntries);
        int contadas = 0;
        for (int i = 0; i < palavras.Length; i++)
        {
            string w = palavras[i];
            if (w.Length < 3) continue;
            string raiz = w.Length >= 5 ? w.Substring(0, w.Length - 2) : w;
            if (tr.IndexOf(raiz, StringComparison.Ordinal) < 0) return false;
            contadas++;
        }
        return contadas > 0;
    }

    // Contem() para fora (o motor confere se o termo que a IA achou esta mesmo na frase)
    public static bool Aparece(string texto, string termo)
    {
        return !string.IsNullOrEmpty(texto) && !string.IsNullOrEmpty(termo) && Contem(texto, termo);
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
        int cjk = 0;
        bool letra = false;
        for (int i = 0; i < n; i++)
        {
            char c = s[i];
            if (char.IsLetter(c))
            {
                letra = true;
                if (c >= '\u2E80' && c <= '\uFFEF' && !(c >= '\uFF00' && c <= '\uFF60')) cjk++;
            }
            else if (c == ' ') palavras++;
            else if (c == '{' || c == '\n' || c == '\r' || c == '\t') return false;
            else if (c == '.' || c == '!' || c == '?' || c == ';' || c == ',' ||
                     c == '\u3002' || c == '\uFF01' || c == '\uFF1F' || c == '\uFF0C' || c == '\uFF1B' ||
                     c == '\u3001' || c == '\uFF1A' || c == '\u2026') return false;
            // separador de varios termos numa linha ("A · B · C"): a linha nao e um termo (ver LiveMotor.Composta)
            else if (c == '\u00B7' || c == '\u30FB' || c == '\u2022' || c == '\uFF5C') return false;
        }
        // chines/japones/coreano nao tem espaco: frase curta passaria como "termo"; limite proprio
        if (cjk > MaxTermoCJK) return false;
        return letra && palavras <= 5;
    }

    // ---------------- categoria automatica (por pistas e palavras-chave, sem gastar token) ----------------

    // pista = nomes do objeto de texto e dos pais na tela, em minusculas (ex.: "btn_ok/menu/")
    public static int Categorizar(string texto, string pista)
    {
        return Categorizar(texto, pista, -1);
    }

    // Categoria em camadas (sem gastar token, menos a IA que ja veio junto com a traducao):
    //  0) palavras-chave das categorias criadas pelo jogador (ganham de tudo)
    //  1) a categoria que a IA escolheu (modo com chave)
    //  2) o nome do proprio texto na tela (txt_itemname, btn_ok...)
    //  3) palavras-chave das padrao (o fim do termo manda: 筑基丹, 青城派, 青城山...)
    //  4) a tela onde apareceu (widget blueprint: bag, skill, mainmenu...)
    public static int Categorizar(string texto, string pista, int catIA)
    {
        int c = Categorias.PorTexto(texto, 1);
        if (c >= 0) return c;
        if (catIA >= 0 && Categorias.Existe(catIA)) return catIA;

        string dono = "", widget = "";
        if (!string.IsNullOrEmpty(pista)) PartesPista(pista.ToLowerInvariant(), out dono, out widget);
        c = PorNome(widget, true);
        if (c >= 0) return c;
        c = Categorias.PorTexto(texto, 2);
        if (c >= 0) return c;
        c = PorNome(dono, false);
        return c >= 0 ? c : 0;
    }

    // nome de widget -> categoria. proprio = nome do texto (ai "btn"/"title" = menu); senao, nome da tela.
    private static int PorNome(string n, bool proprio)
    {
        if (string.IsNullOrEmpty(n)) return -1;
        if (Tem(n, "npc") || Tem(n, "speaker") || Tem(n, "talker") || Tem(n, "charname") || Tem(n, "rolename")
            || Tem(n, "role_name") || Tem(n, "playername") || Tem(n, "nickname") || Tem(n, "character")) return 1;
        if (Tem(n, "sect") || Tem(n, "faction") || Tem(n, "clan") || Tem(n, "guild")) return 3;
        if (Tem(n, "realm") || Tem(n, "cultivat") || Tem(n, "jingjie") || Tem(n, "breakthrough")) return 4;
        if (Tem(n, "skill") || Tem(n, "ability") || Tem(n, "talent") || Tem(n, "gongfa") || Tem(n, "technique")
            || Tem(n, "spell") || Tem(n, "martial")) return 5;
        if (Tem(n, "pill") || Tem(n, "alchemy") || Tem(n, "herb") || Tem(n, "danyao") || Tem(n, "elixir")) return 6;
        if (Tem(n, "weapon") || Tem(n, "equip") || Tem(n, "armor") || Tem(n, "artifact") || Tem(n, "fabao") || Tem(n, "gear")) return 7;
        if (Tem(n, "quest") || Tem(n, "mission") || Tem(n, "task") || Tem(n, "objective") || Tem(n, "chapter")) return 10;
        if (Tem(n, "attribute") || Tem(n, "attr_") || Tem(n, "_attr") || Tem(n, "stat_") || Tem(n, "_stat")
            || Tem(n, "property") || Tem(n, "hpbar") || Tem(n, "hp_") || Tem(n, "mp_")) return 9;
        if (Tem(n, "map") || Tem(n, "location") || Tem(n, "place") || Tem(n, "region") || Tem(n, "teleport")
            || Tem(n, "waypoint") || Tem(n, "area")) return 2;
        if (Tem(n, "item") || Tem(n, "bag") || Tem(n, "inventory") || Tem(n, "backpack") || Tem(n, "loot")
            || Tem(n, "shop") || Tem(n, "material") || Tem(n, "reward") || Tem(n, "craft")) return 8;
        if (Tem(n, "button") || Tem(n, "btn") || Tem(n, "menu") || Tem(n, "option") || Tem(n, "setting")
            || Tem(n, "tab_") || Tem(n, "toggle") || Tem(n, "checkbox") || Tem(n, "combo") || Tem(n, "popup")
            || Tem(n, "confirm") || Tem(n, "dialogbox") || Tem(n, "keyhint") || Tem(n, "hint")) return 11;
        if (proprio && (Tem(n, "title") || Tem(n, "header") || Tem(n, "caption") || Tem(n, "label"))) return 11;
        return -1;
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
