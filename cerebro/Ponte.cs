using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Conversa com a ponte (mod Lua do UE4SS) por dois arquivos na pasta "troca":
//   pedidos.txt   (ponte -> cerebro)  T<TAB>texto<TAB>pista | L<TAB>texto<TAB>lugar (onde o texto aparece)
//                                     Q<TAB>lugar<TAB>n (o lugar mostrou n textos ao mesmo tempo)
//                                     M (abrir/fechar menu) | I (jogo abriu)
//   respostas.txt (cerebro -> ponte)  R<TAB>texto<TAB>traducao | N<TAB>texto (nao traduzir) | X (devolver originais)
//                                     C<TAB>lugar<TAB>texto<TAB>abreviacao (vai antes do R)
// Lugar = Glossario.Onde da pista ("attritem / txt_name"). Nos lugares de LiveConfig.TelasCurtas a ponte usa
// a abreviacao do termo (TermoGlossario.Curta) no lugar da traducao inteira.
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
    // lugares onde cada texto apareceu nesta sessao (para o menu oferecer as telas): texto -> lugares
    private static readonly Dictionary<string, List<string>> lugares = new Dictionary<string, List<string>>();
    private const int MaxLugares = 12;
    // lugar -> textos vistos nele (para a lista de telas e para abreviar a tela inteira)
    private static readonly Dictionary<string, List<string>> porLugar = new Dictionary<string, List<string>>();
    private const int MaxPorLugar = 300;
    // ultima traducao mandada para cada texto, sem as marcas de cor (para abreviar textos que nao sao do glossario)
    private static readonly Dictionary<string, string> respondidos = new Dictionary<string, string>();
    // textos (como vieram, com marcas) que ja tem traducao: para mandar as abreviacoes de novo
    private static readonly HashSet<string> brutos = new HashSet<string>();

    // Texto inteiro dentro de marcas de cor ("<Quality5>孤星推命</>"): devolve o miolo ("孤星推命") e as marcas.
    // Sem marcas: o proprio texto. O glossario e as abreviacoes trabalham com o miolo; a tela recebe as marcas de volta.
    private static readonly System.Text.RegularExpressions.Regex rxMiolo =
        new System.Text.RegularExpressions.Regex("^((?:<[^<>/][^<>]*>)*)([^<>]+)((?:</[^<>]*>)*)$");

    public static string Miolo(string s, out string antes, out string depois)
    {
        antes = "";
        depois = "";
        if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
        System.Text.RegularExpressions.Match m = rxMiolo.Match(s);
        if (!m.Success) return s;
        antes = m.Groups[1].Value;
        depois = m.Groups[3].Value;
        return m.Groups[2].Value;
    }

    public static string Miolo(string s)
    {
        string a, d;
        return Miolo(s, out a, out d);
    }

    public static string Traducao(string texto)
    {
        string r;
        return texto != null && respondidos.TryGetValue(texto, out r) ? r : null;
    }

    public static List<string> TextosDe(string lugar)
    {
        List<string> l;
        return lugar != null && porLugar.TryGetValue(lugar, out l) ? l : new List<string>();
    }

    // quantos textos o lugar ja mostrou ao mesmo tempo (2 ou mais = lista; 1 = dica do mouse, titulo...)
    private static readonly Dictionary<string, int> vezes = new Dictionary<string, int>();

    // lugares dentro de caixa de informacao (dica do mouse, resumo, detalhes): o automatico nao mexe neles
    private static readonly HashSet<string> dicas = new HashSet<string>();
    private static readonly string[] palavrasDica = new string[] { "info", "tip", "desc", "detail", "summary", "hover", "popup" };

    public static bool Dica(string lugar)
    {
        if (lugar == null) return false;
        if (dicas.Contains(lugar)) return true;
        int barra = lugar.IndexOf(" / ", StringComparison.Ordinal);
        string dono = barra >= 0 ? lugar.Substring(0, barra) : lugar;
        for (int k = 0; k < palavrasDica.Length; k++)
            if (dono.IndexOf(palavrasDica[k], StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    // Lugar a partir da pista (nome completo do texto), igual ao Lugar() da ponte (main.lua), e se ele esta
    // dentro de uma caixa de informacao: o dono ou um widget blueprint acima dele (..._C) tem info/tip/desc...
    // no nome. Containers sem _C (ex.: BS_RoleInfo, que e a lista de clas) nao contam.
    private static string LugarDaPista(string pista, out bool dica)
    {
        dica = false;
        if (string.IsNullOrEmpty(pista)) return "";
        string p = pista.ToLowerInvariant();
        int esp = p.IndexOf(' ');
        if (esp >= 0) p = p.Substring(esp + 1);
        string[] seg = p.Split(new char[] { '.', ':', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length == 0) return "";
        string widget = seg[seg.Length - 1];
        if (widget == "widgettree" || widget.EndsWith("_c", StringComparison.Ordinal)) widget = "";
        string dono = "";
        for (int i = seg.Length - 1; i >= 0; i--)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(seg[i], "^(.+)_c(_\\d+)?$");
            if (!m.Success) continue;
            string nome = m.Groups[1].Value;
            for (int k = 0; k < palavrasDica.Length; k++)
                if (nome.IndexOf(palavrasDica[k], StringComparison.Ordinal) >= 0) dica = true;
            if (dono.Length > 0) continue;
            string[] prefixos = new string[] { "wbp_", "wb_", "w_", "ui_", "umg_", "bp_" };
            for (int k = 0; k < prefixos.Length; k++)
                if (nome.StartsWith(prefixos[k], StringComparison.Ordinal)) { nome = nome.Substring(prefixos[k].Length); break; }
            dono = nome.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        }
        if (dono.Length == 0) return widget;
        return widget.Length == 0 ? dono : dono + " / " + widget;
    }

    public static int Vezes(string lugar)
    {
        int n;
        return lugar != null && vezes.TryGetValue(lugar, out n) ? n : 0;
    }

    public static List<string> TodosLugares()
    {
        return new List<string>(porLugar.Keys);
    }

    // marcar/desmarcar tela apertada (menu). Marcada: a IA abrevia os textos dela.
    public static void MarcarTela(string lugar, bool apertada)
    {
        if (apertada) LiveConfig.TelasCurtas.Add(lugar);
        else LiveConfig.TelasCurtas.Remove(lugar);
        LiveConfig.Marcar();
        Reenviar = true;
        if (apertada) Abreviador.Tela(lugar);
    }

    // abreviacao ou tela apertada mudou: manda K + todas as abreviacoes de novo (a ponte troca so o que mudou)
    public static bool Reenviar;

    public static List<string> Lugares(string texto)
    {
        List<string> l;
        return texto != null && lugares.TryGetValue(texto, out l) ? l : new List<string>();
    }

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
        Abreviador.Tick();

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
        if (Reenviar)
        {
            Reenviar = false;
            saida.Append("K\n");
            foreach (string b in brutos) Curtas(b);
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
                string pista = p.Length > 2 ? Desescapar(p[2]) : "";
                bool dica;
                string lp = LugarDaPista(pista, out dica);
                if (dica && lp.Length > 0) dicas.Add(lp);
                Responder(Desescapar(p[1]), pista);
                break;
            case "L":
                if (p.Length < 3) return;
                Lugar(Desescapar(p[1]), Desescapar(p[2]));
                break;
            case "Q":
                int q;
                if (p.Length < 3 || !int.TryParse(p[2], out q)) return;
                string lq = Desescapar(p[1]);
                if (q > Vezes(lq)) vezes[lq] = q;
                break;
            case "M":
                if (MenuPedido != null) MenuPedido();
                break;
            case "I":
                NovaSessao();
                break;
        }
    }

    private static void Lugar(string texto, string lugar)
    {
        if (lugar.Length == 0) return;
        texto = Miolo(texto);
        List<string> l;
        if (!lugares.TryGetValue(texto, out l))
        {
            if (lugares.Count >= MaxEsperando) return;
            l = new List<string>();
            lugares[texto] = l;
        }
        if (l.Count < MaxLugares && !l.Contains(lugar)) l.Add(lugar);
        List<string> t;
        if (!porLugar.TryGetValue(lugar, out t))
        {
            if (porLugar.Count >= 2000) return;
            t = new List<string>();
            porLugar[lugar] = t;
        }
        if (t.Count < MaxPorLugar && !t.Contains(texto)) t.Add(texto);
        if (LiveConfig.TelasCurtas.Contains(lugar)) Abreviador.Pedir(texto);
    }

    // traducao de um termo mudou (jogador editou, escolheu sugestao...): manda de novo para os textos da tela
    // que sao esse termo (com ou sem marcas de cor); a ponte troca na hora
    public static void AtualizarTermo(string original, string nova)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(nova)) return;
        foreach (string b in brutos)
        {
            string antes, depois;
            if (Miolo(b, out antes, out depois).Trim() != original) continue;
            string r = antes + nova + depois;
            respondidos[Miolo(b)] = nova;
            saida.Append("R\t").Append(Escapar(b)).Append('\t').Append(Escapar(r)).Append('\n');
        }
    }

    // abreviacoes do texto para as telas apertadas
    private static void Curtas(string texto)
    {
        if (LiveConfig.TelasCurtas.Count == 0) return;
        string antes, depois;
        string m = Miolo(texto, out antes, out depois);
        TermoGlossario g = Glossario.Obter(m.Trim());
        if (g == null || string.IsNullOrEmpty(g.Curta)) return;
        string c = antes + g.Curta + depois;
        foreach (string lugar in LiveConfig.TelasCurtas)
            saida.Append("C\t").Append(Escapar(lugar)).Append('\t').Append(Escapar(texto)).Append('\t').Append(Escapar(c)).Append('\n');
    }

    private static void NovaSessao()
    {
        esperando.Clear();
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
            Curtas(texto);
            if (respondidos.Count > 20000) { respondidos.Clear(); brutos.Clear(); }
            respondidos[Miolo(texto)] = Miolo(r);
            brutos.Add(texto);
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
