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
    /// <summary>V3-15 #1: the mountain civilisation and renewable mine shafts.</summary>
    public sealed class MountainTests
    {
        private const int Width = 128;
        private static readonly MethodInfo Site = typeof(Battle).GetMethod("MountainShaftSiteIsClear", BindingFlags.Instance | BindingFlags.NonPublic);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition MountainScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Mountain = true;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.GatherIntervalTicks = 1000000;
            s.Economy.MountainWork = 1;
            s.Economy.MountainBaseIntervalTicks = 4;
            s.Economy.MountainIntervalStepTicks = 0;
            s.Economy.MountainMinIntervalTicks = 4;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) EnterMountain(ulong seed)
        {
            var scenario = MountainScenario(seed);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Mountain));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Mountain));
            return (sim, gateway, scenario, sequence);
        }

        private static bool IsPassable(ScenarioDefinition s, int cell)
            => !s.Map.BlockedCellIds.Contains(cell);

        private static int FindSite(Battle sim, ScenarioDefinition s, uint faction)
        {
            for (int origin = 0; origin < s.Map.WidthCells * s.Map.HeightCells; origin++)
            {
                int x = origin % s.Map.WidthCells, z = origin / s.Map.WidthCells;
                if (x + s.Economy.MountainSizeCells > s.Map.WidthCells || z + s.Economy.MountainSizeCells > s.Map.HeightCells) continue;
                if ((bool)Site.Invoke(sim, new object[] { faction, origin })) return origin;
            }
            return -1;
        }

        private static int FindPlainSite(ScenarioDefinition s)
        {
            int size = s.Economy.MountainSizeCells;
            for (int origin = 0; origin < s.Map.WidthCells * s.Map.HeightCells; origin++)
            {
                int x = origin % s.Map.WidthCells, z = origin / s.Map.WidthCells;
                if (x + size > s.Map.WidthCells || z + size > s.Map.HeightCells) continue;
                bool clear = true;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                        if (!IsPassable(s, origin + dz * Width + dx)) clear = false;
                if (!clear || TouchesMountain(s, origin, size)) continue;
                if (s.ResourceNodes.Any(n => n.Position.X.Raw / 65536 / s.Map.CellSizeMeters >= x
                    && n.Position.X.Raw / 65536 / s.Map.CellSizeMeters < x + size
                    && n.Position.Z.Raw / 65536 / s.Map.CellSizeMeters >= z
                    && n.Position.Z.Raw / 65536 / s.Map.CellSizeMeters < z + size)) continue;
                return origin;
            }
            return -1;
        }

        private static bool TouchesMountain(ScenarioDefinition s, int origin, int size)
        {
            int width = s.Map.WidthCells, height = s.Map.HeightCells;
            for (int z = origin / width; z < origin / width + size; z++)
                for (int x = origin % width; x < origin % width + size; x++)
                {
                    if (x > 0 && s.Map.Terrain[(z * width) + x - 1] == (byte)TerrainKind.Mountain) return true;
                    if (x + 1 < width && s.Map.Terrain[(z * width) + x + 1] == (byte)TerrainKind.Mountain) return true;
                    if (z > 0 && s.Map.Terrain[((z - 1) * width) + x] == (byte)TerrainKind.Mountain) return true;
                    if (z + 1 < height && s.Map.Terrain[((z + 1) * width) + x] == (byte)TerrainKind.Mountain) return true;
                }
            return false;
        }

        [Test]
        public void MountainOffKeepsOldBytesAndStateHashes()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Mountain = false;
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
        public void MountainFactionCanBuildOnlyAtMountainEdgeAndHasLimit()
        {
            var state = EnterMountain(2);
            int plain = FindPlainSite(state.scenario);
            Assert.That(plain, Is.GreaterThanOrEqualTo(0));
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, plain));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.MineShaft), Is.False);

            var sites = new List<int>();
            for (int i = 0; i < state.scenario.Map.WidthCells * state.scenario.Map.HeightCells && sites.Count < 4; i++)
            {
                int site = FindSite(state.sim, state.scenario, 1);
                if (site < 0 || sites.Contains(site)) break;
                sites.Add(site);
                state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, site));
                Steps(state.gateway, state.sim, 1);
            }
            Assert.That(state.sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.MineShaft), Is.EqualTo(3));
        }

        [Test]
        public void CompletedShaftAlternatesRenewableStoneAndOreWithoutDepletingNodes()
        {
            var state = EnterMountain(3);
            int site = FindSite(state.sim, state.scenario, 1);
            Assert.That(site, Is.GreaterThanOrEqualTo(0));
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, site));
            Steps(state.gateway, state.sim, 5000);
            var completed = state.sim.Capture(1).Economy;
            Assert.That(completed.Buildings.Single(b => b.Kind == BuildingKind.MineShaft).Complete, Is.True);
            int stoneBefore = completed.Stone;
            int oreBefore = completed.Ore;
            var mineralNodesBefore = completed.Resources
                .Where(r => r.Kind == ResourceKind.Stone || r.Kind == ResourceKind.Ore)
                .ToDictionary(r => r.Id, r => r.Remaining);
            Steps(state.gateway, state.sim, 8);
            var economy = state.sim.Capture(1).Economy;
            Assert.That(economy.Stone, Is.GreaterThan(stoneBefore));
            Assert.That(economy.Ore, Is.GreaterThan(oreBefore));
            foreach (var node in economy.Resources.Where(r => mineralNodesBefore.ContainsKey(r.Id)))
                Assert.That(node.Remaining, Is.EqualTo(mineralNodesBefore[node.Id]));
        }

        [Test]
        public void MountainAutoBuildsShaftsAfterTheCivilisationIsChosen()
        {
            var scenario = MountainScenario(4);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Mountain));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2500);
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Civ, Is.EqualTo(CivKind.Mountain));
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.MineShaft && b.Complete), Is.True);
            Assert.That(economy.Stone, Is.GreaterThan(0));
            Assert.That(economy.Ore, Is.GreaterThan(0));
        }

        [Test]
        public void MountainExtensionRoundTripsWithAcademyAndExistingCivilisationFlags()
        {
            var scenario = MountainScenario(5);
            scenario.Economy.Academy = true;
            scenario.Economy.GoldEnabled = true;
            scenario.Economy.Forestry = true;
            scenario.Economy.Masonry = true;
            scenario.Economy.Caravan = true;
            scenario.Economy.Cavalry = true;
            scenario.Economy.Bridge = true;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Mountain, Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 5), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void MountainScenarioReplaysForTwentyThousandTicks()
        {
            var scenario = MountainScenario(6);
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
