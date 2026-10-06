using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Rts.Tactics;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
public sealed class TacticTests
{
    [Test]
    public void ViewIsCanonicalAndDoesNotExposeInvisibleEnemies()
    {
        var frame = Frame(new[] { new VisibleEnemy(99, new SimPoint(Fix(4), Fix(2)), 1) });
        string first = TacticViewWriter.Write(frame); string second = TacticViewWriter.Write(frame);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(first.IndexOf("\"ownArmies\"", StringComparison.Ordinal), Is.LessThan(first.IndexOf("\"visibleEnemies\"", StringComparison.Ordinal)));
        Assert.That(first, Does.Contain("\"id\":99"));
        var hidden = Frame(Array.Empty<VisibleEnemy>());
        Assert.That(TacticViewWriter.Write(hidden), Does.Not.Contain("\"id\":99"));
    }

    [Test]
    public void ViewAddsKindNamesCompositionsSummaryAndKeepsThemDeterministic()
    {
        var armies = new[]
        {
            new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix(0), Fix(0)), 2, default),
            new OwnArmyView(2, 1, UnitKind.Archer, new SimPoint(Fix(10), Fix(0)), 1, default)
        };
        var enemies = new[]
        {
            new VisibleEnemy(11, new SimPoint(Fix(5), Fix(5)), (byte)UnitKind.Archer),
            new VisibleEnemy(12, new SimPoint(Fix(6), Fix(5)), (byte)UnitKind.Infantry)
        };
        var contacts = new[] { new EnemyContact(20, new SimPoint(Fix(5), Fix(5)), 0, 2, 2, true, new[] { 11U, 12U }) };
        var frame = new FactionFrame(0, 1,
            new[]
            {
                new RenderUnit(101, true, UnitKind.Infantry, new SimPoint(Fix(1), Fix(0)), false, false, false, true, 10),
                new RenderUnit(102, true, UnitKind.Archer, new SimPoint(Fix(9), Fix(0)), false, false, false, true, 10),
                new RenderUnit(103, true, UnitKind.Infantry, new SimPoint(Fix(0), Fix(1)), false, false, false, true, 10)
            },
            new FactionObservation(1, 0, armies, enemies, contacts, Array.Empty<KnownObjective>()),
            Array.Empty<CommandView>(), Array.Empty<GameEvent>(), new FogView(new[] { true }, new[] { true }),
            new MatchResult(false, 0, false, false, true));

        string first = TacticViewWriter.Write(frame);
        Assert.That(first, Is.EqualTo(TacticViewWriter.Write(frame)));
        using (var doc = System.Text.Json.JsonDocument.Parse(first))
        {
            var root = doc.RootElement;
            Assert.That(root.GetProperty("visibleEnemies")[0].GetProperty("kindName").GetString(), Is.EqualTo("Archer"));
            Assert.That(root.GetProperty("ownArmies")[0].GetProperty("composition").GetProperty("Infantry").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("ownArmies")[1].GetProperty("composition").GetProperty("Archer").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("contacts")[0].GetProperty("visibleComposition").GetProperty("Archer").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("enemySummary").GetProperty("visibleCount").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("enemySummary").GetProperty("byKind").GetProperty("Infantry").GetInt32(), Is.EqualTo(1));
        }
        Assert.That(first, Does.Not.Contain("\"id\":99"));
    }

    [Test]
    public void ViewShowsLiveHumanOrdersAndControlledByWithoutTheOtherFaction()
    {
        var sim = new Battle(MapGenerator.Generate(2, true));
        var gateway = new CommandGateway(sim);
        var westArmy = sim.Capture(1).Observation.OwnArmies.OrderBy(a => a.Id).First();
        var eastArmy = sim.Capture(2).Observation.OwnArmies.OrderBy(a => a.Id).First();
        ulong westRequest = gateway.Submit(new UserPolicyIntent(1,
            new ScopeKey(1, ScopeKind.Army, westArmy.Id), PolicyKind.Defend, new PolicyGoal(GoalKind.Core, 1, default), 80,
            new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
            new Expiration(long.MaxValue, 0, ExpireFlags.None)));
        gateway.Submit(new UserPolicyIntent(1,
            new ScopeKey(2, ScopeKind.Army, eastArmy.Id), PolicyKind.Defend, new PolicyGoal(GoalKind.Core, 2, default), 80,
            new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
            new Expiration(long.MaxValue, 0, ExpireFlags.None)));
        for (int i = 0; i < 45; i++) gateway.Step();

        var frame = sim.Capture(1);
        Assert.That(frame.Commands.Any(c => c.Source == CommandSource.Human && c.Status == CommandStatus.Executing), Is.True);
        string first = TacticViewWriter.Write(frame);
        string second = TacticViewWriter.Write(frame);
        Assert.That(second, Is.EqualTo(first));
        using (var doc = System.Text.Json.JsonDocument.Parse(first))
        {
            var root = doc.RootElement;
            var orders = root.GetProperty("orders");
            Assert.That(orders.GetArrayLength(), Is.EqualTo(1));
            Assert.That(orders[0].GetProperty("source").GetString(), Is.EqualTo("Human"));
            Assert.That(orders[0].GetProperty("status").GetString(), Is.EqualTo("Executing"));
            Assert.That(orders[0].GetProperty("target").GetProperty("kind").GetString(), Is.EqualTo("Army"));
            var army = root.GetProperty("ownArmies").EnumerateArray().Single(a => a.GetProperty("id").GetUInt32() == westArmy.Id);
            Assert.That(army.GetProperty("controlledBy").GetString(), Is.EqualTo("Human"));
        }
        Assert.That(frame.Commands.Any(c => c.Target.FactionId != frame.FactionId), Is.False);

        gateway.Cancel(westRequest);
        gateway.Step();
        using (var doc = System.Text.Json.JsonDocument.Parse(TacticViewWriter.Write(sim.Capture(1))))
        {
            Assert.That(doc.RootElement.GetProperty("orders").GetArrayLength(), Is.EqualTo(0));
            var army = doc.RootElement.GetProperty("ownArmies").EnumerateArray().Single(a => a.GetProperty("id").GetUInt32() == westArmy.Id);
            Assert.That(army.GetProperty("controlledBy").GetString(), Is.EqualTo("None"));
        }
    }

    [Test]
    public void AgeViewGuidesAdvanceAndAdvanceCommandChangesAge()
    {
        var scenario = MapGenerator.GenerateTerrain(1);
        scenario.Economy.Ages = true;
        scenario.Economy.StartFood = 1000;
        scenario.Economy.StartWood = 1000;
        scenario.Economy.AdvanceFoodCost = 0;
        scenario.Economy.AdvanceWoodCost = 0;
        scenario.Economy.AdvanceTicks = 1;
        var sim = new Battle(scenario);
        var gateway = new CommandGateway(sim);
        var before = sim.Capture(1);
        var view = TacticViewWriter.Write(before);
        using (var doc = System.Text.Json.JsonDocument.Parse(view))
        {
            var economy = doc.RootElement.GetProperty("economy");
            Assert.That(economy.GetProperty("agesEnabled").GetBoolean(), Is.True);
            Assert.That(economy.GetProperty("nextAgeCost").GetProperty("food").GetInt32(), Is.EqualTo(0));
            Assert.That(economy.GetProperty("canAdvanceNow").GetBoolean(), Is.True);
        }

        var result = TacticCommandReader.Read("{\"version\":1,\"commands\":[{\"type\":\"economy\",\"kind\":\"AdvanceAge\",\"sequence\":1,\"civ\":\"Agrarian\"}]}", before);
        Assert.That(result.Rejected, Is.Empty);
        gateway.SubmitEconomy(result.EconomyCommands[0]);
        gateway.Step();
        var after = sim.Capture(1).Economy;
        Assert.That(after.AdvancingTo == CivKind.Agrarian || after.Age > 0, Is.True);
        Assert.That(after.AdvanceRemaining, Is.GreaterThanOrEqualTo(0));
        for (int i = 0; i < 3 && sim.Capture(1).Economy.Age == 0; i++) gateway.Step();
        Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThan(0));
    }

    [Test]
    public void ReaderBuildsAiPolicyAndRejectsBadCommandIndividually()
    {
        var frame = Frame(Array.Empty<VisibleEnemy>());
        string json = "{\"version\":1,\"commands\":[" +
            "{\"type\":\"policy\",\"kind\":\"Focus\",\"target\":{\"kind\":\"Army\",\"id\":7},\"goal\":{\"kind\":\"Core\",\"id\":2},\"priority\":80,\"allowedLossPermille\":500,\"reservePermille\":0}," +
            "{\"type\":\"mystery\"}," +
            "{\"type\":\"policy\",\"kind\":\"Focus\",\"target\":{\"kind\":\"Army\",\"id\":999},\"goal\":{\"kind\":\"Core\",\"id\":2}}]}";
        var result = TacticCommandReader.Read(json, frame);
        Assert.That(result.Policies, Has.Count.EqualTo(1));
        Assert.That(result.Policies[0].Source, Is.EqualTo(CommandSource.Ai));
        Assert.That(result.Policies[0].ObservedTick, Is.EqualTo(0));
        Assert.That(result.Policies[0].AllowedLoss.Permille, Is.EqualTo(500));
        Assert.That(result.Rejected, Has.Count.EqualTo(2));
    }

    [Test]
    public void BrokenJsonMeansNoCommands()
    {
        var result = TacticCommandReader.Read("{broken", Frame(Array.Empty<VisibleEnemy>()));
        Assert.That(result.IsMalformed, Is.True);
        Assert.That(result.Policies, Is.Empty);
        Assert.That(result.EconomyCommands, Is.Empty);
    }

    [Test]
    public void HostStopsAfterTenRuntimeFailures()
    {
        var source = new AdvancingFrameSource(); var port = new RecordingPort(); var runtime = new ThrowingRuntime();
        var host = new TacticHost(1, source, port, port, runtime);
        for (int i = 0; i < 10; i++) host.Tick();
        Assert.That(host.Failures, Has.Count.EqualTo(10));
        Assert.That(host.Disabled, Is.True);
        Assert.That(runtime.Calls, Is.EqualTo(10));
        Assert.That(port.Proposals, Is.EqualTo(0));
    }

    [Test]
    public void RushAgainstAutoIsValidJsonSendsOrdersAndReplays()
    {
        var scenario = MapGenerator.Generate(1, true);
        var sim = new Battle(scenario);
        var gateway = new CommandGateway(sim);
        var source = new SimulationFrames(sim);
        var host = new TacticHost(1, source, gateway, gateway, new RushTactic());
        int called = 0, sentPolicies = 0, sentEconomy = 0, sawEnemy = 0;
        for (int i = 0; i < 2400 && !sim.Capture(1).Result.HasEnded; i++)
        {
            var r = host.Tick();
            if (r.Called)
            {
                called++;
                using (var doc = System.Text.Json.JsonDocument.Parse(r.ViewJson))
                {
                    Assert.That(doc.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(1));
                    if (doc.RootElement.GetProperty("visibleEnemies").GetArrayLength() > 0) sawEnemy++;
                }
                Assert.That(r.Failure, Is.Null, r.Failure?.Reason);
                sentPolicies += r.SentPolicies; sentEconomy += r.SentEconomy;
            }
            gateway.Step();
        }
        Assert.That(called, Is.GreaterThan(100));
        Assert.That(sentPolicies, Is.GreaterThan(0), "rush must send policies");
        Assert.That(sentEconomy, Is.GreaterThan(0), "rush must send economy commands");
        Assert.That(host.Disabled, Is.False);

        using (var stream = new MemoryStream())
        {
            var build = new BuildIdentity();
            ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, build);
            stream.Position = 0;
            var outcome = ReplayRunner.Replay(stream, build);
            Assert.That(outcome.FirstMismatchTick, Is.Null);
            Assert.That(outcome.IsFault, Is.False);
        }
    }

    /// <summary>
    /// Sending is not enough: the simulation must take the tactic's orders. Without the current policy versions every
    /// order after the first revision was dropped as StaleVersion and a tactic played exactly like no tactic (10-06).
    /// </summary>
    [Test]
    public void TacticOrdersAreTakenByTheSimulationNotDroppedAsStale()
    {
        var sim = new Battle(MapGenerator.Generate(2, true));
        var gateway = new CommandGateway(sim);
        var host = new TacticHost(1, new SimulationFrames(sim), gateway, gateway, new RushTactic(),
            versions: scope => gateway.FactionVersions(1).Versions(scope));
        var seen = new Dictionary<ulong, CommandView>();
        for (int i = 0; i < 2400 && !sim.Capture(1).Result.HasEnded; i++)
        {
            host.Tick();
            gateway.Step();
            foreach (var c in sim.Capture(1).Commands) if (c.Source == CommandSource.Ai) seen[c.CommandId] = c;
        }
        Assert.That(seen.Count, Is.GreaterThan(10), "the tactic's orders must show up as commands");
        int stale = seen.Values.Count(c => c.Reason == ReasonCode.StaleVersion);
        int taken = seen.Values.Count(c => c.Status == CommandStatus.Executing || c.Status == CommandStatus.Completed
            || c.Reason == ReasonCode.Superseded);
        Assert.That(stale, Is.EqualTo(0), "orders dropped as StaleVersion");
        Assert.That(taken, Is.GreaterThan(0), "no order of the tactic was ever executed");
    }

    [Test]
    public void SameTacticMatchProducesTheSameInputs()
    {
        string Run()
        {
            var sim = new Battle(MapGenerator.Generate(2, true));
            var gateway = new CommandGateway(sim);
            var host = new TacticHost(2, new SimulationFrames(sim), gateway, gateway, new RushTactic());
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 1200; i++) { var r = host.Tick(); if (r.Called) log.Append(r.ViewJson).Append('|').Append(r.CommandJson).Append('\n'); gateway.Step(); }
            log.Append(gateway.Inputs.Count());
            return log.ToString();
        }
        Assert.That(Run(), Is.EqualTo(Run()));
    }

    private sealed class SimulationFrames : IFrameSource
    {
        private readonly Battle sim;
        internal SimulationFrames(Battle sim) { this.sim = sim; }
        public FactionFrame Latest(uint factionId) => sim.Capture(factionId);
    }

    private static FactionFrame Frame(IReadOnlyList<VisibleEnemy> enemies)
    {
        var own = new[] { new OwnArmyView(7, 1, UnitKind.Infantry, new SimPoint(Fix(1), Fix(2)), 10, default(PolicyGoal)) };
        var objectives = new[] { new KnownObjective(GoalKind.Core, 1, new SimPoint(Fix(1), Fix(2)), true, 1, true, 1000, 0), new KnownObjective(GoalKind.Core, 2, new SimPoint(Fix(10), Fix(2)), true, 2, true, 1000, 0) };
        var observation = new FactionObservation(1, 0, own, enemies, Array.Empty<EnemyContact>(), objectives);
        return new FactionFrame(0, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(), Array.Empty<GameEvent>(), new FogView(new[] { true }, new[] { true }), new MatchResult(false, 0, false, false, true));
    }
    private static Fix64 Fix(int value) => Fix64.FromInt(value);

    private sealed class AdvancingFrameSource : IFrameSource
    {
        private long tick;
        public FactionFrame Latest(uint factionId) { var frame = Frame(Array.Empty<VisibleEnemy>()); return new FactionFrame(tick++ * 20, 1, frame.Units, frame.Observation, frame.Commands, frame.Events, frame.Fog, frame.Result); }
    }
    private sealed class RecordingPort : ICommandPort, IEconomyPort
    {
        public int Proposals; public ulong Submit(UserPolicyIntent intent) => 0; public void Cancel(ulong requestId) { }
        public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick) { Proposals++; return 1; }
        public void SubmitEconomy(EconomyCommand command) { }
    }
    private sealed class ThrowingRuntime : ITacticRuntime
    {
        public int Calls; public string Name => "throwing"; public void Start(string setupJson) { }
        public string Tick(string viewJson) { Calls++; throw new InvalidOperationException("test failure"); }
    }
}
}
