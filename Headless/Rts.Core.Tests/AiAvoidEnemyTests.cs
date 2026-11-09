using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiAvoidEnemyTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled, bool reservations = false)
        {
            var scenario = MapGenerator.GenerateTerrain(4401);
            scenario.Economy.AiAvoidEnemy = enabled;
            scenario.Economy.AiAvoidEnemyMeters = 24;
            scenario.Economy.BuildingReservations = reservations;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            scenario.Economy.StartStone = 100000;
            scenario.Economy.GatherIntervalTicks = 1000000;
            scenario.Economy.Regions = false;
            return scenario;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static MethodInfo Method(string name)
            => typeof(Battle).GetMethod(name, Hidden);

        private static int Size(Battle simulation, BuildingKind kind)
            => (int)Method("SizeOf").Invoke(simulation, new object[] { kind });

        private static int ClearOrigin(Battle simulation, BuildingKind kind)
        {
            var state = State(simulation);
            int size = Size(simulation, kind), width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int core = state.Map.Cell(state.Cores[0].Definition.Position);
            int cx = core % width, cz = core / width;
            for (int radius = 8; radius < 30; radius++)
                for (int z = Math.Max(0, cz - radius); z <= Math.Min(height - size, cz + radius); z++)
                    for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - size, cx + radius); x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) != radius) continue;
                        int origin = z * width + x;
                        bool clear = true;
                        for (int dz = 0; dz < size && clear; dz++)
                            for (int dx = 0; dx < size; dx++)
                            {
                                int cell = origin + dz * width + dx;
                                if (!state.Map.IsPassable(cell) || state.Nodes.Any(n => state.Map.Cell(n.Definition.Position) == cell)
                                    || state.Cores.Any(c => state.Map.Cell(c.Definition.Position) == cell)) clear = false;
                            }
                        if (clear) return origin;
                    }
            Assert.Fail("clear origin not found");
            return -1;
        }

        private static SimPoint Center(Battle simulation, int origin, BuildingKind kind)
            => (SimPoint)Method("FootprintCenter").Invoke(simulation, new object[] { origin, Size(simulation, kind) });

        private static void SetEnemy(Battle simulation, SimPoint point, bool visible)
        {
            var state = State(simulation);
            for (int i = 0; i < state.Soldiers.Length; i++)
                if (state.Soldiers[i].Alive && state.Soldiers[i].Initial.FactionId == 2)
                {
                    state.Soldiers[i].Position = point;
                    state.Soldiers[i].MoveGoal = point;
                    break;
                }
            Array.Fill(state.Factions[0].VisibleCells, visible);
            Method("PrepareAiAvoidEnemySnapshot").Invoke(simulation, new object[] { 1u });
        }

        private static int Place(Battle simulation, int origin)
        {
            var state = State(simulation);
            int before = state.BuildingCount;
            typeof(Battle).GetField("automaticEconomyAction", Hidden).SetValue(simulation, true);
            typeof(Battle).GetMethods(Hidden).Single(m => m.Name == "PlaceBuildingAt" && m.GetParameters().Length == 5)
                .Invoke(simulation, new object[] { 1u, BuildingKind.House, origin, Facing.North, 0u });
            typeof(Battle).GetField("automaticEconomyAction", Hidden).SetValue(simulation, false);
            return state.BuildingCount - before;
        }

        [Test]
        public void AVisibleEnemyWithin24MetersBlocksPlacementWithoutWood()
        {
            var simulation = new Battle(Scenario(true));
            int origin = ClearOrigin(simulation, BuildingKind.House);
            var state = State(simulation);
            SetEnemy(simulation, Center(simulation, origin, BuildingKind.House), true);
            int wood = state.Economies[0].Wood;
            Assert.That(Place(simulation, origin), Is.EqualTo(0));
            Assert.That(state.Economies[0].Wood, Is.EqualTo(wood));
        }

        [Test]
        public void BInvisibleEnemyDoesNotBlockPlacement()
        {
            var simulation = new Battle(Scenario(true));
            int origin = ClearOrigin(simulation, BuildingKind.House);
            SetEnemy(simulation, Center(simulation, origin, BuildingKind.House), false);
            Assert.That(Place(simulation, origin), Is.EqualTo(1));
        }

        [Test]
        public void CEnemyBeyond24MetersDoesNotBlockPlacement()
        {
            var simulation = new Battle(Scenario(true));
            int origin = ClearOrigin(simulation, BuildingKind.House);
            var state = State(simulation);
            var centre = Center(simulation, origin, BuildingKind.House);
            SimPoint far = state.Map.Center(state.Map.Cell(centre));
            for (int cell = 0; cell < state.Config.Map.WidthCells * state.Config.Map.HeightCells; cell++)
            {
                var candidate = state.Map.Center(cell);
                long dx = candidate.X.Raw - centre.X.Raw, dz = candidate.Z.Raw - centre.Z.Raw;
                if (dx * dx + dz * dz > Fix64.FromInt(24).Raw * Fix64.FromInt(24).Raw) { far = candidate; break; }
            }
            SetEnemy(simulation, far, true);
            Assert.That(Place(simulation, origin), Is.EqualTo(1));
        }

        [Test]
        public void DProcessingSiteSkipsDangerousCandidatesAndFindsSafeSite()
        {
            var simulation = new Battle(Scenario(true));
            var state = State(simulation);
            var core = state.Cores[0].Definition.Position;
            SetEnemy(simulation, core, true);
            int site = (int)Method("FindProcessingSite").Invoke(simulation,
                new object[] { 1u, state.Map.Cell(core), Size(simulation, BuildingKind.House) });
            Assert.That(site, Is.GreaterThanOrEqualTo(0));
            var centre = Center(simulation, site, BuildingKind.House);
            long dx = centre.X.Raw - core.X.Raw, dz = centre.Z.Raw - core.Z.Raw;
            Assert.That(dx * dx + dz * dz, Is.GreaterThan(Fix64.FromInt(24).Raw * Fix64.FromInt(24).Raw));
        }

        [Test]
        public void EAutomaticReservationRemainsUntilEnemyLeaves()
        {
            var scenario = Scenario(true, true);
            scenario.Economy.AiReasons = true;
            var simulation = new Battle(scenario);
            var state = State(simulation);
            int origin = ClearOrigin(simulation, BuildingKind.House);
            SetEnemy(simulation, Center(simulation, origin, BuildingKind.House), true);
            typeof(Battle).GetField("automaticEconomyAction", Hidden).SetValue(simulation, true);
            Method("CreateBuildingReservation").Invoke(simulation, new object[]
                { 1u, BuildingKind.House, origin, Facing.North, 0u, null, -1, 0u, 0u, default(Fix64), 0, 0u });
            typeof(Battle).GetField("automaticEconomyAction", Hidden).SetValue(simulation, false);
            // An automatic reservation is recognised from its saved reason, as after a load.
            for (int i = 0; i < state.BuildingCount; i++)
                if (state.Buildings[i].Alive && state.Buildings[i].Reserved)
                {
                    state.Buildings[i].AiReason = AiReason.PopulationCap;
                    state.Buildings[i].Held = false;
                }
            state.Tick = 1;
            Method("AdvanceBuildingReservations").Invoke(simulation, null);
            Assert.That(state.Buildings.Any(b => b.Alive && b.Reserved), Is.True);
            SetEnemy(simulation, state.Map.Center(0), true);
            state.Tick = 2;
            Method("AdvanceBuildingReservations").Invoke(simulation, null);
            Assert.That(state.Buildings.Any(b => b.Alive && !b.Reserved && b.Kind == BuildingKind.House), Is.True);
        }

        [Test]
        public void FExtension101RoundTripsAndOffAddsNoBytes()
        {
            var on = Scenario(true);
            byte[] bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.AiAvoidEnemy, Is.True);
            Assert.That(decoded.Economy.AiAvoidEnemyMeters, Is.EqualTo(24));
            Assert.That(decoded.Extensions.Single(e => e.Id == 101).Data.Length, Is.EqualTo(8));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var off = Scenario(false);
            Assert.That(off.Extensions.Any(e => e.Id == 101), Is.False);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(Scenario(false))));
        }
    }
}
