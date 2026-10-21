# Tradutor UE5

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Apoie-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/C1P628Z6GT) [![Pix](https://img.shields.io/badge/Pix-Apoie-32BCAD?logo=pix&logoColor=white)](#apoie-o-projeto)

🇺🇸 [English](README.md) · 🇧🇷 **Português (BR)**

Tradução em tempo real para jogos em Unreal Engine 5, portada do
[ModLive](https://github.com/Raz3n28/ModLive-Traducao-Universal) (Unity). Primeiro alvo:
**Sanctum: Whispers of Qingcheng** (下一站青城).

## Instalação

**Requisitos:** Windows 10/11 x64 · [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) · [UE4SS experimental](https://github.com/UE4SS-RE/RE-UE4SS/releases)

1. **Instale o .NET 8 Desktop Runtime (x64)** no link acima ("Desktop Runtime", "Windows x64").
   Para conferir, no cmd: `dotnet --list-runtimes` deve mostrar `Microsoft.WindowsDesktop.App 8.x`.
2. **Instale o UE4SS** (versão experimental mais recente): extraia o zip dele na pasta do executável do jogo.
   No Sanctum: `...\steamapps\common\QingChengDemo\QingCheng\Binaries\Win64\` (onde fica o
   `QingCheng-Win64-Shipping.exe`). Ali devem ficar o `dwmapi.dll` e a pasta `ue4ss`.
3. **Sanctum (UE 5.8):** em `ue4ss\UE4SS-settings.ini`, seção `[Hooks]`, deixe só `HookLoadMap = 1` e
   `HookEngineTick = 1`; todos os outros `Hook... = 0`. Sem isso o jogo pode travar ou fechar.
4. **Baixe o TradutorUE5.zip** em [Releases](https://github.com/Raz3n28/Tradutor-UE5/releases) e copie a pasta `ue4ss` do zip para a mesma `Binaries\Win64`
   (ela se junta com a do UE4SS).
5. **Abra o jogo.** O tradutor abre sozinho. Aperte **F8** para abrir o menu, escolha o idioma e o serviço
   (grátis ou com chave) e clique em **Começar tradução**. **F9** pausa/continua.

Para desinstalar, apague `ue4ss\Mods\TradutorUE5` (ou o `dwmapi.dll` para tirar o UE4SS inteiro).
Detalhes e solução de problemas: [`LEIA-ME.txt`](LEIA-ME.txt).

## Funcionalidades

- **Tradução ao vivo** do texto da tela. Não altera arquivos do jogo: tirou o mod, o jogo volta ao original.
- **Modo grátis** (Google Tradutor, sem chave) ou **modo IA** com a sua chave: Gemini, DeepSeek, Groq,
  OpenRouter, OpenAI, Claude, Ollama (no seu PC) ou qualquer serviço compatível com OpenAI.
- **18 idiomas de destino** e menu em 6 idiomas (PT, EN, ES, RU, FR, DE).
- **Estilos de tradução** (natural, legenda de donghua, épico/wuxia, casual, bem curto), instruções extras
  e teste do estilo.
- **Glossário inteligente** com categorias, abreviações para telas apertadas, trava, conflitos, sugestões
  da IA e pacote opcional de termos xianxia.
- **Falas e descrições** agrupadas por personagem, item ou missão: editar, travar e retraduzir.
- **Cache em disco**, controle de gasto da chave, pacote de tradução (.zip) para exportar/importar, backup
  diário do glossário e relatório de erro com um clique (sem as chaves de API).

## Screenshots

**Painel** – estado, provedor, idioma, glossário, cache e gasto da chave num lugar só.
![Painel](docs/img/painel.webp)

**Geral** – modo grátis (Google) ou com chave de IA, 18 idiomas de destino e quais escritas traduzir.
![Geral](docs/img/geral.webp)

**Provedor** – Gemini, DeepSeek, Groq, OpenRouter, OpenAI, Claude, Ollama ou personalizado.
![Provedor](docs/img/provedor.webp)

**Estilo** – estilos prontos (natural, donghua, épico/wuxia, casual, bem curto), instruções extras e teste do estilo.
![Estilo](docs/img/estilo.webp)

**Glossário** – nomes, técnicas e itens com categoria, abreviação, trava, conflitos e sugestões da IA.
![Glossário](docs/img/glossario.webp)

**Falas e descrições** – falas agrupadas por personagem, item ou missão: editar, travar e retraduzir.
![Falas](docs/img/falas.webp)

**Categorias** – 13 categorias de cultivo com palavras-chave e instrução para a IA.
![Categorias](docs/img/categorias.webp)

**Configurações** – ritmo, tecla F9, pacote de tradução (.zip), relatório de erro, gasto, backup e cache.
![Configurações](docs/img/configuracoes.webp)

**Log** – o que o tradutor está fazendo.
![Log](docs/img/log.webp)

**Como usar** – guia de cada aba dentro do próprio menu (F8).
![Como usar](docs/img/como-usar.webp)

## Como funciona

```
jogo (UE5) ── UE4SS ── ponte Lua ──(arquivos em troca/)── TradutorCerebro.exe ── Google / IA
```

- **Ponte** (`ponte/TradutorUE5/Scripts/main.lua`): mod Lua do UE4SS. Olha os `TextBlock` e
  `RichTextBlock` da tela, manda texto novo para o cérebro e troca pela tradução. F8 abre o menu.
- **Cérebro** (`cerebro/`): .exe em C# (.NET 8, WinForms). Reaproveita Cache, Glossário, Categorias,
  Json, Motor e Provedores do ModLive (UnityWebRequest virou HttpClient). A janela dele é o menu.
- **Troca**: `pedidos.txt` (ponte → cérebro) e `respostas.txt` (cérebro → ponte). Formato em
  `cerebro/Ponte.cs`.

## Build

O GitHub Actions compila a cada push e gera o artefato **TradutorUE5** (zip pronto para copiar em
`Binaries\Win64`). Veja `LEIA-ME.txt` para instalar.

## Apoie o projeto

Curtiu o mod? Um cafezinho ajuda a manter o desenvolvimento (tokens de IA e tempo de teste não são de graça). Valeu! 💙

<table>
<tr>
<td align="center"><b>Ko-fi</b><br><a href="https://ko-fi.com/C1P628Z6GT"><img src="cerebro/img/kofi-qr.png" width="180" alt="Ko-fi QR"></a><br><a href="https://ko-fi.com/C1P628Z6GT">ko-fi.com/C1P628Z6GT</a></td>
<td align="center"><b>Pix</b><br><img src="cerebro/img/pix-qr.png" width="180" alt="Pix QR"><br>Chave aleatória:<br><code>1cf0c1a4-c729-48b2-9498-ff30b8b4dc89</code></td>
</tr>
</table>

## Direitos autorais

© 2026 Raz3n. Todos os direitos reservados. Uso pessoal liberado; redistribuir, vender ou publicar
versões modificadas só com autorização. Veja [`LICENSE`](LICENSE).
