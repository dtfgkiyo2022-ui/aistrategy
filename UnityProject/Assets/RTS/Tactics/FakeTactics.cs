using System;
using System.Collections.Generic;
using System.Globalization;
using Rts.Contracts;

namespace Rts.Tactics
{
    public sealed class IdleTactic : ITacticRuntime
    {
        public string Name => "idle";
        public void Start(string setupJson) { }
        public string Tick(string viewJson) => "{\"version\":1,\"commands\":[]}";
    }

    /// <summary>Deterministic T-1 tactic: every own army focuses the opposing core and the economy makes infantry.</summary>
    public sealed class RushTactic : ITacticRuntime
    {
        private ulong sequence = 1;
        public string Name => "rush";
        public void Start(string setupJson) { }

        public string Tick(string viewJson)
        {
            var root = TacticJson.Object(TacticJson.Parse(viewJson), "view");
            uint faction = checked((uint)TacticJson.Integer(root, "factionId", 0, true)); uint enemy = faction == 1 ? 2U : 1U;
            var commands = new List<string>();
            var units = TacticJson.Array(root.TryGetValue("ownArmies", out var rawUnits) ? rawUnits : root.TryGetValue("units", out rawUnits) ? rawUnits : null, "ownArmies");
            foreach (var raw in units)
            {
                var unit = TacticJson.Object(raw, "unit"); uint id = checked((uint)TacticJson.Integer(unit, "id", 0, true));
                commands.Add("{\"type\":\"policy\",\"kind\":\"Focus\",\"target\":{\"kind\":\"Army\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + "},\"goal\":{\"kind\":\"Core\",\"id\":" + enemy.ToString(CultureInfo.InvariantCulture) + "},\"priority\":100,\"allowedLossPermille\":1000,\"reservePermille\":0}");
            }
            if (root.TryGetValue("economy", out var rawEconomy) && rawEconomy != null)
            {
                var economy = TacticJson.Object(rawEconomy, "economy");
                var buildings = TacticJson.Array(economy.TryGetValue("buildings", out var rawBuildings) ? rawBuildings : null, "buildings");
                uint barracks = 0;
                foreach (var raw in buildings)
                {
                    var building = TacticJson.Object(raw, "building"); if (string.Equals(TacticJson.String(building, "kind", true), BuildingKind.Barracks.ToString(), StringComparison.OrdinalIgnoreCase)) { barracks = checked((uint)TacticJson.Integer(building, "id", 0, true)); break; }
                }
                if (barracks == 0) commands.Add(Command("PlaceBuilding", "\"building\":\"Barracks\",\"cell\":0"));
                else commands.Add(Command("Train", "\"producerId\":" + barracks.ToString(CultureInfo.InvariantCulture) + ",\"unit\":\"Infantry\""));
            }
            return "{\"version\":1,\"commands\":[" + string.Join(",", commands.ToArray()) + "]}";
        }

        private string Command(string kind, string fields) => "{\"type\":\"economy\",\"kind\":\"" + kind + "\",\"sequence\":" + (sequence++).ToString(CultureInfo.InvariantCulture) + "," + fields + "}";
    }
}
