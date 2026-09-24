using System;
using System.IO;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// technical-design-v3 32 #21 (V3-5): an optional win by holding the third age. Off by default (`AgeVictoryEnabled`),
    /// so every scenario that does not set it keeps deciding by core capture alone, exactly as before this feature.
    /// </summary>
    public sealed class AgeVictoryTests
    {
        private static ScenarioDefinition FastAgeScenario(bool enabled)
        {
            var s = MapGenerator.GenerateTerrain(1);
            s.Economy.AgeVictoryEnabled = enabled;
            s.Economy.AgeVictoryTicks = 5;
            s.Economy.StartFood = 10000;
            s.Economy.StartWood = 10000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            return s;
        }

        /// <summary>
        /// Stops the automatic economy first (auto keeps a villager queued almost always, and a queued villager makes
        /// `CanAdvance` refuse the command), then advances one faction to the third age.
        /// </summary>
        private static void AdvanceToThirdAge(CommandGateway gateway, Battle sim, uint faction, ref ulong seq)
        {
            gateway.SubmitEconomy(EconomyCommand.Auto(faction, ++seq, false));
            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(faction, ++seq, 0));
            gateway.SubmitEconomy(EconomyCommand.Advance(faction, ++seq, CivKind.Agrarian));
            for (int i = 0; i < 3; i++) gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.Advance(faction, ++seq, CivKind.Agrarian));
            for (int i = 0; i < 3; i++) gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.Advance(faction, ++seq, CivKind.Agrarian));
            for (int i = 0; i < 3; i++) gateway.Step();
        }

        /// <summary>
        /// Advances both factions to the third age together, tick for tick. Doing this one faction fully at a time would
        /// let the first one alone complete the age-victory streak (with `AgeVictoryTicks` small in these tests) before
        /// the second faction ever got its own Advance commands in, ending the match early.
        /// </summary>
        private static void AdvanceBothToThirdAge(CommandGateway gateway, Battle sim, ref ulong seq)
        {
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++seq, false));
            for (int i = 0; i < 5; i++)
            {
                gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
                gateway.SubmitEconomy(EconomyCommand.CancelTrain(2, ++seq, 0));
            }
            for (int step = 0; step < 3; step++)
            {
                gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Agrarian));
                gateway.SubmitEconomy(EconomyCommand.Advance(2, ++seq, CivKind.Agrarian));
                for (int i = 0; i < 3; i++) gateway.Step();
            }
        }

        [Test]
        public void ThirdAgeWinsAfterTheConfiguredAliveCoreStreakAndItReplays()
        {
            var s = FastAgeScenario(true);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            AdvanceToThirdAge(gateway, sim, 1, ref seq);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));
            for (int i = 0; i < 10 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            Assert.That(sim.Capture(1).Result.HasEnded, Is.True);
            Assert.That(sim.Capture(1).Result.WinnerFactionId, Is.EqualTo(1U));
            Assert.That(sim.Capture(1).Result.IsAgeVictory, Is.True);

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        /// <summary>The one thing this feature must never do: change a scenario that never turns it on.</summary>
        [Test]
        public void DisabledByDefaultAScenarioNeverDecidesByTheThirdAgeAlone()
        {
            var s = FastAgeScenario(false); // AgeVictoryEnabled left at its default: false, never set to true
            Assume.That(s.Economy.AgeVictoryEnabled, Is.False, "the flag defaults to off");
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            AdvanceToThirdAge(gateway, sim, 1, ref seq);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));
            // Well past AgeVictoryTicks (5) or any plausible value, with the flag off: ordinary combat may still end the
            // match on its own (armies clash regardless of ages), but it must never be IsAgeVictory when it does.
            for (int i = 0; i < 4000 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            if (sim.Capture(1).Result.HasEnded) Assert.That(sim.Capture(1).Result.IsAgeVictory, Is.False, "the flag was off");
        }

        /// <summary>Sets a core's HP directly, the way the repair and gems tests reach into private state to force damage.</summary>
        private static void SetCoreHp(Battle sim, uint faction, int hp)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var world = typeof(Battle).GetField("world", flags).GetValue(sim);
            var cores = (Array)world.GetType().GetField("Cores", flags).GetValue(world);
            for (int i = 0; i < cores.Length; i++)
            {
                object core = cores.GetValue(i);
                var definitionField = core.GetType().GetField("Definition", flags);
                var definition = definitionField.GetValue(core);
                if ((uint)definition.GetType().GetField("FactionId", flags).GetValue(definition) != faction) continue;
                core.GetType().GetField("Hp", flags).SetValue(core, hp);
                cores.SetValue(core, i);
                return;
            }
            Assert.Fail("no core for faction " + faction);
        }

        /// <summary>Core capture still wins even while the flag is on and the loser was about to reach the age-victory streak.</summary>
        [Test]
        public void CoreCaptureStillDecidesBeforeTheAgeStreakCompletes()
        {
            var s = FastAgeScenario(true);
            s.Economy.AgeVictoryTicks = 1000; // long enough that the forced core kill below lands first
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            AdvanceToThirdAge(gateway, sim, 1, ref seq);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));
            Assume.That(sim.Capture(1).Result.HasEnded, Is.False, "the streak has not completed yet");

            SetCoreHp(sim, 1, 0);
            gateway.Step();
            Assert.That(sim.Capture(1).Result.HasEnded, Is.True);
            Assert.That(sim.Capture(1).Result.WinnerFactionId, Is.EqualTo(2U), "the east won by capture, not by holding the age");
            Assert.That(sim.Capture(1).Result.IsAgeVictory, Is.False, "a core fell first, not the age streak");
        }

        /// <summary>Once both sides stand in the third age, holding it no longer decides anything by itself.</summary>
        [Test]
        public void BothSidesInTheThirdAgeTurnsOffTheAgeVictoryEntirely()
        {
            var s = FastAgeScenario(true);
            s.Economy.AgeVictoryTicks = 5;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            AdvanceBothToThirdAge(gateway, sim, ref seq);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));
            Assume.That(sim.Capture(2).Economy.Age, Is.EqualTo(3));
            for (int i = 0; i < 50 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            // Whatever happens from here is ordinary combat, not the age streak: it must never carry IsAgeVictory.
            if (sim.Capture(1).Result.HasEnded) Assert.That(sim.Capture(1).Result.IsAgeVictory, Is.False);
        }

        [Test]
        public void SchemaFiveRoundTripsAgeVictoryRules()
        {
            var scenario = FastAgeScenario(true);
            var bytes = ScenarioBinary.Encode(scenario);
            Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(5));
            var copy = ScenarioBinary.Decode(bytes);
            Assert.That(copy.Economy.AgeVictoryEnabled, Is.True);
            Assert.That(copy.Economy.AgeVictoryTicks, Is.EqualTo(5));
            Assert.That(ScenarioBinary.Encode(copy), Is.EqualTo(bytes));
        }
    }
}
