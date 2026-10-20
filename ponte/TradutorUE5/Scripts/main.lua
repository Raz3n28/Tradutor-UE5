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
local INTERVALO_CONFERIR_MS = 100 -- de quanto em quanto tempo confere os textos que ja traduziu
local GANCHO_TEXTO = true         -- gancho no SetText: o jogo trocou o texto (hover...) -> traduz no quadro seguinte

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
local trad = {}       -- original -> traducao (false = o cerebro disse para nao traduzir)
local pedidos = {}    -- original -> true (ja mandado, esperando)
local nossas = {}     -- traducao -> original (texto que a ponte colocou na tela)
local aplicados = {}  -- endereco do widget -> { w = widget, o = original, t = traducao, tam = fonte original }
local ordem = {}      -- enderecos de aplicados, para conferir aos poucos
local cursor = 1
local encolher = false   -- encolher a fonte do texto traduzido maior que o original (menu do cerebro)
local encMin = 0.6
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

---------------- respostas do cerebro ----------------
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
local function MudarFonte(a, tamanho)
    local ok, err = pcall(function()
        local f = a.w.Font
        f.Size = tamanho
        a.w:SetFont(f)
    end)
    if not avisouFonte then
        avisouFonte = true
        Log("encolher fonte: " .. (ok and "ok" or ("falhou: " .. tostring(err))))
    end
    return ok
end

-- so TextBlock (o RichTextBlock tira a fonte dos estilos)
local function Encolher(a)
    if not a.textblock then return end
    if not encolher then
        if a.tam and a.mudou then MudarFonte(a, a.tam); a.mudou = false end
        return
    end
    if not a.tam then
        local ok, t = pcall(function() return a.w.Font.Size end)
        if not ok or not t or t <= 0 then return end
        a.tam = t
    end
    local ok1, lo = pcall(Largura, a.o)
    local ok2, lt = pcall(Largura, a.t)
    if not ok1 or not ok2 then return end
    local fator = 1
    if lt > lo and lt > 0 then fator = math.max(encMin, lo / lt) end
    local novo = math.floor(a.tam * fator + 0.5)
    if novo ~= (a.atual or a.tam) then
        if MudarFonte(a, novo) then a.atual = novo; a.mudou = novo ~= a.tam end
    end
end

local function DevolverOriginais()
    local n = 0
    for _, a in pairs(aplicados) do
        pcall(function()
            if a.w:IsValid() then
                a.w:SetText(FText(a.o))
                if a.mudou and a.tam then MudarFonte(a, a.tam) end
                n = n + 1
            end
        end)
    end
    trad, pedidos, nossas, aplicados, ordem, cursor = {}, {}, {}, {}, {}, 1
    Log("originais devolvidos em " .. n .. " textos")
end

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
        DevolverOriginais()
    elseif tipo == "C" then
        local novoEnc = a == "1"
        encMin = math.max(0.4, math.min(0.95, (tonumber(b) or 60) / 100))
        if novoEnc ~= encolher then
            encolher = novoEnc
            Log("encolher fonte: " .. (encolher and "ligado" or "desligado"))
            -- aplica (ou desfaz) nos textos ja traduzidos
            for _, ap in pairs(aplicados) do pcall(function() if ap.w:IsValid() then Encolher(ap) end end) end
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
local function Pista(w)
    local ok, nome = pcall(function() return w:GetFullName() end)
    return ok and nome or ""
end

-- FindAllOf tambem traz os moldes (Default__ e os templates dentro do WidgetBlueprint, "..._C:WidgetTree.").
-- Eles nao estao na tela, e mexer neles pode derrubar o jogo sem o pcall pegar.
local function EhDeVerdade(w)
    local okEnd, endereco = pcall(function() return w:GetAddress() end)
    if not okEnd or not endereco then return false end
    local r = naTela[endereco]
    if r ~= nil then return r end
    local nome = Pista(w)
    r = nome ~= "" and not nome:find("Default__", 1, true) and not nome:find("_C:WidgetTree", 1, true)
    naTela[endereco] = r
    return r
end

local function Processar(w)
    if not w:IsValid() then return end
    if not EhDeVerdade(w) then return end
    local ok, txt = pcall(function() return w.Text:ToString() end)
    if not ok or txt == nil or txt == "" then return end
    if nossas[txt] then return end
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
            local okEnd, endereco = pcall(function() return w:GetAddress() end)
            if okEnd and endereco then
                local a = aplicados[endereco]
                if not a or a.w ~= w then
                    if a and a.tam and a.mudou then pcall(MudarFonte, a, a.tam) end
                    a = { w = w, textblock = Pista(w):sub(1, 10) == "TextBlock " }
                    if not aplicados[endereco] then ordem[#ordem + 1] = endereco end
                    aplicados[endereco] = a
                end
                a.o, a.t = txt, t
                Encolher(a)
            end
        end
    end
end

-- Confere aos poucos os textos que a ponte ja traduziu: se o jogo escreveu o original de novo
-- (passar o mouse por cima de um botao, por exemplo), troca na hora. No maximo 150 por vez.
local function Conferir()
    local n = #ordem
    if n == 0 then return end
    local passos = math.min(n, 150)
    for _ = 1, passos do
        if cursor > #ordem then cursor = 1 end
        local endereco = ordem[cursor]
        local a = aplicados[endereco]
        local valido = false
        if a then
            local ok, txt = pcall(function()
                if not a.w:IsValid() then return nil end
                return a.w.Text:ToString()
            end)
            if ok and txt ~= nil then
                valido = true
                if txt ~= a.t then Processar(a.w) end
            end
        end
        if valido then
            cursor = cursor + 1
        else
            -- widget sumiu: tira da lista (o ultimo vem para o lugar dele)
            aplicados[endereco] = nil
            ordem[cursor] = ordem[#ordem]
            ordem[#ordem] = nil
        end
    end
end

local function Varrer()
    -- enderecos sao reaproveitados quando o jogo apaga widgets: esquece a lista de vez em quando
    local agora = os.clock()
    if agora >= proxLimpeza then
        naTela = {}
        proxLimpeza = agora + 30
    end
    for _, cls in ipairs(CLASSES) do
        local lista = FindAllOf(cls)
        if lista then
            for _, w in ipairs(lista) do
                Processar(w)
            end
        end
    end
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

local function Tick()
    local ok, erro = pcall(LerRespostas)
    if not ok then Log("erro lendo respostas: " .. tostring(erro)) end
    if PodeVarrer() then
        ok, erro = pcall(Varrer)
        if not ok then Log("erro vigiando a tela: " .. tostring(erro)) end
    end
    ok, erro = pcall(Gravar)
    if not ok then Log("erro gravando pedidos: " .. tostring(erro)) end
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
pcall(RegisterLoadMapPreHook, function() liberadoEm = nil; inicio = os.clock() end)
pcall(RegisterLoadMapPostHook, function() Liberar(ESPERA_MAPA_MS) end)

-- o jogo trocou o texto de um widget (hover, troca de aba...): traduz no quadro seguinte
if GANCHO_TEXTO then
    for _, cls in ipairs(CLASSES) do
        local ok = pcall(RegisterHook, "/Script/UMG." .. cls .. ":SetText", function(ctx)
            if liberadoEm == nil then return end
            local okW, w = pcall(function() return ctx:get() end)
            if not okW or not w then return end
            ExecuteInGameThread(function()
                pcall(function() if PodeVarrer() then Processar(w) end end)
            end)
        end)
        Log("gancho " .. cls .. ":SetText " .. (ok and "ok" or "nao deu"))
    end
end

RegisterKeyBind(TECLA_MENU, function()
    ExecuteInGameThread(function()
        Enviar("M")
        pcall(Gravar)
    end)
end)

LoopAsync(INTERVALO_CONFERIR_MS, function()
    ExecuteInGameThread(function()
        if PodeVarrer() then
            local ok, erro = pcall(Conferir)
            if not ok then Log("erro conferindo: " .. tostring(erro)) end
        end
    end)
    return false
end)

LoopAsync(INTERVALO_MS, function()
    ExecuteInGameThread(Tick)
    return false
end)
