using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CivVarietyCapsTests
    {
        [Test]
        public void CapsFlagOffDoesNotWriteId35OrChangeBytes()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CivVariety = true;
            scenario.Economy.CivVarietyCaps = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 35), Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CapsFlagOffIgnoresCapValuesAndKeepsEveryTickHash()
        {
            var leftScenario = LiveGameRules.Create(3, false, true, false, false);
            leftScenario.Economy.CivVarietyCaps = false;
            leftScenario.Economy.CivVarietyCapMetallurgy = 1;
            leftScenario.Economy.CivVarietyCapAgrarian = 20;
            var rightScenario = LiveGameRules.Create(3, false, true, false, false);
            rightScenario.Economy.CivVarietyCaps = false;
            rightScenario.Economy.CivVarietyCapMetallurgy = 20;
            rightScenario.Economy.CivVarietyCapAgrarian = 1;
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            Assert.That(ScenarioBinary.Encode(leftScenario), Is.EqualTo(ScenarioBinary.Encode(rightScenario)));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void CapsExtensionRoundTripsAllTuningValues()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CivVariety = true;
            scenario.Economy.CivVarietyCaps = true;
            scenario.Economy.CivVarietyCapMetallurgy = 17;
            scenario.Economy.CivVarietyCapAgrarian = 13;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.CivVarietyCaps, Is.True);
            Assert.That(decoded.Economy.CivVarietyCapMetallurgy, Is.EqualTo(17));
            Assert.That(decoded.Economy.CivVarietyCapAgrarian, Is.EqualTo(13));
            Assert.That(decoded.Extensions.Single(x => x.Id == 35).Data.Length, Is.EqualTo(12));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void ChangingConfiguredCapCanChangeTheVarietyChoice()
        {
            var choose = typeof(Battle).GetMethod("ChooseCiv", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(choose, Is.Not.Null);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var low = LiveGameRules.Create(seed, false, true, false, false);
                low.Economy.CivVarietyCaps = true;
                low.Economy.CivVarietyCapMetallurgy = 1;
                low.Economy.CivVarietyCapAgrarian = 20;
                var high = LiveGameRules.Create(seed, false, true, false, false);
                high.Economy.CivVarietyCaps = true;
                high.Economy.CivVarietyCapMetallurgy = 20;
                high.Economy.CivVarietyCapAgrarian = 1;
                var left = new Battle(low);
                var right = new Battle(high);
                for (long tick = 1; tick < 4800; tick++)
                {
                    left.Step(tick, Array.Empty<ScheduledInput>());
                    right.Step(tick, Array.Empty<ScheduledInput>());
                }
                var leftChoice = (CivKind)choose.Invoke(left, new object[] { (uint)1 });
                var rightChoice = (CivKind)choose.Invoke(right, new object[] { (uint)1 });
                if (leftChoice != rightChoice) return;
            }
            Assert.Fail("種1〜40では満点欄による選択差を確認できなかった");
        }

        [Test]
        public void ReservedCivilisationStillHasPriorityWithCaps()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var scenario = LiveGameRules.Create(seed, false, true, false, false);
                scenario.Economy.CivVarietyCaps = true;
                scenario.Economy.CivVarietyCapMetallurgy = 1;
                scenario.Economy.CivVarietyCapAgrarian = 20;
                var sim = new Battle(scenario);
                var gateway = new Rts.Application.CommandGateway(sim);
                gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Sanctuary));
                for (int i = 0; i < 20000 && sim.Capture(1).Economy.Civ == CivKind.Primitive; i++) gateway.Step();
                if (sim.Capture(1).Economy.Civ == CivKind.Sanctuary) return;
            }
            Assert.Fail("種1〜30で予約文明が選ばれなかった");
        }
    }
}
