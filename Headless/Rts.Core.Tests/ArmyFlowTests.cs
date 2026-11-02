using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmyFlowTests
    {
        private static MapDefinition Map(int width = 8, int height = 8)
            => new MapDefinition { WidthCells = width, HeightCells = height, WidthMeters = width * 2,
                HeightMeters = height * 2, CellSizeMeters = 2, DefaultPassable = true, BlockedCellIds = Array.Empty<int>() };

        [Test]
        public void EightDirectionRouteUsesDiagonalsAndDoesNotCutCorners()
        {
            var map = new GridMap(Map());
            var route = map.FindArmyFlowPath(0, map.Center(63), 1, 16);
            Assert.That(route.Length, Is.LessThan(15));
            for (int i = 1; i < route.Length; i++)
            {
                int before = route[i - 1], after = route[i];
                Assert.That(map.ClipMove(map.Center(before), map.Center(after)), Is.EqualTo(map.Center(after)));
            }

            var blocked = Map(3, 3); blocked.BlockedCellIds = new[] { 1, 3 };
            var trapped = new GridMap(blocked).FindArmyFlowPath(0, new GridMap(blocked).Center(8), 1, 16);
            Assert.That(trapped.Length, Is.EqualTo(1));
        }

        [Test]
        public void FormationWidthUsesIntegerCeilingSquareRoot()
        {
            var method = typeof(Battle).GetMethod("ArmyFlowWidth", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That((int)method.Invoke(null, new object[] { 64, 16 }), Is.EqualTo(8));
            Assert.That((int)method.Invoke(null, new object[] { 65, 16 }), Is.EqualTo(9));
            Assert.That((int)method.Invoke(null, new object[] { 300, 16 }), Is.EqualTo(16));
        }

        [Test]
        public void Extension90RoundTripsAndIsAbsentWhenOff()
        {
            var source = LiveGameRules.Create(901UL, true, true, false, false);
            source.Economy.ArmyFlowQuorumPermille = 751; source.Economy.ArmyFlowWaitTicks = 61;
            source.Economy.ArmyFlowMaxWidth = 15; source.Economy.ArmyFlowLookahead = 17;
            source.Economy.ArmyFlowRegoalMeters = 9;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Single(e => e.Id == 90).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.ArmyFlow, Is.True);
            Assert.That(decoded.Economy.ArmyFlowQuorumPermille, Is.EqualTo(751));
            Assert.That(decoded.Economy.ArmyFlowLookahead, Is.EqualTo(17));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(source)));
            source.Economy.ArmyFlow = false;
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(e => e.Id == 90), Is.False);
        }

        [Test]
        public void FlagOffIsHashStableForEveryTick()
        {
            var source = LiveGameRules.Create(902UL, true, true, false, false);
            source.Economy.ArmyFlow = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source); var right = new Battle(decoded);
            for (long tick = 1; tick <= 100; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

    }
}
