using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rts.Contracts;

namespace Rts.Presentation
{
    public sealed class BlueprintDocument
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public string Name = "設計図";
        public int Width = 1;
        public int Height = 1;
        public readonly List<BlueprintBuilding> Buildings = new List<BlueprintBuilding>();
        public readonly List<BlueprintBelt> Belts = new List<BlueprintBelt>();
    }

    public struct BlueprintBuilding
    {
        public BuildingKind Kind;
        public int X;
        public int Z;
        public int Width;
        public int Height;
        public Facing Facing;

        public BlueprintBuilding(BuildingKind kind, int x, int z, int width, int height, Facing facing)
        {
            Kind = kind; X = x; Z = z; Width = width; Height = height; Facing = facing;
        }
    }

    public struct BlueprintBelt
    {
        public int X;
        public int Z;
        public Facing Facing;

        public BlueprintBelt(int x, int z, Facing facing) { X = x; Z = z; Facing = facing; }
    }

    /// <summary>Versioned blueprint data and geometry. File access stays in the Unity presentation layer.</summary>
    public static class BlueprintTools
    {
        public static string Serialize(BlueprintDocument blueprint)
        {
            if (blueprint == null) throw new ArgumentNullException("blueprint");
            var json = new StringBuilder();
            json.Append("{\"version\":").Append(blueprint.Version.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"name\":\"").Append(Escape(blueprint.Name ?? "設計図")).Append("\"");
            json.Append(",\"width\":").Append(blueprint.Width.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"height\":").Append(blueprint.Height.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"buildings\":[");
            for (int i = 0; i < blueprint.Buildings.Count; i++)
            {
                if (i != 0) json.Append(',');
                var b = blueprint.Buildings[i];
                json.Append("{\"kind\":").Append((int)b.Kind).Append(",\"x\":").Append(b.X).Append(",\"z\":").Append(b.Z);
                json.Append(",\"width\":").Append(b.Width).Append(",\"height\":").Append(b.Height).Append(",\"facing\":").Append((int)b.Facing).Append('}');
            }
            json.Append("],\"belts\":[");
            for (int i = 0; i < blueprint.Belts.Count; i++)
            {
                if (i != 0) json.Append(',');
                var b = blueprint.Belts[i];
                json.Append("{\"x\":").Append(b.X).Append(",\"z\":").Append(b.Z).Append(",\"facing\":").Append((int)b.Facing).Append('}');
            }
            return json.Append("]}").ToString();
        }

        public static BlueprintDocument Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("設計図JSONが空です。", "json");
            var result = new BlueprintDocument
            {
                Version = Number(json, "version", -1),
                Name = StringValue(json, "name", "設計図"),
                Width = Number(json, "width", 1),
                Height = Number(json, "height", 1)
            };
            if (result.Version != BlueprintDocument.CurrentVersion) throw new FormatException("未対応の設計図版です。");
            foreach (Match match in Objects(json, "buildings"))
            {
                result.Buildings.Add(new BlueprintBuilding((BuildingKind)Number(match.Value, "kind", 0), Number(match.Value, "x", 0), Number(match.Value, "z", 0),
                    Number(match.Value, "width", 1), Number(match.Value, "height", 1), (Facing)Number(match.Value, "facing", 0)));
            }
            foreach (Match match in Objects(json, "belts"))
                result.Belts.Add(new BlueprintBelt(Number(match.Value, "x", 0), Number(match.Value, "z", 0), (Facing)Number(match.Value, "facing", 0)));
            return result;
        }

        public static BlueprintDocument Rotate(BlueprintDocument source, int quarterTurns)
        {
            if (source == null) throw new ArgumentNullException("source");
            int turns = ((quarterTurns % 4) + 4) % 4;
            var result = Clone(source);
            for (int turn = 0; turn < turns; turn++)
            {
                int oldWidth = result.Width, oldHeight = result.Height;
                for (int i = 0; i < result.Buildings.Count; i++)
                {
                    var b = result.Buildings[i];
                    int x = oldHeight - b.Z - b.Height, z = b.X;
                    result.Buildings[i] = new BlueprintBuilding(b.Kind, x, z, b.Height, b.Width, Turn(b.Facing));
                }
                for (int i = 0; i < result.Belts.Count; i++)
                {
                    var b = result.Belts[i];
                    result.Belts[i] = new BlueprintBelt(oldHeight - 1 - b.Z, b.X, Turn(b.Facing));
                }
                result.Width = oldHeight;
                result.Height = oldWidth;
            }
            return result;
        }

        public static List<List<BlueprintBelt>> SplitBeltRuns(IReadOnlyList<BlueprintBelt> belts, int maxRun)
        {
            if (maxRun <= 0) throw new ArgumentOutOfRangeException("maxRun");
            var result = new List<List<BlueprintBelt>>();
            if (belts == null) return result;
            List<BlueprintBelt> run = null;
            for (int i = 0; i < belts.Count; i++)
            {
                if (run == null || run.Count == maxRun) { run = new List<BlueprintBelt>(); result.Add(run); }
                run.Add(belts[i]);
            }
            return result;
        }

        private static BlueprintDocument Clone(BlueprintDocument source)
        {
            var copy = new BlueprintDocument { Version = source.Version, Name = source.Name, Width = source.Width, Height = source.Height };
            copy.Buildings.AddRange(source.Buildings);
            copy.Belts.AddRange(source.Belts);
            return copy;
        }

        private static Facing Turn(Facing facing) { return (Facing)(((int)facing + 1) % 4); }
        private static int Number(string text, string key, int fallback)
        {
            var match = Regex.Match(text, "\\\"" + key + "\\\"\\s*:\\s*(-?\\d+)");
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static string StringValue(string text, string key, string fallback)
        {
            var match = Regex.Match(text, "\\\"" + key + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
            return match.Success ? Unescape(match.Groups[1].Value) : fallback;
        }

        private static MatchCollection Objects(string text, string key)
        {
            var array = Regex.Match(text, "\\\"" + key + "\\\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            return Regex.Matches(array.Success ? array.Groups[1].Value : "", "\\{[^{}]*\\}");
        }

        private static string Escape(string value) { return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"); }
        private static string Unescape(string value) { return value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\"); }
    }
}
