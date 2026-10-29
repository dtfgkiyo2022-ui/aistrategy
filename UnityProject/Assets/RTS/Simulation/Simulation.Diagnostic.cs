using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private byte[] configurationHash;
        /// <summary>Schema 1: ordered named fields, typed LE values. No derived frames or scratch buffers.</summary>
        public DiagnosticState CaptureDiagnostic()
        {
            if(configurationHash==null)
                using(var sha=SHA256.Create()) configurationHash=sha.ComputeHash(ScenarioBinary.Encode(world.Config));
            using(var s=new MemoryStream())
            using(var w=new StateWriter(s))
            {
                w.Write(1U);
                w.Text("Rules.Version",ScenarioBinary.RulesVersion); w.Blob("Config.Hash",configurationHash);
                w.Value("Tick",world.Tick); w.Value("Result.HasEnded",world.Result.HasEnded); w.Value("Result.WinnerFactionId",world.Result.WinnerFactionId);
                w.Value("Result.IsDraw",world.Result.IsDraw); w.Value("Result.IsFault",world.Result.IsFault); w.Value("Result.IsUndecided",world.Result.IsUndecided); w.Value("Result.IsAgeVictory",world.Result.IsAgeVictory);
                w.Value("NextSoldierId",world.NextSoldierId); w.Value("NextArmyId",world.NextArmyId); w.Value("NextCoreId",world.NextCoreId); w.Value("NextOutpostId",world.NextOutpostId); w.Value("NextFactionId",world.NextFactionId);
                w.Value("CombatRandom.State",world.CombatRandom.State); w.Value("CombatRandom.CallCount",world.CombatRandom.CallCount);
                w.Value("AiRandom.State",world.AiRandom.State); w.Value("AiRandom.CallCount",world.AiRandom.CallCount);
                w.Value("Soldiers.Count",(uint)world.SoldierCount);
                for (int i = 0; i < world.SoldierCount; i++)
                {
                    var p = world.Soldiers[i];
                    string n="Soldiers["+p.Initial.Id.ToString(CultureInfo.InvariantCulture)+"].";
                    w.Value(n+"Id",p.Initial.Id); w.Value(n+"FactionId",p.Initial.FactionId); w.Value(n+"ArmyId",p.Initial.ArmyId); w.Value(n+"Kind",(byte)p.Initial.Kind);
                    w.Value(n+"Alive",p.Alive); w.Point(n+"Position",p.Position); w.Value(n+"Hp",p.Hp); w.Value(n+"TargetKind",p.TargetKind); w.Value(n+"TargetId",p.TargetId);
                     w.Value(n+"NextAttackTick",p.NextAttackTick); w.Point(n+"MoveGoal",p.MoveGoal); w.Value(n+"StepDistance.Raw",p.StepDistance.Raw);
                     if (RationsOn) w.Value(n+"LastCombatTick",p.LastCombatTick);
                    w.Value(n+"IsMoving",p.IsMoving); w.Value(n+"IsAttacking",p.IsAttacking); w.Value(n+"IsRetreating",p.IsRetreating);
                    if (world.Config.Economy.LargeEconomy && p.Initial.Kind == UnitKind.Scout)
                    {
                        w.Value(n+"ExploreHasGoal", p.ExploreHasGoal); w.Point(n+"ExploreGoal", p.ExploreGoal);
                        w.Value(n+"ExploreGoalChunkX", p.ExploreGoalChunkX); w.Value(n+"ExploreGoalChunkZ", p.ExploreGoalChunkZ);
                        w.Value(n+"ExploreRetargetTick", p.ExploreRetargetTick);
                    }
                     if (CraftTree3On) w.Value(n+"SiegePartsEnhanced", p.SiegePartsEnhanced);
                     if (CraftTree4On) w.Value(n+"EliteKitEnhanced", p.EliteKitEnhanced);
                    w.Value(n+"Joining",p.Joining); w.Value(n+"TacticalRoute",p.TacticalRoute);
                    w.Value(n+"LocalCursor",p.LocalCursor); w.Value(n+"JoinCursor",p.JoinCursor); w.Value(n+"SlotWaitTimedOut",p.SlotWaitTimedOut);
                    w.Value(n+"SlotWaitSinceTick",p.SlotWaitSinceTick); w.Point(n+"SlotWaitTarget",p.SlotWaitTarget); w.Point(n+"LocalGoal",p.LocalGoal);
                    w.Value(n+"LocalPath.Count",(uint)(p.LocalPath?.Length ?? 0));
                    if (p.LocalPath != null) for (int j = 0; j < p.LocalPath.Length; j++) w.Value(n+"LocalPath["+j.ToString(CultureInfo.InvariantCulture)+"]",p.LocalPath[j]);
                    if (CultOn) w.Value(n+"CultTargetContactId", p.CultTargetContactId);
                    // Parameters are immutable copies of Config; serialize explicitly to detect accidental divergence too.
                    w.Value(n+"Parameters.Kind",(byte)p.Parameters.Kind); w.Value(n+"Parameters.Hp",p.Parameters.Hp); w.Value(n+"Parameters.Damage",p.Parameters.Damage);
                    w.Value(n+"Parameters.AttackIntervalTicks",p.Parameters.AttackIntervalTicks); w.Value(n+"Parameters.Speed.Raw",p.Parameters.Speed.Raw); w.Value(n+"Parameters.Vision.Raw",p.Parameters.Vision.Raw); w.Value(n+"Parameters.Range.Raw",p.Parameters.Range.Raw);
                }
                w.Value("Armies.Count",(uint)world.Armies.Length);
                foreach(var a in world.Armies) { string n="Armies["+a.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",a.Definition.Id); w.Ids(n+"SoldierIds",a.SoldierIds);
                    w.Value(n+"Path.Count", (uint)a.Path.Length);
                    for (int i = 0; i < a.Path.Length; i++) w.Value(n+"Path["+i.ToString(CultureInfo.InvariantCulture)+"]", a.Path[i]);
                    w.Value(n+"PathCursor",a.PathCursor); w.Value(n+"AutoStage",a.AutoStage);
                    w.Point(n+"PathGoal",a.PathGoal); w.Value(n+"HasPathGoal",a.HasPathGoal); w.Value(n+"PathImpossible",a.PathImpossible);
                }
                // Immutable definitions (including formation slots and roles) are covered by Config.Hash.
                w.Value("Outposts.Count",(uint)world.Outposts.Length);
                foreach(var o in world.Outposts) {
                    string n="Outposts["+o.Definition.Id.ToString(CultureInfo.InvariantCulture)+"].";
                    w.Value(n+"Id",o.Definition.Id); w.Value(n+"OwnerFactionId",o.OwnerFactionId);
                    w.Value(n+"CapturingFaction",o.CapturingFaction); w.Value(n+"CaptureTicks",o.CaptureTicks); w.Value(n+"NextReinforcementTick",o.NextReinforcementTick);
                }
                w.Value("Cores.Count",(uint)world.Cores.Length);
                foreach(var c in world.Cores) { string n="Cores["+c.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",c.Definition.Id); w.Value(n+"Hp",c.Hp); w.Value(n+"NextReinforcementTick",c.NextReinforcementTick); if (CoreDefenceOn) w.Value(n+"DefenceTimer",c.DefenceTimer); }
                w.Value("Factions.Count",(uint)world.Factions.Length);
                foreach(var f in world.Factions) { string n="Factions["+f.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",f.Id); w.Value(n+"CoreId",f.CoreId); w.Value(n+"AliveCount",f.AliveCount); w.Ids(n+"ArmyIds",f.ArmyIds);
                    w.Bools(n+"VisibleCells", f.VisibleCells); w.Bools(n+"ExploredCells", f.ExploredCells);
                    if (ResourceFogOn)
                    {
                        w.Bools(n+"ResourceSeen", f.ResourceSeen);
                        w.Value(n+"ResourceRemaining.Count", (uint)f.ResourceRemaining.Length);
                        for (int i = 0; i < f.ResourceRemaining.Length; i++)
                            w.Value(n+"ResourceRemaining[" + i.ToString(CultureInfo.InvariantCulture) + "]", f.ResourceRemaining[i]);
                    }
                    for(int i=0;i<f.Objectives.Length;i++) { var m=f.Objectives[i]; string o=n+"Objectives["+i.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(o+"OwnerKnown",m.OwnerKnown); w.Value(o+"Owner",m.OwnerFactionId); w.Value(o+"HpKnown",m.HpKnown); w.Value(o+"Hp",m.Hp); w.Value(o+"LastSeenTick",m.LastSeenTick); }
                }
                w.Value("Inputs.Cursor",world.InputCursor);
                WriteCommands(w);
                if (LatePushOn)
                {
                    w.Value("LatePush.Faction1", latePushLatched[0]);
                    w.Value("LatePush.Faction2", latePushLatched[1]);
                }
                if (PushWavesOn)
                {
                    for (int f = 0; f < pushWaveStates.Length; f++)
                    {
                        var wave = pushWaveStates[f];
                        string n = "PushWaves.Faction" + (f + 1).ToString(CultureInfo.InvariantCulture) + ".";
                        w.Value(n + "Phase", (byte)wave.Phase); w.Value(n + "GatheringStartedTick", wave.GatheringStartedTick);
                        w.Value(n + "AssaultStartCount", wave.AssaultStartCount); w.Value(n + "AssaultNumber", wave.AssaultNumber);
                        w.Point(n + "StagingPoint", wave.StagingPoint); w.Ids(n + "AssaultSoldierIds", wave.AssaultSoldierIds);
                    }
                }
                if (ManualControlOn)
                {
                    w.Value("ManualControl.Faction1.AutoAssaultOff", autoAssaultOff[0]);
                    w.Value("ManualControl.Faction2.AutoAssaultOff", autoAssaultOff[1]);
                }
                // Active policies are keyed by army, ordered by ApplyTick then LogIndex then army ID.
                var orders=(ArmyState[])world.Armies.Clone();
                Array.Sort(orders,(a,b)=> { int c=a.ApplyTick.CompareTo(b.ApplyTick); if(c==0)c=a.LogIndex.CompareTo(b.LogIndex); return c==0?a.Definition.Id.CompareTo(b.Definition.Id):c; });
                foreach(var a in orders) { string n="Commands[Army="+a.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Policy",(byte)a.Policy); w.Goal(n+"Goal",a.Goal); w.Value(n+"CommandId",a.CommandId); w.Value(n+"AcceptedTick",a.AcceptedTick); w.Value(n+"ApplyTick",a.ApplyTick); w.Value(n+"LogIndex",a.LogIndex); }
                foreach(var f in world.Factions) { string n="Observations["+f.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"NextArmyContactId",f.NextArmyContactId);
                    for (int i = 0; i < f.ArmyContacts.Length; i++) { var m = f.ArmyContacts[i]; string a = n+"ArmyContacts["+i.ToString(CultureInfo.InvariantCulture)+"].";
                        w.Value(a+"Id",m.Id); w.Point(a+"Position",m.Position); w.Value(a+"LastSeenTick",m.LastSeenTick);
                        w.Value(a+"Min",m.Min); w.Value(a+"Max",m.Max); w.Value(a+"Visible",m.Visible); w.Value(a+"Absent",m.Absent); w.Ids(a+"Covered",m.Covered ?? Array.Empty<uint>()); }
                    w.Value(n+"NextContactId",f.NextContactId); w.Ids(n+"ContactIds",f.ContactIds,world.SoldierCount);
                    for(int i=0;i<world.SoldierCount;i++) { string c=n+"Contacts["+i.ToString(CultureInfo.InvariantCulture)+"]."; w.Point(c+"Position",f.ContactPositions[i]); w.Value(c+"LastSeenTick",f.ContactLastSeenTicks[i]); w.Value(c+"Absent",f.ContactAbsent[i]); }
                    if (CultOn)
                        for (int i=0;i<world.SoldierCount;i++) { string c=n+"CultObservations["+i.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(c+"ContactId",f.CultObservedContactIds[i]); w.Value(c+"Kind",(byte)f.CultObservedKinds[i]); w.Value(c+"LastSeenTick",f.CultObservedLastSeenTicks[i]); w.Value(c+"ValidUntilTick",f.CultObservedValidUntilTicks[i]); } }
                WriteDecision(w);
                // Written only when the economy is on, so Ver.1 scenarios keep their exact canonical bytes.
                if (world.Config.Economy.Enabled) WriteEconomy(w);
                return new DiagnosticState(world.Tick,s.ToArray());
            }
        }
        private void WriteEconomy(StateWriter w)
        {
            for (int f = 0; f < world.Economies.Length; f++)
            {
                var e = world.Economies[f]; string n = "Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Food", e.Food); w.Value(n + "Wood", e.Wood); w.Value(n + "Queued", e.Queued); w.Value(n + "TrainRemaining", e.TrainRemaining); w.Value(n + "AutoOff", e.AutoOff);
                if (world.Config.Economy.AdjacentBuildings)
                {
                    w.Value(n + "AdjacentBuildings", true);
                    w.Value(n + "AdjacentBuildingClearanceCells", world.Config.Economy.AdjacentBuildingClearanceCells);
                }
                if (StorageFeedsProcessingOn) w.Value(n + "StorageFeedsProcessing", true);
                if (RationsOn)
                {
                    w.Value(n + "Rations", e.Rations);
                    w.Value(n + "RationsProduced", e.RationsProduced);
                    w.Value(n + "RationsConsumed", e.RationsConsumed);
                    w.Value(n + "RationsTrainedSoldiers", e.RationsTrainedSoldiers);
                    w.Value(n + "RationsHealedHp", e.RationsHealedHp);
                }
                if (BricksOn)
                {
                    w.Value(n + "Bricks", e.Bricks); w.Value(n + "BricksProduced", e.BricksProduced);
                    w.Value(n + "BricksConsumed", e.BricksConsumed); w.Value(n + "BricksUpgradedWalls", e.BricksUpgradedWalls);
                    w.Value(n + "BricksUpgradedTowers", e.BricksUpgradedTowers);
                    w.Value(n + "BricksCoreRepairHp", e.BricksCoreRepairHp);
                    w.Value(n + "BricksConsumedWalls", e.BricksConsumedWalls);
                    w.Value(n + "BricksConsumedTowers", e.BricksConsumedTowers);
                    w.Value(n + "BricksConsumedCoreRepair", e.BricksConsumedCoreRepair);
                    w.Value(n + "BricksConsumedCastles", e.BricksConsumedCastles);
                }
                if (TackOn)
                {
                    w.Value(n + "Tack", e.Tack);
                    w.Value(n + "TackProduced", e.TackProduced);
                    w.Value(n + "TackConsumed", e.TackConsumed);
                    w.Value(n + "TackTrainedHeavyCavalry", e.TackTrainedHeavyCavalry);
                }
                if (PlanksOn)
                {
                    w.Value(n + "Planks", e.Planks); w.Value(n + "PlanksProduced", e.PlanksProduced);
                    w.Value(n + "PlanksConsumed", e.PlanksConsumed); w.Value(n + "PlanksConsumedRams", e.PlanksConsumedRams);
                    w.Value(n + "PlanksConsumedBridges", e.PlanksConsumedBridges); w.Value(n + "PlanksEnhancedRams", e.PlanksEnhancedRams);
                }
                if (CraftTree1On)
                {
                    w.Value(n + "CutStone", e.CutStone); w.Value(n + "CutStoneProduced", e.CutStoneProduced); w.Value(n + "CutStoneConsumed", e.CutStoneConsumed);
                    w.Value(n + "Frames", e.Frames); w.Value(n + "FramesProduced", e.FramesProduced); w.Value(n + "FramesConsumed", e.FramesConsumed);
                    w.Value(n + "FramesConsumedBuildings", e.FramesConsumedBuildings); w.Value(n + "FramesConsumedUpgrades", e.FramesConsumedUpgrades);
                }
                if (PrimitiveCraftOn)
                {
                    w.Value(n + "StoneTools", e.StoneTools); w.Value(n + "StoneToolsProduced", e.StoneToolsProduced);
                    w.Value(n + "StoneToolsConsumed", e.StoneToolsConsumed); w.Value(n + "StoneToolsTrainedVillagers", e.StoneToolsTrainedVillagers);
                    w.Value(n + "Jerky", e.Jerky); w.Value(n + "JerkyProduced", e.JerkyProduced);
                    w.Value(n + "JerkyConsumed", e.JerkyConsumed); w.Value(n + "JerkyTrainedSoldiers", e.JerkyTrainedSoldiers);
                    var stoneTools = e.QueueStoneTools ?? Array.Empty<bool>();
                    w.Value(n + "QueueStoneTools.Count", (uint)stoneTools.Length);
                    for (int q = 0; q < stoneTools.Length; q++) w.Value(n + "QueueStoneTools[" + q.ToString(CultureInfo.InvariantCulture) + "]", stoneTools[q]);
                }
                if (CraftTree2On)
                {
                    w.Value(n + "ArmsKit", e.ArmsKit); w.Value(n + "ArmsKitProduced", e.ArmsKitProduced); w.Value(n + "ArmsKitConsumed", e.ArmsKitConsumed);
                    w.Value(n + "FineArmsKit", e.FineArmsKit); w.Value(n + "FineArmsKitProduced", e.FineArmsKitProduced); w.Value(n + "FineArmsKitConsumed", e.FineArmsKitConsumed);
                    w.Value(n + "Scholarly", e.Scholarly); w.Value(n + "ScholarlyProduced", e.ScholarlyProduced); w.Value(n + "ScholarlyConsumed", e.ScholarlyConsumed);
                     w.Value(n + "ScholarlyUsedResearch", e.ScholarlyUsedResearch);
                }
                if (CraftTree3On)
                {
                    w.Value(n + "SiegeParts", e.SiegeParts); w.Value(n + "SiegePartsProduced", e.SiegePartsProduced); w.Value(n + "SiegePartsConsumed", e.SiegePartsConsumed); w.Value(n + "SiegePartsEnhancedRams", e.SiegePartsEnhancedRams);
                    w.Value(n + "Mechanism", e.Mechanism); w.Value(n + "MechanismProduced", e.MechanismProduced); w.Value(n + "MechanismConsumed", e.MechanismConsumed);
                    w.Value(n + "MechanismUsedResearch", e.MechanismUsedResearch);
                }
                if (AiFactoryOn)
                {
                    w.Value(n + "MetalProduced", e.MetalProduced);
                    w.Value(n + "MetalConsumed", e.MetalConsumed);
                    w.Value(n + "AiFactorySkippedUnknownResource", e.AiFactorySkippedUnknownResource);
                    w.Value(n + "AiFactorySkippedNoSite", e.AiFactorySkippedNoSite);
                    w.Value(n + "AiFactorySkippedNoRoute", e.AiFactorySkippedNoRoute);
                }
                if (CraftTree4On)
                {
                    w.Value(n + "SiegeEngine", e.SiegeEngine); w.Value(n + "SiegeEngineProduced", e.SiegeEngineProduced); w.Value(n + "SiegeEngineConsumed", e.SiegeEngineConsumed);
                    w.Value(n + "EliteKit", e.EliteKit); w.Value(n + "EliteKitProduced", e.EliteKitProduced); w.Value(n + "EliteKitConsumed", e.EliteKitConsumed);
                    w.Value(n + "CannonProduced", e.CannonProduced);
                }
                if (BeltCraftOn)
                {
                    w.Value(n + "Gears", e.Gears);
                    w.Value(n + "GearsProduced", e.GearsProduced);
                    w.Value(n + "GearsConsumed", e.GearsConsumed);
                }
                if (BooksOn)
                {
                    w.Value(n + "Books", e.Books); w.Value(n + "BooksProduced", e.BooksProduced);
                    w.Value(n + "BooksConsumed", e.BooksConsumed); w.Value(n + "BooksUsedResearch", e.BooksUsedResearch);
                }
                if (PreservesOn)
                {
                    w.Value(n + "PreservesProduced", e.PreservesProduced);
                    w.Value(n + "PreservesConvertedFood", e.PreservesConvertedFood);
                }
                if (JewelsOn)
                {
                    w.Value(n + "JewelsProduced", e.JewelsProduced);
                    w.Value(n + "JewelsConsumed", e.JewelsConsumed);
                    w.Value(n + "JewelsTrainedMercenaries", e.JewelsTrainedMercenaries);
                }
                if (TradeGoodsOn)
                {
                    w.Value(n + "TradeGoodsProduced", e.TradeGoodsProduced);
                    w.Value(n + "TradeGoodsConvertedFood", e.TradeGoodsConvertedFood);
                    w.Value(n + "TradeGoodsConvertedStone", e.TradeGoodsConvertedStone);
                }
                if (IncenseOn)
                {
                    w.Value(n + "Incense", e.Incense);
                    w.Value(n + "IncenseProduced", e.IncenseProduced);
                    w.Value(n + "IncenseConsumed", e.IncenseConsumed);
                    w.Value(n + "IncenseHealedHp", e.IncenseHealedHp);
                }
                if (CharmsOn)
                {
                    w.Value(n + "Charms", e.Charms);
                    w.Value(n + "CharmsProduced", e.CharmsProduced);
                    w.Value(n + "CharmsConsumed", e.CharmsConsumed);
                    w.Value(n + "CharmsTrainedSoldiers", e.CharmsTrainedSoldiers);
                }
                if (RepairKitsOn)
                {
                    w.Value(n + "RepairKits", e.RepairKits);
                    w.Value(n + "RepairKitsProduced", e.RepairKitsProduced);
                    w.Value(n + "RepairKitsConsumed", e.RepairKitsConsumed);
                    w.Value(n + "RepairKitsRepairedHp", e.RepairKitsRepairedHp);
                    w.Value(n + "RepairKitsCoreRepairedHp", e.RepairKitsCoreRepairedHp);
                }
                if (ToolsOn)
                {
                    w.Value(n + "Tools", e.Tools); w.Value(n + "ToolsProduced", e.ToolsProduced);
                    w.Value(n + "ToolsConsumed", e.ToolsConsumed); w.Value(n + "ToolsTrainedVillagers", e.ToolsTrainedVillagers);
                    w.Value(n + "ToolsSavedFood", e.ToolsSavedFood);
                    var tools = e.QueueTools ?? Array.Empty<bool>();
                    w.Value(n + "QueueTools.Count", (uint)tools.Length);
                    for (int q = 0; q < tools.Length; q++) w.Value(n + "QueueTools[" + q.ToString(CultureInfo.InvariantCulture) + "]", tools[q]);
                }
                if (GoodsMarketOn && e.GoodsSoldCounts != null)
                {
                    foreach (var goods in GoodsKinds)
                    {
                        if ((goods == ResourceKind.SiegeEngine || goods == ResourceKind.EliteKit) && !CraftTree4On) continue;
                        int i = (int)goods;
                        string key = n + "GoodsMarket." + goods.ToString();
                        w.Value(key + ".SoldCount", e.GoodsSoldCounts[i]);
                        w.Value(key + ".SoldUnits", e.GoodsSoldUnits[i]);
                        w.Value(key + ".ReceivedFood", e.GoodsReceivedFood[i]);
                        w.Value(key + ".ReceivedWood", e.GoodsReceivedWood[i]);
                    }
                }
            }
            if (RegionsOn)
            {
                uint changed = 0;
                for (int f = 0; f < world.Factions.Length; f++)
                    for (int i = 0; i < world.Regions.Length; i++)
                        if (world.Factions[f].RegionHuman[i] || world.Factions[f].RegionEconomyPolicies[i] != EconomyPolicy.Balanced) changed++;
                w.Value("Regions.Changes.Count", changed);
                for (int f = 0; f < world.Factions.Length; f++)
                    for (int i = 0; i < world.Regions.Length; i++)
                    {
                        if (!world.Factions[f].RegionHuman[i] && world.Factions[f].RegionEconomyPolicies[i] == EconomyPolicy.Balanced) continue;
                        string n = "Regions.Changes[" + f.ToString(CultureInfo.InvariantCulture) + "][" + i.ToString(CultureInfo.InvariantCulture) + "].";
                        w.Value(n + "FactionId", (uint)(f + 1)); w.Value(n + "RegionId", (uint)(i + 1));
                        w.Value(n + "Human", world.Factions[f].RegionHuman[i]);
                        w.Value(n + "EconomyPolicy", (byte)world.Factions[f].RegionEconomyPolicies[i]);
                    }
            }
            w.Value("ResourceNodes.Count", (uint)world.Nodes.Length);
            foreach (var r in world.Nodes)
            {
                string n = "ResourceNodes[" + r.Definition.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Remaining", r.Remaining);
                if (world.Config.Economy.FishingEnabled) w.Value(n + "Fishing", r.Fishing);
            }
            w.Value("NextBuildingId", world.NextBuildingId);
            w.Value("Buildings.Count", (uint)world.BuildingCount);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i]; string n = "Buildings[" + b.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Id", b.Id); w.Value(n + "FactionId", b.FactionId); w.Value(n + "Kind", (byte)b.Kind);
                w.Value(n + "OriginCell", b.OriginCell); w.Value(n + "WorkCell", b.WorkCell); w.Value(n + "Alive", b.Alive); w.Value(n + "Complete", b.Complete);
                w.Value(n + "Hp", b.Hp); w.Value(n + "Progress", b.Progress); w.Value(n + "Queued", b.Queued); w.Value(n + "TrainRemaining", b.TrainRemaining);
                if (AiFactoryOn && b.Kind == BuildingKind.Barracks)
                {
                    w.Value(n + "AiFactoryIdleTicks", b.AiFactoryIdleTicks);
                    w.Value(n + "AiFactoryIdleWindowStartTick", b.AiFactoryIdleWindowStartTick);
                }
                if (world.Config.Economy.BuildingReservations)
                {
                    w.Value(n + "Reserved", b.Reserved);
                    w.Value(n + "ReservationOrder", b.Reserved ? ReservationOrderFor(b) : 0);
                }
                // V3-2: only with industry, so a V3-1 building keeps its exact canonical bytes.
                if (world.Config.Economy.Industry)
                {
                    w.Value(n + "Facing", (byte)b.Facing); w.Value(n + "Input", b.Input); w.Value(n + "Output", b.Output);
                    w.Value(n + "Timer", b.Timer); w.Value(n + "NodeId", b.NodeId); w.Value(n + "Held", b.Held);
                    if (world.Config.Economy.Ages)
                    {
                        w.Value(n + "QueuedMetal", b.QueuedMetal); w.Value(n + "QueuedGems", b.QueuedGems); w.Value(n + "Interval", b.Interval); w.Value(n + "Shots", b.Shots); w.Value(n + "Researching", (byte)b.Researching);
                        if (world.Config.Economy.GoldEnabled) w.Value(n + "QueuedGold", b.QueuedGold);
                        var kinds = b.QueueKinds ?? Array.Empty<UnitKind>();
                        w.Value(n + "QueueKinds.Count", (uint)kinds.Length);
                        for (int q = 0; q < kinds.Length; q++) w.Value(n + "QueueKinds[" + q.ToString(CultureInfo.InvariantCulture) + "]", (byte)kinds[q]);
                        if (RationsOn)
                        {
                            var rationed = b.QueueRations ?? Array.Empty<bool>();
                            w.Value(n + "QueueRations.Count", (uint)rationed.Length);
                            for (int q = 0; q < rationed.Length; q++) w.Value(n + "QueueRations[" + q.ToString(CultureInfo.InvariantCulture) + "]", rationed[q]);
                        }
                        if (PlanksOn)
                        {
                            var planked = b.QueuePlanks ?? Array.Empty<bool>();
                            w.Value(n + "QueuePlanks.Count", (uint)planked.Length);
                            for (int q = 0; q < planked.Length; q++) w.Value(n + "QueuePlanks[" + q.ToString(CultureInfo.InvariantCulture) + "]", planked[q]);
                        }
                        if (CharmsOn)
                        {
                            var charmed = b.QueueCharms ?? Array.Empty<bool>();
                            w.Value(n + "QueueCharms.Count", (uint)charmed.Length);
                            for (int q = 0; q < charmed.Length; q++) w.Value(n + "QueueCharms[" + q.ToString(CultureInfo.InvariantCulture) + "]", charmed[q]);
                        }
                        if (ToolsOn)
                        {
                            var tools = b.QueueTools ?? Array.Empty<bool>();
                            w.Value(n + "QueueTools.Count", (uint)tools.Length);
                            for (int q = 0; q < tools.Length; q++) w.Value(n + "QueueTools[" + q.ToString(CultureInfo.InvariantCulture) + "]", tools[q]);
                        }
                        if (PrimitiveCraftOn)
                        {
                            var jerky = b.QueueJerky ?? Array.Empty<bool>();
                            w.Value(n + "QueueJerky.Count", (uint)jerky.Length);
                            for (int q = 0; q < jerky.Length; q++) w.Value(n + "QueueJerky[" + q.ToString(CultureInfo.InvariantCulture) + "]", jerky[q]);
                        }
                    }
                    if (b.Kind == BuildingKind.Bridge)
                    {
                        w.Value(n + "DestroyedTick", b.DestroyedTick);
                        var bridgeCells = b.BridgeCells ?? Array.Empty<int>();
                        w.Value(n + "BridgeCells.Count", (uint)bridgeCells.Length);
                        for (int j = 0; j < bridgeCells.Length; j++)
                            w.Value(n + "BridgeCells[" + j.ToString(CultureInfo.InvariantCulture) + "]", bridgeCells[j]);
                    }
                    if (world.Config.Economy.ProcessingChain || BricksOn || TackOn || PlanksOn || CraftTree1On || BooksOn || PreservesOn || JewelsOn || TradeGoodsOn || IncenseOn || CharmsOn || RepairKitsOn || ToolsOn || world.Config.Economy.Forestry && world.Economies[b.FactionId - 1].Age >= 2)
                    {
                        w.Value(n + "InputSecondary", b.InputSecondary); w.Value(n + "QueuedSteel", b.QueuedSteel);
                        if (BricksOn) w.Value(n + "BrickUpgraded", b.BrickUpgraded);
                        if (world.Config.Economy.Forestry && world.Economies[b.FactionId - 1].Age >= 2) w.Value(n + "QueuedBowGear", b.QueuedBowGear);
                    }
                    if (world.Config.Economy.Mountain && b.Kind == BuildingKind.MineShaft)
                        w.Value(n + "MountainOreNext", b.MountainOreNext);
                    if (CraftTree1On)
                    {
                        w.Value(n + "Level", b.Level);
                        w.Value(n + "FramesUsed", b.FramesUsed);
                    }
                    if (CraftTree2On)
                    {
                        w.Value(n + "InputTertiary", b.InputTertiary); w.Value(n + "Recipe", (byte)b.Recipe);
                        w.Value(n + "InputSecondaryKind", (byte)b.InputSecondaryKind);
                        var armed = b.QueueArmsKit ?? Array.Empty<bool>();
                        var fineArmed = b.QueueFineArmsKit ?? Array.Empty<bool>();
                        w.Value(n + "QueueArmsKit.Count", (uint)armed.Length);
                        for (int q = 0; q < armed.Length; q++) w.Value(n + "QueueArmsKit[" + q.ToString(CultureInfo.InvariantCulture) + "]", armed[q]);
                        w.Value(n + "QueueFineArmsKit.Count", (uint)fineArmed.Length);
                        for (int q = 0; q < fineArmed.Length; q++) w.Value(n + "QueueFineArmsKit[" + q.ToString(CultureInfo.InvariantCulture) + "]", fineArmed[q]);
                        var siegeQueue = b.QueueSiegeParts ?? Array.Empty<bool>();
                        w.Value(n + "QueueSiegeParts.Count", (uint)siegeQueue.Length);
                        for (int q = 0; q < siegeQueue.Length; q++) w.Value(n + "QueueSiegeParts[" + q.ToString(CultureInfo.InvariantCulture) + "]", siegeQueue[q]);
                        if (CraftTree4On)
                        {
                            var eliteQueue = b.QueueEliteKit ?? Array.Empty<bool>();
                            w.Value(n + "QueueEliteKit.Count", (uint)eliteQueue.Length);
                            for (int q = 0; q < eliteQueue.Length; q++) w.Value(n + "QueueEliteKit[" + q.ToString(CultureInfo.InvariantCulture) + "]", eliteQueue[q]);
                            var engineQueue = b.QueueSiegeEngine ?? Array.Empty<bool>();
                            w.Value(n + "QueueSiegeEngine.Count", (uint)engineQueue.Length);
                            for (int q = 0; q < engineQueue.Length; q++) w.Value(n + "QueueSiegeEngine[" + q.ToString(CultureInfo.InvariantCulture) + "]", engineQueue[q]);
                            w.Value(n + "QueuedSiegeEngine", b.QueuedSiegeEngine);
                        }
                    }
                if (world.Config.Economy.BeltComponents && b.Kind == BuildingKind.Storage)
                {
                    w.Value(n + "StorageKind", (byte)b.StorageKind); w.Value(n + "StorageCount", b.StorageCount);
                }
                if (EasyRebuildOn)
                {
                    w.Value(n + "ConstructionWood", b.ConstructionWood); w.Value(n + "ConstructionStone", b.ConstructionStone);
                    w.Value(n + "ConstructionPlanks", b.ConstructionPlanks); w.Value(n + "ConstructionBricks", b.ConstructionBricks);
                    w.Value(n + "ConstructionSiegeParts", b.ConstructionSiegeParts); w.Value(n + "ConstructionFrames", b.ConstructionFrames);
                    w.Value(n + "MovePauseUntilTick", b.MovePauseUntilTick);
                }
                }
                if (world.Config.Economy.Caravan && b.Kind == BuildingKind.Caravanserai)
                {
                    w.Value(n + "CaravanOutpostId", b.CaravanOutpostId); w.Value(n + "CaravanMarketId", b.CaravanMarketId);
                    w.Value(n + "CaravanDistance.Raw", b.CaravanDistance.Raw); w.Value(n + "CaravanWoodReward", b.CaravanWoodReward);
                }
                if (world.Config.Economy.Sanctuary && b.Kind == BuildingKind.Shrine)
                    w.Value(n + "SanctuaryOutpostId", b.SanctuaryOutpostId);
            }
            w.Value("NextVillagerId", world.NextVillagerId);
            w.Value("Villagers.Count", (uint)world.VillagerCount);
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i]; string n = "Villagers[" + v.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Id", v.Id); w.Value(n + "FactionId", v.FactionId); w.Value(n + "Alive", v.Alive); w.Value(n + "Hp", v.Hp);
                w.Point(n + "Position", v.Position); w.Point(n + "MoveGoal", v.MoveGoal); w.Value(n + "IsMoving", v.IsMoving);
                w.Value(n + "Task", (byte)v.Task); w.Value(n + "NodeId", v.NodeId); w.Value(n + "CarryKind", (byte)v.CarryKind); w.Value(n + "Carry", v.Carry);
                w.Value(n + "NextGatherTick", v.NextGatherTick);
                if (BeltDropOffOn && v.BeltWaitUntilTick != 0) w.Value(n + "BeltWaitUntilTick", v.BeltWaitUntilTick);
                if (AiExpandOn) w.Value(n + "AiExpandWorkStartTick", v.AiExpandWorkStartTick);
                w.Point(n + "RouteGoal", v.RouteGoal); w.Value(n + "RouteCursor", v.RouteCursor);
                if (PrimitiveCraftOn) w.Value(n + "StoneToolsUsed", v.StoneToolsUsed);
                w.Value(n + "BuildingId", v.BuildingId);
                if (world.Config.Economy.Caravan)
                {
                    w.Value(n + "CaravanMarketId", v.CaravanMarketId); w.Value(n + "CaravanseraiId", v.CaravanseraiId);
                    w.Value(n + "CaravanOutpostId", v.CaravanOutpostId); w.Value(n + "CaravanStage", v.CaravanStage); w.Value(n + "CaravanWood", v.CaravanWood);
                    w.Value(n + "CaravanGems", v.CaravanGems); w.Value(n + "CaravanDangerStopped", v.CaravanDangerStopped); w.Value(n + "CaravanSafeTicks", v.CaravanSafeTicks);
                }
                if (world.Config.Economy.Industry) { w.Value(n + "HaulFrom", v.HaulFrom); w.Value(n + "HaulTo", v.HaulTo); w.Value(n + "Held", v.Held); }
                if (world.Config.Economy.ProcessingChain) w.Value(n + "HaulNodeId", v.HaulNodeId);
                w.Value(n + "Route.Count", (uint)v.Route.Length);
                for (int j = 0; j < v.Route.Length; j++) w.Value(n + "Route[" + j.ToString(CultureInfo.InvariantCulture) + "]", v.Route[j]);
            }
            // V3-5: the class each soldier was trained as, only with ages.
            if (world.Config.Economy.Ages)
                for (int i = 0; i < world.SoldierCount; i++)
                    w.Value("Soldiers[" + world.Soldiers[i].Initial.Id.ToString(CultureInfo.InvariantCulture) + "].Class", (byte)world.Soldiers[i].Class);
            // A reserved civilisation, written only while one is held, so a match without one keeps its exact bytes.
            if (world.Config.Economy.Ages)
                for (int f = 0; f < world.Economies.Length; f++)
                    if (world.Economies[f].ReservedCiv != CivKind.Primitive)
                        w.Value("Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].ReservedCiv", (byte)world.Economies[f].ReservedCiv);
            if (world.Config.Economy.AgeClockUx)
                for (int f = 0; f < world.Economies.Length; f++)
                    if (world.Economies[f].CivChoiceDeadlineTick != 0)
                        w.Value("Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].CivChoiceDeadlineTick", world.Economies[f].CivChoiceDeadlineTick);
            // V3-2: written only with industry, so a V3-1 economy keeps its exact canonical bytes.
            if (!world.Config.Economy.Industry)
            {
                if (AgeFourAiOn)
                    for (int f = 0; f < world.Economies.Length; f++)
                    {
                        var e = world.Economies[f]; string n = "Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                        w.Value(n + "AgeFourAiResearchFailureTech", (byte)e.AgeFourAiResearchFailureTech);
                        w.Value(n + "AgeFourAiResearchFailureReason", e.AgeFourAiResearchFailureReason);
                        w.Value(n + "AgeFourAiResearchFailureTick", e.AgeFourAiResearchFailureTick);
                    }
                return;
            }
            for (int f = 0; f < world.Economies.Length; f++)
            {
                var e = world.Economies[f]; string n = "Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Ore", e.Ore); w.Value(n + "Metal", e.Metal); w.Value(n + "CoreHeld", e.CoreHeld); w.Value(n + "Policy", (byte)e.Policy);
                if (world.Config.Economy.ProcessingChain) { w.Value(n + "Charcoal", e.Charcoal); w.Value(n + "Steel", e.Steel); }
                if (world.Config.Economy.Forestry && e.Age >= 2) w.Value(n + "BowGear", e.BowGear);
                if (world.Config.Economy.Ages) { w.Value(n + "Stone", e.Stone); w.Value(n + "Gems", e.Gems); w.Value(n + "Techs", e.Techs); w.Value(n + "Age", e.Age); }
                if (world.Config.Economy.GoldEnabled) w.Value(n + "Gold", e.Gold);
                if (world.Config.Economy.Ages) { w.Value(n + "Civ", (byte)e.Civ); w.Value(n + "AdvancingTo", (byte)e.AdvancingTo); w.Value(n + "AdvanceRemaining", e.AdvanceRemaining); w.Value(n + "AgeVictoryProgress", e.AgeVictoryProgress); }
                if (AgeFourAiOn)
                {
                    w.Value(n + "AgeFourAiResearchFailureTech", (byte)e.AgeFourAiResearchFailureTech);
                    w.Value(n + "AgeFourAiResearchFailureReason", e.AgeFourAiResearchFailureReason);
                    w.Value(n + "AgeFourAiResearchFailureTick", e.AgeFourAiResearchFailureTick);
                }
            }
            uint belts = 0;
            foreach (var b in world.Belts) if (b.FactionId != 0) belts++;
            w.Value("Belts.Count", belts);
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                var b = world.Belts[cell];
                if (b.FactionId == 0) continue;
                string n = "Belts[" + cell.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "FactionId", b.FactionId); w.Value(n + "Facing", (byte)b.Facing); w.Value(n + "Hp", b.Hp);
                w.Value(n + "Item", (byte)b.Item); w.Value(n + "Progress", b.Progress); w.Value(n + "Held", b.Held);
                if (world.Config.Economy.BeltStack) w.Value(n + "StackCount", b.StackCount);
                if (world.Config.Economy.BeltComponents)
                {
                    w.Value(n + "Component", (byte)b.Component); w.Value(n + "SorterKind", (byte)b.SorterKind);
                    w.Value(n + "PairCell", b.PairCell); w.Value(n + "SplitterRightNext", b.SplitterRightNext); w.Value(n + "Fast", b.Fast);
                    if (BeltCraftOn)
                    {
                        w.Value(n + "VeryFast", b.VeryFast); w.Value(n + "Priority", (byte)b.Priority);
                    }
                }
            }
            if (BeltCraftOn && world.Catapults.Length > 0)
            {
                w.Value("Catapults.Count", (uint)world.Catapults.Length);
                for (int i = 0; i < world.Catapults.Length; i++)
                {
                    var c = world.Catapults[i]; string n = "Catapults[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                    w.Value(n + "Active", c.Active); w.Value(n + "FactionId", c.FactionId); w.Value(n + "LauncherCell", c.LauncherCell);
                    w.Value(n + "ReceiverCell", c.ReceiverCell); w.Value(n + "BufferKind", (byte)c.BufferKind); w.Value(n + "BufferedCount", c.BufferedCount);
                    w.Value(n + "FirstLoadTick", c.FirstLoadTick); w.Value(n + "LastThrowTick", c.LastThrowTick);
                    w.Value(n + "ReceiverKind", (byte)c.ReceiverKind); w.Value(n + "ReceiverCount", c.ReceiverCount);
                    w.Value(n + "Shots.Count", (uint)c.Shots.Length);
                    for (int j = 0; j < c.Shots.Length; j++)
                    {
                        var shot = c.Shots[j]; string sn = n + "Shots[" + j.ToString(CultureInfo.InvariantCulture) + "].";
                        w.Value(sn + "Active", shot.Active); w.Value(sn + "Item", (byte)shot.Item); w.Value(sn + "Count", shot.Count); w.Value(sn + "RemainingTicks", shot.RemainingTicks);
                    }
                }
            }
            if (world.Config.Economy.ProcessingChain || BricksOn || TackOn || PlanksOn || CraftTree1On || BooksOn || PreservesOn || JewelsOn || TradeGoodsOn || IncenseOn || CharmsOn || RepairKitsOn || BeltCraftOn || world.Config.Economy.Forestry && world.Economies[0].Age >= 2)
            {
                w.Value("ProcessingLines.NextId", world.NextProcessingLineId);
                w.Value("ProcessingLines.Count", (uint)world.ProcessingLines.Length);
                for (int i = 0; i < world.ProcessingLines.Length; i++)
                {
                    var line = world.ProcessingLines[i];
                    string n = "ProcessingLines[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                    w.Value(n + "Id", line.Id); w.Value(n + "FactionId", line.FactionId); w.Value(n + "Kind", (byte)line.Kind);
                    w.Value(n + "Manager", (byte)line.Manager); w.Value(n + "MineId", line.MineId); w.Value(n + "SmelterId", line.SmelterId);
                    w.Value(n + "KilnId", line.KilnId); w.Value(n + "SteelworksId", line.SteelworksId); w.Value(n + "AssemblerId", line.AssemblerId);
                    if (RationsOn) w.Value(n + "MillId", line.MillId);
                    if (BricksOn) { w.Value(n + "QuarryId", line.QuarryId); w.Value(n + "BrickKilnId", line.BrickKilnId); }
                    if (TackOn) w.Value(n + "SaddleryId", line.SaddleryId);
                    if (PlanksOn) { w.Value(n + "LumberCampId", line.LumberCampId); w.Value(n + "SawmillId", line.SawmillId); }
                    if (CraftTree1On) { w.Value(n + "StonecutterId", line.StonecutterId); w.Value(n + "JoineryId", line.JoineryId); }
                    if (BooksOn) w.Value(n + "ScriptoriumId", line.ScriptoriumId);
                    if (PreservesOn) w.Value(n + "DryingRackId", line.DryingRackId);
                    if (JewelsOn) w.Value(n + "LapidaryId", line.LapidaryId);
                    if (TradeGoodsOn) w.Value(n + "WeaverId", line.WeaverId);
                    if (IncenseOn) w.Value(n + "IncenseHouseId", line.IncenseHouseId);
                    if (CharmsOn) w.Value(n + "CharmWorkshopId", line.CharmWorkshopId);
                    if (RepairKitsOn) w.Value(n + "RepairYardId", line.RepairYardId);
                    if (ToolsOn) w.Value(n + "ToolShopId", line.ToolShopId);
                    if (PrimitiveCraftOn) w.Value(n + "HutId", line.HutId);
                    if (BeltCraftOn) w.Value(n + "GearWorkshopId", line.GearWorkshopId);
                    if (AiFactoryOn) w.Value(n + "DrillId", line.DrillId);
                    if (world.Config.Economy.LineRebuildDelayTicks > 0)
                        w.Value(n + "RebuildAvailableTick", line.RebuildAvailableTick);
                    if (line.RequestPending)
                    {
                        w.Value(n + "RequestPending", true); w.Value(n + "RequestedCell", line.RequestedCell);
                        w.Value(n + "RequestedRegionId", line.RequestedRegionId);
                    }
                    w.Value(n + "BeltCount", (uint)line.BeltCells.Length);
                    for (int j = 0; j < line.BeltCells.Length; j++)
                    {
                        w.Value(n + "Belts[" + j.ToString(CultureInfo.InvariantCulture) + "].Cell", line.BeltCells[j]);
                        w.Value(n + "Belts[" + j.ToString(CultureInfo.InvariantCulture) + "].Facing", (byte)line.BeltFacings[j]);
                    }
                }
            }
        }
        private sealed class StateWriter : BinaryWriter
        {
            public StateWriter(Stream s):base(s,Encoding.UTF8,true) { }
            private void Name(string n,byte type) { RawText(n); Write(type); }
            private void RawText(string v) { var b=Encoding.UTF8.GetBytes(v); Write((uint)b.Length); Write(b); }
            public void Value(string n,byte v) { Name(n,1); Write(v); }
            public void Value(string n,bool v) { Name(n,2); Write(v); }
            public void Value(string n,int v) { Name(n,3); Write(v); }
            public void Value(string n,uint v) { Name(n,4); Write(v); }
            public void Value(string n,long v) { Name(n,5); Write(v); }
            public void Value(string n,ulong v) { Name(n,6); Write(v); }
            public void Text(string n,string v) { Name(n,7); RawText(v); }
            public void Blob(string n,byte[] v) { Name(n,8); Write((uint)v.Length); Write(v); }
            public void Point(string n,SimPoint p) { Value(n+".X.Raw",p.X.Raw); Value(n+".Z.Raw",p.Z.Raw); }
            public void Goal(string n,PolicyGoal g) { Value(n+".Kind",(byte)g.Kind); Value(n+".Id",g.Id); Point(n+".Point",g.Point); }
            public void Ids(string n,uint[] ids) => Ids(n,ids,ids.Length);
            public void Ids(string n,uint[] ids,int count) { Value(n+".Count",(uint)count); for(int i=0;i<count;i++)Value(n+"["+i.ToString(CultureInfo.InvariantCulture)+"]",ids[i]); }
            public void Bools(string n,bool[] values)
            {
                Value(n+".Count",(uint)values.Length);
                for(int wordIndex=0;wordIndex<(values.Length+63)/64;wordIndex++)
                {
                    ulong word=0;
                    int first=wordIndex*64;
                    int count=Math.Min(64,values.Length-first);
                    for(int bit=0;bit<count;bit++) if(values[first+bit]) word|=1UL<<bit;
                    Value(n+".Words["+wordIndex.ToString(CultureInfo.InvariantCulture)+"]",word);
                }
            }
        }
    }
}
