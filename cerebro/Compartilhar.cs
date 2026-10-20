using System;
using System.IO;

// Exportar / importar glossario e cache (aba Avancado), para o jogador passar a traducao para amigos
// ou o autor distribuir uma traducao pronta. Tudo fica na pasta "compartilhar" dentro da pasta do mod:
//   glossario_<idioma>.txt e cache_<idioma>.txt  (mesmo formato dos arquivos do mod)
// Importar so ACRESCENTA o que falta: nada que o jogador ja tem e trocado.
internal static class Compartilhar
{
    public static string Pasta
    {
        get
        {
            string p = Path.Combine(LiveConfig.Pasta, "compartilhar");
            try { if (!Directory.Exists(p)) Directory.CreateDirectory(p); } catch { }
            return p;
        }
    }

    private static string ArqGlossario { get { return Path.Combine(Pasta, "glossario_" + LiveConfig.Idioma + ".txt"); } }
    private static string ArqCache { get { return Path.Combine(Pasta, "cache_" + LiveConfig.Idioma + ".txt"); } }

    // devolve a mensagem para mostrar no menu (ja no idioma do menu)
    public static string Exportar()
    {
        try
        {
            int g = Glossario.Exportar(ArqGlossario);
            int c = TradCache.Exportar(ArqCache);
            LiveLog.Info("lg_exportado", g, c, Pasta);
            return LiveTxt.F("comp_exportado", g, c);
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_gravar", e, Pasta);
            return LiveTxt.F("comp_erro", e.Message);
        }
    }

    public static string Importar()
    {
        try
        {
            bool temG = File.Exists(ArqGlossario);
            bool temC = File.Exists(ArqCache);
            if (!temG && !temC) return LiveTxt.F("comp_nada", LiveConfig.Idioma);
            int g = temG ? Glossario.Importar(ArqGlossario) : 0;
            int c = temC ? TradCache.Importar(ArqCache) : 0;
            LiveLog.Info("lg_importado", g, c);
            // traducoes novas: o que esta na tela e traduzido de novo com elas
            if (g + c > 0) { LiveMotor.Reiniciar(); LiveMotor.VarrerAgora = true; }
            return LiveTxt.F("comp_importado", g, c);
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_ler", e, Pasta);
            return LiveTxt.F("comp_erro", e.Message);
        }
    }
}
