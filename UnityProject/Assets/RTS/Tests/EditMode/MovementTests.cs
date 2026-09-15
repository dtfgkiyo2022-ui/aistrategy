using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Rts.Application;
using Rts.Replay;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class MovementTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static bool Within(SimPoint a, SimPoint b, Fix64 r) => FixMath.CompareDistanceSquared(a.X.Raw-b.X.Raw,a.Z.Raw-b.Z.Raw,r.Raw)<=0;
        private static object World(Battle sim) => typeof(Battle).GetField("world", Hidden).GetValue(sim);
        private static Array States(Battle sim, string name) => (Array)World(sim).GetType().GetField(name, Hidden).GetValue(World(sim));
        private static T Field<T>(object state, string name) => (T)state.GetType().GetField(name, Hidden).GetValue(state);
        private static void Set(object state, string name, object value) => state.GetType().GetField(name, Hidden).SetValue(state, value);
        private static void Safe(Battle sim, GridMap map, int tick)
        {
            Assert.That(sim.Capture(1).Result.IsFault, Is.False, "tick=" + tick);
            foreach (var soldier in States(sim,"Soldiers"))
            {
                var initial=Field<SoldierDefinition>(soldier,"Initial");
                if(initial.Id==0) continue; // unused array capacity
                Assert.That(map.IsPassable(map.Cell(Field<SimPoint>(soldier,"Position"))), Is.True, "tick=" + tick + " soldier=" + initial.Id);
            }
        }
        private static void Run(Battle sim, GridMap map, int from, int to)
        {
            for (int tick = from; tick <= to; tick++) { sim.Step(tick, Array.Empty<ScheduledInput>()); Safe(sim, map, tick); }
        }
        private static ScenarioDefinition Peace()
        {
            var s = WeekTwoScenario.Create();
            s.Soldiers = s.Soldiers.Where(p => p.ArmyId == 1).ToArray();
            s.Rules.CaptureDurationTicks = int.MaxValue;
            s.UnitParameters[0].Damage = s.UnitParameters[1].Damage = 0;
            s.Rules.CoreReinforcementIntervalTicks = s.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            return s;
        }
        private static void Order(Battle sim, int tick, SimPoint goal)
            {
            var focus=PolicyDecisionTests.Order(tick,1,ScopeKind.Army,1,PolicyKind.Focus,new PolicyGoal(GoalKind.Point,0,goal));
            CommandTestInput.Step(sim,tick,tick==1?new[]{PolicyDecisionTests.Order(tick,1,ScopeKind.All,0,PolicyKind.MaintainReserve),focus}:new[]{focus});
        }
        private static void Near(Battle sim, SimPoint goal)
        {
            foreach (var unit in sim.Capture(1).Units.Where(u => u.IsOwn))
                Assert.That(Within(unit.Position, goal, Fix64.FromInt(4)), Is.True, "soldier=" + unit.Id + " x=" + unit.Position.X + " z=" + unit.Position.Z);
        }
        [Test]
        public void FormationTransfersBetweenRoadsAndReturnsHome()
        {
            var s = Peace(); var sim = new Battle(s); var map = new GridMap(s.Map);
            Order(sim, 1, P(128,32)); Run(sim,map,2,2300); Near(sim,P(128,32));
            Order(sim,2301,P(128,96)); Run(sim,map,2302,5400); Near(sim,P(128,96));
            Order(sim,5401,P(24,64)); Run(sim,map,5402,7600); Near(sim,P(24,64));
        }
        [Test]
        public void CoreReinforcementsJoinMovingArmyWithoutStoppingVeterans()
        {
            var s = Peace(); s.Rules.CoreReinforcementIntervalTicks = 100;
            s.Armies[0].HomeObjective = new PolicyGoal(GoalKind.Core,1,default);
            var sim = new Battle(s); var map = new GridMap(s.Map);
            Order(sim,1,P(232,32));
            bool joining = false; int cursorAtJoin = -1; bool advanced = false;
            for(int t=2;t<=4500;t++)
            {
                sim.Step(t,Array.Empty<ScheduledInput>()); Safe(sim,map,t);
                bool now = States(sim,"Soldiers").Cast<object>().Any(p => Field<bool>(p,"Joining"));
                int cursor = Field<int>(States(sim,"Armies").GetValue(0),"PathCursor");
                if(now && !joining) { joining=true; cursorAtJoin=cursor; }
                if(now && cursor>cursorAtJoin && joining) advanced=true;
            }
            Assert.That(joining && advanced,Is.True);
            var ids=Field<uint[]>(States(sim,"Armies").GetValue(0),"SoldierIds");
            Assert.That(ids.Length,Is.EqualTo(16));
            foreach(uint id in ids)
            {
                var soldier=States(sim,"Soldiers").GetValue((int)id-1);
                Assert.That(Within(Field<SimPoint>(soldier,"Position"),P(232,32),Fix64.FromInt(4)),Is.True,"soldier="+id);
                Assert.That(Field<bool>(soldier,"Joining"),Is.False);
            }
        }
        [TestCase("auto")]
        [TestCase("maintain")]
        [TestCase("concentrate")]
        public void NoReachableArmyStallsFor600TicksWithoutNearbyEnemies(string preset)
        {
            var s=WeekTwoScenario.Create(); var sim=new Battle(s); var map=new GridMap(s.Map);
            var inputs=preset=="auto"?Array.Empty<ScheduledInput>():PolicyPresets.InitialInputs(s,preset);
            var previous=new SimPoint[8]; var cursors=new int[8]; var stopped=new int[8];
            for(int t=1;t<=5000;t++)
            {
                sim.Step(t,inputs.Where(i=>i.AcceptedTick+1==t).ToArray()); Safe(sim,map,t);
                foreach(uint f in new uint[]{1,2})
                foreach(var a in sim.Capture(f).Observation.OwnArmies)
                {
                    if(a.AliveCount==0) continue;
                    int i=(int)a.Id-1; var state=States(sim,"Armies").GetValue(i);
                    int cursor=Field<int>(state,"PathCursor");
                    var decision=Field<ArmyDecisionMemory>(state,"Decision");
                    bool hold=decision.Assignment==AssignmentKind.Guard || decision.Assignment==AssignmentKind.Reserve || decision.Assignment==AssignmentKind.CoreDefense;
                    bool enemy=sim.Capture(3-f).Units.Where(u=>u.IsOwn).Any(u=>Within(a.Position,u.Position,Fix64.FromInt(24)));
                    bool unchanged=a.Position.X==previous[i].X && a.Position.Z==previous[i].Z && cursor==cursors[i];
                    stopped[i]=!hold && !enemy && !Field<bool>(state,"PathImpossible") && unchanged ? stopped[i]+1:0;
                    Assert.That(stopped[i],Is.LessThan(600),preset+" tick="+t+" army="+a.Id);
                    previous[i]=a.Position; cursors[i]=cursor;
                }
            }
        }
        [Test]
        public void LocalNavigationStateIsCanonical()
        {
            var sim=new Battle(Peace()); var states=States(sim,"Soldiers");
            foreach(string name in new[]{"Joining","TacticalRoute","LocalPath","LocalCursor","JoinCursor","LocalGoal"})
            {
                var soldier=states.GetValue(0); var field=soldier.GetType().GetField(name,Hidden); var original=field.GetValue(soldier);
                var before=sim.CaptureDiagnostic();
                object value=field.FieldType==typeof(bool)?(object)true:field.FieldType==typeof(int[])?new[]{1,2}:field.FieldType==typeof(SimPoint)?(object)P(30,30):1;
                Set(soldier,name,value); states.SetValue(soldier,0);
                Assert.That(DiagnosticComparison.First(before,sim.CaptureDiagnostic()),Is.Not.Null,name);
                Set(soldier,name,original); states.SetValue(soldier,0);
            }
        }

        // Inject the movement intent at the Decision/Navigation boundary, so combat cannot
        // kill the test soldiers before the route and subsequent mission return are checked.
        [TestCase(233,51,TestName="PursuitAroundCornerThenRejoinsMission")]
        [TestCase(229,52,TestName="ScoutAvoidanceAroundCornerThenRejoinsMission")]
        [TestCase(24,64,TestName="TacticalCoreReturnThenRejoinsMission")]
        public void TacticalIntentRoutesAndRejoins(int x,int z)
        {
            var s=Peace();
            for(int i=0;i<s.Soldiers.Length;i++) s.Soldiers[i].Position=P(221,39);
            var sim=new Battle(s); var map=new GridMap(s.Map);
            var armies=States(sim,"Armies"); var army=armies.GetValue(0);
            Set(army,"Policy",PolicyKind.Focus); Set(army,"Goal",new PolicyGoal(GoalKind.Point,0,P(128,96))); armies.SetValue(army,0);
            var move=typeof(Battle).GetMethod("Move",Hidden);
            var soldiers=States(sim,"Soldiers");
            foreach(var goal in new[]{P(x,z),P(128,96)})
            {
                for(int tick=0;tick<7000;tick++)
                {
                    for(int i=0;i<s.Soldiers.Length;i++)
                    { var soldier=soldiers.GetValue(i); Set(soldier,"MoveGoal",goal); soldiers.SetValue(soldier,i); }
                    move.Invoke(sim,null);
                    for(int i=0;i<s.Soldiers.Length;i++)
                        Assert.That(map.IsPassable(map.Cell(Field<SimPoint>(soldiers.GetValue(i),"Position"))),Is.True);
                }
                for(int i=0;i<s.Soldiers.Length;i++)
                    Assert.That(Within(Field<SimPoint>(soldiers.GetValue(i),"Position"),goal,Fix64.FromInt(1)),Is.True,"soldier="+(i+1));
            }
        }

        [Test]
        public void SharedRouteCacheCanBeRebuiltWithoutChangingCanonicalState()
        {
            var s=WeekTwoScenario.Create(); var a=new Battle(s); var b=new Battle(s);
            var map=(GridMap)World(b).GetType().GetField("Map",Hidden).GetValue(World(b));
            var cache=(System.Collections.IDictionary)typeof(GridMap).GetField("routes",Hidden).GetValue(map);
            var order=(Queue<int>)typeof(GridMap).GetField("routeOrder",Hidden).GetValue(map);
            for(int t=1;t<=1200;t++)
            {
                cache.Clear(); order.Clear();
                a.Step(t,Array.Empty<ScheduledInput>()); b.Step(t,Array.Empty<ScheduledInput>());
                Assert.That(a.CaptureDiagnostic().CanonicalState,Is.EqualTo(b.CaptureDiagnostic().CanonicalState),"tick="+t);
            }
        }
    }
}
