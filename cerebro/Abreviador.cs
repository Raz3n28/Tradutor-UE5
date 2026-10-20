using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Botao "Abreviar com IA" (aba Glossario, so no modo com chave): pede uma abreviacao curta para os termos
// selecionados que ainda nao tem (TermoGlossario.Curta). Mesmo jeito do Classificador: lotes, um pedido por vez,
// no ritmo dos "pedidos por minuto". Gasta tokens so quando o jogador aperta.
internal static class Abreviador
{
    private const int Lote = 40;

    public static bool Rodando;
    public static string Status = "";
    public static bool StatusErro;

    public static void Iniciar(List<TermoGlossario> selecionados)
    {
        if (Rodando) return;
        Provedor p = LiveConfig.Atual;
        string motivo;
        if (p.Tipo == 0) { Status = LiveTxt.T("cia_gratis"); StatusErro = true; return; }
        if (!Provedores.Configurado(p, out motivo)) { Status = motivo; StatusErro = true; return; }
        List<TermoGlossario> alvo = new List<TermoGlossario>();
        for (int i = 0; i < selecionados.Count; i++)
            if (string.IsNullOrEmpty(selecionados[i].Curta)) alvo.Add(selecionados[i]);
        if (alvo.Count == 0) { Status = LiveTxt.T("abr_nada"); StatusErro = false; return; }
        _ = Rodar(p, alvo);
    }

    private static async Task Rodar(Provedor p, List<TermoGlossario> alvo)
    {
        Rodando = true;
        StatusErro = false;
        int feitos = 0, falhas = 0;
        try
        {
            for (int ini = 0; ini < alvo.Count; ini += Lote)
            {
                List<TermoGlossario> parte = alvo.GetRange(ini, Mathf.Min(Lote, alvo.Count - ini));
                List<string> textos = new List<string>();
                for (int i = 0; i < parte.Count; i++) textos.Add(parte[i].Original + " || " + parte[i].Traducao);
                Status = LiveTxt.F("abr_andamento", feitos, alvo.Count);

                Pedido req;
                try { req = Provedores.MontarAbreviar(p, textos); }
                catch (Exception e)
                {
                    Status = LiveTxt.F("comp_erro", e.Message);
                    StatusErro = true;
                    break;
                }
                Resposta resp = await Http.Enviar(req);
                if (resp.Codigo != 200)
                {
                    Status = LiveTxt.F("cia_erro", resp.Codigo, string.IsNullOrEmpty(resp.Erro) ? "" : resp.Erro);
                    StatusErro = true;
                    break;
                }
                List<string> curtas = Provedores.InterpretarLista(p, resp.Corpo, textos.Count);
                if (curtas == null) falhas += parte.Count;
                else
                {
                    for (int i = 0; i < parte.Count; i++)
                    {
                        string c = (curtas[i] ?? "").Trim();
                        if (c.Length == 0 || c.Length >= parte[i].Traducao.Length) continue;   // nao ficou mais curta
                        if (string.IsNullOrEmpty(parte[i].Curta)) { parte[i].Curta = c; feitos++; }
                    }
                    Glossario.Marcar();
                    Ponte.Reenviar = true;
                }
                if (ini + Lote < alvo.Count) await Task.Delay((int)(60000f / Mathf.Max(1, LiveConfig.Rpm)));
            }
        }
        catch (Exception e)
        {
            Status = LiveTxt.F("comp_erro", e.Message);
            StatusErro = true;
        }
        if (!StatusErro) Status = LiveTxt.F("abr_fim", feitos, alvo.Count, falhas);
        Rodando = false;
    }
}
