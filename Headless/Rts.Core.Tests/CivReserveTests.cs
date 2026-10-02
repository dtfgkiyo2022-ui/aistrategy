using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// The civilisation reservation: an advance asked for in the primitive age before it is possible is kept, and taken
    /// on the first tick advancing can start, before the civilisation the ground would choose.
    /// </summary>
    public sealed class CivReserveTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo ChooseCivMethod = typeof(Battle).GetMethod("ChooseCiv", Private);
        private static readonly MethodInfo CanAdvanceWithoutCivMethod = typeof(Battle).GetMethod("CanAdvanceWithoutCiv", Private);
        private static readonly byte[] ReservedName = Encoding.UTF8.GetBytes("ReservedCiv");

        private static ScenarioDefinition Scenario(ulong seed, int villagerTarget = 5)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Sanctuary = true;
            s.Economy.AutoVillagerTarget = villagerTarget;
            s.Economy.AutoInfantryQueue = 0;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            for (int i = 0; i < s.UnitParameters.Length; i++) s.UnitParameters[i].Damage = 0;
            return s;
        }

        /// <summary>The first seed whose ground does not choose the sanctuary for the west.</summary>
        private static ulong SeedNotChoosingSanctuary(out CivKind chosen)
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                chosen = Choose(new Battle(Scenario(seed)), 1);
                if (chosen != CivKind.Sanctuary) return seed;
            }
            Assert.Fail("seed 1..30 all choose the sanctuary");
            chosen = CivKind.Primitive;
            return 0;
        }

        private static CivKind Choose(Battle sim, uint faction) => (CivKind)ChooseCivMethod.Invoke(sim, new object[] { faction });

        private static bool CanAdvanceWithoutCiv(Battle sim, uint faction) => (bool)CanAdvanceWithoutCivMethod.Invoke(sim, new object[] { faction });

        private static void SetEconomyField(Battle sim, uint faction, string name, object value)
        {
            object world = typeof(Battle).GetField("world", Private).GetValue(sim);
            var economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(name, Private).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static bool Started(EconomyView e) => e.AdvanceRemaining > 0 || e.Civ != CivKind.Primitive;

        private static bool Contains(System.Collections.Generic.IReadOnlyList<byte> haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Count; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }

        /// <summary>
        /// Steps until the west starts advancing and checks it starts on exactly the tick after the first tick that ended
        /// with advancing possible. Returns that tick.
        /// </summary>
        private static long StepUntilTheReservedAdvance(Battle sim, CommandGateway gateway, int limit)
        {
            long firstPossible = -1;
            for (int i = 0; i < limit; i++)
            {
                bool possible = CanAdvanceWithoutCiv(sim, 1);
                long tick = sim.Capture(1).Tick;
                gateway.Step();
                var e = sim.Capture(1).Economy;
                if (Started(e))
                {
                    Assert.That(possible, Is.True, "advancing starts only when the previous tick ended with it possible (tick " + (tick + 1) + ")");
                    Assert.That(firstPossible, Is.EqualTo(-1), "the first tick advancing was possible (" + firstPossible + ") already had to start it");
                    return tick + 1;
                }
                if (possible && firstPossible < 0 && tick >= 1) firstPossible = tick;
                if (firstPossible >= 0) Assert.Fail("tick " + firstPossible + " ended with advancing possible, but tick " + (firstPossible + 1) + " did not start it");
            }
            Assert.Fail("no advance within " + limit + " ticks");
            return -1;
        }

        [Test]
        public void AReservationWaitsForTheStockAndBeatsTheGround()
        {
            ulong seed = SeedNotChoosingSanctuary(out CivKind ground);
            var scenario = Scenario(seed);
            Assert.That(scenario.Economy.StartFood, Is.LessThan(scenario.Economy.AdvanceFoodCost), "the start cannot pay for advancing");
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Sanctuary));
            gateway.Step();
            var first = sim.Capture(1).Economy;
            Assert.That(Started(first), Is.False, "nothing starts without the stock");
            Assert.That(first.ReservedCiv, Is.EqualTo(CivKind.Sanctuary), "the button is kept as a reservation");
            Assert.That(sim.Capture(2).Economy.ReservedCiv, Is.EqualTo(CivKind.Primitive), "the east reserved nothing");

            long started = StepUntilTheReservedAdvance(sim, gateway, 20000);
            var e = sim.Capture(1).Economy;
            TestContext.WriteLine("seed " + seed + ": ground chooses " + ground + ", advancing started on tick " + started);
            Assert.That(e.AdvancingTo, Is.EqualTo(CivKind.Sanctuary), "the reserved civilisation, not the ground's " + ground);
            Assert.That(e.ReservedCiv, Is.EqualTo(CivKind.Primitive), "starting clears the reservation");
            for (int i = 0; i < scenario.Economy.AdvanceTicks + 1; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Sanctuary));
        }

        [Test]
        public void AReservationWaitsForVillagerTrainingAtAHandRunCore()
        {
            ulong seed = SeedNotChoosingSanctuary(out _);
            var scenario = Scenario(seed);
            scenario.Economy.StartFood = 2000;
            scenario.Economy.StartWood = 2000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            // The villager first, so the advance arrives while one is training (the core is then run by hand).
            gateway.SubmitEconomy(EconomyCommand.Train(1, 1, 0, UnitKind.Villager));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Sanctuary));
            gateway.Step();
            var first = sim.Capture(1).Economy;
            Assert.That(first.VillagerQueued, Is.EqualTo(1));
            Assert.That(first.CorePlayerHeld, Is.True, "the core is run by hand");
            Assert.That(Started(first), Is.False, "advancing waits for the villager");
            Assert.That(first.ReservedCiv, Is.EqualTo(CivKind.Sanctuary));
            Assert.That(first.Food, Is.GreaterThanOrEqualTo(scenario.Economy.AdvanceFoodCost), "only the training holds it back");

            long started = StepUntilTheReservedAdvance(sim, gateway, scenario.Economy.VillagerTrainTicks + 100);
            var e = sim.Capture(1).Economy;
            Assert.That(e.VillagerQueued, Is.EqualTo(0));
            Assert.That(e.AdvancingTo, Is.EqualTo(CivKind.Sanctuary));
            Assert.That(started, Is.GreaterThan(1));
            for (int i = 0; i < scenario.Economy.AdvanceTicks + 1; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Sanctuary));
        }

        [Test]
        public void TheLaterButtonOverwritesTheReservation()
        {
            ulong seed = SeedNotChoosingSanctuary(out CivKind ground);
            var second = ground == CivKind.Agrarian ? CivKind.Metallurgy : CivKind.Agrarian;
            var scenario = Scenario(seed);
            scenario.Economy.StartFood = 0;
            scenario.Economy.StartWood = 0;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            // The automatic economy off: no gathering, so the stock is set by hand below.
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Sanctuary));
            for (int i = 0; i < 10; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.ReservedCiv, Is.EqualTo(CivKind.Sanctuary));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 3, second));
            for (int i = 0; i < 10; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.ReservedCiv, Is.EqualTo(second), "the later button wins");
            Assert.That(Started(sim.Capture(1).Economy), Is.False);

            SetEconomyField(sim, 1, "Food", scenario.Economy.AdvanceFoodCost);
            SetEconomyField(sim, 1, "Wood", scenario.Economy.AdvanceWoodCost);
            gateway.Step();
            var e = sim.Capture(1).Economy;
            Assert.That(e.AdvancingTo, Is.EqualTo(second), "the very next tick, into the later choice (the ground chooses " + ground + ")");
            Assert.That(e.ReservedCiv, Is.EqualTo(CivKind.Primitive));
            for (int i = 0; i < scenario.Economy.AdvanceTicks + 1; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(second));
        }

        [Test]
        public void AClosedCivilisationIsNotReservedAndChangesNoState()
        {
            ulong seed = SeedNotChoosingSanctuary(out _);
            var scenario = Scenario(seed);
            Assert.That(scenario.Economy.Tollgate, Is.False, "the tollgate is closed on this map");
            var asked = new Battle(scenario);
            var plain = new Battle(scenario);
            var askedGateway = new CommandGateway(asked);
            var plainGateway = new CommandGateway(plain);
            askedGateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Tollgate));
            askedGateway.SubmitEconomy(EconomyCommand.Advance(2, 2, CivKind.Primitive));
            for (int tick = 1; tick <= 300; tick++)
            {
                askedGateway.Step();
                plainGateway.Step();
                Assert.That(asked.Capture(1).Economy.ReservedCiv, Is.EqualTo(CivKind.Primitive));
                Assert.That(asked.Capture(2).Economy.ReservedCiv, Is.EqualTo(CivKind.Primitive));
                // The canonical state counts the inputs read, so it is compared field by field instead of by hash.
                Assert.That(Contains(asked.CaptureDiagnostic().CanonicalState, ReservedName), Is.False, "tick " + tick);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var a = asked.Capture(faction).Economy;
                    var p = plain.Capture(faction).Economy;
                    Assert.That(new object[] { a.Food, a.Wood, a.Civ, a.AdvancingTo, a.AdvanceRemaining, a.VillagerQueued, a.Population },
                        Is.EqualTo(new object[] { p.Food, p.Wood, p.Civ, p.AdvancingTo, p.AdvanceRemaining, p.VillagerQueued, p.Population }),
                        "faction " + faction + ", tick " + tick);
                }
            }
        }

        [Test]
        public void WithoutAReservationTheStateHasNoTraceAndTheGroundChooses()
        {
            ulong seed = SeedNotChoosingSanctuary(out _);
            // The automatic economy's own advance needs AdvanceVillagers (8) villagers, so the map's own target here.
            var scenario = Scenario(seed, MapGenerator.GenerateTerrain(seed).Economy.AutoVillagerTarget);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            bool advanced = false;
            var expected = CivKind.Primitive;
            for (int i = 0; i < 20000 && !advanced; i++)
            {
                // The automatic economy decides on the allocation cycle; what the ground chooses just before it.
                if ((sim.Capture(1).Tick + 1) % 20 == 0) expected = Choose(sim, 1);
                gateway.Step();
                var e = sim.Capture(1).Economy;
                Assert.That(e.ReservedCiv, Is.EqualTo(CivKind.Primitive));
                if (i % 50 == 0) Assert.That(Contains(sim.CaptureDiagnostic().CanonicalState, ReservedName), Is.False, "no reservation is written");
                if (Started(e))
                {
                    advanced = true;
                    TestContext.WriteLine("seed " + seed + ": the automatic economy advanced on tick " + sim.Capture(1).Tick + " into " + e.AdvancingTo);
                    Assert.That(e.Civ != CivKind.Primitive ? e.Civ : e.AdvancingTo, Is.EqualTo(expected), "the ground's civilisation, as before");
                }
            }
            Assert.That(advanced, Is.True, "the automatic economy advances on its own");

            // With one held, the reservation is in the canonical state, so a replay compares it.
            var reserving = new Battle(scenario);
            var reservingGateway = new CommandGateway(reserving);
            reservingGateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Sanctuary));
            reservingGateway.Step();
            Assert.That(Contains(reserving.CaptureDiagnostic().CanonicalState, ReservedName), Is.True);
        }

        [Test]
        public void AReservedMatchReplaysForTwentyThousandTicks()
        {
            ulong seed = SeedNotChoosingSanctuary(out _);
            var scenario = Scenario(seed);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Sanctuary));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, CivKind.Metallurgy));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.ReservedCiv, Is.EqualTo(CivKind.Sanctuary));
            Assert.That(sim.Capture(2).Economy.ReservedCiv, Is.EqualTo(CivKind.Metallurgy));
            for (int i = 0; i < 19999 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Sanctuary));
            Assert.That(sim.Capture(2).Economy.Civ, Is.EqualTo(CivKind.Metallurgy));
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                var record = ReplayRunner.Record(stream, scenario, gateway.Inputs, 20000, identity);
                Assert.That(record.IsFault, Is.False);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }
    }
}
