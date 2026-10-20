using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Cerebro do mod: decide o que traduzir, guarda a fila, respeita o limite de pedidos por minuto,
// manda para o provedor escolhido e guarda o resultado no cache.
internal static class LiveMotor
{
    // tags <...>, {...}, %s/%d e numeros: ficam fora da traducao (viram {0}, {1}...) e voltam no lugar depois.
    // Assim "Tempo: 12:03" e "Tempo: 12:04" usam a mesma traducao em cache.
    // Numero colado em letra latina (Raz3n, v2, MK3) NAO e protegido: faz parte da palavra.
    private static readonly Regex RxProt = new Regex(@"<[^>]+>|\{[^}]*\}|%[sdf]|(?<![A-Za-z0-9_])\d+(?:[.,:]\d+)*(?![A-Za-z0-9_])", RegexOptions.Compiled);
    private static readonly Regex RxTok = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> memo = new Dictionary<string, string>();
    private static readonly HashSet<string> nossas = new HashSet<string>();
    private static readonly HashSet<string> ignorados = new HashSet<string>();

    private static readonly LinkedList<string> fila = new LinkedList<string>();
    private static readonly HashSet<string> naFila = new HashSet<string>();
    private static readonly HashSet<string> falhou = new HashSet<string>();
    private static readonly HashSet<string> unitarios = new HashSet<string>();
    private static readonly Dictionary<string, int> tentativas = new Dictionary<string, int>();
    // pista de onde o texto apareceu (nome do objeto na tela): ajuda a categorizar no glossario
    private static readonly Dictionary<string, string> pistas = new Dictionary<string, string>();
    // traducao antiga -> traducao nova (quando o jogador edita um termo no glossario, o texto na tela troca na hora)
    private static readonly Dictionary<string, string> substituicoes = new Dictionary<string, string>();
    // ultimas frases traduzidas (original, traducao): vao como contexto no pedido para a IA
    private static readonly List<string[]> recentes = new List<string[]>();
    private const int MaxRecentes = 3;

    // IA: pedidos ao mesmo tempo acompanham os "pedidos por minuto" (60 por minuto = 6 de uma vez), entre 3 e 8.
    // Quem esta num plano gratis de IA e toma 429 baixa os pedidos por minuto e isto baixa junto.
    private static int MaxEmVoo { get { return Mathf.Clamp(LiveConfig.Rpm / 10, 3, 8); } }
    // Google gratis (endpoint nao oficial): um pedido por vez (como o XUnity). Sem intervalo fixo: o proximo
    // sai assim que o anterior volta, so com esta folga minima. Se der 429, o endereco fica de castigo e o
    // motor passa para o proximo endereco (ver Castigar).
    public const float GoogleIntervalo = 0.3f;
    // e cada pedido leva varios textos juntos (um por linha, como o XUnity), em vez de um so
    public const int GoogleLote = 10;
    public const int GoogleMaxChars = 800;      // por pedido (vai na URL, entao nao pode ser grande)
    public const int GoogleMaxTexto = 200;      // texto maior que isso vai sozinho num pedido
    public const float GoogleEspera = 0.4f;     // espera a tela "parar" antes de enviar (junta mais textos no lote)
    public const float GoogleEsperaMax = 1.5f;  // mas nunca espera mais que isso
    private static float ultimaEntrada;
    private static float inicioEspera;
    public static int LotesOk;
    public static int LotesFallback;
    private static int emVoo;
    private static float proximoEnvio;
    private static float pausaAte;
    private static int backoff;
    private static int geracao;
    private static int retryAfter;      // segundos pedidos pelo servidor (cabecalho Retry-After), 0 = nao veio

    public static int Traduzidos;
    public static int Pedidos;
    public static int Erros;
    // O erro guarda a CHAVE do texto (nao o texto pronto): assim ele muda de idioma junto com o menu.
    private static string erroChave = "";
    private static object[] erroArgs = new object[0];
    private static string erroTexto = "";
    public static string UltimoErro
    {
        get { return erroChave.Length > 0 ? LiveTxt.F(erroChave, erroArgs) : erroTexto; }
        set { erroTexto = value ?? ""; erroChave = ""; }
    }
    private static void ErroChave(string chave, params object[] args)
    {
        erroChave = chave;
        erroArgs = args;
        erroTexto = "";
    }
    public static string Teste = "";
    public static bool VarrerAgora;

    public static int NaFila { get { return fila.Count + emVoo; } }
    public static int EmVoo { get { return emVoo; } }

    public static bool Nossa(string s)
    {
        return nossas.Contains(s) && !substituicoes.ContainsKey(s);
    }

    // Glossario editado: onde estiver o texto antigo na tela, passa a valer o novo.
    public static void Substituir(string antiga, string nova)
    {
        if (string.IsNullOrEmpty(antiga) || string.IsNullOrEmpty(nova) || antiga == nova) return;
        List<string> chaves = new List<string>(substituicoes.Keys);
        for (int i = 0; i < chaves.Count; i++)
            if (substituicoes[chaves[i]] == antiga) substituicoes[chaves[i]] = nova;
        substituicoes[antiga] = nova;
        memo.Clear();
        VarrerAgora = true;
    }

    // traducao que esta na tela -> texto original do jogo. Serve para devolver o original quando
    // as memorias sao esvaziadas (limpar cache, trocar idioma...), e o texto ser traduzido de novo.
    private static readonly Dictionary<string, string> originais = new Dictionary<string, string>();
    public static bool RestaurarPendente;

    // Devolve o texto original de uma traducao que o mod colocou na tela (null se nao foi o mod).
    public static string Original(string traducao)
    {
        if (string.IsNullOrEmpty(traducao)) return null;
        string o;
        return originais.TryGetValue(traducao, out o) ? o : null;
    }

    // ---- retraducao dos provisorios (vieram do endereco reserva do Google) ----
    private static readonly HashSet<string> retraduzindo = new HashSet<string>();   // na fila/no ar agora
    private static readonly HashSet<string> atualizados = new HashSet<string>();    // ja retraduzidos, falta trocar na tela
    public static bool AtualizarPendente;
    public static int Retraduzidos;

    // O LiveDriver chama para cada texto na tela: se for uma traducao provisoria que acabou de ser
    // retraduzida, devolve a traducao nova (senao null).
    public static string Retraduzir(string textoTela)
    {
        string o = Original(textoTela);
        if (o == null) return null;
        List<string> itens = new List<string>();
        string tpl = Proteger(o, itens);
        if (!atualizados.Contains(tpl)) return null;
        memo.Remove(o);
        return Traduzir(o, null);
    }

    public static void FimAtualizar()
    {
        AtualizarPendente = false;
        atualizados.Clear();
    }

    // O LiveDriver chama depois de devolver os originais na tela.
    public static void FimRestaurar()
    {
        RestaurarPendente = false;
        originais.Clear();
    }

    public static void ZerarContadores()
    {
        Traduzidos = 0;
        Pedidos = 0;
        Erros = 0;
        LotesOk = 0;
        LotesFallback = 0;
        Retraduzidos = 0;
    }

    // Esvazia as memorias (ao trocar idioma, provedor, estilo...). O cache em disco nao e apagado.
    // O texto ja traduzido na tela volta ao original no proximo quadro (LiveDriver) e e traduzido de novo.
    public static void Reiniciar()
    {
        if (originais.Count > 0) RestaurarPendente = true;
        memo.Clear();
        nossas.Clear();
        ignorados.Clear();
        falhou.Clear();
        unitarios.Clear();
        tentativas.Clear();
        pistas.Clear();
        substituicoes.Clear();
        recentes.Clear();
        fila.Clear();
        naFila.Clear();
        retraduzindo.Clear();
        atualizados.Clear();
        AtualizarPendente = false;
        geracao++;
        UltimoErro = "";
        backoff = 0;
        pausaAte = 0f;
    }

    // Chamado quando o jogador mexe na chave/URL/modelo: tira as pausas e deixa tentar de novo.
    public static void Destravar()
    {
        Provedores.LimparRaciocinio();   // modelo novo: tenta o raciocinio minimo de novo
        pausaAte = 0f;
        backoff = 0;
        falhou.Clear();
        tentativas.Clear();
        UltimoErro = "";
    }

    // Frases recentes para o contexto da IA (sem as que estao no proprio lote). Vazio se o jogador desligou.
    public static List<string[]> Recentes(List<string> lote)
    {
        List<string[]> r = new List<string[]>();
        if (!LiveConfig.Contexto) return r;
        for (int i = 0; i < recentes.Count; i++)
            if (lote == null || !lote.Contains(recentes[i][0])) r.Add(recentes[i]);
        return r;
    }

    // so frases (nao termos curtos), e nao muito longas, para nao gastar token a toa
    private static void LembrarRecente(string original, string traducao)
    {
        if (string.IsNullOrEmpty(traducao) || original.Length < 12 || original.Length > 300) return;
        if (Glossario.Termo(original)) return;
        for (int i = recentes.Count - 1; i >= 0; i--)
            if (recentes[i][0] == original) recentes.RemoveAt(i);
        recentes.Add(new string[] { original, traducao });
        while (recentes.Count > MaxRecentes) recentes.RemoveAt(0);
    }

    // ---------------- o que traduzir ----------------

    // Devolve a traducao se ja existe; senao poe na fila e devolve null (o texto original continua na tela).
    public static string Traduzir(string s)
    {
        return Traduzir(s, null);
    }

    // origem = caminho do widget de texto na tela (so para descobrir a categoria no glossario)
    public static string Traduzir(string s, string origem)
    {
        string r = TraduzirInterno(s, origem);
        if (r != null && r != s)
        {
            if (originais.Count > 30000) originais.Clear();
            originais[r] = s;
        }
        return r;
    }

    private static string TraduzirInterno(string s, string origem)
    {
        if (!LiveConfig.Ativo || string.IsNullOrEmpty(s)) return null;
        string r;
        string sub;
        if (substituicoes.TryGetValue(s, out sub))
        {
            Lembrar(nossas, sub);
            return sub;
        }
        if (memo.TryGetValue(s, out r)) return r;
        if (nossas.Contains(s) || ignorados.Contains(s)) return null;

        List<string> itens = new List<string>();
        string tpl = Proteger(s, itens);
        if (!Elegivel(s, tpl))
        {
            Lembrar(ignorados, s);
            return null;
        }

        string tr;
        if (Glossario.TryGet(tpl, out tr) || TradCache.TryGet(tpl, out tr))
        {
            bool ok;
            string res = Restaurar(tr, itens, out ok);
            if (!ok || res == s)
            {
                Lembrar(ignorados, s);
                return null;
            }
            if (memo.Count > 20000) memo.Clear();
            memo[s] = res;
            Lembrar(nossas, res);
            // veio do cache e ainda nao esta no glossario: entra la (se for um termo curto)
            if (!Glossario.Existe(tpl) && Glossario.Termo(tpl)) Glossario.Auto(tpl, tr, Pista(origem));
            return res;
        }

        if (!falhou.Contains(tpl) && naFila.Add(tpl))
        {
            float agoraFila = Time.unscaledTime;
            if (fila.Count == 0) inicioEspera = agoraFila;
            ultimaEntrada = agoraFila;
            fila.AddLast(tpl);
            if (origem != null && !pistas.ContainsKey(tpl) && Glossario.Termo(tpl)) pistas[tpl] = Pista(origem);
        }
        return null;
    }

    // caminho do widget (ex.: "WBP_Bag_C /Engine/.../WBP_Bag.Txt_ItemName"), em minusculas.
    // A ponte manda o nome completo do UObject do texto.
    private static string Pista(string origem)
    {
        return string.IsNullOrEmpty(origem) ? "" : origem.ToLowerInvariant();
    }

    // texto que o motor decidiu nao traduzir (sem letras, tipo de letra desligado, falhou 3 vezes...)
    public static bool Ignorado(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        if (ignorados.Contains(s)) return true;
        List<string> itens = new List<string>();
        return falhou.Contains(Proteger(s, itens));
    }

    private static void Lembrar(HashSet<string> set, string s)
    {
        if (set.Count > 30000) set.Clear();
        set.Add(s);
    }

    private static bool Elegivel(string original, string tpl)
    {
        if (tpl.Length > 1500) return false;
        if (!TemLetraFora(tpl)) return false;
        // so traduz se o texto tiver algum tipo de letra ligado no menu (tpl: sem tags e numeros)
        if ((Scripts(tpl) & LiveConfig.Fontes) == 0) return false;
        return true;
    }

    // Tipos de letra que o texto tem (bits: 1 chines, 2 japones, 4 coreano, 8 cirilico, 16 latino, 32 outros).
    // Han sem kana conta como chines; com kana, como japones.
    public static int Scripts(string s)
    {
        int r = 0;
        bool han = false, kana = false, hangul = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '\u3040' && c <= '\u30FF') kana = true;
            else if ((c >= '\uAC00' && c <= '\uD7AF') || (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F')) hangul = true;
            else if ((c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF') || (c >= '\uF900' && c <= '\uFAFF')) han = true;
            else if (c >= '\u0400' && c <= '\u052F') r |= 8;
            else if (char.IsLetter(c))
            {
                if (c <= '\u024F' || (c >= '\u1E00' && c <= '\u1EFF')) r |= 16;
                else r |= 32;
            }
        }
        if (kana) r |= 2;
        if (hangul) r |= 4;
        if (han && !kana && !hangul) r |= 1;
        return r;
    }

    // tem alguma letra fora dos marcadores {n}?
    private static bool TemLetraFora(string tpl)
    {
        bool dentro = false;
        for (int i = 0; i < tpl.Length; i++)
        {
            char c = tpl[i];
            if (c == '{') { dentro = true; continue; }
            if (dentro) { if (c == '}') dentro = false; continue; }
            if (char.IsLetter(c)) return true;
        }
        return false;
    }

    public static bool TemCJK(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c >= '぀' && c <= 'ヿ') || (c >= '㐀' && c <= '䶿') ||
                (c >= '一' && c <= '鿿') || (c >= '가' && c <= '힯')) return true;
        }
        return false;
    }

    private static string Proteger(string s, List<string> itens)
    {
        return RxProt.Replace(s, delegate (Match m)
        {
            int i = itens.Count;
            itens.Add(m.Value);
            return "{" + i + "}";
        });
    }

    // ok = false se a traducao perdeu algum marcador (ai e melhor nao usar)
    private static string Restaurar(string t, List<string> itens, out bool ok)
    {
        bool tudo = true;
        HashSet<int> vistos = new HashSet<int>();
        string r = RxTok.Replace(t, delegate (Match m)
        {
            int i = int.Parse(m.Groups[1].Value);
            if (i < 0 || i >= itens.Count) { tudo = false; return m.Value; }
            vistos.Add(i);
            return itens[i];
        });
        if (vistos.Count < itens.Count) tudo = false;
        ok = tudo;
        return r;
    }

    // ---------------- envio ----------------

    public static bool PodeTraduzir(out string motivo)
    {
        return Provedores.Configurado(LiveConfig.Atual, out motivo);
    }

    public static void Tick()
    {
        TradCache.FlushSePreciso(Time.unscaledTime);
        Provedor p = LiveConfig.Atual;
        int limiteVoo = p.Tipo == 0 ? 1 : MaxEmVoo;
        if (!LiveConfig.Ativo || emVoo >= limiteVoo) return;
        // fila vazia: no Google, aproveita para retraduzir o que veio do endereco reserva (provisorio)
        bool reTrad = false;
        if (fila.Count == 0)
        {
            if (p.Tipo != 0 || TradCache.TotalProvisorios == 0) return;
            reTrad = true;
        }
        float agora = Time.unscaledTime;
        if (agora < pausaAte || agora < proximoEnvio) return;

        string motivo;
        if (!Provedores.Configurado(p, out motivo))
        {
            UltimoErro = motivo;
            return;
        }

        // Google: so envia quando o lote encheu, ou a tela ficou 0,4 s sem texto novo, ou ja esperou 1,5 s
        if (p.Tipo == 0 && !reTrad && fila.Count < GoogleLote
            && agora - ultimaEntrada < GoogleEspera && agora - inicioEspera < GoogleEsperaMax) return;

        // Google: escolhe o MELHOR endereco livre (se todos estiverem de castigo, espera o primeiro liberar)
        int ep = 0;
        if (p.Tipo == 0)
        {
            ep = EscolherEndereco(agora);
            if (ep < 0) return;
            // retraduzir so vale a pena com um endereco bom
            if (reTrad && ep == EpReserva) return;
        }

        int max = p.Tipo == 0 ? GoogleLote : Mathf.Max(1, LiveConfig.Lote);
        int maxChars = p.Tipo == 0 ? GoogleMaxChars : 3500;
        List<string> lote = new List<string>();
        int chars = 0;
        if (reTrad)
        {
            lote = TradCache.ProximosProvisorios(GoogleLote, GoogleMaxChars, GoogleMaxTexto, naFila);
            for (int i = 0; i < lote.Count; i++)
            {
                naFila.Add(lote[i]);
                retraduzindo.Add(lote[i]);
            }
        }
        while (!reTrad && fila.Count > 0 && lote.Count < max && chars < maxChars)
        {
            string t = fila.First.Value;
            bool unit = unitarios.Contains(t) || (p.Tipo == 0 && t.Length > GoogleMaxTexto);
            if (lote.Count > 0 && unit) break;
            fila.RemoveFirst();
            lote.Add(t);
            chars += t.Length;
            if (unit) break;
        }
        if (lote.Count == 0) return;

        if (p.Tipo == 0) proximoEnvio = agora + GoogleIntervalo;
        else proximoEnvio = agora + 60f / Mathf.Max(1, LiveConfig.Rpm);
        emVoo++;
        Pedidos++;
        _ = Enviar(p, lote, geracao, ep);
    }

    // ---------------- rodizio dos enderecos do Google ----------------

    public static int GoogleEp;
    // ordem de qualidade: gtx, site (mesma traducao do translate.google.com), dict-chrome-ex por ultimo
    private static readonly int[] ordemQualidade = new int[] { 0, 2, 1 };
    // endereco reserva (traducao pior): o que vem dele fica "provisorio" e e retraduzido depois
    public const int EpReserva = 1;
    private static readonly float[] epCastigoAte = new float[Provedores.GoogleEndpoints];
    private static readonly int[] ep429 = new int[Provedores.GoogleEndpoints];
    private static readonly int[] epFormato = new int[Provedores.GoogleEndpoints];
    // falhas seguidas de cada endereco (qualquer motivo). Muitas seguidas = o Google pode ter mudado o endereco.
    private static readonly int[] epFalhas = new int[Provedores.GoogleEndpoints];
    private const int AvisoFalhas = 5;

    private static void FalhouEndereco(int ep)
    {
        if (ep < 0 || ep >= epFalhas.Length) return;
        epFalhas[ep]++;
        // avisa uma vez por sequencia (os castigos continuam sendo logados normalmente)
        if (epFalhas[ep] == AvisoFalhas) LiveLog.Aviso("lg_ep_quebrado", Provedores.NomeGoogle(ep), AvisoFalhas);
    }

    // segundos que faltam para o endereco sair do castigo (0 = livre). Usado na aba Log.
    public static int CastigoRestante(int ep)
    {
        if (ep < 0 || ep >= epCastigoAte.Length) return 0;
        float s = epCastigoAte[ep] - Time.unscaledTime;
        return s > 0f ? Mathf.CeilToInt(s) : 0;
    }

    private static int EscolherEndereco(float agora)
    {
        int n = Provedores.GoogleEndpoints;
        for (int k = 0; k < ordemQualidade.Length; k++)
        {
            int e = ordemQualidade[k];
            if (agora >= epCastigoAte[e])
            {
                if (e != GoogleEp)
                {
                    LiveLog.Info("lg_usando", Provedores.NomeGoogle(e));
                    GoogleEp = e;
                }
                return e;
            }
        }
        // todos de castigo: pausa ate o primeiro liberar
        float menor = epCastigoAte[0];
        for (int i = 1; i < n; i++) if (epCastigoAte[i] < menor) menor = epCastigoAte[i];
        pausaAte = menor;
        return -1;
    }

    // motivo = texto de log (LiveLog.T), traduzido na hora de mostrar
    private static void Castigar(int ep, float segundos, LogTexto motivo)
    {
        float agora = Time.unscaledTime;
        epCastigoAte[ep] = agora + segundos;
        LiveLog.Aviso("lg_castigo", Provedores.NomeGoogle(ep), motivo, Mathf.RoundToInt(segundos));
    }

    private static async Task Enviar(Provedor p, List<string> lote, int ger, int ep)
    {
        Pedido req = null;
        string erro = null;
        string corpo = null;
        int codigo = 0;
        try { req = Provedores.Montar(p, lote, ep); }
        catch (Exception e) { erro = e.Message; }

        if (req != null)
        {
            Resposta resp = await Http.Enviar(req);
            codigo = resp.Codigo;
            if (!string.IsNullOrEmpty(resp.Erro)) erro = resp.Erro;
            corpo = resp.Corpo;
            retryAfter = codigo == 429 && resp.RetryAfter > 0 ? Mathf.Clamp(resp.RetryAfter, 1, 600) : 0;
        }
        emVoo--;
        if (ger != geracao) return; // configuracao mudou enquanto esperava: descarta
        try { Concluir(p, lote, codigo, erro, corpo, ep); }
        catch (Exception e) { LiveLog.Erro("le_parte", e, "finish request"); }
    }

    private static void Concluir(Provedor p, List<string> lote, int codigo, string erro, string corpo, int ep)
    {
        float agora = Time.unscaledTime;
        bool google = p.Tipo == 0;
        if (codigo == 200)
        {
            List<string> catsIA;
            List<string> trad = Provedores.Interpretar(p, lote, corpo, ep, out catsIA);
            if (lote.Count > 1)
            {
                if (trad != null) LotesOk++; else LotesFallback++;
                string nomeEp = google ? ", " + Provedores.NomeGoogle(ep) : "";
                if (trad == null)
                    LiveLog.Aviso("lg_lotes", LotesOk, LotesFallback, lote.Count, LiveLog.T("lg_falhou"), nomeEp);
                else if (LotesOk % 10 == 0)
                    LiveLog.Info("lg_lotes", LotesOk, LotesFallback, lote.Count, LiveLog.T("lg_ok"), nomeEp);
                if (trad == null && corpo != null)
                    LiveLog.Aviso("lg_resposta", corpo.Length > 600 ? corpo.Substring(0, 600) + "..." : corpo);
            }
            if (trad != null)
            {
                bool provisorio = google && ep == EpReserva;
                int reFeitos = 0;
                for (int i = 0; i < lote.Count; i++)
                {
                    if (retraduzindo.Remove(lote[i]))
                    {
                        // retraducao de um provisorio: so troca se veio de um endereco bom
                        if (!provisorio)
                        {
                            TradCache.Add(lote[i], trad[i]);
                            TradCache.RemoverProvisorio(lote[i]);
                            Glossario.TrocarAuto(lote[i], trad[i]);   // termo travado pelo jogador nao muda
                            atualizados.Add(lote[i]);
                            reFeitos++;
                        }
                    }
                    else
                    {
                        TradCache.Add(lote[i], trad[i]);
                        if (provisorio) TradCache.AddProvisorio(lote[i]);
                        string pista;
                        pistas.TryGetValue(lote[i], out pista);
                        pistas.Remove(lote[i]);
                        Glossario.Auto(lote[i], trad[i], pista, catsIA != null ? Categorias.PorNome(catsIA[i]) : -1);
                        LembrarRecente(lote[i], trad[i]);
                        Traduzidos++;
                    }
                    naFila.Remove(lote[i]);
                    tentativas.Remove(lote[i]);
                    unitarios.Remove(lote[i]);
                }
                if (reFeitos > 0)
                {
                    Retraduzidos += reFeitos;
                    memo.Clear();
                    AtualizarPendente = true;
                    LiveLog.Info("lg_retraduzidos", reFeitos, Provedores.NomeGoogle(ep), TradCache.TotalProvisorios);
                }
                backoff = 0;
                if (google) { ep429[ep] = 0; epFormato[ep] = 0; epFalhas[ep] = 0; }
                UltimoErro = "";
                VarrerAgora = true;
                return;
            }
            // resposta fora do formato: tenta de novo um por um
            Erros++;
            ErroChave("e_formato");
            if (google)
            {
                if (lote.Count == 1) FalhouEndereco(ep);
                // lote que falhou: separa sem gastar tentativa. Texto sozinho que falhou: conta tentativa
                // e, se o endereco falhar 3 vezes seguidas assim, ele fica 30 min parado.
                if (lote.Count == 1 && ++epFormato[ep] >= 3)
                {
                    epFormato[ep] = 0;
                    Castigar(ep, 1800f, LiveLog.T("lg_m_formato"));
                }
                for (int i = lote.Count - 1; i >= 0; i--) Devolver(lote[i], lote.Count == 1, lote.Count > 1);
                return;
            }
            for (int i = lote.Count - 1; i >= 0; i--) Devolver(lote[i], true, true);
            return;
        }

        Erros++;
        if (codigo == 401 || codigo == 403)
            ErroChave("e_chave", codigo);
        else if (codigo == 404)
            ErroChave("e_404");
        else if (codigo == 429)
            ErroChave("e_429");
        else
            ErroChave("e_erro", codigo, string.IsNullOrEmpty(erro) ? "" : " - " + erro);

        // o servico nao aceitou o "raciocinio minimo": desliga so isso para este provedor e tenta de novo
        // sem gastar tentativa dos textos
        if (!google && Provedores.ErroRaciocinio(codigo, corpo)
            && Provedores.DesligarRaciocinio(p))
        {
            pausaAte = 0f;
            for (int i = lote.Count - 1; i >= 0; i--) Devolver(lote[i], false, false);
            return;
        }
        if (codigo == 400)
        {
            // pode ser culpa de um texto especifico: conta tentativa
            for (int i = lote.Count - 1; i >= 0; i--) Devolver(lote[i], true, lote.Count > 1);
            return;
        }
        if (google)
        {
            // Google: em vez de pausar tudo, poe so este endereco de castigo e passa para o proximo.
            // 429: 5, 10, 20, 30 min. Outros erros (rede, 403, 5xx): 1 min.
            float castigo;
            if (codigo == 429)
            {
                int n = ep429[ep]++;
                castigo = Mathf.Min(1800f, 300f * (1 << Mathf.Min(n, 3)));
                if (retryAfter > castigo) castigo = retryAfter;
            }
            else castigo = 60f;
            FalhouEndereco(ep);
            Castigar(ep, castigo, codigo == 0 ? LiveLog.T("lg_m_rede", string.IsNullOrEmpty(erro) ? "" : " (" + erro + ")") : LiveLog.T("lg_m_codigo", codigo));
            pausaAte = agora + 1f;
            for (int i = lote.Count - 1; i >= 0; i--) Devolver(lote[i], false, false);
            return;
        }
        float pausa;
        if (codigo == 401 || codigo == 403 || codigo == 404) pausa = 30f;
        else if (codigo == 429) pausa = Mathf.Max(retryAfter, Mathf.Min(300f, 30f * (1 << Mathf.Min(backoff, 3))));
        else pausa = Mathf.Min(120f, 10f * (1 << Mathf.Min(backoff, 4)));
        backoff++;
        pausaAte = agora + pausa;
        for (int i = lote.Count - 1; i >= 0; i--) Devolver(lote[i], false, false);
    }

    private static void Devolver(string tpl, bool contar, bool unitario)
    {
        if (contar)
        {
            int n;
            tentativas.TryGetValue(tpl, out n);
            n++;
            tentativas[tpl] = n;
            if (n >= 3)
            {
                falhou.Add(tpl);
                naFila.Remove(tpl);
                // provisorio que nao deu para retraduzir: fica com a traducao que ja tem
                if (retraduzindo.Remove(tpl)) TradCache.RemoverProvisorio(tpl);
                return;
            }
        }
        if (unitario) unitarios.Add(tpl);
        fila.AddFirst(tpl);
    }

    // ---------------- teste do botao "Testar" ----------------

    public static void Testar()
    {
        Teste = LiveTxt.T("t_testando");
        _ = EnviarTeste(LiveConfig.Atual);
    }

    private static async Task EnviarTeste(Provedor p)
    {
        string motivo;
        if (!Provedores.Configurado(p, out motivo))
        {
            Teste = LiveTxt.F("t_nao_deu", motivo);
            return;
        }
        List<string> lote = new List<string>();
        lote.Add("你好，欢迎来到青城山！Welcome to Mount Qingcheng!");
        int ep = GoogleEp;
        int codigo = 0;
        string err = null;
        string corpo = null;
        // segunda volta so se o servico recusou o "raciocinio minimo" (ai vai sem ele)
        for (int volta = 0; volta < 2; volta++)
        {
            Pedido req = null;
            string erro = null;
            try { req = Provedores.Montar(p, lote, ep); }
            catch (Exception e) { erro = e.Message; }
            if (req == null)
            {
                Teste = LiveTxt.F("t_erro", erro);
                return;
            }
            Resposta resp = await Http.Enviar(req);
            codigo = resp.Codigo;
            err = resp.Erro;
            corpo = resp.Corpo;
            if (p.Tipo != 0 && Provedores.ErroRaciocinio(codigo, corpo)
                && Provedores.DesligarRaciocinio(p)) continue;
            break;
        }
        if (codigo == 200)
        {
            List<string> r = Provedores.Interpretar(p, lote, corpo, ep);
            Teste = r != null ? "OK: " + r[0] : LiveTxt.T("t_formato");
        }
        else if (codigo == 401 || codigo == 403) Teste = LiveTxt.F("t_chave", codigo);
        else if (codigo == 404) Teste = LiveTxt.T("t_404");
        else Teste = LiveTxt.F("t_falhou", codigo, err);
    }
}
