using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Core.Tests
{
    public sealed class OperationTableTests
    {
        private static FactionFrame Frame(long tick, uint owner = 1, int soldiers = 10, VisibleEnemy[] enemies = null)
        {
            var objective = new KnownObjective(GoalKind.Outpost, 1,
                new SimPoint(Fix64.FromInt(0), Fix64.FromInt(0)), true, owner, false, 0, tick);
            var observation = new FactionObservation(1, tick,
                new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(0), Fix64.FromInt(0)), soldiers, default(PolicyGoal)) },
                enemies ?? Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), new[] { objective });
            return new FactionFrame(tick, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), new MatchResult(false, 0, false, false, true));
        }

        private static OperationDefinition Plan(OperationCondition condition, OperationSource source = OperationSource.Human)
        {
            var intent = new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.All, 0), PolicyKind.Retreat,
                default(PolicyGoal), 100, new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(1000, 240, ExpireFlags.ObservationTooOld));
            return new OperationDefinition(condition, new[] { OperationAction.FromPolicy(intent) }, true, source);
        }

        [Test]
        public void OwnershipEnemyAndSelfChangesAreEdges()
        {
            var table = new OperationTable(1);
            var add = table.Add(Plan(OperationCondition.OwnerChangedToEnemy("北の拠点", 1)), Frame(0));
            Assert.That(add.Accepted, Is.True);
            Assert.That(table.Evaluate(Frame(1, 2)).Fired, Has.Count.EqualTo(1));
            Assert.That(table.Evaluate(Frame(2, 2)).Fired, Is.Empty);
            Assert.That(table.Evaluate(Frame(3, 1)).Fired, Is.Empty);
            Assert.That(table.Evaluate(Frame(4, 2)).Fired, Is.Empty, "once=true は再発火しない");

            var second = new OperationTable(1);
            second.Add(Plan(OperationCondition.OwnerChangedToSelf("北の拠点", 1)), Frame(0, 2));
            Assert.That(second.Evaluate(Frame(1, 1)).Fired, Has.Count.EqualTo(1));
        }

        [Test]
        public void NearbyEnemiesAndArmyPercentageUseOnlyObservation()
        {
            var enemy = new[] { new VisibleEnemy(4, new SimPoint(Fix64.FromInt(3), Fix64.FromInt(4)), 1) };
            var table = new OperationTable(1);
            table.Add(Plan(OperationCondition.EnemyNear("北の拠点", 1, 1)), Frame(0, 1, 10));
            Assert.That(table.Evaluate(Frame(1, 1, 10, enemy)).Fired, Has.Count.EqualTo(1));

            var army = new OperationTable(1);
            army.Add(Plan(OperationCondition.OwnArmyBelowPercent(50)), Frame(0, 1, 10));
            Assert.That(army.Evaluate(Frame(1, 1, 5)).Fired, Has.Count.EqualTo(1));
        }

        [Test]
        public void TimeAndAndConditionsFireAtTheRisingEdge()
        {
            var table = new OperationTable(1);
            table.Add(Plan(OperationCondition.AllOf(OperationCondition.TimeAfterSeconds(10), OperationCondition.OwnArmyBelowPercent(50))), Frame(0, 1, 10));
            Assert.That(table.Evaluate(Frame(199, 1, 5)).Fired, Is.Empty);
            Assert.That(table.Evaluate(Frame(201, 1, 5)).Fired, Has.Count.EqualTo(1));
        }

        [Test]
        public void JudgementNeedsSixTenthsConfidence()
        {
            var table = new OperationTable(1);
            table.Add(Plan(OperationCondition.Judgement("operation_north_broken")), Frame(0));
            var low = new JevAnswers(); low.Noul["operation_north_broken"] = 0.59;
            Assert.That(table.Evaluate(Frame(1), low).Fired, Is.Empty);
            var high = new JevAnswers(); high.Noul["operation_north_broken"] = 0.60;
            Assert.That(table.Evaluate(Frame(2), high).Fired, Has.Count.EqualTo(1));
        }

        [Test]
        public void NonOnceOperationWaitsForFalseBeforeRefiring()
        {
            var table = new OperationTable(1);
            var definition = new OperationDefinition(OperationCondition.OwnArmyBelowPercent(50),
                new[] { OperationAction.FromPolicy(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.All, 0), PolicyKind.Retreat, default(PolicyGoal), 100, new LossBudget(1000), default(EndCondition), 0, default(Expiration))) }, false, OperationSource.Ai);
            table.Add(definition, Frame(0, 1, 10));
            Assert.That(table.Evaluate(Frame(1, 1, 5)).Fired, Has.Count.EqualTo(1));
            Assert.That(table.Evaluate(Frame(2, 1, 6)).Fired, Is.Empty);
            Assert.That(table.Evaluate(Frame(3, 1, 5)).Fired, Has.Count.EqualTo(1));
        }

        [Test]
        public void LimitAndCancelAreVisible()
        {
            var table = new OperationTable(1);
            var first = table.Add(Plan(OperationCondition.TimeAfterTick(1)), Frame(0));
            Assert.That(table.Cancel(first.Id), Is.True);
            Assert.That(table.Operations[0].State, Is.EqualTo(OperationState.Cancelled));
            for (int i = 0; i < OperationTable.MaxOperations; i++) table.Add(Plan(OperationCondition.TimeAfterTick(100 + i)), Frame(0));
            Assert.That(table.Add(Plan(OperationCondition.TimeAfterTick(999)), Frame(0)).Accepted, Is.False);
        }

        [Test]
        public void InterpreterTurnsEvaluationShapeIntoAnOperation()
        {
            var frame = Frame(0);
            var summary = AiSituationSummary.From(frame);
            var result = AiResponseInterpreter.Interpret(
                "{\"commands\":[],\"operations\":[{\"when\":{\"kind\":\"EnemyNear\",\"objective\":\"北の拠点\",\"count\":1},\"then\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"}],\"once\":true}],\"say\":\"\"}",
                new AiInterpretationContext { Frame = frame, Summary = summary, StartedTick = 0, DeadlineTick = 240 });
            Assert.That(result.Operations, Has.Count.EqualTo(1));
            Assert.That(result.Operations[0].Then, Has.Count.EqualTo(1));
        }
    }
}
