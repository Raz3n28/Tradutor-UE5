-- Tradutor UE5: ponte dentro do jogo (mod Lua do UE4SS).
-- Vigia os textos da tela (TextBlock e RichTextBlock), manda cada texto novo para o cerebro
-- (TradutorCerebro.exe) e troca pela traducao quando ela chega. F8 abre/fecha o menu do cerebro.
--
-- Conversa com o cerebro por dois arquivos na pasta "troca" (ver cerebro/Ponte.cs):
--   pedidos.txt   (ponte -> cerebro)  T<TAB>texto<TAB>pista | M (menu) | I (jogo abriu)
--   respostas.txt (cerebro -> ponte)  R<TAB>texto<TAB>traducao | N<TAB>texto | X (devolver originais)

---------------- ajustes ----------------
local INTERVALO_MS = 250          -- de quanto em quanto tempo olha a tela
local TECLA_MENU = Key.F8
local ABRIR_CEREBRO = true        -- abre o TradutorCerebro.exe junto com o jogo
local CLASSES = { "TextBlock", "RichTextBlock" }

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
local aplicados = {}  -- endereco do widget -> { w = widget, o = original } (para devolver os originais)
local saida = {}      -- linhas para pedidos.txt
local lidoResp = 0
local naTela = {}     -- endereco do widget -> true (widget de verdade) | false (molde: Default__ ou template)
local proxLimpeza = 0
local primeiraTroca = true

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
local function DevolverOriginais()
    local n = 0
    for _, a in pairs(aplicados) do
        pcall(function()
            if a.w:IsValid() then
                a.w:SetText(FText(a.o))
                n = n + 1
            end
        end)
    end
    trad, pedidos, nossas, aplicados = {}, {}, {}, {}
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
            if okEnd and endereco then aplicados[endereco] = { w = w, o = txt } end
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

local function Tick()
    local ok, erro = pcall(LerRespostas)
    if not ok then Log("erro lendo respostas: " .. tostring(erro)) end
    ok, erro = pcall(Varrer)
    if not ok then Log("erro vigiando a tela: " .. tostring(erro)) end
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

RegisterKeyBind(TECLA_MENU, function()
    ExecuteInGameThread(function()
        Enviar("M")
        pcall(Gravar)
    end)
end)

LoopAsync(INTERVALO_MS, function()
    ExecuteInGameThread(Tick)
    return false
end)
