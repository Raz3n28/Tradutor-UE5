using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using DColor = System.Drawing.Color;

// Menu do tradutor (o F8 do ModLive): uma janela do Windows, fora do jogo, com o visual escuro do ModLive
// (menu lateral numerado, botao de pausar, idioma do menu embaixo). A ponte manda "M" quando o jogador
// aperta F8 e a janela aparece/some. O relogio dela tambem roda o motor e a ponte (uma thread so).
//
// Tudo e montado no codigo. Trocar o idioma do menu ou a escala (aba Interface) monta a janela de novo.
// Cada aba e montada quando aberta; "atualizarAba" refaz so os numeros que mudam (status, contadores).
internal sealed partial class Janela : Form
{
    private const int MaxExtras = 300;

    // abas: 0 Como usar, 1 Painel, 2 Geral, 3 Provedor, 4 Estilo, 5 Glossario, 6 Falas e descricoes, 7 Categorias,
    // 8 Configuracoes, 9 Log, 10 Interface (o numero na tela e o indice: 00..10)
    private static readonly string[] NomesAbas = new string[]
    {
        "nav_tutorial", "nav_painel", "nav_geral", "nav_prov", "nav_estilo", "nav_glos", "nav_falas", "nav_cats", "nav_avanc", "nav_log", "nav_interface"
    };
    private const int AbaTutorial = 0, AbaPainel = 1, AbaGeral = 2, AbaProv = 3, AbaEstilo = 4, AbaGlos = 5, AbaFalas = 6, AbaCats = 7, AbaAvanc = 8, AbaLog = 9, AbaInterface = 10;

    private readonly Timer relogio = new Timer();
    private readonly NotifyIcon bandeja = new NotifyIcon();
    private bool saindo;
    private bool viuJogo;
    private int semJogo;
    private float proximoStatus;
    private float proximoSalvar;
    private float proximoJogo;

    private int aba = AbaPainel;
    private Panel lateral, conteudo, corpo;
    private Label lblTitulo, lblRodape;
    private Button btnAtivar;
    private readonly List<Button> botoesAbas = new List<Button>();
    private FlowLayoutPanel pagina;
    private Action atualizarAba;
    private int L;   // largura util da pagina (cartao)

    // estado das abas (sobrevive a troca de aba)
    private string busca = "";
    private int filtroGlos = FiltroTodos;
    private const int FiltroTodos = -1, FiltroConflitos = -2, FiltroNovos = -3, FiltroTravados = -4;
    private int catSel = 1;
    private bool catNova;
    private bool catConfApagar;
    private string catMsg = "";
    private bool catMsgErro;
    private bool confLimpar;
    private string msgAvanc = "";
    private bool msgAvancErro;
    private bool mostrarChave;

    public Janela(bool mostrar)
    {
        Text = "Universal Translator UE5 - " + LiveTxt.T("brand_sub");
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        BackColor = Tema.Fundo;
        ForeColor = Tema.Texto;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (capturando) { CapturarTecla(e); return; }
            if (e.KeyCode == Keys.F8) Hide();
        };

        bandeja.Icon = SystemIcons.Application;
        bandeja.Text = "Universal Translator UE5";
        bandeja.Visible = true;
        bandeja.DoubleClick += delegate { Alternar(true); };
        Ponte.MenuPedido += delegate { Alternar(!Visible); };

        Construir(true);

        relogio.Interval = 50;
        relogio.Tick += Tick;
        relogio.Start();

        if (!mostrar)
        {
            // comeca escondida: so aparece com F8 no jogo ou pela bandeja
            WindowState = FormWindowState.Minimized;
            Load += delegate { Hide(); WindowState = FormWindowState.Normal; };
        }
    }

    // ---------------- montar a janela ----------------

    private void Construir(bool primeira)
    {
        Tema.Escala = LiveConfig.Escala / 100f;
        SuspendLayout();
        Controls.Clear();
        botoesAbas.Clear();
        Font = Tema.F(9f);
        MinimumSize = new Size(Tema.D(860), Tema.D(600));
        if (primeira || ClientSize.Width < MinimumSize.Width) ClientSize = new Size(Tema.D(1100), Tema.D(780));

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(LiveTxt.T("abrir_menu"), null, delegate { Alternar(true); });
        menu.Items.Add(LiveTxt.T("sair"), null, delegate { Sair(); });
        bandeja.ContextMenuStrip = menu;

        // conteudo (direita): titulo + Fechar, pagina com rolagem, rodape com os numeros
        conteudo = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Conteudo, Padding = new Padding(Tema.D(20), Tema.D(10), Tema.D(14), Tema.D(6)) };
        Panel topo = new Panel { Dock = DockStyle.Top, Height = Tema.D(46), BackColor = Tema.Conteudo };
        lblTitulo = new Label { AutoSize = true, Font = Tema.F(15f, true), ForeColor = Tema.Texto, Location = new Point(0, Tema.D(6)) };
        topo.Controls.Add(lblTitulo);
        Button fechar = Tema.Botao(LiveTxt.T("fechar"), Tema.D(120), Tema.D(30), 0, delegate { Hide(); });
        fechar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topo.Controls.Add(fechar);
        topo.Resize += delegate { fechar.Location = new Point(topo.Width - fechar.Width - Tema.D(4), Tema.D(6)); };
        lblRodape = new Label { Dock = DockStyle.Bottom, Height = Tema.D(28), ForeColor = Tema.Fraco, Font = Tema.F(8.5f), TextAlign = ContentAlignment.MiddleLeft };
        corpo = new PainelFixo { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Conteudo };
        conteudo.Controls.Add(corpo);
        conteudo.Controls.Add(lblRodape);
        conteudo.Controls.Add(topo);

        lateral = MontarLateral();
        Controls.Add(conteudo);
        Controls.Add(lateral);
        ResumeLayout(true);
        fechar.Location = new Point(topo.Width - fechar.Width - Tema.D(4), Tema.D(6));

        MostrarAba(aba);
        AtualizarStatus();
    }

    private Panel MontarLateral()
    {
        int w = Tema.D(210);
        Panel p = new PainelLiso { Dock = DockStyle.Left, Width = w, BackColor = Tema.Lateral };
        int x = Tema.D(16), y = Tema.D(16), iw = w - Tema.D(32);

        Label marca = new Label { Text = "Universal\nTranslator", AutoSize = true, Font = Tema.F(15f, true), ForeColor = Tema.Destaque, Location = new Point(x, y) };
        p.Controls.Add(marca);
        y += marca.PreferredHeight + Tema.D(2);
        Label por = new Label { Text = "UE5 · By Raz3n", AutoSize = true, Font = Tema.F(8.5f), ForeColor = Tema.Fraco, Location = new Point(x, y) };
        p.Controls.Add(por);
        y += por.PreferredHeight + Tema.D(14);

        btnAtivar = Tema.Botao("", iw, Tema.D(34), 2, delegate
        {
            LiveConfig.Ativo = !LiveConfig.Ativo;
            if (LiveConfig.Ativo) LiveMotor.VarrerAgora = true;
            LiveLog.Info(LiveConfig.Ativo ? "lg_comecou" : "lg_pausada");
            AtualizarStatus();
            if (aba == AbaPainel || aba == AbaGeral) MostrarAba(aba);
        });
        btnAtivar.Location = new Point(x, y);
        p.Controls.Add(btnAtivar);
        y += btnAtivar.Height + Tema.D(14);

        for (int i = 0; i < NomesAbas.Length; i++)
        {
            int idx = i;
            Button b = new Button
            {
                Text = "  " + i.ToString("00") + "    " + LiveTxt.T(NomesAbas[i]),
                TextAlign = ContentAlignment.MiddleLeft,
                Width = iw,
                Height = Tema.D(30),
                Location = new Point(x, y),
                Font = Tema.F(9.5f)
            };
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Tema.CartaoClaro;
            b.Cursor = Cursors.Hand;
            b.Click += delegate { MostrarAba(idx); };
            botoesAbas.Add(b);
            p.Controls.Add(b);
            y += b.Height + Tema.D(2);
        }

        // idioma do menu (embaixo)
        int bw = (iw - Tema.D(8)) / 3;
        Panel idiomas = new Panel { Width = iw, Height = Tema.D(22) + 2 * (Tema.D(28) + Tema.D(4)), BackColor = Tema.Lateral };
        idiomas.Controls.Add(new Label { Text = LiveTxt.T("menu_lingua").ToUpperInvariant(), AutoSize = true, ForeColor = Tema.Fraco, Font = Tema.F(8f), Location = new Point(0, 0) });
        for (int i = 0; i < LiveTxt.Codigos.Length; i++)
        {
            int idx = i;
            Button b = Tema.Botao(LiveTxt.Codigos[i], bw, Tema.D(28), i == LiveTxt.Atual ? 1 : 0, delegate
            {
                if (LiveTxt.Atual == idx) return;
                LiveTxt.Atual = idx;
                LiveConfig.Marcar();
                Text = "Universal Translator UE5 - " + LiveTxt.T("brand_sub");
                Construir(false);
            });
            b.Location = new Point((i % 3) * (bw + Tema.D(4)), Tema.D(22) + (i / 3) * (Tema.D(28) + Tema.D(4)));
            b.Margin = new Padding(0);
            idiomas.Controls.Add(b);
        }
        idiomas.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        idiomas.BackColor = DColor.Transparent;
        p.Controls.Add(idiomas);
        // card de apoio (Ko-fi / Pix) logo acima do idioma; some se a janela for baixa demais
        Panel apoio = MontarApoio(iw);
        p.Controls.Add(apoio);
        int fimAbas = y;
        Action posicionar = delegate
        {
            int yi = Math.Max(fimAbas + Tema.D(10), p.Height - idiomas.Height - Tema.D(14));
            idiomas.Location = new Point(x, yi);
            int ya = yi - apoio.Height - Tema.D(12);
            apoio.Location = new Point(x, ya);
            apoio.Visible = ya >= fimAbas + Tema.D(8);
        };
        p.Resize += delegate { posicionar(); };
        posicionar();
        // montanhas em tinta no pe do menu
        p.Paint += delegate (object s, PaintEventArgs e)
        {
            Image m = Tema.Imagem("montanhas.png");
            if (m != null) e.Graphics.DrawImage(m, 0, p.Height - Tema.D(240), w, Tema.D(240));
        };
        return p;
    }

    private void MostrarAba(int n)
    {
        if (n != aba)
        {
            confLimpar = false;
            catConfApagar = false;
        }
        aba = n;
        for (int i = 0; i < botoesAbas.Count; i++)
        {
            Button b = botoesAbas[i];
            bool sel = i == aba;
            b.BackColor = sel ? Tema.DestaqueEscuro : Tema.Lateral;
            b.ForeColor = sel ? Tema.Destaque : Tema.Texto;
            b.Font = Tema.F(9.5f, sel);
        }
        lblTitulo.Text = LiveTxt.T(NomesAbas[aba]);
        atualizarAba = null;

        corpo.SuspendLayout();
        corpo.Controls.Clear();
        corpo.AutoScroll = true;
        corpo.AutoScrollPosition = new Point(0, 0);
        // largura util: a do corpo menos a barra de rolagem
        L = Math.Max(Tema.D(480), corpo.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Tema.D(24));

        if (aba == AbaGlos || aba == AbaLog || aba == AbaFalas)
        {
            // abas "cheias": a tabela/o log ocupam a altura toda (rolagem propria)
            corpo.AutoScroll = false;
            Control c = aba == AbaGlos ? PaginaGlossario() : aba == AbaFalas ? PaginaFalas() : PaginaLog();
            c.Dock = DockStyle.Fill;
            corpo.Controls.Add(c);
        }
        else
        {
            pagina = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                BackColor = Tema.Conteudo,
                Location = new Point(0, 0),
                Padding = new Padding(0, 0, 0, Tema.D(10))
            };
            switch (aba)
            {
                case AbaTutorial: PaginaTutorial(); break;
                case AbaPainel: PaginaPainel(); break;
                case AbaGeral: PaginaGeral(); break;
                case AbaProv: PaginaProvedor(); break;
                case AbaEstilo: PaginaEstilo(); break;
                case AbaCats: PaginaCategorias(); break;
                case AbaAvanc: PaginaAvancado(); break;
                case AbaInterface: PaginaInterface(); break;
            }
            corpo.Controls.Add(pagina);
        }
        corpo.ResumeLayout(true);
        if (atualizarAba != null) atualizarAba();
    }

    private int ultimaLargura;

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        RemontarSeMudou();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // maximizar/restaurar nao passa pelo ResizeEnd
        if (WindowState != FormWindowState.Minimized && corpo != null && !Capture) RemontarSeMudou();
    }

    private void RemontarSeMudou()
    {
        if (corpo == null || corpo.ClientSize.Width == ultimaLargura) return;
        ultimaLargura = corpo.ClientSize.Width;
        if (aba != AbaGlos && aba != AbaLog && aba != AbaFalas) MostrarAba(aba);
        else if (aba == AbaGlos) AjustarGlossario();
    }

    // a pagina do glossario nao e remontada (perderia a tabela): so as linhas mudam de largura e quebram de novo
    private void AjustarGlossario()
    {
        int w = Math.Max(Tema.D(480), corpo.ClientSize.Width - Tema.D(8));
        for (int i = 0; i < larguraGlos.Count; i++)
        {
            Control c = larguraGlos[i].Key;
            if (c.IsDisposed) continue;
            int lw = w - larguraGlos[i].Value;
            c.MaximumSize = new Size(lw, 0);
            if (c is FlowLayoutPanel) c.MinimumSize = new Size(lw, 0);
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
            MostrarAba(aba);
        }
        else Hide();
    }

    // ---------------- tecla global: pausar/continuar sem abrir o menu ----------------
    // RegisterHotKey do Windows: funciona com o jogo em primeiro plano e nao passa pelo UE4SS.
    // A tecla escolhida deixa de chegar no jogo (o Windows entrega so para o tradutor).

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint tipo);
    private const int WM_HOTKEY = 0x0312;
    private const int IdTecla = 0x5455;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
    private bool teclaRegistrada;
    private string teclaErro = "";
    private bool capturando;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegistrarTecla();
    }

    private void RegistrarTecla()
    {
        try { RegistrarTeclaInterno(); }
        catch (Exception e)
        {
            teclaRegistrada = false;
            LiveLog.Erro("le_parte", e, "hotkey");
        }
    }

    private void RegistrarTeclaInterno()
    {
        if (teclaRegistrada) { UnregisterHotKey(Handle, IdTecla); teclaRegistrada = false; }
        teclaErro = "";
        Keys k = (Keys)LiveConfig.TeclaAlternar;
        Keys codigo = k & Keys.KeyCode;
        if (codigo == Keys.None) return;
        uint mods = MOD_NOREPEAT;
        if ((k & Keys.Control) != 0) mods |= MOD_CONTROL;
        if ((k & Keys.Alt) != 0) mods |= MOD_ALT;
        if ((k & Keys.Shift) != 0) mods |= MOD_SHIFT;
        teclaRegistrada = RegisterHotKey(Handle, IdTecla, mods, (uint)codigo);
        if (!teclaRegistrada)
        {
            teclaErro = LiveTxt.F("tecla_erro", NomeTecla(k));
            LiveLog.Aviso("lg_tecla_erro", NomeTecla(k));
        }
        else LiveLog.Info("lg_tecla", NomeTecla(k));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == IdTecla)
        {
            AlternarTraducao();
            return;
        }
        base.WndProc(ref m);
    }

    private void AlternarTraducao()
    {
        LiveConfig.Ativo = !LiveConfig.Ativo;
        if (LiveConfig.Ativo) LiveMotor.VarrerAgora = true;
        LiveLog.Info(LiveConfig.Ativo ? "lg_comecou" : "lg_pausada");
        // o jogo esta na frente: um som diz o que aconteceu (ligou = agudo, pausou = grave)
        try { MessageBeep(LiveConfig.Ativo ? 0x40u : 0x10u); }   // MB_ICONASTERISK / MB_ICONHAND
        catch { }
        AtualizarStatus();
        if (Visible && (aba == AbaPainel || aba == AbaGeral)) MostrarAba(aba);
    }

    public static string NomeTecla(Keys k)
    {
        Keys codigo = k & Keys.KeyCode;
        if (codigo == Keys.None) return LiveTxt.T("tecla_nenhuma");
        string s = "";
        if ((k & Keys.Control) != 0) s += "Ctrl+";
        if ((k & Keys.Alt) != 0) s += "Alt+";
        if ((k & Keys.Shift) != 0) s += "Shift+";
        return s + codigo.ToString();
    }

    // modo "aperte uma tecla" (aba Avancado): Esc cancela, Delete/Backspace tira a tecla
    private void CapturarTecla(KeyEventArgs e)
    {
        Keys codigo = e.KeyCode;
        if (codigo == Keys.ControlKey || codigo == Keys.ShiftKey || codigo == Keys.Menu) return;   // so o modificador
        e.Handled = true;
        e.SuppressKeyPress = true;
        capturando = false;
        if (codigo == Keys.Escape) { MostrarAba(AbaAvanc); return; }
        if (codigo == Keys.Delete || codigo == Keys.Back) LiveConfig.TeclaAlternar = 0;
        else if (codigo == Keys.F8 && e.Modifiers == Keys.None) { teclaErro = LiveTxt.T("tecla_f8"); MostrarAba(AbaAvanc); return; }
        else LiveConfig.TeclaAlternar = (int)(codigo | e.Modifiers);
        LiveConfig.Marcar();
        RegistrarTecla();
        MostrarAba(AbaAvanc);
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
        try { if (teclaRegistrada) UnregisterHotKey(Handle, IdTecla); } catch { }
        teclaRegistrada = false;
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

    private static bool Gratis { get { return LiveConfig.ProvedorId == LiveConfig.IdGoogle; } }

    private void AtualizarStatus()
    {
        if (btnAtivar == null) return;
        bool ativo = LiveConfig.Ativo;
        string txt = LiveTxt.T(ativo ? "pausar" : "comecar");
        if (btnAtivar.Text != txt) btnAtivar.Text = txt;
        Tema.Pintar(btnAtivar, ativo ? 2 : 1);
        lblRodape.Text = string.Format(LiveTxt.T("status"), LiveMotor.NaFila, LiveMotor.Traduzidos, TradCache.Total, LiveMotor.Pedidos, LiveMotor.Erros);
        if (atualizarAba != null)
        {
            try { atualizarAba(); }
            catch (Exception ex) { LiveLog.Erro("le_parte", ex, "menu"); }
        }
    }

    // ---------------- pecas das paginas ----------------

    // cartao: caixa escura com titulo pequeno em cima. Devolve o painel onde vao os controles.
    private FlowLayoutPanel Cartao(string titulo)
    {
        FlowLayoutPanel c = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Tema.Cartao,
            Padding = new Padding(Tema.D(14), Tema.D(10), Tema.D(14), Tema.D(10)),
            Margin = new Padding(0, 0, 0, Tema.D(10)),
            MinimumSize = new Size(L, 0),
            MaximumSize = new Size(L, 0)
        };
        if (!string.IsNullOrEmpty(titulo))
            c.Controls.Add(Tema.Rotulo(titulo.ToUpperInvariant(), Interna, Tema.Fraco, 8.5f, true));
        pagina.Controls.Add(c);
        return c;
    }

    // largura de dentro de um cartao
    private int Interna { get { return L - Tema.D(28); } }

    private Label Rot(Control pai, string texto, DColor cor, float tamanho = 9f, bool negrito = false)
    {
        Label l = Tema.Rotulo(texto, Interna, cor, tamanho, negrito);
        pai.Controls.Add(l);
        return l;
    }

    private Label Dica(Control pai, string texto)
    {
        return Rot(pai, texto, Tema.Fraco, 8.5f);
    }

    // grade de opcoes (uma escolhida): botoes do mesmo tamanho, "cols" por linha
    private FlowLayoutPanel Grade(Control pai, string[] opcoes, int sel, int cols, int largura, Action<int> escolheu)
    {
        FlowLayoutPanel g = GradeFixa(largura, opcoes.Length, cols);
        int bw = (largura - Tema.D(6) * (cols - 1)) / cols;
        List<Button> bs = new List<Button>();
        for (int i = 0; i < opcoes.Length; i++)
        {
            int idx = i;
            Button b = Tema.Botao(opcoes[i], bw, Tema.D(30), i == sel ? 1 : 0, null);
            b.Click += delegate
            {
                for (int k = 0; k < bs.Count; k++) Tema.Pintar(bs[k], k == idx ? 1 : 0);
                for (int k = 0; k < bs.Count; k++) bs[k].Font = Tema.F(9f, k == idx);
                escolheu(idx);
            };
            bs.Add(b);
            g.Controls.Add(b);
        }
        pai.Controls.Add(g);
        return g;
    }

    // painel da grade com tamanho fixo (AutoSize com quebra de linha erra a altura)
    private static FlowLayoutPanel GradeFixa(int largura, int n, int cols)
    {
        int linhas = (n + cols - 1) / cols;
        return new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false,
            Width = largura + Tema.D(6),
            Height = linhas * (Tema.D(30) + Tema.D(6)),
            BackColor = DColor.Transparent,
            Margin = new Padding(0, 0, 0, Tema.D(2)),
            Padding = new Padding(0)
        };
    }

    // grade de liga/desliga (bits)
    private void GradeBits(Control pai, string[] opcoes, int valor, int cols, Action<int> mudou)
    {
        FlowLayoutPanel g = GradeFixa(Interna, opcoes.Length, cols);
        int bw = (Interna - Tema.D(6) * (cols - 1)) / cols;
        int atual = valor;
        for (int i = 0; i < opcoes.Length; i++)
        {
            int bit = 1 << i;
            Button b = Tema.Botao(opcoes[i], bw, Tema.D(30), (atual & bit) != 0 ? 1 : 0, null);
            b.Click += delegate
            {
                atual ^= bit;
                Tema.Pintar(b, (atual & bit) != 0 ? 1 : 0);
                b.Font = Tema.F(9f, (atual & bit) != 0);
                mudou(atual);
            };
            g.Controls.Add(b);
        }
        pai.Controls.Add(g);
    }

    private static string Num(int n)
    {
        return n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");
    }

    private static string Cortar(string s, int max)
    {
        if (s == null) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    private static void AbrirPasta(string pasta)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + pasta + "\"") { UseShellExecute = true }); }
        catch (Exception e) { LiveLog.Erro("le_parte", e, "explorer"); }
    }

    // ---------------- 01 Painel ----------------

    private void PaginaPainel()
    {
        bool gratis = Gratis;

        // faixa de estado
        FlowLayoutPanel faixa = Cartao(null);
        FlowLayoutPanel l1 = Tema.Linha(Interna);
        Label chip = new Label
        {
            AutoSize = false,
            Width = Tema.D(96),
            Height = Tema.D(28),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Tema.F(9f, true),
            Margin = new Padding(0, 0, Tema.D(10), 0)
        };
        l1.Controls.Add(chip);
        Label sub = Tema.Rotulo("", Interna - Tema.D(320), Tema.Fraco, 9f);
        sub.Margin = new Padding(0, Tema.D(6), Tema.D(10), 0);
        l1.Controls.Add(sub);
        faixa.Controls.Add(l1);

        // 6 cartoes de resumo (3 por linha); "Abrir" leva para a aba
        FlowLayoutPanel cards = Tema.Linha(L + Tema.D(10));
        cards.Margin = new Padding(0, 0, 0, Tema.D(4));
        pagina.Controls.Add(cards);
        int cw = (L - Tema.D(20)) / 3;
        Label[] vModo = Resumo(cards, cw, LiveTxt.T("pn_modo"), AbaProv);
        Label[] vIdioma = Resumo(cards, cw, LiveTxt.T("pn_idioma"), AbaGeral);
        Label[] vEstilo = Resumo(cards, cw, LiveTxt.T("pn_estilo"), AbaEstilo);
        Label[] vGlos = Resumo(cards, cw, LiveTxt.T("pn_glos"), AbaGlos);
        Label[] vCache = Resumo(cards, cw, LiveTxt.T("pn_cache_card"), AbaAvanc);
        Label[] vPonte = Resumo(cards, cw, LiveTxt.T("pn_ponte"), AbaLog);

        // estado: numeros grandes
        FlowLayoutPanel est = Cartao(LiveTxt.T("pn_estado"));
        FlowLayoutPanel blocos = Tema.Linha(Interna + Tema.D(10));
        est.Controls.Add(blocos);
        int bw = (Interna - Tema.D(30)) / 4;
        Label nTrad = Bloco(blocos, bw, LiveTxt.T("pn_sessao"));
        Label nCache = Bloco(blocos, bw, LiveTxt.T("pn_cache"));
        Label nPed = Bloco(blocos, bw, LiveTxt.T("pn_pedmin"));
        Label nGlos = Bloco(blocos, bw, LiveTxt.T("pn_glos"));
        Label inteligente = Dica(est, "");

        // ultimas traducoes
        FlowLayoutPanel ult = Cartao(LiveTxt.T("pn_ultimas"));
        Label[] linhas = new Label[8];
        for (int i = 0; i < linhas.Length; i++)
        {
            linhas[i] = Tema.Rotulo("", Interna, Tema.Texto, 9f);
            linhas[i].AutoEllipsis = true;
            linhas[i].AutoSize = false;
            linhas[i].Width = Interna;
            linhas[i].Height = Tema.D(22);
            ult.Controls.Add(linhas[i]);
        }

        atualizarAba = delegate
        {
            bool at = LiveConfig.Ativo;
            chip.Text = LiveTxt.T(at ? "st_ativa" : "st_pausada");
            chip.BackColor = at ? Tema.Ok : Tema.Aviso;
            chip.ForeColor = Tema.TextoEscuro;
            sub.Text = LiveTxt.T(at ? "st_sub_ativa" : "st_sub_pausada");

            vModo[0].Text = gratis ? LiveTxt.T("modo_gratis") : LiveConfig.Atual.Nome;
            vModo[1].Text = gratis ? LiveTxt.T("pn_gratis_sub") : LiveTxt.T("pn_api_sub");
            vIdioma[0].Text = NomeIdioma();
            vIdioma[1].Text = LiveTxt.T("pn_idioma_sub");
            vEstilo[0].Text = gratis ? "-" : LiveTxt.T("estilo" + LiveConfig.Estilo);
            vEstilo[1].Text = gratis ? LiveTxt.T("pn_estilo_gratis") : LiveTxt.F("pn_estilo_sub", (LiveConfig.PromptExtra ?? "").Length, MaxExtras);
            int travados = 0;
            List<TermoGlossario> lista = Glossario.Lista;
            for (int i = 0; i < lista.Count; i++) if (lista[i].Travado) travados++;
            vGlos[0].Text = LiveTxt.F("pn_termos", Num(Glossario.Total));
            vGlos[1].Text = LiveTxt.F("pn_termos_sub2", Glossario.Conflitos, Num(travados));
            vGlos[1].ForeColor = Glossario.Conflitos > 0 ? Tema.Aviso : Tema.Fraco;
            vCache[0].Text = gratis ? LiveTxt.T("pn_ritmo_auto") : LiveTxt.F("pn_rpm_val", LiveConfig.Rpm);
            vCache[1].Text = LiveTxt.F("pn_cache_sub", Num(TradCache.Total));
            bool conectada = (DateTime.Now - Ponte.UltimoContato).TotalSeconds < 30 || Ponte.Recebidos > 0;
            vPonte[0].Text = LiveTxt.T(conectada ? "pn_ponte_ok" : "pn_ponte_espera");
            vPonte[0].ForeColor = conectada ? Tema.Ok : Tema.Aviso;
            vPonte[1].Text = LiveTxt.F("pn_ponte_sub", Num(Ponte.Recebidos), Ponte.Esperando);

            nTrad.Text = Num(LiveMotor.Traduzidos);
            nCache.Text = Num(TradCache.Total);
            nPed.Text = Num(LiveMotor.PedidosMinuto) + (gratis ? "" : "  / " + LiveConfig.Rpm);
            nGlos.Text = Num(Glossario.Total);
            inteligente.Text = gratis ? LiveTxt.T("pn_glos_gratis") : LiveTxt.F("pn_glos_ia", LiveMotor.TermosAchados, LiveMotor.Refeitos);

            List<string[]> u = LiveMotor.Ultimas;
            for (int i = 0; i < linhas.Length; i++)
            {
                int k = u.Count - 1 - i;
                if (k < 0)
                {
                    linhas[i].Text = i == 0 && u.Count == 0 ? LiveTxt.T("pn_ultimas_vazio") : "";
                    linhas[i].ForeColor = Tema.Fraco;
                    linhas[i].Visible = i == 0;
                    continue;
                }
                linhas[i].Visible = true;
                TermoGlossario tg = Glossario.Obter(u[k][0]);
                string cat = tg != null ? "   [" + Categorias.Nome(tg.Cat) + "]" : "";
                linhas[i].Text = Cortar(u[k][0], 40) + "   →   " + Cortar(u[k][1], 60) + cat;
                linhas[i].ForeColor = Tema.Texto;
            }
        };
    }

    // cartao do resumo: titulo, valor, linha de baixo e "Abrir". Devolve { valor, linha de baixo }.
    private Label[] Resumo(FlowLayoutPanel pai, int largura, string titulo, int abaDestino)
    {
        FlowLayoutPanel c = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Tema.Cartao,
            Width = largura,
            Height = Tema.D(112),
            Padding = new Padding(Tema.D(12), Tema.D(8), Tema.D(10), Tema.D(4)),
            Margin = new Padding(0, 0, Tema.D(10), Tema.D(10))
        };
        int iw = largura - Tema.D(22);
        c.Controls.Add(Tema.Rotulo(titulo.ToUpperInvariant(), iw, Tema.Fraco, 8f, true));
        Label v = Tema.Rotulo("", iw, Tema.Texto, 11f, true);
        v.AutoEllipsis = true;
        v.AutoSize = false;
        v.Width = iw;
        v.Height = Tema.D(24);
        c.Controls.Add(v);
        Label s = Tema.Rotulo("", iw, Tema.Fraco, 8.5f);
        s.AutoEllipsis = true;
        s.AutoSize = false;
        s.Width = iw;
        s.Height = Tema.D(20);
        c.Controls.Add(s);
        Button abrir = Tema.Botao(LiveTxt.T("pn_abrir"), Tema.D(90), Tema.D(24), 4, delegate { MostrarAba(abaDestino); });
        abrir.TextAlign = ContentAlignment.MiddleLeft;
        abrir.Font = Tema.F(8.5f, true);
        c.Controls.Add(abrir);
        pai.Controls.Add(c);
        return new Label[] { v, s };
    }

    private Label Bloco(FlowLayoutPanel pai, int largura, string rotulo)
    {
        FlowLayoutPanel b = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Tema.CartaoClaro,
            Width = largura,
            Height = Tema.D(66),
            Padding = new Padding(Tema.D(10), Tema.D(6), Tema.D(6), Tema.D(4)),
            Margin = new Padding(0, 0, Tema.D(10), Tema.D(6))
        };
        b.Controls.Add(Tema.Rotulo(rotulo, largura - Tema.D(16), Tema.Fraco, 8.5f));
        Label n = Tema.Rotulo("0", largura - Tema.D(16), Tema.Texto, 15f, true);
        b.Controls.Add(n);
        pai.Controls.Add(b);
        return n;
    }

    private static string NomeIdioma()
    {
        for (int i = 0; i < LiveConfig.Idiomas.Length; i++)
            if (LiveConfig.Idiomas[i][0] == LiveConfig.Idioma) return LiveConfig.Idiomas[i][1];
        return LiveConfig.Idioma;
    }

    // ---------------- 02 Geral ----------------

    private void PaginaGeral()
    {
        FlowLayoutPanel topo = Cartao(null);
        Button ativar = Tema.Botao(LiveTxt.T(LiveConfig.Ativo ? "pausar" : "comecar"), Tema.D(260), Tema.D(36), LiveConfig.Ativo ? 2 : 1, delegate
        {
            LiveConfig.Ativo = !LiveConfig.Ativo;
            if (LiveConfig.Ativo) LiveMotor.VarrerAgora = true;
            LiveLog.Info(LiveConfig.Ativo ? "lg_comecou" : "lg_pausada");
            MostrarAba(AbaGeral);
            AtualizarStatus();
        });
        topo.Controls.Add(ativar);
        Rot(topo, LiveTxt.T(LiveConfig.Ativo ? "trad_rodando" : "trad_pausada"), LiveConfig.Ativo ? Tema.Ok : Tema.Aviso);

        bool gratis = Gratis;
        FlowLayoutPanel modo = Cartao(LiveTxt.T("modo"));
        Grade(modo, new string[] { LiveTxt.T("modo_gratis"), LiveTxt.T("modo_api") }, gratis ? 0 : 1, 2, Interna, delegate (int m)
        {
            if (m == 0 && !Gratis)
            {
                LiveConfig.ProvedorId = LiveConfig.IdGoogle;
                Mudou();
            }
            else if (m == 1 && Gratis)
            {
                LiveConfig.ProvedorId = LiveConfig.UltimoProvedorComChave;
                if (LiveConfig.ProvedorId == LiveConfig.IdGoogle) LiveConfig.ProvedorId = "gemini";
                LiveMotor.Destravar();
                Mudou();
            }
            MostrarAba(AbaGeral);
        });
        Dica(modo, DicaProvedor(LiveConfig.Atual));

        FlowLayoutPanel idioma = Cartao(LiveTxt.T("traduzir_para"));
        string[] nomes = new string[LiveConfig.Idiomas.Length];
        int sel = -1;
        for (int i = 0; i < nomes.Length; i++)
        {
            nomes[i] = LiveConfig.Idiomas[i][1];
            if (LiveConfig.Idiomas[i][0] == LiveConfig.Idioma) sel = i;
        }
        Grade(idioma, nomes, sel, 4, Interna, delegate (int i)
        {
            string novo = LiveConfig.Idiomas[i][0];
            if (novo == LiveConfig.Idioma) return;
            LiveConfig.Idioma = novo;
            TradCache.TrocouIdioma();
            Glossario.TrocouIdioma();
            Mudou();
        });
        Dica(idioma, LiveTxt.T("aviso_cjk"));

        FlowLayoutPanel oque = Cartao(LiveTxt.T("o_que"));
        string[] tipos = new string[] { LiveTxt.T("fonte_zh"), LiveTxt.T("fonte_ja"), LiveTxt.T("fonte_ko"), LiveTxt.T("fonte_cir"), LiveTxt.T("fonte_lat"), LiveTxt.T("fonte_out") };
        Label nenhum = null;
        GradeBits(oque, tipos, LiveConfig.Fontes, 3, delegate (int v)
        {
            if (v == LiveConfig.Fontes) return;
            LiveConfig.Fontes = v;
            Mudou();
            if (nenhum != null) nenhum.Visible = v == 0;
        });
        Dica(oque, LiveTxt.T("fonte_dica"));
        nenhum = Rot(oque, LiveTxt.T("fonte_nenhum"), Tema.Aviso);
        nenhum.Visible = LiveConfig.Fontes == 0;

        FlowLayoutPanel st = Cartao("Status");
        Label ponte = Rot(st, "", Tema.Texto);
        Label numeros = Rot(st, "", Tema.Fraco, 8.5f);
        Label erro = Rot(st, "", Tema.Erro);
        Dica(st, LiveTxt.T("menu_dica"));
        atualizarAba = delegate
        {
            bool conectada = (DateTime.Now - Ponte.UltimoContato).TotalSeconds < 30 || Ponte.Recebidos > 0;
            ponte.Text = LiveTxt.T(conectada ? "ponte_ok" : "ponte_espera");
            ponte.ForeColor = conectada ? Tema.Ok : Tema.Aviso;
            numeros.Text = string.Format(LiveTxt.T("ponte_status"), Ponte.Recebidos, Ponte.Enviados, Ponte.Esperando);
            string e = LiveMotor.UltimoErro;
            erro.Text = string.IsNullOrEmpty(e) ? "" : LiveTxt.T("ultimo_erro") + e;
        };
    }

    private static string DicaProvedor(Provedor p)
    {
        string k = "dica_" + p.Id;
        return LiveTxt.Tem(k) ? LiveTxt.T(k) : p.Dica;
    }

    // ---------------- 03 Provedor / API ----------------

    private void PaginaProvedor()
    {
        Provedor atual = LiveConfig.Atual;
        if (Gratis)
        {
            FlowLayoutPanel g = Cartao(LiveTxt.T("modo_gratis"));
            Rot(g, LiveTxt.T("gratis_info"), Tema.Aviso);
            Dica(g, DicaProvedor(atual));
            Button usar = Tema.Botao(LiveTxt.T("modo_api"), Tema.D(220), Tema.D(30), 1, delegate
            {
                LiveConfig.ProvedorId = LiveConfig.UltimoProvedorComChave == LiveConfig.IdGoogle ? "gemini" : LiveConfig.UltimoProvedorComChave;
                LiveMotor.Destravar();
                Mudou();
                MostrarAba(AbaProv);
            });
            g.Controls.Add(usar);
            return;
        }

        FlowLayoutPanel c = Cartao(LiveTxt.T("servico"));
        List<Provedor> lista = new List<Provedor>();
        for (int i = 0; i < LiveConfig.Provedores.Length; i++)
            if (LiveConfig.Provedores[i].Id != LiveConfig.IdGoogle) lista.Add(LiveConfig.Provedores[i]);
        string[] nomes = new string[lista.Count];
        int sel = 0;
        for (int i = 0; i < lista.Count; i++)
        {
            nomes[i] = lista[i].Nome;
            if (lista[i].Id == atual.Id) sel = i;
        }
        Grade(c, nomes, sel, 3, Interna, delegate (int i)
        {
            if (lista[i].Id == LiveConfig.ProvedorId) return;
            LiveConfig.ProvedorId = lista[i].Id;
            LiveConfig.UltimoProvedorComChave = lista[i].Id;
            LiveMotor.Destravar();
            Mudou();
            MostrarAba(AbaProv);
        });
        Dica(c, DicaProvedor(atual));

        string id = atual.Id;
        FlowLayoutPanel cfg = Cartao(null);
        Rot(cfg, LiveTxt.T("url"), Tema.Fraco, 8.5f);
        TextBox url = Tema.Campo(LiveConfig.Url(id), Interna);
        url.TextChanged += delegate
        {
            LiveConfig.Urls[id] = url.Text.Trim();
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        cfg.Controls.Add(url);

        Rot(cfg, LiveTxt.T(atual.PrecisaChave ? "chave_obrig" : "chave_opc"), Tema.Fraco, 8.5f);
        TextBox chave = Tema.Campo(LiveConfig.Chave(id), Interna);
        chave.UseSystemPasswordChar = !mostrarChave;
        chave.TextChanged += delegate
        {
            LiveConfig.Chaves[id] = chave.Text.Trim();
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        cfg.Controls.Add(chave);
        FlowLayoutPanel lc = Tema.Linha(Interna);
        CheckBox mostrar = Tema.Marca(LiveTxt.T("mostrar").Trim(), mostrarChave, Tema.D(200));
        mostrar.Margin = new Padding(0, Tema.D(4), Tema.D(16), 0);
        mostrar.CheckedChanged += delegate { mostrarChave = mostrar.Checked; chave.UseSystemPasswordChar = !mostrarChave; };
        lc.Controls.Add(mostrar);
        lc.Controls.Add(Tema.Botao(LiveTxt.T("apagar_chave"), Tema.D(140), Tema.D(26), 0, delegate { chave.Text = ""; }));
        cfg.Controls.Add(lc);

        Rot(cfg, LiveTxt.T("modelo"), Tema.Fraco, 8.5f);
        TextBox modelo = Tema.Campo(LiveConfig.Modelo(id), Tema.D(360));
        modelo.TextChanged += delegate
        {
            LiveConfig.Modelos[id] = modelo.Text.Trim();
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        cfg.Controls.Add(modelo);
        Label falta = Rot(cfg, "", Tema.Erro);

        FlowLayoutPanel teste = Cartao(null);
        teste.Controls.Add(Tema.Botao(LiveTxt.T("testar"), Tema.D(220), Tema.D(32), 1, delegate { LiveMotor.Testar(); }));
        Label res = Rot(teste, "", Tema.Texto);

        atualizarAba = delegate
        {
            string motivo;
            falta.Text = Provedores.Configurado(LiveConfig.Atual, out motivo) ? "" : LiveTxt.T("falta") + motivo;
            res.Text = LiveMotor.Teste;
            res.ForeColor = LiveMotor.Teste.StartsWith("OK", StringComparison.Ordinal) ? Tema.Ok : Tema.Aviso;
        };
    }

    // ---------------- 04 Estilo ----------------

    private void PaginaEstilo()
    {
        if (Gratis)
        {
            FlowLayoutPanel g = Cartao(null);
            Rot(g, LiveTxt.T("estilo_gratis"), Tema.Aviso);
            return;
        }

        FlowLayoutPanel c = Cartao(LiveTxt.T("estilo_titulo"));
        string[] nomes = new string[LiveConfig.Estilos.Length];
        for (int i = 0; i < nomes.Length; i++) nomes[i] = LiveTxt.T("estilo" + i);
        Label resumo = null;
        Grade(c, nomes, LiveConfig.Estilo, 2, Interna, delegate (int e)
        {
            LiveConfig.Estilo = e;
            LiveConfig.Marcar();
            if (resumo != null) resumo.Text = LiveTxt.T("resumo" + e);
        });
        resumo = Rot(c, LiveTxt.T("resumo" + LiveConfig.Estilo), Tema.Ok);

        // como a IA traduz
        FlowLayoutPanel ia = Cartao(LiveTxt.T("estilo_ia"));
        Opcao(ia, LiveTxt.T("contexto").Trim(), LiveConfig.Contexto, LiveTxt.T("contexto_dica"), delegate (bool v) { LiveConfig.Contexto = v; });
        Opcao(ia, LiveTxt.T("raciocinio"), LiveConfig.Raciocinio, LiveTxt.T("raciocinio_dica"), delegate (bool v)
        {
            LiveConfig.Raciocinio = v;
            Provedores.LimparRaciocinio();
        });
        Opcao(ia, LiveTxt.T("op_extrair"), LiveConfig.ExtrairTermos, LiveTxt.T("op_extrair_dica"), delegate (bool v) { LiveConfig.ExtrairTermos = v; });
        Opcao(ia, LiveTxt.T("op_verificar"), LiveConfig.VerificarGlossario, LiveTxt.T("op_verificar_dica"), delegate (bool v) { LiveConfig.VerificarGlossario = v; });
        Opcao(ia, LiveTxt.T("op_encaixar"), LiveConfig.Encaixar, LiveTxt.T("op_encaixar_dica"), delegate (bool v) { LiveConfig.Encaixar = v; });
        Opcao(ia, LiveTxt.T("op_exemplos"), LiveConfig.Exemplos, LiveTxt.F("op_exemplos_dica", Glossario.Travados), delegate (bool v) { LiveConfig.Exemplos = v; });

        // instrucoes extras
        FlowLayoutPanel ex = Cartao(null);
        FlowLayoutPanel cab = Tema.Linha(Interna);
        Label tit = Tema.Rotulo(LiveTxt.T("extras_titulo"), Interna - Tema.D(90), Tema.Fraco, 8.5f);
        tit.AutoSize = false;
        tit.Width = Interna - Tema.D(90);
        tit.Height = Tema.D(18);
        cab.Controls.Add(tit);
        Label cont = Tema.Rotulo("", Tema.D(80), Tema.Fraco, 8.5f);
        cab.Controls.Add(cont);
        ex.Controls.Add(cab);
        TextBox extras = Tema.Caixa(LiveConfig.PromptExtra, Interna, 4, MaxExtras);
        Action contar = delegate
        {
            int n = (LiveConfig.PromptExtra ?? "").Length;
            cont.Text = n + " / " + MaxExtras;
            cont.ForeColor = n >= MaxExtras ? Tema.Erro : Tema.Fraco;
        };
        contar();
        extras.TextChanged += delegate
        {
            LiveConfig.PromptExtra = extras.Text;
            LiveConfig.Marcar();
            contar();
        };
        ex.Controls.Add(extras);
        Rot(ex, LiveTxt.T("exemplos"), Tema.Fraco, 8.5f);
        string[] exemplos = new string[] { LiveTxt.T("ex1"), LiveTxt.T("ex2"), LiveTxt.T("ex3"), LiveTxt.T("ex4"), LiveTxt.T("ex5") };
        FlowLayoutPanel g2 = GradeFixa(Interna, exemplos.Length, 2);
        int bw = (Interna - Tema.D(6)) / 2;
        for (int i = 0; i < exemplos.Length; i++)
        {
            string texto = exemplos[i];
            g2.Controls.Add(Tema.Botao(texto, bw, Tema.D(30), 0, delegate
            {
                string a = extras.Text ?? "";
                if (a.IndexOf(texto, StringComparison.Ordinal) >= 0) return;
                string novo = a.Trim().Length == 0 ? texto : a.TrimEnd() + " " + texto;
                if (novo.Length <= MaxExtras) extras.Text = novo;
            }));
        }
        ex.Controls.Add(g2);
        ex.Controls.Add(Tema.Botao(LiveTxt.T("limpar_extras"), Tema.D(110), Tema.D(28), 0, delegate { extras.Text = ""; }));
        Dica(ex, LiveTxt.T("estilo_nota"));

        // caixa de teste: a mesma frase sem nada e com o que o jogador escolheu, lado a lado
        FlowLayoutPanel tst = Cartao(LiveTxt.T("teste_titulo"));
        Dica(tst, LiveTxt.T("teste_dica"));
        TextBox entrada = Tema.Caixa(testeTexto, Interna, 3, 300);
        entrada.TextChanged += delegate { testeTexto = entrada.Text; };
        tst.Controls.Add(entrada);
        int meia = (Interna - Tema.D(10)) / 2;
        FlowLayoutPanel lados = Tema.Linha(Interna);
        Label semRes, comRes;
        lados.Controls.Add(LadoTeste(LiveTxt.T("teste_sem"), meia, out semRes));
        lados.Controls.Add(LadoTeste(LiveTxt.T("teste_com"), meia, out comRes));
        Button btTestar = null;
        btTestar = Tema.Botao(LiveTxt.T("teste_bt"), Tema.D(160), Tema.D(30), 1, delegate { TestarEstilo(entrada.Text, semRes, comRes, btTestar); });
        tst.Controls.Add(btTestar);
        tst.Controls.Add(lados);

        // o jogo (vai no pedido)
        FlowLayoutPanel jogo = Cartao(LiveTxt.T("jogo_titulo"));
        TextBox txtJogo = Tema.Caixa(LiveConfig.Jogo, Interna, 3, 400);
        txtJogo.TextChanged += delegate
        {
            string v = txtJogo.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            LiveConfig.Jogo = v.Length > 0 ? v : LiveConfig.JogoPadrao;
            LiveConfig.Marcar();
        };
        jogo.Controls.Add(txtJogo);
        jogo.Controls.Add(Tema.Botao(LiveTxt.T("jogo_padrao"), Tema.D(200), Tema.D(28), 0, delegate { txtJogo.Text = LiveConfig.JogoPadrao; }));
    }

    private string testeTexto = "";

    private FlowLayoutPanel LadoTeste(string titulo, int largura, out Label resultado)
    {
        FlowLayoutPanel f = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Tema.CorCampo,
            Padding = new Padding(Tema.D(8)),
            Margin = new Padding(0, Tema.D(6), Tema.D(10), 0),
            MinimumSize = new Size(largura, Tema.D(70)),
            MaximumSize = new Size(largura, 0)
        };
        f.Controls.Add(Tema.Rotulo(titulo, largura - Tema.D(16), Tema.Fraco, 8.5f, true));
        resultado = Tema.Rotulo("", largura - Tema.D(16), Tema.Texto, 9.5f);
        f.Controls.Add(resultado);
        return f;
    }

    // dois pedidos: so as regras fixas (sem nada) e o pedido normal (estilo, instrucoes, opcoes, glossario).
    // Nao entra no cache nem no glossario.
    private async void TestarEstilo(string texto, Label sem, Label com, Button bt)
    {
        texto = (texto ?? "").Trim();
        if (texto.Length == 0) return;
        Provedor p = LiveConfig.Atual;
        string motivo;
        if (p.Tipo == 0) { sem.Text = LiveTxt.T("cia_gratis"); return; }
        if (!Provedores.Configurado(p, out motivo)) { sem.Text = motivo; return; }
        List<string> l = new List<string> { texto };
        Pedido a, b;
        try
        {
            Provedores.Teste = true;
            Provedores.TesteCru = true;
            a = Provedores.Montar(p, l, 0);
            Provedores.TesteCru = false;
            b = Provedores.Montar(p, l, 0);
        }
        catch (Exception e) { sem.Text = LiveTxt.F("comp_erro", e.Message); return; }
        finally { Provedores.Teste = false; Provedores.TesteCru = false; }
        bt.Enabled = false;
        sem.Text = com.Text = LiveTxt.T("teste_esperando");
        sem.ForeColor = com.ForeColor = Tema.Fraco;
        Task<Resposta> ta = Http.Enviar(a);
        Task<Resposta> tb = Http.Enviar(b);
        MostrarTeste(sem, p, l, await ta);
        MostrarTeste(com, p, l, await tb);
        if (!bt.IsDisposed) bt.Enabled = true;
    }

    private static void MostrarTeste(Label lb, Provedor p, List<string> l, Resposta r)
    {
        if (lb.IsDisposed) return;   // trocou de aba enquanto esperava
        if (r.Codigo != 200)
        {
            lb.Text = LiveTxt.F("cia_erro", r.Codigo, string.IsNullOrEmpty(r.Erro) ? "" : r.Erro);
            lb.ForeColor = Tema.Erro;
            return;
        }
        List<string> t = Provedores.Interpretar(p, l, r.Corpo, 0);
        lb.Text = t != null && t.Count > 0 ? t[0] : LiveTxt.T("teste_falhou");
        lb.ForeColor = t != null ? Tema.Texto : Tema.Erro;
    }

    private void Opcao(FlowLayoutPanel pai, string texto, bool valor, string dica, Action<bool> mudou)
    {
        CheckBox c = Tema.Marca(texto, valor, Interna);
        c.CheckedChanged += delegate
        {
            mudou(c.Checked);
            LiveConfig.Marcar();
            LiveMotor.Destravar();
        };
        pai.Controls.Add(c);
        if (!string.IsNullOrEmpty(dica))
        {
            Label d = Dica(pai, dica);
            d.Margin = new Padding(Tema.D(20), 0, 0, Tema.D(8));
        }
    }

    // ---------------- 05 Glossario ----------------
    // Lista a esquerda (original em dourado, traducao, abreviacao, categoria colorida, icone de trava/pacote/conflito)
    // e painel do termo a direita (tudo editavel num lugar so, sugestoes da IA com 1 clique). Ajuda no botao "?".

    private DataGridView grade;
    private Label glosContagem;
    private Label glosAbrStatus;
    // controles do glossario que acompanham a largura da janela (controle, quanto tirar da largura)
    private readonly List<KeyValuePair<Control, int>> larguraGlos = new List<KeyValuePair<Control, int>>();
    private FlowLayoutPanel filtros;
    private int versaoGrade = -1;
    private readonly List<TermoGlossario> filtrado = new List<TermoGlossario>();
    private bool preenchendo;
    // painel do termo
    private FlowLayoutPanel det;
    private Label detOrig, detNivel, detExemplo, detOnde, detAjuda, detSugTitulo, detConflitoTxt;
    private TextBox detTrad, detCurta;
    private ComboBox detCat;
    private Button detTrava;
    private FlowLayoutPanel detConflito, detSug, detLinhaCat, detAcoes;
    private TermoGlossario detTermo;
    private bool detPreenchendo;
    // sugestoes da IA por termo (nesta sessao): original -> [traducao, nota]
    private readonly Dictionary<string, List<string[]>> sugestoes = new Dictionary<string, List<string[]>>();
    private bool sugerindo;
    private string sugStatus = "";
    private bool sugErro;

    private Control PaginaGlossario()
    {
        TableLayoutPanel t = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, BackColor = Tema.Conteudo, Padding = new Padding(0, 0, Tema.D(4), 0) };
        Tema.Papel(t);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int w = Math.Max(Tema.D(480), corpo.ClientSize.Width - Tema.D(8));

        // busca + botoes numa linha so
        FlowLayoutPanel topo = Tema.Linha(w);
        // a busca fica com o que sobra da linha (os 3 botoes ocupam ~540)
        TextBox tb = Tema.Campo(busca, Math.Max(Tema.D(160), Math.Min(Tema.D(380), w - Tema.D(590))));
        tb.Margin = new Padding(0, Tema.D(2), Tema.D(10), Tema.D(6));
        Label lupa = Tema.Rotulo("🔍", Tema.D(24), Tema.Fraco, 10f);
        lupa.Margin = new Padding(0, Tema.D(4), Tema.D(2), 0);
        topo.Controls.Add(lupa);
        // espera parar de digitar (~250 ms) antes de filtrar: redesenhar a tabela grande a cada tecla trava a digitacao
        Timer espera = new Timer { Interval = 250 };
        espera.Tick += delegate { espera.Stop(); if (!grade.IsDisposed) { versaoGrade = -1; PreencherGrade(); } };
        tb.TextChanged += delegate { busca = tb.Text; espera.Stop(); espera.Start(); };
        tb.Disposed += delegate { espera.Dispose(); };
        topo.Controls.Add(tb);
        topo.Controls.Add(Tema.Botao("＋ " + LiveTxt.T("glos_novo").TrimStart('+', ' '), Tema.D(150), Tema.D(30), 1, delegate { NovoTermo(); }));
        topo.Controls.Add(Tema.Botao("▦ " + LiveTxt.T("abr_telas_bt"), Tema.D(180), Tema.D(30), 0, delegate { Telas(); }));
        Button pac = Tema.Botao("📜 " + LiveTxt.T("pac_bt"), Tema.D(170), Tema.D(30), 0, delegate { Pacote(); });
        pac.BackColor = Tema.Rgb(0x2A, 0x23, 0x12);
        pac.ForeColor = Tema.Dourado;
        pac.FlatAppearance.BorderColor = Tema.Rgb(0x5B, 0x4A, 0x22);
        topo.Controls.Add(pac);
        t.Controls.Add(topo);
        larguraGlos.Clear();
        larguraGlos.Add(new KeyValuePair<Control, int>(topo, 0));

        // filtros em "pilulas": Todos, Conflitos, Novos, Travados e as categorias (com contagem)
        filtros = Tema.Linha(w);
        larguraGlos.Add(new KeyValuePair<Control, int>(filtros, 0));
        t.Controls.Add(filtros);

        // lista | painel do termo
        TableLayoutPanel meio = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DColor.Transparent, Margin = new Padding(0, Tema.D(4), 0, 0) };
        meio.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        meio.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        meio.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        grade = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Tema.Cartao,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = Tema.Rgb(0x1D, 0x27, 0x2F),
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = Tema.D(32),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            Font = Tema.F(9.5f),
            Margin = new Padding(0, 0, Tema.D(10), 0)
        };
        grade.RowTemplate.Height = Tema.D(32);
        grade.ColumnHeadersDefaultCellStyle.BackColor = Tema.Rgb(0x18, 0x21, 0x29);
        grade.ColumnHeadersDefaultCellStyle.ForeColor = Tema.Fraco;
        grade.ColumnHeadersDefaultCellStyle.Font = Tema.F(8f, true);
        grade.ColumnHeadersDefaultCellStyle.SelectionBackColor = Tema.Rgb(0x18, 0x21, 0x29);
        grade.ColumnHeadersDefaultCellStyle.Padding = new Padding(Tema.D(6), 0, 0, 0);
        grade.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grade.DefaultCellStyle.BackColor = Tema.Cartao;
        grade.DefaultCellStyle.ForeColor = Tema.Texto;
        grade.DefaultCellStyle.SelectionBackColor = Tema.DestaqueEscuro;
        grade.DefaultCellStyle.SelectionForeColor = Tema.Texto;
        grade.DefaultCellStyle.Padding = new Padding(Tema.D(6), 0, 0, 0);
        grade.AlternatingRowsDefaultCellStyle.BackColor = Tema.Rgb(0x19, 0x20, 0x27);

        DataGridViewTextBoxColumn co = new DataGridViewTextBoxColumn { Name = "orig", HeaderText = LiveTxt.T("col_orig"), ReadOnly = true, FillWeight = 24 };
        co.DefaultCellStyle.ForeColor = Tema.Dourado;
        co.DefaultCellStyle.SelectionForeColor = Tema.Dourado;
        co.DefaultCellStyle.Font = Tema.F(10.5f);
        grade.Columns.Add(co);
        grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "trad", HeaderText = LiveTxt.T("col_trad") + " ✎", FillWeight = 34 });
        grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "curta", HeaderText = LiveTxt.T("col_curta") + " ✎", FillWeight = 14 });
        DataGridViewComboBoxColumn cc = new DataGridViewComboBoxColumn
        {
            Name = "cat",
            HeaderText = LiveTxt.T("col_cat"),
            FillWeight = 18,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing
        };
        int[] ordem = Categorias.Ordem();
        for (int i = 0; i < ordem.Length; i++) cc.Items.Add(Categorias.Nome(ordem[i]));
        cc.DefaultCellStyle.BackColor = Tema.Cartao;
        cc.DefaultCellStyle.ForeColor = Tema.Texto;
        grade.Columns.Add(cc);
        DataGridViewTextBoxColumn ci = new DataGridViewTextBoxColumn { Name = "ic", HeaderText = "", ReadOnly = true, FillWeight = 6 };
        ci.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grade.Columns.Add(ci);

        grade.CellValueChanged += GradeMudou;
        grade.CurrentCellDirtyStateChanged += delegate
        {
            // combo: grava na hora (sem precisar sair da celula)
            if (grade.IsCurrentCellDirty && !(grade.CurrentCell is DataGridViewTextBoxCell)) grade.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grade.DataError += delegate (object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
        grade.SelectionChanged += delegate { MostrarSelecionado(); };
        foreach (DataGridViewColumn col in grade.Columns) col.HeaderText = col.HeaderText.ToUpperInvariant();
        grade.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !grade.IsCurrentCellInEditMode) { ApagarSelecionados(); e.Handled = true; }
        };
        meio.Controls.Add(grade, 0, 0);
        meio.Controls.Add(MontarDetalhe(), 1, 0);
        t.Controls.Add(meio);

        // rodape: selecionar tudo + contagem
        FlowLayoutPanel rod = Tema.Linha(w);
        rod.Margin = new Padding(0, Tema.D(6), 0, 0);
        rod.Controls.Add(Tema.Botao("☑ " + LiveTxt.T("abr_sel_tudo"), Tema.D(150), Tema.D(26), 0, delegate { grade.SelectAll(); }));
        glosContagem = Tema.Rotulo("", w - Tema.D(170), Tema.Fraco, 8.5f);
        glosContagem.Margin = new Padding(Tema.D(6), Tema.D(5), 0, 0);
        rod.Controls.Add(glosContagem);
        t.Controls.Add(rod);
        larguraGlos.Add(new KeyValuePair<Control, int>(rod, 0));
        larguraGlos.Add(new KeyValuePair<Control, int>(glosContagem, Tema.D(170)));

        versaoGrade = -1;
        PreencherGrade();
        atualizarAba = delegate
        {
            if (versaoGrade != Glossario.Versao && !grade.IsCurrentCellInEditMode) PreencherGrade();
            else AtualizarStatusDetalhe();
        };
        return t;
    }

    // painel do termo (direita): original grande, campos, nivel/trava, conflito, sugestoes, onde aparece, acoes
    private Control MontarDetalhe()
    {
        Panel caixa = new PainelLiso { Dock = DockStyle.Fill, BackColor = Tema.Rgb(0x17, 0x1F, 0x26), Margin = new Padding(0), AutoScroll = true, Padding = new Padding(Tema.D(16), Tema.D(12), Tema.D(14), Tema.D(10)) };
        caixa.Paint += delegate (object s, PaintEventArgs e)
        {
            using (Pen p = new Pen(Tema.Rgb(0x2A, 0x36, 0x40))) e.Graphics.DrawRectangle(p, 0, 0, caixa.Width - 1, caixa.Height - 1);
            // brilho jade no canto
            using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int r = Tema.D(200);
                gp.AddEllipse(caixa.Width - r / 2 - Tema.D(10), -r / 2, r, r);
                using (System.Drawing.Drawing2D.PathGradientBrush b = new System.Drawing.Drawing2D.PathGradientBrush(gp))
                {
                    b.CenterColor = DColor.FromArgb(34, 45, 212, 191);
                    b.SurroundColors = new DColor[] { DColor.FromArgb(0, 45, 212, 191) };
                    e.Graphics.FillPath(b, gp);
                }
            }
        };
        det = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = DColor.Transparent, Location = new Point(Tema.D(16), Tema.D(12)) };
        caixa.Controls.Add(det);

        detOrig = Tema.Rotulo("", Tema.D(300), Tema.Dourado, 20f);
        det.Controls.Add(detOrig);

        det.Controls.Add(Rotulinho(LiveTxt.T("col_trad")));
        detTrad = Tema.Campo("", Tema.D(300));
        detTrad.Font = Tema.F(11f);
        detTrad.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SalvarDetTrad(); } };
        detTrad.Leave += delegate { SalvarDetTrad(); };
        det.Controls.Add(detTrad);

        det.Controls.Add(Rotulinho(LiveTxt.T("col_curta").TrimEnd('.') + " (" + LiveTxt.T("det_curta_dica") + ")"));
        detCurta = Tema.Campo("", Tema.D(300));
        detCurta.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SalvarDetCurta(); } };
        detCurta.Leave += delegate { SalvarDetCurta(); };
        det.Controls.Add(detCurta);

        det.Controls.Add(Rotulinho(LiveTxt.T("col_cat") + " · " + LiveTxt.T("col_nivel")));
        detLinhaCat = Tema.Linha(Tema.D(300));
        detCat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Tema.D(150), BackColor = Tema.CorCampo, ForeColor = Tema.Texto, FlatStyle = FlatStyle.Flat, Font = Tema.F(9.5f), Margin = new Padding(0, 0, Tema.D(8), Tema.D(6)) };
        int[] ordem = Categorias.Ordem();
        for (int i = 0; i < ordem.Length; i++) detCat.Items.Add(Categorias.Nome(ordem[i]));
        detCat.SelectedIndexChanged += delegate
        {
            if (detPreenchendo || detTermo == null || detCat.SelectedIndex < 0) return;
            int c = ordem[detCat.SelectedIndex];
            if (c == detTermo.Cat) return;
            detTermo.Cat = c;
            detTermo.Travado = true;    // escolher a categoria na mao trava (Reclassificar nao desfaz)
            Glossario.Marcar();
        };
        detLinhaCat.Controls.Add(detCat);
        detNivel = Tema.Rotulo("", Tema.D(120), Tema.Texto, 9f);
        detNivel.Margin = new Padding(0, Tema.D(6), Tema.D(6), 0);
        detLinhaCat.Controls.Add(detNivel);
        detTrava = Tema.Botao("", Tema.D(110), Tema.D(28), 0, delegate
        {
            if (detTermo == null) return;
            detTermo.Travado = !detTermo.Travado;
            Glossario.Marcar();
        });
        detLinhaCat.Controls.Add(detTrava);
        det.Controls.Add(detLinhaCat);

        // conflito: a IA deu outra traducao
        detConflito = Tema.Linha(Tema.D(300));
        detConflito.BackColor = Tema.Rgb(0x2E, 0x26, 0x10);
        detConflito.Padding = new Padding(Tema.D(8));
        detConflitoTxt = Tema.Rotulo("", Tema.D(280), Tema.Aviso, 9f);
        detConflito.Controls.Add(detConflitoTxt);
        detConflito.Controls.Add(Tema.Botao(LiveTxt.T("glos_usar_nova"), Tema.D(150), Tema.D(26), 1, delegate { ResolverSelecionado(true); }));
        detConflito.Controls.Add(Tema.Botao(LiveTxt.T("glos_manter"), Tema.D(150), Tema.D(26), 0, delegate { ResolverSelecionado(false); }));
        det.Controls.Add(detConflito);

        detExemplo = Tema.Rotulo("", Tema.D(300), Tema.Fraco, 8.5f);
        det.Controls.Add(detExemplo);

        // sugestoes da IA
        detSugTitulo = Rotulinho(LiveTxt.T("sug_titulo"));
        det.Controls.Add(detSugTitulo);
        detSug = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Tema.Rgb(0x10, 0x16, 0x1B), Padding = new Padding(Tema.D(8), Tema.D(4), Tema.D(8), Tema.D(4)), Margin = new Padding(0, 0, 0, Tema.D(8)) };
        det.Controls.Add(detSug);

        detOnde = Tema.Rotulo("", Tema.D(300), Tema.Fraco, 8.5f);
        detOnde.Margin = new Padding(0, Tema.D(2), 0, Tema.D(8));
        det.Controls.Add(detOnde);

        detAcoes = Tema.Linha(Tema.D(300));
        detAcoes.Controls.Add(Tema.Botao("✨ " + LiveTxt.T("sug_bt"), Tema.D(190), Tema.D(30), 1, delegate { Sugerir(); }));
        detAcoes.Controls.Add(Tema.Botao("✂ " + LiveTxt.T("abr_mostrar"), Tema.D(190), Tema.D(30), 0, delegate { AbreviarEMostrar(); }));
        detAcoes.Controls.Add(Tema.Botao("🗑 " + LiveTxt.T("glos_apagar_sel"), Tema.D(170), Tema.D(30), 3, delegate { ApagarSelecionados(); }));
        Button ajudaBt = Tema.Botao("?", Tema.D(30), Tema.D(30), 0, delegate { detAjuda.Visible = !detAjuda.Visible; });
        ajudaBt.Font = Tema.F(10f, true);
        detAcoes.Controls.Add(ajudaBt);
        det.Controls.Add(detAcoes);
        glosAbrStatus = Tema.Rotulo("", Tema.D(300), Tema.Ok, 8.5f);
        det.Controls.Add(glosAbrStatus);
        detAjuda = Tema.Rotulo(LiveTxt.T("abr_ajuda") + "\n\n" + LiveTxt.T("glos_nota2") + "\n\n" + LiveTxt.T("sug_ajuda"), Tema.D(300), Tema.Fraco, 8.5f);
        detAjuda.Visible = false;
        det.Controls.Add(detAjuda);

        // sempre desconta a barra vertical: assim nunca aparece a horizontal
        caixa.Resize += delegate { AjustarDetalhe(caixa.Width - Tema.D(36) - SystemInformation.VerticalScrollBarWidth); };
        AjustarDetalhe(Tema.D(380));
        return caixa;
    }

    private Label Rotulinho(string texto)
    {
        Label l = Tema.Rotulo(texto.ToUpperInvariant(), Tema.D(300), Tema.Fraco, 7.5f, true);
        l.Margin = new Padding(0, Tema.D(8), 0, Tema.D(3));
        return l;
    }

    // largura do painel do termo mudou: campos e textos acompanham
    private void AjustarDetalhe(int w)
    {
        if (det == null || w < Tema.D(200)) return;
        det.SuspendLayout();
        foreach (Control c in det.Controls)
        {
            if (c is Label) c.MaximumSize = new Size(w, 0);
            else if (c is TextBox) c.Width = w;
            else if (c is FlowLayoutPanel && c != detSug) { c.MaximumSize = new Size(w, 0); c.MinimumSize = new Size(w, 0); }
        }
        detSug.MinimumSize = new Size(w, 0);
        detSug.MaximumSize = new Size(w, 0);
        foreach (Control c in detSug.Controls) { c.MaximumSize = new Size(w - Tema.D(16), 0); c.MinimumSize = new Size(w - Tema.D(16), 0); }
        detConflitoTxt.MaximumSize = new Size(w - Tema.D(16), 0);
        det.ResumeLayout(true);
    }

    private void MontarFiltros()
    {
        int[] ordem = Categorias.Ordem();
        int[] cont = new int[Categorias.LimiteId];
        int total = 0, conflitos = 0, novos = 0, travados = 0;
        string b = (busca ?? "").Trim().ToLowerInvariant();
        List<TermoGlossario> todos = Glossario.Lista;
        for (int i = 0; i < todos.Count; i++)
        {
            TermoGlossario t = todos[i];
            if (!Bate(t, b)) continue;
            total++;
            if (t.Cat >= 0 && t.Cat < cont.Length) cont[t.Cat]++;
            if (t.Conflito) conflitos++;
            if (t.Travado) travados++;
            else if (!t.Confirmado) novos++;
        }
        filtros.SuspendLayout();
        filtros.Controls.Clear();
        AddFiltro(FiltroTodos, LiveTxt.T("todos"), total, Tema.Texto);
        if (conflitos > 0 || filtroGlos == FiltroConflitos) AddFiltro(FiltroConflitos, "⚠ " + LiveTxt.T("glos_conflitos"), conflitos, Tema.Aviso);
        AddFiltro(FiltroNovos, LiveTxt.T("nivel_novo"), novos, Tema.Texto);
        AddFiltro(FiltroTravados, "🔒 " + LiveTxt.T("nivel_travado"), travados, Tema.Texto);
        for (int i = 0; i < ordem.Length; i++)
            AddFiltro(ordem[i], "● " + Categorias.Nome(ordem[i]), cont[ordem[i]], Tema.Cor(Categorias.Cor(ordem[i])));
        filtros.ResumeLayout(true);
    }

    // pilula: fundo escuro arredondado, nome na cor da categoria e o numero mais apagado
    private void AddFiltro(int id, string texto, int n, DColor cor)
    {
        bool sel = filtroGlos == id;
        string rot = texto + "  " + n;
        Button b = Tema.Botao(rot, Tema.D(14) + TextRenderer.MeasureText(rot, Tema.F(8.5f, sel)).Width + Tema.D(14), Tema.D(26), sel ? 1 : 0, delegate
        {
            filtroGlos = id;
            versaoGrade = -1;
            PreencherGrade();
        });
        b.Font = Tema.F(8.5f, sel);
        if (!sel)
        {
            b.ForeColor = cor;
            b.BackColor = Tema.Rgb(0x16, 0x1D, 0x23);
            b.FlatAppearance.BorderColor = Tema.Rgb(0x26, 0x31, 0x3A);
        }
        Tema.Arredondar(b, Tema.D(13));
        b.Margin = new Padding(0, 0, Tema.D(5), Tema.D(5));
        filtros.Controls.Add(b);
    }

    private static bool Bate(TermoGlossario t, string b)
    {
        return b.Length == 0
            || t.Original.ToLowerInvariant().IndexOf(b, StringComparison.Ordinal) >= 0
            || t.Traducao.ToLowerInvariant().IndexOf(b, StringComparison.Ordinal) >= 0;
    }

    private static string Icone(TermoGlossario t)
    {
        if (t.Conflito) return "⚠";
        if (t.Travado) return "🔒";
        if (!string.IsNullOrEmpty(t.Pacote)) return "📜";
        return "";
    }

    private void PreencherGrade()
    {
        if (grade == null) return;
        versaoGrade = Glossario.Versao;
        MontarFiltros();
        TermoGlossario selAntes = grade.CurrentRow != null && grade.CurrentRow.Index < filtrado.Count ? filtrado[grade.CurrentRow.Index] : null;
        filtrado.Clear();
        string b = (busca ?? "").Trim().ToLowerInvariant();
        List<TermoGlossario> todos = Glossario.Lista;
        for (int i = 0; i < todos.Count; i++)
        {
            TermoGlossario t = todos[i];
            if (!Bate(t, b)) continue;
            if (filtroGlos == FiltroConflitos && !t.Conflito) continue;
            if (filtroGlos == FiltroNovos && (t.Travado || t.Confirmado)) continue;
            if (filtroGlos == FiltroTravados && !t.Travado) continue;
            if (filtroGlos >= 0 && t.Cat != filtroGlos) continue;
            filtrado.Add(t);
        }
        // conflitos primeiro, depois em ordem alfabetica
        filtrado.Sort(delegate (TermoGlossario x, TermoGlossario y)
        {
            if (x.Conflito != y.Conflito) return x.Conflito ? -1 : 1;
            return string.Compare(x.Original, y.Original, StringComparison.OrdinalIgnoreCase);
        });
        int max = Math.Min(filtrado.Count, 3000);
        preenchendo = true;
        grade.SuspendLayout();
        grade.Rows.Clear();
        int voltar = -1;
        for (int i = 0; i < max; i++)
        {
            TermoGlossario t = filtrado[i];
            int r = grade.Rows.Add(t.Original, t.Traducao, string.IsNullOrEmpty(t.Curta) ? "" : t.Curta, Categorias.Nome(t.Cat), Icone(t));
            DataGridViewRow row = grade.Rows[r];
            DColor cor = Tema.Cor(Categorias.Cor(t.Cat));
            row.Cells["cat"].Style.ForeColor = cor;
            row.Cells["cat"].Style.SelectionForeColor = cor;
            row.Cells["ic"].Style.ForeColor = t.Conflito ? Tema.Aviso : t.Travado ? Tema.Destaque : Tema.Dourado;
            if (t == selAntes) voltar = r;
        }
        grade.ResumeLayout();
        preenchendo = false;
        // sem selecao o painel ficava vazio ate clicar: seleciona o primeiro
        if (voltar < 0 && grade.Rows.Count > 0) voltar = 0;
        if (voltar >= 0)
        {
            grade.CurrentCell = grade.Rows[voltar].Cells[0];
            grade.Rows[voltar].Selected = true;
        }
        glosContagem.Text = Glossario.Total == 0 ? LiveTxt.T("glos_vazio")
            : LiveTxt.F("glos_rodape", filtrado.Count, Glossario.Total) + (filtrado.Count > max ? " (" + max + ")" : "")
              + "   ·   " + LiveTxt.F("glos_memoria", Glossario.Travados);
        MostrarSelecionado();
    }

    private static string Nivel(TermoGlossario t)
    {
        if (t.Conflito) return LiveTxt.T("nivel_conflito");
        if (t.Travado) return LiveTxt.T("nivel_travado");
        if (t.Confirmado) return LiveTxt.T("nivel_confirmado");
        return LiveTxt.T("nivel_novo") + " (" + t.Vistos + "/" + Glossario.VistosConfirmar + ")";
    }

    private TermoGlossario Termo(int linha)
    {
        return linha >= 0 && linha < filtrado.Count ? filtrado[linha] : null;
    }

    // troca a traducao de um termo (editou, escolheu sugestao): trava, cache, e a tela do jogo troca na hora
    private void AplicarTraducao(TermoGlossario t, string nova)
    {
        nova = (nova ?? "").Trim();
        if (t == null || nova.Length == 0 || nova == t.Traducao) return;
        string antiga = t.Traducao;
        t.Traducao = nova;
        t.Travado = true;           // o que o jogador escolheu fica dele
        t.Alternativa = "";
        Glossario.Marcar();
        TradCache.Add(t.Original, nova);
        LiveMotor.Substituir(antiga, nova);
        Ponte.AtualizarTermo(t.Original, nova);
    }

    private void GradeMudou(object sender, DataGridViewCellEventArgs e)
    {
        if (preenchendo || e.RowIndex < 0) return;
        TermoGlossario t = Termo(e.RowIndex);
        if (t == null) return;
        DataGridViewRow row = grade.Rows[e.RowIndex];
        string col = grade.Columns[e.ColumnIndex].Name;
        if (col == "trad")
        {
            string nova = ((row.Cells["trad"].Value as string) ?? "").Trim();
            if (nova.Length == 0 || nova == t.Traducao) { preenchendo = true; row.Cells["trad"].Value = t.Traducao; preenchendo = false; return; }
            AplicarTraducao(t, nova);
        }
        else if (col == "curta")
        {
            string nova = ((row.Cells["curta"].Value as string) ?? "").Trim();
            if (nova == (t.Curta ?? "")) return;
            t.Curta = nova;
            Glossario.Marcar();
            if (LiveConfig.TelasCurtas.Count > 0) Ponte.Reenviar = true;
        }
        else if (col == "cat")
        {
            string nome = row.Cells["cat"].Value as string;
            int[] ordem = Categorias.Ordem();
            for (int i = 0; i < ordem.Length; i++)
            {
                if (Categorias.Nome(ordem[i]) != nome || ordem[i] == t.Cat) continue;
                t.Cat = ordem[i];
                t.Travado = true;       // escolher a categoria na mao trava (Reclassificar nao desfaz)
                Glossario.Marcar();
                break;
            }
        }
        else return;
        // a linha muda de cor/icone: refaz no proximo relogio (fora do evento da grade)
        BeginInvoke(new Action(delegate { if (!grade.IsDisposed && !grade.IsCurrentCellInEditMode) PreencherGrade(); }));
    }

    private void SalvarDetTrad()
    {
        if (detPreenchendo || detTermo == null) return;
        string nova = detTrad.Text.Trim();
        if (nova.Length == 0) { detTrad.Text = detTermo.Traducao; return; }
        AplicarTraducao(detTermo, nova);
    }

    private void SalvarDetCurta()
    {
        if (detPreenchendo || detTermo == null) return;
        string nova = detCurta.Text.Trim();
        if (nova == (detTermo.Curta ?? "")) return;
        detTermo.Curta = nova;
        Glossario.Marcar();
        if (LiveConfig.TelasCurtas.Count > 0) Ponte.Reenviar = true;
    }

    private void MostrarSelecionado()
    {
        if (det == null) return;
        TermoGlossario t = grade.CurrentRow != null ? Termo(grade.CurrentRow.Index) : null;
        // nao atropela o que o jogador esta digitando no painel
        bool digitando = t != null && t == detTermo && (detTrad.Focused || detCurta.Focused);
        detTermo = t;
        det.SuspendLayout();
        detPreenchendo = true;
        bool tem = t != null;
        foreach (Control c in det.Controls) if (c != detOrig && c != detAjuda && c != glosAbrStatus) c.Visible = tem;
        if (!tem)
        {
            detOrig.Text = "";
            detExemplo.Visible = true;
            detExemplo.Text = LiveTxt.T("glos_editar_dica2");
        }
        else
        {
            detOrig.Text = t.Original;
            if (!digitando)
            {
                detTrad.Text = t.Traducao;
                detCurta.Text = t.Curta ?? "";
            }
            int[] ordem = Categorias.Ordem();
            detCat.SelectedIndex = Array.IndexOf(ordem, t.Cat);
            detNivel.Text = Nivel(t);
            detNivel.ForeColor = t.Conflito ? Tema.Aviso : t.Travado ? Tema.Destaque : t.Confirmado ? Tema.Ok : Tema.Fraco;
            detTrava.Text = t.Travado ? "🔓 " + LiveTxt.T("det_destravar") : "🔒 " + LiveTxt.T("det_travar");
            Tema.Pintar(detTrava, t.Travado ? 0 : 1);
            detConflito.Visible = t.Conflito;
            if (t.Conflito) detConflitoTxt.Text = LiveTxt.F("glos_conflito_txt", t.Alternativa);
            detExemplo.Visible = !string.IsNullOrEmpty(t.Exemplo);
            detExemplo.Text = LiveTxt.T("glos_exemplo") + " " + t.Exemplo;
            List<string> ls = Ponte.Lugares(t.Original);
            int apertadas = 0;
            for (int i = 0; i < ls.Count; i++) if (LiveConfig.TelasCurtas.Contains(ls[i])) apertadas++;
            detOnde.Text = "📍 " + (ls.Count == 0 ? LiveTxt.T("abr_sem_telas") : LiveTxt.F("abr_resumo", ls.Count, apertadas));
            detOnde.ForeColor = apertadas > 0 ? Tema.Destaque : Tema.Fraco;
            MostrarSugestoes(t);
        }
        detPreenchendo = false;
        AtualizarStatusDetalhe();
        det.ResumeLayout(true);
    }

    private void AtualizarStatusDetalhe()
    {
        if (glosAbrStatus == null || glosAbrStatus.IsDisposed) return;
        string st = sugerindo || sugStatus.Length > 0 ? sugStatus : Abreviador.Status;
        bool erro = sugerindo || sugStatus.Length > 0 ? sugErro : Abreviador.StatusErro;
        if (glosAbrStatus.Text != st) glosAbrStatus.Text = st;
        glosAbrStatus.ForeColor = erro ? Tema.Erro : Tema.Ok;
    }

    private void MostrarSugestoes(TermoGlossario t)
    {
        detSug.SuspendLayout();
        detSug.Controls.Clear();
        List<string[]> l;
        if (!sugestoes.TryGetValue(t.Original, out l) || l.Count == 0)
        {
            detSugTitulo.Visible = false;
            detSug.Visible = false;
            detSug.ResumeLayout();
            return;
        }
        int w = Math.Max(Tema.D(200), detSug.MaximumSize.Width - Tema.D(16));
        for (int i = 0; i < l.Count; i++)
        {
            string tr = l[i][0];
            FlowLayoutPanel linha = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, MinimumSize = new Size(w, 0), MaximumSize = new Size(w, 0), BackColor = DColor.Transparent, Margin = new Padding(0, Tema.D(2), 0, Tema.D(2)) };
            Button usar = Tema.Botao(LiveTxt.T("sug_usar"), Tema.D(60), Tema.D(24), 0, delegate { AplicarTraducao(t, tr); });
            usar.ForeColor = Tema.Destaque;
            usar.FlatAppearance.BorderColor = Tema.Destaque;
            usar.Font = Tema.F(8f, true);
            Tema.Arredondar(usar, Tema.D(12));
            linha.Controls.Add(usar);
            Label lt = Tema.Rotulo(tr, w - Tema.D(70), Tema.Texto, 9.5f);
            lt.Margin = new Padding(0, Tema.D(3), 0, 0);
            linha.Controls.Add(lt);
            if (l[i][1].Length > 0)
            {
                Label ln = Tema.Rotulo(l[i][1], w - Tema.D(70), Tema.Fraco, 8f);
                ln.Margin = new Padding(Tema.D(66), 0, 0, Tema.D(2));
                linha.Controls.Add(ln);
                linha.SetFlowBreak(lt, true);
            }
            detSug.Controls.Add(linha);
        }
        detSugTitulo.Visible = true;
        detSug.Visible = true;
        detSug.ResumeLayout(true);
    }

    // botao "Sugerir traducoes": a IA da 3-4 opcoes para os termos selecionados (ate 20 por vez)
    private async void Sugerir()
    {
        if (sugerindo) return;
        List<TermoGlossario> sel = Selecionados();
        if (sel.Count == 0) return;
        if (sel.Count > 20) sel = sel.GetRange(0, 20);
        Provedor p = LiveConfig.Atual;
        string motivo;
        if (p.Tipo == 0) { sugStatus = LiveTxt.T("cia_gratis"); sugErro = true; AtualizarStatusDetalhe(); return; }
        if (!Provedores.Configurado(p, out motivo)) { sugStatus = motivo; sugErro = true; AtualizarStatusDetalhe(); return; }
        List<string> itens = new List<string>();
        for (int i = 0; i < sel.Count; i++) itens.Add(sel[i].Original + " || " + sel[i].Traducao + " || " + Categorias.Nome(sel[i].Cat));
        sugerindo = true;
        sugErro = false;
        sugStatus = LiveTxt.F("sug_andamento", sel.Count);
        AtualizarStatusDetalhe();
        try
        {
            Resposta r = await Http.Enviar(Provedores.MontarSugerir(p, itens));
            if (r.Codigo != 200)
            {
                sugStatus = LiveTxt.F("cia_erro", r.Codigo, string.IsNullOrEmpty(r.Erro) ? "" : r.Erro);
                sugErro = true;
            }
            else
            {
                List<string> l = Provedores.InterpretarLista(p, r.Corpo, itens.Count);
                if (l == null) { sugStatus = LiveTxt.T("teste_falhou"); sugErro = true; }
                else
                {
                    int n = 0;
                    for (int i = 0; i < sel.Count; i++)
                    {
                        List<string[]> ops = Provedores.LerSugestoes(l[i]);
                        if (ops.Count == 0) continue;
                        sugestoes[sel[i].Original] = ops;
                        n++;
                    }
                    sugStatus = LiveTxt.F("sug_fim", n);
                    if (sel.Count > 1 && n > 0) SugestoesEmLista(sel);
                }
            }
        }
        catch (Exception e) { sugStatus = LiveTxt.F("comp_erro", e.Message); sugErro = true; }
        sugerindo = false;
        if (det != null && !det.IsDisposed) MostrarSelecionado();
    }

    // varios termos: janela com as opcoes de cada um
    private void SugestoesEmLista(List<TermoGlossario> sel)
    {
        using (Form d = new Form())
        {
            d.Text = LiveTxt.T("sug_bt");
            d.FormBorderStyle = FormBorderStyle.Sizable;
            d.StartPosition = FormStartPosition.CenterParent;
            d.ClientSize = new Size(Tema.D(720), Tema.D(600));
            d.MinimizeBox = false;
            d.BackColor = Tema.Cartao;
            d.ForeColor = Tema.Texto;
            d.Font = Tema.F(9f);
            int w = Tema.D(660);
            FlowLayoutPanel f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Tema.D(16)) };
            d.Controls.Add(f);
            f.Controls.Add(Tema.Rotulo(LiveTxt.T("sug_lista_dica"), w, Tema.Fraco, 8.5f));
            for (int i = 0; i < sel.Count; i++)
            {
                TermoGlossario t = sel[i];
                List<string[]> ops;
                if (!sugestoes.TryGetValue(t.Original, out ops)) continue;
                Label tit = Tema.Rotulo(t.Original + "   →   " + t.Traducao, w, Tema.Dourado, 10.5f);
                tit.Margin = new Padding(0, Tema.D(12), 0, Tema.D(4));
                f.Controls.Add(tit);
                Label feito = Tema.Rotulo("", w, Tema.Ok, 8.5f);
                for (int k = 0; k < ops.Count; k++)
                {
                    string tr = ops[k][0];
                    FlowLayoutPanel linha = Tema.Linha(w);
                    Button usar = Tema.Botao(LiveTxt.T("sug_usar"), Tema.D(60), Tema.D(24), 0, delegate { AplicarTraducao(t, tr); feito.Text = "✓ " + tr; });
                    usar.ForeColor = Tema.Destaque;
                    usar.FlatAppearance.BorderColor = Tema.Destaque;
                    usar.Font = Tema.F(8f, true);
                    linha.Controls.Add(usar);
                    Label lt = Tema.Rotulo(tr + (ops[k][1].Length > 0 ? "   ·   " + ops[k][1] : ""), w - Tema.D(70), Tema.Texto, 9.5f);
                    lt.Margin = new Padding(0, Tema.D(4), 0, 0);
                    linha.Controls.Add(lt);
                    f.Controls.Add(linha);
                }
                f.Controls.Add(feito);
            }
            d.ShowDialog(this);
        }
        versaoGrade = -1;
        PreencherGrade();
    }

    private List<TermoGlossario> Selecionados()
    {
        List<TermoGlossario> l = new List<TermoGlossario>();
        foreach (DataGridViewRow r in grade.SelectedRows)
        {
            TermoGlossario t = Termo(r.Index);
            if (t != null) l.Add(t);
        }
        if (l.Count == 0 && grade.CurrentRow != null && Termo(grade.CurrentRow.Index) != null) l.Add(Termo(grade.CurrentRow.Index));
        return l;
    }

    // botao "Abreviar e mostrar": a IA abrevia os selecionados (os que ainda nao tem) e as telas onde eles
    // aparecem em lista (2+ textos juntos) ficam apertadas. Dica do mouse e titulos (1 texto por vez) ficam inteiros.
    private void AbreviarEMostrar()
    {
        List<TermoGlossario> sel = Selecionados();
        if (sel.Count == 0) return;
        // lista inteira sem filtrar a categoria: confirma (titulos, menus e nomes de tudo junto)
        if (filtroGlos == FiltroTodos && sel.Count > 20
            && MessageBox.Show(this, LiveTxt.F("abr_conf_todos", sel.Count), LiveTxt.T("abr_mostrar"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Abreviador.Iniciar(sel);
        int marcadas = 0;
        for (int i = 0; i < sel.Count; i++)
        {
            List<string> ls = Ponte.Lugares(sel[i].Original);
            for (int k = 0; k < ls.Count; k++)
            {
                // so listas (2+ textos juntos) e nunca caixa de informacao (dica do mouse, resumo)
                if (LiveConfig.TelasCurtas.Contains(ls[k]) || Ponte.Vezes(ls[k]) < 2 || Ponte.Dica(ls[k])) continue;
                Ponte.MarcarTela(ls[k], true);
                marcadas++;
            }
        }
        if (marcadas == 0) Ponte.Reenviar = true;
        MostrarSelecionado();
    }

    // pacote xianxia: escolhe os grupos, importa ou remove
    private void Pacote()
    {
        using (Form d = new Form())
        {
            d.Text = LiveTxt.T("pac_bt");
            d.FormBorderStyle = FormBorderStyle.Sizable;
            d.StartPosition = FormStartPosition.CenterParent;
            d.ClientSize = new Size(Tema.D(700), Tema.D(600));
            d.MinimizeBox = false;
            d.BackColor = Tema.Cartao;
            d.ForeColor = Tema.Texto;
            d.Font = Tema.F(9f);
            int w = Tema.D(640);
            FlowLayoutPanel f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Tema.D(16)) };
            d.Controls.Add(f);
            f.Controls.Add(Tema.Rotulo(LiveTxt.T("pac_dica"), w, Tema.Texto, 9f));
            if (!PacoteXianxia.TemIdioma)
            {
                f.Controls.Add(Tema.Rotulo(LiveTxt.T("pac_idioma"), w, Tema.Aviso, 9f));
                d.ShowDialog(this);
                return;
            }
            List<CheckBox> caixas = new List<CheckBox>();
            List<Label> contas = new List<Label>();
            for (int g = 0; g < PacoteXianxia.Grupos; g++)
            {
                CheckBox cb = Tema.Marca(LiveTxt.T("pac_g" + g), true, w);
                cb.Font = Tema.F(9f, true);
                cb.Margin = new Padding(0, Tema.D(10), 0, 0);
                caixas.Add(cb);
                f.Controls.Add(cb);
                Label ex = Tema.Rotulo(PacoteXianxia.Exemplos(g, 4) + "  …", w - Tema.D(20), Tema.Fraco, 8.5f);
                ex.Margin = new Padding(Tema.D(20), 0, 0, 0);
                f.Controls.Add(ex);
                Label conta = Tema.Rotulo("", w - Tema.D(20), Tema.Fraco, 8f);
                conta.Margin = new Padding(Tema.D(20), 0, 0, 0);
                contas.Add(conta);
                f.Controls.Add(conta);
            }
            Action contar = delegate
            {
                for (int g = 0; g < contas.Count; g++)
                    contas[g].Text = LiveTxt.F("pac_conta", PacoteXianxia.Total(g), PacoteXianxia.NoGlossario(g));
            };
            contar();
            Label st = Tema.Rotulo("", w, Tema.Ok, 9f);
            FlowLayoutPanel bts = Tema.Linha(w);
            bts.Margin = new Padding(0, Tema.D(14), 0, 0);
            bts.Controls.Add(Tema.Botao(LiveTxt.T("pac_importar"), Tema.D(200), Tema.D(30), 1, delegate
            {
                List<int> gs = new List<int>();
                for (int g = 0; g < caixas.Count; g++) if (caixas[g].Checked) gs.Add(g);
                int ja;
                int n = PacoteXianxia.Importar(gs, out ja);
                st.Text = LiveTxt.F("pac_importados", n, ja);
                st.ForeColor = Tema.Ok;
                contar();
            }));
            bts.Controls.Add(Tema.Botao(LiveTxt.T("pac_remover"), Tema.D(200), Tema.D(30), 3, delegate
            {
                List<int> gs = new List<int>();
                for (int g = 0; g < caixas.Count; g++) if (caixas[g].Checked) gs.Add(g);
                if (MessageBox.Show(d, LiveTxt.T("pac_remover_conf"), LiveTxt.T("pac_bt"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int ficam;
                int n = PacoteXianxia.Remover(gs, out ficam);
                st.Text = LiveTxt.F("pac_removidos", n, ficam);
                st.ForeColor = Tema.Aviso;
                contar();
            }));
            f.Controls.Add(bts);
            f.Controls.Add(st);
            d.ShowDialog(this);
        }
        versaoGrade = -1;
        PreencherGrade();
    }

    // todas as telas vistas nesta sessao: marcar = apertada (a IA abrevia os textos dela e ali vai a versao curta)
    private void Telas()
    {
        using (Form d = new Form())
        {
            d.Text = LiveTxt.T("abr_telas_bt");
            d.FormBorderStyle = FormBorderStyle.Sizable;
            d.StartPosition = FormStartPosition.CenterParent;
            d.ClientSize = new Size(Tema.D(700), Tema.D(560));
            d.MinimizeBox = false;
            d.BackColor = Tema.Cartao;
            d.ForeColor = Tema.Texto;
            d.Font = Tema.F(9f);
            int w = Tema.D(640);
            FlowLayoutPanel f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Tema.D(16)) };
            d.Controls.Add(f);
            f.Controls.Add(Tema.Rotulo(LiveTxt.T("abr_guia"), w, Tema.Texto, 9f));
            List<string> ls = Ponte.TodosLugares();
            // marcadas primeiro, depois as listas, depois as com mais textos
            ls.Sort(delegate (string a, string b)
            {
                bool ma = LiveConfig.TelasCurtas.Contains(a), mb = LiveConfig.TelasCurtas.Contains(b);
                if (ma != mb) return ma ? -1 : 1;
                bool la = Ponte.Vezes(a) >= 2, lb2 = Ponte.Vezes(b) >= 2;
                if (la != lb2) return la ? -1 : 1;
                int c = Ponte.TextosDe(b).Count.CompareTo(Ponte.TextosDe(a).Count);
                return c != 0 ? c : string.CompareOrdinal(a, b);
            });
            foreach (string l in LiveConfig.TelasCurtas) if (!ls.Contains(l)) ls.Add(l);
            List<CheckBox> caixas = new List<CheckBox>();
            List<bool> ehDica = new List<bool>();
            FlowLayoutPanel bts = Tema.Linha(w);
            bts.Controls.Add(Tema.Botao(LiveTxt.T("abr_marcar_todas"), Tema.D(300), Tema.D(28), 0, delegate
            {
                // caixas de informacao ficam de fora (se quiser, marca uma por uma)
                for (int k = 0; k < caixas.Count; k++) if (!ehDica[k]) caixas[k].Checked = true;
            }));
            bts.Controls.Add(Tema.Botao(LiveTxt.T("abr_desmarcar_todas"), Tema.D(170), Tema.D(28), 0, delegate { foreach (CheckBox c in caixas) c.Checked = false; }));
            f.Controls.Add(bts);
            if (ls.Count == 0) f.Controls.Add(Tema.Rotulo(LiveTxt.T("abr_sem_telas"), w, Tema.Aviso, 9f));
            for (int i = 0; i < ls.Count; i++)
            {
                string lugar = ls[i];
                List<string> textos = Ponte.TextosDe(lugar);
                // nome da tela = o que tem nela, com o antes -> depois
                List<string> ex = new List<string>();
                for (int k = 0; k < textos.Count && ex.Count < 4; k++)
                {
                    string tr = Ponte.Traducao(textos[k]) ?? textos[k];
                    TermoGlossario g = Glossario.Obter(textos[k]);
                    ex.Add(g != null && !string.IsNullOrEmpty(g.Curta) ? tr + " → " + g.Curta : tr);
                }
                string nome = ex.Count > 0 ? string.Join("  ·  ", ex) + (textos.Count > ex.Count ? "  …" : "") : lugar;
                CheckBox cb = Tema.Marca(nome, LiveConfig.TelasCurtas.Contains(lugar), w);
                cb.Margin = new Padding(0, Tema.D(10), 0, 0);
                cb.CheckedChanged += delegate { Ponte.MarcarTela(lugar, cb.Checked); };
                caixas.Add(cb);
                ehDica.Add(Ponte.Dica(lugar));
                f.Controls.Add(cb);
                string tipo = Ponte.Dica(lugar) ? LiveTxt.T("abr_tipo_dica") : Ponte.Vezes(lugar) >= 2 ? LiveTxt.T("abr_tipo_lista") : LiveTxt.T("abr_tipo_um");
                Label lb = Tema.Rotulo(tipo + "  ·  " + LiveTxt.F("abr_n_textos", textos.Count) + "  ·  " + lugar, w, Tema.Fraco, 7.5f);
                lb.Margin = new Padding(Tema.D(20), 0, 0, 0);
                f.Controls.Add(lb);
            }
            d.ShowDialog(this);
        }
        MostrarSelecionado();
    }

    private void ResolverSelecionado(bool usarNova)
    {
        TermoGlossario t = grade.CurrentRow != null ? Termo(grade.CurrentRow.Index) : null;
        if (t == null || !t.Conflito) return;
        string antiga = t.Traducao;
        Glossario.Resolver(t, usarNova);
        if (usarNova)
        {
            TradCache.Add(t.Original, t.Traducao);
            LiveMotor.Substituir(antiga, t.Traducao);
            Ponte.AtualizarTermo(t.Original, t.Traducao);
        }
        PreencherGrade();
    }

    private void ApagarSelecionados()
    {
        List<TermoGlossario> apagar = new List<TermoGlossario>();
        foreach (DataGridViewRow r in grade.SelectedRows)
        {
            TermoGlossario t = Termo(r.Index);
            if (t != null) apagar.Add(t);
        }
        if (apagar.Count == 0) return;
        if (MessageBox.Show(this, LiveTxt.F("glos_apagar_conf", apagar.Count), LiveTxt.T("nav_glos"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        for (int i = 0; i < apagar.Count; i++) Glossario.Remover(apagar[i]);
        LiveMotor.VarrerAgora = true;
        PreencherGrade();
    }

    private void NovoTermo()
    {
        using (Form d = new Form())
        {
            d.Text = LiveTxt.T("glos_novo");
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.StartPosition = FormStartPosition.CenterParent;
            d.ClientSize = new Size(Tema.D(480), Tema.D(250));
            d.MinimizeBox = false;
            d.MaximizeBox = false;
            d.BackColor = Tema.Cartao;
            d.ForeColor = Tema.Texto;
            d.Font = Tema.F(9f);
            int w = Tema.D(440);
            FlowLayoutPanel f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Tema.D(18)) };
            d.Controls.Add(f);
            f.Controls.Add(Tema.Rotulo(LiveTxt.T("novo_orig"), w, Tema.Fraco, 8.5f));
            TextBox orig = Tema.Campo("", w);
            f.Controls.Add(orig);
            f.Controls.Add(Tema.Rotulo(LiveTxt.T("lbl_trad"), w, Tema.Fraco, 8.5f));
            TextBox trad = Tema.Campo("", w);
            f.Controls.Add(trad);
            ComboBox cat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Tema.D(220), BackColor = Tema.CorCampo, ForeColor = Tema.Texto, FlatStyle = FlatStyle.Flat };
            int[] ordem = Categorias.Ordem();
            for (int i = 0; i < ordem.Length; i++) cat.Items.Add(Categorias.Nome(ordem[i]));
            int pre = filtroGlos >= 0 ? Array.IndexOf(ordem, filtroGlos) : -1;
            cat.SelectedIndex = pre >= 0 ? pre : Array.IndexOf(ordem, 0);
            orig.TextChanged += delegate
            {
                // categoria automatica enquanto digita (o jogador pode trocar)
                if (filtroGlos < 0 && orig.Text.Trim().Length > 0)
                {
                    int c = Glossario.Categorizar(orig.Text.Trim(), "", -1);
                    int p = Array.IndexOf(ordem, c);
                    if (p >= 0) cat.SelectedIndex = p;
                }
            };
            f.Controls.Add(cat);
            Button ok = Tema.Botao(LiveTxt.T("add"), Tema.D(120), Tema.D(30), 1, null);
            ok.DialogResult = DialogResult.OK;
            ok.Margin = new Padding(0, Tema.D(12), 0, 0);
            f.Controls.Add(ok);
            d.AcceptButton = ok;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string o = orig.Text.Trim();
            string n = trad.Text.Trim();
            if (o.Length == 0 || n.Length == 0) return;
            Glossario.Definir(o, n, ordem[Math.Max(0, cat.SelectedIndex)], true);
            TradCache.Add(o, n);
            // a ponte so troca o que ainda nao tem traducao: devolve os originais e pede de novo
            LiveMotor.Reiniciar();
            LiveMotor.VarrerAgora = true;
            PreencherGrade();
        }
    }

    // ---------------- 06 Categorias ----------------

    private void PaginaCategorias()
    {
        int[] ordem = Categorias.Ordem();
        if (!Categorias.Existe(catSel)) catSel = ordem.Length > 0 ? ordem[0] : 0;

        FlowLayoutPanel topo = Cartao(null);
        string[] nomes = new string[ordem.Length];
        int pos = -1;
        for (int i = 0; i < ordem.Length; i++)
        {
            nomes[i] = Categorias.Nome(ordem[i]);
            if (!catNova && ordem[i] == catSel) pos = i;
        }
        Grade(topo, nomes, pos, 5, Interna, delegate (int i)
        {
            catSel = ordem[i];
            catNova = false;
            catConfApagar = false;
            catMsg = "";
            MostrarAba(AbaCats);
        });
        topo.Controls.Add(Tema.Botao(LiveTxt.T("cat_novo"), Tema.D(150), Tema.D(28), catNova ? 1 : 0, delegate
        {
            catNova = true;
            catMsg = "";
            catConfApagar = false;
            MostrarAba(AbaCats);
        }));

        if (catNova)
        {
            FlowLayoutPanel n = Cartao(LiveTxt.T("cat_nova_tit"));
            Rot(n, LiveTxt.T("cat_nome"), Tema.Fraco, 8.5f);
            TextBox nome = Tema.Campo("", Tema.D(300));
            nome.MaxLength = Categorias.MaxNome;
            n.Controls.Add(nome);
            Rot(n, LiveTxt.T("cat_palavras"), Tema.Fraco, 8.5f);
            TextBox pal = Tema.Caixa("", Interna, 3, Categorias.MaxPalavras);
            n.Controls.Add(pal);
            Dica(n, LiveTxt.T("cat_ex_pal"));
            Rot(n, LiveTxt.T("cat_instr"), Tema.Fraco, 8.5f);
            TextBox ins = Tema.Caixa("", Interna, 2, Categorias.MaxInstrucao);
            n.Controls.Add(ins);
            Dica(n, LiveTxt.T("cat_ex"));
            if (Gratis) Rot(n, LiveTxt.T("cat_gratis"), Tema.Aviso);
            FlowLayoutPanel l = Tema.Linha(Interna);
            l.Controls.Add(Tema.Botao(LiveTxt.T("cat_criar"), Tema.D(120), Tema.D(30), 1, delegate
            {
                string erro;
                int id = Categorias.Criar(nome.Text, pal.Text, ins.Text, out erro);
                if (id >= 0)
                {
                    catSel = id;
                    catNova = false;
                    catMsg = "";
                }
                else
                {
                    catMsg = LiveTxt.F(erro, Categorias.MaxNovas);
                    catMsgErro = true;
                }
                MostrarAba(AbaCats);
            }));
            l.Controls.Add(Tema.Botao(LiveTxt.T("cancelar"), Tema.D(120), Tema.D(30), 0, delegate { catNova = false; catMsg = ""; MostrarAba(AbaCats); }));
            n.Controls.Add(l);
            if (catMsg.Length > 0) Rot(n, catMsg, catMsgErro ? Tema.Erro : Tema.Ok);
            return;
        }

        CategoriaInfo ci = Categorias.Obter(catSel);
        if (ci == null) return;
        FlowLayoutPanel c = Cartao(Categorias.Nome(catSel));
        if (ci.Padrao) Dica(c, LiveTxt.T("cat_fixa"));
        else
        {
            Rot(c, LiveTxt.T("cat_nome"), Tema.Fraco, 8.5f);
            TextBox nome = Tema.Campo(ci.Nome, Tema.D(300));
            nome.MaxLength = Categorias.MaxNome;
            nome.TextChanged += delegate { ci.Nome = nome.Text; Categorias.Marcar(); };
            c.Controls.Add(nome);
        }
        Rot(c, LiveTxt.T("cat_palavras"), Tema.Fraco, 8.5f);
        TextBox palavras = Tema.Caixa(ci.Palavras, Interna, 4, Categorias.MaxPalavras);
        palavras.TextChanged += delegate { ci.Palavras = palavras.Text; Categorias.Marcar(); };
        c.Controls.Add(palavras);
        Dica(c, LiveTxt.T("cat_ex_pal2"));

        FlowLayoutPanel cab = Tema.Linha(Interna);
        Label tit = Tema.Rotulo(LiveTxt.T("cat_instr"), Interna - Tema.D(90), Tema.Fraco, 8.5f);
        tit.AutoSize = false;
        tit.Width = Interna - Tema.D(90);
        tit.Height = Tema.D(18);
        cab.Controls.Add(tit);
        Label cont = Tema.Rotulo((ci.Instrucao ?? "").Length + " / " + Categorias.MaxInstrucao, Tema.D(80), Tema.Fraco, 8.5f);
        cab.Controls.Add(cont);
        c.Controls.Add(cab);
        TextBox instr = Tema.Caixa(ci.Instrucao, Interna, 3, Categorias.MaxInstrucao);
        instr.TextChanged += delegate
        {
            ci.Instrucao = instr.Text;
            Categorias.Marcar();
            cont.Text = instr.Text.Length + " / " + Categorias.MaxInstrucao;
        };
        c.Controls.Add(instr);
        Dica(c, LiveTxt.T("cat_ex"));
        if (Gratis) Rot(c, LiveTxt.T("cat_gratis"), Tema.Aviso);

        FlowLayoutPanel l1 = Tema.Linha(Interna);
        l1.Controls.Add(Tema.Botao(LiveTxt.T("cat_restaurar"), Tema.D(170), Tema.D(30), 0, delegate
        {
            Categorias.Restaurar(catSel);
            catConfApagar = false;
            catMsg = "";
            MostrarAba(AbaCats);
        }));
        if (!ci.Padrao)
        {
            l1.Controls.Add(Tema.Botao(LiveTxt.T(catConfApagar ? "cat_apagar_conf" : "cat_apagar"), Tema.D(210), Tema.D(30), catConfApagar ? 3 : 0, delegate
            {
                if (catConfApagar)
                {
                    Categorias.Apagar(catSel);
                    if (filtroGlos == catSel) filtroGlos = FiltroTodos;
                    catSel = 1;
                    catConfApagar = false;
                    catMsg = "";
                }
                else catConfApagar = true;
                MostrarAba(AbaCats);
            }));
        }
        c.Controls.Add(l1);

        FlowLayoutPanel r = Cartao(LiveTxt.T("cat_reclass_tit"));
        FlowLayoutPanel l2 = Tema.Linha(Interna);
        l2.Controls.Add(Tema.Botao(LiveTxt.T("cat_reclass"), Tema.D(250), Tema.D(30), 0, delegate
        {
            int n = Glossario.Reclassificar();
            catMsg = LiveTxt.F("cat_reclass_n", n);
            catMsgErro = false;
            MostrarAba(AbaCats);
        }));
        Button ia = null;
        if (!Gratis)
        {
            ia = Tema.Botao("", Tema.D(250), Tema.D(30), 1, delegate
            {
                if (Classificador.Rodando) Classificador.Parar();
                else { catMsg = ""; Classificador.Iniciar(); }
            });
            l2.Controls.Add(ia);
        }
        r.Controls.Add(l2);
        Dica(r, LiveTxt.T("cat_reclass_dica"));
        if (!Gratis) Dica(r, LiveTxt.T("cat_reclass_ia_dica"));
        Label st = Rot(r, "", Tema.Ok);
        Label msg = Rot(r, catMsg, catMsgErro ? Tema.Erro : Tema.Ok);
        atualizarAba = delegate
        {
            if (ia != null) ia.Text = LiveTxt.T(Classificador.Rodando ? "cia_parar" : "cat_reclass_ia");
            st.Text = Gratis ? "" : Classificador.Status;
            st.ForeColor = Classificador.StatusErro ? Tema.Erro : Tema.Ok;
            msg.Text = catMsg;
        };
    }

    // ---------------- 07 Avancado ----------------

    private void PaginaAvancado()
    {
        bool gratis = Gratis;
        FlowLayoutPanel ritmo = Cartao(LiveTxt.T("av_ritmo"));
        if (gratis) Dica(ritmo, LiveTxt.T("rpm_gratis"));
        else
        {
            FlowLayoutPanel l1 = Tema.Linha(Interna);
            Label r1 = Tema.Rotulo(LiveTxt.T("rpm"), Tema.D(220), Tema.Texto, 9f);
            r1.AutoSize = false;
            r1.Width = Tema.D(220);
            r1.Height = Tema.D(24);
            l1.Controls.Add(r1);
            NumericUpDown rpm = Tema.Numero(1, 600, LiveConfig.Rpm, Tema.D(90));
            rpm.ValueChanged += delegate { LiveConfig.Rpm = (int)rpm.Value; LiveConfig.Marcar(); };
            l1.Controls.Add(rpm);
            ritmo.Controls.Add(l1);
            FlowLayoutPanel l2 = Tema.Linha(Interna);
            Label r2 = Tema.Rotulo(LiveTxt.T("lote"), Tema.D(220), Tema.Texto, 9f);
            r2.AutoSize = false;
            r2.Width = Tema.D(220);
            r2.Height = Tema.D(24);
            l2.Controls.Add(r2);
            NumericUpDown lote = Tema.Numero(1, 30, LiveConfig.Lote, Tema.D(90));
            lote.ValueChanged += delegate { LiveConfig.Lote = (int)lote.Value; LiveConfig.Marcar(); };
            l2.Controls.Add(lote);
            ritmo.Controls.Add(l2);
            Dica(ritmo, LiveTxt.T("rpm_aviso"));
        }

        // tecla global de pausar/continuar
        FlowLayoutPanel tec = Cartao(LiveTxt.T("av_teclas"));
        FlowLayoutPanel lt = Tema.Linha(Interna);
        Label rt = Tema.Rotulo(LiveTxt.T("tecla_pausa"), Tema.D(260), Tema.Texto, 9f);
        rt.AutoSize = false;
        rt.Width = Tema.D(260);
        rt.Height = Tema.D(28);
        rt.TextAlign = ContentAlignment.MiddleLeft;
        lt.Controls.Add(rt);
        string nomeAtual = capturando ? LiveTxt.T("tecla_aperte") : NomeTecla((Keys)LiveConfig.TeclaAlternar);
        lt.Controls.Add(Tema.Botao(nomeAtual, Tema.D(200), Tema.D(30), capturando ? 2 : 1, delegate
        {
            capturando = !capturando;
            teclaErro = "";
            MostrarAba(AbaAvanc);
        }));
        lt.Controls.Add(Tema.Botao(LiveTxt.T("tecla_tirar"), Tema.D(120), Tema.D(30), 0, delegate
        {
            capturando = false;
            LiveConfig.TeclaAlternar = 0;
            LiveConfig.Marcar();
            RegistrarTecla();
            MostrarAba(AbaAvanc);
        }));
        tec.Controls.Add(lt);
        Dica(tec, LiveTxt.T(capturando ? "tecla_dica_captura" : "tecla_dica"));
        if (teclaErro.Length > 0) Rot(tec, teclaErro, Tema.Erro);

        FlowLayoutPanel ferr = Cartao(LiveTxt.T("av_ferr"));
        FlowLayoutPanel l3 = Tema.Linha(Interna + Tema.D(6));
        int bw = (Interna - Tema.D(12)) / 3;
        Label msg = null;
        l3.Controls.Add(Tema.Botao(LiveTxt.T("comp_exportar"), bw, Tema.D(30), 0, delegate
        {
            msgAvanc = Compartilhar.Exportar();
            msgAvancErro = false;
            msg.Text = msgAvanc;
            msg.ForeColor = Tema.Ok;
        }));
        l3.Controls.Add(Tema.Botao(LiveTxt.T("comp_importar"), bw, Tema.D(30), 0, delegate
        {
            msgAvanc = Compartilhar.Importar();
            msgAvancErro = false;
            msg.Text = msgAvanc;
            msg.ForeColor = Tema.Ok;
            LiveMotor.VarrerAgora = true;
        }));
        l3.Controls.Add(Tema.Botao(LiveTxt.T("log_pasta"), bw, Tema.D(30), 0, delegate { AbrirPasta(Compartilhar.Pasta); }));
        ferr.Controls.Add(l3);
        Dica(ferr, LiveTxt.F("comp_dica", LiveConfig.Idioma));
        msg = Rot(ferr, msgAvanc, msgAvancErro ? Tema.Erro : Tema.Ok);

        FlowLayoutPanel cache = Cartao(LiveTxt.T("av_cache"));
        cache.Controls.Add(Tema.Botao(LiveTxt.T(confLimpar ? "limpar_conf" : "limpar_cache"), Interna, Tema.D(32), confLimpar ? 3 : 0, delegate
        {
            if (!confLimpar)
            {
                confLimpar = true;
            }
            else
            {
                confLimpar = false;
                int n = TradCache.Total;
                TradCache.Limpar();
                Glossario.LimparNaoTravados();
                LiveMotor.ZerarContadores();
                Mudou();   // Reiniciar: a tela volta ao original e e traduzida de novo
                LiveLog.Info("lg_cache_limpo", n);
                msgAvanc = LiveTxt.T("limpar_feito");
                msgAvancErro = false;
            }
            MostrarAba(AbaAvanc);
        }));
        if (confLimpar) Rot(cache, LiveTxt.F("limpar_aviso", TradCache.Total), Tema.Erro);
        else Dica(cache, LiveTxt.T("limpar_dica"));
    }

    // ---------------- 08 Log ----------------

    private Control PaginaLog()
    {
        TableLayoutPanel t = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, BackColor = Tema.Conteudo };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        FlowLayoutPanel topo = Tema.Linha(corpo.ClientSize.Width);
        topo.Controls.Add(Tema.Botao(LiveTxt.T("log_pasta"), Tema.D(160), Tema.D(28), 0, delegate { AbrirPasta(LiveConfig.Pasta); }));
        TextBox txt = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9f * Tema.Escala),
            BackColor = Tema.CorCampo,
            ForeColor = Tema.Texto,
            BorderStyle = BorderStyle.None
        };
        topo.Controls.Add(Tema.Botao(LiveTxt.T("log_limpar"), Tema.D(160), Tema.D(28), 0, delegate { LiveLog.Limpar(); txt.Text = ""; }));
        t.Controls.Add(topo);
        t.Controls.Add(txt);
        int visto = -1;
        atualizarAba = delegate
        {
            if (visto == LiveLog.Count && LiveLog.Count < 300) return;
            visto = LiveLog.Count;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < LiveLog.Count; i++) sb.Append(LiveLog.Texto(i)).Append("\r\n");
            if (txt.Text.Length == sb.Length) return;
            txt.Text = sb.ToString();
            txt.SelectionStart = txt.Text.Length;
            txt.ScrollToCaret();
        };
        return t;
    }

    // ---------------- 09 Interface ----------------

    private void PaginaInterface()
    {
        FlowLayoutPanel c = Cartao(LiveTxt.T("if_tamanho"));
        int[] valores = new int[] { 80, 90, 100, 110, 125, 140, 160 };
        string[] nomes = new string[valores.Length];
        int sel = -1;
        for (int i = 0; i < valores.Length; i++)
        {
            nomes[i] = valores[i] + "%";
            if (valores[i] == LiveConfig.Escala) sel = i;
        }
        Grade(c, nomes, sel, valores.Length, Interna, delegate (int i)
        {
            if (LiveConfig.Escala == valores[i]) return;
            LiveConfig.Escala = valores[i];
            LiveConfig.Marcar();
            BeginInvoke(new Action(delegate { Construir(false); }));
        });
        Dica(c, LiveTxt.T("if_tamanho_dica"));
        c.Controls.Add(Tema.Botao(LiveTxt.T("if_padrao"), Tema.D(200), Tema.D(30), 0, delegate
        {
            if (LiveConfig.Escala == 100) return;
            LiveConfig.Escala = 100;
            LiveConfig.Marcar();
            BeginInvoke(new Action(delegate { Construir(false); }));
        }));
    }
}
