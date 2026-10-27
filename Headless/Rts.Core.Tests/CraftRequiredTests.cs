using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CraftRequiredTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(71071UL, false, true);
            var e = scenario.Economy;
            e.Ages = true;
            e.Industry = true;
            e.ProcessingChain = true;
            e.CraftTree1 = true;
            e.CraftTree2 = true;
            e.CraftRequired = enabled;
            e.CraftRequiredAiArmsStockTarget = 20;
            e.CraftRequiredAiArmsAssemblersAge1 = 1;
            e.CraftRequiredAiArmsAssemblersAge2 = 2;
            e.CraftRequiredAiArmsAssemblersAge3 = 4;
            e.CraftRequiredAiArmsAssemblersAge4 = 4;
            e.AutoVillagerTarget = 0;
            e.AutoInfantryQueue = 0;
            e.StartFood = 10000;
            e.StartWood = 10000;
            e.StartMetal = 10000;
            return scenario;
        }

        [Test]
        public void Extension71RoundTripsAndOffStateHasNoExtension()
        {
            var source = Scenario(true);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 71);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftRequired, Is.True);
            Assert.That(decoded.Economy.CraftRequiredAiArmsStockTarget, Is.EqualTo(20));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge1, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge2, Is.EqualTo(2));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge3, Is.EqualTo(4));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge4, Is.EqualTo(4));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            var offDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(off));
            Assert.That(offDecoded.Economy.CraftRequired, Is.False);
            Assert.That(offDecoded.Extensions.Any(x => x.Id == 71), Is.False);
        }

        [Test, Explicit("CraftRequired 旗オフの30,000 tickハッシュ一致")]
        public void FlagOffMatchesItsRoundTripAtEveryTick()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void AgeTwoSoldierNeedsArmsKitButAgeOneAndExcludedUnitsKeepNormalCosts()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Metal", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            SetEconomy(simulation, 0, "EliteKit", 0);
            var canPay = typeof(Battle).GetMethod("CanPay", PrivateInstance, null,
                new[] { typeof(uint), typeof(UnitKind) }, null);
            var cost = typeof(Battle).GetMethod("CostOf", PrivateInstance, null,
                new[] { typeof(uint), typeof(UnitKind) }, null);
            Assert.That(canPay, Is.Not.Null);
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.False);
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(10000));

            SetEconomy(simulation, 0, "ArmsKit", 1);
            var infantryCost = cost.Invoke(simulation, new object[] { 1u, UnitKind.Infantry });
            Assert.That(Get(infantryCost, "Item2"), Is.EqualTo(0));
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.True);

            foreach (var kind in new[] { UnitKind.Villager, UnitKind.Scout, UnitKind.Monk })
            {
                if (kind == UnitKind.Monk) SetConfig(simulation, "MonksEnabled", true);
                Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, kind }), Is.True, kind.ToString());
            }

            SetEconomy(simulation, 0, "Age", 1);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            var ageOneCost = cost.Invoke(simulation, new object[] { 1u, UnitKind.Infantry });
            Assert.That(Get(ageOneCost, "Item2"), Is.GreaterThan(0));
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.True);
        }

        [Test]
        public void EnqueueConsumesArmsKitAndCancelRefundsItWithoutRefundingZeroWood()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Metal", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 1);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType);
            Set(building, "Kind", BuildingKind.Barracks);
            var enqueue = typeof(Battle).GetMethod("Enqueue", PrivateInstance);
            var cancel = typeof(Battle).GetMethod("CancelLast", PrivateInstance);
            var beforeWood = GetEconomy(simulation, 0, "Wood");
            var args = new object[] { 1u, building, UnitKind.Infantry };
            enqueue.Invoke(simulation, args);
            Assert.That(GetEconomy(simulation, 0, "ArmsKit"), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(beforeWood));
            Assert.That(Get(args[1], "Queued"), Is.EqualTo(1));
            var cancelArgs = new[] { (object)1u, args[1] };
            cancel.Invoke(simulation, cancelArgs);
            Assert.That(GetEconomy(simulation, 0, "ArmsKit"), Is.EqualTo(1));
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(beforeWood));
            Assert.That(Get(cancelArgs[1], "Queued"), Is.EqualTo(0));
        }

        [Test, Explicit("CraftRequired AI の age1 assembler・ArmsKit・兵士生成を確認")]
        public void AiBuildsArmsKitsBeforeTrainingAgeTwoSoldiers()
        {
            var scenario = LiveGameRules.Create(71071UL, true, true, false, false);
            scenario.Economy.CraftRequired = true;
            scenario.Economy.AutoInfantryQueue = 1;
            scenario.Economy.CraftRequiredAiArmsStockTarget = 20;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 30000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            bool produced = false, assembler = false, ageTwo = false, soldier = false;
            for (int faction = 0; faction < 2; faction++)
            {
                var economy = GetEconomyObject(simulation, faction);
                produced |= Get(economy, "ArmsKitProduced") > 0;
                ageTwo |= Get(economy, "Age") >= 2;
                var soldiers = (Array)GetWorld(simulation).GetType().GetField("Soldiers", PrivateInstance).GetValue(GetWorld(simulation));
                soldier |= soldiers.Cast<object>().Any(value => Get(value, "Alive") != 0
                    && Get(GetObject(value, "Initial"), "FactionId") == faction + 1
                    && Get(GetObject(value, "Initial"), "Kind") == (int)UnitKind.Infantry);
            }
            var world = GetWorld(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            foreach (var value in buildings)
            {
                if ((BuildingKind)Get(value, "Kind") == BuildingKind.Assembler && Get(value, "Complete") != 0)
                    assembler = true;
            }
            Assert.That(ageTwo, Is.True);
            Assert.That(assembler, Is.True);
            Assert.That(produced, Is.True);
            Assert.That(soldier, Is.True);
        }

        [Test]
        public void EnabledReplayStaysDeterministic()
        {
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 1200, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static object GetWorld(Battle simulation)
            => typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);

        private static object GetEconomyObject(Battle simulation, int index)
        {
            var economies = (Array)GetWorld(simulation).GetType().GetField("Economies", PrivateInstance).GetValue(GetWorld(simulation));
            return economies.GetValue(index);
        }

        private static int GetEconomy(Battle simulation, int index, string name)
            => Get(GetEconomyObject(simulation, index), name);

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = GetWorld(simulation);
            var economiesField = world.GetType().GetField("Economies", PrivateInstance);
            var economies = (Array)economiesField.GetValue(world);
            var economy = economies.GetValue(index);
            Set(economy, name, value);
            economies.SetValue(economy, index);
        }

        private static void SetConfig(Battle simulation, string name, object value)
        {
            var world = GetWorld(simulation);
            var config = world.GetType().GetField("Config", PrivateInstance).GetValue(world);
            var economy = config.GetType().GetField("Economy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(config);
            Set(economy, name, value);
        }

        private static void Set(object value, string name, object fieldValue)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fieldValue != null && !field.FieldType.IsInstanceOfType(fieldValue))
                fieldValue = field.FieldType.IsEnum
                    ? Enum.ToObject(field.FieldType, fieldValue)
                    : Convert.ChangeType(fieldValue, Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType);
            field.SetValue(value, fieldValue);
        }

        private static int Get(object value, string name)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fieldValue = field.GetValue(value);
            return fieldValue is bool b ? (b ? 1 : 0) : Convert.ToInt32(fieldValue);
        }

        private static object GetObject(object value, string name)
            => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
    }
}
