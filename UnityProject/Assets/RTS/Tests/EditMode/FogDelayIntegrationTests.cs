using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    /// <summary>Issue 3-7: over whole matches, the AI does not act on unseen enemies and delays never stall a match.</summary>
    public sealed class FogDelayIntegrationTests
    {
        private static long Raw(Dictionary<string, string> fields, string key) => long.Parse(fields[key], System.Globalization.CultureInfo.InvariantCulture);

        private static Dictionary<string, string> FieldMap(Simulation.Simulation sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        [Test]
        public void EveryEnemyTargetWasVisibleToItsAttackerWhenTheTargetWasChosen()
        {
            var scenario = WeekTwoScenario.Create();
            var map = scenario.Map;
            long cellRaw = (long)map.CellSizeMeters * 65536L;
            var sim = new Simulation.Simulation(scenario);
            var gateway = new CommandGateway(sim);
            var west = PolicyPresets.CreateController("maintain", 1, gateway);
            var east = PolicyPresets.CreateController("maintain", 2, gateway);
            west.Initialize(); east.Initialize();

            var previous = FieldMap(sim);
            var previousVisible = new[] { sim.Capture(1).Fog.VisibleCells, sim.Capture(2).Fog.VisibleCells };
            int checkedTargets = 0;

            for (long tick = 1; tick <= 4000 && !sim.Capture(1).Result.HasEnded; tick++)
            {
                gateway.Step();
                var now = FieldMap(sim);
                // Reinforcements get new IDs, so walk every soldier the state holds, not only the initial ones.
                for (uint soldierId = 1; soldierId <= (uint)Raw(now, "Soldiers.Count"); soldierId++)
                {
                    string n = "Soldiers[" + soldierId + "].";
                    if (now[n + "Alive"] != "1" || Raw(now, n + "TargetKind") != 1) continue;
                    uint targetId = (uint)Raw(now, n + "TargetId");
                    // The decision at this tick used the observation published at the end of the previous tick.
                    string t = "Soldiers[" + targetId + "].";
                    uint attackerFaction = (uint)Raw(now, n + "FactionId");
                    Assert.That((uint)Raw(now, t + "FactionId"), Is.Not.EqualTo(attackerFaction), "A soldier targeted its own side.");
                    Assert.That(previous.ContainsKey(t + "Position.X.Raw"), Is.True, "The target did not exist when the target was chosen.");
                    int cx = (int)(Raw(previous, t + "Position.X.Raw") / cellRaw), cz = (int)(Raw(previous, t + "Position.Z.Raw") / cellRaw);
                    var visible = previousVisible[(int)attackerFaction - 1];
                    Assert.That(visible[cz * map.WidthCells + cx], Is.True,
                        "tick " + tick + ": soldier " + soldierId + " targeted enemy " + targetId + " that its faction could not see.");
                    checkedTargets++;
                }
                previous = now;
                previousVisible = new[] { sim.Capture(1).Fog.VisibleCells, sim.Capture(2).Fog.VisibleCells };
                west.Step(sim.Capture(1)); east.Step(sim.Capture(2));
            }
            Assert.That(checkedTargets, Is.GreaterThan(0), "The match never produced an enemy target, so nothing was checked.");
        }

        [TestCase(0)]
        [TestCase(60)]
        [TestCase(200)]
        [TestCase(400)]
        public void EveryReplyDelayKeepsTheMatchRunningAndTheNewerOrderWins(int delay)
        {
            const long ticks = 1200;
            var result = InterventionRunner.Run(WeekTwoScenario.Create(), ticks, "maintain", InterventionStyle.Change, delay);
            Assert.That(result.LastTick, Is.EqualTo(ticks), "The match must keep running through the whole horizon.");
            Assert.That(result.FirstContactTick, Is.GreaterThan(0), "No contact was reported, so the change order was never sent.");

            var reserves = result.Commands.Where(c => c.Kind == "MaintainReserve").OrderBy(c => c.CommandId).ToList();
            Assert.That(reserves.Count, Is.EqualTo(2), "One start order and one change order.");
            var defend = result.Commands.Single(c => c.Kind == "Defend");

            if (delay <= 200)
            {
                long expectedApply = result.FirstContactTick + System.Math.Max(40, delay + 1);
                Assert.That(defend.FinalStatus, Is.EqualTo("Executing"), "The newer order must be carried out.");
                Assert.That(defend.ApplyTick, Is.EqualTo(expectedApply));
                Assert.That(reserves[1].FinalStatus, Is.EqualTo("Executing"));
                Assert.That(reserves[0].FinalStatus, Is.EqualTo("Cancelled"), "The older reserve order must be replaced by the newer one.");
                Assert.That(reserves[0].Reason, Is.EqualTo("Superseded"));
            }
            else
            {
                // 400 ticks is past the 240-tick deadline: every player reply arrives too late, including the first one,
                // so none of them takes effect. The match still runs on with the automatic allocation alone.
                Assert.That(result.Commands.All(c => c.FinalStatus == "Expired" && c.Reason == "Deadline"), Is.True,
                    string.Join(", ", result.Commands.Select(c => c.Kind + ":" + c.FinalStatus + "/" + c.Reason)));
            }
        }

        [Test]
        public void TheLongProfileLetsTheTwentySecondReplyTakeEffect()
        {
            // Chapter 11: a dedicated setting with a 500-tick deadline compares the case where a 20 s reply is still valid.
            var result = InterventionRunner.Run(WeekTwoScenario.Create(), 1300, "maintain", InterventionStyle.Change, 400, AiTimingProfile.Long);
            Assert.That(result.LastTick, Is.EqualTo(1300));
            var defend = result.Commands.Single(c => c.Kind == "Defend");
            Assert.That(defend.FinalStatus, Is.EqualTo("Executing"));
            Assert.That(defend.ApplyTick, Is.EqualTo(result.FirstContactTick + 401));
            Assert.That(result.Commands.Any(c => c.FinalStatus == "Expired"), Is.False);
        }

        [Test]
        public void PushSendsEveryArmyAtTheEnemyCoreAndSecureSendsEachArmyToItsOutpost()
        {
            var push = InterventionRunner.Run(WeekTwoScenario.Create(), 900, "maintain", InterventionStyle.Push, 0);
            var focus = push.Commands.Single(c => c.Kind == "Focus");
            Assert.That(focus.Target, Is.EqualTo("All:0"));
            Assert.That(focus.FinalStatus, Is.EqualTo("Executing"), focus.Reason);
            Assert.That(focus.ApplyTick, Is.EqualTo(push.FirstContactTick + 40));

            var secure = InterventionRunner.Run(WeekTwoScenario.Create(), 900, "maintain", InterventionStyle.Secure, 0);
            var armies = secure.Commands.Where(c => c.Kind == "Focus").Select(c => c.Target).OrderBy(t => t).ToArray();
            Assert.That(armies, Is.EqualTo(new[] { "Army:1", "Army:2" }));
            Assert.That(secure.Commands.Where(c => c.Kind == "Focus").All(c => c.FinalStatus == "Executing"), Is.True,
                string.Join(", ", secure.Commands.Select(c => c.Kind + ":" + c.FinalStatus + "/" + c.Reason)));
        }

        [Test]
        public void TheSameInterventionRunTwiceProducesTheSameInputLog()
        {
            var a = InterventionRunner.Run(WeekTwoScenario.Create(), 900, "concentrate", InterventionStyle.Change, 60);
            var b = InterventionRunner.Run(WeekTwoScenario.Create(), 900, "concentrate", InterventionStyle.Change, 60);
            Assert.That(a.Inputs.Length, Is.EqualTo(b.Inputs.Length));
            for (int i = 0; i < a.Inputs.Length; i++)
                Assert.That(InputBinary.Encode(a.Inputs[i]), Is.EqualTo(InputBinary.Encode(b.Inputs[i])), "input " + i);
            Assert.That(a.FirstContactTick, Is.EqualTo(b.FirstContactTick));
        }
    }
}
