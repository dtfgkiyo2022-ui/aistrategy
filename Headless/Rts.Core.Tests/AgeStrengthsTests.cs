using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AgeStrengthsTests
    {
        private static ScenarioDefinition Scenario(bool strengths = true)
        {
            var s = LiveGameRules.Create(3, false, true, false, false);
            s.Economy.Ages = true;
            s.Economy.AgeFour = true;
            s.Economy.AgeStrengths = strengths;
            s.Economy.AgeClock = false;
            s.Economy.AgeClockUx = false;
            s.Economy.EarlyArms = false;
            s.Economy.AgeVictoryEnabled = false;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.StartStone = 50000;
            s.Economy.StartMetal = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            s.Economy.Age4FoodCost = 0;
            s.Economy.Age4WoodCost = 0;
            s.Economy.Age4Ticks = 1;
            s.Cores[0].Hp = s.Cores[1].Hp = 6000;
            return s;
        }

        [Test]
        public void Extension62RoundTripsVersion2AndIsAbsentWhenOff()
        {
            var on = Scenario(true);
            var onBytes = ScenarioBinary.Encode(on);
            var onCopy = ScenarioBinary.Decode(onBytes);
            var extension = onCopy.Extensions.Single(x => x.Id == 62);
            Assert.That(extension.Version, Is.EqualTo(2));
            Assert.That(extension.Data.Length, Is.EqualTo(43 * sizeof(int)));
            Assert.That(onCopy.Economy.SanctuaryCharmHpAge4Permille, Is.EqualTo(1500));
            Assert.That(ScenarioBinary.Encode(onCopy), Is.EqualTo(onBytes));

            var off = Scenario(false);
            var offCopy = ScenarioBinary.Decode(ScenarioBinary.Encode(off));
            Assert.That(offCopy.Extensions.Any(x => x.Id == 62), Is.False);
        }

        [Test]
        public void AgeStrengthsOffKeepsEveryTickHashIdenticalAfterRoundTrip()
        {
            var source = MinimalOffScenario();
            var left = new Battle(source);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(source)));
            for (long tick = 1; tick <= 30000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        private static ScenarioDefinition MinimalOffScenario()
        {
            var s = MapGenerator.GenerateTerrain(1, false, false);
            s.Economy.Ages = true;
            s.Economy.AgeFour = true;
            s.Economy.AgeStrengths = false;
            s.Economy.AgeClock = false;
            s.Economy.AgeClockUx = false;
            return s;
        }

        [Test]
        public void EveryCivilisationHasAnInactiveAndActiveNumericStrengthMetric()
        {
            var metrics = new (CivKind civ, Func<Battle, int> metric)[]
            {
                (CivKind.Agrarian, s => Invoke<int>(s, "ScaledHousingCeiling", 1u, 4)),
                (CivKind.Metallurgy, s => Invoke<int>(s, "SmeltTicksFor", 1u)),
                (CivKind.Forestry, s => Invoke<int>(s, "WoodOf", BuildingKind.LumberCamp, 1u)),
                (CivKind.Masonry, s => Invoke<int>(s, "HpOf", BuildingKind.Wall, 1u, -1)),
                (CivKind.Caravan, s => Invoke<int>(s, "MarketReturnFor", 1u, 100)),
                (CivKind.Cavalry, s => Invoke<int>(s, "CavalryTrainTicks", 1u, 100)),
                (CivKind.Bridge, s => Invoke<int>(s, "WorkOf", BuildingKind.Bridge, 1u)),
                (CivKind.Academy, s => Invoke<bool>(s, "AgeStrength", 1u, CivKind.Academy, 2)
                    ? Math.Max(1, (Invoke<int>(s, "AcademyTicks", TechKind.Tools) * Economy(s).AcademyResearchTimePermille + 999) / 1000)
                    : Invoke<int>(s, "AcademyTicks", TechKind.Tools)),
                (CivKind.Cult, s => Invoke<int>(s, "ConversionTicksFor", 1u)),
                (CivKind.Fishing, s => Invoke<int>(s, "CarryFor", 1u, true)),
                (CivKind.Mountain, s => Invoke<int>(s, "GatherTicksFor", 1u, FirstNodeId(s, ResourceKind.Ore))),
                (CivKind.Tollgate, s => Invoke<int>(s, "HpOf", BuildingKind.Tollgate, 1u, -1)),
                (CivKind.Metropolis, s => Invoke<int>(s, "TownVillagerTrainTicksFor", 1u, false)),
                (CivKind.Sanctuary, s => SanctuaryCharmMetric(s))
            };

            foreach (var row in metrics)
            {
                var inactive = new Battle(Scenario(false));
                var active = new Battle(Scenario(true));
                SetFaction(inactive, row.civ, 4);
                SetFaction(active, row.civ, 4);
                int before = row.metric(inactive);
                int after = row.metric(active);
                Assert.That(after, Is.Not.EqualTo(before), row.civ.ToString());
            }
        }

        [Test]
        public void StrengthsWaitForTheAdvanceTimerBeforeBecomingActive()
        {
            var scenario = Scenario(true);
            scenario.Economy.AdvanceTicks = 4;
            var sim = new Battle(scenario);
            SetAdvanceTimer(sim, CivKind.Metallurgy, 4);
            Invoke<object>(sim, "AdvanceAges");
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(0));
            Assert.That(EconomyLong(sim, "AdvanceRemaining"), Is.EqualTo(3));
            Assert.That(Invoke<bool>(sim, "AgeStrength", 1u, CivKind.Metallurgy, 2), Is.False);
            for (int i = 0; i < 3; i++) Invoke<object>(sim, "AdvanceAges");
            Assert.That(EconomyLong(sim, "AdvanceRemaining"), Is.EqualTo(0));
            SetFaction(sim, CivKind.Metallurgy, 1);
            Assert.That(EconomyByte(sim, "Age"), Is.EqualTo(1));
            SetAdvanceTimer(sim, CivKind.Metallurgy, 4);
            Invoke<object>(sim, "AdvanceAges");
            Assert.That(EconomyByte(sim, "Age"), Is.EqualTo(1));
            Assert.That(EconomyLong(sim, "AdvanceRemaining"), Is.EqualTo(3));
            for (int i = 0; i < 3; i++) Invoke<object>(sim, "AdvanceAges");
            Assert.That(EconomyLong(sim, "AdvanceRemaining"), Is.EqualTo(0));
            SetFaction(sim, CivKind.Metallurgy, 2);
            Assert.That(EconomyByte(sim, "Age"), Is.EqualTo(2));
            Assert.That(Invoke<bool>(sim, "AgeStrength", 1u, CivKind.Metallurgy, 2), Is.True);
            Assert.That(Invoke<int>(sim, "SmeltTicksFor", 1u), Is.LessThan(scenario.Economy.SmeltTicks));
        }

        [Test]
        public void MasonryCavalryAndCultMaxHpStrengthsAreAppliedOnceToExistingUnits()
        {
            var masonry = new Battle(Scenario(true));
            SetFaction(masonry, CivKind.Masonry, 4);
            int masonryBefore = CoreMaxHp(masonry);
            Invoke<object>(masonry, "ApplyAgeStrengthsAtAdvance", 1u);
            int masonryAfter = CoreMaxHp(masonry);
            Assert.That(masonryAfter, Is.EqualTo(masonryBefore * 1300 / 1000));
            Invoke<object>(masonry, "ApplyAgeStrengthsAtAdvance", 1u);
            Assert.That(CoreMaxHp(masonry), Is.EqualTo(masonryAfter));

            var cavalry = new Battle(Scenario(true));
            PrepareSoldier(cavalry, UnitKind.HeavyCavalry, 100);
            SetFaction(cavalry, CivKind.Cavalry, 4);
            Invoke<object>(cavalry, "ApplyAgeStrengthsAtAdvance", 1u);
            Assert.That(SoldierHp(cavalry), Is.EqualTo(115));

            var cult = new Battle(Scenario(true));
            PrepareSoldier(cult, UnitKind.Monk, 100);
            SetFaction(cult, CivKind.Cult, 4);
            Invoke<object>(cult, "ApplyAgeStrengthsAtAdvance", 1u);
            Assert.That(SoldierHp(cult), Is.EqualTo(150));
        }

        [Test]
        public void AgeStrengthsRecordAndReplayHaveMatchingTickHashes()
        {
            using (var stream = new MemoryStream())
            {
                var result = ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 600, new BuildIdentity());
                Assert.That(result.IsFault, Is.False);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(replay.IsFault, Is.False);
                Assert.That(replay.FirstMismatchTick, Is.Null);
            }
        }

        private static int SanctuaryCharmMetric(Battle sim)
        {
            var economy = Economy(sim);
            return Invoke<bool>(sim, "AgeStrength", 1u, CivKind.Sanctuary, 4)
                ? economy.SanctuaryCharmHpAge4Permille : 1000;
        }

        private static EconomyRules Economy(Battle sim)
            => (EconomyRules)ReadField(ReadField(ReadField(sim, "world"), "Config"), "Economy");

        private static uint FirstNodeId(Battle sim, ResourceKind kind)
        {
            var world = ReadField(sim, "world");
            var nodes = (Array)ReadField(world, "Nodes");
            for (int i = 0; i < nodes.Length; i++)
            {
                var definition = ReadField(nodes.GetValue(i), "Definition");
                if ((ResourceKind)ReadField(definition, "Kind") == kind) return (uint)i + 1;
            }
            return 1;
        }

        private static void SetFaction(Battle sim, CivKind civ, byte age)
        {
            var world = ReadField(sim, "world");
            var economies = (Array)ReadField(world, "Economies");
            var economy = economies.GetValue(0);
            WriteField(economy, "Civ", civ);
            WriteField(economy, "Age", age);
            economies.SetValue(economy, 0);
        }

        private static void SetAdvanceTimer(Battle sim, CivKind civ, long remaining)
        {
            var world = ReadField(sim, "world");
            var economies = (Array)ReadField(world, "Economies");
            var economy = economies.GetValue(0);
            WriteField(economy, "AdvancingTo", civ);
            WriteField(economy, "AdvanceRemaining", remaining);
            economies.SetValue(economy, 0);
        }

        private static long EconomyLong(Battle sim, string field)
        {
            var world = ReadField(sim, "world");
            var economies = (Array)ReadField(world, "Economies");
            return (long)ReadField(economies.GetValue(0), field);
        }

        private static byte EconomyByte(Battle sim, string field)
        {
            var world = ReadField(sim, "world");
            var economies = (Array)ReadField(world, "Economies");
            return (byte)ReadField(economies.GetValue(0), field);
        }

        private static int CoreMaxHp(Battle sim)
        {
            var world = ReadField(sim, "world");
            var cores = (Array)ReadField(world, "Cores");
            return (int)ReadField(ReadField(cores.GetValue(0), "Definition"), "Hp");
        }

        private static void PrepareSoldier(Battle sim, UnitKind kind, int hp)
        {
            var world = ReadField(sim, "world");
            var soldiers = (Array)ReadField(world, "Soldiers");
            var soldier = soldiers.GetValue(0);
            var initial = ReadField(soldier, "Initial");
            WriteField(initial, "FactionId", 1u);
            WriteField(initial, "Kind", kind);
            WriteField(initial, "Hp", hp);
            WriteField(soldier, "Initial", initial);
            var parameters = ReadField(soldier, "Parameters");
            WriteField(parameters, "Hp", hp);
            WriteField(soldier, "Parameters", parameters);
            WriteField(soldier, "Hp", hp);
            WriteField(soldier, "Alive", true);
            soldiers.SetValue(soldier, 0);
        }

        private static int SoldierHp(Battle sim)
        {
            var soldiers = (Array)ReadField(ReadField(sim, "world"), "Soldiers");
            return (int)ReadField(soldiers.GetValue(0), "Hp");
        }

        private static object ReadField(object value, string name)
            => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);

        private static void WriteField(object value, string name, object fieldValue)
            => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(value, fieldValue);

        private static T Invoke<T>(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == name && m.GetParameters().Length == arguments.Length)
                .Single(m => ParametersMatch(m.GetParameters(), arguments));
            return (T)method.Invoke(target, arguments);
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] arguments)
        {
            for (int i = 0; i < parameters.Length; i++)
                if (arguments[i] == null ? parameters[i].ParameterType.IsValueType : !parameters[i].ParameterType.IsAssignableFrom(arguments[i].GetType()))
                    return false;
            return true;
        }
    }
}
