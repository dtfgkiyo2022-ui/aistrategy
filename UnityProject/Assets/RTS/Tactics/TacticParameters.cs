using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rts.Tactics
{
    /// <summary>One user-visible tactic knob declared in tactic.json.</summary>
    public sealed class TacticParamDefinition
    {
        public TacticParamDefinition(string name, string label, string type, object defaultValue,
            decimal? min = null, decimal? max = null, decimal? step = null, IReadOnlyList<string> choices = null)
        {
            if (string.IsNullOrEmpty(name) || !name.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9'))
                throw new FormatException("paramsのnameは英数字でなければなりません: " + name);
            if (string.IsNullOrWhiteSpace(label)) throw new FormatException("paramsのlabelは必須です: " + name);
            if (type != "int" && type != "number" && type != "bool" && type != "choice")
                throw new FormatException("paramsのtypeが不正です: " + type);
            Name = name;
            Label = label;
            Type = type;
            Choices = choices == null ? Array.Empty<string>() : choices.ToArray();

            if (type == "choice")
            {
                if (Choices.Count == 0 || Choices.Any(string.IsNullOrEmpty) || Choices.Distinct(StringComparer.Ordinal).Count() != Choices.Count)
                    throw new FormatException("choiceのchoicesは重複のない空でない配列が必要です: " + name);
                if (!(defaultValue is string choice) || Array.IndexOf(Choices.ToArray(), choice) < 0)
                    throw new FormatException("choiceのdefaultがchoicesにありません: " + name);
                DefaultValue = choice;
                return;
            }
            if (type == "bool")
            {
                if (!(defaultValue is bool)) throw new FormatException("boolのdefaultは真偽値でなければなりません: " + name);
                if (min.HasValue || max.HasValue || step.HasValue) throw new FormatException("boolに範囲やstepは指定できません: " + name);
                DefaultValue = defaultValue;
                return;
            }
            if (!min.HasValue || !max.HasValue || !step.HasValue || step.Value <= 0m || min.Value > max.Value)
                throw new FormatException("数値paramsにはmin/max/正のstepが必要です: " + name);
            if (type == "int" && (decimal.Truncate(min.Value) != min.Value || decimal.Truncate(max.Value) != max.Value || decimal.Truncate(step.Value) != step.Value))
                throw new FormatException("intのmin/max/stepは整数でなければなりません: " + name);
            if (!TryNumber(defaultValue, out var numeric) || numeric < min.Value || numeric > max.Value || decimal.Truncate(numeric) != numeric && type == "int")
                throw new FormatException("paramsのdefaultが範囲または型に合いません: " + name);
            if (type == "int" && (numeric < int.MinValue || numeric > int.MaxValue))
                throw new FormatException("intの値がC#整数の範囲外です: " + name);
            if ((numeric - min.Value) % step.Value != 0m)
                throw new FormatException("paramsのdefaultがstepに合いません: " + name);
            Min = min;
            Max = max;
            Step = step;
            DefaultValue = type == "int" ? (object)checked((int)numeric) : numeric;
        }

        public string Name { get; }
        public string Label { get; }
        public string Type { get; }
        public object DefaultValue { get; }
        public object Default => DefaultValue;
        public decimal? Min { get; }
        public decimal? Max { get; }
        public decimal? Step { get; }
        public IReadOnlyList<string> Choices { get; }

        public bool TryNormalize(object value, out object normalized, out string reason)
        {
            normalized = null;
            reason = null;
            if (Type == "bool")
            {
                if (value is bool boolean) { normalized = boolean; return true; }
                reason = "boolはtrueまたはfalseです。";
                return false;
            }
            if (Type == "choice")
            {
                if (value is string choice && Array.IndexOf(Choices.ToArray(), choice) >= 0) { normalized = choice; return true; }
                reason = "choiceの値がchoicesにありません。";
                return false;
            }
            if (!TryNumber(value, out var numeric))
            {
                reason = Type + "は数値でなければなりません。";
                return false;
            }
            if (Type == "int" && decimal.Truncate(numeric) != numeric)
            {
                reason = "intは整数でなければなりません。";
                return false;
            }
            if (numeric < Min.Value || numeric > Max.Value)
            {
                reason = "値が範囲外です。";
                return false;
            }
            if ((numeric - Min.Value) % Step.Value != 0m)
            {
                reason = "値がstepに合いません。";
                return false;
            }
            if (Type == "int" && (numeric < int.MinValue || numeric > int.MaxValue))
            {
                reason = "intの値が範囲外です。";
                return false;
            }
            normalized = Type == "int" ? (object)(int)numeric : numeric;
            return true;
        }

        private static bool TryNumber(object value, out decimal number)
        {
            switch (value)
            {
                case decimal m: number = m; return true;
                case long l: number = l; return true;
                case int i: number = i; return true;
                case short s: number = s; return true;
                case byte b: number = b; return true;
                case double d when !double.IsNaN(d) && !double.IsInfinity(d): number = (decimal)d; return true;
                case float f when !float.IsNaN(f) && !float.IsInfinity(f): number = (decimal)f; return true;
                default: number = 0m; return false;
            }
        }
    }

    public sealed class TacticParamChange
    {
        public long Tick { get; internal set; }
        public string Name { get; internal set; }
        public object From { get; internal set; }
        public object To { get; internal set; }
    }

    public interface ITacticParameterRuntime
    {
        IReadOnlyList<TacticParamDefinition> Parameters { get; }
        void SetParameters(IReadOnlyDictionary<string, object> values);
    }

    /// <summary>Small JSON helper shared by runtimes and the match-pack writer.</summary>
    public static class TacticParameterJson
    {
        public static string Value(object value)
        {
            if (value == null) return "null";
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is string text) return TacticJson.Quote(text);
            if (value is decimal decimalValue) return decimalValue.ToString("0.#############################", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string Object(IReadOnlyDictionary<string, object> values)
        {
            var b = new StringBuilder("{");
            if (values != null)
            {
                bool first = true;
                foreach (var pair in values)
                {
                    if (!first) b.Append(',');
                    first = false;
                    b.Append(TacticJson.Quote(pair.Key)).Append(':').Append(Value(pair.Value));
                }
            }
            return b.Append('}').ToString();
        }

        public static string AddParams(string json, IReadOnlyDictionary<string, object> values)
        {
            string source = (json ?? "{}").TrimEnd();
            if (source.Length == 0 || source[source.Length - 1] != '}') throw new FormatException("JSONオブジェクトが必要です。");
            int end = source.Length - 1;
            string field = "\"params\":" + Object(values);
            int contentEnd = end;
            while (contentEnd > 0 && char.IsWhiteSpace(source[contentEnd - 1])) contentEnd--;
            bool empty = contentEnd > 0 && source[contentEnd - 1] == '{';
            return source.Substring(0, end) + (empty ? "" : ",") + field + source.Substring(end);
        }
    }
}
