-- Tradutor UE5: ponte dentro do jogo (mod Lua do UE4SS).
-- Vigia os textos da tela (TextBlock e RichTextBlock), manda cada texto novo para o cerebro
-- (TradutorCerebro.exe) e troca pela traducao quando ela chega. F8 abre/fecha o menu do cerebro.
--
-- Conversa com o cerebro por dois arquivos na pasta "troca" (ver cerebro/Ponte.cs):
--   pedidos.txt   (ponte -> cerebro)  T<TAB>texto<TAB>pista | L<TAB>texto<TAB>lugar | M (menu) | I (jogo abriu)
--                                     Q<TAB>lugar<TAB>n (o lugar ja mostrou n textos ao mesmo tempo: lista)
--   respostas.txt (cerebro -> ponte)  R<TAB>texto<TAB>traducao | N<TAB>texto | X (devolver originais)
--                                     C<TAB>lugar<TAB>texto<TAB>abreviacao | K (esquecer as abreviacoes)
-- Abreviacoes mudaram: o cerebro manda K + todas as C de novo, e a ponte troca so os textos afetados
-- (sem devolver os originais nem pedir tudo de novo).
-- lugar = tela / nome do texto (ex.: "attritem / txt_name"). Nos lugares que o jogador marcou como apertados
-- o cerebro manda a abreviacao (C) e ela vai no lugar da traducao inteira; nos outros (ex.: a dica do mouse)
-- fica a traducao inteira.

---------------- ajustes ----------------
local INTERVALO_MS = 500          -- de quanto em quanto tempo olha a tela
-- varrer a tela enquanto o jogo carrega derruba o jogo (objetos pela metade). Por isso a ponte so comeca
-- depois que o jogador entra (PlayerController:ClientRestart) e para enquanto um mapa carrega.
local ESPERA_INICIO_MS = 5000     -- espera depois que o jogador entrou
local ESPERA_MAX_MS = 60000       -- comeca mesmo assim se o jogo nunca avisar
local ESPERA_MAPA_MS = 3000       -- espera depois que um mapa terminou de carregar
local ESPERA_VIDEO_MS = 6000      -- espera depois de aplicar configuracoes de video (o jogo refaz as telas)
local TECLA_MENU = Key.F8
local ABRIR_CEREBRO = true        -- abre o TradutorCerebro.exe junto com o jogo
local CLASSES = { "TextBlock", "RichTextBlock" }
-- telas de carregamento do jogo: os textos delas ficam como estao (aparecem enquanto o mundo e montado)
local TELAS_CARREGANDO = { "WBP_LoadingScreen_C" }
-- ESTABILIDADE: a ponte so le a tela a cada INTERVALO_MS e usa o minimo de funcoes do UE4SS (IsValid,
-- GetAddress, GetFullName, Text, SetText). Nas versoes que liam mais vezes ou usavam mais funcoes
-- (gancho no SetText, conferencia a cada 150 ms, marcas internas, fonte) o UE4SS beta derrubava o jogo.

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
local aplicados = {}  -- endereco do widget -> { o = original, t = traducao }
local curtas = {}     -- lugar -> { original -> abreviacao }
local ehCurta = {}    -- abreviacao -> true (para voltar a traducao inteira quando a tela deixa de ser apertada)
local avisados = {}   -- lugar .. "\1" .. original -> true (cerebro ja sabe que o texto aparece ali)
local nesta = {}      -- lugar -> quantos textos dele nesta passada
local maximo = {}     -- lugar -> maior quantidade ja avisada ao cerebro
local devolver = false   -- o cerebro mandou devolver os originais (feito no proximo ciclo, na lista viva)
local saida = {}      -- linhas para pedidos.txt
local lidoResp = 0
local naTela = {}     -- endereco do widget -> lugar (widget de verdade) | false (molde: Default__ ou template)
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
    elseif tipo == "C" then
        local lugar, o, c = l:match("^C\t([^\t]*)\t([^\t]*)\t(.*)$")
        if lugar and c ~= "" then
            o, c = Desescapar(o), Desescapar(c)
            lugar = Desescapar(lugar)
            curtas[lugar] = curtas[lugar] or {}
            curtas[lugar][o] = c
            nossas[c] = o
            ehCurta[c] = true
        end
    elseif tipo == "K" then
        curtas = {}
    elseif tipo == "X" then
        devolver = true
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
-- textos da propria tela de carregamento ficam como estao (ela aparece enquanto o mundo e montado)
local function DaTelaDeCarregamento(pista)
    for _, nome in ipairs(TELAS_CARREGANDO) do
        if pista:find(nome, 1, true) then return true end
    end
    return false
end

-- lugar do texto, tirado do nome completo (igual ao "Onde" do cerebro, Glossario.PartesPista):
-- "...wbp_attritem_c_3.widgettree.txt_name" -> "attritem / txt_name"
local PREFIXOS = { "wbp_", "wb_", "w_", "ui_", "umg_", "bp_" }
local function Lugar(nome)
    local p = nome:lower()
    local esp = p:find(" ", 1, true)
    if esp then p = p:sub(esp + 1) end
    local seg = {}
    for s in p:gmatch("[^%.:/]+") do seg[#seg + 1] = s end
    if #seg == 0 then return "" end
    local widget = seg[#seg]
    if widget == "widgettree" or widget:sub(-2) == "_c" then widget = "" end
    local dono = ""
    for i = #seg, 1, -1 do
        local d = seg[i]:match("^(.+)_c_%d+$") or seg[i]:match("^(.+)_c$")
        if d then
            for _, pre in ipairs(PREFIXOS) do
                if d:sub(1, #pre) == pre then d = d:sub(#pre + 1); break end
            end
            -- bs_secondtitle1 e bs_secondtitle2 sao a mesma tela repetida: fica "bs_secondtitle"
            d = d:gsub("%d+$", "")
            dono = d
            break
        end
    end
    if dono == "" then return widget end
    if widget == "" then return dono end
    return dono .. " / " .. widget
end

-- FindAllOf tambem traz os moldes (Default__ e os templates dentro do WidgetBlueprint, "..._C:WidgetTree.").
-- Eles nao estao na tela, e mexer neles pode derrubar o jogo sem o pcall pegar.
local function EhDeVerdade(w, endereco)
    local r = naTela[endereco]
    if r ~= nil then return r end
    local nome = Pista(w)
    if nome ~= "" and not nome:find("Default__", 1, true) and not nome:find("_C:WidgetTree", 1, true)
        and not DaTelaDeCarregamento(nome) then
        r = Lugar(nome)
    else
        r = false
    end
    naTela[endereco] = r
    return r
end

local function Texto(w)
    local ok, txt = pcall(function() return w.Text:ToString() end)
    if ok then return txt end
    return nil
end

-- conta ao cerebro em que lugares cada texto curto aparece (uma vez por lugar): e assim que o menu
-- mostra as telas onde da para usar a abreviacao
local function Avisar(o, lugar)
    if lugar == "" or #o > 48 then return end
    local k = lugar .. "\1" .. o
    if avisados[k] then return end
    avisados[k] = true
    Enviar("L\t" .. Escapar(o) .. "\t" .. Escapar(lugar))
end

-- w = widget vivo (veio do FindAllOf agora)
local function Processar(w, endereco)
    if not w:IsValid() then return end
    local lugar = EhDeVerdade(w, endereco)
    if not lugar then return end
    local txt = Texto(w)
    if txt == nil or txt == "" then return end
    if lugar ~= "" then nesta[lugar] = (nesta[lugar] or 0) + 1 end
    local o = nossas[txt]
    if o then
        Avisar(o, lugar)
        -- texto ja e nosso: confere se este lugar quer a abreviacao ou a traducao inteira
        -- (so escreve na tela quando a abreviacao entra ou sai; o resto fica como esta)
        local t = trad[o]
        if t then
            local c = curtas[lugar] and curtas[lugar][o]
            local quer = nil
            if c and txt ~= c then quer = c
            elseif not c and ehCurta[txt] and txt ~= t then quer = t end
            if quer and pcall(function() w:SetText(FText(quer)) end) then
                aplicados[endereco] = { o = o, t = quer }
            end
        end
        return
    end
    local t = trad[txt]
    if t == nil then
        if not pedidos[txt] then
            pedidos[txt] = true
            Enviar("T\t" .. Escapar(txt) .. "\t" .. Escapar(Pista(w)))
        end
    elseif t ~= false then
        Avisar(txt, lugar)
        local c = curtas[lugar] and curtas[lugar][txt]
        if c then t = c end
        if primeiraTroca then Log("primeira troca de texto: " .. Pista(w)) end
        local okSet = pcall(function() w:SetText(FText(t)) end)
        if primeiraTroca then
            primeiraTroca = false
            Log("primeira troca " .. (okSet and "ok" or "falhou"))
        end
        if okSet then aplicados[endereco] = { o = txt, t = t } end
    end
end

-- passa por todos os textos vivos
local function Passar()
    nesta = {}
    for _, cls in ipairs(CLASSES) do
        local lista = FindAllOf(cls)
        if lista then
            for _, w in ipairs(lista) do
                local e = Endereco(w)
                if e then
                    local ok, erro = pcall(Processar, w, e)
                    if not ok then Log("erro num texto: " .. tostring(erro)) end
                end
            end
        end
    end
    -- lugar que mostra varios textos juntos e lista (atributos, clas...); a dica do mouse mostra um so
    for lugar, n in pairs(nesta) do
        if n > (maximo[lugar] or 0) and (maximo[lugar] or 0) < 50 then
            maximo[lugar] = n
            Enviar("Q\t" .. Escapar(lugar) .. "\t" .. n)
        end
    end
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
                            n = n + 1
                        end
                    end)
                end
            end
        end
    end
    trad, pedidos, nossas, aplicados, curtas = {}, {}, {}, {}, {}
    Log("originais devolvidos em " .. n .. " textos")
end

local function Varrer()
    -- enderecos sao reaproveitados quando o jogo apaga widgets: esquece a lista de vez em quando
    local agora = os.clock()
    if agora >= proxLimpeza then
        naTela = {}
        proxLimpeza = agora + 30
    end
    Passar()
end

local function Liberar(ms)
    liberadoEm = os.clock() + ms / 1000
end

local avisouVarrendo = false
local function PodeVarrer()
    local agora = os.clock()
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

-- configuracoes de video aplicadas: o jogo refaz as telas (mudou a resolucao...). Ler um texto no meio disso
-- derrubou o jogo, entao a ponte para um pouco.
local function PausaVideo()
    if liberadoEm ~= nil then
        Liberar(ESPERA_VIDEO_MS)
        Log("configuracoes de video aplicadas: pausado " .. (ESPERA_VIDEO_MS / 1000) .. " s")
    end
end
for _, f in ipairs({ "ApplySettings", "ApplyResolutionSettings", "ApplyNonResolutionSettings", "SetScreenResolution", "SetFullscreenMode" }) do
    pcall(RegisterHook, "/Script/Engine.GameUserSettings:" .. f, PausaVideo)
end

RegisterKeyBind(TECLA_MENU, function()
    ExecuteInGameThread(AbrirMenu)
end)

LoopAsync(INTERVALO_MS, function()
    ExecuteInGameThread(Tick)
    return false
end)
