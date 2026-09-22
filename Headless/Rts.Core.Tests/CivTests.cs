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
        private static (ScenarioDefinition s, Battle sim, CommandGateway gateway) Advanced(ulong seed, CivKind civ, bool widenReserve = false)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            // Room for the trained units even after the automatic economy fills the usual armies.
            if (widenReserve) s.Armies[2].Capacity = 26;
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
                // Research may add weapons and armour on top (32 #6); forged soldiers start at ForgedInfantryHp, ordinary ones at 100.
                // By damage, not HP: armour (+20) lifts an ordinary soldier to the forged HP, while weapons (+2) leaves
                // ordinary damage under the forged one (32.9, this is what made the test read farmers as forging).
                int forged = trained.Count(id => Number(f, "Soldiers[" + id + "].Parameters.Damage") >= s.Economy.ForgedInfantryDamage);
                int ordinary = trained.Count(id => Number(f, "Soldiers[" + id + "].Parameters.Damage") < s.Economy.ForgedInfantryDamage);
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
        /// Gate 1 of V3-4 (25): on one ground farming makes the west stronger, on another metallurgy does. Measured over
        /// 8 seeds again after the wood floor (32.11): seed 2 favours farming, seed 1 metallurgy. Metallurgy now pays on
        /// seed 1 alone, which is written down as a balance item - the gate only asks that the ground decides.
        /// </summary>
        [Test]
        public void TheCivilisationThatPaysDependsOnTheGround()
        {
            long farm2 = Power(2, CivKind.Agrarian), metal2 = Power(2, CivKind.Metallurgy);
            long farm1 = Power(1, CivKind.Agrarian), metal1 = Power(1, CivKind.Metallurgy);
            TestContext.WriteLine("seed 2: farming " + farm2 + ", metallurgy " + metal2 + "; seed 1: farming " + farm1 + ", metallurgy " + metal1);
            Assert.That(farm2, Is.GreaterThan(metal2), "on seed 2's ground farming pays");
            Assert.That(metal1, Is.GreaterThan(farm1), "on seed 1's ground metallurgy pays");
        }

        /// <summary>32 #7, #8: the second age raises the population ceiling and opens the civilisation's own unit.</summary>
        [TestCase(CivKind.Agrarian, UnitKind.Archer, UnitKind.Cavalry)]
        [TestCase(CivKind.Metallurgy, UnitKind.Cavalry, UnitKind.Archer)]
        public void TheSecondAgeOpensTheCivilisationsOwnUnit(CivKind civ, UnitKind own, UnitKind other)
        {
            var s = MapGenerator.GenerateTerrain(7);
            s.Armies[2].Capacity = 26; // room for the trained unit after the automatic economy filled the usual armies
            s.Economy.StartFood = 3000; s.Economy.StartWood = 3000; s.Economy.StartMetal = 100;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            uint barracks = 0;
            for (int t = 0; t < 4000 && barracks == 0; t += 20)
            {
                Steps(gateway, sim, 20);
                var b = sim.Capture(1).Economy.Buildings.FirstOrDefault(v => v.Kind == BuildingKind.Barracks && v.FactionId == 1 && v.Complete);
                barracks = b.Id;
            }
            Assume.That(barracks, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, barracks, own));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            for (int i = 0; i < 5; i++) { gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, barracks)); gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0)); }
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Buildings.First(b => b.Id == barracks).Queued, Is.EqualTo(0), "not before the second age");
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.AdvanceRemaining, Is.GreaterThan(0), "the second age is under way");
            Steps(gateway, sim, s.Economy.Age2Ticks + 1);
            var e = sim.Capture(1).Economy;
            Assert.That(e.Age, Is.EqualTo(2));
            Assert.That(e.PopulationCap, Is.LessThanOrEqualTo(s.Economy.PopulationCap + s.Economy.Age2PopulationBonus));

            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, barracks, other)); // the other civilisation's unit: refused
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, barracks, own));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            string n = "Buildings[" + barracks + "].";
            Assert.That(Number(f, n + "Queued"), Is.EqualTo(1));
            Assert.That(f[n + "QueueKinds[0]"], Is.EqualTo(((byte)own).ToString(CultureInfo.InvariantCulture)));
            long next = Number(f, "NextSoldierId");
            Steps(gateway, sim, 400);
            f = Fields(sim);
            // The east goes on training too, so the new west soldier is the first west one from here.
            long mine = Enumerable.Range((int)next, (int)(Number(f, "NextSoldierId") - next)).FirstOrDefault(i => f["Soldiers[" + i + "].FactionId"] == "1");
            Assert.That(mine, Is.GreaterThan(0), "it came out");
            string id = "Soldiers[" + mine + "].";
            Assert.That(f[id + "Class"], Is.EqualTo(((byte)own).ToString(CultureInfo.InvariantCulture)));
            Assert.That(f[id + "Kind"], Is.EqualTo(((byte)UnitKind.Infantry).ToString(CultureInfo.InvariantCulture)), "it fights as infantry");
            if (own == UnitKind.Archer) Assert.That(Number(f, id + "Parameters.Range.Raw"), Is.EqualTo(s.Economy.ArcherRange.Raw));
            else Assert.That(Number(f, id + "Parameters.Speed.Raw"), Is.EqualTo(s.Economy.CavalrySpeed.Raw));
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
