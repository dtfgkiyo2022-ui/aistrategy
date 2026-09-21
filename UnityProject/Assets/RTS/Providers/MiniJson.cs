using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rts.Providers
{
    /// <summary>
    /// A small JSON reader for the one reply shape the gateway returns. Unity's Mono has no System.Text.Json and this
    /// assembly may not touch UnityEngine, so it is written out here. Objects become Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers double, plus string, bool and null. Anything malformed throws FormatException,
    /// which the transport treats as a failed call.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 32;

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0;
            var value = Value(text, ref i, 0);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected trailing text at " + i + ".");
            return value;
        }

        private static object Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Nested too deeply.");
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end.");
            char c = s[i];
            if (c == '{') return Obj(s, ref i, depth);
            if (c == '[') return Arr(s, ref i, depth);
            if (c == '"') return Str(s, ref i);
            if (Match(s, ref i, "true")) return true;
            if (Match(s, ref i, "false")) return false;
            if (Match(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        private static Dictionary<string, object> Obj(string s, ref int i, int depth)
        {
            var map = new Dictionary<string, object>();
            i++; Skip(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Object key expected at " + i + ".");
                string key = Str(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected at " + i + ".");
                i++;
                map[key] = Value(s, ref i, depth + 1);
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("Unexpected end.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("',' or '}' expected at " + i + ".");
            }
        }

        private static List<object> Arr(string s, ref int i, int depth)
        {
            var list = new List<object>();
            i++; Skip(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value(s, ref i, depth + 1));
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("Unexpected end.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("',' or ']' expected at " + i + ".");
            }
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Bad \\u escape.");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape at " + (i - 1) + ".");
                }
            }
            throw new FormatException("Unterminated string.");
        }

        private static double Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw new FormatException("Value expected at " + start + ".");
            return d;
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }
    }
}
