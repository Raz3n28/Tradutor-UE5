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

    // Pacote de traducao (.zip, o jogador escolhe onde): glossario (com abreviacoes), cache, falas editadas e
    // telas apertadas do idioma atual. Quem importa joga traduzido sem gastar IA. Importar so ACRESCENTA.
    public static string ExportarZip(string destino)
    {
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), "tradutorue5_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            int g = Glossario.Exportar(Path.Combine(tmp, "glossario.txt"));
            int c = TradCache.Exportar(Path.Combine(tmp, "cache.txt"));
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            System.Collections.Generic.Dictionary<string, string> falas = Falas.TodasTravadas();
            foreach (System.Collections.Generic.KeyValuePair<string, string> kv in falas)
                sb.Append(Ponte.Escapar(kv.Key)).Append('\t').Append(Ponte.Escapar(kv.Value)).Append('\n');
            File.WriteAllText(Path.Combine(tmp, "falas.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(tmp, "telas_curtas.txt"), string.Join("\n", LiveConfig.TelasCurtas), new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(tmp, "info.txt"), "Tradutor UE5 " + Atualizacao.Atual + "\nidioma=" + LiveConfig.Idioma + "\n", new System.Text.UTF8Encoding(false));
            if (File.Exists(destino)) File.Delete(destino);
            System.IO.Compression.ZipFile.CreateFromDirectory(tmp, destino);
            try { Directory.Delete(tmp, true); } catch { }
            LiveLog.Info("lg_exportado", g, c, destino);
            return LiveTxt.F("pkt_exportado", g, c, falas.Count);
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_gravar", e, destino);
            return LiveTxt.F("comp_erro", e.Message);
        }
    }

    public static string ImportarZip(string origem)
    {
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), "tradutorue5_" + Guid.NewGuid().ToString("N"));
            System.IO.Compression.ZipFile.ExtractToDirectory(origem, tmp);
            string info = Path.Combine(tmp, "info.txt");
            if (File.Exists(info))
            {
                string idioma = "";
                foreach (string l in File.ReadAllLines(info)) if (l.StartsWith("idioma=", StringComparison.Ordinal)) idioma = l.Substring(7).Trim();
                if (idioma.Length > 0 && idioma != LiveConfig.Idioma) { try { Directory.Delete(tmp, true); } catch { } return LiveTxt.F("pkt_idioma", idioma, LiveConfig.Idioma); }
            }
            int g = File.Exists(Path.Combine(tmp, "glossario.txt")) ? Glossario.Importar(Path.Combine(tmp, "glossario.txt")) : 0;
            int c = File.Exists(Path.Combine(tmp, "cache.txt")) ? TradCache.Importar(Path.Combine(tmp, "cache.txt")) : 0;
            int f = 0;
            string arqF = Path.Combine(tmp, "falas.txt");
            if (File.Exists(arqF))
                foreach (string l in File.ReadAllLines(arqF, System.Text.Encoding.UTF8))
                {
                    int t = l.IndexOf('\t');
                    if (t <= 0) continue;
                    string texto = Ponte.Desescapar(l.Substring(0, t));
                    if (Falas.EstaTravada(texto)) continue;
                    Falas.Travar(texto, Ponte.Desescapar(l.Substring(t + 1)));
                    f++;
                }
            string arqT = Path.Combine(tmp, "telas_curtas.txt");
            if (File.Exists(arqT))
            {
                foreach (string l in File.ReadAllLines(arqT)) if (l.Trim().Length > 0) LiveConfig.TelasCurtas.Add(l.Trim());
                LiveConfig.Marcar();
                Ponte.Reenviar = true;
            }
            try { Directory.Delete(tmp, true); } catch { }
            LiveLog.Info("lg_importado", g, c);
            if (g + c + f > 0) { LiveMotor.Reiniciar(); LiveMotor.VarrerAgora = true; }
            return LiveTxt.F("pkt_importado", g, c, f);
        }
        catch (Exception e)
        {
            LiveLog.Erro("le_ler", e, origem);
            return LiveTxt.F("comp_erro", e.Message);
        }
    }

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
