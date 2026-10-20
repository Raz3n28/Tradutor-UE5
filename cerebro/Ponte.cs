using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Conversa com a ponte (mod Lua do UE4SS) por dois arquivos na pasta "troca":
//   pedidos.txt   (ponte -> cerebro)  T<TAB>texto<TAB>pista | M (abrir/fechar menu) | I (jogo abriu)
//   respostas.txt (cerebro -> ponte)  R<TAB>texto<TAB>traducao | N<TAB>texto (nao traduzir) | X (devolver originais)
//                                     | C<TAB>encolher(0/1)<TAB>minimo% (config da ponte)
// Cada lado so acrescenta linhas no fim e lembra ate onde ja leu. A ponte zera os dois quando o jogo abre.
// Texto com \ TAB ou quebra de linha vai escapado (\\ \t \n \r), igual ao cache.
internal static class Ponte
{
    public static string PastaMod = AppContext.BaseDirectory;

    private static string Troca
    {
        get
        {
            string p = Path.Combine(PastaMod, "troca");
            try { if (!Directory.Exists(p)) Directory.CreateDirectory(p); } catch { }
            return p;
        }
    }

    private static string ArqPedidos { get { return Path.Combine(Troca, "pedidos.txt"); } }
    private static string ArqRespostas { get { return Path.Combine(Troca, "respostas.txt"); } }

    private const int MaxEsperando = 5000;
    private static long lido;
    private static readonly StringBuilder resto = new StringBuilder();   // linha que chegou pela metade
    private static readonly StringBuilder saida = new StringBuilder();
    // textos que a ponte pediu e ainda nao tiveram resposta: texto -> pista
    private static readonly Dictionary<string, string> esperando = new Dictionary<string, string>();
    private static bool ativoAntes;
    private static string cfgEnviada;   // ultima linha C mandada (null = mandar de novo)

    public static int Esperando { get { return esperando.Count; } }
    public static int Recebidos;
    public static int Enviados;
    public static DateTime UltimoContato = DateTime.MinValue;

    // a ponte apertou F8
    public static event Action MenuPedido;

    // chamado pelo relogio da janela (thread da janela, junto com o motor)
    public static void Tick()
    {
        Ler();

        if (LiveConfig.Ativo != ativoAntes)
        {
            ativoAntes = LiveConfig.Ativo;
            // pausou: a tela volta ao original; comecou: responde tudo que estava esperando
            if (!LiveConfig.Ativo) saida.Append("X\n");
            else LiveMotor.VarrerAgora = true;
        }
        // memorias esvaziadas (trocou idioma, provedor, estilo...) ou provisorios retraduzidos:
        // a ponte devolve os originais e pede tudo de novo
        if (LiveMotor.RestaurarPendente)
        {
            LiveMotor.FimRestaurar();
            saida.Append("X\n");
        }
        if (LiveMotor.AtualizarPendente)
        {
            LiveMotor.FimAtualizar();
            saida.Append("X\n");
        }
        string cfg = "C\t" + (LiveConfig.Encolher ? "1" : "0") + "\t" + LiveConfig.EncolherMin;
        if (cfg != cfgEnviada)
        {
            cfgEnviada = cfg;
            saida.Append(cfg).Append('\n');
        }
        if (LiveMotor.VarrerAgora)
        {
            LiveMotor.VarrerAgora = false;
            List<KeyValuePair<string, string>> todos = new List<KeyValuePair<string, string>>(esperando);
            for (int i = 0; i < todos.Count; i++) Responder(todos[i].Key, todos[i].Value);
        }
        Gravar();
    }

    private static void Ler()
    {
        string arq = ArqPedidos;
        if (!File.Exists(arq)) return;
        string novo;
        try
        {
            using (FileStream fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < lido)
                {
                    // arquivo zerado: o jogo abriu de novo
                    lido = 0;
                    resto.Clear();
                    NovaSessao();
                }
                if (fs.Length == lido) return;
                fs.Seek(lido, SeekOrigin.Begin);
                byte[] buf = new byte[fs.Length - lido];
                int n = 0;
                while (n < buf.Length)
                {
                    int r = fs.Read(buf, n, buf.Length - n);
                    if (r <= 0) break;
                    n += r;
                }
                // so ate a ultima quebra de linha (o resto ainda esta sendo escrito)
                int fim = Array.LastIndexOf(buf, (byte)'\n', n - 1);
                if (fim < 0) return;
                lido += fim + 1;
                novo = Encoding.UTF8.GetString(buf, 0, fim + 1);
            }
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_ler", e, "pedidos.txt");
            return;
        }
        UltimoContato = DateTime.Now;
        string[] linhas = novo.Split('\n');
        for (int i = 0; i < linhas.Length; i++)
        {
            string l = linhas[i].TrimEnd('\r');
            if (l.Length == 0) continue;
            try { Linha(l); }
            catch (Exception e) { LiveLog.Erro("le_parte", e, "ponte"); }
        }
    }

    private static void Linha(string l)
    {
        string[] p = l.Split('\t');
        switch (p[0])
        {
            case "T":
                if (p.Length < 2) return;
                Recebidos++;
                Responder(Desescapar(p[1]), p.Length > 2 ? Desescapar(p[2]) : "");
                break;
            case "M":
                if (MenuPedido != null) MenuPedido();
                break;
            case "I":
                NovaSessao();
                break;
        }
    }

    private static void NovaSessao()
    {
        esperando.Clear();
        cfgEnviada = null;
        LiveLog.Info("lg_ponte_ok");
    }

    private static void Responder(string texto, string pista)
    {
        if (!LiveConfig.Ativo)
        {
            Esperar(texto, pista);
            return;
        }
        string r = LiveMotor.Traduzir(texto, pista);
        if (r != null)
        {
            esperando.Remove(texto);
            saida.Append("R\t").Append(Escapar(texto)).Append('\t').Append(Escapar(r)).Append('\n');
            Enviados++;
        }
        else if (LiveMotor.Ignorado(texto))
        {
            esperando.Remove(texto);
            saida.Append("N\t").Append(Escapar(texto)).Append('\n');
        }
        else Esperar(texto, pista);
    }

    private static void Esperar(string texto, string pista)
    {
        if (esperando.Count >= MaxEsperando && !esperando.ContainsKey(texto)) return;
        esperando[texto] = pista;
    }

    private static void Gravar()
    {
        if (saida.Length == 0) return;
        try
        {
            using (FileStream fs = new FileStream(ArqRespostas, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] b = new UTF8Encoding(false).GetBytes(saida.ToString());
                fs.Write(b, 0, b.Length);
            }
            saida.Clear();
        }
        catch (Exception e)
        {
            // a ponte pode estar lendo/zerando agora: tenta de novo no proximo tick
            LiveLog.Erro("le_gravar", e, "respostas.txt");
        }
    }

    public static string Escapar(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    public static string Desescapar(string s)
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
