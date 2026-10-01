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
    public sealed class SanctuaryTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool sanctuary)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Sanctuary = sanctuary;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 1000;
            scenario.Economy.AutoVillagerTarget = 3;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            scenario.Outposts[0].OwnerFactionId = 1;
            for (int i = 0; i < scenario.UnitParameters.Length; i++) scenario.UnitParameters[i].Damage = 0;
            return scenario;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtSanctuaryAge()
        {
            var scenario = Scenario(true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Sanctuary));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Sanctuary));
            return (scenario, sim, gateway, sequence);
        }

        private static int FindSite(Battle sim, uint outpostId)
            => (int)typeof(Battle).GetMethod("FindSanctuarySite", Private).Invoke(sim, new object[] { 1u, outpostId });

        private static uint PlaceAndBuildShrine(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence, uint outpostId)
        {
            int origin = FindSite(sim, outpostId);
            Assert.That(origin, Is.GreaterThanOrEqualTo(0), "a legal shrine site exists");
            uint before = (uint)sim.Capture(1).Economy.Buildings.Count;
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Shrine, origin, Facing.North));
            Steps(gateway, sim, 1);
            var placed = sim.Capture(1).Economy.Buildings;
            Assert.That(placed.Count, Is.EqualTo(before + 1));
            uint id = placed[(int)before].Id;
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            // A shrine is deliberately placed near an outpost rather than beside the core;
            // allow the assigned villagers enough deterministic time to walk there first.
            Steps(gateway, sim, scenario.Economy.ShrineWork + 1000);
            Assert.That(sim.Capture(1).Economy.Buildings.Single(b => b.Id == id).Complete, Is.True);
            return id;
        }

        private static void SetOutpostOwner(Battle sim, uint outpostId, uint faction)
        {
            object world = typeof(Battle).GetField("world", Private).GetValue(sim);
            var field = world.GetType().GetField("Outposts", Private);
            var outposts = (Array)field.GetValue(world);
            object outpost = outposts.GetValue((int)outpostId - 1);
            outpost.GetType().GetField("OwnerFactionId", Private).SetValue(outpost, faction);
            outposts.SetValue(outpost, (int)outpostId - 1);
        }

        private static int SanctuaryDamage(Battle sim, uint faction, int damage)
            => (int)typeof(Battle).GetMethod("SanctuaryDamage", Private).Invoke(sim, new object[] { faction, damage });

        [Test]
        public void SanctuaryOffKeepsOldBytesAndStateStable()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Sanctuary = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old);
            var right = new Battle(off);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void ShrineIsOnlyNearAnOwnedOutpostAndOnePerOutpost()
        {
            var state = AtSanctuaryAge();
            uint first = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 1);
            var fields = DiagnosticComparison.Fields(state.sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(fields["Buildings[" + first + "].SanctuaryOutpostId"], Is.EqualTo("1"));

            int anotherSite = FindSite(state.sim, 1);
            Assert.That(anotherSite, Is.EqualTo(-1), "the same outpost cannot receive a second shrine");

            if (state.scenario.Outposts.Length > 1)
            {
                SetOutpostOwner(state.sim, 2, 1);
                uint second = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 2);
                fields = DiagnosticComparison.Fields(state.sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
                Assert.That(fields["Buildings[" + second + "].SanctuaryOutpostId"], Is.EqualTo("2"));
            }
        }

        [Test]
        public void OwnedShrinesScaleDamageAndOutpostLossStopsTheBonus()
        {
            var state = AtSanctuaryAge();
            uint shrine = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 1);
            SetOutpostOwner(state.sim, 1, 1);
            bool allowed = (bool)typeof(Battle).GetMethod("SanctuaryAllowed", Private).Invoke(state.sim, new object[] { 1u });
            var fields = DiagnosticComparison.Fields(state.sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(allowed, Is.True, "sanctuary allowed; shrine=" + shrine + ", outpost=" + fields["Buildings[" + shrine + "].SanctuaryOutpostId"]);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(110));
            SetOutpostOwner(state.sim, 1, 2);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(100));
            SetOutpostOwner(state.sim, 1, 1);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(110));
            Assert.That(SanctuaryDamage(state.sim, 2, 100), Is.EqualTo(100));
        }

        [Test]
        public void SanctuaryAutomaticallyBuildsAResettledShrine()
        {
            var scenario = Scenario(true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Sanctuary));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 5000);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Shrine), Is.True);
        }

        [Test]
        public void SanctuaryExtensionRoundTripsWithIdEight()
        {
            var scenario = Scenario(true);
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Sanctuary, Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 8), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void SanctuaryReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = Scenario(true);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }
    }
}
