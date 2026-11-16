using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class UniqueUnitsTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly UnitKind[] Kinds =
        {
            UnitKind.IronGuard, UnitKind.Magus, UnitKind.HorseArcher,
            UnitKind.Zealot, UnitKind.ShadowHunter, UnitKind.StoneWarden
        };

        [Test]
        public void LiveRulesExposeSixUniqueUnitsAndTheirAuthoredStats()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);

            Assert.That(scenario.Economy.UniqueUnits, Is.True);
            Assert.That(scenario.UnitParameters.Where(x => Kinds.Contains(x.Kind)).ToArray(), Has.Length.EqualTo(6));
            Assert.That(scenario.UnitParameters.Single(x => x.Kind == UnitKind.IronGuard).Hp, Is.EqualTo(300));
            Assert.That(scenario.UnitParameters.Single(x => x.Kind == UnitKind.Magus).AbilityPower, Is.EqualTo(40));
            Assert.That(scenario.UnitParameters.Single(x => x.Kind == UnitKind.HorseArcher).Damage, Is.EqualTo(9));
            Assert.That(scenario.UnitParameters.Single(x => x.Kind == UnitKind.Zealot).Hp, Is.EqualTo(160));
            Assert.That(scenario.UnitParameters.Single(x => x.Kind == UnitKind.ShadowHunter).Range, Is.EqualTo(Fix64.FromInt(2)));
            Assert.That(scenario.UnitParameters.Single(x => x.Kind == UnitKind.StoneWarden).Armor, Is.EqualTo(35));

            var kits = scenario.UnitSkillKits.Where(x => Kinds.Contains(x.UnitKind)).ToArray();
            Assert.That(kits, Has.Length.EqualTo(6));
            Assert.That(kits.Single(x => x.UnitKind == UnitKind.Zealot).PassiveKind, Is.EqualTo(PassiveKind.BloodOath));
            Assert.That(kits.SelectMany(x => x.SkillIds), Is.EquivalentTo(Enumerable.Range(20, 24)));
        }

        [Test]
        public void UniqueUnitsRoundTripPreservesExtensionAndDefinitions()
        {
            var source = LiveGameRules.Create(7, true, true, false, false);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);

            Assert.That(decoded.Economy.UniqueUnits, Is.True);
            Assert.That(decoded.UnitParameters.Single(x => x.Kind == UnitKind.StoneWarden).AbilityPower, Is.EqualTo(30));
            Assert.That(decoded.UnitSkillKits.Single(x => x.UnitKind == UnitKind.Magus).Resource, Is.EqualTo(SkillResource.Mana));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void UniqueUnitsOffOmitsOnlyTheUniqueDefinitions()
        {
            var off = LiveGameRules.Create(1, true, true, false, false, uniqueUnitsOff: true);

            Assert.That(off.Economy.UniqueUnits, Is.False);
            Assert.That(off.UnitParameters.Any(x => Kinds.Contains(x.Kind)), Is.False);
            Assert.That(off.UnitSkillKits.Any(x => Kinds.Contains(x.UnitKind)), Is.False);
            Assert.That(off.Skills, Has.Length.EqualTo(43));
        }

        [Test]
        public void HealthSkillsAndBloodOathUseTheRequestedResources()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            var health = scenario.Skills.Where(x => x.Id >= 40 && x.Id <= 43).ToArray();

            Assert.That(health, Is.All.Matches<SkillDefinition>(x => x.Resource == SkillResource.Health));
            Assert.That(health.Select(x => x.Cost).ToArray(), Is.EqualTo(new[] { 15, 20, 10, 30 }));
            Assert.That(scenario.UnitSkillKits.Single(x => x.UnitKind == UnitKind.Zealot).PassiveKind,
                Is.EqualTo(PassiveKind.BloodOath));
        }

        [Test]
        public void AutomaticEconomyTrainsAUniqueUnitWithoutCivGoods()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.StartFood = 50000;
            scenario.Economy.StartWood = 50000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 0;
            scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.Age2Ticks = 1;
            scenario.Economy.LargeInfantryTrainTicks = 1;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AutoInfantryQueue = 20;
            scenario.Economy.PopulationCap = 1000;
            for (int i = 0; i < scenario.Armies.Length; i++)
                if (scenario.Armies[i].FactionId == 1) scenario.Armies[i].Capacity = 1000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            var simulation = new Battle(scenario);
            AddCompletedBarracks(simulation);
            var gateway = new CommandGateway(simulation);
            SetEconomy(simulation, "Age", (byte)2);
            SetEconomy(simulation, "Civ", CivKind.Metallurgy);
            SetEconomy(simulation, "Food", 50000);
            SetEconomy(simulation, "Wood", 50000);
            SetEconomy(simulation, "Metal", 50000);
            SetEconomy(simulation, "Steel", 0); // 11-16: the civilisation good is not part of the cost for now
            SetEconomy(simulation, "ArmsKit", 0); // no arms: the soldier keeps the plain C5.2.1 numbers (arms give HP x1.1)
            for (int i = 0; i < 4; i++) EnqueueAutomaticInfantry(simulation);
            UniqueUnitStatsSnapshot stats = default;
            for (int tick = 0; tick < 5000; tick++)
            {
                gateway.Step();
                stats = simulation.CaptureUniqueUnitStats().Single(x => x.FactionId == 1);
                if (stats.IronGuard > 0) break;
            }

            var finalEconomy = simulation.Capture(1).Economy;
            Assert.That(stats.IronGuard, Is.GreaterThan(0), "お任せが鋼なしで Iron Guard を訓練するまで到達しなかった。"
                + $" tick={simulation.Capture(1).Tick} age={finalEconomy.Age} civ={finalEconomy.Civ} F={finalEconomy.Food} W={finalEconomy.Wood} Steel={finalEconomy.Steel}"
                + $" considered={stats.CandidatesConsidered} replaced={stats.Replacements} goods={stats.SkippedGoodsShort} train={stats.SkippedCannotTrain} pay={stats.SkippedCannotPay}"
                + " buildings=" + string.Join(",", finalEconomy.Buildings.Select(x => x.Kind + ":" + x.Complete)));
            Assert.That(stats.Replacements, Is.GreaterThan(0));
            Assert.That(stats.CandidatesConsidered, Is.GreaterThanOrEqualTo(stats.Replacements));
            Assert.That(stats.SkippedGoodsShort, Is.EqualTo(0), "no unique unit waits for a civilisation good");

            // 11-16: the barracks spawns as Infantry; the trained unique unit must carry its own numbers (HP 300, AR 50).
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var soldiers = (Array)world.GetType().GetField("Soldiers", PrivateInstance).GetValue(world);
            int count = (int)world.GetType().GetProperty("SoldierCount", PrivateInstance).GetValue(world);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var soldier = soldiers.GetValue(i);
                if ((UnitKind)soldier.GetType().GetField("Class", PrivateInstance).GetValue(soldier) != UnitKind.IronGuard) continue;
                var parameters = (UnitParameters)soldier.GetType().GetField("Parameters", PrivateInstance).GetValue(soldier);
                Assert.That(parameters.Hp, Is.EqualTo(300));
                Assert.That(parameters.Armor, Is.EqualTo(50));
                Assert.That(parameters.Damage, Is.EqualTo(18));
                found = true;
            }
            Assert.That(found, Is.True, "a trained Iron Guard soldier exists");
        }

        private static void SetEconomy(Battle simulation, string name, object value)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            var economy = economies.GetValue(0);
            economy.GetType().GetField(name, PrivateInstance).SetValue(economy, value);
            economies.SetValue(economy, 0);
        }

        private static void AddCompletedBarracks(Battle simulation)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var cores = (Array)world.GetType().GetField("Cores", PrivateInstance).GetValue(world);
            var coreDefinition = cores.GetValue(0).GetType().GetField("Definition", PrivateInstance).GetValue(cores.GetValue(0));
            var position = coreDefinition.GetType().GetField("Position", PrivateInstance | BindingFlags.Public).GetValue(coreDefinition);
            var map = world.GetType().GetField("Map", PrivateInstance).GetValue(world);
            int coreCell = (int)map.GetType().GetMethod("Cell", PrivateInstance | BindingFlags.Public).Invoke(map, new[] { position });
            int centre = (int)typeof(Battle).GetMethod("FindAiExpandBarracksSite", PrivateInstance).Invoke(simulation, new object[] { 1u, coreCell });
            typeof(Battle).GetMethods(PrivateInstance).Single(method => method.Name == "PlaceBuildingAt" && method.GetParameters().Length == 5)
                .Invoke(simulation, new object[] { 1u, BuildingKind.Barracks, centre, Facing.North, 0u });
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            uint nextBuildingId = (uint)world.GetType().GetField("NextBuildingId", PrivateInstance).GetValue(world);
            int index = checked((int)nextBuildingId - 2);
            var building = buildings.GetValue(index);
            Set(building, "Complete", true);
            buildings.SetValue(building, index);
        }

        private static void EnqueueAutomaticInfantry(Battle simulation)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            int index = -1;
            for (int i = 0; i < buildings.Length; i++)
            {
                var building = buildings.GetValue(i);
                if ((BuildingKind)building.GetType().GetField("Kind", PrivateInstance).GetValue(building) == BuildingKind.Barracks)
                {
                    index = i;
                    break;
                }
            }
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "test setup did not produce a barracks");
            var buildingValue = buildings.GetValue(index);
            typeof(Battle).GetField("automaticEconomyAction", PrivateInstance).SetValue(simulation, true);
            var args = new object[] { 1u, buildingValue, UnitKind.Infantry };
            typeof(Battle).GetMethods(PrivateInstance).Single(method => method.Name == "Enqueue" && method.GetParameters().Length == 3)
                .Invoke(simulation, args);
            buildings.SetValue(args[1], index);
        }

        private static void Set(object instance, string name, object value)
            => instance.GetType().GetField(name, PrivateInstance).SetValue(instance, value);
    }
}
