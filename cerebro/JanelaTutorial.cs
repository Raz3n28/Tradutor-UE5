using System;
using System.Drawing;
using System.Windows.Forms;
using DColor = System.Drawing.Color;

// 00 Como usar: tutorial passo a passo (imagem + texto curto + Anterior/Proximo + botao para a aba certa).
// Abre sozinho na primeira vez (LiveConfig.TutorialVisto).
internal sealed partial class Janela
{
    private const int PassosTutorial = 9;
    private int passo;
    // aba que cada passo abre (-1 = nenhuma)
    private static readonly int[] abaDoPasso = new int[] { -1, -1, AbaPainel, AbaProv, AbaPainel, AbaEstilo, AbaGlos, AbaGlos, AbaAvanc };

    private void PaginaTutorial()
    {
        passo = Mathf.Clamp(passo, 0, PassosTutorial - 1);
        FlowLayoutPanel c = Cartao(null);
        Tema.Papel(c);

        // bolinhas: em que passo esta (clicaveis)
        FlowLayoutPanel bolas = Tema.Linha(Interna);
        for (int i = 0; i < PassosTutorial; i++)
        {
            int idx = i;
            Button b = Tema.Botao((i + 1).ToString(), Tema.D(30), Tema.D(30), i == passo ? 2 : (i < passo ? 1 : 0), delegate { passo = idx; MostrarAba(AbaTutorial); });
            b.Font = Tema.F(9f, true);
            Tema.Arredondar(b, Tema.D(15));
            b.Margin = new Padding(0, 0, Tema.D(6), Tema.D(10));
            bolas.Controls.Add(b);
        }
        c.Controls.Add(bolas);

        Label tit = Tema.Rotulo(LiveTxt.T("tut" + (passo + 1) + "_t"), Interna, Tema.Dourado, 15f, true);
        c.Controls.Add(tit);

        int ih = Math.Min(Tema.D(320), Interna * 320 / 720);
        PictureBox img = new PictureBox
        {
            Image = Tema.Imagem("tut" + (passo + 1) + ".png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(Interna, ih),
            BackColor = DColor.Transparent,
            Margin = new Padding(0, Tema.D(6), 0, Tema.D(10))
        };
        c.Controls.Add(img);

        Label txt = Tema.Rotulo(LiveTxt.T("tut" + (passo + 1) + "_x"), Interna, Tema.Texto, 10.5f);
        c.Controls.Add(txt);

        FlowLayoutPanel nav = Tema.Linha(Interna);
        nav.Margin = new Padding(0, Tema.D(14), 0, 0);
        Button ant = Tema.Botao("◀ " + LiveTxt.T("tut_ant"), Tema.D(140), Tema.D(34), 0, delegate { passo--; MostrarAba(AbaTutorial); });
        ant.Enabled = passo > 0;
        nav.Controls.Add(ant);
        bool ultimo = passo == PassosTutorial - 1;
        nav.Controls.Add(Tema.Botao(ultimo ? LiveTxt.T("tut_fim") + " ✓" : LiveTxt.T("tut_prox") + " ▶", Tema.D(160), Tema.D(34), 1, delegate
        {
            if (ultimo) { passo = 0; MostrarAba(AbaPainel); }
            else { passo++; MostrarAba(AbaTutorial); }
        }));
        int alvo = abaDoPasso[passo];
        if (alvo >= 0)
        {
            Button ir = Tema.Botao("↗ " + LiveTxt.F("tut_ir", LiveTxt.T(NomesAbas[alvo])), Tema.D(240), Tema.D(34), 0, delegate { MostrarAba(alvo); });
            ir.Margin = new Padding(Tema.D(20), 0, 0, 0);
            nav.Controls.Add(ir);
        }
        c.Controls.Add(nav);
    }
}
