using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed class Provedor
{
    public string Id;
    public string Nome;
    public int Tipo;            // 0 = Google Tradutor (sem chave) | 1 = compativel com OpenAI | 2 = Claude (Anthropic)
    public string Url;
    public string Modelo;
    public bool PrecisaChave;
    public string Dica;

    public Provedor(string id, string nome, int tipo, string url, string modelo, bool chave, string dica)
    {
        Id = id; Nome = nome; Tipo = tipo; Url = url; Modelo = modelo; PrecisaChave = chave; Dica = dica;
    }
}

internal static class LiveConfig
{
    public const string IdGoogle = "google";

    public static readonly Provedor[] Provedores = new Provedor[]
    {
        new Provedor("google", "Google Tradutor (gratis)", 0, "", "", false,
            "Gratis e sem chave. Qualidade media. Endpoint nao oficial: pode ficar lento ou parar."),
        new Provedor("gemini", "Google Gemini", 1, "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.6-flash", true,
            "Chave GRATIS em aistudio.google.com (tem limite por minuto e por dia)."),
        new Provedor("deepseek", "DeepSeek", 1, "https://api.deepseek.com", "deepseek-flash", true,
            "Barato e bom para traducao. Chave em platform.deepseek.com."),
        new Provedor("groq", "Groq", 1, "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", true,
            "Tem plano gratis (console.groq.com). Muito rapido."),
        new Provedor("openrouter", "OpenRouter", 1, "https://openrouter.ai/api/v1", "deepseek/deepseek-v4-flash", true,
            "Um site, varios modelos. Existem modelos gratis (nome termina em :free). Troque o campo Modelo."),
        new Provedor("openai", "OpenAI", 1, "https://api.openai.com/v1", "gpt-5-mini", true,
            "Chave em platform.openai.com (pago)."),
        new Provedor("claude", "Claude (Anthropic)", 2, "https://api.anthropic.com/v1/messages", "claude-haiku-5-5", true,
            "Chave em console.anthropic.com (pago)."),
        new Provedor("ollama", "Ollama (no seu PC)", 1, "http://localhost:11434/v1", "qwen2.5:7b", false,
            "Modelo rodando no seu proprio PC: gratis e sem internet. Precisa do Ollama aberto."),
        new Provedor("custom", "Personalizado (compativel OpenAI)", 1, "", "", false,
            "Qualquer servico no formato OpenAI. Preencha URL, chave (se precisar) e modelo.")
    };

    // codigo, nome no proprio idioma (so para o menu), nome em ingles (usado no pedido para a IA)
    public static readonly string[][] Idiomas = new string[][]
    {
        new string[] { "pt", "Português (BR)", "Brazilian Portuguese" },
        new string[] { "en", "English", "English" },
        new string[] { "es", "Español", "Spanish" },
        new string[] { "fr", "Français", "French" },
        new string[] { "de", "Deutsch", "German" },
        new string[] { "it", "Italiano", "Italian" },
        new string[] { "ru", "Русский", "Russian" },
        new string[] { "uk", "Українська", "Ukrainian" },
        new string[] { "pl", "Polski", "Polish" },
        new string[] { "tr", "Türkçe", "Turkish" },
        new string[] { "nl", "Nederlands", "Dutch" },
        new string[] { "id", "Indonesia", "Indonesian" },
        new string[] { "vi", "Tiếng Việt", "Vietnamese" },
        new string[] { "cs", "Čeština", "Czech" },
        new string[] { "ro", "Română", "Romanian" },
        new string[] { "ja", "Japanese", "Japanese" },
        new string[] { "ko", "Korean", "Korean" },
        new string[] { "zh-CN", "Chinese (simpl.)", "Simplified Chinese" }
    };

    // Estilos de traducao (so valem para IA; o Google Tradutor nao aceita instrucoes).
    // nome, instrucao (em ingles, enviada junto com as regras fixas)
    public static readonly string[][] Estilos = new string[][]
    {
        new string[] { "Padrao (fiel e curto)", "Translate faithfully but keep each text short and clear, because UI space is limited. Use natural wording for the target language." },
        new string[] { "Natural / conversado", "Translate in a natural, fluent, conversational way, like a professional game localization. Prefer idiomatic phrasing over word-for-word translation." },
        new string[] { "Literal", "Translate as literally and accurately as possible, staying close to the source wording. Do not paraphrase." },
        new string[] { "Bem curto (botoes)", "Make every translation as short as possible (abbreviate if needed) so it fits small buttons and labels, without losing the meaning." }
    };

    public static bool Ativo = false;     // sempre abre pausado: o jogador aperta "Comecar traducao" no menu
    public static int Estilo = 0;
    public static string PromptExtra = "";
    // o jogo, do jeito que vai no pedido para a IA (em ingles)
    public const string JogoPadrao = "a Chinese xianxia (cultivation) action RPG called 'Sanctum: Whispers of Qingcheng', set in ancient China around Mount Qingcheng";
    public static string Jogo = JogoPadrao;
    public static string ProvedorId = IdGoogle;
    public static string UltimoProvedorComChave = "gemini";
    public static string Idioma = "pt";   // padrao na primeira vez (sem config.txt): portugues
    // o que traduzir (por tipo de letra): 1 chines, 2 japones, 4 coreano, 8 cirilico, 16 latino, 32 outros. 63 = tudo
    public const int FontesTodas = 63;
    public static int Fontes = FontesTodas;
    public static int Rpm = 60;         // pedidos por minuto
    public static int Lote = 8;         // textos por pedido (so IA)
    public static bool BotaoVisivel = true;
    public static bool Contexto = true;     // manda as ultimas frases como contexto para a IA (so modo com chave)
    // tamanho minimo da fonte quando o texto nao cabe, em % do original (EncaixeFonte)
    public static int FonteMin = 65;
    // fonte dos textos traduzidos: caminho do .ttf/.otf, "" = a do jogo (FonteEscolhida)
    public static string FonteTrad = "";
    // false = pede para a IA pensar o minimo (mais rapido); true = deixa o modelo pensar (pode traduzir melhor)
    public static bool Raciocinio = false;
    // glossario inteligente (so modo com chave): a IA tambem devolve os termos que achou dentro das frases,
    // e a traducao que ignorou um termo travado/confirmado do glossario e refeita uma vez
    public static bool ExtrairTermos = true;
    public static bool VerificarGlossario = true;
    // tamanho da janela do menu, em % (aba Interface)
    public static int Escala = 100;

    public static readonly Dictionary<string, string> Chaves = new Dictionary<string, string>();
    public static readonly Dictionary<string, string> Urls = new Dictionary<string, string>();
    public static readonly Dictionary<string, string> Modelos = new Dictionary<string, string>();

    private static bool sujo;

    // pasta "dados" ao lado da pasta do cerebro (ue4ss\Mods\TradutorUE5\dados): cache, glossario, config e log
    public static string Pasta
    {
        get
        {
            string p = Path.Combine(Ponte.PastaMod, "dados");
            try { if (!Directory.Exists(p)) Directory.CreateDirectory(p); } catch { }
            return p;
        }
    }

    public static string Arquivo { get { return Path.Combine(Pasta, "config.txt"); } }

    public static Provedor Atual
    {
        get { return Achar(ProvedorId); }
    }

    public static Provedor Achar(string id)
    {
        for (int i = 0; i < Provedores.Length; i++)
            if (Provedores[i].Id == id) return Provedores[i];
        return Provedores[0];
    }

    public static string Chave(string id)
    {
        string v;
        return Chaves.TryGetValue(id, out v) ? v : "";
    }

    public static string Url(string id)
    {
        string v;
        if (Urls.TryGetValue(id, out v)) return v;
        return Achar(id).Url;
    }

    public static string Modelo(string id)
    {
        string v;
        if (Modelos.TryGetValue(id, out v)) return v;
        return Achar(id).Modelo;
    }

    public static string NomeIdiomaIngles()
    {
        for (int i = 0; i < Idiomas.Length; i++)
            if (Idiomas[i][0] == Idioma) return Idiomas[i][2];
        return Idioma;
    }

    public static void Marcar() { sujo = true; }

    public static void SalvarSeSujo()
    {
        if (sujo) Salvar();
    }

    public static void Carregar()
    {
        string arq = Arquivo;
        if (!File.Exists(arq)) return;
        string[] linhas = File.ReadAllLines(arq, Encoding.UTF8);
        for (int i = 0; i < linhas.Length; i++)
        {
            string l = linhas[i];
            int sep = l.IndexOf('=');
            if (sep <= 0) continue;
            string k = l.Substring(0, sep).Trim();
            string v = l.Substring(sep + 1).Trim();
            int n;
            if (k == "ativo") { }   // ignorado de proposito: o mod sempre abre pausado
            else if (k == "provedor") ProvedorId = v;
            else if (k == "ultimo_com_chave") UltimoProvedorComChave = v;
            else if (k == "idioma") Idioma = v;
            else if (k == "fonte") { if (int.TryParse(v, out n)) Fontes = n == 1 ? 7 : FontesTodas; }   // formato antigo
            else if (k == "fontes") { if (int.TryParse(v, out n)) Fontes = n & FontesTodas; }
            else if (k == "rpm") { if (int.TryParse(v, out n)) Rpm = Mathf.Clamp(n, 1, 600); }
            else if (k == "lote") { if (int.TryParse(v, out n)) Lote = Mathf.Clamp(n, 1, 30); }
            else if (k == "botao") BotaoVisivel = v != "0";
            else if (k == "contexto") Contexto = v != "0";
            else if (k == "fonte_min") { if (int.TryParse(v, out n)) FonteMin = Mathf.Clamp(n, 50, 90); }
            else if (k == "fonte_trad") FonteTrad = v;
            else if (k == "raciocinio") Raciocinio = v == "1";
            else if (k == "extrair_termos") ExtrairTermos = v != "0";
            else if (k == "verificar_glossario") VerificarGlossario = v != "0";
            else if (k == "escala") { if (int.TryParse(v, out n)) Escala = Mathf.Clamp(n, 80, 160); }
            else if (k == "menu_idioma") { if (int.TryParse(v, out n)) LiveTxt.Atual = n; }
            else if (k == "estilo") { if (int.TryParse(v, out n)) Estilo = Mathf.Clamp(n, 0, Estilos.Length - 1); }
            else if (k == "prompt") PromptExtra = v.Replace("\\n", "\n");
            else if (k == "jogo") { if (v.Length > 0) Jogo = v; }
            else if (k.StartsWith("chave.")) Chaves[k.Substring(6)] = v;
            else if (k.StartsWith("url.")) Urls[k.Substring(4)] = v;
            else if (k.StartsWith("modelo.")) Modelos[k.Substring(7)] = v;
        }
        LiveLog.Info("lg_config", ProvedorId, Idioma);
    }

    public static void Salvar()
    {
        sujo = false;
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ativo=" + (Ativo ? "1" : "0"));
            sb.AppendLine("provedor=" + ProvedorId);
            sb.AppendLine("ultimo_com_chave=" + UltimoProvedorComChave);
            sb.AppendLine("idioma=" + Idioma);
            sb.AppendLine("fontes=" + Fontes);
            sb.AppendLine("rpm=" + Rpm);
            sb.AppendLine("lote=" + Lote);
            sb.AppendLine("botao=" + (BotaoVisivel ? "1" : "0"));
            sb.AppendLine("contexto=" + (Contexto ? "1" : "0"));
            sb.AppendLine("fonte_min=" + FonteMin);
            sb.AppendLine("fonte_trad=" + FonteTrad);
            sb.AppendLine("raciocinio=" + (Raciocinio ? "1" : "0"));
            sb.AppendLine("extrair_termos=" + (ExtrairTermos ? "1" : "0"));
            sb.AppendLine("verificar_glossario=" + (VerificarGlossario ? "1" : "0"));
            sb.AppendLine("escala=" + Escala);
            sb.AppendLine("menu_idioma=" + LiveTxt.Atual);
            sb.AppendLine("estilo=" + Estilo);
            sb.AppendLine("jogo=" + (Jogo ?? "").Replace("\r", " ").Replace("\n", " "));
            sb.AppendLine("prompt=" + (PromptExtra ?? "").Replace("\r", "").Replace("\n", "\\n"));
            foreach (KeyValuePair<string, string> kv in Chaves) sb.AppendLine("chave." + kv.Key + "=" + kv.Value);
            foreach (KeyValuePair<string, string> kv in Urls) sb.AppendLine("url." + kv.Key + "=" + kv.Value);
            foreach (KeyValuePair<string, string> kv in Modelos) sb.AppendLine("modelo." + kv.Key + "=" + kv.Value);
            File.WriteAllText(Arquivo, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_gravar", e, LiveLog.T("it_config"));
        }
    }
}
