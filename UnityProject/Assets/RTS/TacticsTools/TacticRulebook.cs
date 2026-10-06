using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Tactics
{
    /// <summary>Generates the AI- and human-readable tactic rulebook from the current code definitions.</summary>
    public static class TacticRulebook
    {
        public const int Version = 1;

        public static (string markdown, string json) Write(ScenarioDefinition scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            var markdown = new StringBuilder();
            markdown.AppendLine("# 戦術ルールブック");
            markdown.AppendLine();
            markdown.AppendLine("- `rulebookVersion`: 1");
            markdown.AppendLine("- `viewVersion`: " + TacticViewWriter.Version);
            markdown.AppendLine("- `commandVersion`: " + TacticViewWriter.Version);
            markdown.AppendLine("- `scenarioId`: `" + scenario.ScenarioId + "`");
            markdown.AppendLine("- `mapSeed`: " + scenario.Seed.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine();
            markdown.AppendLine("## 戦術ファイルの形");
            markdown.AppendLine();
            markdown.AppendLine("`tactic.json` は `name`, `author`, `version`, `language`, `entry`, `apiVersion`, `description` を持ちます。入口は `main.js` です。");
            markdown.AppendLine("任意で `params` に、英数字の `name`、表示用 `label`、`type`（`int` / `number` / `bool` / `choice`）、`default` を並べます。数値は `min`, `max`, `step` も必須で、既定値と変更値は範囲内かつstepに一致しなければなりません。`choice` は `choices` 配列と、その中の `default` が必要です。");
            markdown.AppendLine("試合開始時に `onStart(setup)` を1回、以後20tick（1秒）ごとに `onTick(view)` を呼び、`setup.params` と `view.params` に現在値の辞書を渡します。`{ version: 1, commands: [...] }` を返します。命令を返さない回は `{version:1,commands:[]}` とします。");
            markdown.AppendLine();
            markdown.AppendLine("## 戦術のつまみ `params`");
            markdown.AppendLine();
            markdown.AppendLine("`params` の数値例: `[{\"name\":\"attackThreshold\",\"label\":\"攻めに切り替える兵の数\",\"type\":\"int\",\"default\":30,\"min\":5,\"max\":100,\"step\":5}]`。`tactic-match` では `--west-param name=value` / `--east-param name=value`（複数可）で開始値を指定できます。試し遊びでは自軍は操作、相手は表示だけです。");
            markdown.AppendLine("1回の予算は50ms・100万文・再帰256・メモリ64MBです。失敗が10回続くとその試合では停止します。`console.log` は1回20行、1行200文字までです。`Math.random` は試合の種から決まります。");
            markdown.AppendLine();
            markdown.AppendLine("## 戦況 `view`");
            markdown.AppendLine();
            markdown.AppendLine("座標はメートル単位の小数（小数第3位まで）、`tick` は1秒20tickです。渡されるのはその陣営が観測できる情報だけです。");
            markdown.AppendLine();
            markdown.AppendLine("|項目|内容|");
            markdown.AppendLine("|---|---|");
            foreach (var row in ViewRows()) markdown.AppendLine("|`" + row.Item1 + "`|" + row.Item2 + "|");
            markdown.AppendLine();
            markdown.AppendLine("## 命令");
            markdown.AppendLine();
            markdown.AppendLine("`commands` の各要素は `type` で判別します。未知の命令や不正な値はその要素だけ捨てられ、理由が記録されます。`version` は必須です。");
            markdown.AppendLine();
            markdown.AppendLine("|type|項目・既定値・範囲|");
            markdown.AppendLine("|---|---|");
            foreach (var row in CommandRows()) markdown.AppendLine("|`" + row.Item1 + "`|" + row.Item2 + "|");
            markdown.AppendLine();
            markdown.AppendLine("## 列挙の値");
            markdown.AppendLine();
            foreach (var row in EnumRows())
            {
                markdown.AppendLine("### `" + row.Name + "`");
                markdown.AppendLine();
                markdown.AppendLine(string.Join(", ", row.Values.Select(v => "`" + v + "`")));
                markdown.AppendLine();
            }
            markdown.AppendLine("## シナリオの数値");
            markdown.AppendLine();
            markdown.AppendLine("### 兵種");
            markdown.AppendLine();
            markdown.AppendLine("|UnitKind|HP|攻撃|射程|速さ|費用|");
            markdown.AppendLine("|---:|---:|---:|---:|---:|---|");
            foreach (var p in scenario.UnitParameters.OrderBy(x => (byte)x.Kind))
                markdown.AppendLine("|`" + p.Kind + "`|" + p.Hp + "|" + p.Damage + "|" + Fix(p.Range) + "|" + Fix(p.Speed) + "|" + UnitCost(scenario.Economy, p.Kind) + "|");
            markdown.AppendLine();
            markdown.AppendLine("### 建物と時代");
            markdown.AppendLine();
            markdown.AppendLine("|BuildingKind|費用|建設時間(tick)|");
            markdown.AppendLine("|---|---|---:|");
            foreach (var row in BuildingRows(scenario.Economy)) markdown.AppendLine("|`" + row.Name + "`|" + row.Cost + "|" + row.Work + "|");
            markdown.AppendLine();
            markdown.AppendLine("時代進行: `Age2` は food=" + scenario.Economy.Age2FoodCost + ", wood=" + scenario.Economy.Age2WoodCost + ", ticks=" + scenario.Economy.Age2Ticks + "。`Age3` は food=" + scenario.Economy.Age3FoodCost + ", wood=" + scenario.Economy.Age3WoodCost + ", ticks=" + scenario.Economy.Age3Ticks + "。");
            markdown.AppendLine();
            markdown.AppendLine("## 見本 `TacticSamples/defend-then-push/main.js`");
            markdown.AppendLine();
            markdown.AppendLine("```javascript");
            markdown.AppendLine(ReadSample());
            markdown.AppendLine("```");

            string json = BuildJson(scenario);
            return (markdown.ToString(), json);
        }

        private static IEnumerable<(string, string)> ViewRows()
        {
            return new[] {
                ("version", "戦況の版番号"), ("tick", "現在の試合 tick"), ("factionId", "自陣営のID"),
                ("ownArmies[]", "自軍部隊の id/kind/count/position/homeObjective"), ("ownArmies[].position", "x/z。メートル"), ("ownArmies[].homeObjective", "kind/id/point"),
                ("visibleEnemies[]", "現在見えている敵の id/kind/position"), ("visibleEnemies[].position", "x/z。メートル"),
                ("contacts[]", "id/position/lastSeenTick/min/max/visible/uncertain/strengthUnknown/absent/covered"), ("contacts[].covered", "この接触に含まれる観測済み接触ID"),
                ("objectives[]", "kind/id/position/ownerKnown/ownerFactionId/hpKnown/hp/lastSeenTick/capturingFactionId/captureTicks/captureDurationTicks"),
                ("economy", "food/wood/ore/metal/stone/gold/gems/population/populationCap/civilisation/age/agesEnabled/advancingTo/advanceRemainingTicks/nextAgeCost{food,wood,gold}/canAdvanceNow/auto/policy"),
                ("economy.villagers[]", "id/position/activity/hp"), ("economy.buildings[]", "id/kind/position/complete/queued/researching"), ("economy.resources[]", "id/kind/position/remaining"),
                ("regions[]", "id/centerKind/centerId/center/control/policy/economyPolicy"), ("params", "tactic.jsonで宣言したつまみの現在値")
            };
        }

        private static IEnumerable<(string, string)> CommandRows()
        {
            return new[] {
                ("policy", "`kind`, `target`（省略不可）, `goal`（Focus/Defend/Scout は必須）, `priority`（既定100、0..100）, `allowedLossPermille`（既定1000、0..1000）, `reservePermille`（既定0、0..1000）, `validUntilTick`（既定 observed tick+20）, `maxObservationAgeTicks`（既定20）, `expire`。自陣の観測目標以外は捨てられます。"),
                ("economy", "`kind`, `sequence`（既定は配列位置+1）と種類ごとの項目。現在受け付けるのは PlaceBuilding, Train, AssignVillagers, Research, AdvanceAge, SetEconomyPolicy, ReturnEconomyToAuto。建物・兵種・研究・文明・対象ID・cellの所有、範囲、費用はシミュレーションで検査され、条件不成立なら記録されず捨てられます。"),
                ("global", "`policy`: `none` / `maintain` / `concentrate`。同じ返答で1つまで。"),
                ("doctrine", "`preset`: `none` / `maintain` / `concentrate`。`global` の別名。")
            };
        }

        private sealed class EnumRow
        {
            public string Name;
            public string[] Values;
        }

        private static IEnumerable<EnumRow> EnumRows()
        {
            var types = new[] { typeof(UnitKind), typeof(BuildingKind), typeof(ResourceKind), typeof(CivKind), typeof(TechKind), typeof(PolicyKind), typeof(GoalKind), typeof(ScopeKind), typeof(EndKind), typeof(EconomyPolicy), typeof(EconomyTargetKind), typeof(EconomyCommandKind), typeof(RegionControl), typeof(RegionCenterKind), typeof(ExpireFlags), typeof(Facing), typeof(RaidTargetKind), typeof(CaravanStopReason) };
            foreach (var type in types)
                yield return new EnumRow { Name = type.Name, Values = Enum.GetValues(type).Cast<Enum>().Select(x => x.ToString()).ToArray() };
            yield return new EnumRow { Name = "ExtendedTechKind", Values = new[] { "Bridgeworks", "SiegeDeployment", "FishingNet", "DriedFish", "DeepShaft", "MountainFort", "Sermon", "MartyrBlessing", "GateDefence", "GateNetwork", "MarketFestivity", "CitizenMilitia", "Pilgrimage", "HolyRelic" } };
        }

        private sealed class BuildingRow
        {
            public string Name;
            public string Cost;
            public int Work;
        }

        private static IEnumerable<BuildingRow> BuildingRows(EconomyRules e)
        {
            var rows = new Dictionary<BuildingKind, BuildingRow>();
            Add(rows, BuildingKind.Barracks, e.BarracksWoodCost, 0, e.BarracksWork);
            Add(rows, BuildingKind.Mine, e.MineWoodCost, 0, e.MineWork); Add(rows, BuildingKind.Smelter, e.SmelterWoodCost, 0, e.SmelterWork);
            Add(rows, BuildingKind.Farm, e.FarmWoodCost, 0, e.FarmWork); Add(rows, BuildingKind.House, e.HouseWoodCost, 0, e.HouseWork);
            Add(rows, BuildingKind.DropSite, e.DropSiteWoodCost, 0, e.DropSiteWork); Add(rows, BuildingKind.Wall, 0, e.WallStoneCost, 0);
            Add(rows, BuildingKind.Tower, e.TowerWoodCost, e.TowerStoneCost, e.TowerWork); Add(rows, BuildingKind.Blacksmith, e.BlacksmithWoodCost, 0, e.BlacksmithWork);
            Add(rows, BuildingKind.Market, e.MarketWoodCost, 0, e.MarketWork); Add(rows, BuildingKind.SiegeWorkshop, e.WorkshopWoodCost, 0, e.WorkshopWork);
            Add(rows, BuildingKind.ArcheryRange, e.RangeWoodCost, 0, e.RangeWork); Add(rows, BuildingKind.Stable, e.StableWoodCost, 0, e.StableWork);
            Add(rows, BuildingKind.Castle, e.CastleWoodCost, e.CastleStoneCost, e.CastleWork); Add(rows, BuildingKind.CharcoalKiln, e.CharcoalKilnWoodCost, 0, e.CharcoalKilnWork);
            Add(rows, BuildingKind.Steelworks, e.SteelworksWoodCost, 0, e.SteelworksWork); Add(rows, BuildingKind.LumberCamp, e.LumberCampWoodCost, 0, e.LumberCampWork);
            Add(rows, BuildingKind.Fletcher, e.FletcherWoodCost, 0, e.FletcherWork); Add(rows, BuildingKind.Quarry, e.QuarryWoodCost, 0, e.QuarryWork);
            Add(rows, BuildingKind.Caravanserai, e.CaravanseraiWoodCost, 0, e.CaravanseraiWork); Add(rows, BuildingKind.EngineerCamp, e.EngineerCampWoodCost, 0, e.EngineerCampWork);
            Add(rows, BuildingKind.Bridge, e.BridgeWoodCost, 0, e.BridgeWork); Add(rows, BuildingKind.Academy, e.AcademyWoodCost, 0, e.AcademyWork);
            Add(rows, BuildingKind.Monastery, e.MonasteryWoodCost, 0, e.MonasteryWork); Add(rows, BuildingKind.Harbor, e.HarborWoodCost, 0, e.HarborWork);
            Add(rows, BuildingKind.MineShaft, e.MountainWoodCost, 0, e.MountainWork); Add(rows, BuildingKind.Tollgate, e.TollgateWoodCost, e.TollgateStoneCost, e.TollgateWork);
            Add(rows, BuildingKind.GrandHouse, e.GrandHouseWoodCost, 0, e.GrandHouseWork); Add(rows, BuildingKind.Shrine, e.ShrineWoodCost, e.ShrineStoneCost, e.ShrineWork);
            Add(rows, BuildingKind.Town, e.TownWoodCost, e.TownStoneCost, e.TownWork);
            return Enum.GetValues(typeof(BuildingKind)).Cast<BuildingKind>().Select(kind => rows[kind]);
        }

        private static void Add(Dictionary<BuildingKind, BuildingRow> rows, BuildingKind kind, int wood, int stone, int work)
        { rows[kind] = new BuildingRow { Name = kind.ToString(), Cost = "wood=" + wood + ", stone=" + stone, Work = work }; }

        private static string Fix(Fix64 value) => ((decimal)value.Raw / 65536m).ToString("0.###", CultureInfo.InvariantCulture);

        private static string ReadSample()
        {
            string path = FindUpward(Path.Combine("TacticSamples", "defend-then-push", "main.js"));
            return path == null ? "function onTick(view) { return { version: 1, commands: [] }; }" : File.ReadAllText(path, new UTF8Encoding(false, true)).TrimEnd();
        }

        private static string FindUpward(string relative)
        {
            for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, relative);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static string BuildJson(ScenarioDefinition scenario)
        {
            var b = new StringBuilder(); b.Append('{');
            Property(b, "rulebookVersion", "1"); Property(b, "viewVersion", TacticViewWriter.Version.ToString(CultureInfo.InvariantCulture));
            Property(b, "commandVersion", TacticViewWriter.Version.ToString(CultureInfo.InvariantCulture)); Property(b, "scenarioId", Quote(scenario.ScenarioId));
            Property(b, "mapSeed", scenario.Seed.ToString(CultureInfo.InvariantCulture)); Property(b, "tickRateHz", scenario.TickRateHz.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"budgets\":{\"callTimeoutMs\":50,\"statementLimit\":1000000,\"recursionLimit\":256,\"memoryBytes\":67108864,\"failureLimit\":10,\"decisionIntervalTicks\":20,\"consoleLineLimit\":20,\"consoleCharacterLimit\":200}");
            b.Append(",\"viewFields\":["); var views = ViewRows().ToArray(); for (int i = 0; i < views.Length; i++) { if (i != 0) b.Append(','); b.Append("{\"name\":").Append(Quote(views[i].Item1)).Append(",\"description\":").Append(Quote(views[i].Item2)).Append('}'); } b.Append(']');
            b.Append(",\"commands\":["); var commands = CommandRows().ToArray(); for (int i = 0; i < commands.Length; i++) { if (i != 0) b.Append(','); b.Append("{\"type\":").Append(Quote(commands[i].Item1)).Append(",\"description\":").Append(Quote(commands[i].Item2)).Append('}'); } b.Append(']');
            b.Append(",\"enums\":{"); var enums = EnumRows().ToArray(); for (int i = 0; i < enums.Length; i++) { if (i != 0) b.Append(','); b.Append(Quote(enums[i].Name)).Append(": ["); for (int j = 0; j < enums[i].Values.Length; j++) { if (j != 0) b.Append(','); b.Append(Quote(enums[i].Values[j])); } b.Append(']'); } b.Append('}');
            b.Append(",\"units\":["); for (int i = 0; i < scenario.UnitParameters.Length; i++) { if (i != 0) b.Append(','); var p = scenario.UnitParameters[i]; b.Append("{\"kind\":").Append(Quote(p.Kind.ToString())).Append(",\"hp\":").Append(p.Hp).Append(",\"damage\":").Append(p.Damage).Append(",\"range\":").Append(Fix(p.Range)).Append(",\"speed\":").Append(Fix(p.Speed)).Append(",\"cost\":").Append(Quote(UnitCost(scenario.Economy, p.Kind))).Append('}'); } b.Append(']');
            b.Append(",\"buildings\":["); var buildings = BuildingRows(scenario.Economy).ToArray(); for (int i = 0; i < buildings.Length; i++) { if (i != 0) b.Append(','); b.Append("{\"kind\":").Append(Quote(buildings[i].Name)).Append(",\"cost\":").Append(Quote(buildings[i].Cost)).Append(",\"workTicks\":").Append(buildings[i].Work).Append('}'); } b.Append(']');
            b.Append(",\"ages\":{\"Age2\":{\"food\":").Append(scenario.Economy.Age2FoodCost).Append(",\"wood\":").Append(scenario.Economy.Age2WoodCost).Append(",\"ticks\":").Append(scenario.Economy.Age2Ticks).Append("},\"Age3\":{\"food\":").Append(scenario.Economy.Age3FoodCost).Append(",\"wood\":").Append(scenario.Economy.Age3WoodCost).Append(",\"ticks\":").Append(scenario.Economy.Age3Ticks).Append("}}");
            b.Append(",\"sample\":").Append(Quote(ReadSample()));
            b.Append('}'); return b.ToString();
        }

        private static string UnitCost(EconomyRules e, UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Infantry: return "food=" + e.InfantryFoodCost + ", wood=" + e.InfantryWoodCost + ", metal=" + e.InfantryMetalCost + ", ticks=" + e.InfantryTrainTicks;
                case UnitKind.Scout: return "food=" + e.ScoutFoodCost + ", wood=" + e.ScoutWoodCost + ", ticks=" + e.ScoutTrainTicks;
                case UnitKind.Villager: return "food=" + e.VillagerFoodCost + ", ticks=" + e.VillagerTrainTicks;
                case UnitKind.Archer: return "food=" + e.ArcherFood + ", wood=" + e.ArcherWood + ", ticks=" + e.ArcherTicks;
                case UnitKind.Cavalry: return "food=" + e.CavalryFood + ", wood=" + e.CavalryWood + ", metal=" + e.CavalryMetal + ", ticks=" + e.CavalryTicks;
                case UnitKind.LightCavalry: return "food=" + e.LightCavalryFood + ", wood=" + e.LightCavalryWood + ", ticks=" + e.LightCavalryTicks;
                case UnitKind.HeavyInfantry: return "food=" + e.HeavyInfantryFoodCost + ", wood=" + e.HeavyInfantryWoodCost + ", steel=" + e.HeavyInfantrySteelCost + ", ticks=" + e.HeavyInfantryTrainTicks;
                case UnitKind.SkirmishArcher: return "food=" + e.SkirmishArcherFoodCost + ", bowGear=" + e.SkirmishArcherBowGearCost + ", ticks=" + e.SkirmishArcherTrainTicks;
                case UnitKind.Ram: return "food=" + e.RamFood + ", wood=" + e.RamWood + ", ticks=" + e.RamTicks;
                case UnitKind.Mercenary: return "gems=" + e.MercenaryGems + ", ticks=" + e.MercenaryTicks;
                case UnitKind.Monk: return "food=" + e.MonkFoodCost + ", gold=" + e.MonkGoldCost + ", ticks=" + e.MonkTrainTicks;
                default: return "—";
            }
        }

        private static void Property(StringBuilder b, string name, string value)
        { if (b.Length > 1) b.Append(','); b.Append(Quote(name)).Append(':').Append(value.StartsWith("\"") ? value : value); }
        private static string Quote(string value) => TacticJson.Quote(value ?? "");
    }
}
