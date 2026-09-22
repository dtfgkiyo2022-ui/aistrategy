using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>technical-design-v3 28 (V3-4 PR1): terrain kinds and the mapgen-3 map.</summary>
    public sealed class TerrainTests
    {
        private const int Width = 128, Height = 64;

        private static int CellOf(SimPoint p) => (int)(p.Z.Raw / 65536 / 2) * Width + (int)(p.X.Raw / 65536 / 2);

        private static long Distance2(SimPoint a, int cell)
        {
            long x = (cell % Width) * 2 + 1 - a.X.Raw / 65536, z = (cell / Width) * 2 + 1 - a.Z.Raw / 65536;
            return x * x + z * z;
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        [TestCase(4UL)]
        [TestCase(5UL)]
        [TestCase(6UL)]
        [TestCase(7UL)]
        [TestCase(8UL)]
        public void TheTerrainMapKeepsItsRules(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed, out var leans);
            Assert.That(ScenarioBinary.Encode(MapGenerator.GenerateTerrain(seed)), Is.EqualTo(ScenarioBinary.Encode(s)), "same seed, same bytes");
            var one = MapGenerator.Generate(seed, true);
            Assert.That(s.Cores.Select(c => c.Position), Is.EqualTo(one.Cores.Select(c => c.Position)), "cores drawn as on mapgen-1");
            Assert.That(s.Outposts.Select(o => o.Position), Is.EqualTo(one.Outposts.Select(o => o.Position)));

            var blocked = new bool[Width * Height];
            foreach (int c in s.Map.BlockedCellIds) blocked[c] = true;
            for (int i = 0; i < blocked.Length; i++)
                Assert.That(blocked[i], Is.EqualTo(s.Map.Terrain[i] != 0), "cell " + i + ": blocked exactly where there is terrain");
            Assert.That(s.Map.BlockedCellIds.Length * 1000 / blocked.Length, Is.LessThanOrEqualTo(250));
            Assert.That(s.Map.Terrain.Count(k => k == (byte)TerrainKind.River), Is.GreaterThan(0), "there is a river");

            var reach = MapGenerator.Reachable(blocked, CellOf(s.Cores[0].Position));
            Assert.That(reach[CellOf(s.Cores[1].Position)], "the river has a ford on the way between the cores");
            foreach (var o in s.Outposts) Assert.That(reach[CellOf(o.Position)]);
            foreach (var n in s.ResourceNodes) Assert.That(reach[CellOf(n.Position)], "resource " + n.Id + " can be walked to");
            foreach (var d in s.Soldiers) Assert.That(blocked[CellOf(d.Position)], Is.False);
            foreach (var v in s.Villagers) Assert.That(blocked[CellOf(v.Position)], Is.False);

            // Each core's lean shows in the ground next to it.
            for (int f = 0; f < 2; f++)
            {
                var core = s.Cores[f].Position;
                var kind = leans[f] == MapGenerator.CoreLean.Mountain ? TerrainKind.Mountain : TerrainKind.River;
                int outer = leans[f] == MapGenerator.CoreLean.Mountain ? 36 + 12 : 30 + 8;
                bool near = Enumerable.Range(0, s.Map.Terrain.Length).Any(c => s.Map.Terrain[c] == (byte)kind && Distance2(core, c) <= (long)outer * outer);
                Assert.That(near, "core " + (f + 1) + " leans " + leans[f]);
            }
            // Ore only at the foot of mountains.
            foreach (var n in s.ResourceNodes.Where(n => n.Kind == ResourceKind.Ore))
            {
                int c = CellOf(n.Position), x = c % Width, z = c / Width;
                bool foot = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) }.Any(d =>
                {
                    int nx = x + d.Item1, nz = z + d.Item2;
                    return nx >= 0 && nz >= 0 && nx < Width && nz < Height && s.Map.Terrain[nz * Width + nx] == (byte)TerrainKind.Mountain;
                });
                Assert.That(foot, "ore " + n.Id + " next to a mountain");
            }
            new Battle(s);
        }

        [Test]
        public void TheTerrainRoundTripsAndOlderMapsKeepTheirSchema()
        {
            var s = MapGenerator.GenerateTerrain(3);
            var bytes = ScenarioBinary.Encode(s);
            Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(5));
            var back = ScenarioBinary.Decode(bytes);
            Assert.That(back.Map.Terrain, Is.EqualTo(s.Map.Terrain));
            Assert.That(ScenarioBinary.Encode(back), Is.EqualTo(bytes));
            Assert.That(BitConverter.ToInt32(ScenarioBinary.Encode(MapGenerator.Generate(3, true, true)), 0), Is.EqualTo(4), "mapgen-2 keeps schema 4");

            // Terrain must sit on blocked cells, and needs the industry map.
            var bad = MapGenerator.GenerateTerrain(3);
            int open = Enumerable.Range(0, bad.Map.Terrain.Length).First(c => bad.Map.Terrain[c] == 0);
            bad.Map.Terrain[open] = (byte)TerrainKind.Forest;
            Assert.Throws<ArgumentException>(() => new Battle(bad));
            var plain = MapGenerator.Generate(3, true);
            plain.Map.Terrain = new byte[Width * Height];
            Assert.Throws<ArgumentException>(() => new Battle(plain));
        }

        [TestCase(1UL)]
        [TestCase(5UL)]
        public void AnAutomaticMatchOnTheTerrainRunsAndReplays(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            for (int i = 0; i < 6000 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            Assert.That(sim.Capture(1).Result.IsFault, Is.False);
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
    }
}
