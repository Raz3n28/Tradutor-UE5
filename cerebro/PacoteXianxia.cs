using System;
using System.Collections.Generic;

// Pacote de glossario xianxia (opcional, botao no Glossario): termos comuns de cultivo em PT-BR e EN, escritos
// a mao com as traducoes mais usadas pela comunidade. So entra o que o jogador escolher, sem trava, e sem
// trocar termo que ja existe. Cada termo fica marcado com o grupo (TermoGlossario.Pacote) para poder ser removido;
// termo que o jogador travou ou cuja traducao mudou nao e removido (virou dele).
internal static class PacoteXianxia
{
    // grupos: nome no menu = LiveTxt "pac_g<n>"; categoria do glossario de cada grupo
    public const int Grupos = 7;
    private static readonly int[] catGrupo = new int[] { 4, 12, 3, 0, 8, 5, 0 };   // reino, titulo, seita, outros, item, tecnica, outros

    private sealed class Item
    {
        public int Grupo;
        public string Zh, En, Pt;
    }

    private static List<Item> itens;

    // grupo<TAB>chines<TAB>ingles<TAB>portugues
    private const string Dados = @"0	炼气	Qi Refining	Refinamento de Qi
0	练气	Qi Refining	Refinamento de Qi
0	筑基	Foundation Establishment	Estabelecimento de Fundação
0	结丹	Core Formation	Formação do Núcleo
0	金丹	Golden Core	Núcleo Dourado
0	元婴	Nascent Soul	Alma Nascente
0	化神	Deity Transformation	Transformação Divina
0	炼虚	Void Refining	Refinamento do Vazio
0	合体	Body Integration	Integração Corporal
0	大乘	Mahayana	Mahayana
0	渡劫	Tribulation Transcendence	Travessia da Tribulação
0	真仙	True Immortal	Imortal Verdadeiro
0	金仙	Golden Immortal	Imortal Dourado
0	大罗金仙	Daluo Golden Immortal	Imortal Dourado Daluo
0	仙帝	Immortal Emperor	Imperador Imortal
0	后天	Acquired Realm	Reino Adquirido
0	先天	Innate Realm	Reino Inato
0	宗师	Grandmaster	Grão-Mestre
0	大宗师	Great Grandmaster	Grande Grão-Mestre
0	武者	Martial Artist	Artista Marcial
0	淬体	Body Tempering	Têmpera Corporal
0	锻体	Body Forging	Forja Corporal
0	开脉	Meridian Opening	Abertura dos Meridianos
0	聚气	Qi Gathering	Acúmulo de Qi
0	凝元	Essence Condensation	Condensação de Essência
0	化境	Transcendent Realm	Reino Transcendente
0	圣人	Saint	Santo
0	至尊	Supreme	Supremo
0	武王	Martial King	Rei Marcial
0	武尊	Martial Venerable	Venerável Marcial
0	武圣	Martial Saint	Santo Marcial
0	武帝	Martial Emperor	Imperador Marcial
0	初期	Early Stage	Estágio Inicial
0	中期	Middle Stage	Estágio Médio
0	后期	Late Stage	Estágio Avançado
0	巅峰	Peak	Pico
0	圆满	Perfection	Perfeição
0	大圆满	Great Perfection	Grande Perfeição
0	境界	Realm	Reino
0	突破	Breakthrough	Avanço
0	瓶颈	Bottleneck	Gargalo
0	天劫	Heavenly Tribulation	Tribulação Celestial
0	雷劫	Lightning Tribulation	Tribulação do Relâmpago
0	心魔	Inner Demon	Demônio Interior
0	走火入魔	Qi Deviation	Desvio de Qi
0	闭关	Secluded Cultivation	Cultivo Recluso
0	飞升	Ascension	Ascensão
0	修为	Cultivation Base	Base de Cultivo
0	修炼	Cultivation	Cultivo
0	修士	Cultivator	Cultivador
0	修仙	Immortal Cultivation	Cultivo Imortal
0	仙人	Immortal	Imortal
0	凡人	Mortal	Mortal
0	道心	Dao Heart	Coração do Dao
0	道基	Dao Foundation	Fundação do Dao
0	大道	Great Dao	Grande Dao
0	天道	Heavenly Dao	Dao Celestial
0	悟道	Dao Comprehension	Compreensão do Dao
1	师父	Master	Mestre
1	师尊	Venerable Master	Mestre Venerável
1	师傅	Master	Mestre
1	徒弟	Disciple	Discípulo
1	弟子	Disciple	Discípulo
1	师兄	Senior Brother	Irmão Sênior
1	师姐	Senior Sister	Irmã Sênior
1	师弟	Junior Brother	Irmão Júnior
1	师妹	Junior Sister	Irmã Júnior
1	师叔	Martial Uncle	Tio Marcial
1	师伯	Senior Martial Uncle	Tio Marcial Sênior
1	师祖	Grandmaster	Mestre Ancestral
1	师娘	Master's Wife	Esposa do Mestre
1	长老	Elder	Ancião
1	大长老	Great Elder	Grande Ancião
1	太上长老	Supreme Elder	Ancião Supremo
1	掌门	Sect Leader	Líder da Seita
1	宗主	Sect Master	Mestre da Seita
1	阁主	Pavilion Master	Mestre do Pavilhão
1	殿主	Palace Master	Mestre do Palácio
1	峰主	Peak Master	Mestre do Pico
1	谷主	Valley Master	Mestre do Vale
1	岛主	Island Master	Mestre da Ilha
1	堂主	Hall Master	Mestre do Salão
1	城主	City Lord	Senhor da Cidade
1	家主	Clan Head	Chefe do Clã
1	族长	Clan Patriarch	Patriarca do Clã
1	老祖	Old Ancestor	Velho Ancestral
1	祖师	Founding Patriarch	Patriarca Fundador
1	前辈	Senior	Sênior
1	晚辈	Junior	Júnior
1	道友	Fellow Daoist	Companheiro de Dao
1	少侠	Young Hero	Jovem Herói
1	大侠	Great Hero	Grande Herói
1	女侠	Heroine	Heroína
1	公子	Young Master	Jovem Mestre
1	少爷	Young Master	Jovem Mestre
1	少主	Young Lord	Jovem Senhor
1	小姐	Young Lady	Senhorita
1	姑娘	Miss	Moça
1	圣女	Saintess	Santa
1	圣子	Holy Son	Filho Sagrado
1	天骄	Heaven's Chosen	Escolhido do Céu
1	天才	Genius	Gênio
1	妖孽	Monstrous Genius	Gênio Monstruoso
1	魔头	Demon Head	Cabeça Demoníaca
1	魔尊	Demon Venerable	Venerável Demoníaco
1	散修	Rogue Cultivator	Cultivador Independente
1	护法	Protector	Protetor
1	执事	Deacon	Diácono
1	真人	Zhenren	Zhenren
1	道长	Daoist Priest	Sacerdote Taoísta
1	道人	Daoist	Taoísta
1	大师	Master	Grande Mestre
1	陛下	Your Majesty	Vossa Majestade
1	殿下	Your Highness	Vossa Alteza
1	皇帝	Emperor	Imperador
1	太子	Crown Prince	Príncipe Herdeiro
1	王爷	Prince	Príncipe
1	将军	General	General
1	在下	this one	este humilde
1	本座	this venerable one	este venerável
1	老夫	this old man	este velho
1	贫道	this poor Daoist	este pobre taoísta
2	宗门	Sect	Seita
2	门派	Sect	Seita
2	宗派	Sect	Seita
2	外门	Outer Sect	Seita Externa
2	内门	Inner Sect	Seita Interna
2	外门弟子	Outer Disciple	Discípulo Externo
2	内门弟子	Inner Disciple	Discípulo Interno
2	核心弟子	Core Disciple	Discípulo Principal
2	真传弟子	True Disciple	Discípulo Verdadeiro
2	亲传弟子	Personal Disciple	Discípulo Pessoal
2	杂役弟子	Servant Disciple	Discípulo Servo
2	记名弟子	Registered Disciple	Discípulo Registrado
2	正道	Righteous Path	Caminho Justo
2	魔道	Demonic Path	Caminho Demoníaco
2	邪道	Evil Path	Caminho Maligno
2	魔教	Demonic Cult	Culto Demoníaco
2	魔门	Demonic Sect	Seita Demoníaca
2	世家	Aristocratic Clan	Clã Aristocrático
2	家族	Clan	Clã
2	皇朝	Imperial Dynasty	Dinastia Imperial
2	王朝	Dynasty	Dinastia
2	帝国	Empire	Império
2	圣地	Holy Land	Terra Sagrada
2	道观	Daoist Temple	Templo Taoísta
2	寺庙	Temple	Templo
2	书院	Academy	Academia
2	学院	Academy	Academia
2	武馆	Martial Hall	Escola Marcial
2	镖局	Escort Agency	Agência de Escolta
2	商会	Merchant Guild	Guilda Mercante
2	拍卖会	Auction	Leilão
2	丹阁	Alchemy Pavilion	Pavilhão de Alquimia
2	藏经阁	Scripture Pavilion	Pavilhão das Escrituras
2	炼丹房	Alchemy Room	Sala de Alquimia
2	宗门大比	Sect Tournament	Torneio da Seita
2	江湖	Jianghu	Jianghu
2	武林	Martial World	Mundo Marcial
2	少林	Shaolin	Shaolin
2	武当	Wudang	Wudang
2	峨眉	Emei	Emei
2	华山	Huashan	Huashan
2	昆仑	Kunlun	Kunlun
2	丐帮	Beggars' Sect	Seita dos Mendigos
3	丹田	Dantian	Dantian
3	经脉	Meridians	Meridianos
3	穴位	Acupoint	Ponto de Acupuntura
3	穴道	Acupoint	Ponto Vital
3	奇经八脉	Eight Extraordinary Meridians	Oito Meridianos Extraordinários
3	任督二脉	Ren and Du Meridians	Meridianos Ren e Du
3	真气	True Qi	Qi Verdadeiro
3	灵气	Spiritual Qi	Qi Espiritual
3	灵力	Spiritual Power	Poder Espiritual
3	内力	Inner Strength	Força Interior
3	元气	Vital Energy	Energia Vital
3	仙气	Immortal Qi	Qi Imortal
3	魔气	Demonic Qi	Qi Demoníaco
3	煞气	Malevolent Qi	Qi Maligno
3	杀气	Killing Intent	Intenção Assassina
3	剑气	Sword Qi	Qi da Espada
3	刀气	Blade Qi	Qi da Lâmina
3	神识	Divine Sense	Sentido Divino
3	灵识	Spiritual Sense	Sentido Espiritual
3	元神	Primordial Spirit	Espírito Primordial
3	神魂	Soul	Alma
3	魂魄	Soul	Alma
3	肉身	Physical Body	Corpo Físico
3	体魄	Physique	Físico
3	灵根	Spiritual Root	Raiz Espiritual
3	资质	Aptitude	Aptidão
3	血脉	Bloodline	Linhagem de Sangue
3	精血	Essence Blood	Sangue Essencial
3	寿元	Lifespan	Longevidade
3	气血	Qi and Blood	Qi e Sangue
3	道体	Dao Body	Corpo do Dao
3	圣体	Holy Body	Corpo Sagrado
3	剑心	Sword Heart	Coração da Espada
3	剑意	Sword Intent	Intenção da Espada
3	刀意	Blade Intent	Intenção da Lâmina
3	意境	Artistic Conception	Concepção Artística
3	领域	Domain	Domínio
3	法相	Dharma Form	Forma Dharma
3	法则	Law	Lei
3	本源	Origin	Origem
3	因果	Karma	Carma
3	气运	Fortune	Fortuna
3	机缘	Fortuitous Encounter	Encontro Fortuito
3	真元	True Essence	Essência Verdadeira
3	罡气	Gang Qi	Qi Gang
4	丹药	Pill	Pílula
4	灵丹	Spirit Pill	Pílula Espiritual
4	仙丹	Immortal Pill	Pílula Imortal
4	筑基丹	Foundation Establishment Pill	Pílula de Estabelecimento de Fundação
4	聚气丹	Qi Gathering Pill	Pílula de Acúmulo de Qi
4	回气丹	Qi Recovery Pill	Pílula de Recuperação de Qi
4	疗伤丹	Healing Pill	Pílula de Cura
4	解毒丹	Antidote Pill	Pílula Antídoto
4	丹炉	Alchemy Furnace	Fornalha Alquímica
4	丹方	Pill Recipe	Receita de Pílula
4	炼丹	Alchemy	Alquimia
4	炼丹师	Alchemist	Alquimista
4	炼器	Artifact Refining	Refino de Artefatos
4	炼器师	Artificer	Artífice
4	阵法师	Formation Master	Mestre de Formações
4	符师	Talisman Master	Mestre de Talismãs
4	灵草	Spirit Herb	Erva Espiritual
4	灵药	Spirit Medicine	Remédio Espiritual
4	仙草	Immortal Herb	Erva Imortal
4	灵果	Spirit Fruit	Fruta Espiritual
4	人参	Ginseng	Ginseng
4	灵芝	Lingzhi Mushroom	Cogumelo Lingzhi
4	雪莲	Snow Lotus	Lótus da Neve
4	灵石	Spirit Stone	Pedra Espiritual
4	下品灵石	Low-grade Spirit Stone	Pedra Espiritual de Grau Baixo
4	中品灵石	Mid-grade Spirit Stone	Pedra Espiritual de Grau Médio
4	上品灵石	High-grade Spirit Stone	Pedra Espiritual de Grau Alto
4	极品灵石	Top-grade Spirit Stone	Pedra Espiritual de Grau Supremo
4	妖丹	Demon Core	Núcleo Demoníaco
4	兽核	Beast Core	Núcleo de Fera
4	内丹	Inner Core	Núcleo Interno
4	法宝	Magic Treasure	Tesouro Mágico
4	灵宝	Spirit Treasure	Tesouro Espiritual
4	仙器	Immortal Artifact	Artefato Imortal
4	神器	Divine Artifact	Artefato Divino
4	法器	Magic Tool	Ferramenta Mágica
4	灵器	Spirit Artifact	Artefato Espiritual
4	本命法宝	Natal Treasure	Tesouro Natal
4	飞剑	Flying Sword	Espada Voadora
4	储物袋	Storage Pouch	Bolsa de Armazenamento
4	储物戒指	Storage Ring	Anel de Armazenamento
4	乾坤袋	Qiankun Pouch	Bolsa Qiankun
4	符箓	Talisman	Talismã
4	符咒	Talisman	Talismã
4	传音符	Message Talisman	Talismã de Mensagem
4	传送符	Teleportation Talisman	Talismã de Teletransporte
4	玉简	Jade Slip	Placa de Jade
4	令牌	Token	Insígnia
4	秘籍	Secret Manual	Manual Secreto
4	剑谱	Sword Manual	Manual da Espada
4	阵盘	Formation Disk	Disco de Formação
4	阵旗	Formation Flag	Bandeira de Formação
4	宝剑	Precious Sword	Espada Preciosa
4	长剑	Longsword	Espada Longa
4	宝甲	Treasure Armor	Armadura Preciosa
4	银两	Silver Taels	Taéis de Prata
4	铜钱	Copper Coins	Moedas de Cobre
4	毒药	Poison	Veneno
4	天材地宝	Heavenly Treasures	Tesouros Celestiais
4	秘宝	Secret Treasure	Tesouro Secreto
4	至宝	Supreme Treasure	Tesouro Supremo
5	功法	Cultivation Technique	Técnica de Cultivo
5	心法	Mental Method	Método Mental
5	秘术	Secret Art	Arte Secreta
5	神通	Divine Ability	Habilidade Divina
5	武技	Martial Skill	Habilidade Marcial
5	武学	Martial Arts	Artes Marciais
5	武功	Martial Arts	Artes Marciais
5	内功	Internal Art	Arte Interna
5	外功	External Art	Arte Externa
5	轻功	Lightness Skill	Habilidade de Leveza
5	身法	Movement Technique	Técnica de Movimento
5	步法	Footwork	Trabalho de Pés
5	剑法	Swordsmanship	Esgrima
5	剑术	Sword Art	Arte da Espada
5	刀法	Blade Technique	Técnica da Lâmina
5	拳法	Fist Technique	Técnica de Punho
5	掌法	Palm Technique	Técnica de Palma
5	指法	Finger Technique	Técnica de Dedo
5	枪法	Spear Technique	Técnica de Lança
5	棍法	Staff Technique	Técnica de Bastão
5	暗器	Hidden Weapon	Arma Oculta
5	点穴	Acupoint Strike	Golpe nos Pontos Vitais
5	阵法	Formation	Formação
5	大阵	Grand Formation	Grande Formação
5	护山大阵	Mountain Protection Formation	Formação de Proteção da Montanha
5	结界	Barrier	Barreira
5	禁制	Restriction	Restrição
5	封印	Seal	Selo
5	御剑	Sword Control	Controle da Espada
5	御剑飞行	Sword Flight	Voo na Espada
5	遁术	Escape Technique	Técnica de Fuga
5	瞬移	Teleport	Teletransporte
5	分身	Clone	Clone
5	化形	Transformation	Transformação
5	炼体	Body Refining	Refino Corporal
5	双修	Dual Cultivation	Cultivo Duplo
5	夺舍	Body Seizing	Tomada de Corpo
5	招式	Move	Golpe
5	绝招	Ultimate Move	Golpe Supremo
5	杀招	Killing Move	Golpe Mortal
5	起手式	Opening Stance	Postura Inicial
5	剑阵	Sword Formation	Formação de Espadas
5	运功	Circulate Qi	Circular Qi
5	调息	Regulate Breath	Regular a Respiração
5	打坐	Meditate	Meditar
5	疗伤	Heal Wounds	Curar Ferimentos
5	护体真气	Protective Qi	Qi Protetor
5	法术	Spell	Feitiço
5	道法	Dao Art	Arte do Dao
5	仙术	Immortal Art	Arte Imortal
5	魔功	Demonic Art	Arte Demoníaca
5	邪功	Evil Art	Arte Maligna
5	秘法	Secret Technique	Técnica Secreta
6	妖兽	Demonic Beast	Fera Demoníaca
6	灵兽	Spirit Beast	Fera Espiritual
6	凶兽	Ferocious Beast	Fera Feroz
6	神兽	Divine Beast	Fera Divina
6	魔兽	Magic Beast	Fera Mágica
6	妖族	Demon Race	Raça Demoníaca
6	魔族	Devil Race	Raça dos Demônios
6	人族	Human Race	Raça Humana
6	妖怪	Monster	Monstro
6	厉鬼	Vengeful Ghost	Fantasma Vingativo
6	僵尸	Jiangshi	Jiangshi
6	真龙	True Dragon	Dragão Verdadeiro
6	凤凰	Phoenix	Fênix
6	麒麟	Qilin	Qilin
6	玄武	Black Tortoise	Tartaruga Negra
6	白虎	White Tiger	Tigre Branco
6	朱雀	Vermilion Bird	Pássaro Vermelhão
6	青龙	Azure Dragon	Dragão Azul
6	魔修	Demonic Cultivator	Cultivador Demoníaco
6	邪修	Evil Cultivator	Cultivador Maligno
6	妖王	Demon King	Rei Demônio
6	魔王	Devil King	Rei Diabo
6	秘境	Secret Realm	Reino Secreto
6	洞府	Cave Abode	Morada da Caverna
6	洞天福地	Blessed Land	Terra Abençoada
6	灵脉	Spirit Vein	Veio Espiritual
6	禁地	Forbidden Land	Terra Proibida
6	仙界	Immortal Realm	Reino Imortal
6	凡间	Mortal World	Mundo Mortal
6	人间	Human World	Mundo Humano
6	魔界	Demon Realm	Reino Demoníaco
6	冥界	Underworld	Submundo
6	九幽	Nine Netherworlds	Nove Submundos
6	天界	Heavenly Realm	Reino Celestial
6	上界	Upper Realm	Reino Superior
6	下界	Lower Realm	Reino Inferior
6	天地	Heaven and Earth	Céu e Terra
6	乾坤	Qiankun	Qiankun
6	阴阳	Yin and Yang	Yin e Yang
6	五行	Five Elements	Cinco Elementos
6	八卦	Eight Trigrams	Oito Trigramas
6	太极	Taiji	Taiji
6	红尘	Mortal World	Mundo Secular
6	轮回	Reincarnation	Reencarnação
6	劫数	Calamity	Calamidade
6	天命	Heavenly Mandate	Mandato Celestial
6	天意	Will of Heaven	Vontade do Céu";

    private static List<Item> Itens()
    {
        if (itens != null) return itens;
        itens = new List<Item>();
        foreach (string l in Dados.Split('\n'))
        {
            string[] p = l.TrimEnd('\r').Split('\t');
            if (p.Length < 4) continue;
            itens.Add(new Item { Grupo = int.Parse(p[0]), Zh = p[1], En = p[2], Pt = p[3] });
        }
        return itens;
    }

    // so ha PT-BR e EN
    public static bool TemIdioma { get { return LiveConfig.Idioma == "pt" || LiveConfig.Idioma == "en"; } }

    private static string Traducao(Item i)
    {
        return LiveConfig.Idioma == "pt" ? i.Pt : i.En;
    }

    public static string Chave(int grupo) { return "x" + grupo; }

    public static int Total(int grupo)
    {
        int n = 0;
        foreach (Item i in Itens()) if (i.Grupo == grupo) n++;
        return n;
    }

    public static string Exemplos(int grupo, int max)
    {
        List<string> l = new List<string>();
        foreach (Item i in Itens())
            if (i.Grupo == grupo && l.Count < max) l.Add(i.Zh + " " + Traducao(i));
        return string.Join("  ·  ", l);
    }

    // quantos termos do grupo estao no glossario agora (vindos do pacote)
    public static int NoGlossario(int grupo)
    {
        string k = Chave(grupo);
        int n = 0;
        foreach (TermoGlossario t in Glossario.Lista) if (t.Pacote == k) n++;
        return n;
    }

    // devolve quantos entraram; jaExistiam = os que o glossario ja tinha (ficam como estao)
    public static int Importar(ICollection<int> grupos, out int jaExistiam)
    {
        jaExistiam = 0;
        int n = 0;
        foreach (Item i in Itens())
        {
            if (!grupos.Contains(i.Grupo)) continue;
            if (Glossario.Obter(i.Zh) != null) { jaExistiam++; continue; }
            Glossario.Definir(i.Zh, Traducao(i), catGrupo[i.Grupo], false);
            TermoGlossario t = Glossario.Obter(i.Zh);
            if (t != null) { t.Pacote = Chave(i.Grupo); n++; }
        }
        if (n > 0) Glossario.Marcar();
        return n;
    }

    // devolve quantos sairam; ficam = os do pacote que o jogador travou ou editou
    public static int Remover(ICollection<int> grupos, out int ficam)
    {
        ficam = 0;
        Dictionary<string, Item> porZh = new Dictionary<string, Item>();
        foreach (Item i in Itens()) porZh[i.Zh] = i;
        List<TermoGlossario> sair = new List<TermoGlossario>();
        foreach (TermoGlossario t in Glossario.Lista)
        {
            if (string.IsNullOrEmpty(t.Pacote)) continue;
            Item i;
            if (!porZh.TryGetValue(t.Original, out i) || !grupos.Contains(i.Grupo)) continue;
            if (t.Travado || t.Traducao != Traducao(i)) { ficam++; continue; }
            sair.Add(t);
        }
        for (int k = 0; k < sair.Count; k++) Glossario.Remover(sair[k]);
        return sair.Count;
    }
}
