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
    /// <summary>
    /// technical-design-v3 27 (V3-4 PR3): what each civilisation changes - the price and pace of infantry, forged
    /// infantry for metallurgy, and farms for the agrarian civilisation. Runs cross the advancing time and many
    /// training and farming cycles.
    /// </summary>
    public sealed class CivTests
    {
        private const int Width = 128;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        /// <summary>The west advances at once into <paramref name="civ"/> (plenty of stock); its economy stays automatic.</summary>
        private static (ScenarioDefinition s, Battle sim, CommandGateway gateway) Advanced(ulong seed, CivKind civ)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.StartFood = 3000; s.Economy.StartWood = 3000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, civ));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            Assert.That(Fields(sim)["Economy[1].Civ"], Is.EqualTo(((byte)civ).ToString(CultureInfo.InvariantCulture)));
            return (s, sim, gateway);
        }

        [Test]
        public void EachCivilisationPricesInfantryItsOwnWay()
        {
            var primitive = new Battle(MapGenerator.GenerateTerrain(1)).Capture(1).Economy;
            Assert.That((primitive.InfantryFoodCost, primitive.InfantryWoodCost, primitive.InfantryMetalCost), Is.EqualTo((50, 20, 0)));
            var agrarian = Advanced(1, CivKind.Agrarian).sim.Capture(1).Economy;
            Assert.That((agrarian.InfantryFoodCost, agrarian.InfantryWoodCost, agrarian.InfantryMetalCost), Is.EqualTo((35, 10, 0)));
            var metallurgy = Advanced(1, CivKind.Metallurgy).sim.Capture(1).Economy;
            Assert.That((metallurgy.InfantryFoodCost, metallurgy.InfantryWoodCost, metallurgy.InfantryMetalCost), Is.EqualTo((50, 20, 5)));
        }

        [Test]
        public void MetallurgyTrainsForgedInfantryAndAgrarianTrainsOrdinaryOnesFaster()
        {
            foreach (var civ in new[] { CivKind.Metallurgy, CivKind.Agrarian })
            {
                var (s, sim, gateway) = Advanced(5, civ);
                Steps(gateway, sim, 9000);
                var f = Fields(sim);
                var trained = Enumerable.Range(s.Soldiers.Length + 1, (int)Number(f, "NextSoldierId") - s.Soldiers.Length - 1)
                    .Where(id => f["Soldiers[" + id + "].FactionId"] == "1").ToArray();
                TestContext.WriteLine(civ + ": west trained " + trained.Length);
                Assert.That(trained.Length, Is.GreaterThan(0), civ + " trains infantry");
                // Soldiers queued while the west was still primitive (the first 1200 ticks) come out ordinary.
                int forged = trained.Count(id => Number(f, "Soldiers[" + id + "].Parameters.Hp") == s.Economy.ForgedInfantryHp
                    && Number(f, "Soldiers[" + id + "].Parameters.Damage") == s.Economy.ForgedInfantryDamage);
                int ordinary = trained.Count(id => Number(f, "Soldiers[" + id + "].Parameters.Hp") == 100 && Number(f, "Soldiers[" + id + "].Parameters.Damage") == 10);
                TestContext.WriteLine(civ + ": forged " + forged + ", ordinary " + ordinary);
                Assert.That(forged + ordinary, Is.EqualTo(trained.Length), "every trained soldier is one or the other");
                if (civ == CivKind.Metallurgy) Assert.That(forged, Is.GreaterThan(0), "metallurgy forges its infantry");
                else Assert.That(forged, Is.EqualTo(0), "farmers never forge");
                // The east stayed primitive or chose for itself; its soldiers are never forged by the west's choice.
            }
        }

        /// <summary>Strength the west trained by tick 12000 as <paramref name="civ"/>: HP times damage of each soldier it made.</summary>
        private static long Power(ulong seed, CivKind civ)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.StartFood = 800; s.Economy.StartWood = 600;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, civ));
            Steps(gateway, sim, 12000);
            var f = Fields(sim);
            long power = 0;
            for (long id = s.Soldiers.Length + 1; id < Number(f, "NextSoldierId"); id++)
                if (f["Soldiers[" + id + "].FactionId"] == "1")
                    power += Number(f, "Soldiers[" + id + "].Parameters.Hp") * Number(f, "Soldiers[" + id + "].Parameters.Damage");
            return power;
        }

        /// <summary>
        /// Gate 1 of V3-4 (25): on one ground farming makes the west stronger, on another metallurgy does. Seed 4 puts the
        /// west by a river, seed 3 by a mountain (measured over 8 seeds: 27.2, 32.4).
        /// </summary>
        [Test]
        public void TheCivilisationThatPaysDependsOnTheGround()
        {
            long riverFarm = Power(4, CivKind.Agrarian), riverMetal = Power(4, CivKind.Metallurgy);
            long hillFarm = Power(3, CivKind.Agrarian), hillMetal = Power(3, CivKind.Metallurgy);
            TestContext.WriteLine("river (seed 4): farming " + riverFarm + ", metallurgy " + riverMetal + "; mountain (seed 3): farming " + hillFarm + ", metallurgy " + hillMetal);
            Assert.That(riverFarm, Is.GreaterThan(riverMetal), "by the river, farming pays");
            Assert.That(hillMetal, Is.GreaterThan(hillFarm), "by the mountain, metallurgy pays");
        }

        [Test]
        public void AFarmMakesFoodAtThePaceOfItsGroundAndOnlyForFarmers()
        {
            // A river-leaning west (seed 7): its food and river make farms quick.
            var metal = Advanced(7, CivKind.Metallurgy);
            var s = metal.s;
            int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / 2) * Width + (int)(s.Cores[0].Position.X.Raw / 65536 / 2);
            var ring = new List<int>();
            int cx = core % Width, cz = core / Width;
            for (int r = 5; r <= 14; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r && cx + dx >= 0 && cz + dz >= 0 && cx + dx < Width - 2 && cz + dz < 62) ring.Add((cz + dz) * Width + cx + dx);
            metal.gateway.SubmitEconomy(EconomyCommand.Auto(1, 2, false));
            foreach (int origin in ring.Take(40)) metal.gateway.SubmitEconomy(EconomyCommand.Place(1, 3, BuildingKind.Farm, origin, Facing.North));
            Steps(metal.gateway, metal.sim, 1);
            var mf = Fields(metal.sim);
            Assert.That(Enumerable.Range(1, (int)Number(mf, "Buildings.Count")).Count(b => mf["Buildings[" + b + "].Kind"] == "4"), Is.EqualTo(0), "no farms for metallurgy");

            var farm = Advanced(7, CivKind.Agrarian);
            farm.gateway.SubmitEconomy(EconomyCommand.Auto(1, 2, false));
            uint id = 0;
            foreach (int origin in ring)
            {
                long count = Number(Fields(farm.sim), "Buildings.Count");
                farm.gateway.SubmitEconomy(EconomyCommand.Place(1, 3, BuildingKind.Farm, origin, Facing.North));
                Steps(farm.gateway, farm.sim, 1);
                var f0 = Fields(farm.sim);
                if (Number(f0, "Buildings.Count") > count && f0["Buildings[" + (count + 1) + "].Kind"] == "4") { id = (uint)(count + 1); break; }
            }
            Assume.That(id, Is.Not.EqualTo(0u), "a farm site near the west core");
            var f = Fields(farm.sim);
            long interval = Number(f, "Buildings[" + id + "].Interval");
            // The same rule, counted here from the map alone.
            int origin0 = (int)Number(f, "Buildings[" + id + "].OriginCell");
            double fx = (origin0 % Width) * 2 + 2, fz = (origin0 / Width) * 2 + 2;
            int steps = s.ResourceNodes.Count(n => n.Kind == ResourceKind.Food
                && Math.Pow(n.Position.X.Raw / 65536.0 - fx, 2) + Math.Pow(n.Position.Z.Raw / 65536.0 - fz, 2) <= 16 * 16);
            bool river = Enumerable.Range(0, s.Map.Terrain.Length).Any(c => s.Map.Terrain[c] == (byte)TerrainKind.River
                && Math.Pow((c % Width) * 2 + 1 - fx, 2) + Math.Pow((c / Width) * 2 + 1 - fz, 2) <= 8 * 8);
            Assert.That(interval, Is.EqualTo(Math.Max(20, 60 - 10 * (steps + (river ? 1 : 0)))));

            farm.gateway.SubmitEconomy(EconomyCommand.Assign(1, 4, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int t = 0; t < 3000 && Fields(farm.sim)["Buildings[" + id + "].Complete"] != "1"; t += 20) Steps(farm.gateway, farm.sim, 20);
            Assert.That(Fields(farm.sim)["Buildings[" + id + "].Complete"], Is.EqualTo("1"));
            Steps(farm.gateway, farm.sim, (int)interval * 5 + 1);
            Assert.That(Number(Fields(farm.sim), "Buildings[" + id + "].Output"), Is.EqualTo(5), "one food per interval, no belt yet");

            // Carried by hand to the core, the food reaches the stock.
            long food = Number(Fields(farm.sim), "Economy[1].Food");
            farm.gateway.SubmitEconomy(EconomyCommand.Assign(1, 5, new uint[] { 1 }, EconomyTargetKind.Building, id));
            Steps(farm.gateway, farm.sim, 1500);
            Assert.That(Number(Fields(farm.sim), "Economy[1].Food"), Is.GreaterThan(food));

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, farm.s, farm.gateway.Inputs, farm.sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }
    }
}
