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
    public sealed class MetalLineFixTests
    {
        [Test]
        public void Extension130RoundTripsAndFlagOffOmitsIt()
        {
            var enabled = LiveGameRules.Create(1301UL, true, true, false, false);
            Assert.That(enabled.Economy.MetalLineFix, Is.True);
            var bytes = ScenarioBinary.Encode(enabled);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(extension => extension.Id == 130).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.MetalLineFix, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            enabled.Economy.MetalLineFix = false;
            var off = ScenarioBinary.Decode(ScenarioBinary.Encode(enabled));
            Assert.That(off.Economy.MetalLineFix, Is.False);
            Assert.That(off.Extensions.Any(extension => extension.Id == 130), Is.False);
        }

        [Test]
        public void FlagOffKeepsEveryTickHashStable()
        {
            var left = LiveGameRules.Create(1302UL, true, true, false, false);
            var right = LiveGameRules.Create(1302UL, true, true, false, false);
            left.Economy.MetalLineFix = false;
            right.Economy.MetalLineFix = false;
            var a = new Battle(left);
            var b = new Battle(right);
            for (long tick = 1; tick <= 500; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void LiveLargeMapEnablesMetalLineFix()
        {
            Assert.That(LiveGameRules.Create(1303UL, true, true, false, false).Economy.MetalLineFix, Is.True);
            Assert.That(LiveGameRules.Create(1303UL, false, true, false, false).Economy.MetalLineFix, Is.False);
        }

        [Test]
        public void LiveLargeMapFactoryCanBuildItsSmelter()
        {
            var scenario = LiveGameRules.Create(1305UL, true, true, false, false);
            scenario.Economy.MetalLineFix = true;
            var simulation = new Battle(scenario);
            var world = (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            var economy = world.Economies[0];
            economy.Age = 2;
            economy.Wood = economy.Stone = economy.Planks = economy.Bricks = economy.Frames = economy.Fittings = economy.Rope = economy.Glass = economy.SiegeParts = 1000;
            world.Economies[0] = economy;
            int mineCell = FindOpenRun(world, 3);
            var mine = new BuildingState { Id = 1, FactionId = 1, Kind = BuildingKind.Mine,
                OriginCell = mineCell, WorkCell = mineCell, Alive = true, Complete = true };
            var placeSmelter = typeof(Battle).GetMethod("PlaceSmelter", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(uint), typeof(BuildingState), typeof(bool) }, null);
            uint smelterId = (uint)placeSmelter.Invoke(simulation, new object[] { 1U, mine, false });
            Assert.That(smelterId, Is.GreaterThan(0));
            Assert.That(world.Buildings[smelterId - 1].Kind,
                Is.EqualTo(BuildingKind.Smelter).Or.EqualTo(BuildingKind.BlastFurnace));
        }

        [Test]
        public void AffordableMetalRouteStartsAtTheDestinationSide()
        {
            var scenario = LiveGameRules.Create(1304UL, true, true, false, false);
            scenario.Economy.MetalLineFix = true;
            scenario.Economy.Wards = false;
            var simulation = new Battle(scenario);
            var world = (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            int width = world.Config.Map.WidthCells;
            int start = FindOpenRun(world, 24);
            int[] route = Enumerable.Range(start, 24).ToArray();
            var facings = Enumerable.Repeat(Facing.East, route.Length).ToArray();
            var economy = world.Economies[0];
            economy.Wood = world.Config.Economy.BeltWoodCost * 2;
            world.Economies[0] = economy;
            var method = typeof(Battle).GetMethod("LayRoutesWithReason", BindingFlags.Instance | BindingFlags.NonPublic);
            var result = (bool)method.Invoke(simulation, new object[]
            {
                1U, AiReason.ProcessingBelt, 0, new[] { (route, facings) }
            });
            Assert.That(result, Is.False);
            Assert.That(world.Belts[route[23]].FactionId, Is.EqualTo(1U));
            Assert.That(world.Belts[route[22]].FactionId, Is.EqualTo(1U));
            Assert.That(world.Belts[route[0]].FactionId, Is.EqualTo(0U));
            Assert.That(width, Is.GreaterThan(0));
        }

        private static int FindOpenRun(WorldState world, int length)
        {
            int width = world.Config.Map.WidthCells;
            int height = world.Config.Map.HeightCells;
            for (int z = 2; z < height - 2; z++)
                for (int x = 2; x + length < width - 2; x++)
                {
                    bool free = true;
                    for (int dx = 0; dx < length; dx++)
                        if (!world.Map.IsPassable(z * width + x + dx)) { free = false; break; }
                    if (free) return z * width + x;
                }
            throw new InvalidOperationException("no passable run");
        }
    }
}
