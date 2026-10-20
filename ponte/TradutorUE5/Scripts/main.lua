-- Tradutor UE5: ponte dentro do jogo (mod Lua do UE4SS).
-- Vigia os textos da tela (TextBlock e RichTextBlock), manda cada texto novo para o cerebro
-- (TradutorCerebro.exe) e troca pela traducao quando ela chega. F8 abre/fecha o menu do cerebro.
--
-- Conversa com o cerebro por dois arquivos na pasta "troca" (ver cerebro/Ponte.cs):
--   pedidos.txt   (ponte -> cerebro)  T<TAB>texto<TAB>pista | M (menu) | I (jogo abriu)
--   respostas.txt (cerebro -> ponte)  R<TAB>texto<TAB>traducao | N<TAB>texto | X (devolver originais)
--                                     C<TAB>encolher(0/1)<TAB>minimo% (config da ponte, vem do menu)

---------------- ajustes ----------------
local INTERVALO_MS = 500          -- de quanto em quanto tempo olha a tela
-- varrer a tela enquanto o jogo carrega derruba o jogo (objetos pela metade). Por isso a ponte so comeca
-- depois que o jogador entra (PlayerController:ClientRestart) e para enquanto um mapa carrega.
local ESPERA_INICIO_MS = 5000     -- espera depois que o jogador entrou
local ESPERA_MAX_MS = 60000       -- comeca mesmo assim se o jogo nunca avisar
local ESPERA_MAPA_MS = 3000       -- espera depois que um mapa terminou de carregar
local TECLA_MENU = Key.F8
local ABRIR_CEREBRO = true        -- abre o TradutorCerebro.exe junto com o jogo
local CLASSES = { "TextBlock", "RichTextBlock" }
-- telas de carregamento do jogo: enquanto uma delas estiver na tela, a ponte nao mexe em nada
-- (o mundo esta sendo montado e ler widgets pela metade derruba o jogo)
local TELAS_CARREGANDO = { "WBP_LoadingScreen_C" }
local INTERVALO_CONFERIR_MS = 150 -- de quanto em quanto tempo confere os textos que ja traduziu
local GANCHO_TEXTO = true         -- gancho no SetText: o jogo trocou um texto (hover...) -> confere ja no proximo ciclo

---------------- pastas ----------------
local function PastaDoMod()
    -- 1) caminho deste arquivo (...\Mods\TradutorUE5\Scripts\main.lua)
    local ok, info = pcall(debug.getinfo, 1, "S")
    if ok and info and info.source then
        local p = info.source:gsub("^@", ""):gsub("/", "\\")
        local pasta = p:match("^(.*)\\Scripts\\[^\\]+$")
        if pasta then return pasta end
    end
    -- 2) package.path que o UE4SS monta com a pasta Scripts do mod
    for parte in string.gmatch(package.path or "", "[^;]+") do
        local pasta = parte:gsub("/", "\\"):match("^(.*\\TradutorUE5)\\Scripts\\")
        if pasta then return pasta end
    end
    -- 3) relativo a Binaries\Win64 (UE4SS 3.x e versoes antigas)
    for _, p in ipairs({ "ue4ss\\Mods\\TradutorUE5", "Mods\\TradutorUE5" }) do
        local f = io.open(p .. "\\Scripts\\main.lua", "rb")
        if f then f:close(); return p end
    end
    return "ue4ss\\Mods\\TradutorUE5"
end

local MOD = PastaDoMod()
local TROCA = MOD .. "\\troca"
local ARQ_PEDIDOS = TROCA .. "\\pedidos.txt"
local ARQ_RESPOSTAS = TROCA .. "\\respostas.txt"
local CEREBRO = MOD .. "\\cerebro\\TradutorCerebro.exe"

local function Log(s)
    print("[TradutorUE5] " .. tostring(s) .. "\n")
end

---------------- escape (igual ao do cerebro: \\ \t \n \r) ----------------
local function Escapar(s)
    return (s:gsub("\\", "\\\\"):gsub("\t", "\\t"):gsub("\n", "\\n"):gsub("\r", "\\r"))
end

local volta = { n = "\n", t = "\t", r = "\r" }
local function Desescapar(s)
    return (s:gsub("\\(.)", function(c) return volta[c] or c end))
end

---------------- estado ----------------
-- IMPORTANTE: nenhum widget fica guardado de um quadro para outro. O jogo destroi widgets a toda hora
-- (troca de tela) e ler um widget destruido derruba o jogo. Tudo e guardado pelo endereco, e o widget
-- e sempre pego de novo na lista viva (FindAllOf).
local trad = {}       -- original -> traducao (false = o cerebro disse para nao traduzir)
local pedidos = {}    -- original -> true (ja mandado, esperando)
local nossas = {}     -- traducao -> original (texto que a ponte colocou na tela)
local aplicados = {}  -- endereco do widget -> { o = original, t = traducao, tam = fonte original, atual, rico }
local encolher = false   -- encolher a fonte do texto traduzido maior que o original (menu do cerebro)
local encMin = 0.6
local refazerFonte = false
local devolver = false   -- o cerebro mandou devolver os originais (feito no proximo ciclo, na lista viva)
local mexeu = false      -- o gancho do SetText viu o jogo trocar algum texto
local saida = {}      -- linhas para pedidos.txt
local lidoResp = 0
local naTela = {}     -- endereco do widget -> true (widget de verdade) | false (molde: Default__ ou template)
local proxLimpeza = 0
local primeiraTroca = true
local liberadoEm = nil   -- os.clock() a partir do qual pode varrer (nil = ainda nao)
local inicio = os.clock()

local function Enviar(linha)
    saida[#saida + 1] = linha .. "\n"
end

local function Gravar()
    if #saida == 0 then return end
    local f = io.open(ARQ_PEDIDOS, "ab")
    if not f then return end
    f:write(table.concat(saida))
    f:close()
    saida = {}
end

local function Endereco(w)
    local ok, e = pcall(function() return w:GetAddress() end)
    if ok then return e end
    return nil
end

local function Pista(w)
    local ok, nome = pcall(function() return w:GetFullName() end)
    return ok and nome or ""
end

---------------- fonte (encolher o texto que nao cabe) ----------------
-- largura aproximada: chines/japones/coreano = 1, espaco = 0.3, resto = 0.55 (tags <...> nao contam)
local function Largura(s)
    s = s:gsub("<[^>]*>", "")
    local l = 0
    for _, c in utf8.codes(s) do
        if c >= 0x2E80 then l = l + 1
        elseif c == 32 then l = l + 0.3
        else l = l + 0.55 end
    end
    return l
end

local avisouFonte = false
local function MudarFonte(w, tamanho)
    local ok, err = pcall(function()
        local f = w.Font
        f.Size = tamanho
        w:SetFont(f)
    end)
    if not avisouFonte then
        avisouFonte = true
        Log("encolher fonte: " .. (ok and "ok" or ("falhou: " .. tostring(err))))
    end
    return ok
end

-- so a familia TextBlock (o RichTextBlock tira a fonte dos estilos). w = widget vivo agora.
local function Encolher(w, a)
    if a.rico then return end
    local alvo
    if not a.tam then
        if not encolher then return end
        local ok, t = pcall(function() return w.Font.Size end)
        if not ok or not t or t <= 0 then return end
        a.tam = t
    end
    if encolher then
        local ok1, lo = pcall(Largura, a.o)
        local ok2, lt = pcall(Largura, a.t)
        if not ok1 or not ok2 then return end
        local fator = 1
        if lt > lo and lt > 0 then fator = math.max(encMin, lo / lt) end
        alvo = math.floor(a.tam * fator + 0.5)
    else
        alvo = a.tam
    end
    if alvo ~= (a.atual or a.tam) then
        if MudarFonte(w, alvo) then a.atual = alvo end
    end
end

---------------- respostas do cerebro ----------------
local function Linha(l)
    local tipo, a, b = l:match("^([^\t]*)\t?([^\t]*)\t?(.*)$")
    if tipo == "R" then
        local o, t = Desescapar(a), Desescapar(b)
        trad[o] = t
        nossas[t] = o
        pedidos[o] = nil
    elseif tipo == "N" then
        local o = Desescapar(a)
        trad[o] = false
        pedidos[o] = nil
    elseif tipo == "X" then
        devolver = true
    elseif tipo == "C" then
        local novoEnc = a == "1"
        local novoMin = math.max(0.4, math.min(0.95, (tonumber(b) or 60) / 100))
        if novoEnc ~= encolher or novoMin ~= encMin then
            encolher, encMin = novoEnc, novoMin
            refazerFonte = true
            Log("encolher fonte: " .. (encolher and "ligado" or "desligado") .. " (minimo " .. math.floor(encMin * 100) .. "%)")
        end
    end
end

local function LerRespostas()
    local f = io.open(ARQ_RESPOSTAS, "rb")
    if not f then return end
    local tam = f:seek("end")
    if tam < lidoResp then lidoResp = 0 end
    if tam > lidoResp then
        f:seek("set", lidoResp)
        local dados = f:read(tam - lidoResp) or ""
        -- so ate a ultima quebra de linha (o resto ainda esta sendo escrito)
        local fim = dados:match(".*()\n")
        if fim then
            lidoResp = lidoResp + fim
            for l in dados:sub(1, fim):gmatch("([^\n]*)\n") do
                l = l:gsub("\r$", "")
                if l ~= "" then pcall(Linha, l) end
            end
        end
    end
    f:close()
end

---------------- vigiar a tela ----------------
-- Marcas da Unreal de objeto que nao esta pronto: molde (CDO/archetype), ainda carregando ou sendo
-- destruido. Ler um objeto assim derruba o jogo sem o pcall pegar. Os valores vem das tabelas do
-- UE4SS (EObjectFlags / EInternalObjectFlags); se faltarem, usa os numeros da Unreal 5.
local function Mascara(tabela, nomes, reserva)
    local m = 0
    local ok = pcall(function()
        local t = _G[tabela]
        for _, n in ipairs(nomes) do
            local v = t and t[n]
            if type(v) == "number" then m = m | v end
        end
    end)
    if not ok or m == 0 then m = reserva end
    return m
end
local FLAGS_RUINS = Mascara("EObjectFlags", { "RF_ClassDefaultObject", "RF_ArchetypeObject", "RF_NeedInitialization",
    "RF_NeedLoad", "RF_NeedPostLoad", "RF_NeedPostLoadSubobjects", "RF_BeginDestroyed", "RF_FinishDestroyed" },
    0x10 | 0x20 | 0x200 | 0x400 | 0x1000 | 0x2000 | 0x8000 | 0x10000)
local INTERNAS_RUINS = Mascara("EInternalObjectFlags", { "Unreachable", "Garbage", "PendingKill", "Async",
    "AsyncLoading", "PendingConstruction" }, 0)

local function Pronto(w)
    local ok, ruim = pcall(function()
        if w:HasAnyFlags(FLAGS_RUINS) then return true end
        if INTERNAS_RUINS ~= 0 and w:HasAnyInternalFlags(INTERNAS_RUINS) then return true end
        return false
    end)
    -- se o UE4SS nao tiver essas funcoes, segue com as outras protecoes
    return not ok or not ruim
end

local function Carregando()
    for _, nome in ipairs(TELAS_CARREGANDO) do
        local ok, sim = pcall(function()
            local t = FindFirstOf(nome)
            return t ~= nil and t:IsValid() and t:IsInViewport()
        end)
        if ok and sim then return true end
    end
    return false
end

-- FindAllOf tambem traz os moldes (Default__ e os templates dentro do WidgetBlueprint, "..._C:WidgetTree.").
-- Eles nao estao na tela, e mexer neles pode derrubar o jogo sem o pcall pegar.
local function EhDeVerdade(w, endereco)
    local r = naTela[endereco]
    if r ~= nil then return r end
    local nome = Pista(w)
    r = nome ~= "" and not nome:find("Default__", 1, true) and not nome:find("_C:WidgetTree", 1, true)
    naTela[endereco] = r
    return r
end

local function Texto(w)
    local ok, txt = pcall(function() return w.Text:ToString() end)
    if ok then return txt end
    return nil
end

-- w = widget vivo (veio do FindAllOf agora), rico = e RichTextBlock
local function Processar(w, endereco, rico)
    if not w:IsValid() or not Pronto(w) or not EhDeVerdade(w, endereco) then return end
    local txt = Texto(w)
    if txt == nil or txt == "" then return end
    local a = aplicados[endereco]
    if nossas[txt] then
        if a and refazerFonte then Encolher(w, a) end
        return
    end
    local t = trad[txt]
    if t == nil then
        if not pedidos[txt] then
            pedidos[txt] = true
            Enviar("T\t" .. Escapar(txt) .. "\t" .. Escapar(Pista(w)))
        end
    elseif t ~= false then
        if primeiraTroca then Log("primeira troca de texto: " .. Pista(w)) end
        local okSet = pcall(function() w:SetText(FText(t)) end)
        if primeiraTroca then
            primeiraTroca = false
            Log("primeira troca " .. (okSet and "ok" or "falhou"))
        end
        if okSet then
            -- o mesmo widget pode trocar de texto (aba, hover): a fonte original (tam) continua valendo
            if not a then
                a = { rico = rico }
                aplicados[endereco] = a
            end
            a.o, a.t = txt, t
            Encolher(w, a)
        end
    end
end

-- passa por todos os textos vivos. soAplicados = so os que a ponte ja traduziu (conferencia rapida)
local function Passar(soAplicados)
    for _, cls in ipairs(CLASSES) do
        local rico = cls == "RichTextBlock"
        local lista = FindAllOf(cls)
        if lista then
            for _, w in ipairs(lista) do
                local e = Endereco(w)
                if e and (not soAplicados or aplicados[e]) then
                    local ok, erro = pcall(Processar, w, e, rico)
                    if not ok then Log("erro num texto: " .. tostring(erro)) end
                end
            end
        end
    end
    refazerFonte = false
end

-- o cerebro pediu os originais de volta (pausou, limpou o cache...): so nos widgets vivos
local function DevolverOriginais()
    devolver = false
    local n = 0
    for _, cls in ipairs(CLASSES) do
        local lista = FindAllOf(cls)
        if lista then
            for _, w in ipairs(lista) do
                local e = Endereco(w)
                local a = e and aplicados[e]
                if a then
                    pcall(function()
                        if w:IsValid() and Texto(w) == a.t then
                            w:SetText(FText(a.o))
                            if a.tam and a.atual and a.atual ~= a.tam then MudarFonte(w, a.tam) end
                            n = n + 1
                        end
                    end)
                end
            end
        end
    end
    trad, pedidos, nossas, aplicados = {}, {}, {}, {}
    Log("originais devolvidos em " .. n .. " textos")
end

local function Varrer()
    -- enderecos sao reaproveitados quando o jogo apaga widgets: esquece a lista de vez em quando
    local agora = os.clock()
    if agora >= proxLimpeza then
        naTela = {}
        proxLimpeza = agora + 30
    end
    Passar(false)
end

local function Liberar(ms)
    liberadoEm = os.clock() + ms / 1000
end

local avisouVarrendo = false
local avisouCarregando = false
local function PodeVarrer()
    local agora = os.clock()
    if Carregando() then
        if not avisouCarregando then avisouCarregando = true; Log("tela de carregamento: pausado") end
        return false
    elseif avisouCarregando then
        avisouCarregando = false
        Log("tela de carregamento saiu")
        Liberar(ESPERA_MAPA_MS)
        return false
    end
    if liberadoEm == nil and agora - inicio >= ESPERA_MAX_MS / 1000 then
        Log("o jogo nao avisou que o jogador entrou; comecando assim mesmo")
        liberadoEm = agora
    end
    if liberadoEm == nil or agora < liberadoEm then return false end
    if not avisouVarrendo then
        avisouVarrendo = true
        Log("comecando a vigiar a tela")
    end
    return true
end

-- ciclo normal (INTERVALO_MS): respostas do cerebro, a tela toda, pedidos
local function Tick()
    local ok, erro = pcall(LerRespostas)
    if not ok then Log("erro lendo respostas: " .. tostring(erro)) end
    if PodeVarrer() then
        if devolver then
            ok, erro = pcall(DevolverOriginais)
            if not ok then Log("erro devolvendo originais: " .. tostring(erro)) end
        end
        ok, erro = pcall(Varrer)
        if not ok then Log("erro vigiando a tela: " .. tostring(erro)) end
    end
    ok, erro = pcall(Gravar)
    if not ok then Log("erro gravando pedidos: " .. tostring(erro)) end
end

-- ciclo rapido (INTERVALO_CONFERIR_MS): so os textos ja traduzidos, e so quando o gancho viu o jogo
-- trocar algum texto (ou para aplicar a fonte nova)
local function Conferir()
    if not (mexeu or refazerFonte) or devolver or not PodeVarrer() then return end
    mexeu = false
    local ok, erro = pcall(Passar, not refazerFonte)
    if not ok then Log("erro conferindo: " .. tostring(erro)) end
end

local function AbrirMenu()
    Enviar("M")
    pcall(Gravar)
end

---------------- inicio ----------------
local function Iniciar()
    -- jogo abriu: zera a conversa
    local f = io.open(ARQ_RESPOSTAS, "wb")
    if not f then
        os.execute('mkdir "' .. TROCA .. '" 2>nul')
        f = io.open(ARQ_RESPOSTAS, "wb")
    end
    if f then f:close() end
    f = io.open(ARQ_PEDIDOS, "wb")
    if f then
        f:write("I\n")
        f:close()
    else
        Log("nao consegui criar " .. ARQ_PEDIDOS)
    end
    if ABRIR_CEREBRO then
        local exe = io.open(CEREBRO, "rb")
        if exe then
            exe:close()
            os.execute('start "" "' .. CEREBRO .. '"')
        else
            Log("cerebro nao encontrado: " .. CEREBRO)
        end
    end
    Log("pronto. Pasta do mod: " .. MOD .. " | F8 abre o menu")
end

Iniciar()

local okGancho = pcall(RegisterHook, "/Script/Engine.PlayerController:ClientRestart", function()
    if liberadoEm == nil then Log("jogador entrou") end
    Liberar(ESPERA_INICIO_MS)
end)
if not okGancho then Log("sem gancho ClientRestart; espera " .. (ESPERA_MAX_MS / 1000) .. " s") end

-- troca de mapa: para de varrer ate o mapa novo terminar de carregar
local okMapa1 = pcall(RegisterLoadMapPreHook, function()
    liberadoEm = nil
    inicio = os.clock()
    Log("carregando mapa: pausado")
end)
local okMapa2 = pcall(RegisterLoadMapPostHook, function()
    Liberar(ESPERA_MAPA_MS)
    Log("mapa carregado: volta em " .. (ESPERA_MAPA_MS / 1000) .. " s")
end)
if not (okMapa1 and okMapa2) then Log("sem gancho de troca de mapa") end

-- o jogo trocou o texto de algum widget (hover, troca de aba...): so marca. Quem troca e o Conferir,
-- no proximo ciclo rapido, pela lista viva. (O gancho nao guarda o widget nem cria funcao nova.)
local function NoSetText()
    mexeu = true
end
if GANCHO_TEXTO then
    for _, cls in ipairs(CLASSES) do
        local ok = pcall(RegisterHook, "/Script/UMG." .. cls .. ":SetText", NoSetText)
        Log("gancho " .. cls .. ":SetText " .. (ok and "ok" or "nao deu"))
    end
end

RegisterKeyBind(TECLA_MENU, function()
    ExecuteInGameThread(AbrirMenu)
end)

LoopAsync(INTERVALO_CONFERIR_MS, function()
    ExecuteInGameThread(Conferir)
    return false
end)

LoopAsync(INTERVALO_MS, function()
    ExecuteInGameThread(Tick)
    return false
end)
