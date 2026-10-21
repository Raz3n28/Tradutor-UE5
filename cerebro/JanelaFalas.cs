using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DColor = System.Drawing.Color;

// 06 Falas e descricoes: grupos (quem fala / item / missao) a esquerda, os textos do grupo a direita
// com a traducao editavel, travar, retraduzir e mover para outro grupo.
internal sealed partial class Janela
{
    private int filtroFalas = -1;      // -1 todos, senao Falas.Personagens...
    private string buscaFalas = "";
    private string grupoSel;
    private DataGridView gradeGrupos;
    private FlowLayoutPanel listaFalas;
    private Label falasVazio;
    private FlowLayoutPanel filtrosFalas;
    private int versaoFalas = -1;
    private readonly List<GrupoFalas> gruposVisiveis = new List<GrupoFalas>();

    private static readonly string[] iconeTipo = { "👤", "🎒", "📜", "💬" };

    private Control PaginaFalas()
    {
        TableLayoutPanel t = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, BackColor = Tema.Conteudo, Padding = new Padding(0, 0, Tema.D(4), 0) };
        Tema.Papel(t);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        int w = Math.Max(Tema.D(480), corpo.ClientSize.Width - Tema.D(8));

        FlowLayoutPanel topo = Tema.Linha(w);
        Label lupa = Tema.Rotulo("🔍", Tema.D(24), Tema.Fraco, 10f);
        lupa.Margin = new Padding(0, Tema.D(4), Tema.D(2), 0);
        topo.Controls.Add(lupa);
        TextBox tb = Tema.Campo(buscaFalas, Tema.D(340));
        Timer espera = new Timer { Interval = 300 };
        espera.Tick += delegate { espera.Stop(); versaoFalas = -1; PreencherGrupos(); };
        tb.TextChanged += delegate { buscaFalas = tb.Text; espera.Stop(); espera.Start(); };
        tb.Disposed += delegate { espera.Dispose(); };
        topo.Controls.Add(tb);
        Label dica = Tema.Rotulo(LiveTxt.T("falas_dica"), w - Tema.D(380), Tema.Fraco, 8.5f);
        dica.Margin = new Padding(Tema.D(10), Tema.D(2), 0, 0);
        topo.Controls.Add(dica);
        t.Controls.Add(topo);

        filtrosFalas = Tema.Linha(w);
        t.Controls.Add(filtrosFalas);

        TableLayoutPanel meio = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DColor.Transparent };
        meio.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
        meio.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
        meio.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        gradeGrupos = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Tema.Cartao,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Tema.Rgb(0x1D, 0x27, 0x2F),
            RowHeadersVisible = false,
            ColumnHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = Tema.F(10f),
            Margin = new Padding(0, 0, Tema.D(10), 0)
        };
        gradeGrupos.RowTemplate.Height = Tema.D(34);
        gradeGrupos.DefaultCellStyle.BackColor = Tema.Cartao;
        gradeGrupos.DefaultCellStyle.ForeColor = Tema.Texto;
        gradeGrupos.DefaultCellStyle.SelectionBackColor = Tema.DestaqueEscuro;
        gradeGrupos.DefaultCellStyle.SelectionForeColor = Tema.Destaque;
        gradeGrupos.DefaultCellStyle.Padding = new Padding(Tema.D(6), 0, 0, 0);
        gradeGrupos.Columns.Add(new DataGridViewTextBoxColumn { Name = "g", FillWeight = 80 });
        DataGridViewTextBoxColumn cn = new DataGridViewTextBoxColumn { Name = "n", FillWeight = 20 };
        cn.DefaultCellStyle.ForeColor = Tema.Fraco;
        cn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        gradeGrupos.Columns.Add(cn);
        gradeGrupos.SelectionChanged += delegate
        {
            if (preenchendoGrupos) return;
            int i = gradeGrupos.CurrentRow != null ? gradeGrupos.CurrentRow.Index : -1;
            grupoSel = i >= 0 && i < gruposVisiveis.Count ? gruposVisiveis[i].Nome : null;
            MostrarFalas();
        };
        meio.Controls.Add(gradeGrupos, 0, 0);

        Panel direita = new PainelFixo { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Rgb(0x17, 0x1F, 0x26), Padding = new Padding(Tema.D(14)) };
        listaFalas = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = DColor.Transparent, Location = new Point(Tema.D(14), Tema.D(10)) };
        direita.Controls.Add(listaFalas);
        falasVazio = Tema.Rotulo(LiveTxt.T("falas_vazio"), Tema.D(400), Tema.Fraco, 10f);
        falasVazio.Location = new Point(Tema.D(14), Tema.D(14));
        direita.Controls.Add(falasVazio);
        direita.Resize += delegate { larguraFalas = direita.Width - Tema.D(40) - SystemInformation.VerticalScrollBarWidth; falasVazio.MaximumSize = new Size(Math.Max(Tema.D(200), larguraFalas), 0); MostrarFalas(); };
        meio.Controls.Add(direita, 1, 0);
        t.Controls.Add(meio);

        versaoFalas = -1;
        PreencherGrupos();
        atualizarAba = delegate { if (versaoFalas != Falas.Versao && !EditandoFala()) PreencherGrupos(); };
        return t;
    }

    private int larguraFalas = 500;
    private bool preenchendoGrupos;

    private bool EditandoFala()
    {
        if (listaFalas == null) return false;
        foreach (Control c in listaFalas.Controls)
            foreach (Control d in c.Controls)
                if (d is TextBox && d.Focused) return true;
        return false;
    }

    private string NomeVisivel(string nome)
    {
        string m = Ponte.Miolo(nome).Trim();
        TermoGlossario g = Glossario.Obter(m);
        string tr = g != null ? g.Traducao : (LiveMotor.Consultar(nome) ?? Ponte.Traducao(m));
        return tr != null && tr != m ? Ponte.Miolo(tr) + "  ·  " + m : m;
    }

    private static bool BateFala(GrupoFalas g, string b)
    {
        if (b.Length == 0) return true;
        if (g.Nome.ToLowerInvariant().Contains(b)) return true;
        foreach (string t in g.Textos)
        {
            if (t.ToLowerInvariant().Contains(b)) return true;
            string tr = LiveMotor.Consultar(t);
            if (tr != null && tr.ToLowerInvariant().Contains(b)) return true;
        }
        return false;
    }

    private void PreencherGrupos()
    {
        if (gradeGrupos == null || gradeGrupos.IsDisposed) return;
        versaoFalas = Falas.Versao;
        string b = (buscaFalas ?? "").Trim().ToLowerInvariant();
        List<GrupoFalas> todos = Falas.Grupos;
        int[] cont = new int[4];
        gruposVisiveis.Clear();
        foreach (GrupoFalas g in todos)
        {
            if (g.Textos.Count == 0 || !BateFala(g, b)) continue;
            cont[g.Tipo]++;
            if (filtroFalas < 0 || g.Tipo == filtroFalas) gruposVisiveis.Add(g);
        }
        gruposVisiveis.Sort(delegate (GrupoFalas x, GrupoFalas y)
        {
            if (x.Tipo != y.Tipo) return x.Tipo.CompareTo(y.Tipo);
            return y.Textos.Count.CompareTo(x.Textos.Count);
        });

        filtrosFalas.SuspendLayout();
        filtrosFalas.Controls.Clear();
        AddFiltroFalas(-1, LiveTxt.T("todos"), cont[0] + cont[1] + cont[2] + cont[3]);
        for (int i = 0; i < 4; i++) AddFiltroFalas(i, iconeTipo[i] + " " + LiveTxt.T("falas_t" + i), cont[i]);
        filtrosFalas.ResumeLayout(true);

        preenchendoGrupos = true;
        gradeGrupos.Rows.Clear();
        int voltar = 0;
        for (int i = 0; i < gruposVisiveis.Count; i++)
        {
            GrupoFalas g = gruposVisiveis[i];
            gradeGrupos.Rows.Add(iconeTipo[g.Tipo] + "  " + NomeVisivel(g.Nome), g.Textos.Count.ToString());
            if (g.Nome == grupoSel) voltar = i;
        }
        preenchendoGrupos = false;
        if (gruposVisiveis.Count > 0)
        {
            gradeGrupos.CurrentCell = gradeGrupos.Rows[voltar].Cells[0];
            grupoSel = gruposVisiveis[voltar].Nome;
        }
        else grupoSel = null;
        MostrarFalas();
    }

    private void AddFiltroFalas(int id, string texto, int n)
    {
        bool sel = filtroFalas == id;
        string rot = texto + "  " + n;
        Button b = Tema.Botao(rot, Tema.D(28) + TextRenderer.MeasureText(rot, Tema.F(8.5f, sel)).Width, Tema.D(26), sel ? 1 : 0, delegate
        {
            filtroFalas = id;
            versaoFalas = -1;
            PreencherGrupos();
        });
        b.Font = Tema.F(8.5f, sel);
        if (!sel) { b.BackColor = Tema.Rgb(0x16, 0x1D, 0x23); b.FlatAppearance.BorderColor = Tema.Rgb(0x26, 0x31, 0x3A); }
        Tema.Arredondar(b, Tema.D(13));
        b.Margin = new Padding(0, 0, Tema.D(5), Tema.D(6));
        filtrosFalas.Controls.Add(b);
    }

    // textos do grupo selecionado: original (dourado), traducao editavel e acoes
    private void MostrarFalas()
    {
        if (listaFalas == null || listaFalas.IsDisposed) return;
        listaFalas.SuspendLayout();
        listaFalas.Controls.Clear();
        GrupoFalas g = null;
        foreach (GrupoFalas x in gruposVisiveis) if (x.Nome == grupoSel) { g = x; break; }
        falasVazio.Visible = g == null;
        if (g != null)
        {
            int w = Math.Max(Tema.D(300), larguraFalas);
            Label tit = Tema.Rotulo(iconeTipo[g.Tipo] + "  " + NomeVisivel(g.Nome), w, Tema.Dourado, 15f, true);
            tit.Margin = new Padding(0, 0, 0, Tema.D(10));
            listaFalas.Controls.Add(tit);
            for (int i = 0; i < g.Textos.Count; i++) listaFalas.Controls.Add(CartaoFala(g.Textos[i], w));
        }
        listaFalas.ResumeLayout(true);
    }

    private Control CartaoFala(string texto, int w)
    {
        FlowLayoutPanel c = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Tema.Cartao,
            Padding = new Padding(Tema.D(12), Tema.D(10), Tema.D(12), Tema.D(8)),
            Margin = new Padding(0, 0, 0, Tema.D(8)),
            MinimumSize = new Size(w, 0),
            MaximumSize = new Size(w, 0)
        };
        int iw = w - Tema.D(24);
        Label orig = Tema.Rotulo(Ponte.Miolo(texto), iw, Tema.Dourado, 10.5f);
        c.Controls.Add(orig);
        bool travada = Falas.EstaTravada(texto);
        string atual = LiveMotor.Consultar(texto);
        TextBox tr = Tema.Caixa(atual == null ? "" : Ponte.Miolo(atual), iw, 3, 0);
        tr.Font = Tema.F(10.5f);
        c.Controls.Add(tr);
        Label st = Tema.Rotulo(atual == null ? LiveTxt.T("falas_sem_trad") : travada ? "🔒 " + LiveTxt.T("falas_travada") : "", iw, travada ? Tema.Destaque : Tema.Fraco, 8.5f);
        FlowLayoutPanel acoes = Tema.Linha(iw);
        acoes.Margin = new Padding(0, Tema.D(4), 0, 0);
        acoes.Controls.Add(Tema.Botao("💾 " + LiveTxt.T("falas_salvar"), Tema.D(170), Tema.D(28), 1, delegate
        {
            string nova = tr.Text.Trim();
            if (nova.Length == 0) return;
            // devolve as marcas de cor do original, se tinha
            string antes, depois;
            Ponte.Miolo(texto, out antes, out depois);
            string completa = antes + nova + depois;
            Falas.Travar(texto, completa);
            Ponte.MandarTraducao(texto, completa);
            st.Text = "🔒 " + LiveTxt.T("falas_travada");
            st.ForeColor = Tema.Destaque;
        }));
        acoes.Controls.Add(Tema.Botao("↻ " + LiveTxt.T("falas_refazer"), Tema.D(150), Tema.D(28), 0, delegate
        {
            Falas.Destravar(texto);
            LiveMotor.Refazer(texto);
            Ponte.Refazer(texto);
            st.Text = LiveTxt.T("falas_refazendo");
            st.ForeColor = Tema.Aviso;
        }));
        if (travada)
            acoes.Controls.Add(Tema.Botao("🔓 " + LiveTxt.T("det_destravar"), Tema.D(130), Tema.D(28), 0, delegate
            {
                Falas.Destravar(texto);
                st.Text = "";
            }));
        acoes.Controls.Add(Tema.Botao("⇄ " + LiveTxt.T("falas_mover"), Tema.D(130), Tema.D(28), 0, delegate { MoverFala(texto); }));
        c.Controls.Add(acoes);
        c.Controls.Add(st);
        return c;
    }

    // a ligacao automatica errou: escolhe (ou digita) o grupo certo
    private void MoverFala(string texto)
    {
        using (Form d = new Form())
        {
            d.Text = LiveTxt.T("falas_mover");
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.StartPosition = FormStartPosition.CenterParent;
            d.MinimizeBox = false;
            d.MaximizeBox = false;
            d.ClientSize = new Size(Tema.D(420), Tema.D(170));
            d.BackColor = Tema.Cartao;
            d.ForeColor = Tema.Texto;
            d.Font = Tema.F(9f);
            FlowLayoutPanel f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Tema.D(16)) };
            d.Controls.Add(f);
            f.Controls.Add(Tema.Rotulo(LiveTxt.T("falas_mover_dica"), Tema.D(380), Tema.Fraco, 8.5f));
            ComboBox cb = new ComboBox { Width = Tema.D(380), BackColor = Tema.CorCampo, ForeColor = Tema.Texto, Font = Tema.F(10f) };
            foreach (GrupoFalas g in Falas.Grupos) cb.Items.Add(g.Nome);
            cb.Items.Add(LiveTxt.T("falas_sem_nome"));
            f.Controls.Add(cb);
            Button ok = Tema.Botao(LiveTxt.T("falas_mover"), Tema.D(140), Tema.D(30), 1, null);
            ok.DialogResult = DialogResult.OK;
            ok.Margin = new Padding(0, Tema.D(10), 0, 0);
            f.Controls.Add(ok);
            d.AcceptButton = ok;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string novo = (cb.Text ?? "").Trim();
            if (novo.Length == 0) return;
            Falas.Mover(texto, novo);
            grupoSel = novo;
            versaoFalas = -1;
            PreencherGrupos();
        }
    }
}
