using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

// Monta o pedido HTTP de cada tipo de provedor e le a resposta.
internal static class Provedores
{
    public static bool Configurado(Provedor p, out string motivo)
    {
        motivo = null;
        if (p.Tipo == 0) return true;
        if (string.IsNullOrEmpty(LiveConfig.Url(p.Id))) { motivo = LiveTxt.T("m_url"); return false; }
        if (string.IsNullOrEmpty(LiveConfig.Modelo(p.Id))) { motivo = LiveTxt.T("m_modelo"); return false; }
        if (p.PrecisaChave && string.IsNullOrEmpty(LiveConfig.Chave(p.Id))) { motivo = LiveTxt.T("m_chave"); return false; }
        return true;
    }

    // ep = qual endereco do Google usar (so vale no modo gratis; ver GoogleEndpoints)
    public static Pedido Montar(Provedor p, List<string> textos, int ep)
    {
        if (p.Tipo == 0) return MontarGoogle(textos, ep);
        if (p.Tipo == 2) return MontarClaude(p, textos);
        return MontarOpenAI(p, textos);
    }

    // devolve null se a resposta nao for utilizavel
    public static List<string> Interpretar(Provedor p, List<string> textos, string corpo, int ep)
    {
        List<string> cats;
        return Interpretar(p, textos, corpo, ep, out cats);
    }

    public static List<string> Interpretar(Provedor p, List<string> textos, string corpo, int ep, out List<string> cats)
    {
        List<string[]> termos;
        return Interpretar(p, textos, corpo, ep, out cats, out termos);
    }

    // cats = categoria que a IA deu para cada texto ("" = frase), ou null se nao veio (Google, formato antigo)
    // termos = termos que a IA achou dentro das frases: { original, traducao, categoria } (null se nao veio)
    public static List<string> Interpretar(Provedor p, List<string> textos, string corpo, int ep, out List<string> cats, out List<string[]> termos)
    {
        cats = null;
        termos = null;
        int esperado = textos.Count;
        try
        {
            if (string.IsNullOrEmpty(corpo)) return null;
            if (p.Tipo == 0)
            {
                List<string> g = InterpretarGoogle(corpo, textos, ep);
                if (g != null) for (int i = 0; i < g.Count; i++) g[i] = Composto(g[i]);
                return g;
            }
            List<string> lista = ListaDeTextos(Conteudo(p, corpo), out cats, out termos);
            if (lista == null || lista.Count != esperado) { cats = null; termos = null; return null; }
            if (cats != null && cats.Count != esperado) cats = null;   // categorias erradas nao estragam a traducao
            for (int i = 0; i < lista.Count; i++) lista[i] = Composto(lista[i]);
            return lista;
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_parte", e, "read response");
            return null;
        }
    }

    // Acento "solto" (a + til separado, Unicode decomposto) vira uma letra so (a com til). A fonte do jogo nao tem
    // o til sozinho: ele vinha da fonte reserva, desenhado fora do lugar (riscos e pontos sobre as letras).
    public static string Composto(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        try { return s.IsNormalized(NormalizationForm.FormC) ? s : s.Normalize(NormalizationForm.FormC); }
        catch { return s; }
    }

    // texto da resposta da IA (formato OpenAI ou Claude)
    private static string Conteudo(Provedor p, string corpo)
    {
        object raiz = Json.Parse(corpo);
        if (p.Tipo == 2) return Json.Obter(raiz, "content", 0, "text") as string;
        return Json.Obter(raiz, "choices", 0, "message", "content") as string;
    }

    // ---------------- classificar termos com a IA (botao "Reclassificar com IA") ----------------

    public static Pedido MontarClassificar(Provedor p, List<string> termos)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("You classify short terms (names, items, buttons, titles...) of " + LiveConfig.Jogo + ". ");
        sb.Append("The user sends a JSON array of terms; they may be Chinese, English or mixed. ");
        sb.Append("Some items end with \" || context: ...\" (a sentence where the term appeared): use it only to understand the term, classify the term itself. ");
        sb.Append("For each term pick the best category from the list below. ");
        sb.Append("Reply with ONLY a JSON array of category names copied exactly from the list, with exactly the same number of items and the same order as the input.");
        sb.Append("\nCategories:").Append(Categorias.ListaParaIA(true));
        return MontarIA(p, sb.ToString(), Json.ListaTextos(termos), Mathf.Clamp(256 + termos.Count * 12, 512, 4096));
    }

    // lista de categorias da resposta (null se o formato ou a quantidade nao bater)
    public static List<string> InterpretarLista(Provedor p, string corpo, int esperado)
    {
        try
        {
            if (string.IsNullOrEmpty(corpo)) return null;
            List<string> cats;
            List<string[]> termos;
            List<string> l = ListaDeTextos(Conteudo(p, corpo), out cats, out termos);
            return l != null && l.Count == esperado ? l : null;
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_parte", e, "read response");
            return null;
        }
    }

    // ---------------- Google Tradutor (gratis, sem chave) ----------------

    // Tres enderecos do Google, cada um com limite separado. O motor troca de um para outro quando um da 429.
    //   0 = gtx (translate.googleapis.com)      - textos um por linha, GET
    //   1 = dict-chrome-ex (clients5.google.com) - um "q=" por texto, GET (o da extensao do Chrome)
    //   2 = site (translate.google.com batchexecute) - igual ao XUnity "Google v2", POST
    public const int GoogleEndpoints = 3;
    private static readonly string[] nomesGoogle = new string[] { "gtx", "dict-chrome-ex", "site (batchexecute)" };
    private const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36";
    private static readonly System.Random sorteio = new System.Random();
    private static long fsid;
    private static long reqId;

    public static string NomeGoogle(int ep)
    {
        return ep >= 0 && ep < nomesGoogle.Length ? nomesGoogle[ep] : "?";
    }

    private static Pedido MontarGoogle(List<string> textos, int ep)
    {
        if (ep == 1) return MontarDict(textos);
        if (ep == 2) return MontarSite(textos);
        return MontarGtx(textos);
    }

    private static List<string> InterpretarGoogle(string corpo, List<string> textos, int ep)
    {
        if (ep == 1) return InterpretarDict(corpo, textos);
        if (ep == 2) return SepararLinhas(TextoSite(corpo), textos);
        object raiz = Json.Parse(corpo);
        List<object> segs = Json.Obter(raiz, 0) as List<object>;
        return SepararLinhas(JuntarSegmentos(segs), textos);
    }

    // textos um por linha (cada texto pode ter mais de uma linha)
    private static string JuntarLinhas(List<string> textos)
    {
        StringBuilder junto = new StringBuilder();
        for (int i = 0; i < textos.Count; i++)
        {
            if (i > 0) junto.Append('\n');
            junto.Append(SemCR(textos[i]));
        }
        return junto.ToString();
    }

    private static void Cab(Pedido r, string nome, string valor)
    {
        r.Cab(nome, valor);
    }

    // ---- 0: gtx ----
    private static Pedido MontarGtx(List<string> textos)
    {
        string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl="
            + Json.UrlEsc(LiveConfig.Idioma) + "&q=" + Json.UrlEsc(JuntarLinhas(textos));
        Pedido r = new Pedido(url, "GET");
        Cab(r, "User-Agent", UA);
        r.Timeout = 30;
        return r;
    }

    // ---- 1: dict-chrome-ex ----
    private static Pedido MontarDict(List<string> textos)
    {
        StringBuilder url = new StringBuilder("https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl=");
        url.Append(Json.UrlEsc(LiveConfig.Idioma));
        for (int i = 0; i < textos.Count; i++) url.Append("&q=").Append(Json.UrlEsc(SemCR(textos[i])));
        Pedido r = new Pedido(url.ToString(), "GET");
        Cab(r, "User-Agent", UA);
        r.Timeout = 30;
        return r;
    }

    // Resposta: uma lista com um item por texto. Cada item e "traducao" ou ["traducao","idioma de origem"].
    // Com um texto so, as vezes vem ["traducao","idioma"] direto.
    private static List<string> InterpretarDict(string corpo, List<string> textos)
    {
        List<object> l = Json.Parse(corpo) as List<object>;
        if (l == null || l.Count == 0) return null;
        List<string> r = new List<string>();
        if (textos.Count == 1)
        {
            string u = ItemDict(l[0]);
            if (u == null || u.Trim().Length == 0) return null;
            r.Add(u.Trim());
            return r;
        }
        if (l.Count != textos.Count) return null;
        for (int i = 0; i < l.Count; i++)
        {
            string t = ItemDict(l[i]);
            if (t == null || t.Trim().Length == 0) return null;
            r.Add(t.Trim());
        }
        return r;
    }

    private static string ItemDict(object item)
    {
        string s = item as string;
        if (s != null) return s;
        return Json.Obter(item, 0) as string;
    }

    // ---- 2: site (batchexecute), copiado do XUnity GoogleTranslateEndpointV2 ----
    private static Pedido MontarSite(List<string> textos)
    {
        if (fsid == 0)
        {
            fsid = ((long)sorteio.Next(100000000, 999999999) * 1000000000L) + sorteio.Next(0, 999999999);
            reqId = sorteio.Next(1000, 99999);
        }
        string interno = "[[" + Json.Esc(JuntarLinhas(textos)) + ",\"auto\"," + Json.Esc(LiveConfig.Idioma) + ",true],[null]]";
        string freq = "[[[\"MkEWBc\"," + Json.Esc(interno) + ",null,\"generic\"]]]";
        string url = "https://translate.google.com/_/TranslateWebserverUi/data/batchexecute?rpcids=MkEWBc"
            + "&f.sid=" + fsid
            + "&bl=" + Json.UrlEsc("boq_translate-webserver_20210323.10_p0")
            + "&hl=en-US&soc-app=1&soc-platform=1&soc-device=1"
            + "&_reqid=" + reqId
            + "&rt=c";
        reqId += 100000;
        Pedido r = new Pedido(url, "POST");
        r.Corpo = Encoding.UTF8.GetBytes("f.req=" + Json.UrlEsc(freq) + "&");
        Cab(r, "Content-Type", "application/x-www-form-urlencoded;charset=UTF-8");
        Cab(r, "User-Agent", UA);
        Cab(r, "Accept", "*/*");
        Cab(r, "X-Same-Domain", "1");
        Cab(r, "Referer", "https://translate.google.com/");
        Cab(r, "Origin", "https://translate.google.com");
        r.Timeout = 30;
        return r;
    }

    // A resposta comeca com )]}' e um numero; o primeiro bloco [["wrb.fr","MkEWBc","<json em texto>",...]] tem a traducao.
    private static string TextoSite(string corpo)
    {
        int ini = corpo.IndexOf("[[\"wrb.fr\"", StringComparison.Ordinal);
        if (ini < 0) return null;
        object bloco = Json.Parse(corpo.Substring(ini)); // o leitor para no fim do primeiro bloco
        string interno = Json.Obter(bloco, 0, 2) as string;
        if (string.IsNullOrEmpty(interno)) return null;
        object arr = Json.Parse(interno);
        List<object> segs = Json.Obter(arr, 1, 0, 0, 5) as List<object>;
        if (segs == null) return Json.Obter(arr, 1, 0, 0, 0) as string;
        return JuntarSegmentos(segs);
    }

    // ---- comum aos enderecos que devolvem pedacos (frases) ----

    // Junta os pedacos. Se nenhum dos dois lados tiver espaco/quebra na emenda, poe uma quebra de linha
    // (como o XUnity). O endereco "site" manda a quebra no COMECO do pedaco seguinte ("\nNo dependencies"),
    // entao olhar so o fim do anterior criava uma linha em branco a mais e o lote sempre falhava.
    private static string JuntarSegmentos(List<object> segs)
    {
        if (segs == null) return null;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < segs.Count; i++)
        {
            string parte = Json.Obter(segs[i], 0) as string;
            if (string.IsNullOrEmpty(parte)) continue;
            if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && !char.IsWhiteSpace(parte[0])) sb.Append('\n');
            sb.Append(parte);
        }
        return sb.ToString();
    }

    private static string SemCR(string s)
    {
        return s.IndexOf('\r') < 0 ? s : s.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    // Separa por linha: cada texto original leva tantas linhas quantas tinha.
    // Se a conta nao bater, devolve null (o motor entao tenta de novo um texto por vez).
    private static List<string> SepararLinhas(string tudo, List<string> textos)
    {
        if (string.IsNullOrEmpty(tudo)) return null;
        List<string> r = new List<string>();
        if (textos.Count <= 1)
        {
            r.Add(tudo.Trim());
            return r;
        }
        string[] linhas = SemCR(tudo).TrimEnd('\n').Split('\n');
        int atual = 0;
        for (int i = 0; i < textos.Count; i++)
        {
            int n = SemCR(textos[i]).Split('\n').Length;
            if (atual + n > linhas.Length) return null;
            StringBuilder t = new StringBuilder();
            for (int k = 0; k < n; k++)
            {
                if (k > 0) t.Append('\n');
                t.Append(linhas[atual + k].Trim());
            }
            atual += n;
            string s = t.ToString();
            if (s.Trim().Length == 0) return null;
            r.Add(s);
        }
        if (atual != linhas.Length) return null;
        return r;
    }

    // ---------------- IA no formato OpenAI (DeepSeek, Gemini, Groq, OpenRouter, Ollama...) ----------------

    private static Pedido MontarOpenAI(Provedor p, List<string> textos)
    {
        return MontarIA(p, PromptSistema(textos), Json.ListaTextos(textos), MaxTokens(textos));
    }

    private static Pedido MontarClaude(Provedor p, List<string> textos)
    {
        return MontarIA(p, PromptSistema(textos), Json.ListaTextos(textos), MaxTokens(textos));
    }

    // pedido para a IA (formato OpenAI ou Claude) com o prompt de sistema e a mensagem do usuario prontos.
    // maxTokens so vai no Claude (la e obrigatorio).
    private static Pedido MontarIA(Provedor p, string sistema, string usuario, int maxTokens)
    {
        if (p.Tipo == 2) return MontarClaudeIA(p, sistema, usuario, maxTokens);
        string url = LiveConfig.Url(p.Id).Trim().TrimEnd('/');
        if (!url.EndsWith("/chat/completions")) url += "/chat/completions";

        string modelo = LiveConfig.Modelo(p.Id).Trim();
        string json = "{\"model\":" + Json.Esc(modelo) + (AceitaTemperatura(modelo) ? ",\"temperature\":0.2" : "")
            + RaciocinioMinimo(p, modelo) + ",\"messages\":["
            + "{\"role\":\"system\",\"content\":" + Json.Esc(sistema) + "},"
            + "{\"role\":\"user\",\"content\":" + Json.Esc(usuario) + "}]}";
        Pedido r = Post(url, json);
        string chave = LiveConfig.Chave(p.Id).Trim();
        if (chave.Length > 0) r.Cab("Authorization", "Bearer " + chave);
        return r;
    }

    // Modelos "que pensam" da OpenAI (gpt-5..., o1, o3, o4...) so aceitam a temperatura padrao:
    // mandar 0.2 da erro 400. Para eles o campo nao vai no pedido.
    private static bool AceitaTemperatura(string modelo)
    {
        string m = (modelo ?? "").ToLowerInvariant();
        int barra = m.LastIndexOf('/');
        if (barra >= 0) m = m.Substring(barra + 1);   // ex.: "openai/gpt-5-mini" no OpenRouter
        if (m.StartsWith("gpt-5", StringComparison.Ordinal)) return false;
        if (m.Length >= 2 && m[0] == 'o' && char.IsDigit(m[1])) return false;
        return true;
    }

    // Raciocinio minimo: os modelos que "pensam" antes de responder (gpt-5, o1/o3, Gemini, gpt-oss) gastam
    // segundos pensando, e para traduzir isso quase nao muda a qualidade. Se o servico recusar o campo (erro 400
    // falando de "reasoning"), o motor chama DesligarRaciocinio e o campo deixa de ir para esse provedor.
    private static readonly HashSet<string> semRaciocinio = new HashSet<string>();

    public static void LimparRaciocinio()
    {
        semRaciocinio.Clear();
    }

    // erro 400 que fala do campo de raciocinio ("reasoning_effort" ou "thinking")
    public static bool ErroRaciocinio(int codigo, string corpo)
    {
        if (codigo != 400 || corpo == null) return false;
        return corpo.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) >= 0
            || corpo.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool DesligarRaciocinio(Provedor p)
    {
        if (string.IsNullOrEmpty(RaciocinioMinimo(p, LiveConfig.Modelo(p.Id).Trim()))) return false;
        semRaciocinio.Add(p.Id);
        LiveLog.Aviso("lg_sem_raciocinio", p.Nome);
        return true;
    }

    private static string RaciocinioMinimo(Provedor p, string modelo)
    {
        // opcao "IA pensa antes de traduzir" ligada: o modelo usa o raciocinio padrao dele
        if (LiveConfig.Raciocinio || semRaciocinio.Contains(p.Id)) return "";
        string m = (modelo ?? "").ToLowerInvariant();
        // DeepSeek V4 (deepseek-flash...) pensa por padrao; o site oficial desliga com "thinking"
        if (p.Id == "deepseek" || LiveConfig.Url(p.Id).IndexOf("api.deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0)
            return ",\"thinking\":{\"type\":\"disabled\"}";
        int barra = m.LastIndexOf('/');
        if (barra >= 0) m = m.Substring(barra + 1);
        string nivel = null;
        if (m.StartsWith("gpt-5.", StringComparison.Ordinal)) nivel = "none";           // gpt-5.1 em diante
        else if (m.StartsWith("gpt-5", StringComparison.Ordinal)) nivel = "minimal";    // gpt-5, gpt-5-mini, gpt-5-nano
        else if (m.Length >= 2 && m[0] == 'o' && char.IsDigit(m[1])) nivel = "low";    // o1, o3, o4-mini...
        else if (m.StartsWith("gemini-2.5-flash", StringComparison.Ordinal)) nivel = "none";
        else if (m.StartsWith("gemini-", StringComparison.Ordinal)) nivel = "low";     // Gemini 3 nao desliga, so diminui
        else if (m.StartsWith("gpt-oss", StringComparison.Ordinal)) nivel = "low";
        if (nivel == null) return "";
        return ",\"reasoning_effort\":\"" + nivel + "\"";
    }

    // ---------------- Claude (Anthropic) ----------------

    private static Pedido MontarClaudeIA(Provedor p, string sistema, string usuario, int maxTokens)
    {
        string url = LiveConfig.Url(p.Id).Trim();
        string json = "{\"model\":" + Json.Esc(LiveConfig.Modelo(p.Id).Trim()) + ",\"max_tokens\":" + maxTokens + ",\"temperature\":0.2,"
            + "\"system\":" + Json.Esc(sistema) + ",\"messages\":["
            + "{\"role\":\"user\",\"content\":" + Json.Esc(usuario) + "}]}";
        Pedido r = Post(url, json);
        r.Cab("x-api-key", LiveConfig.Chave(p.Id).Trim());
        r.Cab("anthropic-version", "2023-06-01");
        return r;
    }

    // Limite de resposta pelo tamanho do lote: folga para idiomas que gastam mais tokens,
    // sem pedir demais num lote pequeno. Resposta cortada no meio faz o lote inteiro falhar.
    private static int MaxTokens(List<string> textos)
    {
        int chars = 0;
        for (int i = 0; i < textos.Count; i++) chars += textos[i].Length;
        return Mathf.Clamp(1024 + chars * 3, 1024, 8192);
    }

    // ---------------- comum ----------------

    private static Pedido Post(string url, string json)
    {
        Pedido r = new Pedido(url, "POST");
        r.Corpo = Encoding.UTF8.GetBytes(json);
        r.TipoCorpo = "application/json";
        r.Timeout = 60;
        return r;
    }

    // Regras fixas (garantem o formato da resposta) + estilo escolhido pelo jogador
    // + termos do glossario que aparecem nos textos + textos recentes como contexto.
    public static string PromptSistema(List<string> textos)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("You are a professional game localizer. You translate UI text, item names, quests and dialogue of " + LiveConfig.Jogo + ". ");
        sb.Append("The user sends a JSON array of strings. Translate EVERY string into ").Append(LiveConfig.NomeIdiomaIngles()).Append(". ");
        sb.Append("The source text may be Chinese, English or a mix of both. ");
        sb.Append("Hard rules: keep placeholders such as {0}, {1}, {2} and all markup tags EXACTLY unchanged and in the text; ");
        sb.Append("never add notes or explanations; if a string is already in the target language, return it unchanged. ");
        sb.Append("The input has exactly ").Append(textos.Count).Append(textos.Count == 1 ? " string" : " strings")
          .Append(". Each string is ONE item even when it contains line breaks (\\n): keep its line breaks inside that item, ")
          .Append("and never merge two strings into one or split one into two. ");
        // lote com termo curto (vai entrar no glossario): a IA tambem diz a categoria de cada um, e ja traduz
        // seguindo a instrucao dessa categoria. Lote com frase (e "extrair termos" ligado): a IA tambem devolve
        // os nomes proprios e termos do jogo que achou dentro das frases (alimenta o glossario).
        // Nenhum dos dois: formato simples (gasta menos).
        bool comCategoria = TemTermo(textos);
        bool comTermos = LiveConfig.ExtrairTermos && TemFrase(textos);
        if (comCategoria || comTermos)
        {
            sb.Append("Reply with ONLY a JSON object {\"t\": [...]");
            if (comCategoria) sb.Append(", \"c\": [...]");
            if (comTermos) sb.Append(", \"g\": [...]");
            sb.Append("}. \"t\" = the translations, with exactly the same number of items and the same order as the input. ");
            if (comCategoria)
            {
                sb.Append("\"c\" = for each input string, in the same order: if it is a short term (a name, place, sect, realm, technique, pill, artifact, item, stat, menu button, title...), ");
                sb.Append("the name of its category copied exactly from the category list below; if it is a sentence or longer text, \"\". ");
            }
            if (comTermos)
            {
                sb.Append("\"g\" = game-specific proper terms that appear INSIDE the longer strings (names of people, places, sects, cultivation realms, techniques, pills, artifacts, special items), ");
                sb.Append("each as [term copied exactly from the input, its translation exactly as you wrote it in \"t\", category name from the list below]. ");
                sb.Append("At most 5 per string; skip common words and whole sentences; [] if there are none.");
            }
        }
        else
            sb.Append("Reply with ONLY a JSON array of strings, with exactly the same number of items and the same order as the input.");

        int e = LiveConfig.Estilo;
        if (e < 0 || e >= LiveConfig.Estilos.Length) e = 0;
        string estilo = LiveConfig.Estilos[e][1];
        if (estilo.Length > 0) sb.Append("\nStyle: ").Append(estilo);
        string extra = LiveConfig.PromptExtra;
        if (extra != null && extra.Trim().Length > 0)
            sb.Append("\nExtra instructions from the player (follow them, but the hard rules above always win): ").Append(extra.Trim());
        if (comCategoria || comTermos)
            sb.Append("\nCategories (for \"c\" and \"g\"). Translate each short term following its category's instruction; the hard rules above always win:")
              .Append(Categorias.ListaParaIA(false));
        else
        {
            string cats = Categorias.TextoPrompt();
            if (cats.Length > 0) sb.Append("\n").Append(cats);
        }

        // glossario: o mesmo nome traduzido do mesmo jeito tambem dentro de frases
        List<TermoGlossario> termos = Glossario.TermosEm(textos, 40, 1500);
        if (termos.Count > 0)
        {
            sb.Append("\nGlossary: when one of these terms appears inside a string, translate it exactly like this:");
            for (int i = 0; i < termos.Count; i++)
                sb.Append("\n- ").Append(UmaLinha(termos[i].Original)).Append(" => ").Append(UmaLinha(termos[i].Traducao));
            // texto que voltou porque a traducao anterior ignorou o glossario
            if (LiveMotor.Reforcar(textos))
                sb.Append("\nIMPORTANT: a previous translation of this text ignored the glossary. Every glossary term above that appears in the input MUST be translated exactly as listed (it may be inflected for grammar).");
        }

        // contexto: o que apareceu na tela pouco antes (ajuda em dialogos: genero, tratamento, assunto)
        List<string[]> recentes = LiveMotor.Recentes(textos);
        if (recentes.Count > 0)
        {
            sb.Append("\nRecent text shown in the game just before, with its translation. Context only: do NOT translate it again and do NOT include it in your reply:");
            for (int i = 0; i < recentes.Count; i++)
                sb.Append("\n- ").Append(UmaLinha(recentes[i][0])).Append(" => ").Append(UmaLinha(recentes[i][1]));
        }
        return sb.ToString();
    }

    // frase = texto que nao e termo curto, com algum tamanho (onde pode ter nome proprio dentro)
    private static bool TemFrase(List<string> textos)
    {
        if (textos == null) return false;
        for (int i = 0; i < textos.Count; i++)
            if (textos[i].Length >= 6 && !Glossario.Termo(textos[i])) return true;
        return false;
    }

    private static bool TemTermo(List<string> textos)
    {
        if (textos == null) return false;
        for (int i = 0; i < textos.Count; i++)
            if (Glossario.Termo(textos[i])) return true;
        return false;
    }

    private static string UmaLinha(string s)
    {
        return (s ?? "").Replace("\r", " ").Replace("\n", " ");
    }

    // Tira cercas de codigo (```json) e le o primeiro array JSON de strings.
    // Aceita os dois formatos: array de strings, ou objeto {"t": [...], "c": [...], "g": [[o, t, c], ...]}
    // (traducoes + categorias + termos achados nas frases).
    private static List<string> ListaDeTextos(string conteudo, out List<string> cats, out List<string[]> termos)
    {
        cats = null;
        termos = null;
        if (conteudo == null) return null;
        int chave = conteudo.IndexOf('{');
        int colchete = conteudo.IndexOf('[');
        if (chave >= 0 && (colchete < 0 || chave < colchete))
        {
            int fimObj = conteudo.LastIndexOf('}');
            if (fimObj > chave)
            {
                Dictionary<string, object> obj = Json.Parse(conteudo.Substring(chave, fimObj - chave + 1)) as Dictionary<string, object>;
                if (obj != null)
                {
                    List<object> t = Json.Obter(obj, "t") as List<object>;
                    if (t == null) return null;
                    List<string> r1 = new List<string>();
                    for (int i = 0; i < t.Count; i++)
                    {
                        string s = t[i] as string;
                        if (s == null) return null;
                        r1.Add(s);
                    }
                    List<object> c = Json.Obter(obj, "c") as List<object>;
                    if (c != null)
                    {
                        cats = new List<string>();
                        for (int i = 0; i < c.Count; i++) cats.Add((c[i] as string) ?? "");
                    }
                    List<object> g = Json.Obter(obj, "g") as List<object>;
                    if (g != null)
                    {
                        termos = new List<string[]>();
                        for (int i = 0; i < g.Count && termos.Count < 60; i++)
                        {
                            List<object> item = g[i] as List<object>;
                            if (item == null || item.Count < 2) continue;
                            string o = item[0] as string, tr = item[1] as string;
                            if (string.IsNullOrEmpty(o) || string.IsNullOrEmpty(tr)) continue;
                            termos.Add(new string[] { o.Trim(), tr.Trim(), item.Count > 2 ? ((item[2] as string) ?? "") : "" });
                        }
                    }
                    return r1;
                }
            }
        }
        int ini = conteudo.IndexOf('[');
        int fim = conteudo.LastIndexOf(']');
        if (ini < 0 || fim <= ini) return null;
        List<object> a = Json.Parse(conteudo.Substring(ini, fim - ini + 1)) as List<object>;
        if (a == null) return null;
        List<string> r = new List<string>();
        for (int i = 0; i < a.Count; i++)
        {
            string t = a[i] as string;
            if (t == null) return null;
            r.Add(t);
        }
        return r;
    }
}
