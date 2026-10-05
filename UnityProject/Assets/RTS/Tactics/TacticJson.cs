using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rts.Tactics
{
    /// <summary>Small dependency-free JSON reader used by the Unity and headless tactic entry points.</summary>
    internal static class TacticJson
    {
        internal static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON is null.");
            var parser = new Parser(text);
            object value = parser.Value();
            parser.White();
            if (!parser.End) throw new FormatException("Trailing JSON data.");
            return value;
        }

        internal static Dictionary<string, object> Object(object value, string name = "object")
        {
            var result = value as Dictionary<string, object>;
            if (result == null) throw new FormatException(name + " must be an object.");
            return result;
        }

        internal static List<object> Array(object value, string name = "array")
        {
            var result = value as List<object>;
            if (result == null) throw new FormatException(name + " must be an array.");
            return result;
        }

        internal static string String(Dictionary<string, object> obj, string name, bool required = false)
        {
            if (!obj.TryGetValue(name, out var value) || value == null)
            {
                if (required) throw new FormatException(name + " is required.");
                return null;
            }
            var result = value as string;
            if (result == null) throw new FormatException(name + " must be a string.");
            return result;
        }

        internal static long Integer(Dictionary<string, object> obj, string name, long fallback, bool required = false)
        {
            if (!obj.TryGetValue(name, out var value) || value == null)
            {
                if (required) throw new FormatException(name + " is required.");
                return fallback;
            }
            if (value is long l) return l;
            if (value is decimal m && m >= long.MinValue && m <= long.MaxValue && m == decimal.Truncate(m)) return checked((long)m);
            if (value is double d && d >= long.MinValue && d <= long.MaxValue && d == Math.Truncate(d)) return checked((long)d);
            throw new FormatException(name + " must be an integer.");
        }

        internal static decimal Number(Dictionary<string, object> obj, string name, bool required = false)
        {
            if (!obj.TryGetValue(name, out var value) || value == null)
            {
                if (required) throw new FormatException(name + " is required.");
                return 0m;
            }
            if (value is long l) return l;
            if (value is decimal m) return m;
            if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return (decimal)d;
            throw new FormatException(name + " must be a number.");
        }

        internal static bool Boolean(Dictionary<string, object> obj, string name, bool fallback, bool required = false)
        {
            if (!obj.TryGetValue(name, out var value) || value == null)
            {
                if (required) throw new FormatException(name + " is required.");
                return fallback;
            }
            if (value is bool b) return b;
            throw new FormatException(name + " must be a boolean.");
        }

        internal static string Quote(string value)
        {
            var b = new StringBuilder();
            b.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;
            internal Parser(string text) { this.text = text; }
            internal bool End => index == text.Length;
            internal void White() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }

            internal object Value()
            {
                White();
                if (End) throw new FormatException("Unexpected end of JSON.");
                switch (text[index])
                {
                    case '{': return ObjectValue();
                    case '[': return ArrayValue();
                    case '"': return StringValue();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default: return NumberValue();
                }
            }

            private Dictionary<string, object> ObjectValue()
            {
                index++; var result = new Dictionary<string, object>(StringComparer.Ordinal); White();
                if (Take('}')) return result;
                while (true)
                {
                    White(); if (End || text[index] != '"') throw new FormatException("Object key must be a string.");
                    string key = StringValue(); White(); Expect(':'); result.Add(key, Value()); White();
                    if (Take('}')) return result; Expect(',');
                }
            }

            private List<object> ArrayValue()
            {
                index++; var result = new List<object>(); White();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(Value()); White(); if (Take(']')) return result; Expect(',');
                }
            }

            private string StringValue()
            {
                Expect('"'); var b = new StringBuilder();
                while (!End)
                {
                    char c = text[index++];
                    if (c == '"') return b.ToString();
                    if (c < 0x20) throw new FormatException("Control character in JSON string.");
                    if (c != '\\') { b.Append(c); continue; }
                    if (End) throw new FormatException("Unfinished escape.");
                    c = text[index++];
                    switch (c)
                    {
                        case '"': b.Append('"'); break; case '\\': b.Append('\\'); break;
                        case '/': b.Append('/'); break; case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                        case 'u': b.Append((char)Hex4()); break;
                        default: throw new FormatException("Unknown JSON escape.");
                    }
                }
                throw new FormatException("Unfinished JSON string.");
            }

            private object NumberValue()
            {
                int start = index; if (text[index] == '-') index++;
                Digits(); if (!End && text[index] == '.') { index++; Digits(); }
                if (!End && (text[index] == 'e' || text[index] == 'E'))
                { index++; if (!End && (text[index] == '+' || text[index] == '-')) index++; Digits(); }
                string token = text.Substring(start, index - start);
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)) return m;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
                throw new FormatException("Invalid JSON number.");
            }

            private void Digits()
            {
                int start = index; while (!End && text[index] >= '0' && text[index] <= '9') index++;
                if (start == index) throw new FormatException("JSON number needs digits.");
            }
            private int Hex4()
            {
                if (index + 4 > text.Length) throw new FormatException("Short unicode escape.");
                int result = 0; for (int i = 0; i < 4; i++)
                { int d = HexDigit(text[index++]); if (d < 0) throw new FormatException("Invalid unicode escape."); result = result * 16 + d; }
                return result;
            }
            private static int HexDigit(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            private void Literal(string literal) { if (index + literal.Length > text.Length || text.Substring(index, literal.Length) != literal) throw new FormatException("Invalid JSON literal."); index += literal.Length; }
            private bool Take(char c) { if (!End && text[index] == c) { index++; return true; } return false; }
            private void Expect(char c) { White(); if (End || text[index] != c) throw new FormatException("Expected '" + c + "'."); index++; }
        }
    }
}
