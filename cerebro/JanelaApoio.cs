using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using DColor = System.Drawing.Color;

// Card "Apoie o projeto" no pe do menu lateral (QR do Ko-fi pequeno + botoes Ko-fi e Pix) e a janela
// com o QR grande. O QR do Pix e fixo (chave aleatoria, sem valor): nao expira.
internal sealed partial class Janela
{
    private const string LinkKofi = "https://ko-fi.com/C1P628Z6GT";
    private const string ChavePix = "1cf0c1a4-c729-48b2-9498-ff30b8b4dc89";
    private const string NomePix = "Paulo Ricardo da Silva";
    private const string BancoPix = "Mercado Pago";

    private Panel MontarApoio(int largura)
    {
        Panel c = new Panel { Width = largura, Height = Tema.D(78), BackColor = Tema.Cartao };
        c.Paint += delegate (object s, PaintEventArgs e)
        {
            using (Pen p = new Pen(Tema.Borda)) e.Graphics.DrawRectangle(p, 0, 0, c.Width - 1, c.Height - 1);
        };
        int q = Tema.D(62);
        PictureBox qr = new PictureBox
        {
            Image = Tema.Imagem("kofi-qr.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(q, q),
            Location = new Point(Tema.D(8), Tema.D(8)),
            BackColor = DColor.White,
            Cursor = Cursors.Hand
        };
        qr.Click += delegate { AbrirApoio(0); };
        c.Controls.Add(qr);
        int tx = q + Tema.D(16);
        Label t = new Label
        {
            Text = LiveTxt.T("apoio_card"),
            Location = new Point(tx, Tema.D(6)),
            Size = new Size(largura - tx - Tema.D(6), Tema.D(34)),
            ForeColor = Tema.Texto,
            BackColor = DColor.Transparent,
            Font = Tema.F(8.5f)
        };
        c.Controls.Add(t);
        int bw = (largura - tx - Tema.D(10)) / 2;
        Button k = Tema.Botao("Ko-fi", bw, Tema.D(24), 1, delegate { AbrirApoio(0); });
        k.Font = Tema.F(8f, true);
        k.Location = new Point(tx, Tema.D(46));
        c.Controls.Add(k);
        Button x = Tema.Botao("Pix", bw, Tema.D(24), 2, delegate { AbrirApoio(1); });
        x.Font = Tema.F(8f, true);
        x.Location = new Point(tx + bw + Tema.D(4), Tema.D(46));
        c.Controls.Add(x);
        return c;
    }

    // janela com o QR grande: 0 Ko-fi, 1 Pix
    private void AbrirApoio(int qual)
    {
        using (Form d = new Form())
        {
            d.Text = LiveTxt.T("apoio_titulo");
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.StartPosition = FormStartPosition.CenterParent;
            d.MinimizeBox = false;
            d.MaximizeBox = false;
            d.ClientSize = new Size(Tema.D(420), Tema.D(560));
            d.BackColor = Tema.Cartao;
            d.ForeColor = Tema.Texto;
            d.Font = Tema.F(9f);
            Tema.Papel(d);
            int w = Tema.D(380);
            FlowLayoutPanel f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Tema.D(20)), BackColor = DColor.Transparent };
            d.Controls.Add(f);
            FlowLayoutPanel abas = Tema.Linha(w);
            Button bk = Tema.Botao("Ko-fi", (w - Tema.D(8)) / 2, Tema.D(30), 0, null);
            Button bp = Tema.Botao("Pix", (w - Tema.D(8)) / 2, Tema.D(30), 0, null);
            abas.Controls.Add(bk);
            abas.Controls.Add(bp);
            f.Controls.Add(abas);
            Label frase = Tema.Rotulo("", w, Tema.Texto, 9.5f);
            f.Controls.Add(frase);
            PictureBox img = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(w, Tema.D(300)),
                BackColor = DColor.White,
                Margin = new Padding(0, Tema.D(8), 0, Tema.D(8))
            };
            f.Controls.Add(img);
            Label info = Tema.Rotulo("", w, Tema.Texto, 9.5f);
            f.Controls.Add(info);
            Label aviso = Tema.Rotulo("", w, Tema.Ok, 8.5f);
            Button acao = Tema.Botao("", w, Tema.D(34), 1, null);
            acao.Margin = new Padding(0, Tema.D(8), 0, 0);
            f.Controls.Add(acao);
            f.Controls.Add(aviso);
            int atual = qual;
            Action mostrar = null;
            mostrar = delegate
            {
                Tema.Pintar(bk, atual == 0 ? 1 : 0);
                Tema.Pintar(bp, atual == 1 ? 2 : 0);
                aviso.Text = "";
                if (atual == 0)
                {
                    frase.Text = LiveTxt.T("apoio_kofi");
                    img.Image = Tema.Imagem("kofi-qr.png");
                    info.Text = LinkKofi;
                    acao.Text = LiveTxt.T("apoio_abrir_kofi");
                }
                else
                {
                    frase.Text = LiveTxt.T("apoio_pix");
                    img.Image = Tema.Imagem("pix-qr.png");
                    info.Text = LiveTxt.T("apoio_nome") + ": " + NomePix + "\n" + LiveTxt.T("apoio_banco") + ": " + BancoPix
                        + "\n" + LiveTxt.T("apoio_chave") + ": " + ChavePix;
                    acao.Text = LiveTxt.T("apoio_copiar");
                }
            };
            bk.Click += delegate { atual = 0; mostrar(); };
            bp.Click += delegate { atual = 1; mostrar(); };
            acao.Click += delegate
            {
                if (atual == 0)
                {
                    try { Process.Start(new ProcessStartInfo(LinkKofi) { UseShellExecute = true }); }
                    catch (Exception e) { aviso.Text = e.Message; aviso.ForeColor = Tema.Erro; }
                }
                else
                {
                    try { Clipboard.SetText(ChavePix); aviso.Text = LiveTxt.T("apoio_copiado"); aviso.ForeColor = Tema.Ok; }
                    catch (Exception e) { aviso.Text = e.Message; aviso.ForeColor = Tema.Erro; }
                }
            };
            mostrar();
            d.ShowDialog(this);
        }
    }
}
