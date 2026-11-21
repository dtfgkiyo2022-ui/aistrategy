using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    // 11-20 play: the late push numbered its Doctrine commands inside the simulation, the gateway numbered the human
    // orders from its own cursor, and every human army order after the late push began was dropped as StaleVersion.
    public sealed class HumanOrderIdsTests
    {
        private static ScenarioDefinition Scenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.LatePush = true;
            scenario.Economy.LatePushAfterTicks = 1;
            scenario.Economy.LatePushAdvantageAfterTicks = 100000;
            return scenario;
        }

        private static UserPolicyIntent CoreIntent(ulong sequence, ScenarioDefinition scenario, uint army)
        {
            uint enemyCore = scenario.Factions.First(f => f.Id == 2).CoreId;
            return new UserPolicyIntent(sequence, new ScopeKey(1, ScopeKind.Army, army), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, enemyCore, default), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None));
        }

        [Test]
        public void HumanOrderAfterLatePushReachesTheArmy()
        {
            var scenario = Scenario();
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            for (int i = 0; i < 3; i++) gateway.Step();
            var lateCommands = simulation.Capture(1).Commands.Where(c => c.Source == CommandSource.Doctrine).Select(c => c.CommandId).ToArray();
            Assert.That(lateCommands, Is.Not.Empty, "the late push must have numbered a command inside the simulation");

            uint army = scenario.Armies.Where(a => a.FactionId == 1).OrderBy(a => a.Id).First().Id;
            gateway.Submit(CoreIntent(1, scenario, army));
            bool executing = false;
            for (int i = 0; i < 60 && !executing; i++)
            {
                gateway.Step();
                executing = simulation.Capture(1).Commands.Any(c => c.Source == CommandSource.Human
                    && c.Target.Equals(new ScopeKey(1, ScopeKind.Army, army)) && c.Status == CommandStatus.Executing);
            }
            Assert.That(executing, Is.True, "the human order must not be dropped as a stale version");
            var human = simulation.Capture(1).Commands.First(c => c.Source == CommandSource.Human);
            Assert.That(lateCommands, Does.Not.Contain(human.CommandId));
        }

        [Test]
        public void GatewayWithoutHumanOrdersNumbersAsBefore()
        {
            // Only a human order moves the gateway's cursors, so a proposal is numbered from the gateway alone.
            var scenario = Scenario();
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            for (int i = 0; i < 3; i++) gateway.Step();
            Assert.That(simulation.NextCommandId, Is.GreaterThan(1UL));
            var target = new ScopeKey(2, ScopeKind.All, 0);
            var order = new PolicyOrder(0, 0, CommandSource.Ai, target, PolicyKind.MaintainReserve, default(PolicyGoal), 50,
                new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100, simulation.Revision(target),
                simulation.Versions(target).Where(v => !v.Scope.Equals(target)).ToArray(), 3,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));
            gateway.Propose(2, 1, new[] { order }, 10);
            var proposal = gateway.Step().Single(i => i.Kind == InputKind.Proposal);
            Assert.That(proposal.Orders[0].CommandId, Is.EqualTo(1UL));
        }
    }
}
