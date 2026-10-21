# Universal Translator UE5

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20me-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/C1P628Z6GT)

🇺🇸 **English** · 🇧🇷 [Português (BR)](README.pt-BR.md)

Real-time translation for Unreal Engine 5 games, ported from
[ModLive](https://github.com/Raz3n28/ModLive-Traducao-Universal) (Unity). First target:
**Sanctum: Whispers of Qingcheng** (下一站青城).

## How to install

**Requirements:** Windows 10/11 x64 · [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) · [UE4SS experimental](https://github.com/UE4SS-RE/RE-UE4SS/releases)

1. **Install the .NET 8 Desktop Runtime (x64)** from the link above ("Desktop Runtime", "Windows x64").
   To check, in cmd: `dotnet --list-runtimes` should list `Microsoft.WindowsDesktop.App 8.x`.
2. **Install UE4SS** (latest experimental): extract its zip into the folder with the game's executable.
   For Sanctum: `...\steamapps\common\QingChengDemo\QingCheng\Binaries\Win64\` (where
   `QingCheng-Win64-Shipping.exe` is). You should see `dwmapi.dll` and a `ue4ss` folder there.
3. **Sanctum (UE 5.8):** in `ue4ss\UE4SS-settings.ini`, `[Hooks]` section, keep only `HookLoadMap = 1` and
   `HookEngineTick = 1`; set every other `Hook...` to `0`. Otherwise the game may freeze or crash.
4. **Download TradutorUE5.zip** from [Releases](https://github.com/Raz3n28/Tradutor-UE5/releases) and copy
   its `ue4ss` folder into the same `Binaries\Win64` folder (it merges with the UE4SS one).
5. **Start the game.** The translator opens by itself. Press **F8** to open the menu, pick the language and
   the service (free or API key) and click **Start translating**. **F9** pauses/resumes.

To uninstall, delete `ue4ss\Mods\TradutorUE5` (or `dwmapi.dll` to remove UE4SS entirely).
Details and troubleshooting (Portuguese): [`LEIA-ME.txt`](LEIA-ME.txt).

## Features

- **Live translation** of on-screen text. No game files are changed: remove the mod and the game is original.
- **Free mode** (Google Translate, no key) or **AI mode** with your own key: Gemini, DeepSeek, Groq,
  OpenRouter, OpenAI, Claude, Ollama (local) or any OpenAI-compatible endpoint.
- **18 target languages** and a menu in 6 languages (PT, EN, ES, RU, FR, DE).
- **Translation styles** (natural, donghua subtitles, epic/wuxia, casual, very short), custom instructions
  and a style tester.
- **Smart glossary** with categories, abbreviations for cramped screens, locking, conflicts, AI suggestions
  and an optional xianxia term pack.
- **Lines and descriptions** grouped by speaker, item or quest: edit, lock and retranslate.
- **Disk cache**, token cost tracker, translation pack export/import (.zip), daily glossary backup and
  one-click error report (API keys are never included).

## Screenshots

**Dashboard** – status, provider, language, glossary, cache and key spending in one place.
![Dashboard](docs/img/painel.webp)

**General** – free mode (Google) or AI key, 18 target languages and which scripts to translate.
![General](docs/img/geral.webp)

**Provider** – Gemini, DeepSeek, Groq, OpenRouter, OpenAI, Claude, Ollama or custom.
![Provider](docs/img/provedor.webp)

**Style** – ready-made styles, extra instructions and a style tester.
![Style](docs/img/estilo.webp)

**Glossary** – names, techniques and items with category, abbreviation, lock, conflicts and AI suggestions.
![Glossary](docs/img/glossario.webp)

**Lines and descriptions** – lines grouped by character, item or quest: edit, lock and retranslate.
![Lines and descriptions](docs/img/falas.webp)

**Categories** – 13 cultivation categories with keywords and an instruction for the AI.
![Categories](docs/img/categorias.webp)

**Settings** – pace, F9 key, translation pack (.zip), error report, spending, backup and cache.
![Settings](docs/img/configuracoes.webp)

**Log** – what the translator is doing.
![Log](docs/img/log.webp)

**How to use** – a guide to every tab, right inside the menu (F8).
![How to use](docs/img/como-usar.webp)

## How it works

```
game (UE5) ── UE4SS ── Lua bridge ──(files in troca/)── TradutorCerebro.exe ── Google / AI
```

- **Bridge** (`ponte/TradutorUE5/Scripts/main.lua`): UE4SS Lua mod. Watches the on-screen `TextBlock` and
  `RichTextBlock`, sends new text to the brain and swaps in the translation. F8 opens the menu.
- **Brain** (`cerebro/`): C# .exe (.NET 8, WinForms). Its window is the menu.
- **Exchange**: `pedidos.txt` (bridge → brain) and `respostas.txt` (brain → bridge). Format in
  `cerebro/Ponte.cs`.

## Build

GitHub Actions builds on every push and produces the **TradutorUE5** artifact (a zip ready to copy into
`Binaries\Win64`).

## Support the project

Like the mod? A coffee helps keep it going (AI tokens and testing time aren't free). Thank you! 💙

<table>
<tr>
<td align="center"><b>Ko-fi</b><br><a href="https://ko-fi.com/C1P628Z6GT"><img src="cerebro/img/kofi-qr.png" width="180" alt="Ko-fi QR"></a><br><a href="https://ko-fi.com/C1P628Z6GT">ko-fi.com/C1P628Z6GT</a></td>
<td align="center"><b>Pix (Brazil)</b><br><img src="cerebro/img/pix-qr.png" width="180" alt="Pix QR"><br>Random key:<br><code>1cf0c1a4-c729-48b2-9498-ff30b8b4dc89</code></td>
</tr>
</table>

## Copyright

© 2026 Raz3n. All rights reserved. Free for personal use; redistribution, selling or publishing
modified versions requires permission. See [`LICENSE`](LICENSE).
