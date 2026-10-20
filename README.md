# Tradutor UE5

Tradução em tempo real para jogos em Unreal Engine 5, portada do
[ModLive](https://github.com/Raz3n28/ModLive-Traducao-Universal) (Unity). Primeiro alvo:
**Sanctum: Whispers of Qingcheng** (下一站青城).

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
