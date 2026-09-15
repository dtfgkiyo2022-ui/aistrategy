using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Application;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class PolicyDecisionTests
    {
        private static SimPoint P(int x, int z = 32) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static PolicyGoal G(uint id) => new PolicyGoal(GoalKind.Outpost, id, default);
        private static readonly ScheduledInput[] None = Array.Empty<ScheduledInput>();
        private static FactionObservation Observation(long tick = 0, VisibleEnemy[] enemies = null, bool owned = true)
        {
            enemies = enemies ?? Array.Empty<VisibleEnemy>();
            var armies = new[] {
                new OwnArmyView(1,1,UnitKind.Infantry,P(40),8,G(1)),
                new OwnArmyView(2,1,UnitKind.Infantry,P(48),8,G(2)),
                new OwnArmyView(3,1,UnitKind.Infantry,P(24),2,new PolicyGoal(GoalKind.Core,1,default)),
                new OwnArmyView(4,1,UnitKind.Scout,P(28),2,G(1)) };
            var objectives = new[] {
                new KnownObjective(GoalKind.Core,1,P(24),true,1,true,3000,tick),
                new KnownObjective(GoalKind.Core,2,P(232),true,2,true,3000,tick),
                new KnownObjective(GoalKind.Outpost,1,P(128),true,owned ? 1U : 0U,false,0,tick),
                new KnownObjective(GoalKind.Outpost,2,P(128,96),true,owned ? 1U : 0U,false,0,tick) };
            return new FactionObservation(1,tick,armies,enemies,enemies.Select(e => new EnemyContact(e.ContactId,e.Position,tick,1,1,true)).ToArray(),objectives);
        }
        private static ArmyDecisionInput[] Inputs(FactionObservation o, PolicyKind policy = 0)
        {
            return o.OwnArmies.Select(a => new ArmyDecisionInput(a,
                policy == 0 ? default : new PolicyView(1,CommandSource.Human,policy,G(2),new LossBudget(1000),0),
                a.Id == 3, default, o.Objectives.Select(v => new ObjectiveRoute(new PolicyGoal(v.Kind,v.Id,default),
                    (int)(PolicyDecision.Distance(a.Position,v.Position) / 65536 / 65536))).ToArray())).ToArray();
        }
        private static ArmyDecisionMemory[] Allocate(FactionObservation o, ushort reserve, uint[] abandon = null, PolicyKind policy = 0)
            => PolicyDecision.Allocate(o,20,Inputs(o,policy),reserve,abandon ?? Array.Empty<uint>(),Array.Empty<AttackMemory>(),Array.Empty<PolicyOrder>(),out _);
        [Test]
        public void CoreDefensePrecedesReserveAndProtectedOutposts()
        {
            var enemies = Enumerable.Range(1,12).Select(i => new VisibleEnemy((uint)i,P(25),1)).ToArray();
            var o = Observation(enemies:enemies);
            var result = Allocate(o,200);
            Assert.That(result.Take(3).All(a => a.Assignment == AssignmentKind.CoreDefense),Is.True);
            Assert.That(result.Take(3).All(a => a.Goal.Kind == GoalKind.Core && a.Goal.Id == 1),Is.True);
        }
        [Test]
        public void DefaultReserveDoesNotRoundUpLargeArmyBeforeFocus()
        {
            var o = Observation(); var result = Allocate(o,200,policy:PolicyKind.Focus);
            Assert.That(result[2].Assignment,Is.EqualTo(AssignmentKind.Reserve));
            Assert.That(result[0].Assignment,Is.EqualTo(AssignmentKind.Advance));
            // Focus armies are considered only after the actual reserve constraint is met.
            Assert.That(result[1].Assignment,Is.Not.EqualTo(AssignmentKind.Guard));
            Assert.That(result[3].Goal.Id,Is.EqualTo(2));
        }
        [Test]
        public void FocusDoesNotCreateGuardsWithoutAnOccupationThreat()
        {
            var o = Observation(); var before = Allocate(o,0,policy:PolicyKind.Focus);
            var after = Allocate(o,0,new uint[]{1},PolicyKind.Focus);
            Assert.That(before[0].Assignment,Is.EqualTo(AssignmentKind.Advance));
            Assert.That(after[0].Assignment,Is.EqualTo(AssignmentKind.Advance));
            Assert.That(after[0].Goal.Id,Is.EqualTo(2));
            Assert.That(after[1].Assignment,Is.EqualTo(AssignmentKind.Advance));
            Assert.That(o.Objectives[2].OwnerFactionId,Is.EqualTo(1));
        }
        [Test]
        public void HumanDefendCannotBeOverwrittenAndReserveShortfallIsReported()
        {
            var o=Observation(); var input=Inputs(o,PolicyKind.Defend);
            var result=PolicyDecision.Allocate(o,20,input,1000,Array.Empty<uint>(),Array.Empty<AttackMemory>(),Array.Empty<PolicyOrder>(),out int shortage);
            Assert.That(shortage,Is.EqualTo(20));
            Assert.That(result.All(a=>a.Assignment==AssignmentKind.Advance),Is.True);
        }
        [Test]
        public void OnlyAutoRetreatsAfterFortyConsecutiveInferiorTicksAndHoldsSixty()
        {
            var enemies=Enumerable.Range(1,20).Select(i=>new VisibleEnemy((uint)i,P(40),1)).ToArray();
            var o=Observation(enemies:enemies); var auto=default(ArmyDecisionMemory); var human=auto;
            var policy=new PolicyView(1,CommandSource.Human,PolicyKind.Defend,G(1),new LossBudget(1000),0);
            for(int tick=1;tick<=40;tick++)
            {
                auto=PolicyDecision.AssessRetreat(o,o.OwnArmies[0],default,auto,tick,false,false);
                human=PolicyDecision.AssessRetreat(o,o.OwnArmies[0],policy,human,tick,false,false);
                Assert.That(auto.Returning,Is.EqualTo(tick==40));
                Assert.That(human.Returning,Is.False);
            }
            auto=PolicyDecision.AssessRetreat(o,o.OwnArmies[0],default,auto,50,false,true);
            Assert.That(auto.HoldUntilTick,Is.EqualTo(110));
            auto=PolicyDecision.AssessRetreat(o,o.OwnArmies[0],default,auto,109,false,false);
            Assert.That(auto.Returning,Is.False);
        }
        [Test]
        public void ShortAdaptationCountsTwoAttacksButNotFogFlicker()
        {
            var memory=default(AttackMemory);
            foreach(long tick in new long[]{1,2,60,61,122})
            {
                bool visible=tick==1 || tick==60 || tick==122;
                var o=Observation(tick,visible?new[]{new VisibleEnemy(1,P(128),1)}:null);
                memory=PolicyDecision.ObserveAttack(o,o.Objectives[2],memory);
                if(tick<122) Assert.That(memory.AlertUntilTick,Is.Zero);
            }
            Assert.That(memory.LastAttackTick,Is.EqualTo(122));
            Assert.That(memory.AlertUntilTick,Is.EqualTo(722));
        }
        private static TacticalInput Tactical(long tick, SimPoint position, PolicyKind policy=0, AssignmentKind assignment=0, bool returning=false)
            => new TacticalInput(1,tick,position,P(100),P(24),Fix64.FromInt(2),Fix64.FromInt(4),policy,assignment,returning);
        [TestCase(61,111)] [TestCase(2,112)]
        public void PursuitStopsAtSixtyTicksOrTwelveMetersAndMustReturn(long tick,int position)
        {
            var memory=default(PursuitMemory);
            var o=Observation(enemies:new[]{new VisibleEnemy(1,P(110),1)});
            PolicyDecision.Tactics(o,Tactical(1,P(100)),ref memory);
            Assert.That(memory.Active,Is.True); Assert.That(memory.StartTick,Is.EqualTo(1));
            var stop=PolicyDecision.Tactics(o,Tactical(tick,P(position)),ref memory);
            Assert.That(memory.Returning,Is.True); Assert.That(stop.MoveGoal,Is.EqualTo(P(100)));
            var returning=PolicyDecision.Tactics(o,Tactical(tick+1,P(105)),ref memory);
            Assert.That(returning.TargetContactId,Is.Zero); Assert.That(returning.MoveGoal,Is.EqualTo(P(100)));
            PolicyDecision.Tactics(o,Tactical(tick+2,P(100)),ref memory);
            Assert.That(memory.Active,Is.True); Assert.That(memory.StartTick,Is.EqualTo(tick+2));
        }
        [Test]
        public void TargetChangesCannotRestartPursuitAndLostContactCannotBeAttacked()
        {
            var m=default(PursuitMemory);
            PolicyDecision.Tactics(Observation(enemies:new[]{new VisibleEnemy(1,P(110),1)}),Tactical(1,P(100)),ref m);
            PolicyDecision.Tactics(Observation(enemies:new[]{new VisibleEnemy(2,P(112),1)}),Tactical(20,P(105)),ref m);
            Assert.That(m.StartTick,Is.EqualTo(1)); Assert.That(m.Start,Is.EqualTo(P(100)));
            var lost=PolicyDecision.Tactics(Observation(),Tactical(21,P(105)),ref m);
            Assert.That(lost.TargetContactId,Is.Zero); Assert.That(m.Returning,Is.True);
        }
        [Test]
        public void DefendHoldsEightMetersAndInterceptsOnlyWithinTwelve()
        {
            var m=default(PursuitMemory);
            var hold=PolicyDecision.Tactics(Observation(),Tactical(1,P(107),PolicyKind.Defend),ref m);
            Assert.That(hold.MoveGoal,Is.EqualTo(P(107)));
            var outside=PolicyDecision.Tactics(Observation(enemies:new[]{new VisibleEnemy(1,P(113),1)}),Tactical(2,P(107),PolicyKind.Defend),ref m);
            Assert.That(outside.MoveGoal,Is.EqualTo(P(107))); Assert.That(m.Active,Is.False);
            var inside=PolicyDecision.Tactics(Observation(enemies:new[]{new VisibleEnemy(1,P(112),1)}),Tactical(3,P(107),PolicyKind.Defend),ref m);
            Assert.That(inside.MoveGoal,Is.EqualTo(P(112))); Assert.That(m.Active,Is.True);
        }
        [Test]
        public void NewHumanDefendOverridesPreviousReserveAssignment()
        {
            var memory=default(PursuitMemory);
            var intent=PolicyDecision.Tactics(Observation(),Tactical(1,P(24),PolicyKind.Defend,AssignmentKind.Reserve),ref memory);
            Assert.That(intent.MoveGoal,Is.EqualTo(P(100)));
            Assert.That(intent.IsRetreating,Is.False);
        }
        [Test]
        public void ReserveInterceptsOnlyInCoreDefenseCircleAndRetreatNeverAttacks()
        {
            var m=default(PursuitMemory);
            var o=Observation(enemies:new[]{new VisibleEnemy(1,P(49),1)});
            var intent=PolicyDecision.Tactics(o,Tactical(1,P(47),assignment:AssignmentKind.Reserve),ref m);
            Assert.That(intent.TargetContactId,Is.Zero); Assert.That(m.Active,Is.False);
            o=Observation(enemies:new[]{new VisibleEnemy(1,P(48),1)});
            intent=PolicyDecision.Tactics(o,Tactical(2,P(47),assignment:AssignmentKind.Reserve),ref m);
            Assert.That(intent.TargetContactId,Is.EqualTo(1));
            intent=PolicyDecision.Tactics(o,Tactical(3,P(47),PolicyKind.Retreat),ref m);
            Assert.That(intent.TargetContactId,Is.Zero); Assert.That(intent.IsRetreating,Is.True);
        }
        [Test]
        public void ScoutAvoidsVisibleEnemyAndReturnsHomeAfterLosingIt()
        {
            var o=Observation(enemies:new[]{new VisibleEnemy(1,P(106),1)});
            Assert.That(PolicyDecision.ScoutReturn(o,P(100),P(24)).X,Is.LessThan(P(100).X));
            var hidden=new FactionObservation(1,1,o.OwnArmies,Array.Empty<VisibleEnemy>(),o.Contacts,o.Objectives);
            Assert.That(PolicyDecision.ScoutReturn(hidden,P(100),P(24)),Is.EqualTo(P(24)));
        }

        internal static ScheduledInput Order(long tick, uint faction, ScopeKind scope, uint id, PolicyKind kind,
            PolicyGoal goal=default, ushort loss=1000, EndKind end=EndKind.UntilReplaced, ushort reserve=0)
        {
            var o=new PolicyOrder(1,0,CommandSource.Human,new ScopeKey(faction,scope,id),kind,goal,50,new LossBudget(loss),
                new EndCondition(end,0),reserve,0,Array.Empty<PolicyVersion>(),tick-1,new Expiration(long.MaxValue,0,ExpireFlags.None));
            return new ScheduledInput(1,InputKind.Resolve,tick-1,tick,1,1,new[]{o});
        }
        private static string Field(Battle sim,string name) => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Single(p=>p.Key==name).Value;
        private static void Run(Battle sim,int from,int to) { for(int t=from;t<=to;t++) sim.Step(t,None); }
        [TestCase(PolicyKind.Focus)] [TestCase(PolicyKind.Defend)] [TestCase(PolicyKind.Scout)]
        public void LossThresholdReturnsThenCompletes(PolicyKind kind)
        {
            var s=WeekOneScenario.Create(); s.UnitParameters[0].Damage=s.UnitParameters[1].Damage=0;
            uint id=kind==PolicyKind.Scout ? 4U : 1U;
            var sim=new Battle(s);
            var goal=kind==PolicyKind.Focus ? new PolicyGoal(GoalKind.Core,2,default) : G(1);
            CommandTestInput.Step(sim,1,new[]{Order(1,1,ScopeKind.Army,id,kind,goal,0)});
            Assert.That(sim.Capture(1).Commands.Single().Status,Is.EqualTo(CommandStatus.Executing));
            Assert.That(Field(sim,"CommandStates[101].Armies["+id+"].Returning"),Is.EqualTo("1"));
            Run(sim,2,900);
            Assert.That(sim.Capture(1).Commands.Single().Status,Is.EqualTo(CommandStatus.Completed));
        }
        [Test]
        public void LossReachedEndCompletesImmediatelyButPreservesReturnSuccessor()
        {
            var s=WeekOneScenario.Create(); s.Soldiers[0].Position=P(80,64); var sim=new Battle(s);
            CommandTestInput.Step(sim,1,new[]{Order(1,1,ScopeKind.Army,1,PolicyKind.Defend,G(1),0,EndKind.LossReached)});
            Assert.That(sim.Capture(1).Commands.Single().Status,Is.EqualTo(CommandStatus.Completed));
            sim.Step(2,None);
            Assert.That(sim.Capture(1).Units.Where(u=>u.IsOwn && u.Id<=4).All(u=>u.IsRetreating),Is.True);
        }
        [Test]
        public void RetreatCompletesAtHomeAndReturnToAutoCancelsHumanConstraints()
        {
            var scenario=WeekOneScenario.Create();
            for(int i=0;i<4;i++) scenario.Soldiers[i].Position=P(24,64);
            var sim=new Battle(scenario);
            CommandTestInput.Step(sim,1,new[]{Order(1,1,ScopeKind.Army,1,PolicyKind.Retreat,end:EndKind.Arrived)});
            Assert.That(sim.Capture(1).Commands.Single().Status,Is.EqualTo(CommandStatus.Completed));
            Assert.That(Field(sim,"Ai.Armies[1].HoldUntilTick"),Is.EqualTo("61"));
            CommandTestInput.Step(sim,2,new[]{Order(2,1,ScopeKind.Army,1,PolicyKind.Defend,G(1))});
            CommandTestInput.Step(sim,3,new[]{Order(3,1,ScopeKind.Army,1,PolicyKind.ReturnToAuto)});
            Assert.That(sim.Capture(1).Commands.Last().Status,Is.EqualTo(CommandStatus.Completed));
            Assert.That(sim.Capture(1).Commands.ElementAt(1).Status,Is.EqualTo(CommandStatus.Cancelled));
        }
        [Test]
        public void PresetsAreOrdinaryLoggedPoliciesWithDifferentAllocations()
        {
            var s=WeekTwoScenario.Create(); var maintain=new Battle(s); var concentrate=new Battle(s);
            var a=PolicyPresets.InitialInputs(s,"maintain"); var b=PolicyPresets.InitialInputs(s,"concentrate");
            for(int t=1;t<=2200;t++)
            { maintain.Step(t,a.Where(i=>i.AcceptedTick==t-1).ToArray()); concentrate.Step(t,b.Where(i=>i.AcceptedTick==t-1).ToArray()); }
            Assert.That(maintain.Capture(2).Commands.All(c=>c.Status==CommandStatus.Executing || c.Status==CommandStatus.Completed),Is.True);
            Assert.That(concentrate.Capture(2).Commands.All(c=>c.Status==CommandStatus.Executing || c.Status==CommandStatus.Completed),Is.True);
            Assert.That(maintain.Capture(2).Observation.OwnArmies.Select(v=>v.Position),Is.Not.EqualTo(concentrate.Capture(2).Observation.OwnArmies.Select(v=>v.Position)));
            Assert.That(maintain.Capture(2).Objectives.Where(o=>o.Kind==GoalKind.Outpost).Select(o=>o.OwnerFactionId),Is.Not.EqualTo(concentrate.Capture(2).Objectives.Where(o=>o.Kind==GoalKind.Outpost).Select(o=>o.OwnerFactionId)));
        }
        [Test]
        public void AbandonMidMatchChangesAssignmentsWithoutDestroyingAssets()
        {
            var s=WeekTwoScenario.Create(); s.Outposts[0].OwnerFactionId=1; s.Outposts[1].OwnerFactionId=1;
            var auto=new Battle(s); var abandon=new Battle(s);
            var reserve=Order(1,1,ScopeKind.All,0,PolicyKind.MaintainReserve,reserve:0);
            CommandTestInput.Step(auto,1,new[]{reserve}); CommandTestInput.Step(abandon,1,new[]{reserve});
            Run(auto,2,40); Run(abandon,2,40);
            CommandTestInput.Step(abandon,41,new[]{Order(41,1,ScopeKind.Outpost,1,PolicyKind.AllowAbandon)}); auto.Step(41,None);
            Assert.That(abandon.Capture(1).Objectives.Single(o=>o.Kind==GoalKind.Outpost && o.Id==1).OwnerFactionId,Is.EqualTo(1));
            Assert.That(abandon.Capture(1).Observation.OwnArmies.Sum(a=>a.AliveCount),Is.EqualTo(auto.Capture(1).Observation.OwnArmies.Sum(a=>a.AliveCount)));
            Run(auto,42,400); Run(abandon,42,400);
            Assert.That(Field(auto,"Ai.Armies[1].Assignment"),Is.EqualTo("2"));
            Assert.That(Field(abandon,"Ai.Armies[1].Assignment"),Is.Not.EqualTo("2"));
            Assert.That(auto.Capture(1).Observation.OwnArmies[0].Position,Is.Not.EqualTo(abandon.Capture(1).Observation.OwnArmies[0].Position));
        }
        [Test]
        public void SameObservationFromWorldsWithDifferentUnobservedEnemiesGivesSameDecisions()
        {
            var a=WeekTwoScenario.Create(); var b=WeekTwoScenario.Create(); b.Soldiers[20].Position=P(230,96); b.Soldiers[20].Hp=50;
            var left=new Battle(a); var right=new Battle(b);
            FactionObservation Mask(Battle sim)
            {
                var o=sim.Capture(1).Observation;
                return new FactionObservation(o.FactionId,o.Tick,o.OwnArmies,Array.Empty<VisibleEnemy>(),Array.Empty<EnemyContact>(),o.Objectives);
            }
            var x=Mask(left); var y=Mask(right);
            Assert.That(left.CaptureDiagnostic().CanonicalState,Is.Not.EqualTo(right.CaptureDiagnostic().CanonicalState));
            Assert.That(Allocate(x,200),Is.EqualTo(Allocate(y,200)));
            var mx=default(PursuitMemory); var my=mx;
            Assert.That(PolicyDecision.Tactics(x,Tactical(1,P(100)),ref mx),Is.EqualTo(PolicyDecision.Tactics(y,Tactical(1,P(100)),ref my)));
            Assert.That(mx,Is.EqualTo(my));
        }

        [Test]
        public void AbandoningNorthChangesOwnershipWhileAutoKeepsItsGarrison()
        {
            var s=WeekTwoScenario.Create(); s.UnitParameters[0].Damage=s.UnitParameters[1].Damage=0;
            s.Outposts[0].OwnerFactionId=s.Outposts[1].OwnerFactionId=1;
            for(int i=0;i<8;i++) { s.Soldiers[i].Position=P(128,96); s.Soldiers[20+i].Position=P(130,96); }
            var auto=new Battle(s); var abandon=new Battle(s);
            var initial=new[] {
                Order(1,1,ScopeKind.All,0,PolicyKind.MaintainReserve),
                Order(1,2,ScopeKind.All,0,PolicyKind.MaintainReserve),
                Order(1,2,ScopeKind.Army,5,PolicyKind.Defend,G(1)) };
            CommandTestInput.Step(auto,1,initial); CommandTestInput.Step(abandon,1,initial);
            Run(auto,2,40); Run(abandon,2,40);
            auto.Step(41,None); CommandTestInput.Step(abandon,41,new[]{Order(41,1,ScopeKind.Outpost,1,PolicyKind.AllowAbandon)});
            Run(auto,42,2500); Run(abandon,42,2500);
            Assert.That(auto.Capture(1).Objectives.Single(o=>o.Kind==GoalKind.Outpost && o.Id==1).OwnerFactionId,Is.EqualTo(1));
            Assert.That(abandon.Capture(1).Objectives.Single(o=>o.Kind==GoalKind.Outpost && o.Id==1).OwnerFactionId,Is.EqualTo(2));
        }

        [Test]
        public void NonzeroLossBudgetCountsDeathsAndHumanDefendReturnsWithoutAttacking()
        {
            var s=WeekOneScenario.Create(); s.UnitParameters[0].Damage=10;
            s.Soldiers=new[] {
                new SoldierDefinition {Id=1,FactionId=1,ArmyId=1,Kind=UnitKind.Infantry,Alive=true,Hp=10,Position=P(100,64)},
                new SoldierDefinition {Id=2,FactionId=1,ArmyId=1,Kind=UnitKind.Infantry,Alive=true,Hp=100,Position=P(100,64)},
                new SoldierDefinition {Id=3,FactionId=2,ArmyId=5,Kind=UnitKind.Infantry,Alive=true,Hp=100,Position=P(102,64)} };
            // Hold this enemy in place so the surviving defender can reach its core.
            s.UnitParameters[0].Range=Fix64.FromInt(2);
            var sim=new Battle(s);
            CommandTestInput.Step(sim,1,new[]{Order(1,1,ScopeKind.Army,1,PolicyKind.Defend,G(1),500)});
            Assert.That(Field(sim,"CommandStates[101].Armies[1].Deaths"),Is.EqualTo("1"));
            Assert.That(Field(sim,"CommandStates[101].Armies[1].Returning"),Is.EqualTo("1"));
            sim.Step(2,None);
            Assert.That(sim.Capture(1).Units.Single(u=>u.IsOwn).IsRetreating,Is.True);
            Assert.That(sim.Capture(1).Units.Single(u=>u.IsOwn).IsAttacking,Is.False);
            Run(sim,3,1500);
            Assert.That(sim.Capture(1).Commands.Single().Status,Is.EqualTo(CommandStatus.Completed));
        }

        [Test]
        public void UnknownThreatIsTenAndVisibleEstimateUsesUpperBoundOnly()
        {
            var o=Observation(700);
            var unknown=new FactionObservation(1,700,o.OwnArmies,o.VisibleEnemies,
                new[]{new EnemyContact(1,P(128),0,0,-1,false)},o.Objectives);
            Assert.That(PolicyDecision.Estimate(unknown,P(128)),Is.EqualTo(10));
            var estimated=new FactionObservation(1,700,o.OwnArmies,o.VisibleEnemies,
                new[]{new EnemyContact(1,P(128),700,6,10,true)},o.Objectives);
            Assert.That(PolicyDecision.Estimate(estimated,P(128)),Is.EqualTo(10));
            Assert.That(PolicyDecision.Estimate(estimated,P(24)),Is.Zero);
        }
        [Test]
        public void NeverObservedOutpostIsLessAttractiveThanObservedEmptyOutpost()
        {
            var known=Observation(owned:false); var objectives=known.Objectives.ToArray();
            objectives[2]=new KnownObjective(GoalKind.Outpost,1,P(40),false,0,false,0,0);
            var o=new FactionObservation(1,0,known.OwnArmies,known.VisibleEnemies,known.Contacts,objectives);
            Assert.That(Allocate(o,0)[0].Goal.Id,Is.EqualTo(2));
        }

        [Test]
        public void AlertAloneDoesNotCreateAGuard()
        {
            var o=Observation(); var input=Inputs(o);
            var result=PolicyDecision.Allocate(o,20,input,200,Array.Empty<uint>(),
                new[]{new AttackMemory {OutpostId=2,AlertUntilTick=600}},Array.Empty<PolicyOrder>(),out _);
            Assert.That(result[1].Assignment,Is.EqualTo(AssignmentKind.Advance));
        }

        [Test]
        public void AiMemoryFieldsAllParticipateInCanonicalState()
        {
            var sim=new Battle(WeekTwoScenario.Create()); sim.Step(1,None);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            object world=typeof(Battle).GetField("world",flags).GetValue(sim);
            foreach(string group in new[]{"Armies","Soldiers"})
            {
                var array=(Array)world.GetType().GetField(group,flags).GetValue(world);
                object state=array.GetValue(0);
                var field=state.GetType().GetField(group=="Armies"?"Decision":"Pursuit",flags);
                object original=field.GetValue(state);
                foreach(var member in original.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
                {
                    object memory=field.GetValue(state);
                    object value=member.FieldType==typeof(bool)?(object)true:member.FieldType==typeof(long)?1L:
                        member.FieldType==typeof(PolicyGoal)?(object)G(1):member.FieldType==typeof(SimPoint)?P(50):(object)AssignmentKind.Guard;
                    var before=sim.CaptureDiagnostic().CanonicalState;
                    member.SetValue(memory,value); field.SetValue(state,memory); array.SetValue(state,0);
                    Assert.That(sim.CaptureDiagnostic().CanonicalState,Is.Not.EqualTo(before),member.Name);
                    field.SetValue(state,original); array.SetValue(state,0);
                }
            }
            var memories=(AttackMemory[][])typeof(Battle).GetField("attackMemory",flags).GetValue(sim);
            foreach(var field in typeof(AttackMemory).GetFields())
            {
                var original=memories[0][0]; object changed=original;
                object value=field.FieldType==typeof(bool)?(object)!(bool)field.GetValue(original):
                    field.FieldType==typeof(uint)?(object)((uint)field.GetValue(original)+1U):(long)field.GetValue(original)+1;
                var before=sim.CaptureDiagnostic().CanonicalState;
                field.SetValue(changed,value); memories[0][0]=(AttackMemory)changed;
                Assert.That(sim.CaptureDiagnostic().CanonicalState,Is.Not.EqualTo(before),field.Name);
                memories[0][0]=original;
            }
        }

        [Test]
        public void DecisionPublicInputsAndOutputsNeverExposeSimulationTypes()
        {
            foreach(var method in typeof(PolicyDecision).GetMethods(BindingFlags.Static|BindingFlags.Public))
                foreach(var type in method.GetParameters().Select(p=>p.ParameterType).Append(method.ReturnType))
                    Assert.That(type.ToString(),Does.Not.Contain("Rts.Simulation"));
            foreach(var reference in typeof(PolicyDecision).Assembly.GetReferencedAssemblies())
                if(reference.Name.StartsWith("Rts.",StringComparison.Ordinal)) Assert.That(reference.Name,Is.EqualTo("Rts.Contracts"));
        }
    }
}
