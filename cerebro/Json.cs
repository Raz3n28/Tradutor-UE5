using System;
using System.Collections.Generic;
using System.Text;

// JSON minimo feito a mao (a Newtonsoft do jogo e uma versao cortada e nao serve).
// Ler: objeto = Dictionary<string, object>, lista = List<object>, texto = string, resto = string/bool/null.
internal static class Json
{
    public static string Esc(string s)
    {
        StringBuilder sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    public static string ListaTextos(List<string> textos)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < textos.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Esc(textos[i]));
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static string UrlEsc(string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        StringBuilder sb = new StringBuilder(b.Length * 2);
        for (int i = 0; i < b.Length; i++)
        {
            byte x = b[i];
            if ((x >= 'a' && x <= 'z') || (x >= 'A' && x <= 'Z') || (x >= '0' && x <= '9') || x == '-' || x == '_' || x == '.' || x == '~')
                sb.Append((char)x);
            else
                sb.Append('%').Append(x.ToString("X2"));
        }
        return sb.ToString();
    }

    public static object Parse(string texto)
    {
        Leitor l = new Leitor(texto);
        return l.Ler();
    }

    // anda pelo JSON: texto = chave de objeto, numero = posicao de lista. Devolve null se nao achar.
    public static object Obter(object raiz, params object[] caminho)
    {
        object atual = raiz;
        for (int i = 0; i < caminho.Length; i++)
        {
            if (atual == null) return null;
            string chave = caminho[i] as string;
            if (chave != null)
            {
                Dictionary<string, object> d = atual as Dictionary<string, object>;
                if (d == null || !d.ContainsKey(chave)) return null;
                atual = d[chave];
            }
            else
            {
                int pos = (int)caminho[i];
                List<object> l = atual as List<object>;
                if (l == null || pos < 0 || pos >= l.Count) return null;
                atual = l[pos];
            }
        }
        return atual;
    }

    private sealed class Leitor
    {
        private readonly string s;
        private int i;

        public Leitor(string texto) { s = texto; }

        public object Ler()
        {
            return Valor();
        }

        private void Espaco()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private object Valor()
        {
            Espaco();
            if (i >= s.Length) throw new Exception("JSON: fim inesperado");
            char c = s[i];
            if (c == '{') return Obj();
            if (c == '[') return Arr();
            if (c == '"') return Str();
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int ini = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == ini) throw new Exception("JSON: caractere inesperado '" + c + "' na posicao " + i);
            return s.Substring(ini, i - ini);
        }

        private Dictionary<string, object> Obj()
        {
            i++;
            Dictionary<string, object> d = new Dictionary<string, object>();
            Espaco();
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Espaco();
                string k = Str();
                Espaco();
                if (s[i] != ':') throw new Exception("JSON: faltou ':'");
                i++;
                d[k] = Valor();
                Espaco();
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new Exception("JSON: objeto mal formado");
            }
            return d;
        }

        private List<object> Arr()
        {
            i++;
            List<object> l = new List<object>();
            Espaco();
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Valor());
                Espaco();
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new Exception("JSON: lista mal formada");
            }
            return l;
        }

        private string Str()
        {
            if (s[i] != '"') throw new Exception("JSON: esperava texto");
            i++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char e = s[i++];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else if (e == 'u')
                    {
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                    }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
