using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class TackTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);
            internal void Step(int count) { for (int i = 0; i < count; i++) Gateway.Step(); }
        }

        private static ScenarioDefinition Scenario(bool tack)
        {
            var s = MapGenerator.GenerateTerrain(9137UL, false, true);
            s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Cavalry = true; s.Economy.Tack = tack; s.Economy.StartFood = 1000; s.Economy.StartWood = 5000;
            s.Economy.SaddleryWoodCost = 1; s.Economy.SaddleryWork = 1; s.Economy.SaddleryHp = 10;
            s.Economy.TackTicks = 100; s.Economy.TackFoodInput = 2; s.Economy.TackWoodInput = 2;
            s.Economy.HeavyCavalryTackCost = 2; s.Economy.HeavyCavalryFoodCost = 70; s.Economy.HeavyCavalryTrainTicks = 5;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.StableWoodCost = 1; s.Economy.StableWork = 1; s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1;
            s.Economy.LightCavalryFood = 0; s.Economy.LightCavalryWood = 0; s.Economy.LightCavalryTicks = 5;
            s.Economy.CavalryFood = 0; s.Economy.CavalryWood = 0; s.Economy.CavalryMetal = 0; s.Economy.CavalryTicks = 5;
            s.Cores[0].Hp = 1000000; s.Cores[1].Hp = 1000000;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static bool PlaceSaddlery(Fixture f)
        {
            int before = int.Parse(Fields(f.Simulation)["Buildings.Count"], CultureInfo.InvariantCulture);
            int cells = f.Scenario.Map.WidthCells * f.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Saddlery, cell, Facing.North));
                f.Step(1);
                if (int.Parse(Fields(f.Simulation)["Buildings.Count"], CultureInfo.InvariantCulture) > before) return true;
            }
            return false;
        }

        private static uint Place(Fixture f, BuildingKind kind)
        {
            var before = Fields(f.Simulation);
            int count = int.Parse(before["Buildings.Count"], CultureInfo.InvariantCulture);
            int cells = f.Scenario.Map.WidthCells * f.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                f.Send(EconomyCommand.Place(1, ++f.Sequence, kind, cell, Facing.North));
                f.Step(1);
                var fields = Fields(f.Simulation);
                if (int.Parse(fields["Buildings.Count"], CultureInfo.InvariantCulture) == count + 1
                    && fields["Buildings[" + (count + 1) + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)(count + 1);
            }
            Assert.Fail("合法な建設地点が見つからない: " + kind);
            return 0;
        }

        private static void Build(Fixture f, uint buildingId)
        {
            f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, buildingId));
            for (int i = 0; i < 1500 && Fields(f.Simulation)["Buildings[" + buildingId + "].Complete"] != "1"; i++) f.Step(1);
            Assert.That(Fields(f.Simulation)["Buildings[" + buildingId + "].Complete"], Is.EqualTo("1"));
        }

        private static void ReachCavalry(Fixture f)
        {
            f.Send(EconomyCommand.Advance(1, ++f.Sequence, CivKind.Cavalry));
            f.Step(3);
            Assert.That(Fields(f.Simulation)["Economy[1].Civ"], Is.EqualTo(((byte)CivKind.Cavalry).ToString(CultureInfo.InvariantCulture)));
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetEconomy(Battle simulation, uint faction, string fieldName, int value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetBuilding(Battle simulation, uint buildingId, string fieldName, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var buildings = (Array)field.GetValue(world);
            var building = buildings.GetValue((int)buildingId - 1);
            building.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, value);
            buildings.SetValue(building, (int)buildingId - 1);
        }

        private static void SetVillager(Battle simulation, uint villagerId, string fieldName, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Villagers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var villagers = (Array)field.GetValue(world);
            var villager = villagers.GetValue((int)villagerId - 1);
            var target = villager.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (target.FieldType.IsEnum && value is int) value = Enum.ToObject(target.FieldType, value);
            target.SetValue(villager, value);
            villagers.SetValue(villager, (int)villagerId - 1);
        }

        private static SimPoint BuildingWorkPosition(Battle simulation, uint buildingId)
        {
            var world = World(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            var building = buildings.GetValue((int)buildingId - 1);
            int workCell = (int)building.GetType().GetField("WorkCell", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(building);
            var map = world.GetType().GetField("Map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            return (SimPoint)map.GetType().GetMethod("Center", new[] { typeof(int) }).Invoke(map, new object[] { workCell });
        }

        private static void SetSoldier(Battle simulation, int index, string fieldName, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Soldiers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var soldiers = (Array)field.GetValue(world);
            var soldier = soldiers.GetValue(index);
            soldier.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(soldier, value);
            soldiers.SetValue(soldier, index);
        }

        private static void FillDuel(Battle simulation, ScenarioDefinition scenario, UnitKind first, UnitKind second)
        {
            var fields = Fields(simulation);
            int firstIndex = -1, secondIndex = -1;
            for (int i = 1; i <= 256; i++)
            {
                if (!fields.TryGetValue("Soldiers[" + i + "].FactionId", out var faction)) continue;
                if (!fields.TryGetValue("Soldiers[" + i + "].Class", out var unitClass)) continue;
                if (faction == "1" && unitClass != ((byte)UnitKind.Villager).ToString(CultureInfo.InvariantCulture) && firstIndex < 0) firstIndex = i - 1;
                if (faction == "2" && unitClass != ((byte)UnitKind.Villager).ToString(CultureInfo.InvariantCulture) && secondIndex < 0) secondIndex = i - 1;
            }
            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(secondIndex, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < 256; i++)
                if (fields.ContainsKey("Soldiers[" + (i + 1) + "].FactionId"))
                if (i != firstIndex && i != secondIndex) SetSoldier(simulation, i, "Alive", false);

            var view = simulation.Capture(1);
            var firstPosition = view.Units.First(u => u.IsOwn && u.Kind != UnitKind.Villager).Position;
            UnitParameters parametersFirst = ParametersFor(scenario, first);
            UnitParameters parametersSecond = ParametersFor(scenario, second);
            SetSoldier(simulation, firstIndex, "Alive", true);
            SetSoldier(simulation, firstIndex, "Class", first);
            SetSoldier(simulation, firstIndex, "Parameters", parametersFirst);
            SetSoldier(simulation, firstIndex, "Hp", parametersFirst.Hp);
            SetSoldier(simulation, firstIndex, "Position", firstPosition);
            SetSoldier(simulation, firstIndex, "MoveGoal", firstPosition);
            SetSoldier(simulation, firstIndex, "TargetKind", (byte)1);
            SetSoldier(simulation, firstIndex, "TargetId", (uint)(secondIndex + 1));
            SetSoldier(simulation, firstIndex, "NextAttackTick", 0L);
            SetSoldier(simulation, secondIndex, "Alive", true);
            SetSoldier(simulation, secondIndex, "Class", second);
            SetSoldier(simulation, secondIndex, "Parameters", parametersSecond);
            SetSoldier(simulation, secondIndex, "Hp", parametersSecond.Hp);
            SetSoldier(simulation, secondIndex, "Position", firstPosition);
            SetSoldier(simulation, secondIndex, "MoveGoal", firstPosition);
            SetSoldier(simulation, secondIndex, "TargetKind", (byte)1);
            SetSoldier(simulation, secondIndex, "TargetId", (uint)(firstIndex + 1));
            SetSoldier(simulation, secondIndex, "NextAttackTick", 0L);
        }

        private static UnitParameters ParametersFor(ScenarioDefinition scenario, UnitKind kind)
        {
            var e = scenario.Economy;
            if (kind == UnitKind.HeavyCavalry) return new UnitParameters { Kind = kind, Hp = e.HeavyCavalryHp, Damage = e.HeavyCavalryDamage, AttackIntervalTicks = e.HeavyCavalryAttackIntervalTicks, Range = e.HeavyCavalryRange, Speed = e.HeavyCavalrySpeed, Vision = e.HeavyCavalryVision };
            if (kind == UnitKind.LightCavalry) return new UnitParameters { Kind = kind, Hp = e.LightCavalryHp, Damage = e.LightCavalryDamage, AttackIntervalTicks = e.LightCavalryInterval, Range = e.LightCavalryRange, Speed = e.LightCavalrySpeed, Vision = e.LightCavalryVision };
            if (kind == UnitKind.Cavalry) return new UnitParameters { Kind = kind, Hp = e.CavalryHp, Damage = e.CavalryDamage, AttackIntervalTicks = e.CavalryInterval, Range = Fix64.FromInt(2), Speed = Fix64.FromInt(4), Vision = Fix64.FromInt(24) };
            return scenario.UnitParameters.First(p => p.Kind == kind);
        }

        [Test]
        public void TackExtensionRoundTripsIdTwentyTwoAndChangedValues()
        {
            var s = Scenario(true);
            s.Economy.SaddleryWoodCost = 97; s.Economy.TackTicks = 111; s.Economy.HeavyCavalrySpeed = Fix64.FromInt(5);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(s));
            Assert.That(decoded.Economy.Tack, Is.True);
            Assert.That(decoded.Economy.SaddleryWoodCost, Is.EqualTo(97));
            Assert.That(decoded.Economy.TackTicks, Is.EqualTo(111));
            Assert.That(decoded.Economy.HeavyCavalrySpeed, Is.EqualTo(Fix64.FromInt(5)));
        }

        [Test]
        public void TackOffRejectsSaddleryAndTackLineWithoutCreatingStock()
        {
            var f = new Fixture(Scenario(false));
            f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Saddlery, 0, Facing.North));
            f.Send(EconomyCommand.RequestLineAt(1, ++f.Sequence, ProcessingLineKind.Tack, 0));
            f.Step(1);
            var fields = Fields(f.Simulation);
            Assert.That(fields.ContainsKey("Economy[1].Tack"), Is.False);
            Assert.That(fields["Buildings.Count"], Is.EqualTo("0"));
        }

        [Test]
        public void TackOnAllowsCavalryCivToUseSaddleryButNotOtherCiv()
        {
            var cavalry = new Fixture(Scenario(true));
            cavalry.Send(EconomyCommand.Advance(1, ++cavalry.Sequence, CivKind.Cavalry));
            cavalry.Step(2);
            Assert.That(PlaceSaddlery(cavalry), Is.True);

            var other = new Fixture(Scenario(true));
            other.Send(EconomyCommand.Advance(1, ++other.Sequence, CivKind.Agrarian));
            other.Step(2);
            other.Send(EconomyCommand.Place(1, ++other.Sequence, BuildingKind.Saddlery, 100, Facing.North));
            other.Step(1);
            Assert.That(Fields(other.Simulation)["Buildings.Count"], Is.EqualTo("0"));
        }

        [Test]
        public void SaddleryUsesTwoFoodAndTwoWoodInOneHundredTicksStopsAndReturnsTackToCore()
        {
            var f = new Fixture(Scenario(true));
            ReachCavalry(f);
            uint saddlery = Place(f, BuildingKind.Saddlery);
            Build(f, saddlery);
            SetEconomy(f.Simulation, 1, "Food", 0);
            SetEconomy(f.Simulation, 1, "Wood", 0);
            SetBuilding(f.Simulation, saddlery, "Input", 2);
            SetBuilding(f.Simulation, saddlery, "InputSecondary", 2);
            SetVillager(f.Simulation, 1, "HaulFrom", saddlery);
            SetVillager(f.Simulation, 1, "HaulTo", 0U);
            SetVillager(f.Simulation, 1, "Task", 6); // VillagerTask.ToPickup
            SetVillager(f.Simulation, 1, "Position", BuildingWorkPosition(f.Simulation, saddlery));
            f.Step(99);
            Assert.That(Fields(f.Simulation)["Economy[1].TackProduced"], Is.EqualTo("0"));
            f.Step(1);
            Assert.That(Fields(f.Simulation)["Economy[1].TackProduced"], Is.EqualTo("1"));
            SetVillager(f.Simulation, 1, "HaulFrom", saddlery);
            SetVillager(f.Simulation, 1, "HaulTo", 0U);
            SetVillager(f.Simulation, 1, "Task", 6);
            SetVillager(f.Simulation, 1, "Position", BuildingWorkPosition(f.Simulation, saddlery));
            f.Step(500);
            var hauled = Fields(f.Simulation);
            Assert.That(hauled["Economy[1].Tack"], Is.EqualTo("1"), "完成品が村人の運搬でコアの蓄えに入る: " + string.Join(", ", hauled.Where(p => p.Key.StartsWith("Villagers[1].", StringComparison.Ordinal) || p.Key.StartsWith("Buildings[" + saddlery + "].", StringComparison.Ordinal)).Select(p => p.Key + "=" + p.Value)));

            SetBuilding(f.Simulation, saddlery, "Input", 2);
            SetBuilding(f.Simulation, saddlery, "InputSecondary", 1);
            f.Step(120);
            Assert.That(Fields(f.Simulation)["Economy[1].TackProduced"], Is.EqualTo("1"));
            SetBuilding(f.Simulation, saddlery, "InputSecondary", 2);
            f.Step(100);
            Assert.That(Fields(f.Simulation)["Economy[1].TackProduced"], Is.EqualTo("2"));
        }

        [Test]
        public void HeavyCavalryPaysTackAndFoodWithoutMetalAndCancelRefundsThem()
        {
            var f = new Fixture(Scenario(true));
            ReachCavalry(f);
            uint stable = Place(f, BuildingKind.Stable);
            Build(f, stable);
            SetEconomy(f.Simulation, 1, "Food", 70);
            SetEconomy(f.Simulation, 1, "Metal", 10);
            SetEconomy(f.Simulation, 1, "Tack", 2);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, stable, UnitKind.HeavyCavalry));
            f.Step(1);
            var queued = Fields(f.Simulation);
            Assert.That(queued["Buildings[" + stable + "].QueueKinds[0]"], Is.EqualTo(((byte)UnitKind.HeavyCavalry).ToString(CultureInfo.InvariantCulture)));
            Assert.That(queued["Economy[1].Food"], Is.EqualTo("0"));
            Assert.That(queued["Economy[1].Tack"], Is.EqualTo("0"));
            Assert.That(queued["Economy[1].Metal"], Is.EqualTo("10"));
            f.Send(EconomyCommand.CancelTrain(1, ++f.Sequence, stable));
            f.Step(1);
            var cancelled = Fields(f.Simulation);
            Assert.That(cancelled["Economy[1].Food"], Is.EqualTo("70"));
            Assert.That(cancelled["Economy[1].Tack"], Is.EqualTo("2"));
            Assert.That(cancelled["Economy[1].Metal"], Is.EqualTo("10"));
        }

        [Test]
        public void HeavyCavalryNeedsTwoTackButLightCavalryStillTrainsWithOneOrLess()
        {
            var f = new Fixture(Scenario(true));
            ReachCavalry(f);
            uint stable = Place(f, BuildingKind.Stable);
            Build(f, stable);
            SetEconomy(f.Simulation, 1, "Food", 0);
            SetEconomy(f.Simulation, 1, "Tack", 1);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, stable, UnitKind.HeavyCavalry));
            f.Step(1);
            Assert.That(Fields(f.Simulation)["Buildings[" + stable + "].Queued"], Is.EqualTo("0"));
            f.Send(EconomyCommand.Train(1, ++f.Sequence, stable, UnitKind.LightCavalry));
            f.Step(1);
            Assert.That(Fields(f.Simulation)["Buildings[" + stable + "].QueueKinds[0]"], Is.EqualTo(((byte)UnitKind.LightCavalry).ToString(CultureInfo.InvariantCulture)));

            var other = new Fixture(Scenario(true));
            other.Send(EconomyCommand.Advance(1, ++other.Sequence, CivKind.Agrarian));
            other.Step(3);
            other.Send(EconomyCommand.Advance(1, ++other.Sequence, CivKind.Agrarian));
            other.Step(3);
            uint otherStable = Place(other, BuildingKind.Stable);
            Build(other, otherStable);
            SetEconomy(other.Simulation, 1, "Food", 70);
            SetEconomy(other.Simulation, 1, "Tack", 2);
            other.Send(EconomyCommand.Train(1, ++other.Sequence, otherStable, UnitKind.HeavyCavalry));
            other.Step(1);
            Assert.That(Fields(other.Simulation)["Buildings[" + otherStable + "].Queued"], Is.EqualTo("0"));
        }

        [Test]
        public void HeavyCavalryHasTheChosenNumbersAndWinsLightButLosesToCavalryByDamagePerInterval()
        {
            var f = new Fixture(Scenario(true));
            ReachCavalry(f);
            uint stable = Place(f, BuildingKind.Stable);
            Build(f, stable);
            SetEconomy(f.Simulation, 1, "Food", 70);
            SetEconomy(f.Simulation, 1, "Tack", 2);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, stable, UnitKind.HeavyCavalry));
            f.Step(1);
            int next = int.Parse(Fields(f.Simulation)["NextSoldierId"], CultureInfo.InvariantCulture);
            f.Step(5);
            var fields = Fields(f.Simulation);
            int heavy = Enumerable.Range(next, 40).First(id => fields.ContainsKey("Soldiers[" + id + "].Class")
                && fields["Soldiers[" + id + "].Class"] == ((byte)UnitKind.HeavyCavalry).ToString(CultureInfo.InvariantCulture));
            Assert.That(fields["Soldiers[" + heavy + "].Hp"], Is.EqualTo("128"));
            Assert.That(fields["Soldiers[" + heavy + "].Parameters.Damage"], Is.EqualTo("10"));
            Assert.That(fields["Soldiers[" + heavy + "].Parameters.AttackIntervalTicks"], Is.EqualTo("20"));
            Assert.That(fields["Soldiers[" + heavy + "].Parameters.Range.Raw"], Is.EqualTo(Fix64.FromInt(2).Raw.ToString(CultureInfo.InvariantCulture)));
            Assert.That(fields["Soldiers[" + heavy + "].Parameters.Speed.Raw"], Is.EqualTo(Fix64.FromInt(4).Raw.ToString(CultureInfo.InvariantCulture)));
            Assert.That(fields["Soldiers[" + heavy + "].Parameters.Vision.Raw"], Is.EqualTo(Fix64.FromInt(24).Raw.ToString(CultureInfo.InvariantCulture)));

            var lightDuel = new Fixture(Scenario(true));
            FillDuel(lightDuel.Simulation, lightDuel.Scenario, UnitKind.HeavyCavalry, UnitKind.LightCavalry);
            lightDuel.Step(400);
            fields = Fields(lightDuel.Simulation);
            Assert.That(fields.Any(p => p.Key.EndsWith(".Class", StringComparison.Ordinal) && p.Value == ((byte)UnitKind.HeavyCavalry).ToString(CultureInfo.InvariantCulture)
                && fields.TryGetValue(p.Key.Replace(".Class", ".Alive"), out var alive) && alive == "1"), Is.True);

            var duel = new Fixture(Scenario(true));
            FillDuel(duel.Simulation, duel.Scenario, UnitKind.HeavyCavalry, UnitKind.Cavalry);
            duel.Step(400);
            var duelFields = Fields(duel.Simulation);
            Assert.That(duelFields.Any(p => p.Key.EndsWith(".Class", StringComparison.Ordinal) && p.Value == ((byte)UnitKind.Cavalry).ToString(CultureInfo.InvariantCulture)
                && duelFields.TryGetValue(p.Key.Replace(".Class", ".Alive"), out var alive) && alive == "1"), Is.True);
        }

        [Test]
        public void TackFlagDoesNotChangeLightCavalryRaidsAndHeavyCavalryIsNotARaider()
        {
            Assert.That(OffenseDecision.IsMobileArmyKind(UnitKind.LightCavalry), Is.True);
            Assert.That(OffenseDecision.IsMobileArmyKind(UnitKind.HeavyCavalry), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.HeavyCavalry, UnitKind.Archer), Is.True);
            Assert.That(CombatMath.Counters(UnitKind.HeavyCavalry, UnitKind.Infantry), Is.False);
            var on = new Fixture(Scenario(true));
            var off = new Fixture(Scenario(false));
            on.Step(200); off.Step(200);
            Assert.That(Fields(on.Simulation).ContainsKey("Economy[1].TackTrainedHeavyCavalry"), Is.True);
            Assert.That(Fields(off.Simulation).ContainsKey("Economy[1].Tack"), Is.False);
        }

        [Test, Explicit("騎馬の文明が工房・馬具・重騎兵まで実際に回す長時間テスト")]
        public void CavalryAutomaticEconomyBuildsSaddleryAndTrainsHeavyCavalry()
        {
            var scenario = LiveGameRules.Create(1UL, false, true, false, false);
            scenario.Economy.StartFood = 1000;
            scenario.Economy.StartWood = 5000;
            scenario.Cores[0].Hp = 1000000;
            scenario.Cores[1].Hp = 1000000;
            var simulation = new Battle(scenario);
            MatchPackSnapshot snapshot = null;
            for (long tick = 1; tick <= 30000 && !simulation.Capture(1).Result.HasEnded; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 100 != 0) continue;
                snapshot = simulation.CaptureMatchPackSnapshot();
                var progress = snapshot.Factions.FirstOrDefault(f => f.Civ == CivKind.Cavalry.ToString());
                if (progress != null && progress.TackProduced > 0 && progress.TackTrainedHeavyCavalry > 0
                    && progress.Soldiers.Single(s => s.Kind == UnitKind.Infantry.ToString()).Count > 0
                    && progress.Soldiers.Single(s => s.Kind == UnitKind.LightCavalry.ToString()).Count > 0) break;
            }
            snapshot = snapshot ?? simulation.CaptureMatchPackSnapshot();
            var cavalry = snapshot.Factions.FirstOrDefault(f => f.Civ == CivKind.Cavalry.ToString());
            Assume.That(cavalry, Is.Not.Null);
            Assert.That(cavalry.Buildings.Single(b => b.Kind == BuildingKind.Saddlery.ToString()).Count, Is.GreaterThan(0),
                "tick=" + snapshot.Tick + " civ=" + cavalry.Civ + " age=" + cavalry.Age + " F=" + cavalry.Food + " W=" + cavalry.Wood + " T=" + cavalry.Tack
                + " buildings=" + string.Join("/", cavalry.Buildings.Select(b => b.Kind + ":" + b.Count))
                + " soldiers=" + string.Join("/", cavalry.Soldiers.Select(s => s.Kind + ":" + s.Count)));
            Assert.That(cavalry.TackProduced, Is.GreaterThan(0), "tick=" + snapshot.Tick + " age=" + cavalry.Age + " F=" + cavalry.Food + " W=" + cavalry.Wood + " T=" + cavalry.Tack
                + " buildings=" + string.Join("/", cavalry.Buildings.Select(b => b.Kind + ":" + b.Count)));
            Assert.That(cavalry.TackTrainedHeavyCavalry, Is.GreaterThan(0), "tick=" + snapshot.Tick + " tack=" + cavalry.Tack + " produced=" + cavalry.TackProduced);
            Assert.That(cavalry.Soldiers.Single(s => s.Kind == UnitKind.Infantry.ToString()).Count, Is.GreaterThan(0));
            Assert.That(cavalry.Soldiers.Single(s => s.Kind == UnitKind.LightCavalry.ToString()).Count, Is.GreaterThan(0));
        }

        [Test]
        public void TackOnRecordsAndReplaysTheSameHashAtEveryTick()
        {
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 160, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
