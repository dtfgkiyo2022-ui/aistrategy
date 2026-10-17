using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Simulation;
using Rts.Tests.EditMode;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>Measurements for the 2026-10-18 movement follow-up.</summary>
    public sealed class MoveFix2Tests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static ScheduledInput Focus(long tick, uint army, SimPoint goal)
        {
            return new PolicyOrderInput(tick, army, goal).Input;
        }

        private sealed class PolicyOrderInput
        {
            internal ScheduledInput Input { get; }

            internal PolicyOrderInput(long tick, uint army, SimPoint goal)
            {
                var order = new PolicyOrder(1, 0, CommandSource.Human,
                    new ScopeKey(1, ScopeKind.Army, army), PolicyKind.Focus,
                    new PolicyGoal(GoalKind.Point, 0, goal), 50, new LossBudget(1000),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, 0,
                    Array.Empty<PolicyVersion>(), tick - 1,
                    new Expiration(long.MaxValue, 0, ExpireFlags.None));
                Input = new ScheduledInput(1, InputKind.Resolve, tick - 1, tick, 1, 1, new[] { order });
            }
        }

        [Test]
        public void HumanFocusStopsScoutAndArmyAfterSixHundredTicks()
        {
            foreach (uint army in new[] { 1U, 4U })
            {
                var scenario = TerrainScenario();
                var simulation = new Battle(scenario);
                var goal = army == 4 ? P(160, 96) : P(160, 64);
                CommandTestInput.Step(simulation, 1, new[] { Focus(1, army, goal) });
                var positions = new List<SimPoint>();
                for (int tick = 2; tick <= 2400; tick++)
                {
                    simulation.Step(tick, Array.Empty<ScheduledInput>());
                    var view = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == army);
                    positions.Add(view.Position);
                }

                int reversals = 0;
                long total = 0;
                int previous = 0;
                for (int i = 1; i < positions.Count; i++)
                {
                    long dx = positions[i].PositionDelta(positions[i - 1]);
                    total = checked(total + dx);
                    int direction = CompareDistance(positions[i], positions[i - 1]);
                    if (direction != 0 && previous != 0 && direction != previous) reversals++;
                    if (direction != 0) previous = direction;
                }
                var command = simulation.Capture(1).Commands.SingleOrDefault(c => c.Target.Kind == ScopeKind.Army && c.Target.Id == army);
                var world = typeof(Battle).GetField("world", Hidden).GetValue(simulation);
                var armies = (Array)world.GetType().GetField("Armies", Hidden).GetValue(world);
                var armyState = armies.GetValue((int)army - 1);
                bool impossible = (bool)armyState.GetType().GetField("PathImpossible", Hidden).GetValue(armyState);
                var path = (int[])armyState.GetType().GetField("Path", Hidden).GetValue(armyState);
                int cursor = (int)armyState.GetType().GetField("PathCursor", Hidden).GetValue(armyState);
                long tail = 0;
                for (int i = Math.Max(1, positions.Count - 600); i < positions.Count; i++) tail += positions[i].PositionDelta(positions[i - 1]);
                if (army == 4)
                {
                    Assert.That(tail, Is.LessThanOrEqualTo(2L * 65536), "Scout kept moving after the 600-tick observation window.");
                    var final = positions[positions.Count - 1];
                    Assert.That(Squared(final, goal), Is.LessThanOrEqualTo(65536L * 65536L), "Scout did not settle at the human Focus destination.");
                }
                else
                {
                    Assert.That(tail, Is.LessThanOrEqualTo(2L * 65536), $"Army {army} kept moving after the 600-tick observation window.");
                    Assert.That(Squared(positions[positions.Count - 1], goal), Is.LessThanOrEqualTo(65536L * 65536L), $"Army {army} did not settle at the human Focus destination.");
                }
                TestContext.WriteLine($"army={army} totalRaw={total} tailRaw={tail} reversals={reversals} command={command.Status}/{command.Reason} path={cursor}/{path.Length} impossible={impossible}");
            }
        }

        [Test]
        public void CrowdedDestinationDoesNotMakeTheArmyOscillate()
        {
            var scenario = TerrainScenario();
            CrowdDestination(scenario, 1, P(160, 64));
            var simulation = new Battle(scenario);
            var goal = P(160, 64);
            CommandTestInput.Step(simulation, 1, new[] { Focus(1, 1, goal) });
            var positions = new List<SimPoint>();
            for (int tick = 2; tick <= 2400; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                positions.Add(simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position);
            }
            long tail = 0;
            for (int i = Math.Max(1, positions.Count - 600); i < positions.Count; i++) tail += positions[i].PositionDelta(positions[i - 1]);
            Assert.That(tail, Is.LessThanOrEqualTo(2L * 65536), "Army oscillated after reaching a crowded destination.");
        }

        [Test]
        public void HumanPointMoveArrowHidesOnlyAfterArrival()
        {
            var goal = P(100, 100);
            var command = new CommandView(1, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, goal), CommandStatus.Executing, 1, 1, ReasonCode.None);
            Assert.That(BattlefieldArrowRules.ShouldShowMovementArrow(command, P(95, 100)), Is.True);
            Assert.That(BattlefieldArrowRules.ShouldShowMovementArrow(command, P(97, 100)), Is.False);

            var attack = new CommandView(2, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, 2, default(SimPoint)), CommandStatus.Executing, 1, 1, ReasonCode.None);
            Assert.That(BattlefieldArrowRules.ShouldShowMovementArrow(attack, P(100, 100)), Is.True);
        }

        [Test]
        public void DirectHumanCommandStartsMovingAtItsFixedTransmissionTick()
        {
            var scenario = TerrainScenario();
            var baseline = new Battle(scenario);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var goal = P(80, 32);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, goal), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));

            long firstChanged = 0;
            for (int i = 0; i < 80; i++)
            {
                baseline.Step(baseline.Capture(1).Tick + 1, Array.Empty<ScheduledInput>());
                gateway.Step();
                var position = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position;
                var baselinePosition = baseline.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position;
                if (firstChanged == 0 && position.PositionDelta(baselinePosition) != 0) firstChanged = simulation.Capture(1).Tick;
            }

            var command = simulation.Capture(1).Commands.SingleOrDefault(c => c.Target.Kind == ScopeKind.Army && c.Target.Id == 1);
            TestContext.WriteLine($"firstChanged={firstChanged} command={command.Status}/{command.Reason} apply={command.ApplyTick} accepted={command.AcceptedTick}");
            Assert.That(firstChanged, Is.EqualTo(44), "Direct human Focus should begin moving four ticks after the fixed ApplyTick 40.");
        }

        private static ScenarioDefinition TerrainScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Enabled = true;
            scenario.Economy.DirectPathMovement = true;
            scenario.Rules.CoreReinforcementIntervalTicks = int.MaxValue;
            scenario.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            for (int i = 0; i < scenario.UnitParameters.Length; i++) scenario.UnitParameters[i].Damage = 0;
            return scenario;
        }

        private static void CrowdDestination(ScenarioDefinition scenario, uint army, SimPoint goal)
        {
            for (int i = 0; i < scenario.Soldiers.Length; i++)
            {
                var soldier = scenario.Soldiers[i];
                if (soldier.FactionId == 1 && soldier.ArmyId != army) { soldier.Position = goal; scenario.Soldiers[i] = soldier; }
            }
        }

        private static int CompareDistance(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw;
            long dz = a.Z.Raw - b.Z.Raw;
            return dx == 0 && dz == 0 ? 0 : dx != 0 ? Math.Sign(dx) : Math.Sign(dz);
        }

        private static long Squared(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw;
            long dz = a.Z.Raw - b.Z.Raw;
            return checked(dx * dx + dz * dz);
        }
    }

    internal static class SimPointMeasurementExtensions
    {
        internal static long PositionDelta(this SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw;
            long dz = a.Z.Raw - b.Z.Raw;
            return Math.Abs(dx) + Math.Abs(dz);
        }
    }
}
