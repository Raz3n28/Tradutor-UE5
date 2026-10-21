using System;
using System.Collections.Generic;
using System.IO;

// Backup automatico do glossario: uma copia por dia (pasta dados\backup), guardando as ultimas 7.
// Configuracoes > Restaurar backup volta uma delas.
internal static class Backup
{
    private const int Manter = 7;

    public static string Pasta
    {
        get
        {
            string p = Path.Combine(LiveConfig.Pasta, "backup");
            try { if (!Directory.Exists(p)) Directory.CreateDirectory(p); } catch { }
            return p;
        }
    }

    private static string ArqGlossario { get { return Path.Combine(LiveConfig.Pasta, "glossario_" + LiveConfig.Idioma + ".txt"); } }

    // chamado ao abrir: faz a copia de hoje (se ainda nao fez) e apaga as velhas
    public static void Diario()
    {
        Glossario.SalvarSeSujo();
        string g = ArqGlossario;
        if (!File.Exists(g)) return;
        string hoje = Path.Combine(Pasta, "glossario_" + LiveConfig.Idioma + "_" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
        if (!File.Exists(hoje)) File.Copy(g, hoje);
        List<string> l = Lista();
        for (int i = Manter; i < l.Count; i++) { try { File.Delete(l[i]); } catch { } }
    }

    // backups do idioma atual, o mais novo primeiro
    public static List<string> Lista()
    {
        List<string> l = new List<string>();
        try { l.AddRange(Directory.GetFiles(Pasta, "glossario_" + LiveConfig.Idioma + "_*.txt")); } catch { }
        l.Sort();
        l.Reverse();
        return l;
    }

    public static string Data(string arq)
    {
        string n = Path.GetFileNameWithoutExtension(arq);
        int u = n.LastIndexOf('_');
        return u >= 0 ? n.Substring(u + 1) : n;
    }

    // volta o glossario daquele dia (o atual vira um backup "antes de restaurar" antes)
    public static void Restaurar(string arq)
    {
        Glossario.SalvarSeSujo();
        string g = ArqGlossario;
        if (File.Exists(g)) File.Copy(g, Path.Combine(Pasta, "antes_de_restaurar_" + LiveConfig.Idioma + ".txt"), true);
        File.Copy(arq, g, true);
        Glossario.Carregar();
    }
}
