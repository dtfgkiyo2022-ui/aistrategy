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
    public sealed class RationsTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);

            internal void Step(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
        }

        private static ScenarioDefinition Scenario(bool rations)
        {
            var scenario = MapGenerator.GenerateTerrain(7301UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.Rations = rations;
            scenario.Economy.StartFood = 3;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 0;
            scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.Age2Ticks = 1;
            scenario.Economy.MillWoodCost = 1;
            scenario.Economy.MillWork = 1;
            scenario.Economy.MillTicks = 80;
            scenario.Economy.MillFoodInput = 3;
            scenario.Economy.RationsFarmMinimum = 1;
            scenario.Economy.RationsFoodReserve = 1;
            scenario.Economy.BarracksWoodCost = 1;
            scenario.Economy.BarracksWork = 1;
            scenario.Economy.InfantryFoodCost = 0;
            scenario.Economy.InfantryWoodCost = 0;
            scenario.Economy.InfantryTrainTicks = 100;
            scenario.Economy.AgrarianInfantryTicks = 100;
            scenario.Economy.VillagerFoodCost = 0;
            scenario.Economy.ScoutFoodCost = 0;
            return scenario;
        }

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static void ReachAgrarian(Fixture fixture)
        {
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, CivKind.Agrarian));
            fixture.Step(2);
            Assert.That(Fields(fixture.Simulation)["Economy[1].Civ"], Is.EqualTo(((byte)CivKind.Agrarian).ToString(CultureInfo.InvariantCulture)));
        }

        private static void ReachCivilisation(Fixture fixture, CivKind civ)
        {
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, civ));
            fixture.Step(2);
        }

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            int count = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                int after = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
                if (after == count + 1) return (uint)after;
            }
            Assert.Fail("合法な建設地点が見つからない: " + kind);
            return 0;
        }

        private static void PlaceAt(Fixture fixture, BuildingKind kind, int cell)
        {
            int count = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
            fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Buildings.Count"), Is.EqualTo(count + 1));
        }

        private static void Build(Fixture fixture, uint buildingId)
        {
            fixture.Send(EconomyCommand.Assign(1, ++fixture.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, buildingId));
            for (int i = 0; i < 1500 && Fields(fixture.Simulation)["Buildings[" + buildingId + "].Complete"] != "1"; i++) fixture.Step(1);
            Assert.That(Fields(fixture.Simulation)["Buildings[" + buildingId + "].Complete"], Is.EqualTo("1"));
        }

        private static void CollectMillOutput(Fixture fixture, uint millId, int ticks)
        {
            fixture.Send(EconomyCommand.Assign(1, ++fixture.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, millId));
            fixture.Step(ticks);
        }

        [Test]
        public void RationsOffRejectsMillAndRationsLineWithoutChangingStateOrHash()
        {
            var fixture = new Fixture(Scenario(false));
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Mill, 0, Facing.North));
            fixture.Send(EconomyCommand.RequestLineAt(1, ++fixture.Sequence, ProcessingLineKind.Rations, 0));
            fixture.Step(1);
            var after = fixture.Simulation.CaptureDiagnostic();
            var control = new Fixture(Scenario(false));
            control.Send(EconomyCommand.Place(1, ++control.Sequence, BuildingKind.Mill, 0, Facing.North));
            control.Send(EconomyCommand.RequestLineAt(1, ++control.Sequence, ProcessingLineKind.Rations, 0));
            control.Step(1);
            Assert.That(after.CanonicalState, Is.EqualTo(control.Simulation.CaptureDiagnostic().CanonicalState));
            Assert.That(ReplayBinary.Hash(after.CanonicalState.ToArray()), Is.EqualTo(
                ReplayBinary.Hash(control.Simulation.CaptureDiagnostic().CanonicalState.ToArray())));
            Assert.That(Number(Fields(fixture.Simulation), "Buildings.Count"), Is.EqualTo(0));
            Assert.That(Fields(fixture.Simulation).ContainsKey("Economy[1].Rations"), Is.False);
        }

        [Test]
        public void RationsExtensionRoundTripsChangedValuesAndRecordedReplayMatchesEveryTick()
        {
            var scenario = Scenario(true);
            scenario.Economy.MillTicks = 91;
            scenario.Economy.RationsTrainPermille = 731;
            scenario.Economy.RationsHealQuietTicks = 217;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 20).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.MillTicks, Is.EqualTo(91));
            Assert.That(decoded.Economy.RationsTrainPermille, Is.EqualTo(731));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var input = new ScheduledInput(1, 0, 1, EconomyCommand.RequestLineAt(1, 1, ProcessingLineKind.Rations, 0));
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, new[] { input }, 140, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }

            var left = new Battle(scenario);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)));
            for (long tick = 1; tick <= 140; tick++)
            {
                var inputs = tick == 1 ? new[] { input } : Array.Empty<ScheduledInput>();
                left.Step(tick, inputs);
                right.Step(tick, inputs);
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }

        [Test]
        public void MillUsesThreeFoodForOneRationInEightyTicksAndStopsUntilFoodReturns()
        {
            var fixture = new Fixture(Scenario(true));
            ReachAgrarian(fixture);
            uint mill = Place(fixture, BuildingKind.Mill);
            Build(fixture, mill);
            fixture.Step(79);
            var before = Fields(fixture.Simulation);
            Assert.That(Number(before, "Economy[1].RationsProduced"), Is.EqualTo(0));
            fixture.Step(3);
            var produced = Fields(fixture.Simulation);
            Assert.That(Number(produced, "Economy[1].RationsProduced"), Is.EqualTo(1));
            Assert.That(Number(produced, "Economy[1].Food"), Is.EqualTo(0));
            fixture.Step(100);
            var stopped = Fields(fixture.Simulation);
            Assert.That(Number(stopped, "Economy[1].RationsProduced"), Is.EqualTo(1));

            int foodNode = fixture.Scenario.ResourceNodes.Select((n, i) => new { Node = n, Index = i + 1 })
                .Where(x => x.Node.Kind == ResourceKind.Food).Select(x => x.Index).First();
            fixture.Send(EconomyCommand.Assign(1, ++fixture.Sequence, new uint[] { 1 }, EconomyTargetKind.ResourceNode, (uint)foodNode));
            for (int i = 0; i < 2000 && Number(Fields(fixture.Simulation), "Economy[1].RationsProduced") == 1; i++) fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].RationsProduced"), Is.GreaterThan(1));

            var other = new Fixture(Scenario(true));
            ReachCivilisation(other, CivKind.Metallurgy);
            int legalCell = (int)Number(before, "Buildings[" + mill + "].OriginCell");
            int otherBefore = (int)Number(Fields(other.Simulation), "Buildings.Count");
            other.Send(EconomyCommand.Place(1, ++other.Sequence, BuildingKind.Mill, legalCell, Facing.North));
            other.Step(1);
            Assert.That(Number(Fields(other.Simulation), "Buildings.Count"), Is.EqualTo(otherBefore));
        }

        [Test]
        public void RationsShortenSoldierTrainingAndDoNotMarkVillagerOrScout()
        {
            var trainingScenario = Scenario(true);
            trainingScenario.Economy.StartFood = 1200;
            for (int i = 0; i < trainingScenario.Soldiers.Length; i++)
            {
                var soldier = trainingScenario.Soldiers[i];
                if (soldier.FactionId == 1)
                {
                    soldier.Alive = false;
                    soldier.Hp = 0;
                    trainingScenario.Soldiers[i] = soldier;
                }
            }
            var fixture = new Fixture(trainingScenario);
            ReachAgrarian(fixture);
            uint mill = Place(fixture, BuildingKind.Mill);
            Build(fixture, mill);
            CollectMillOutput(fixture, mill, 2000);
            var outputFields = Fields(fixture.Simulation);
            var stock = Number(outputFields, "Economy[1].Rations");
            Assert.That(stock, Is.GreaterThanOrEqualTo(1));
            uint barracks = Place(fixture, BuildingKind.Barracks);
            Build(fixture, barracks);
            fixture.Send(EconomyCommand.Train(1, ++fixture.Sequence, barracks, UnitKind.Infantry));
            fixture.Step(1);
            var queued = Fields(fixture.Simulation);
            Assert.That(queued["Buildings[" + barracks + "].QueueRations[0]"], Is.EqualTo("1"));
            Assert.That(Number(queued, "Economy[1].Rations"), Is.EqualTo(stock - 1));
            Assert.That(Number(queued, "Buildings[" + barracks + "].TrainRemaining"), Is.EqualTo(69));

            fixture.Send(EconomyCommand.Train(1, ++fixture.Sequence, barracks, UnitKind.Scout));
            fixture.Send(EconomyCommand.Train(1, ++fixture.Sequence, 0, UnitKind.Villager));
            fixture.Step(1);
            var nonSoldiers = Fields(fixture.Simulation);
            Assert.That(nonSoldiers["Buildings[" + barracks + "].QueueRations[1]"], Is.EqualTo("0"));
            Assert.That(Number(nonSoldiers, "Economy[1].Rations"), Is.EqualTo(stock - 1));

            var ordinaryScenario = Scenario(true);
            ordinaryScenario.Economy.StartFood = 1200;
            for (int i = 0; i < ordinaryScenario.Soldiers.Length; i++)
            {
                var soldier = ordinaryScenario.Soldiers[i];
                if (soldier.FactionId == 1)
                {
                    soldier.Alive = false;
                    soldier.Hp = 0;
                    ordinaryScenario.Soldiers[i] = soldier;
                }
            }
            var ordinary = new Fixture(ordinaryScenario);
            ReachAgrarian(ordinary);
            uint ordinaryBarracks = Place(ordinary, BuildingKind.Barracks);
            Build(ordinary, ordinaryBarracks);
            ordinary.Send(EconomyCommand.Train(1, ++ordinary.Sequence, ordinaryBarracks, UnitKind.Infantry));
            ordinary.Step(1);
            Assert.That(Number(Fields(ordinary.Simulation), "Buildings[" + ordinaryBarracks + "].TrainRemaining"), Is.EqualTo(99));
        }

        [Test]
        public void RationsHealingUsesIdOrderQuietTimeAndTenBodyIntervalCap()
        {
            var scenario = Scenario(true);
            scenario.Economy.StartFood = 1200;
            scenario.Economy.MillTicks = 1;
            scenario.Economy.MillFoodInput = 1;
            scenario.Economy.RationsHealIntervalTicks = 200;
            scenario.Economy.RationsHealQuietTicks = 0;
            scenario.Economy.RationsHealHp = 10;
            scenario.Economy.RationsHealMaxPerInterval = 10;
            for (int i = 0; i < scenario.UnitParameters.Length; i++)
            {
                var parameters = scenario.UnitParameters[i];
                parameters.Damage = 0;
                parameters.Vision = Fix64.FromInt(0);
                parameters.Range = Fix64.FromInt(0);
                scenario.UnitParameters[i] = parameters;
            }
            for (int i = 0; i < scenario.Soldiers.Length; i++)
            {
                var soldier = scenario.Soldiers[i];
                if (soldier.FactionId == 1) soldier.Hp = 1;
                else
                {
                    if (soldier.Id != 21)
                    {
                        soldier.Alive = false;
                        soldier.Hp = 0;
                    }
                    soldier.Position = new SimPoint(Fix64.FromInt(250), Fix64.FromInt(126));
                }
                scenario.Soldiers[i] = soldier;
            }
            var fixture = new Fixture(scenario);
            ReachAgrarian(fixture);
            uint mill = Place(fixture, BuildingKind.Mill);
            Build(fixture, mill);
            CollectMillOutput(fixture, mill, 6000);
            var before = Fields(fixture.Simulation);
            long healedBefore = Number(before, "Economy[1].RationsHealedHp");
            long consumedBefore = Number(before, "Economy[1].RationsConsumed");
            Assert.That(Number(before, "Economy[1].Rations"), Is.GreaterThanOrEqualTo(10));
            fixture.Step(8);
            var healed = Fields(fixture.Simulation);
            long healedDelta = Number(healed, "Economy[1].RationsHealedHp") - healedBefore;
            long consumedDelta = Number(healed, "Economy[1].RationsConsumed") - consumedBefore;
            Assert.That(healedDelta, Is.GreaterThan(0).And.LessThanOrEqualTo(100));
            Assert.That(consumedDelta, Is.GreaterThan(0).And.LessThanOrEqualTo(10));
            for (int id = 1; id <= 10; id++)
                if (healed.ContainsKey("Soldiers[" + id + "].FactionId") && healed["Soldiers[" + id + "].FactionId"] == "1")
                {
                    int previousHp = (int)Number(before, "Soldiers[" + id + "].Hp");
                    int currentHp = (int)Number(healed, "Soldiers[" + id + "].Hp");
                    Assert.That(currentHp, Is.GreaterThanOrEqualTo(previousHp), "soldier " + id);
                }
            var atBoundary = Number(healed, "Economy[1].RationsHealedHp");
            fixture.Step(199);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].RationsHealedHp"), Is.EqualTo(atBoundary));
            fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].RationsHealedHp"), Is.GreaterThanOrEqualTo(atBoundary));
        }

        [Test, Explicit("農耕のお任せを長時間回し、製粉所・糧食・訓練まで確認する長時間テスト")]
        public void AgrarianAutomaticEconomyBuildsMillAndUsesRations()
        {
            var scenario = LiveGameRules.Create(6UL, false, true, false, false);
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 30000 && !simulation.Capture(1).Result.HasEnded; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var snapshot = simulation.CaptureMatchPackSnapshot();
            var agrarian = snapshot.Factions.Single(f => f.Civ == CivKind.Agrarian.ToString());
            Assert.That(agrarian.Buildings.Single(b => b.Kind == BuildingKind.Mill.ToString()).Count, Is.GreaterThan(0));
            Assert.That(agrarian.RationsProduced, Is.GreaterThan(0));
        }
    }
}
