using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>V3-10 #1: the cavalry civilisation, its early stable and its light cavalry.</summary>
    public sealed class CavalryTests
    {
        private const int Width = 128;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway) AtCavalryAge1(bool auto)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Cavalry = true;
            scenario.Economy.StartFood = 6000;
            scenario.Economy.StartWood = 6000;
            scenario.Armies[2].Capacity = 26;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, auto));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Cavalry));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Cavalry));
            return (scenario, sim, gateway);
        }

        private static ScenarioDefinition CavalryMatchScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Cavalry = true;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.StableWork = 1;
            s.Economy.LightCavalryTicks = 1;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static uint PlaceStable(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / 2) * Width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / 2);
            int cx = core % Width, cz = core / Width;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx > Width - 4 || cz + dz > 60) continue;
                        long before = Number(Fields(sim), "Buildings.Count");
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Stable,
                            (cz + dz) * Width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var fields = Fields(sim);
                        if (Number(fields, "Buildings.Count") > before
                            && fields["Buildings[" + (before + 1) + "].Kind"] == ((byte)BuildingKind.Stable).ToString(CultureInfo.InvariantCulture))
                            return (uint)(before + 1);
                    }
            return 0;
        }

        [Test]
        public void CavalryEnablesStableAndLightCavalryInTheFirstAgeOnly()
        {
            var cavalry = AtCavalryAge1(false);
            ulong sequence = 10;
            uint stable = PlaceStable(cavalry.scenario, cavalry.sim, cavalry.gateway, ref sequence);
            Assert.That(stable, Is.Not.EqualTo(0u));
            Steps(cavalry.gateway, cavalry.sim, cavalry.scenario.Economy.StableWork + 300);
            Assert.That(cavalry.sim.Capture(1).Economy.Buildings.First(b => b.Id == stable).Complete, Is.True);

            long food = cavalry.sim.Capture(1).Economy.Food;
            long wood = cavalry.sim.Capture(1).Economy.Wood;
            cavalry.gateway.SubmitEconomy(EconomyCommand.Train(1, ++sequence, stable, UnitKind.LightCavalry));
            Steps(cavalry.gateway, cavalry.sim, 1);
            cavalry.gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++sequence, stable));
            Steps(cavalry.gateway, cavalry.sim, 1);
            Assert.That(cavalry.sim.Capture(1).Economy.Food, Is.EqualTo(food));
            Assert.That(cavalry.sim.Capture(1).Economy.Wood, Is.EqualTo(wood));

            long next = Number(Fields(cavalry.sim), "NextSoldierId");
            cavalry.gateway.SubmitEconomy(EconomyCommand.Train(1, ++sequence, stable, UnitKind.LightCavalry));
            Steps(cavalry.gateway, cavalry.sim, cavalry.scenario.Economy.LightCavalryTicks + 5);
            var fieldsAfter = Fields(cavalry.sim);
            var trained = Enumerable.Range((int)next, (int)Number(fieldsAfter, "NextSoldierId") - (int)next)
                .FirstOrDefault(id => fieldsAfter["Soldiers[" + id + "].FactionId"] == "1");
            Assert.That(trained, Is.GreaterThan(0));
            Assert.That(fieldsAfter["Soldiers[" + trained + "].Class"], Is.EqualTo(((byte)UnitKind.LightCavalry).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Number(fieldsAfter, "Soldiers[" + trained + "].Parameters.Hp"), Is.EqualTo(cavalry.scenario.Economy.LightCavalryHp));
            Assert.That(Number(fieldsAfter, "Soldiers[" + trained + "].Parameters.Damage"), Is.EqualTo(cavalry.scenario.Economy.LightCavalryDamage));

            var old = MapGenerator.GenerateTerrain(1);
            var oldSim = new Battle(old);
            var oldGateway = new CommandGateway(oldSim);
            oldGateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Cavalry));
            Steps(oldGateway, oldSim, 1);
            Assert.That(oldSim.Capture(1).Economy.AdvanceRemaining, Is.EqualTo(0));
        }

        [Test]
        public void LightCavalryUsesTheExistingCavalryCounterLine()
        {
            Assert.That(CombatMath.Counters(UnitKind.LightCavalry, UnitKind.Archer), Is.True);
            Assert.That(CombatMath.Counters(UnitKind.Archer, UnitKind.LightCavalry), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.Infantry, UnitKind.LightCavalry), Is.True);
            Assert.That(CombatMath.Counters(UnitKind.LightCavalry, UnitKind.Infantry), Is.False);
        }

        [Test]
        public void CavalryTailRoundTripsAfterForestryAndMasonryAndDisabledBytesStayTheSame()
        {
            var disabled = MapGenerator.GenerateTerrain(2);
            byte[] before = ScenarioBinary.Encode(disabled);
            byte[] after = ScenarioBinary.Encode(ScenarioBinary.Decode(before));
            Assert.That(after, Is.EqualTo(before));

            disabled.Economy.Forestry = true;
            disabled.Economy.Masonry = true;
            disabled.Economy.Cavalry = true;
            byte[] bytes = ScenarioBinary.Encode(disabled);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Cavalry, Is.True);
            Assert.That(decoded.Economy.Forestry, Is.True);
            Assert.That(decoded.Economy.Masonry, Is.True);
            Assert.That(decoded.Economy.LightCavalryFood, Is.EqualTo(disabled.Economy.LightCavalryFood));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CavalryCanReachTheSecondAgeWithTheAutomaticEconomy()
        {
            var cavalry = AtCavalryAge1(false);
            ulong sequence = 20;
            uint stable = PlaceStable(cavalry.scenario, cavalry.sim, cavalry.gateway, ref sequence);
            Assume.That(stable, Is.Not.EqualTo(0u));
            Steps(cavalry.gateway, cavalry.sim, cavalry.scenario.Economy.StableWork + 300);
            cavalry.gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            Steps(cavalry.gateway, cavalry.sim, 14000);
            Assert.That(cavalry.sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void AutomaticCavalryProducesAMobileMajorityArmy()
        {
            var cavalry = AtCavalryAge1(true);
            Steps(cavalry.gateway, cavalry.sim, 12000);
            var fields = Fields(cavalry.sim);
            var light = fields.Where(p => p.Key.StartsWith("Soldiers[", StringComparison.Ordinal) && p.Key.EndsWith("].Class", StringComparison.Ordinal)
                    && p.Value == ((byte)UnitKind.LightCavalry).ToString(CultureInfo.InvariantCulture))
                .Select(p => p.Key.Substring(9, p.Key.IndexOf(']', 9) - 9)).ToArray();
            Assert.That(light.Length, Is.GreaterThan(0));
            var armyIds = light.Select(id => fields["Soldiers[" + id + "].ArmyId"]).Distinct().ToArray();
            Assert.That(armyIds.Length, Is.EqualTo(1));
            string army = armyIds[0];
            int lightCount = fields.Count(p => p.Key.EndsWith("].Class", StringComparison.Ordinal) && p.Value == ((byte)UnitKind.LightCavalry).ToString(CultureInfo.InvariantCulture)
                && fields.TryGetValue("Soldiers[" + p.Key.Substring(9, p.Key.IndexOf(']', 9) - 9) + "].ArmyId", out var id) && id == army);
            int infantryCount = fields.Count(p => p.Key.EndsWith("].Class", StringComparison.Ordinal) && p.Value == "0"
                && fields.TryGetValue("Soldiers[" + p.Key.Substring(9, p.Key.IndexOf(']', 9) - 9) + "].ArmyId", out var id) && id == army);
            Assert.That(lightCount, Is.GreaterThan(infantryCount));
        }

        [Test]
        public void MobileDecisionChoosesOnlyObservedRaidTargetsAndKeepsHumanOrders()
        {
            SimPoint p(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
            var own = new OwnArmyView(1, 1, UnitKind.LightCavalry, p(10, 10), 4, new PolicyGoal(GoalKind.Core, 1, default));
            var target = new ObservedRaidTarget(71, RaidTargetKind.Carrier, p(40, 10), 1);
            var observation = new FactionObservation(1, 20, new[] { own }, Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                new[] { new KnownObjective(GoalKind.Core, 1, p(10, 10), true, 1, true, 100, 20) }, 40, new[] { target });
            var route = new ObjectiveRoute(new PolicyGoal(GoalKind.Point, target.Id, target.Position), 30, new[] { own.Position, target.Position });
            var input = new ArmyDecisionInput(own, default, false, new ArmyDecisionMemory(AssignmentKind.Advance, default, false, 0, 0), new[] { route });
            var chosen = PolicyDecision.Allocate(observation, 20, new[] { input }, 0, Array.Empty<uint>(), Array.Empty<AttackMemory>(),
                Array.Empty<PolicyOrder>(), out _)[0];
            Assert.That(chosen.Assignment, Is.EqualTo(AssignmentKind.Advance));
            Assert.That(chosen.Goal.Kind, Is.EqualTo(GoalKind.Point));
            Assert.That(chosen.Goal.Id, Is.EqualTo(target.Id));

            var humanGoal = new PolicyGoal(GoalKind.Outpost, 2, default);
            var human = new PolicyView(9, CommandSource.Human, PolicyKind.Focus, humanGoal, default, 0);
            var humanInput = new ArmyDecisionInput(own, human, false, new ArmyDecisionMemory(AssignmentKind.Reserve, humanGoal, false, 0, 0), new[] { route });
            var held = PolicyDecision.Allocate(observation, 20, new[] { humanInput }, 0, Array.Empty<uint>(), Array.Empty<AttackMemory>(),
                Array.Empty<PolicyOrder>(), out _)[0];
            Assert.That(held.Goal.Kind, Is.EqualTo(GoalKind.Outpost));
            Assert.That(held.Goal.Id, Is.EqualTo(2u));
        }

        [Test]
        public void UnobservedRaidInformationIsNotAValidMobileMission()
        {
            SimPoint p(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
            var own = new OwnArmyView(1, 1, UnitKind.LightCavalry, p(10, 10), 4, new PolicyGoal(GoalKind.Core, 1, default));
            var hiddenRoute = new ObjectiveRoute(new PolicyGoal(GoalKind.Point, 99, p(80, 10)), 70, new[] { own.Position, p(80, 10) });
            var observation = new FactionObservation(1, 20, new[] { own }, Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                new[] { new KnownObjective(GoalKind.Core, 1, p(10, 10), true, 1, true, 100, 20) });
            var input = new ArmyDecisionInput(own, default, false, new ArmyDecisionMemory(AssignmentKind.Advance, default, false, 0, 0), new[] { hiddenRoute });
            var chosen = PolicyDecision.Allocate(observation, 20, new[] { input }, 0, Array.Empty<uint>(), Array.Empty<AttackMemory>(),
                Array.Empty<PolicyOrder>(), out _)[0];
            Assert.That(chosen.Goal.Kind, Is.Not.EqualTo(GoalKind.Point));
        }

        [Test]
        public void CavalryTerrainScoreRewardsOpenRoutesAndRejectsAThreeCellChoke()
        {
            const int width = 11, height = 11, core = 5 * width + 1;
            var open = new bool[width * height];
            var observed = new bool[width * height];
            Array.Fill(open, true); Array.Fill(observed, true);
            var openScore = CavalryTerrainScoring.Score(width, height, open, observed, core,
                new[] { 5 * width + 9, 2 * width + 9, 8 * width + 9 }, 1);
            Assert.That(openScore.ReachableObjectives, Is.EqualTo(3));
            Assert.That(openScore.ChokeDependentObjectives, Is.EqualTo(0));
            Assert.That(openScore.Points, Is.EqualTo(5));
            Assert.That(EconomyDecision.ChooseCiv(0, 3, 0, 0, openScore.Points, 3), Is.EqualTo(CivKind.Cavalry));

            var choke = (bool[])open.Clone();
            for (int z = 0; z < height; z++) if (z != 5) choke[z * width + 5] = false;
            var chokeScore = CavalryTerrainScoring.Score(width, height, choke, observed, core, new[] { 5 * width + 9 }, 1);
            Assert.That(chokeScore.ReachableObjectives, Is.EqualTo(1));
            Assert.That(chokeScore.ChokeBlockedObjectives, Is.EqualTo(1));
            Assert.That(chokeScore.Points, Is.EqualTo(1));
            Assert.That(EconomyDecision.ChooseCiv(0, 3, 0, 2, chokeScore.Points, 3), Is.EqualTo(CivKind.Masonry));

            var unseen = (bool[])observed.Clone();
            unseen[5 * width + 9] = false;
            var unseenScore = CavalryTerrainScoring.Score(width, height, open, unseen, core, new[] { 5 * width + 9 }, 1);
            Assert.That(unseenScore.ReachableObjectives, Is.EqualTo(0));
            Assert.That(unseenScore.Points, Is.EqualTo(0));
        }

        [Test]
        public void TerrainSeedsProduceBothCavalryAndNonCavalryChoices()
        {
            var choices = new HashSet<CivKind>();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var scenario = MapGenerator.GenerateTerrain(seed);
                scenario.Economy.Cavalry = true;
                scenario.Economy.StartFood = 2500;
                scenario.Economy.StartWood = 2500;
                scenario.Cores[0].Hp = 1000000;
                scenario.Cores[1].Hp = 1000000;
                var sim = new Battle(scenario);
                for (long tick = 1; tick <= 16000 && sim.Capture(1).Economy.Civ == CivKind.Primitive; tick++)
                    sim.Step(tick, Array.Empty<ScheduledInput>());
                choices.Add(sim.Capture(1).Economy.Civ);
            }
            Assert.That(choices, Does.Contain(CivKind.Cavalry));
            Assert.That(choices.Any(c => c != CivKind.Cavalry), Is.True);
        }

        [TestCase(CivKind.Cavalry, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Cavalry)]
        [TestCase(CivKind.Cavalry, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Cavalry)]
        public void CavalryCombinationsReachTheSecondAgeWithoutFault(CivKind west, CivKind east)
        {
            var s = CavalryMatchScenario(21);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            var result = sim.Capture(1).Result;
            TestContext.WriteLine(west + " vs " + east + ": tick=" + sim.Capture(1).Tick + ", winner=" + result.WinnerFactionId
                + ", ended=" + result.HasEnded + ", undecided=" + result.IsUndecided
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), west + " reaches the second age");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), east + " reaches the second age");
        }

        [Test]
        public void NormalCavalryStartReplaysForTwentyThousandTicks()
        {
            // Seed 1 is a normal-start probe where the automatic choice includes cavalry.
            var s = CavalryMatchScenario(1);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            var fields = Fields(sim);
            bool lightCavalry = fields.Any(p => p.Key.EndsWith("].Class", StringComparison.Ordinal)
                && p.Value == ((byte)UnitKind.LightCavalry).ToString(CultureInfo.InvariantCulture));
            TestContext.WriteLine("normal seed 1: civs=" + sim.Capture(1).Economy.Civ + "/" + sim.Capture(2).Economy.Civ
                + ", tick=" + sim.Capture(1).Tick + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age
                + ", light-cavalry=" + lightCavalry);
            Assert.That(sim.Capture(1).Economy.Civ == CivKind.Cavalry || sim.Capture(2).Economy.Civ == CivKind.Cavalry,
                "通常開始で騎馬が選ばれる");
            Assert.That(lightCavalry, Is.True, "通常開始から軽騎兵が作られる");
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から西が第2時代まで進む");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から東が第2時代まで進む");
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }
    }
}
