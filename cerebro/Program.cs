using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// O cerebro: roda fora do jogo (a ponte do UE4SS abre ele junto com o jogo) e fecha sozinho quando o jogo fecha.
// Fica em ue4ss\Mods\TradutorUE5\cerebro\TradutorCerebro.exe
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool novo;
        using (Mutex unico = new Mutex(true, "TradutorUE5_Cerebro", out novo))
        {
            if (!novo) return;   // ja tem um aberto (o jogo abriu de novo antes do anterior fechar)

            // pasta do mod = a de cima da pasta do exe
            string exe = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Ponte.PastaMod = Directory.GetParent(exe) != null ? Directory.GetParent(exe).FullName : exe;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--mod") Ponte.PastaMod = args[i + 1];

            LiveLog.Info("==============================================");
            LiveLog.Info("lg_iniciando");
            try { LiveConfig.Carregar(); }
            catch (Exception e) { LiveLog.Erro("le_carregar", e, LiveLog.T("it_config")); }
            try { TradCache.Carregar(); }
            catch (Exception e) { LiveLog.Erro("le_carregar", e, LiveLog.T("it_cache")); }
            try { Categorias.Carregar(); }
            catch (Exception e) { LiveLog.Erro("le_carregar", e, LiveLog.T("it_cat")); }
            try { Glossario.Carregar(); }
            catch (Exception e) { LiveLog.Erro("le_carregar", e, LiveLog.T("it_glos")); }
            LiveLog.Info("lg_pronto");

            ApplicationConfiguration.Initialize();
            Application.Run(new Janela(args.Length > 0 && Array.IndexOf(args, "--mostrar") >= 0));
            Salvar();
        }
    }

    public static void Salvar()
    {
        try { TradCache.Flush(); LiveConfig.SalvarSeSujo(); Glossario.SalvarSeSujo(); } catch { }
    }

    // O jogo ainda esta aberto? (qualquer processo "*-Win64-Shipping" ou "*-WinGDK-Shipping")
    public static bool JogoAberto()
    {
        try
        {
            Process[] ps = Process.GetProcesses();
            for (int i = 0; i < ps.Length; i++)
            {
                string n = ps[i].ProcessName;
                if (n.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { return true; }
        return false;
    }
}
