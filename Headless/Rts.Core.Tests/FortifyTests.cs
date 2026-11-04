using System;
using System.Collections.Generic;
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
    public sealed class FortifyTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(ulong seed = 1)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.Fortify = true;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            scenario.Economy.StartStone = 1000;
            scenario.Economy.GatherIntervalTicks = 1000000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.RepairCostPermille = 500;
            return scenario;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static (Battle simulation, CommandGateway gateway) PlaceWalls(ulong seed, int count = 2)
        {
            var scenario = Scenario(seed);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            int width = scenario.Map.WidthCells;
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            var cells = new List<int>();
            for (int d = 3; d < 10 && cells.Count < count; d++)
            {
                int candidate = core + d;
                if (candidate >= 0 && candidate < scenario.Map.WidthCells * scenario.Map.HeightCells
                    && !scenario.Map.BlockedCellIds.Contains(candidate)) cells.Add(candidate);
            }
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(1, 3, cells));
            gateway.Step();
            return (simulation, gateway);
        }

        [Test]
        public void Extension93RoundTripsAndOffAddsNoBytes()
        {
            var on = Scenario();
            on.Economy.RepairFirstMilliHp = 1400;
            on.Economy.GateHp = 777;
            byte[] bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Fortify, Is.True);
            Assert.That(decoded.Economy.GateHp, Is.EqualTo(777));
            Assert.That(decoded.Extensions.Single(value => value.Id == 93).Data.Length, Is.EqualTo(40));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var off = MapGenerator.GenerateTerrain(1);
            var baseline = MapGenerator.GenerateTerrain(1);
            off.Economy.Fortify = false;
            off.Economy.GateHp = 777;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
        }

        [Test]
        public void GatePassesOwnerAndBlocksEnemyAfterWallReplacementButOwnWallsBlockEveryone()
        {
            var placed = PlaceWalls(1, 2);
            var state = State(placed.simulation);
            Assert.That(state.BuildingCount, Is.GreaterThanOrEqualTo(2));
            int gateCell = state.Buildings[0].OriginCell, wallCell = state.Buildings[1].OriginCell;
            placed.gateway.SubmitEconomy(EconomyCommand.PlaceGate(1, 4, gateCell));
            placed.gateway.Step();
            Assert.That(placed.simulation.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Gate), Is.EqualTo(1));
            Assert.That(state.Map.IsPassableFor(gateCell, 1), Is.True);
            Assert.That(state.Map.IsPassableFor(gateCell, 2), Is.False);
            // Review (11-04): the overlay used to open the owner's walls as well as its gates.
            Assert.That(state.Map.IsPassableFor(wallCell, 1), Is.False);
            Assert.That(state.Map.IsPassableFor(wallCell, 2), Is.False);
        }

        private static List<int> Ring(WorldState state, uint faction, int radius)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int core = state.Map.Cell(state.Cores[state.Factions[faction - 1].CoreId - 1].Definition.Position);
            int cx = core % width, cz = core / width;
            var cells = new List<int>();
            for (int z = Math.Max(0, cz - radius); z <= Math.Min(height - 1, cz + radius); z++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) == radius) cells.Add(z * width + x);
            return cells;
        }

        private static bool Reaches(WorldState state, uint faction, uint targetFaction)
        {
            var from = state.Cores[state.Factions[faction - 1].CoreId - 1].Definition.Position;
            var to = state.Cores[state.Factions[targetFaction - 1].CoreId - 1].Definition.Position;
            int[] path = state.Map.FindPath(state.Map.Cell(from), to, faction);
            return path.Length > 0 && path[path.Length - 1] == state.Map.Cell(to);
        }

        /// <summary>Gate first, then the rest of the ring; returns the ring cells that stay open (should be none).</summary>
        private static List<int> EncloseCore(Battle simulation, CommandGateway gateway, uint faction, int radius, ref ulong sequence)
        {
            var state = State(simulation);
            var ring = Ring(state, faction, radius);
            int width = state.Config.Map.WidthCells;
            int core = state.Map.Cell(state.Cores[state.Factions[faction - 1].CoreId - 1].Definition.Position);
            int gate = ring.First(cell => cell % width == core % width && state.Map.IsPassable(cell));
            gateway.SubmitEconomy(EconomyCommand.PlaceGate(faction, ++sequence, gate));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(faction, ++sequence, ring.Where(cell => cell != gate).ToList()));
            gateway.Step();
            return ring.Where(cell => state.Map.IsPassable(cell)).ToList();
        }

        [Test]
        public void AGatedRingClosesTheCoreToTheEnemyOnlyAndAWallOnlyRingIsRefused()
        {
            var scenario = Scenario(2);
            scenario.Economy.StartStone = 100000;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.Step();
            var state = State(simulation);
            ulong sequence = 10;
            var open = EncloseCore(simulation, gateway, 2, 6, ref sequence);
            Assert.That(open, Is.Empty, "ring cells left open");
            Assert.That(Reaches(state, 2, 1), Is.True, "the owner walks out through its gate");
            Assert.That(Reaches(state, 1, 2), Is.False, "the enemy is shut out");
            // Without a gate the owner would shut itself in: at least one cell is refused.
            var ring = Ring(state, 1, 6);
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(1, ++sequence, ring));
            gateway.Step();
            Assert.That(ring.Count(cell => state.Map.IsPassable(cell)), Is.GreaterThan(0));
        }

        [Test]
        public void BreakablePathIsASeparateFallback()
        {
            var map = new GridMap(new MapDefinition { WidthCells = 5, HeightCells = 3, WidthMeters = 10, HeightMeters = 6,
                CellSizeMeters = 2, DefaultPassable = true, BlockedCellIds = new[] { 2 } });
            map.BreakablePassability = (cell, faction) => cell == 2 && faction == 1;
            int[] normal = map.FindPath(0, map.Center(4), 1);
            int[] breach = map.FindBreakablePath(0, map.Center(4), 1);
            Assert.That(normal, Does.Not.Contain(2));
            Assert.That(breach, Does.Contain(2));
        }

        /// <summary>11-04 review: an army sent at an enclosed core breaks through the ring and goes on.</summary>
        [Test]
        public void AnArmySentAtAnEnclosedCoreBreaksThroughTheRing()
        {
            var scenario = LiveGameRules.Create(40041UL, true, true, false, false);
            Assert.That(scenario.Economy.Fortify, Is.True);
            scenario.Economy.AiWallStoneReserve = 1000000;
            scenario.Economy.StartStone = 100000;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.Step();
            ulong sequence = 100;
            var open = EncloseCore(simulation, gateway, 2, 6, ref sequence);
            var state = State(simulation);
            Assert.That(open, Is.Empty, "ring cells left open");
            Assert.That(Reaches(state, 1, 2), Is.False);
            uint army = scenario.Factions[0].ArmyIds[0];
            uint core = state.Cores[state.Factions[1].CoreId - 1].Definition.Id;
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, army), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, core, default), 50, new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 0; i < 12000 && !simulation.Capture(1).Result.HasEnded && state.Economies[1].FortifyDestroyed == 0; i++)
                gateway.Step();
            Assert.That(state.Economies[0].FortifyBreachUses, Is.GreaterThan(0), "breach routes used");
            Assert.That(state.Economies[1].FortifyDestroyed, Is.GreaterThan(0), "ring broken");
            Assert.That(state.Economies[1].FortifyCollapsed, Is.GreaterThan(0), "neighbours fell with it");
        }

        private static int WallIndex(WorldState state)
        {
            for (int i = 0; i < state.BuildingCount; i++)
                if (state.Buildings[i].Alive && state.Buildings[i].Kind == BuildingKind.Wall) return i;
            return -1;
        }

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(8, 3)]
        public void RepairUsesSortedVillagersAndChargesMaterials(int workers, int expectedHpGain)
        {
            var placed = PlaceWalls(3, 1);
            var state = State(placed.simulation);
            int wall = WallIndex(state);
            Assert.That(wall, Is.GreaterThanOrEqualTo(0));
            ref var building = ref state.Buildings[wall];
            int max = building.Hp;
            building.Hp = max - 100;
            for (int i = 0; i < workers && i < state.VillagerCount; i++)
            {
                state.Villagers[i].Alive = true;
                state.Villagers[i].Task = VillagerTask.Building;
                state.Villagers[i].BuildingId = building.Id;
            }
            state.Economies[0].Wood = 1000;
            state.Economies[0].Stone = 1000;
            int before = building.Hp;
            placed.simulation.Step(2, Array.Empty<ScheduledInput>());
            int gain = state.Buildings[wall].Hp - before;
            Assert.That(gain, Is.EqualTo(expectedHpGain));
        }

        /// <summary>Review (11-04): the cost was 1000 times too high. A wall repaired by 100 HP owes stone x 100 / max / 2.</summary>
        [Test]
        public void RepairChargesHalfTheBuildCostInProportion()
        {
            var placed = PlaceWalls(3, 1);
            var state = State(placed.simulation);
            int wall = WallIndex(state);
            Assert.That(wall, Is.GreaterThanOrEqualTo(0));
            ref var building = ref state.Buildings[wall];
            int max = building.Hp, stone = building.ConstructionStone;
            Assert.That(stone, Is.GreaterThan(0));
            building.Hp = max - 100;
            for (int i = 0; i < Math.Min(8, state.VillagerCount); i++)
            {
                state.Villagers[i].Alive = true;
                state.Villagers[i].Task = VillagerTask.Building;
                state.Villagers[i].BuildingId = building.Id;
            }
            for (long tick = 2; tick < 200 && state.Buildings[wall].Hp < max; tick++)
                placed.simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(state.Buildings[wall].Hp, Is.EqualTo(max));
            long owedMilli = (long)stone * 100 * 500 / max;
            long paidMilli = state.Economies[0].FortifyRepairStone * 1000L + state.Buildings[wall].RepairStoneCostMilli;
            Assert.That(paidMilli, Is.InRange(owedMilli - 40, owedMilli));
        }

        [Test]
        public void RepairStopsWhenTheRequiredStockIsMissing()
        {
            var placed = PlaceWalls(4, 1);
            var state = State(placed.simulation);
            int wall = WallIndex(state);
            Assert.That(wall, Is.GreaterThanOrEqualTo(0));
            ref var building = ref state.Buildings[wall];
            building.Hp -= 50;
            state.Villagers[0].Task = VillagerTask.Building;
            state.Villagers[0].BuildingId = building.Id;
            state.Economies[0].Wood = 0;
            state.Economies[0].Stone = 0;
            int hp = building.Hp;
            placed.simulation.Step(2, Array.Empty<ScheduledInput>());
            Assert.That(state.Buildings[wall].Hp, Is.EqualTo(hp));
        }

        [Test]
        public void AdjacentFortificationCollapsesWithoutChainReaction()
        {
            var placed = PlaceWalls(5, 2);
            var state = State(placed.simulation);
            Assert.That(state.BuildingCount, Is.GreaterThanOrEqualTo(2));
            state.Buildings[0].Hp = 1;
            state.Buildings[1].Hp = 1;
            var damage = (long[])typeof(Battle).GetField("buildingDamage", Hidden).GetValue(placed.simulation);
            damage[0] = 10;
            typeof(Battle).GetMethod("ApplyRaidDamage", Hidden).Invoke(placed.simulation, null);
            typeof(Battle).GetMethod("ResolveRaidDeaths", Hidden).Invoke(placed.simulation, null);
            Assert.That(state.Economies[0].FortifyDestroyed, Is.EqualTo(1));
            Assert.That(state.Economies[0].FortifyCollapsed, Is.EqualTo(1));
            Assert.That(state.Buildings[1].Alive, Is.False);
        }

        [Test]
        public void FortifyOffCanonicalHashIsUnchangedEachTick()
        {
            var source = LiveGameRules.Create(40042UL, true, true, false, false);
            Assert.That(source.Economy.Fortify, Is.True);
            source.Economy.Fortify = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Any(x => x.Id == 93), Is.False);
            var a = new Battle(source); var b = new Battle(decoded);
            for (long tick = 1; tick <= 1300; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>()); b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
