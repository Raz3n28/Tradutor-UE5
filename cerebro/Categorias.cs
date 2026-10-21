using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed class CategoriaInfo
{
    public int Id;
    public string Nome = "";        // so nas categorias criadas; as padrao usam o nome do LiveTxt
    public int Cor;                 // indice da paleta (so nas criadas)
    public string Palavras = "";    // palavras-chave (categoria automatica, vale em qualquer modo)
    public string Instrucao = "";   // instrucao para a IA (so no modo com chave)

    // palavras-chave ja separadas (cache)
    public string[] Chaves;
    public string ChavesDe;

    public bool Padrao { get { return Id < Categorias.NumBase; } }
}

// Categorias do glossario: as 13 padrao (ids 0-12, feitas para jogo de cultivo) + ate 8 criadas pelo jogador
// (ids 13 em diante, nunca reaproveitados).
// Cada uma tem palavras-chave (categoria automatica, sem gastar token) e uma instrucao para a IA.
// Arquivo unico, vale para todos os idiomas: categorias.txt
//   id<TAB>nome<TAB>cor<TAB>palavras<TAB>instrucao        e as linhas  #proximo<TAB>N  e  #versao<TAB>2
// Versao 1 (do ModLive: 9 padrao, criadas a partir do 9): as criadas mudam de id (+4), ver Migrar.
internal static class Categorias
{
    // 0 Outros, 1 Personagem, 2 Lugar, 3 Seita, 4 Reino (de cultivo), 5 Tecnica, 6 Pilula, 7 Artefato, 8 Item,
    // 9 Atributo, 10 Missao, 11 Menu, 12 Titulo
    public const int NumBase = 13;
    public const int Versao2 = 2;
    private const int NumBaseV1 = 9;
    public const int LimiteId = 64;        // ids vao de 0 a 63
    public const int MaxNovas = 8;
    public const int MaxNome = 20;
    public const int MaxPalavras = 300;
    public const int MaxInstrucao = 150;

    // ordem em que aparecem nos filtros (Outros por ultimo), depois as criadas
    private static readonly int[] OrdemPadrao = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0 };
    // desempate das palavras-chave das padrao (quando duas casam do mesmo jeito, vale a que vem antes)
    private static readonly int[] OrdemBusca = new int[] { 3, 4, 5, 6, 7, 1, 12, 2, 9, 10, 8, 11 };
    // categoria antiga (versao 1, ModLive) -> nova
    private static readonly int[] DeV1 = new int[] { 0, 3, 8, 7, 5, 2, 1, 11, 12 };

    private static readonly Color[] Paleta = new Color[]
    {
        new Color(0.40f, 0.85f, 0.85f, 1f),
        new Color(0.95f, 0.55f, 0.75f, 1f),
        new Color(0.70f, 0.90f, 0.40f, 1f),
        new Color(0.95f, 0.65f, 0.30f, 1f),
        new Color(0.55f, 0.65f, 1.00f, 1f),
        new Color(0.85f, 0.80f, 0.55f, 1f),
        new Color(0.60f, 0.95f, 0.75f, 1f),
        new Color(0.85f, 0.60f, 0.95f, 1f)
    };

    private static readonly Dictionary<int, CategoriaInfo> mapa = new Dictionary<int, CategoriaInfo>();
    private static readonly List<CategoriaInfo> lista = new List<CategoriaInfo>();
    private static int proximoId = NumBase;
    private static bool sujo;
    private static int[] ordemCache;

    // sobe a cada mudanca
    public static int Versao;

    private static string Arquivo { get { return Path.Combine(LiveConfig.Pasta, "categorias.txt"); } }

    public static int NumNovas { get { return lista.Count - NumBase; } }

    // ---------------- carregar / salvar ----------------

    public static void Carregar()
    {
        mapa.Clear();
        lista.Clear();
        proximoId = NumBase;
        for (int i = 0; i < NumBase; i++)
        {
            CategoriaInfo c = new CategoriaInfo();
            c.Id = i;
            c.Palavras = PalavrasPadrao(i);
            c.Instrucao = InstrucaoPadrao(i);
            Adicionar(c);
        }

        string arq = Arquivo;
        if (File.Exists(arq))
        {
            try
            {
                string[] linhas = File.ReadAllLines(arq, Encoding.UTF8);
                // arquivo de antes das instrucoes padrao (sem a linha #instrucoes): instrucao vazia numa
                // categoria padrao vira a instrucao padrao. Depois disso, vazio e escolha do jogador.
                bool temMarca = false;
                bool v2 = false;
                for (int i = 0; i < linhas.Length; i++)
                {
                    if (linhas[i].StartsWith("#instrucoes", StringComparison.Ordinal)) temMarca = true;
                    if (linhas[i].StartsWith("#versao\t" + Versao2, StringComparison.Ordinal)) v2 = true;
                }
                for (int i = 0; i < linhas.Length; i++)
                {
                    string[] p = linhas[i].Split('\t');
                    if (p.Length >= 2 && p[0] == "#proximo")
                    {
                        int pr;
                        if (int.TryParse(p[1], out pr))
                        {
                            if (!v2) pr += NumBase - NumBaseV1;
                            if (pr > proximoId && pr <= LimiteId) proximoId = pr;
                        }
                        continue;
                    }
                    if (p.Length < 5) continue;
                    int id;
                    if (!int.TryParse(p[0], out id) || id < 0 || id >= LimiteId) continue;
                    if (!v2)
                    {
                        // arquivo antigo: as padrao de antes nao valem mais (as novas tem palavras e instrucoes
                        // proprias); as criadas pelo jogador ficam, com o id novo
                        if (id < NumBaseV1) continue;
                        id = Migrar(id);
                        if (id <= 0) continue;
                        sujo = true;
                    }
                    string nome = Desescapar(p[1]);
                    int cor;
                    int.TryParse(p[2], out cor);
                    string pal = Cortar(Desescapar(p[3]), MaxPalavras);
                    string ins = Cortar(Desescapar(p[4]), MaxInstrucao);
                    CategoriaInfo c;
                    if (id < NumBase)
                    {
                        c = mapa[id];
                    }
                    else
                    {
                        if (mapa.ContainsKey(id) || NumNovas >= MaxNovas || nome.Trim().Length == 0) continue;
                        c = new CategoriaInfo();
                        c.Id = id;
                        c.Nome = Cortar(nome.Trim(), MaxNome);
                        c.Cor = cor < 0 ? 0 : cor;
                        Adicionar(c);
                        if (id >= proximoId) proximoId = id + 1;
                    }
                    c.Palavras = pal;
                    c.Instrucao = (!temMarca && id < NumBase && ins.Trim().Length == 0) ? InstrucaoPadrao(id) : ins;
                }
            }
            catch (Exception e)
            {
                LiveLog.Erro("le_ler", e, LiveLog.T("it_cat"));
            }
        }
        Versao++;
        ordemCache = null;
        LiveLog.Info("lg_categorias", lista.Count);
    }

    // id de categoria da versao 1 (ModLive e arquivos antigos do glossario) -> id de agora. 0 = Outros.
    public static int Migrar(int idV1)
    {
        if (idV1 >= 0 && idV1 < DeV1.Length) return DeV1[idV1];
        int n = idV1 + NumBase - NumBaseV1;
        return n < LimiteId ? n : 0;
    }

    private static void Adicionar(CategoriaInfo c)
    {
        mapa[c.Id] = c;
        lista.Add(c);
    }

    public static void Marcar()
    {
        sujo = true;
        Versao++;
        ordemCache = null;
    }

    public static void SalvarSeSujo()
    {
        if (sujo) Salvar();
    }

    public static void Salvar()
    {
        sujo = false;
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("#proximo\t").Append(proximoId).Append('\n');
            sb.Append("#instrucoes\t1\n");
            sb.Append("#versao\t").Append(Versao2).Append('\n');
            for (int i = 0; i < lista.Count; i++)
            {
                CategoriaInfo c = lista[i];
                sb.Append(c.Id).Append('\t')
                  .Append(Escapar(c.Padrao ? "" : c.Nome)).Append('\t')
                  .Append(c.Cor).Append('\t')
                  .Append(Escapar(c.Palavras ?? "")).Append('\t')
                  .Append(Escapar(c.Instrucao ?? "")).Append('\n');
            }
            File.WriteAllText(Arquivo, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_gravar", e, LiveLog.T("it_cat"));
        }
    }

    // ---------------- consultas ----------------

    public static CategoriaInfo Obter(int id)
    {
        CategoriaInfo c;
        return mapa.TryGetValue(id, out c) ? c : null;
    }

    public static bool Existe(int id)
    {
        return mapa.ContainsKey(id);
    }

    // ids na ordem dos filtros e das listas
    public static int[] Ordem()
    {
        if (ordemCache != null) return ordemCache;
        List<int> r = new List<int>();
        for (int i = 0; i < OrdemPadrao.Length; i++) r.Add(OrdemPadrao[i]);
        for (int i = NumBase; i < lista.Count; i++) r.Add(lista[i].Id);
        ordemCache = r.ToArray();
        return ordemCache;
    }

    public static string Nome(int id)
    {
        CategoriaInfo c = Obter(id);
        if (c == null) return LiveTxt.T("cat0");
        if (c.Padrao) return LiveTxt.T("cat" + id);
        return c.Nome.Length > 0 ? c.Nome : "?";
    }

    // para o prompt da IA (sempre em ingles nas padrao)
    public static string NomeIngles(int id)
    {
        CategoriaInfo c = Obter(id);
        if (c == null) return "Other";
        if (c.Padrao) return LiveTxt.TIdioma("cat" + id, 1);
        return c.Nome;
    }

    public static Color Cor(int id)
    {
        CategoriaInfo c = Obter(id);
        if (c != null && !c.Padrao) return Paleta[c.Cor % Paleta.Length];
        switch (id)
        {
            case 1: return new Color(0.65f, 0.71f, 0.99f, 1f);   // Personagem
            case 2: return new Color(0.99f, 0.83f, 0.30f, 1f);   // Lugar
            case 3: return new Color(0.49f, 0.83f, 0.99f, 1f);   // Seita
            case 4: return new Color(0.94f, 0.67f, 0.99f, 1f);   // Reino
            case 5: return new Color(0.77f, 0.71f, 0.99f, 1f);   // Tecnica
            case 6: return new Color(0.53f, 0.94f, 0.67f, 1f);   // Pilula
            case 7: return new Color(0.99f, 0.62f, 0.62f, 1f);   // Artefato
            case 8: return new Color(0.99f, 0.73f, 0.45f, 1f);   // Item
            case 9: return new Color(0.60f, 0.90f, 0.90f, 1f);   // Atributo
            case 10: return new Color(0.90f, 0.85f, 0.60f, 1f);  // Missao
            case 11: return new Color(0.70f, 0.78f, 0.85f, 1f);  // Menu
            case 12: return new Color(0.99f, 0.60f, 0.85f, 1f);  // Titulo
            default: return new Color(0.75f, 0.75f, 0.78f, 1f);
        }
    }

    // ---------------- criar / apagar / restaurar ----------------

    // devolve o id da nova categoria, ou -1 (erro = chave do texto de erro no LiveTxt)
    public static int Criar(string nome, string palavras, string instrucao, out string erro)
    {
        erro = null;
        string n = (nome ?? "").Trim();
        if (n.Length > MaxNome) n = n.Substring(0, MaxNome);
        if (NumNovas >= MaxNovas || proximoId >= LimiteId)
        {
            erro = "cat_limite";
            return -1;
        }
        if (n.Length == 0)
        {
            erro = "cat_erro_nome";
            return -1;
        }
        for (int i = 0; i < lista.Count; i++)
        {
            if (string.Compare(Nome(lista[i].Id), n, StringComparison.OrdinalIgnoreCase) == 0)
            {
                erro = "cat_erro_nome";
                return -1;
            }
        }
        CategoriaInfo c = new CategoriaInfo();
        c.Id = proximoId++;
        c.Nome = n;
        c.Cor = c.Id - NumBase;
        c.Palavras = Cortar((palavras ?? "").Trim(), MaxPalavras);
        c.Instrucao = Cortar((instrucao ?? "").Trim(), MaxInstrucao);
        Adicionar(c);
        Marcar();
        return c.Id;
    }

    // so as criadas. Os termos dela voltam para "Outros".
    public static void Apagar(int id)
    {
        CategoriaInfo c = Obter(id);
        if (c == null || c.Padrao) return;
        mapa.Remove(id);
        lista.Remove(c);
        Glossario.MoverCategoria(id, 0);
        Marcar();
    }

    // padrao: volta as palavras-chave originais e limpa a instrucao. Criada: limpa os dois campos.
    public static void Restaurar(int id)
    {
        CategoriaInfo c = Obter(id);
        if (c == null) return;
        c.Palavras = c.Padrao ? PalavrasPadrao(id) : "";
        c.Instrucao = c.Padrao ? InstrucaoPadrao(id) : "";
        Marcar();
    }

    // ---------------- categoria automatica (palavras-chave) ----------------

    // modo 0 = todas (criadas primeiro), 1 = so as criadas, 2 = so as padrao. Devolve o id ou -1.
    // Duas passadas: 1) palavra-chave no FIM do termo (o que manda num nome: 筑基丹 = pilula, 青城派 = seita,
    // "Iron Sword" = arma); a mais longa ganha. 2) palavra-chave em qualquer lugar (menos as de uma letra
    // so de chines/japones: essas so valem no fim, senao 剑诀 seria arma por causa do 剑).
    public static int PorTexto(string texto, int modo)
    {
        if (string.IsNullOrEmpty(texto)) return -1;
        string baixo = texto.ToLowerInvariant().Trim();
        while (baixo.Length > 0 && !char.IsLetterOrDigit(baixo[baixo.Length - 1])) baixo = baixo.Substring(0, baixo.Length - 1);
        if (baixo.Length == 0) return -1;
        HashSet<string> palavras = PalavrasDe(baixo);
        string ultima = UltimaPalavra(baixo);
        List<CategoriaInfo> ordem = new List<CategoriaInfo>();
        if (modo != 2) for (int i = NumBase; i < lista.Count; i++) ordem.Add(lista[i]);
        if (modo != 1)
        {
            for (int i = 0; i < OrdemBusca.Length; i++)
            {
                CategoriaInfo c = Obter(OrdemBusca[i]);
                if (c != null) ordem.Add(c);
            }
        }
        // 1) no fim
        int melhor = -1, tam = 0;
        for (int i = 0; i < ordem.Count; i++)
        {
            int n = Fim(ordem[i], baixo, ultima);
            if (n > tam) { tam = n; melhor = ordem[i].Id; }
        }
        if (melhor >= 0) return melhor;
        // 2) em qualquer lugar
        for (int i = 0; i < ordem.Count; i++)
            if (Casa(ordem[i], baixo, palavras)) return ordem[i].Id;
        return -1;
    }

    // tamanho da maior palavra-chave que fecha o termo (0 = nenhuma)
    private static int Fim(CategoriaInfo c, string baixo, string ultima)
    {
        string[] chaves = Chaves(c);
        int r = 0;
        for (int i = 0; i < chaves.Length; i++)
        {
            string k = chaves[i];
            if (k.Length <= r) continue;
            if (SoLetrasAscii(k) || k.IndexOf(' ') >= 0)
            {
                // latino: a ultima palavra (ou as ultimas, se a chave tiver espaco)
                if (k.IndexOf(' ') >= 0 ? baixo.EndsWith(k, StringComparison.Ordinal) && (baixo.Length == k.Length || !char.IsLetter(baixo[baixo.Length - k.Length - 1]))
                                        : ultima == k || (ultima.Length > 3 && ultima[ultima.Length - 1] == 's' && ultima.Substring(0, ultima.Length - 1) == k))
                    r = k.Length;
            }
            else if (baixo.EndsWith(k, StringComparison.Ordinal)) r = k.Length;
        }
        return r;
    }

    private static string UltimaPalavra(string baixo)
    {
        int fim = baixo.Length;
        while (fim > 0 && !char.IsLetter(baixo[fim - 1])) fim--;
        int ini = fim;
        while (ini > 0 && char.IsLetter(baixo[ini - 1])) ini--;
        return baixo.Substring(ini, fim - ini);
    }

    private static bool Casa(CategoriaInfo c, string baixo, HashSet<string> palavras)
    {
        string[] chaves = Chaves(c);
        for (int i = 0; i < chaves.Length; i++)
        {
            string k = chaves[i];
            if (SoLetrasAscii(k))
            {
                if (palavras.Contains(k)) return true;
            }
            else if (k.Length == 1 && LiveMotor.TemCJK(k))
            {
                continue;   // letra solta de chines: so no fim (passada 1)
            }
            else if (baixo.IndexOf(k, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string[] Chaves(CategoriaInfo c)
    {
        string pal = c.Palavras ?? "";
        if (c.Chaves != null && c.ChavesDe == pal) return c.Chaves;
        List<string> r = new List<string>();
        string[] partes = pal.Split(new char[] { ',', ';', '\uFF0C', '\u3001', '\n' });
        for (int i = 0; i < partes.Length && r.Count < 80; i++)
        {
            string k = partes[i].Trim().ToLowerInvariant();
            if (k.Length > 0) r.Add(k);
        }
        c.Chaves = r.ToArray();
        c.ChavesDe = pal;
        return c.Chaves;
    }

    private static bool SoLetrasAscii(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch < 'a' || ch > 'z') return false;
        }
        return s.Length > 0;
    }

    // palavras do texto (e a forma sem "s" do plural)
    private static HashSet<string> PalavrasDe(string baixo)
    {
        HashSet<string> set = new HashSet<string>();
        int n = baixo.Length;
        int i = 0;
        while (i < n)
        {
            while (i < n && !char.IsLetter(baixo[i])) i++;
            int ini = i;
            while (i < n && char.IsLetter(baixo[i])) i++;
            if (i > ini)
            {
                string w = baixo.Substring(ini, i - ini);
                set.Add(w);
                if (w.Length > 3 && w[w.Length - 1] == 's') set.Add(w.Substring(0, w.Length - 1));
            }
        }
        return set;
    }

    // ---------------- prompt da IA ----------------

    // Categoria pelo nome que a IA devolveu (nome em ingles das padrao, ou o nome no idioma do menu,
    // ou o nome de uma categoria criada). -1 se vazio ou se nao achou.
    public static int PorNome(string nome)
    {
        if (string.IsNullOrEmpty(nome)) return -1;
        string n = nome.Trim();
        if (n.Length == 0) return -1;
        for (int i = 0; i < lista.Count; i++)
        {
            int id = lista[i].Id;
            if (string.Equals(n, NomeIngles(id), StringComparison.OrdinalIgnoreCase)) return id;
            if (string.Equals(n, Nome(id), StringComparison.OrdinalIgnoreCase)) return id;
        }
        return -1;
    }

    // Lista de TODAS as categorias para a IA escolher (nome exato a copiar + instrucao + exemplos).
    // comExemplos = manda tambem as palavras-chave (ajuda a IA a entender categorias criadas pelo jogador).
    public static string ListaParaIA(bool comExemplos)
    {
        StringBuilder sb = new StringBuilder();
        int[] ordem = Ordem();
        for (int k = 0; k < ordem.Length; k++)
        {
            CategoriaInfo c = Obter(ordem[k]);
            if (c == null) continue;
            sb.Append("\n- ").Append(NomeIngles(c.Id).Replace("\n", " "));
            string ins = (c.Instrucao ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
            if (ins.Length > 0) sb.Append(": ").Append(ins);
            else if (c.Id == 0) sb.Append(": use when no other category fits.");
            if (comExemplos)
            {
                string pal = (c.Palavras ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
                if (pal.Length > 120) pal = pal.Substring(0, 120);
                if (pal.Length > 0) sb.Append(" (examples: ").Append(pal).Append(")");
            }
        }
        return sb.ToString();
    }

    public static string TextoPrompt()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < lista.Count; i++)
        {
            CategoriaInfo c = lista[i];
            string ins = (c.Instrucao ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
            if (ins.Length == 0) continue;
            if (sb.Length > 1500) break;
            if (sb.Length == 0)
                sb.Append("Term categories. When a short string (a name, item, button, title...) clearly belongs to one of these categories, follow that category's instruction (the hard rules above always win):");
            sb.Append("\n- ").Append(NomeIngles(c.Id)).Append(": ").Append(ins);
        }
        return sb.ToString();
    }

    // ---------------- padroes ----------------

    // instrucao de fabrica para a IA (so modo com chave). Em ingles porque vai no pedido junto com as regras fixas,
    // e vale para qualquer idioma de destino. Maximo MaxInstrucao (150) letras cada. Neutras de proposito:
    // o jogador ajusta depois de ver como o jogo chama as coisas (e trava no glossario o que quiser fixo).
    public static string InstrucaoPadrao(int id)
    {
        switch (id)
        {
            case 1: return "People and forms of address. Romanize Chinese names in pinyin, never translate their meaning. Translate titles such as Elder or Senior Brother.";
            case 2: return "Place names: keep the proper part in pinyin and translate the generic part (mountain, village, valley, temple, cave).";
            case 3: return "Sects, clans and factions: proper part in pinyin, translate the generic word (sect, clan, palace). Same sect = same translation.";
            case 4: return "Cultivation realm or stage. Translate by meaning, concise, and always use the same translation for the same realm.";
            case 5: return "Techniques, martial arts and spells: short and punchy, like a move name. No full sentences.";
            case 6: return "Pills, elixirs and herbs: short noun phrase, no articles. Keep invented proper names in pinyin.";
            case 7: return "Weapons, armor and treasures: concise and evocative. Keep proper names in pinyin.";
            case 8: return "Items and materials: short noun phrase, no articles.";
            case 9: return "Stats and attributes: very short, the usual RPG term in the target language.";
            case 10: return "Quest titles and objectives: short and clear, no final period in titles.";
            case 11: return "Menu buttons and UI labels: as short as possible (1-2 words), the usual wording for game menus.";
            case 12: return "Titles and epithets earned by characters: short and evocative.";
            default: return "";
        }
    }

    // palavras-chave de fabrica das categorias padrao (jogo de cultivo / xianxia). Letra solta de chines
    // so vale no FIM do termo (ver PorTexto).
    public static string PalavrasPadrao(int id)
    {
        switch (id)
        {
            case 1: return "elder, master, senior, junior, disciple, brother, sister, lord, lady, 师兄, 师姐, 师弟, 师妹, 师父, 师傅, 师尊, 长老, 掌门, 宗主, 前辈, 道友, 弟子, 先生, 公子, 姑娘, 夫人, 真人, 道长, 大师, 仙子";
            case 2: return "mountain, peak, temple, cave, village, town, city, valley, forest, river, lake, cliff, 山, 峰, 寺, 观, 洞, 村, 镇, 城, 谷, 林, 河, 湖, 关, 崖, 岭, 坡, 府, 殿, 庄, 岛";
            case 3: return "sect, clan, faction, school, alliance, guild, order, family, 门派, 宗门, 宗, 派, 门, 帮, 教, 宫, 盟, 阁, 族";
            case 4: return "realm, stage, 境, 期, 境界, 炼气, 筑基, 金丹, 结丹, 元婴, 化神, 炼虚, 合体, 大乘, 渡劫, 先天, 后天";
            case 5: return "skill, technique, art, sutra, formation, spell, palm, fist, stance, 技能, 功法, 心法, 剑诀, 诀, 功, 法, 掌, 拳, 指, 经, 术, 阵, 式, 步, 印, 咒";
            case 6: return "pill, elixir, herb, grass, fruit, 丹, 药, 草, 果, 芝, 丸, 散, 液, 露, 灵药, 丹药";
            case 7: return "sword, blade, saber, spear, staff, bow, fan, whip, armor, robe, ring, weapon, treasure, artifact, 剑, 刀, 枪, 棍, 弓, 扇, 鞭, 甲, 袍, 戒, 镯, 冠, 靴, 武器, 法宝, 灵宝, 法器, 宝, 器";
            case 8: return "item, material, ore, stone, jade, talisman, scroll, token, key, letter, 灵石, 石, 玉, 符, 卷, 令, 牌, 信, 材料, 道具, 物品, 矿, 晶";
            case 9: return "hp, mp, health, attack, defense, speed, crit, luck, stamina, 气血, 生命, 灵力, 真气, 法力, 攻击, 防御, 速度, 暴击, 悟性, 根骨, 气运, 体力, 修为, 经验, 属性, 力量, 身法";
            case 10: return "quest, mission, task, objective, chapter, 任务, 主线, 支线, 委托, 目标, 章";
            case 11: return "continue, start, new game, load, save, settings, options, exit, quit, back, confirm, cancel, close, apply, reset, 继续, 开始, 新游戏, 读取, 载入, 存档, 保存, 设置, 选项, 退出, 返回, 确定, 确认, 取消, 关闭, 应用, 重置";
            case 12: return "title, achievement, 称号, 头衔, 成就, 仙, 圣, 尊, 王, 帝, 皇";
            default: return "";
        }
    }

    // ---------------- util ----------------

    private static string Cortar(string s, int max)
    {
        if (s == null) return "";
        return s.Length <= max ? s : s.Substring(0, max);
    }

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
