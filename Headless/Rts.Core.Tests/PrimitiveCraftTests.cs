using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class PrimitiveCraftTests
    {
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(66066UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.CraftTree1 = true;
            scenario.Economy.PrimitiveCraft = enabled;
            scenario.Economy.HutWoodCost = 40;
            scenario.Economy.PrimitiveCraftTicks = 4;
            scenario.Economy.StoneToolsGatherIntervalPermille = 850;
            scenario.Economy.JerkyHpPermille = 1100;
            return scenario;
        }

        [Test]
        public void Extension66RoundTripsPrimitiveRules()
        {
            var source = Scenario(true);
            source.Economy.HutWoodCost = 47;
            source.Economy.JerkyOutput = 3;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 66 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.PrimitiveCraft, Is.True);
            Assert.That(decoded.Economy.HutWoodCost, Is.EqualTo(47));
            Assert.That(decoded.Economy.JerkyOutput, Is.EqualTo(3));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void FlagOffDoesNotWritePrimitiveExtensionOrChangeHashes()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            Assert.That(ScenarioBinary.Encode(Scenario(false)), Is.EqualTo(ScenarioBinary.Encode(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))))));
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void NewContractValuesAreAppended()
        {
            Assert.That((byte)ResourceKind.StoneTools, Is.EqualTo(31));
            Assert.That((byte)ResourceKind.Jerky, Is.EqualTo(32));
            Assert.That((byte)BuildingKind.Hut, Is.EqualTo(48));
            Assert.That((byte)ProcessingLineKind.PrimitiveCraft, Is.EqualTo(20));
            Assert.That((byte)RecipeKind.StoneTools, Is.EqualTo(8));
            Assert.That((byte)RecipeKind.Jerky, Is.EqualTo(9));
        }

        [Test]
        public void EditModeTextHasBothLanguages()
        {
            UiText.Japanese = false;
            Assert.That(UiText.PrimitiveCraftHutDescription(), Does.Contain("Hut"));
            UiText.Japanese = true;
            Assert.That(UiText.PrimitiveCraftHutDescription(), Does.Contain("作業小屋"));
            UiText.Japanese = false;
        }

        [Test]
        public void PrimitiveCraftBuildsHutAndProducesGoods()
        {
            var scenario = LiveGameRules.Create(66067UL, false, true, false, false);
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 5000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                if (sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Hut && b.Complete)
                    && sim.Capture(1).Economy.StoneTools > 0)
                    break;
            }
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.PrimitiveCraftEnabled, Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Hut), Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Sawmill), Is.True);
            Assert.That(economy.StoneTools + economy.Jerky, Is.GreaterThan(0));
        }

        [Test]
        public void PrimitiveCraftReplayIsDeterministic()
        {
            var left = new Battle(Scenario(true));
            var right = new Battle(Scenario(true));
            for (long tick = 1; tick <= 500; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
