using System;
using System.Drawing;
using System.Windows.Forms;

// 00 Como usar: guia das abas, so texto. Um botao por aba; cada um mostra para que ela serve
// e o que faz cada opcao. Abre sozinho na primeira vez (LiveConfig.TutorialVisto).
internal sealed partial class Janela
{
    private int passo = AbaPainel;   // aba explicada agora (AbaPainel..AbaInterface)

    private void PaginaTutorial()
    {
        passo = Mathf.Clamp(passo, AbaPainel, AbaInterface);
        FlowLayoutPanel c = Cartao(null);

        c.Controls.Add(Tema.Rotulo(LiveTxt.T("guia_intro"), Interna, Tema.Fraco, 9.5f));

        // um botao por aba (quebra a linha se nao couber)
        FlowLayoutPanel abas = Tema.Linha(Interna);
        abas.Margin = new Padding(0, Tema.D(6), 0, Tema.D(8));
        for (int i = AbaPainel; i <= AbaInterface; i++)
        {
            int idx = i;
            string rot = i.ToString("00") + "  " + LiveTxt.T(NomesAbas[i]);
            bool sel = i == passo;
            Button b = Tema.Botao(rot, Tema.D(28) + TextRenderer.MeasureText(rot, Tema.F(9f, sel)).Width, Tema.D(30), sel ? 1 : 0, delegate { passo = idx; MostrarAba(AbaTutorial); });
            abas.Controls.Add(b);
        }
        c.Controls.Add(abas);

        c.Controls.Add(Tema.Rotulo(passo.ToString("00") + "  " + LiveTxt.T(NomesAbas[passo]), Interna, Tema.Dourado, 15f, true));
        Label txt = Tema.Rotulo(LiveTxt.T("guia" + passo), Interna, Tema.Texto, 10.5f);
        txt.Margin = new Padding(0, Tema.D(6), 0, Tema.D(4));
        c.Controls.Add(txt);

        FlowLayoutPanel nav = Tema.Linha(Interna);
        nav.Margin = new Padding(0, Tema.D(14), 0, 0);
        Button ant = Tema.Botao("◀ " + LiveTxt.T("tut_ant"), Tema.D(140), Tema.D(34), 0, delegate { passo--; MostrarAba(AbaTutorial); });
        ant.Enabled = passo > AbaPainel;
        nav.Controls.Add(ant);
        Button prox = Tema.Botao(LiveTxt.T("tut_prox") + " ▶", Tema.D(160), Tema.D(34), 1, delegate { passo++; MostrarAba(AbaTutorial); });
        prox.Enabled = passo < AbaInterface;
        nav.Controls.Add(prox);
        int alvo = passo;
        string rotIr = "↗ " + LiveTxt.F("tut_ir", LiveTxt.T(NomesAbas[alvo]));
        Button ir = Tema.Botao(rotIr, Tema.D(40) + TextRenderer.MeasureText(rotIr, Tema.F(9f)).Width, Tema.D(34), 0, delegate { MostrarAba(alvo); });
        ir.Margin = new Padding(Tema.D(20), 0, 0, 0);
        nav.Controls.Add(ir);
        c.Controls.Add(nav);
    }
}
