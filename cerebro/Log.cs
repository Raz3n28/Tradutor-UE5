using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Texto de log guardado como CHAVE + argumentos (LiveTxt): a aba Log mostra no idioma do menu,
// o log.txt fica sempre em ingles. Um argumento pode ser outro LogTexto (traduzido junto).
// Chave que nao existe no LiveTxt aparece como esta (ex.: a linha "=====").
internal sealed class LogTexto
{
    public readonly string Chave;
    public readonly object[] Args;

    public LogTexto(string chave, object[] args)
    {
        Chave = chave ?? "";
        Args = args ?? new object[0];
    }

    // idx < 0 = idioma do menu agora; 1 = ingles
    public string Render(int idx)
    {
        string fmt;
        if (!LiveTxt.Tem(Chave)) fmt = Chave;
        else fmt = idx < 0 ? LiveTxt.T(Chave) : LiveTxt.TIdioma(Chave, idx);
        if (Args.Length == 0) return fmt;
        object[] a = new object[Args.Length];
        for (int i = 0; i < Args.Length; i++)
        {
            LogTexto sub = Args[i] as LogTexto;
            a[i] = sub != null ? sub.Render(idx) : Args[i];
        }
        try { return string.Format(fmt, a); }
        catch { return fmt; }
    }
}

// Log do mod: vai para a aba "Log" do menu (ultimas linhas) e para log.txt na pasta do mod
// (o arquivo e apagado a cada vez que o jogo abre).
internal static class LiveLog
{
    private sealed class Linha
    {
        public string Hora;
        public LogTexto Texto;
        public string Extra;    // mensagem da excecao (texto do sistema, nao traduz)
        public bool Ruim;       // erro ou aviso: aparece em vermelho
    }

    private const int MaxLinhas = 300;
    private const int Ingles = 1;
    private static readonly List<Linha> linhas = new List<Linha>();
    private static string arquivo;
    private static bool arquivoIniciado;

    public static int Count { get { return linhas.Count; } }
    public static string Arquivo { get { return arquivo ?? ""; } }

    // linha i ja no idioma do menu
    public static string Texto(int i)
    {
        Linha l = linhas[i];
        return "[" + l.Hora + "] " + l.Texto.Render(-1) + (l.Extra != null ? ": " + l.Extra : "");
    }

    public static bool Ruim(int i)
    {
        return linhas[i].Ruim;
    }

    // para usar como argumento de outra mensagem (ex.: o motivo dentro de "endereco X deu {1}")
    public static LogTexto T(string chave, params object[] args)
    {
        return new LogTexto(chave, args);
    }

    public static void Info(string chave, params object[] args)
    {
        Guardar(new LogTexto(chave, args), null, false);
    }

    public static void Aviso(string chave, params object[] args)
    {
        Guardar(new LogTexto(chave, args), null, true);
    }

    public static void Erro(string chave, Exception e, params object[] args)
    {
        Guardar(new LogTexto(chave, args), e, true);
    }

    public static void Limpar()
    {
        linhas.Clear();
    }

    private static void Guardar(LogTexto texto, Exception e, bool ruim)
    {
        Linha l = new Linha();
        try { l.Hora = DateTime.Now.ToString("HH:mm:ss"); }
        catch { l.Hora = "--:--:--"; }
        l.Texto = texto;
        l.Extra = e != null ? e.Message : null;
        l.Ruim = ruim;
        linhas.Add(l);
        if (linhas.Count > MaxLinhas) linhas.RemoveRange(0, linhas.Count - MaxLinhas);

        string en;
        try { en = texto.Render(Ingles); }
        catch { en = texto.Chave; }
        string linha = "[" + l.Hora + "] " + (e != null ? "ERROR: " : "") + en + (l.Extra != null ? ": " + l.Extra : "");
        try
        {
            if (!arquivoIniciado)
            {
                arquivoIniciado = true;
                arquivo = Path.Combine(LiveConfig.Pasta, "log.txt");
                File.WriteAllText(arquivo, "", new UTF8Encoding(false));
            }
            if (arquivo != null)
                File.AppendAllText(arquivo, linha + (e != null ? "\n" + e.ToString() : "") + "\n", new UTF8Encoding(false));
        }
        catch
        {
            arquivo = null;
        }
    }
}
