using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiFactoryTests
    {
        private static ScenarioDefinition Scenario(bool factory)
        {
            var scenario = LiveGameRules.Create(67001UL, false, true, false, false);
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.CraftTree1 = true; e.PrimitiveCraft = true; e.AiFactory = factory;
            e.CommonDrill = true; e.Forestry = true; e.Masonry = true; e.Inserters = true; e.BeltComponents = true;
            e.HutWork = 1; e.LumberCampWork = 1; e.SawmillWork = 1; e.DrillWork = 1; e.StonecutterWork = 1;
            e.PrimitiveCraftTicks = 4; e.CraftTreePlankTicks = 4; e.CraftTreeStonecutterTicks = 4;
            e.AiFactoryBarracksIdleWindowTicks = 20;
            return scenario;
        }

        [Test]
        public void FlagOffIsHashStable()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension67RoundTripsAllAiFactoryRules()
        {
            var source = Scenario(true);
            source.Economy.AiFactoryPrimitiveLineCount = 7;
            source.Economy.AiFactoryBarracksIdlePermille = 125;
            source.Economy.AiFactoryAge4BarracksMaxBuildings = 11;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 67 && x.Version == 1), Is.True,
                "extensions=" + string.Join(",", decoded.Extensions.Select(x => x.Id.ToString())));
            Assert.That(decoded.Economy.AiFactory, Is.True);
            Assert.That(decoded.Economy.AiFactoryPrimitiveLineCount, Is.EqualTo(7));
            Assert.That(decoded.Economy.AiFactoryBarracksIdlePermille, Is.EqualTo(125));
            Assert.That(decoded.Economy.AiFactoryAge4BarracksMaxBuildings, Is.EqualTo(11));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PrimitiveFactoryBuildsLinesAndProcessedGoods()
        {
            var sim = new Battle(Scenario(true));
            for (long tick = 1; tick <= 6000; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.AiFactoryEnabled, Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Hut), Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Sawmill), Is.True,
                string.Join(",", economy.Buildings.Select(b => b.Kind.ToString())));
            Assert.That(economy.Belts.Count, Is.GreaterThan(0));
            Assert.That(economy.StoneTools + economy.Jerky + economy.Planks + economy.CutStone, Is.GreaterThan(0));
        }

        [Test]
        public void PrimitiveAgeBarracksStayAtConfiguredCap()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiFactoryPrimitiveBarracksMaxBuildings = 1;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 3000; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Barracks), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void UnavailableFactoryTemplateDoesNotPreventOtherTemplates()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiFactoryPrimitiveLineCount = 1;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 2500; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Hut), Is.True);
            Assert.That(economy.Belts.Count, Is.GreaterThan(0));
        }

        [Test]
        public void FactoryReplayHashesMatchEveryTick()
        {
            var left = new Battle(Scenario(true));
            var right = new Battle(Scenario(true));
            for (long tick = 1; tick <= 1000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
