using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Botao "Reclassificar com IA" (aba Categorias, so no modo com chave): manda os termos NAO travados do glossario
// para a IA em lotes e coloca cada um na categoria que ela escolher. Um pedido por vez, no ritmo dos
// "pedidos por minuto". Termo travado nunca muda. Gasta tokens so quando o jogador aperta.
// Vai junto a frase onde o termo apareceu (quando tem): ajuda a IA a saber se 青城 e lugar ou seita.
internal static class Classificador
{
    private const int Lote = 40;

    public static bool Rodando;
    public static string Status = "";
    public static bool StatusErro;
    private static bool parar;

    public static void Iniciar()
    {
        if (Rodando) return;
        Provedor p = LiveConfig.Atual;
        string motivo;
        if (p.Tipo == 0) { Status = LiveTxt.T("cia_gratis"); StatusErro = true; return; }
        if (!Provedores.Configurado(p, out motivo)) { Status = motivo; StatusErro = true; return; }
        List<TermoGlossario> alvo = new List<TermoGlossario>();
        List<TermoGlossario> todos = Glossario.Lista;
        for (int i = 0; i < todos.Count; i++)
            if (!todos[i].Travado) alvo.Add(todos[i]);
        if (alvo.Count == 0) { Status = LiveTxt.T("cia_nada"); StatusErro = false; return; }
        parar = false;
        _ = Rodar(p, alvo);
    }

    public static void Parar()
    {
        parar = true;
    }

    private static async Task Rodar(Provedor p, List<TermoGlossario> alvo)
    {
        Rodando = true;
        StatusErro = false;
        int mudou = 0, feitos = 0, falhas = 0;
        LiveLog.Info("lg_cia_inicio", alvo.Count);
        try
        {
            for (int ini = 0; ini < alvo.Count && !parar; ini += Lote)
            {
                List<TermoGlossario> parte = alvo.GetRange(ini, Mathf.Min(Lote, alvo.Count - ini));
                List<string> textos = new List<string>();
                for (int i = 0; i < parte.Count; i++)
                {
                    string ex = parte[i].Exemplo ?? "";
                    textos.Add(ex.Length > 0 ? parte[i].Original + " || context: " + ex : parte[i].Original);
                }
                Status = LiveTxt.F("cia_andamento", feitos, alvo.Count, mudou);

                Pedido req;
                try { req = Provedores.MontarClassificar(p, textos); }
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
                    LiveLog.Aviso("lg_cia_erro", resp.Codigo);
                    break;
                }
                List<string> cats = Provedores.InterpretarLista(p, resp.Corpo, textos.Count);
                if (cats == null) falhas += parte.Count;   // resposta fora do formato: esse lote fica como esta
                else
                {
                    bool algum = false;
                    for (int i = 0; i < parte.Count; i++)
                    {
                        TermoGlossario t = parte[i];
                        if (t.Travado) continue;    // o jogador pode ter travado enquanto rodava
                        int id = Categorias.PorNome(cats[i]);
                        if (id >= 0 && id != t.Cat) { t.Cat = id; mudou++; algum = true; }
                    }
                    if (algum) Glossario.Marcar();
                }
                feitos += parte.Count;
                if (ini + Lote < alvo.Count && !parar) await Task.Delay((int)(60000f / Mathf.Max(1, LiveConfig.Rpm)));
            }
        }
        catch (Exception e)
        {
            Status = LiveTxt.F("comp_erro", e.Message);
            StatusErro = true;
        }
        if (!StatusErro) Status = LiveTxt.F("cia_fim", mudou, feitos, falhas);
        LiveLog.Info("lg_cia_fim", mudou, feitos, falhas);
        Rodando = false;
    }
}
