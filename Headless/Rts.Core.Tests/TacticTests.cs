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
