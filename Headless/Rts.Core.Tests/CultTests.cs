using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public sealed class CultTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition CultScenario(bool monks = true, Action<ScenarioDefinition> customize = null)
        {
            var s = MapGenerator.GenerateTerrain(1);
            s.Economy.Cult = true;
            s.Economy.MonksEnabled = monks;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 5000;
            s.Economy.AutoVillagerTarget = 3;
            s.Economy.AutoInfantryQueue = 0;
            s.Cores[0].Hp = s.Cores[1].Hp = 100000;
            customize?.Invoke(s);
            return s;
        }

        private static void AdvanceCultAge(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Cult));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 3);
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtCultAge(Action<ScenarioDefinition> customize = null, bool holdEnemy = false, bool humanArmyPolicy = false)
        {
            var scenario = CultScenario(true, customize);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            if (holdEnemy)
                gateway.Submit(new UserPolicyIntent(1, new ScopeKey(2, ScopeKind.All, 0), PolicyKind.Defend, default,
                    0, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
                    new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            if (humanArmyPolicy)
                gateway.Submit(new UserPolicyIntent(0, new ScopeKey(1, ScopeKind.Army, 3), PolicyKind.Defend,
                    new PolicyGoal(GoalKind.Core, 1, default), 0, new LossBudget(300),
                    new EndCondition(EndKind.UntilReplaced, 0), 0,
                    new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Cult));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 5);
            return (scenario, sim, gateway, sequence);
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence, uint monastery, uint monk) CultWithOneManualMonk(int conversionTicks = 100000, int postTrainTicks = 60, bool humanPolicy = false, Action<ScenarioDefinition> customize = null)
        {
            var state = AtCultAge(s =>
            {
                s.Economy.AdvanceTicks = 1;
                s.Economy.ConversionTicks = conversionTicks;
                s.Economy.MonasteryWork = 1;
                s.Economy.MonkTrainTicks = 1;
                var west = s.Cores[0].Position;
                for (int i = 20; i < s.Soldiers.Length; i++)
                    s.Soldiers[i].Position = s.Cores[1].Position;
                int targetOffset = conversionTicks < 100000 ? 6 : -15;
                s.Soldiers[20].Position = conversionTicks < 100000
                    ? new SimPoint(s.Soldiers[17].Position.X, west.Z - Fix64.FromInt(9))
                    : new SimPoint(west.X + Fix64.FromInt(targetOffset), west.Z);
                s.Soldiers[20].Hp = 100;
                int infantryIndex = Array.FindIndex(s.UnitParameters, p => p.Kind == UnitKind.Infantry);
                s.UnitParameters[infantryIndex].Hp = 100000;
                if (conversionTicks < 100000) s.UnitParameters[infantryIndex].Speed = Fix64.FromInt(0);
                for (int i = 0; i < s.Soldiers.Length; i++)
                {
                    var parameters = Array.Find(s.UnitParameters, p => p.Kind == s.Soldiers[i].Kind);
                    s.Soldiers[i].Hp = s.Soldiers[i].Alive ? parameters.Hp : 0;
                }
                customize?.Invoke(s);
            }, false, humanPolicy);
            uint monastery = PlaceAndBuild(state.scenario, state.sim, state.gateway, ref state.sequence);
            state.gateway.SubmitEconomy(EconomyCommand.Train(1, ++state.sequence, monastery, UnitKind.Monk));
            Steps(state.gateway, state.sim, state.scenario.Economy.MonkTrainTicks + postTrainTicks);
            uint monk = state.sim.Capture(1).Units.First(u => u.IsOwn && u.Kind == UnitKind.Monk).Id;
            return (state.scenario, state.sim, state.gateway, state.sequence, monastery, monk);
        }

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static int Number(Dictionary<string, string> fields, string name)
            => int.Parse(fields[name], CultureInfo.InvariantCulture);

        private static uint PlaceAndBuild(ScenarioDefinition s, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            int before = sim.Capture(1).Economy.Buildings.Count;
            int width = s.Map.WidthCells;
            int core = width * ((int)(s.Cores[0].Position.Z.Raw / 65536) / s.Map.CellSizeMeters)
                + (int)(s.Cores[0].Position.X.Raw / 65536) / s.Map.CellSizeMeters;
            int cx = core % width, cz = core / width;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx >= width - s.Economy.MonasterySizeCells
                            || cz + dz >= s.Map.HeightCells - s.Economy.MonasterySizeCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Monastery,
                            (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count <= before || buildings[before].Kind != BuildingKind.Monastery) continue;
                        uint id = buildings[before].Id;
                        gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 },
                            EconomyTargetKind.Building, id));
                        for (int tick = 0; tick < 900; tick++)
                        {
                            Steps(gateway, sim, 1);
                            if (sim.Capture(1).Economy.Buildings.First(b => b.Id == id).Complete) break;
                        }
                        return id;
                    }
            return 0;
        }

        [Test]
        public void CultOffKeepsTheOldBytesAndState()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Cult = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old); var right = new Battle(off);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void CultRequiresMonksAndCanBeSelectedWithItsOwnFlag()
        {
            var disabled = CultScenario(false);
            var noMonks = new Battle(disabled);
            var noMonksGateway = new CommandGateway(noMonks);
            noMonksGateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Cult));
            Steps(noMonksGateway, noMonks, disabled.Economy.AdvanceTicks + 2);
            Assert.That(noMonks.Capture(1).Economy.Civ, Is.Not.EqualTo(CivKind.Cult));

            var selected = AtCultAge();
            Assert.That(selected.sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Cult));
        }

        [Test]
        public void MonasteryUsesCultFootprintAndOnlyCultCanTrainItsMonk()
        {
            var state = AtCultAge();
            uint monastery = PlaceAndBuild(state.scenario, state.sim, state.gateway, ref state.sequence);
            Assert.That(monastery, Is.Not.EqualTo(0u));
            var building = state.sim.Capture(1).Economy.Buildings.First(b => b.Id == monastery);
            Assert.That((building.SizeMeters, building.Hp), Is.EqualTo((state.scenario.Economy.MonasterySizeCells * state.scenario.Map.CellSizeMeters,
                state.scenario.Economy.MonasteryHp)));

            var before = Fields(state.sim);
            int food = Number(before, "Economy[1].Food"), wood = Number(before, "Economy[1].Wood");
            state.gateway.SubmitEconomy(EconomyCommand.Train(1, ++state.sequence, monastery, UnitKind.Monk));
            Steps(state.gateway, state.sim, 1);
            var queued = Fields(state.sim);
            Assert.That(Number(queued, "Buildings[" + monastery + "].Queued"), Is.EqualTo(1));
            Assert.That(Number(queued, "Economy[1].Food"), Is.EqualTo(food - state.scenario.Economy.MonasteryMonkFoodCost));
            Assert.That(Number(queued, "Economy[1].Wood"), Is.EqualTo(wood - state.scenario.Economy.MonasteryMonkWoodCost));

            state.gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++state.sequence, monastery));
            Steps(state.gateway, state.sim, 1);
            var cancelled = Fields(state.sim);
            Assert.That(Number(cancelled, "Economy[1].Food"), Is.EqualTo(food));
            Assert.That(Number(cancelled, "Economy[1].Wood"), Is.EqualTo(wood));
        }

        [Test]
        public void CultExtensionRoundTripsAndKeepsOtherExtensions()
        {
            var scenario = CultScenario();
            scenario.Economy.Academy = true;
            scenario.Economy.GoldEnabled = true;
            scenario.Economy.Forestry = true;
            scenario.Economy.Masonry = true;
            scenario.Economy.Caravan = true;
            scenario.Economy.Bridge = true;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Cult, Is.True);
            Assert.That(decoded.Economy.MonasteryMonkWoodCost, Is.EqualTo(scenario.Economy.MonasteryMonkWoodCost));
            Assert.That(decoded.Extensions.Any(e => e.Id == 3), Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 2), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CultMonkKeepsOnlyVisibleEnemyContactsAsTargets()
        {
            var state = CultWithOneManualMonk();
            var visible = state.sim.Capture(1).Observation.VisibleEnemies.Select(e => e.ContactId).ToHashSet();
            var fields = Fields(state.sim);
            var targets = fields.Where(p => p.Key.EndsWith("CultTargetContactId", StringComparison.Ordinal) && p.Value != "0")
                .Select(p => uint.Parse(p.Value, CultureInfo.InvariantCulture)).ToArray();
            Assert.That(targets, Is.Not.Empty);
            Assert.That(targets.All(visible.Contains), Is.True);
        }

        [Test]
        public void CultMonkKeepsTheSameSafeTarget()
        {
            var state = CultWithOneManualMonk();
            var targets = new HashSet<uint>();
            for (int i = 0; i < 30; i++)
            {
                state.gateway.Step();
                foreach (var pair in Fields(state.sim).Where(p => p.Key.EndsWith("CultTargetContactId", StringComparison.Ordinal)))
                    if (pair.Value != "0") targets.Add(uint.Parse(pair.Value, CultureInfo.InvariantCulture));
            }
            Assert.That(targets, Has.Count.EqualTo(1));
        }

        [Test]
        public void CultAutomaticMonksAreCappedByTheirEscorts()
        {
            var state = AtCultAge();
            uint monastery = PlaceAndBuild(state.scenario, state.sim, state.gateway, ref state.sequence);
            Assert.That(monastery, Is.Not.EqualTo(0u));
            state.gateway.SubmitEconomy(EconomyCommand.Auto(1, ++state.sequence, true));
            Steps(state.gateway, state.sim, 2500);
            int monks = state.sim.Capture(1).Units.Count(u => u.IsOwn && u.Kind == UnitKind.Monk);
            Assert.That(monks, Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void ConvertedSoldierIsAddedToAnOwnArmy()
        {
            var state = CultWithOneManualMonk(1, 3);
            var before = state.sim.Capture(1).Units.Count(u => u.IsOwn && u.Kind != UnitKind.Monk);
            Steps(state.gateway, state.sim, 200);
            var after = Fields(state.sim);
            Assert.That(state.sim.Capture(1).Units.Count(u => u.IsOwn && u.Kind != UnitKind.Monk), Is.GreaterThan(before));
            uint newest = (uint)Number(after, "Soldiers.Count");
            Assert.That(after.Any(p => p.Key.Contains("SoldierIds[", StringComparison.Ordinal) && p.Value == newest.ToString(CultureInfo.InvariantCulture)), Is.True);
        }

        [Test]
        public void HumanArmyPolicyStopsCultAutomaticTargeting()
        {
            var state = CultWithOneManualMonk(100000, 60, true);
            var fields = Fields(state.sim);
            Assert.That(fields.Where(p => p.Key.EndsWith("CultTargetContactId", StringComparison.Ordinal)).All(p => p.Value == "0"), Is.True);
        }

        [Test]
        public void CultReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = CultScenario();
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void SermonShortensOnlyTheCultFactionConversion()
        {
            var state = AtCultAge(s =>
            {
                s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1; s.Economy.SermonTicks = 1;
                s.Economy.ConversionTicks = 400;
            }, humanArmyPolicy: true);
            uint monastery = PlaceAndBuild(state.scenario, state.sim, state.gateway, ref state.sequence);
            AdvanceCultAge(state.scenario, state.sim, state.gateway, ref state.sequence);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, monastery, CultTech.Sermon));
            Steps(state.gateway, state.sim, state.scenario.Economy.SermonTicks + 2);

            var method = typeof(Battle).GetMethod("ConversionTicksFor", BindingFlags.Instance | BindingFlags.NonPublic);
            int cultTicks = (int)method.Invoke(state.sim, new object[] { 1u });
            int otherFactionTicks = (int)method.Invoke(state.sim, new object[] { 2u });
            Assert.That(cultTicks, Is.EqualTo(400 * 2 / 3));
            Assert.That(otherFactionTicks, Is.EqualTo(400));
        }

        [Test]
        public void MartyrBlessingRaisesCultMonkHpOnlyAfterThirdAgeResearch()
        {
            var state = CultWithOneManualMonk(100000, 2, true, s =>
            {
                s.Economy.Age2FoodCost = s.Economy.Age2WoodCost = 0;
                s.Economy.Age2Ticks = s.Economy.Age3Ticks = 1;
                s.Economy.Age3FoodCost = s.Economy.Age3WoodCost = 0;
                s.Economy.SermonTicks = s.Economy.MartyrBlessingTicks = 1;
            });
            uint monastery = state.monastery;
            int before = state.sim.Capture(1).Units.First(u => u.Id == state.monk).Hp;
            AdvanceCultAge(state.scenario, state.sim, state.gateway, ref state.sequence);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, monastery, CultTech.Sermon));
            Steps(state.gateway, state.sim, 3);
            AdvanceCultAge(state.scenario, state.sim, state.gateway, ref state.sequence);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, monastery, CultTech.MartyrBlessing));
            Steps(state.gateway, state.sim, 3);
            int after = state.sim.Capture(1).Units.First(u => u.Id == state.monk).Hp;
            Assert.That(after, Is.EqualTo(before * state.scenario.Economy.MartyrBlessingHpPermille / 1000));
        }

        [Test]
        public void CultBudgetLeavesAgeCostBeforeTrainingAndResearch()
        {
            var training = AtCultAge(s => { s.Economy.StartFood = 700; s.Economy.StartWood = 500; s.Economy.Age2FoodCost = 500; s.Economy.Age2WoodCost = 300; });
            uint monastery = PlaceAndBuild(training.scenario, training.sim, training.gateway, ref training.sequence);
            training.gateway.SubmitEconomy(EconomyCommand.Train(1, ++training.sequence, monastery, UnitKind.Monk));
            Steps(training.gateway, training.sim, 1);
            Assert.That(training.sim.Capture(1).Economy.Buildings.First(b => b.Id == monastery).Queued, Is.EqualTo(0));

            var research = AtCultAge(s =>
            {
                s.Economy.StartFood = 2000; s.Economy.StartWood = 1500; s.Economy.Age2FoodCost = 500; s.Economy.Age2WoodCost = 300;
                s.Economy.Age2Ticks = 1; s.Economy.SermonTicks = 1;
            });
            monastery = PlaceAndBuild(research.scenario, research.sim, research.gateway, ref research.sequence);
            AdvanceCultAge(research.scenario, research.sim, research.gateway, ref research.sequence);
            research.gateway.SubmitEconomy(EconomyCommand.Research(1, ++research.sequence, monastery, CultTech.Sermon));
            Steps(research.gateway, research.sim, 1);
            Assert.That(research.sim.Capture(1).Economy.Buildings.First(b => b.Id == monastery).Researching, Is.EqualTo((TechKind)0));
        }

        [Test]
        public void CultCanReachTheNextAgeWhileConversionAttemptsFail()
        {
            var state = AtCultAge(s =>
            {
                s.Economy.ConversionTicks = 100000; s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            });
            AdvanceCultAge(state.scenario, state.sim, state.gateway, ref state.sequence);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(2));
        }

        [Test]
        public void CultVersionOneExtensionIsReadableAndUpgradesToStableVersionTwo()
        {
            var scenario = CultScenario();
            byte[] current = ScenarioBinary.Encode(scenario);
            int marker = -1;
            for (int i = current.Length - 4; i >= 0; i--)
                if (BitConverter.ToInt32(current, i) == 0x4E545845) { marker = i; break; }
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));
            Assert.That(BitConverter.ToInt32(current, marker + 12), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(current, marker + 20), Is.EqualTo(14 * sizeof(int)));

            int recordData = marker + 24;
            byte[] v1 = new byte[current.Length - 7 * sizeof(int)];
            Buffer.BlockCopy(current, 0, v1, 0, recordData + 7 * sizeof(int));
            Buffer.BlockCopy(current, recordData + 14 * sizeof(int), v1, recordData + 7 * sizeof(int),
                current.Length - (recordData + 14 * sizeof(int)));
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, v1, marker + 16, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(7 * sizeof(int)), 0, v1, marker + 20, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.ToInt32(v1, marker + 8) - 7 * sizeof(int)), 0, v1, marker + 8, sizeof(int));

            var decodedV1 = ScenarioBinary.Decode(v1);
            Assert.That(decodedV1.Extensions.Single(e => e.Id == 3).Version, Is.EqualTo(1));
            byte[] v2 = ScenarioBinary.Encode(decodedV1);
            var decodedV2 = ScenarioBinary.Decode(v2);
            Assert.That(decodedV2.Extensions.Single(e => e.Id == 3).Version, Is.EqualTo(2));
            Assert.That(ScenarioBinary.Encode(decodedV2), Is.EqualTo(v2));
        }
    }
}
