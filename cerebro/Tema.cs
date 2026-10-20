using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Cores e pecas do menu escuro (o mesmo visual do ModLive). Tudo e criado no codigo, sem designer.
// Tamanhos passam por D() e fontes por F(): a aba Interface muda a escala (LiveConfig.Escala) e a janela
// e montada de novo.
internal static class Tema
{
    public static readonly System.Drawing.Color Fundo = Rgb(0x0F, 0x12, 0x16);
    public static readonly System.Drawing.Color Lateral = Rgb(0x12, 0x16, 0x1B);
    public static readonly System.Drawing.Color Conteudo = Rgb(0x16, 0x1B, 0x21);
    public static readonly System.Drawing.Color Cartao = Rgb(0x1C, 0x22, 0x2A);
    public static readonly System.Drawing.Color CartaoClaro = Rgb(0x23, 0x2A, 0x33);
    public static readonly System.Drawing.Color CorCampo = Rgb(0x0E, 0x11, 0x15);
    public static readonly System.Drawing.Color Borda = Rgb(0x2B, 0x33, 0x3D);
    public static readonly System.Drawing.Color Texto = Rgb(0xE2, 0xE8, 0xEF);
    public static readonly System.Drawing.Color Fraco = Rgb(0x8B, 0x95, 0xA1);
    public static readonly System.Drawing.Color Destaque = Rgb(0x2D, 0xD4, 0xBF);
    public static readonly System.Drawing.Color DestaqueEscuro = Rgb(0x16, 0x3B, 0x38);
    public static readonly System.Drawing.Color Laranja = Rgb(0xF5, 0xA5, 0x24);
    public static readonly System.Drawing.Color Ok = Rgb(0x4A, 0xDE, 0x80);
    public static readonly System.Drawing.Color Aviso = Rgb(0xFB, 0xBF, 0x24);
    public static readonly System.Drawing.Color Erro = Rgb(0xF8, 0x71, 0x71);
    public static readonly System.Drawing.Color Perigo = Rgb(0x7F, 0x1D, 0x1D);
    public static readonly System.Drawing.Color TextoEscuro = Rgb(0x0B, 0x0F, 0x14);

    public static float Escala = 1f;

    public static int D(float px)
    {
        return (int)Math.Round(px * Escala);
    }

    private static readonly Dictionary<string, Font> fontes = new Dictionary<string, Font>();

    public static Font F(float tamanho, bool negrito = false)
    {
        float t = tamanho * Escala;
        string k = t.ToString("0.00") + (negrito ? "b" : "");
        Font f;
        if (!fontes.TryGetValue(k, out f))
        {
            f = new Font("Segoe UI", t, negrito ? FontStyle.Bold : FontStyle.Regular);
            fontes[k] = f;
        }
        return f;
    }

    public static System.Drawing.Color Rgb(int r, int g, int b)
    {
        return System.Drawing.Color.FromArgb(r, g, b);
    }

    // estilo do botao: 0 normal, 1 selecionado (teal), 2 laranja, 3 perigo, 4 link (sem fundo)
    public static void Pintar(Button b, int estilo)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = estilo == 0 ? 1 : 0;
        b.FlatAppearance.BorderColor = Borda;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        switch (estilo)
        {
            case 1:
                b.BackColor = Destaque;
                b.ForeColor = TextoEscuro;
                b.FlatAppearance.MouseOverBackColor = Rgb(0x4F, 0xE3, 0xD0);
                b.FlatAppearance.MouseDownBackColor = Rgb(0x22, 0xB0, 0x9E);
                break;
            case 2:
                b.BackColor = Laranja;
                b.ForeColor = TextoEscuro;
                b.FlatAppearance.MouseOverBackColor = Rgb(0xF8, 0xB8, 0x4E);
                b.FlatAppearance.MouseDownBackColor = Rgb(0xD9, 0x8C, 0x10);
                break;
            case 3:
                b.BackColor = Perigo;
                b.ForeColor = Texto;
                b.FlatAppearance.MouseOverBackColor = Rgb(0x99, 0x2A, 0x2A);
                b.FlatAppearance.MouseDownBackColor = Rgb(0x66, 0x16, 0x16);
                break;
            case 4:
                b.BackColor = System.Drawing.Color.Transparent;
                b.ForeColor = Destaque;
                b.FlatAppearance.MouseOverBackColor = CartaoClaro;
                b.FlatAppearance.MouseDownBackColor = CartaoClaro;
                break;
            default:
                b.BackColor = CartaoClaro;
                b.ForeColor = Texto;
                b.FlatAppearance.MouseOverBackColor = Rgb(0x2D, 0x35, 0x40);
                b.FlatAppearance.MouseDownBackColor = Rgb(0x1A, 0x20, 0x27);
                break;
        }
    }

    public static Button Botao(string texto, int largura, int altura, int estilo, EventHandler clique)
    {
        Button b = new Button
        {
            Text = texto,
            Width = largura,
            Height = altura,
            Font = F(9f, estilo == 1 || estilo == 2),
            Margin = new Padding(0, 0, D(6), D(6)),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Pintar(b, estilo);
        if (clique != null) b.Click += clique;
        return b;
    }

    public static Label Rotulo(string texto, int largura, System.Drawing.Color cor, float tamanho = 9f, bool negrito = false)
    {
        return new Label
        {
            Text = texto,
            AutoSize = true,
            MaximumSize = new Size(Math.Max(D(60), largura), 0),
            ForeColor = cor,
            BackColor = System.Drawing.Color.Transparent,
            Font = F(tamanho, negrito),
            Margin = new Padding(0, D(2), 0, D(4))
        };
    }

    public static TextBox Campo(string texto, int largura)
    {
        return new TextBox
        {
            Text = texto ?? "",
            Width = largura,
            BackColor = Tema.CorCampo,
            ForeColor = Texto,
            BorderStyle = BorderStyle.FixedSingle,
            Font = F(9.5f),
            Margin = new Padding(0, 0, D(6), D(6))
        };
    }

    public static TextBox Caixa(string texto, int largura, int linhas, int max)
    {
        TextBox t = Campo(texto, largura);
        t.Multiline = true;
        t.ScrollBars = ScrollBars.Vertical;
        t.WordWrap = true;
        t.Height = D(10) + (int)(linhas * F(9.5f).GetHeight() + 0.5f);
        if (max > 0) t.MaxLength = max;
        return t;
    }

    public static CheckBox Marca(string texto, bool marcado, int largura)
    {
        return new CheckBox
        {
            Text = texto,
            Checked = marcado,
            AutoSize = true,
            MaximumSize = new Size(largura, 0),
            ForeColor = Texto,
            BackColor = System.Drawing.Color.Transparent,
            // caixinha padrao do Windows (o estilo "plano" desenhava o ✓ branco na caixa branca: invisivel)
            FlatStyle = FlatStyle.Standard,
            Font = F(9f),
            Margin = new Padding(0, D(2), 0, D(2)),
            Cursor = Cursors.Hand
        };
    }

    public static NumericUpDown Numero(int min, int max, int valor, int largura)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Max(min, Math.Min(max, valor)),
            Width = largura,
            BackColor = Tema.CorCampo,
            ForeColor = Texto,
            BorderStyle = BorderStyle.FixedSingle,
            Font = F(9.5f),
            Margin = new Padding(0, 0, D(10), D(6))
        };
    }

    // linha horizontal de controles (quebra se nao couber)
    public static FlowLayoutPanel Linha(int largura)
    {
        return new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            MaximumSize = new Size(largura, 0),
            MinimumSize = new Size(largura, 0),
            BackColor = System.Drawing.Color.Transparent,
            Margin = new Padding(0, 0, 0, D(2)),
            Padding = new Padding(0)
        };
    }

    // cor da categoria (Categorias.Cor) para a tela
    public static System.Drawing.Color Cor(global::Color c)
    {
        return c.ParaDrawing();
    }
}
