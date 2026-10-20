using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

// Menu do tradutor (o F8 do ModLive): uma janela do Windows, fora do jogo. A ponte manda "M" quando o
// jogador aperta F8 e a janela aparece/some. O relogio dela tambem roda o motor e a ponte (uma thread so).
internal sealed class Janela : Form
{
    private const int MaxExtras = 300;

    private readonly Timer relogio = new Timer();
    private readonly NotifyIcon bandeja = new NotifyIcon();
    private bool saindo;
    private bool viuJogo;
    private int semJogo;
    private float proximoStatus;
    private float proximoSalvar;
    private float proximoJogo;
    private int versaoGlos = -1;
    private int logVisto = -1;

    // Geral
    private Button btnAtivar;
    private Label lblEstado, lblPonte, lblStatus, lblErro;
    private ComboBox cbIdioma;
    private readonly List<CheckBox> cbScripts = new List<CheckBox>();
    // Provedor
    private ComboBox cbProv;
    private Label lblDica, lblTeste, lblFalta;
    private TextBox txtUrl, txtChave, txtModelo;
    private NumericUpDown numRpm, numLote;
    private CheckBox chkContexto, chkRaciocinio;
    // Estilo
    private ComboBox cbEstilo;
    private Label lblResumo;
    private TextBox txtExtras, txtJogo;
    // Glossario
    private TextBox txtBusca;
    private ListView lvGlos;
    // Log
    private TextBox txtLog;

    private bool carregando;

    public Janela(bool mostrar)
    {
        Text = "Tradutor UE5 - " + LiveTxt.T("brand_sub");
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 560);
        MinimumSize = new Size(600, 460);
        Font = new Font("Segoe UI", 9.5f);
        ShowInTaskbar = true;

        TabControl abas = new TabControl { Dock = DockStyle.Fill };
        abas.TabPages.Add(PaginaGeral());
        abas.TabPages.Add(PaginaProvedor());
        abas.TabPages.Add(PaginaEstilo());
        abas.TabPages.Add(PaginaGlossario());
        abas.TabPages.Add(PaginaLog());
        Controls.Add(abas);

        bandeja.Icon = SystemIcons.Application;
        bandeja.Text = "Tradutor UE5";
        bandeja.Visible = true;
        bandeja.DoubleClick += delegate { Alternar(true); };
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(LiveTxt.T("abrir_menu"), null, delegate { Alternar(true); });
        menu.Items.Add(LiveTxt.T("sair"), null, delegate { Sair(); });
        bandeja.ContextMenuStrip = menu;

        Ponte.MenuPedido += delegate { Alternar(!Visible); };

        relogio.Interval = 50;
        relogio.Tick += Tick;
        relogio.Start();

        Recarregar();
        if (!mostrar)
        {
            // comeca escondida: so aparece com F8 no jogo ou pela bandeja
            WindowState = FormWindowState.Minimized;
            Load += delegate { Hide(); WindowState = FormWindowState.Normal; };
        }
    }

    // ---------------- relogio ----------------

    private void Tick(object sender, EventArgs e)
    {
        float agora = Time.unscaledTime;
        try { LiveMotor.Tick(); }
        catch (Exception ex) { LiveLog.Erro("le_parte", ex, "engine"); }
        try { Ponte.Tick(); }
        catch (Exception ex) { LiveLog.Erro("le_parte", ex, "bridge"); }

        if (agora >= proximoSalvar)
        {
            proximoSalvar = agora + 10f;
            LiveConfig.SalvarSeSujo();
            Glossario.SalvarSeSujo();
        }
        if (agora >= proximoJogo)
        {
            proximoJogo = agora + 5f;
            // fecha sozinho quando o jogo fecha (so depois de ter visto o jogo aberto)
            if (Program.JogoAberto()) { viuJogo = true; semJogo = 0; }
            else if (viuJogo && ++semJogo >= 3) Sair();
        }
        if (agora >= proximoStatus && Visible)
        {
            proximoStatus = agora + 0.5f;
            AtualizarStatus();
        }
    }

    private void Alternar(bool mostrar)
    {
        if (mostrar)
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            // por cima do jogo (que esta em primeiro plano)
            TopMost = true;
            Activate();
            TopMost = false;
            Recarregar();
        }
        else Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // o X so esconde: o tradutor continua rodando ate o jogo fechar
        if (!saindo && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void Sair()
    {
        if (saindo) return;
        saindo = true;
        relogio.Stop();
        Program.Salvar();
        bandeja.Visible = false;
        bandeja.Dispose();
        Close();
        Application.Exit();
    }

    // trocou algo que muda as traducoes: esvazia as memorias e a tela e traduzida de novo
    private static void Mudou()
    {
        LiveConfig.Marcar();
        LiveMotor.Reiniciar();
        LiveMotor.VarrerAgora = true;
    }

    // ---------------- aba Geral ----------------

    private TabPage PaginaGeral()
    {
        TabPage p = new TabPage(LiveTxt.T("nav_geral"));
        FlowLayoutPanel f = Coluna();
        p.Controls.Add(f);

        btnAtivar = new Button { Width = 260, Height = 40, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold) };
        btnAtivar.Click += delegate
        {
            LiveConfig.Ativo = !LiveConfig.Ativo;
            if (LiveConfig.Ativo) LiveMotor.VarrerAgora = true;
            LiveLog.Info(LiveConfig.Ativo ? "lg_comecou" : "lg_pausada");
            AtualizarStatus();
        };
        f.Controls.Add(btnAtivar);
        lblEstado = Rotulo("");
        f.Controls.Add(lblEstado);

        f.Controls.Add(Titulo(LiveTxt.T("traduzir_para")));
        cbIdioma = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        for (int i = 0; i < LiveConfig.Idiomas.Length; i++) cbIdioma.Items.Add(LiveConfig.Idiomas[i][1]);
        cbIdioma.SelectedIndexChanged += delegate
        {
            if (carregando || cbIdioma.SelectedIndex < 0) return;
            string novo = LiveConfig.Idiomas[cbIdioma.SelectedIndex][0];
            if (novo == LiveConfig.Idioma) return;
            LiveConfig.Idioma = novo;
            TradCache.TrocouIdioma();
            Glossario.TrocouIdioma();
            Mudou();
        };
        f.Controls.Add(cbIdioma);

        f.Controls.Add(Titulo(LiveTxt.T("o_que")));
        string[] nomes = new string[] { "fonte_zh", "fonte_ja", "fonte_ko", "fonte_cir", "fonte_lat", "fonte_out" };
        FlowLayoutPanel linha = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(660, 0) };
        for (int i = 0; i < nomes.Length; i++)
        {
            int bit = 1 << i;
            CheckBox c = new CheckBox { Text = LiveTxt.T(nomes[i]), AutoSize = true, Tag = bit };
            c.CheckedChanged += delegate
            {
                if (carregando) return;
                int v = LiveConfig.Fontes;
                v = c.Checked ? (v | bit) : (v & ~bit);
                if (v == LiveConfig.Fontes) return;
                LiveConfig.Fontes = v;
                Mudou();
            };
            cbScripts.Add(c);
            linha.Controls.Add(c);
        }
        f.Controls.Add(linha);
        f.Controls.Add(Rotulo(LiveTxt.T("fonte_dica"), true));

        f.Controls.Add(Titulo("Status"));
        lblPonte = Rotulo("");
        lblStatus = Rotulo("");
        lblErro = Rotulo("");
        lblErro.ForeColor = System.Drawing.Color.Firebrick;
        f.Controls.Add(lblPonte);
        f.Controls.Add(lblStatus);
        f.Controls.Add(lblErro);
        f.Controls.Add(Rotulo(LiveTxt.T("menu_dica"), true));
        return p;
    }

    // ---------------- aba Provedor ----------------

    private TabPage PaginaProvedor()
    {
        TabPage p = new TabPage(LiveTxt.T("nav_prov"));
        FlowLayoutPanel f = Coluna();
        p.Controls.Add(f);

        f.Controls.Add(Titulo(LiveTxt.T("servico")));
        cbProv = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        for (int i = 0; i < LiveConfig.Provedores.Length; i++) cbProv.Items.Add(LiveConfig.Provedores[i].Nome);
        cbProv.SelectedIndexChanged += delegate
        {
            if (carregando || cbProv.SelectedIndex < 0) return;
            Provedor pr = LiveConfig.Provedores[cbProv.SelectedIndex];
            if (pr.Id == LiveConfig.ProvedorId) return;
            LiveConfig.ProvedorId = pr.Id;
            if (pr.Tipo != 0) LiveConfig.UltimoProvedorComChave = pr.Id;
            LiveMotor.Destravar();
            Mudou();
            Recarregar();
        };
        f.Controls.Add(cbProv);
        lblDica = Rotulo("", true);
        f.Controls.Add(lblDica);

        f.Controls.Add(Titulo(LiveTxt.T("url")));
        txtUrl = new TextBox { Width = 600 };
        txtUrl.TextChanged += delegate
        {
            if (carregando) return;
            string id = LiveConfig.ProvedorId;
            if (txtUrl.Text.Trim() == LiveConfig.Url(id)) return;
            LiveConfig.Urls[id] = txtUrl.Text.Trim();
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        f.Controls.Add(txtUrl);

        f.Controls.Add(Titulo(LiveTxt.T("chave_obrig")));
        txtChave = new TextBox { Width = 600, UseSystemPasswordChar = true };
        txtChave.TextChanged += delegate
        {
            if (carregando) return;
            string id = LiveConfig.ProvedorId;
            if (txtChave.Text.Trim() == LiveConfig.Chave(id)) return;
            LiveConfig.Chaves[id] = txtChave.Text.Trim();
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        f.Controls.Add(txtChave);
        CheckBox mostrar = new CheckBox { Text = LiveTxt.T("mostrar").Trim(), AutoSize = true };
        mostrar.CheckedChanged += delegate { txtChave.UseSystemPasswordChar = !mostrar.Checked; };
        f.Controls.Add(mostrar);

        f.Controls.Add(Titulo(LiveTxt.T("modelo")));
        txtModelo = new TextBox { Width = 320 };
        txtModelo.TextChanged += delegate
        {
            if (carregando) return;
            string id = LiveConfig.ProvedorId;
            if (txtModelo.Text.Trim() == LiveConfig.Modelo(id)) return;
            LiveConfig.Modelos[id] = txtModelo.Text.Trim();
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        f.Controls.Add(txtModelo);
        lblFalta = Rotulo("");
        lblFalta.ForeColor = System.Drawing.Color.Firebrick;
        f.Controls.Add(lblFalta);

        Button testar = new Button { Text = LiveTxt.T("testar"), Width = 200, Height = 30 };
        testar.Click += delegate { LiveMotor.Testar(); };
        f.Controls.Add(testar);
        lblTeste = Rotulo("", true);
        f.Controls.Add(lblTeste);

        FlowLayoutPanel ritmo = new FlowLayoutPanel { AutoSize = true };
        ritmo.Controls.Add(Rotulo(LiveTxt.T("rpm")));
        numRpm = new NumericUpDown { Minimum = 1, Maximum = 600, Width = 70 };
        numRpm.ValueChanged += delegate { if (!carregando) { LiveConfig.Rpm = (int)numRpm.Value; LiveConfig.Marcar(); } };
        ritmo.Controls.Add(numRpm);
        ritmo.Controls.Add(Rotulo(LiveTxt.T("lote")));
        numLote = new NumericUpDown { Minimum = 1, Maximum = 30, Width = 60 };
        numLote.ValueChanged += delegate { if (!carregando) { LiveConfig.Lote = (int)numLote.Value; LiveConfig.Marcar(); } };
        ritmo.Controls.Add(numLote);
        f.Controls.Add(ritmo);
        f.Controls.Add(Rotulo(LiveTxt.T("rpm_aviso"), true));

        chkContexto = new CheckBox { Text = LiveTxt.T("contexto").Trim(), AutoSize = true };
        chkContexto.CheckedChanged += delegate { if (!carregando) { LiveConfig.Contexto = chkContexto.Checked; LiveConfig.Marcar(); } };
        f.Controls.Add(chkContexto);
        chkRaciocinio = new CheckBox { Text = LiveTxt.T("raciocinio"), AutoSize = true };
        chkRaciocinio.CheckedChanged += delegate
        {
            if (carregando) return;
            LiveConfig.Raciocinio = chkRaciocinio.Checked;
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        f.Controls.Add(chkRaciocinio);
        f.Controls.Add(Rotulo(LiveTxt.T("raciocinio_dica"), true));
        return p;
    }

    // ---------------- aba Estilo ----------------

    private TabPage PaginaEstilo()
    {
        TabPage p = new TabPage(LiveTxt.T("nav_estilo"));
        FlowLayoutPanel f = Coluna();
        p.Controls.Add(f);

        f.Controls.Add(Titulo(LiveTxt.T("estilo_titulo")));
        cbEstilo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        for (int i = 0; i < LiveConfig.Estilos.Length; i++) cbEstilo.Items.Add(LiveTxt.T("estilo" + i));
        cbEstilo.SelectedIndexChanged += delegate
        {
            if (carregando || cbEstilo.SelectedIndex < 0) return;
            LiveConfig.Estilo = cbEstilo.SelectedIndex;
            LiveConfig.Marcar();
            lblResumo.Text = LiveTxt.T("resumo" + LiveConfig.Estilo);
        };
        f.Controls.Add(cbEstilo);
        lblResumo = Rotulo("", true);
        f.Controls.Add(lblResumo);

        f.Controls.Add(Titulo(LiveTxt.T("extras_titulo")));
        txtExtras = new TextBox { Width = 640, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = MaxExtras };
        txtExtras.TextChanged += delegate
        {
            if (carregando) return;
            LiveConfig.PromptExtra = txtExtras.Text;
            LiveConfig.Marcar();
        };
        f.Controls.Add(txtExtras);
        f.Controls.Add(Rotulo(LiveTxt.T("estilo_nota"), true));

        f.Controls.Add(Titulo(LiveTxt.T("jogo_titulo")));
        txtJogo = new TextBox { Width = 640, Height = 60, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 400 };
        txtJogo.TextChanged += delegate
        {
            if (carregando) return;
            string v = txtJogo.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            LiveConfig.Jogo = v.Length > 0 ? v : LiveConfig.JogoPadrao;
            LiveConfig.Marcar();
        };
        f.Controls.Add(txtJogo);
        Button padrao = new Button { Text = LiveTxt.T("jogo_padrao"), AutoSize = true };
        padrao.Click += delegate { txtJogo.Text = LiveConfig.JogoPadrao; };
        f.Controls.Add(padrao);
        return p;
    }

    // ---------------- aba Glossario ----------------

    private TabPage PaginaGlossario()
    {
        TabPage p = new TabPage(LiveTxt.T("nav_glos"));
        TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        p.Controls.Add(t);

        FlowLayoutPanel topo = new FlowLayoutPanel { AutoSize = true };
        topo.Controls.Add(Rotulo(LiveTxt.T("glos_buscar")));
        txtBusca = new TextBox { Width = 260 };
        txtBusca.TextChanged += delegate { versaoGlos = -1; AtualizarGlossario(); };
        topo.Controls.Add(txtBusca);
        Button novo = new Button { Text = LiveTxt.T("glos_novo"), AutoSize = true };
        novo.Click += delegate { EditarTermo(null); };
        topo.Controls.Add(novo);
        t.Controls.Add(topo);

        lvGlos = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
        lvGlos.Columns.Add(LiveTxt.T("col_orig"), 220);
        lvGlos.Columns.Add(LiveTxt.T("col_trad"), 220);
        lvGlos.Columns.Add(LiveTxt.T("col_cat"), 110);
        lvGlos.Columns.Add(LiveTxt.T("col_trava"), 80);
        lvGlos.DoubleClick += delegate
        {
            if (lvGlos.SelectedItems.Count > 0) EditarTermo(lvGlos.SelectedItems[0].Tag as TermoGlossario);
        };
        lvGlos.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || lvGlos.SelectedItems.Count == 0) return;
            foreach (ListViewItem it in lvGlos.SelectedItems) Glossario.Remover(it.Tag as TermoGlossario);
            AtualizarGlossario();
        };
        t.Controls.Add(lvGlos);
        t.Controls.Add(Rotulo(LiveTxt.T("glos_editar_dica"), true));
        p.Enter += delegate { AtualizarGlossario(); };
        return p;
    }

    private void AtualizarGlossario()
    {
        if (lvGlos == null || versaoGlos == Glossario.Versao) return;
        versaoGlos = Glossario.Versao;
        string busca = (txtBusca.Text ?? "").Trim();
        lvGlos.BeginUpdate();
        lvGlos.Items.Clear();
        List<TermoGlossario> lista = Glossario.Lista;
        for (int i = 0; i < lista.Count && lvGlos.Items.Count < 2000; i++)
        {
            TermoGlossario t = lista[i];
            if (busca.Length > 0
                && t.Original.IndexOf(busca, StringComparison.OrdinalIgnoreCase) < 0
                && t.Traducao.IndexOf(busca, StringComparison.OrdinalIgnoreCase) < 0) continue;
            ListViewItem it = new ListViewItem(new string[] { t.Original, t.Traducao, Categorias.Nome(t.Cat), t.Travado ? LiveTxt.T("travado") : "" });
            it.Tag = t;
            lvGlos.Items.Add(it);
        }
        lvGlos.EndUpdate();
    }

    // t = null: termo novo
    private void EditarTermo(TermoGlossario t)
    {
        using (Form d = new Form())
        {
            d.Text = t == null ? LiveTxt.T("glos_novo") : LiveTxt.T("editar");
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.StartPosition = FormStartPosition.CenterParent;
            d.ClientSize = new Size(460, 230);
            d.MinimizeBox = false;
            d.MaximizeBox = false;
            FlowLayoutPanel f = Coluna();
            d.Controls.Add(f);
            f.Controls.Add(Rotulo(LiveTxt.T("novo_orig")));
            TextBox orig = new TextBox { Width = 420, Text = t != null ? t.Original : "", ReadOnly = t != null };
            f.Controls.Add(orig);
            f.Controls.Add(Rotulo(LiveTxt.T("lbl_trad")));
            TextBox trad = new TextBox { Width = 420, Text = t != null ? t.Traducao : "" };
            f.Controls.Add(trad);
            ComboBox cat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            int[] ordem = Categorias.Ordem();
            for (int i = 0; i < ordem.Length; i++)
            {
                cat.Items.Add(Categorias.Nome(ordem[i]));
                if (t != null && t.Cat == ordem[i]) cat.SelectedIndex = i;
            }
            if (cat.SelectedIndex < 0) cat.SelectedIndex = Array.IndexOf(ordem, 0);
            f.Controls.Add(cat);
            Button ok = new Button { Text = LiveTxt.T("ok"), DialogResult = DialogResult.OK, Width = 100 };
            f.Controls.Add(ok);
            d.AcceptButton = ok;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string o = orig.Text.Trim();
            string n = trad.Text.Trim();
            if (o.Length == 0 || n.Length == 0) return;
            Glossario.Definir(o, n, ordem[Math.Max(0, cat.SelectedIndex)], true);
            // a ponte so troca o que ainda nao tem traducao: devolve os originais e pede de novo
            LiveMotor.Reiniciar();
            LiveMotor.VarrerAgora = true;
            AtualizarGlossario();
        }
    }

    // ---------------- aba Log ----------------

    private TabPage PaginaLog()
    {
        TabPage p = new TabPage(LiveTxt.T("nav_log"));
        TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        p.Controls.Add(t);

        FlowLayoutPanel topo = new FlowLayoutPanel { AutoSize = true };
        Button pasta = new Button { Text = LiveTxt.T("log_pasta"), AutoSize = true };
        pasta.Click += delegate
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + LiveConfig.Pasta + "\"") { UseShellExecute = true }); }
            catch (Exception e) { LiveLog.Erro("le_parte", e, "explorer"); }
        };
        topo.Controls.Add(pasta);
        Button limpar = new Button { Text = LiveTxt.T("limpar_cache"), AutoSize = true };
        limpar.Click += delegate
        {
            string aviso = string.Format(LiveTxt.T("limpar_aviso"), TradCache.Total);
            if (MessageBox.Show(this, aviso, LiveTxt.T("limpar_cache"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int n = TradCache.Total;
            TradCache.Limpar();
            Glossario.LimparNaoTravados();
            LiveMotor.Reiniciar();
            LiveMotor.VarrerAgora = true;
            LiveLog.Info("lg_cache_limpo", n);
        };
        topo.Controls.Add(limpar);
        t.Controls.Add(topo);

        txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
        t.Controls.Add(txtLog);
        return p;
    }

    // ---------------- atualizar ----------------

    // poe na tela o que esta na configuracao
    private void Recarregar()
    {
        carregando = true;
        try
        {
            for (int i = 0; i < LiveConfig.Idiomas.Length; i++)
                if (LiveConfig.Idiomas[i][0] == LiveConfig.Idioma) cbIdioma.SelectedIndex = i;
            for (int i = 0; i < cbScripts.Count; i++)
                cbScripts[i].Checked = (LiveConfig.Fontes & (int)cbScripts[i].Tag) != 0;

            Provedor pr = LiveConfig.Atual;
            cbProv.SelectedIndex = Array.IndexOf(LiveConfig.Provedores, pr);
            lblDica.Text = LiveTxt.T("dica_" + pr.Id);
            bool ia = pr.Tipo != 0;
            txtUrl.Enabled = ia;
            txtChave.Enabled = ia;
            txtModelo.Enabled = ia;
            txtUrl.Text = ia ? LiveConfig.Url(pr.Id) : "";
            txtChave.Text = ia ? LiveConfig.Chave(pr.Id) : "";
            txtModelo.Text = ia ? LiveConfig.Modelo(pr.Id) : "";
            numRpm.Value = Mathf.Clamp(LiveConfig.Rpm, 1, 600);
            numLote.Value = Mathf.Clamp(LiveConfig.Lote, 1, 30);
            numRpm.Enabled = ia;
            numLote.Enabled = ia;
            chkContexto.Checked = LiveConfig.Contexto;
            chkRaciocinio.Checked = LiveConfig.Raciocinio;

            cbEstilo.SelectedIndex = Mathf.Clamp(LiveConfig.Estilo, 0, LiveConfig.Estilos.Length - 1);
            lblResumo.Text = LiveTxt.T("resumo" + cbEstilo.SelectedIndex);
            txtExtras.Text = LiveConfig.PromptExtra ?? "";
            txtJogo.Text = LiveConfig.Jogo ?? "";
        }
        finally
        {
            carregando = false;
        }
        AtualizarStatus();
    }

    private void AtualizarStatus()
    {
        bool ativo = LiveConfig.Ativo;
        btnAtivar.Text = LiveTxt.T(ativo ? "pausar" : "comecar");
        btnAtivar.BackColor = ativo ? System.Drawing.Color.FromArgb(240, 190, 90) : System.Drawing.Color.FromArgb(90, 200, 190);
        lblEstado.Text = LiveTxt.T(ativo ? "trad_rodando" : "trad_pausada");

        bool conectada = (DateTime.Now - Ponte.UltimoContato).TotalSeconds < 30 || Ponte.Recebidos > 0;
        lblPonte.Text = LiveTxt.T(conectada ? "ponte_ok" : "ponte_espera") + "\n"
            + string.Format(LiveTxt.T("ponte_status"), Ponte.Recebidos, Ponte.Enviados, Ponte.Esperando);
        lblStatus.Text = string.Format(LiveTxt.T("status"), LiveMotor.NaFila, LiveMotor.Traduzidos, TradCache.Total, LiveMotor.Pedidos, LiveMotor.Erros);
        string erro = LiveMotor.UltimoErro;
        lblErro.Text = string.IsNullOrEmpty(erro) ? "" : LiveTxt.T("ultimo_erro") + erro;

        string motivo;
        lblFalta.Text = Provedores.Configurado(LiveConfig.Atual, out motivo) ? "" : LiveTxt.T("falta") + motivo;
        lblTeste.Text = LiveMotor.Teste;

        if (logVisto != LiveLog.Count || LiveLog.Count == 300)
        {
            logVisto = LiveLog.Count;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < LiveLog.Count; i++) sb.Append(LiveLog.Texto(i)).Append("\r\n");
            if (txtLog.Text.Length != sb.Length)
            {
                txtLog.Text = sb.ToString();
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();
            }
        }
        AtualizarGlossario();
    }

    // ---------------- util ----------------

    private static FlowLayoutPanel Coluna()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10)
        };
    }

    private static Label Titulo(string s)
    {
        return new Label { Text = s, AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(3, 12, 3, 3) };
    }

    private static Label Rotulo(string s, bool fraco = false)
    {
        Label l = new Label { Text = s, AutoSize = true, MaximumSize = new Size(660, 0) };
        if (fraco) l.ForeColor = System.Drawing.Color.DimGray;
        return l;
    }
}
