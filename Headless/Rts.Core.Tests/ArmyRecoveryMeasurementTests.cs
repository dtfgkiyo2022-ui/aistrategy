using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>Measurement for the army-recovery request. The output is worktree evidence, not game state.</summary>
    public sealed class ArmyRecoveryMeasurementTests
    {
        [Test]
        public void AiArmyRecoveryExtension52RoundTripsAndOffOmitsIt()
        {
            var on = LiveGameRules.Create(1, false, true, false, false);
            on.Economy.ExploreScouting = false;
            byte[] onBytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(onBytes);
            Assert.That(decoded.Economy.AiArmyRecovery, Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 52), Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 52).Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(onBytes));

            var off = LiveGameRules.Create(1, false, true, false, false);
            off.Economy.ExploreScouting = false;
            off.Economy.AiArmyRecovery = false;
            byte[] offBytes = ScenarioBinary.Encode(off);
            Assert.That(ScenarioBinary.Decode(offBytes).Economy.AiArmyRecovery, Is.False);
            Assert.That(ScenarioBinary.Decode(offBytes).Extensions.Any(x => x.Id == 52), Is.False);
        }

        [Test, Explicit("AiArmyRecovery off hash regression")]
        public void AiArmyRecoveryOffKeepsBytesAndStateHashesIdentical()
        {
            var leftScenario = LiveGameRules.Create(1, false, true, false, false);
            leftScenario.Economy.ExploreScouting = false;
            leftScenario.Economy.AiArmyRecovery = false;
            var rightScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario));
            Assert.That(ScenarioBinary.Encode(rightScenario), Is.EqualTo(ScenarioBinary.Encode(leftScenario)));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void AiArmyRecoveryOnRecordsAndReplaysWithTheSameHashes()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.ExploreScouting = false;
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                var record = ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 600, build);
                Assert.That(record.IsFault, Is.False);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, build);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test, Explicit("army recovery measurement")]
        public void MeasureInfantryDecisionConditionsAroundAgeThreeClock()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.ExploreScouting = false;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            var output = new StringBuilder();
            output.AppendLine("tick\tfaction\tinfantry\tscouts\tfood\twood\tmetal\tsteel\tvillagers\tpopulation\tpopCap\tage\tadvanceRemaining\tadvanceTo\tbarracks\tready\theld\tqueued\ttrainRemaining\tfoodCost\twoodCost\tmetalCost\tpopulationCondition\tqueueCondition\tfoodCondition\twoodCondition\tmetalCondition\troomCondition\tsavingToAdvance\tprimitiveSaving\tdecisionWouldTrain\tfoodNodes\twoodNodes\tfoodRemaining\twoodRemaining\tidle\ttoResource\ttoResourceUnreachable\tgatherFood\treturningFood\tgatherWood\treturningWood\ttoBuild\tbuilding\thauling\ttrading");
            for (long tick = 1; tick <= 16000; tick++)
            {
                gateway.Step();
                if (tick < 14000 || tick > 16000 || tick % 20 != 0) continue;
                for (uint faction = 1; faction <= 2; faction++)
                    AppendMeasurement(output, sim, faction);
            }

            Directory.CreateDirectory("armyrec");
            File.WriteAllText("armyrec/army-recovery-baseline.tsv", output.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine(output.ToString());
        }

        [Test, Explicit("fails before AiArmyRecovery")]
        public void SeedOneMustTrainInfantryAfterTick15000()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.ExploreScouting = false;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            int infantryAt15000 = -1;
            int infantryAfter15000 = -1;
            for (long tick = 1; tick <= 18000; tick++)
            {
                gateway.Step();
                if (tick == 15000) infantryAt15000 = OwnInfantry(sim.Capture(1));
                if (tick > 15000 && OwnInfantry(sim.Capture(1)) > 0)
                {
                    infantryAfter15000 = OwnInfantry(sim.Capture(1));
                    break;
                }
            }

            Assert.That(infantryAt15000, Is.EqualTo(0), "the request's seed-1 stall must still be the reproduction point");
            Assert.That(infantryAfter15000, Is.GreaterThan(0), "automatic economy must recover infantry after tick 15000");
        }

        private static void AppendMeasurement(StringBuilder output, Battle sim, uint faction)
        {
            var frame = sim.Capture(faction);
            var e = frame.Economy;
            int infantry = OwnInfantry(frame);
            int scouts = frame.Units.Count(x => x.IsOwn && x.Kind == UnitKind.Scout);
            int livingVillagers = Invoke<int>(sim, "LivingVillagers", faction);
            int livingSoldiers = Invoke<int>(sim, "LivingSoldiers", faction);
            int economyQueued = ReadEconomyInt(sim, faction, "Queued");
            int queuedInfantry = Invoke<int>(sim, "QueuedInfantry", faction);
            int queuedTownVillagers = Invoke<int>(sim, "QueuedTownVillagers", faction);
            int population = livingVillagers + livingSoldiers + economyQueued + queuedInfantry + queuedTownVillagers;
            int popCap = Invoke<int>(sim, "PopCapFor", faction);
            int foodCost = Invoke<int>(sim, "InfantryFoodFor", faction);
            int woodCost = Invoke<int>(sim, "InfantryWoodFor", faction);
            int metalCost = Invoke<int>(sim, "InfantryMetalFor", faction);
            bool room = Invoke<bool>(sim, "HasInfantryRoom", faction);
            bool saving = Invoke<bool>(sim, "SavingToAdvance", faction);
            bool primitiveSaving = saving && e.Civ == CivKind.Primitive;
            var barracks = e.Buildings.Where(x => x.Kind == BuildingKind.Barracks).ToArray();
            bool ready = barracks.Any(x => x.Complete && !x.PlayerHeld);
            int queued = barracks.Sum(x => x.Queued);
            long trainRemaining = barracks.Length == 0 ? 0 : barracks.Min(x => x.TrainRemaining);
            int queueTarget = Math.Min(2, 5);
            bool wouldTrain = ready && queued < queueTarget && e.Food >= foodCost && e.Wood >= woodCost
                && population < popCap && room && e.Metal >= metalCost && !primitiveSaving;
            var foodNodes = e.Resources.Where(x => x.Kind == ResourceKind.Food).ToArray();
            var woodNodes = e.Resources.Where(x => x.Kind == ResourceKind.Wood).ToArray();
            int activity(VillagerActivity activity) => e.Villagers.Count(x => x.Activity == activity);
            int carryActivity(VillagerActivity activity, ResourceKind kind) => e.Villagers.Count(x => x.Activity == activity && x.CarryKind == kind);
            int toResourceUnreachable = CountUnreachableToNode(sim, faction);
            output.AppendLine(string.Join("\t", frame.Tick, faction, infantry, scouts, e.Food, e.Wood, e.Metal, e.Steel,
                livingVillagers, population, popCap, e.Age, e.AdvanceRemaining, e.AdvancingTo, barracks.Length, ready,
                barracks.Any(x => x.PlayerHeld), queued, trainRemaining, foodCost, woodCost, metalCost,
                population < popCap, queued < queueTarget, e.Food >= foodCost, e.Wood >= woodCost, e.Metal >= metalCost, room, saving,
                primitiveSaving, wouldTrain, foodNodes.Length, woodNodes.Length, foodNodes.Sum(x => x.Remaining), woodNodes.Sum(x => x.Remaining),
                activity(VillagerActivity.Idle), activity(VillagerActivity.ToResource), toResourceUnreachable, carryActivity(VillagerActivity.Gathering, ResourceKind.Food),
                carryActivity(VillagerActivity.Returning, ResourceKind.Food), carryActivity(VillagerActivity.Gathering, ResourceKind.Wood),
                carryActivity(VillagerActivity.Returning, ResourceKind.Wood), activity(VillagerActivity.ToBuild), activity(VillagerActivity.Building),
                activity(VillagerActivity.Hauling), activity(VillagerActivity.Trading)));
        }

        private static int OwnInfantry(FactionFrame frame)
            => frame.Units.Count(x => x.IsOwn && x.Kind == UnitKind.Infantry);

        private static int ReadEconomyInt(Battle sim, uint faction, string name)
        {
            object economy = GetWorldField(sim, "Economies").GetType().GetElementType() == null
                ? null : ((Array)GetWorldField(sim, "Economies")).GetValue((int)faction - 1);
            return (int)economy.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(economy);
        }

        private static T Invoke<T>(Battle sim, string method, params object[] args)
        {
            var info = typeof(Battle).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)info.Invoke(sim, args);
        }

        private static object GetWorldField(Battle sim, string name)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            return world.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
        }

        private static int CountUnreachableToNode(Battle sim, uint faction)
        {
            object world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            var worldType = world.GetType();
            var villagers = (Array)worldType.GetField("Villagers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            var nodes = (Array)worldType.GetField("Nodes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            var map = (GridMap)worldType.GetField("Map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            int count = 0;
            foreach (object villager in villagers)
            {
                var type = villager.GetType();
                if (!(bool)type.GetField("Alive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(villager)
                    || (uint)type.GetField("FactionId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(villager) != faction
                    || (byte)type.GetField("Task", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(villager) != 1) continue;
                uint nodeId = (uint)type.GetField("NodeId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(villager);
                if (nodeId == 0 || nodeId > nodes.Length) continue;
                var node = nodes.GetValue((int)nodeId - 1);
                var definition = node.GetType().GetField("Definition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(node);
                var goal = (SimPoint)definition.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(definition);
                var position = (SimPoint)type.GetField("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(villager);
                if (map.SharedRoute(map.Cell(position), goal, faction).Length == 0) count++;
            }
            return count;
        }
    }
}
