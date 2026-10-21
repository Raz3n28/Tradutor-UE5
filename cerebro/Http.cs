using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Pedido HTTP montado pelos Provedores (no lugar do UnityWebRequest do mod original).
internal sealed class Pedido
{
    public string Metodo = "GET";
    public string Url;
    public byte[] Corpo;
    public string TipoCorpo;
    public int Timeout = 30;    // segundos
    public readonly List<KeyValuePair<string, string>> Cabecalhos = new List<KeyValuePair<string, string>>();

    public Pedido(string url, string metodo)
    {
        Url = url;
        Metodo = metodo;
    }

    public void Cab(string nome, string valor)
    {
        Cabecalhos.Add(new KeyValuePair<string, string>(nome, valor));
    }
}

internal sealed class Resposta
{
    public int Codigo;          // 0 = nem chegou no servidor (rede, timeout)
    public string Corpo;
    public string Erro;
    public int RetryAfter;      // segundos pedidos pelo servidor no 429 (0 = nao veio)
}

// Envia os pedidos. Chamado da thread da janela: o "await" volta para ela, entao o motor
// continua rodando numa thread so (como as corrotinas do Unity).
internal static class Http
{
    private static readonly HttpClient cliente = Criar();

    private static HttpClient Criar()
    {
        HttpClient c = new HttpClient();
        c.Timeout = System.Threading.Timeout.InfiniteTimeSpan;   // cada pedido tem o seu (Pedido.Timeout)
        return c;
    }

    public static async Task<Resposta> Enviar(Pedido p)
    {
        Resposta r = new Resposta();
        try
        {
            using (HttpRequestMessage msg = new HttpRequestMessage(new HttpMethod(p.Metodo), p.Url))
            using (System.Threading.CancellationTokenSource limite = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(p.Timeout)))
            {
                if (p.Corpo != null)
                {
                    msg.Content = new ByteArrayContent(p.Corpo);
                    if (p.TipoCorpo != null) msg.Content.Headers.TryAddWithoutValidation("Content-Type", p.TipoCorpo);
                }
                for (int i = 0; i < p.Cabecalhos.Count; i++)
                {
                    string nome = p.Cabecalhos[i].Key;
                    string valor = p.Cabecalhos[i].Value;
                    if (string.Equals(nome, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (msg.Content != null)
                        {
                            msg.Content.Headers.Remove("Content-Type");
                            msg.Content.Headers.TryAddWithoutValidation(nome, valor);
                        }
                    }
                    else msg.Headers.TryAddWithoutValidation(nome, valor);
                }
                using (HttpResponseMessage resp = await cliente.SendAsync(msg, limite.Token))
                {
                    r.Codigo = (int)resp.StatusCode;
                    r.Corpo = Encoding.UTF8.GetString(await resp.Content.ReadAsByteArrayAsync());
                    // tokens gastos (resposta da IA): conta para o painel de gasto
                    if (r.Codigo == 200) Gasto.Registrar(r.Corpo);
                    if (!resp.IsSuccessStatusCode) r.Erro = resp.ReasonPhrase;
                    if (resp.Headers.RetryAfter != null)
                    {
                        if (resp.Headers.RetryAfter.Delta.HasValue) r.RetryAfter = (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds;
                        else if (resp.Headers.RetryAfter.Date.HasValue) r.RetryAfter = (int)(resp.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            r.Codigo = 0;
            r.Erro = "timeout";
        }
        catch (Exception e)
        {
            r.Codigo = 0;
            r.Erro = e.InnerException != null ? e.InnerException.Message : e.Message;
        }
        return r;
    }
}
