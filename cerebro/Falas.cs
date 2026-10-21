using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Falas e descricoes: textos longos agrupados por quem fala (ou pelo item / missao a que pertencem).
// A ponte manda "V nome texto lugar" quando um nome curto e um texto longo aparecem juntos na mesma caixa.
// Arquivos na pasta dados:
//   falas.txt                    nome<TAB>texto<TAB>lugar   (os grupos; valem para qualquer idioma)
//   falas_travadas_<idioma>.txt  texto<TAB>traducao         (o que o jogador editou: vale antes do cache e da IA,
//                                                              e nao sai ao limpar o cache)
internal sealed class GrupoFalas
{
    public string Nome;
    public int Tipo;
    public string Lugar = "";
    public readonly List<string> Textos = new List<string>();
}

internal static class Falas
{
    public const int Personagens = 0, Itens = 1, Missoes = 2, Outros = 3;

    private static readonly Dictionary<string, GrupoFalas> grupos = new Dictionary<string, GrupoFalas>();
    private static readonly Dictionary<string, string> grupoDo = new Dictionary<string, string>();   // texto -> nome
    private static readonly Dictionary<string, string> travadas = new Dictionary<string, string>();
    private static string idiomaTravadas;
    public static int Versao;

    private static string ArqGrupos { get { return Path.Combine(LiveConfig.Pasta, "falas.txt"); } }
    private static string ArqTravadas { get { return Path.Combine(LiveConfig.Pasta, "falas_travadas_" + LiveConfig.Idioma + ".txt"); } }

    public static List<GrupoFalas> Grupos { get { return new List<GrupoFalas>(grupos.Values); } }

    public static void Carregar()
    {
        grupos.Clear();
        grupoDo.Clear();
        try
        {
            if (File.Exists(ArqGrupos))
                foreach (string l in File.ReadAllLines(ArqGrupos, Encoding.UTF8))
                {
                    string[] p = l.Split('\t');
                    if (p.Length < 2) continue;
                    Juntar(Ponte.Desescapar(p[0]), Ponte.Desescapar(p[1]), p.Length > 2 ? Ponte.Desescapar(p[2]) : "");
                }
        }
        catch (Exception e) { LiveLog.Erro("le_ler", e, "falas.txt"); }
        CarregarTravadas();
        Versao++;
    }

    private static void CarregarTravadas()
    {
        travadas.Clear();
        idiomaTravadas = LiveConfig.Idioma;
        try
        {
            if (File.Exists(ArqTravadas))
                foreach (string l in File.ReadAllLines(ArqTravadas, Encoding.UTF8))
                {
                    int t = l.IndexOf('\t');
                    if (t > 0) travadas[Ponte.Desescapar(l.Substring(0, t))] = Ponte.Desescapar(l.Substring(t + 1));
                }
        }
        catch (Exception e) { LiveLog.Erro("le_ler", e, "falas_travadas"); }
    }

    private static void Juntar(string nome, string texto, string lugar)
    {
        string antes;
        if (grupoDo.TryGetValue(texto, out antes))
        {
            if (antes == nome) return;
            GrupoFalas velho;
            if (grupos.TryGetValue(antes, out velho)) velho.Textos.Remove(texto);
        }
        GrupoFalas g;
        if (!grupos.TryGetValue(nome, out g))
        {
            g = new GrupoFalas { Nome = nome, Lugar = lugar ?? "" };
            g.Tipo = TipoDe(nome, lugar);
            grupos[nome] = g;
        }
        g.Textos.Add(texto);
        grupoDo[texto] = nome;
    }

    // a ponte viu nome + texto juntos
    public static void Visto(string nome, string texto, string lugar)
    {
        if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(texto) || grupoDo.ContainsKey(texto)) return;
        Juntar(nome, texto, lugar);
        Acrescentar(nome, texto, lugar);
        Versao++;
    }

    private static void Acrescentar(string nome, string texto, string lugar)
    {
        try
        {
            File.AppendAllText(ArqGrupos, Ponte.Escapar(nome) + "\t" + Ponte.Escapar(texto) + "\t" + Ponte.Escapar(lugar ?? "") + "\n", new UTF8Encoding(false));
        }
        catch (Exception e) { LiveLog.Erro("le_gravar", e, "falas.txt"); }
    }

    // o jogador mudou o texto de grupo (a ligacao automatica errou)
    public static void Mover(string texto, string novoNome)
    {
        if (string.IsNullOrEmpty(novoNome)) return;
        Juntar(novoNome, texto, "");
        Acrescentar(novoNome, texto, "");   // a linha mais nova vale ao carregar
        Versao++;
    }

    // tipo do grupo: pela categoria do nome no glossario; senao pelo nome da tela
    private static int TipoDe(string nome, string lugar)
    {
        TermoGlossario t = Glossario.Obter(Ponte.Miolo(nome).Trim());
        if (t != null)
        {
            if (t.Cat == 1) return Personagens;
            if (t.Cat == 6 || t.Cat == 7 || t.Cat == 8) return Itens;
            if (t.Cat == 10) return Missoes;
        }
        string l = (lugar ?? "").ToLowerInvariant();
        string[] pers = { "dialog", "talk", "story", "chat", "speak", "npc", "bubble" };
        string[] item = { "item", "bag", "goods", "equip", "weapon", "pill", "inventory" };
        string[] miss = { "quest", "task", "mission" };
        foreach (string k in pers) if (l.IndexOf(k, StringComparison.Ordinal) >= 0) return Personagens;
        foreach (string k in item) if (l.IndexOf(k, StringComparison.Ordinal) >= 0) return Itens;
        foreach (string k in miss) if (l.IndexOf(k, StringComparison.Ordinal) >= 0) return Missoes;
        return Outros;
    }

    public static bool Travada(string texto, out string traducao)
    {
        if (idiomaTravadas != LiveConfig.Idioma) CarregarTravadas();
        traducao = null;
        return texto != null && travadas.TryGetValue(texto, out traducao);
    }

    public static bool EstaTravada(string texto)
    {
        string t;
        return Travada(texto, out t);
    }

    public static void Travar(string texto, string traducao)
    {
        if (idiomaTravadas != LiveConfig.Idioma) CarregarTravadas();
        travadas[texto] = traducao;
        SalvarTravadas();
        Versao++;
    }

    public static void Destravar(string texto)
    {
        if (travadas.Remove(texto)) { SalvarTravadas(); Versao++; }
    }

    private static void SalvarTravadas()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in travadas)
                sb.Append(Ponte.Escapar(kv.Key)).Append('\t').Append(Ponte.Escapar(kv.Value)).Append('\n');
            File.WriteAllText(ArqTravadas, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e) { LiveLog.Erro("le_gravar", e, "falas_travadas"); }
    }

    // para o pacote de traducao (compartilhar)
    public static Dictionary<string, string> TodasTravadas()
    {
        if (idiomaTravadas != LiveConfig.Idioma) CarregarTravadas();
        return new Dictionary<string, string>(travadas);
    }
}
