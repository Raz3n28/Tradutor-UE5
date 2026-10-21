using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Aviso de versao nova: ao abrir, olha a ultima versao publicada (Releases) no GitHub do projeto.
// Sem internet, sem release ou erro: fica quieto.
internal static class Atualizacao
{
    public const string Pagina = "https://github.com/Raz3n28/Tradutor-UE5/releases";
    private const string Api = "https://api.github.com/repos/Raz3n28/Tradutor-UE5/releases/latest";

    public static string Nova = "";      // ex.: "0.5.0" ("" = nada novo)
    public static string Link = Pagina;

    public static string Atual
    {
        get
        {
            Version v = typeof(Atualizacao).Assembly.GetName().Version;
            return v == null ? "0" : v.Major + "." + v.Minor + "." + v.Build;
        }
    }

    public static async Task Verificar()
    {
        try
        {
            using (HttpClient c = new HttpClient())
            {
                c.Timeout = TimeSpan.FromSeconds(10);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("TradutorUE5/" + Atual);
                string corpo = await c.GetStringAsync(Api);
                Match tag = Regex.Match(corpo, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                Match url = Regex.Match(corpo, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/[^\"]+)\"");
                if (!tag.Success) return;
                Version nova, atual;
                if (Version.TryParse(Completar(tag.Groups[1].Value), out nova) && Version.TryParse(Completar(Atual), out atual) && nova > atual)
                {
                    Nova = tag.Groups[1].Value;
                    if (url.Success) Link = url.Groups[1].Value;
                    LiveLog.Info("lg_versao_nova", Nova);
                }
            }
        }
        catch { }
    }

    private static string Completar(string v)
    {
        string[] p = v.Trim('.').Split('.');
        return p.Length == 1 ? v + ".0" : v.Trim('.');
    }
}
