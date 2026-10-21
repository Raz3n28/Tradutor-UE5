using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

// Botao "Gerar relatorio de erro" (Configuracoes): junta num .zip na Area de Trabalho o que ajuda a achar um
// problema: log do tradutor, config (SEM as chaves de API), UE4SS.log, log do jogo e o ultimo crash do jogo,
// e o fim do pedidos.txt. Nada e enviado para lugar nenhum: o jogador manda o zip se quiser.
internal static class Relatorio
{
    public static string Gerar()
    {
        string destino = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "TradutorUE5-relatorio-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".zip");
        Glossario.SalvarSeSujo();
        LiveConfig.SalvarSeSujo();
        using (FileStream fs = new FileStream(destino, FileMode.Create))
        using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            StringBuilder info = new StringBuilder();
            info.AppendLine("Tradutor UE5 " + Atualizacao.Atual);
            info.AppendLine("Data: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            info.AppendLine("Windows: " + Environment.OSVersion);
            info.AppendLine(".NET: " + Environment.Version);
            info.AppendLine("Pasta do mod: " + Ponte.PastaMod);
            info.AppendLine("Provedor: " + LiveConfig.ProvedorId + "  Idioma: " + LiveConfig.Idioma + "  Ativo: " + LiveConfig.Ativo);
            Texto(zip, "info.txt", info.ToString());

            // log do tradutor e config sem chaves
            foreach (string f in SeguroLista(LiveConfig.Pasta, "*.log")) Arquivo(zip, f, "tradutor/" + Path.GetFileName(f), 2000000);
            foreach (string f in SeguroLista(LiveConfig.Pasta, "log*.txt")) Arquivo(zip, f, "tradutor/" + Path.GetFileName(f), 2000000);
            if (File.Exists(LiveConfig.Arquivo))
            {
                StringBuilder sb = new StringBuilder();
                foreach (string l in File.ReadAllLines(LiveConfig.Arquivo))
                    if (!l.StartsWith("chave.", StringComparison.Ordinal)) sb.AppendLine(l);
                Texto(zip, "tradutor/config.txt", sb.ToString());
            }
            Arquivo(zip, Path.Combine(Ponte.PastaMod, "troca", "pedidos.txt"), "tradutor/pedidos-fim.txt", 300000);

            // UE4SS: ...\Win64\ue4ss\Mods\TradutorUE5 -> ...\Win64\ue4ss\UE4SS.log
            string ue4ss = Subir(Ponte.PastaMod, 2);
            if (ue4ss != null)
            {
                Arquivo(zip, Path.Combine(ue4ss, "UE4SS.log"), "ue4ss/UE4SS.log", 3000000);
                Arquivo(zip, Path.Combine(ue4ss, "UE4SS-settings.ini"), "ue4ss/UE4SS-settings.ini", 200000);
            }

            // jogo: ...\<Projeto>\Binaries\Win64 -> %LOCALAPPDATA%\<Projeto>\Saved (logs e o crash mais novo)
            string win64 = Subir(Ponte.PastaMod, 3);
            string projeto = win64 != null ? Path.GetFileName(Subir(win64, 2) ?? "") : "";
            if (projeto.Length > 0)
            {
                string saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), projeto, "Saved");
                foreach (string f in SeguroLista(Path.Combine(saved, "Logs"), "*.log")) Arquivo(zip, f, "jogo/Logs/" + Path.GetFileName(f), 8000000);
                string crashes = Path.Combine(saved, "Crashes");
                if (Directory.Exists(crashes))
                {
                    string novo = null;
                    DateTime quando = DateTime.MinValue;
                    foreach (string d in Directory.GetDirectories(crashes))
                    {
                        DateTime t = Directory.GetLastWriteTime(d);
                        if (t > quando) { quando = t; novo = d; }
                    }
                    if (novo != null)
                        foreach (string f in SeguroLista(novo, "*"))
                            if (!f.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) || new FileInfo(f).Length < 20000000)
                                Arquivo(zip, f, "jogo/Crash/" + Path.GetFileName(f), 20000000);
                }
            }
        }
        return destino;
    }

    private static string Subir(string pasta, int n)
    {
        try
        {
            DirectoryInfo d = new DirectoryInfo(pasta);
            for (int i = 0; i < n && d != null; i++) d = d.Parent;
            return d == null ? null : d.FullName;
        }
        catch { return null; }
    }

    private static List<string> SeguroLista(string pasta, string padrao)
    {
        List<string> l = new List<string>();
        try { if (Directory.Exists(pasta)) l.AddRange(Directory.GetFiles(pasta, padrao)); } catch { }
        return l;
    }

    private static void Texto(ZipArchive zip, string nome, string conteudo)
    {
        ZipArchiveEntry e = zip.CreateEntry(nome);
        using (StreamWriter w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(conteudo);
    }

    // copia o arquivo (so o fim, se for maior que max); arquivo em uso e aberto com compartilhamento
    private static void Arquivo(ZipArchive zip, string origem, string nome, long max)
    {
        try
        {
            if (!File.Exists(origem)) return;
            using (FileStream f = new FileStream(origem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (f.Length > max) f.Seek(f.Length - max, SeekOrigin.Begin);
                ZipArchiveEntry e = zip.CreateEntry(nome);
                using (Stream s = e.Open()) f.CopyTo(s);
            }
        }
        catch { }
    }
}
