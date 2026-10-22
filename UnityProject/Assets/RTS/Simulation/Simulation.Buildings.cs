using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3 buildings (technical-design-v3 5.1-5.4, V3-1 PR3a): the automatic economy places one barracks near the core,
    /// villagers build it, and it trains infantry that join the armies through the Ver.1 reinforcement assignment.
    /// A footprint is impassable from placement on, so the terrain changes: every cached route is dropped and every
    /// army and unit plans again. Buildings cannot be seen or attacked by the enemy yet (PR3b).
    /// </summary>
    public sealed partial class Simulation
    {
        private const int SiteSearchRadiusCells = 30, CoreClearanceCells = 4, NodeClearanceCells = 2, BuildingClearanceCells = 2;
        // V3-11 #4 provisional simultaneous bridge limit. It is deliberately a simulation rule rather than a
        // contract field, so enabling bridges does not alter the old scenario bytes.
        private const int MaxActiveBridges = 3;

        private static bool IsActualBuilding(BuildingState building) => building.Alive && !building.Reserved;

        private bool IsPlannedBuilding(BuildingState building)
            => building.Alive && (!building.Reserved || BuildingReservationsOn);

        private bool IsActualBuilding(uint id)
            => id != 0 && id <= world.BuildingCount && IsActualBuilding(world.Buildings[id - 1]);

        /// <summary>AI phase, after villager training (5.4 steps 2 and 3).</summary>
        private void DecideBuildings(uint faction)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (LargeEconomyOn) DecideTown(faction);
            bool hasBarracks = false, ready = false;
            int barracks = -1, barracksCount = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || (b.Reserved && !BuildingReservationsOn) || b.FactionId != faction || b.Kind != BuildingKind.Barracks) continue;
                hasBarracks = true;
                barracksCount++;
                // V3-3: a barracks the player operates is not queued by the automatic economy.
                if (b.Complete && !b.Held && barracks < 0) { ready = true; barracks = i; }
            }
            if (EconomyScaleOn)
            {
                int desired = LargeEconomyOn
                    ? Math.Min(rules.LargeBarracksMaxBuildings, Math.Max(1, LivingVillagers(faction) / rules.LargeBarracksVillagersPerBuilding))
                    : Math.Min(4, Math.Max(1, LivingVillagers(faction) / 15));
                bool mayAddBarracks = barracksCount == 0 || !SavingToAdvance(faction);
                if (mayAddBarracks && EconomyDecision.ShouldBuildBarracks(barracksCount, desired, economy.Wood - (BuildingReservationsOn ? ReservedConstructionWood(faction) : 0), rules.BarracksWoodCost))
                    PlaceBarracks(faction);
            }
            else if (EconomyDecision.ShouldBuildBarracks(hasBarracks, economy.Wood - (BuildingReservationsOn ? ReservedConstructionWood(faction) : 0), rules.BarracksWoodCost))
                PlaceBarracks(faction);
            if (!LargeEconomyOn) DecideTown(faction);
            DecideSanctuaryShrine(faction);
            if (barracks < 0) return;
            if (CultAllowed(faction) && !DecideCultMonastery(faction)) return;
            ref var first = ref world.Buildings[barracks];
            if (CultAllowed(faction) && DecideCultMonk(faction)) return;
            if (DecideMonk(faction, ref first)) return;
            if (DecideScout(faction, ref first)) return;
            if (!EconomyScaleOn)
            {
                // The hire goes first: the infantry queue refills every time it empties, so a later check never ran.
                if (!DecideMountainMercenary(faction, ref first)) DecideInfantry(faction, ref first, ready);
                return;
            }
            // With scaling, each finished and unheld barracks gets its own short queue. The scan is in stable
            // building-id order, so replay order does not depend on collection enumeration.
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var building = ref world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || building.Kind != BuildingKind.Barracks || building.Held) continue;
                if (!DecideMountainMercenary(faction, ref building)) DecideInfantry(faction, ref building, true);
            }
        }

        private bool DecideMountainMercenary(uint faction, ref BuildingState barracks)
        {
            if (!JewelsAllowed(faction) || world.Economies[faction - 1].Age < 2 || !barracks.Complete
                || barracks.Held || !Trains(barracks, UnitKind.Mercenary)) return false;
            var rules = world.Config.Economy;
            // Not "an empty queue": the infantry queue is refilled before it empties, and gems piled up unused.
            if (barracks.Queued >= rules.QueueLimit) return false;
            int infantry = CountClass(faction, UnitKind.Infantry);
            int mercenaries = CountClass(faction, UnitKind.Mercenary);
            if (mercenaries * rules.JewelMercenaryInfantryRatio >= infantry) return false;
            if (!HasRoomFor(faction, UnitKind.Mercenary) || !CanPay(faction, UnitKind.Mercenary, barracks.Kind)) return false;
            Enqueue(faction, ref barracks, UnitKind.Mercenary);
            return true;
        }

        private bool DecideInfantry(uint faction, ref BuildingState building, bool ready)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction) + QueuedTownVillagers(faction);
            if (!EconomyDecision.ShouldTrainInfantry(ready, building.Queued, Math.Min(PlanOf(faction).InfantryQueue, rules.QueueLimit),
                economy.Food, economy.Wood, InfantryFoodFor(faction), InfantryWoodFor(faction), population, PopCapFor(faction), HasInfantryRoom(faction))
                || economy.Metal < InfantryMetalFor(faction) || (SavingToAdvance(faction) && economy.Civ == CivKind.Primitive)) return false;
            // V3-5 (32 #8): in the second age, one in three is the civilisation's own unit when it can be paid.
            UnitKind kind;
            // V3-6: steel is the automatic economy's explicit signal to replace an infantry with a heavy infantry.
            // It is checked before the civilisation's one-in-three special unit so the stock is never silently ignored.
            if (ProcessingAvailable(faction) && economy.Steel >= rules.HeavyInfantrySteelCost
                && HasRoomFor(faction, UnitKind.HeavyInfantry) && CanPay(faction, UnitKind.HeavyInfantry)) kind = UnitKind.HeavyInfantry;
            else
            {
                var special = SpecialUnit(faction);
                kind = special != 0 && CanPay(faction, special) && 2 * CountClass(faction, special) < CountClass(faction, UnitKind.Infantry) ? special : UnitKind.Infantry;
            }
            int batch = LargeEconomyOn ? Math.Min(rules.LargeInfantryBatch,
                Math.Min(rules.QueueLimit - building.Queued, PlanOf(faction).InfantryQueue - building.Queued)) : 1;
            for (int i = 0; i < batch; i++)
            {
                if (!HasRoomFor(faction, kind) || !CanPay(faction, kind)) break;
                Enqueue(faction, ref building, kind);
            }
            return true;
        }

        /// <summary>Living line soldiers trained (or queued) as <paramref name="kind"/>; infantry counts those of no class.</summary>
        private int CountClass(uint faction, UnitKind kind)
        {
            int count = QueuedOf(faction, kind);
            foreach (int i in world.SoldierTraversal)
            {
                var s = world.Soldiers[i];
                if (!s.Alive || s.Initial.FactionId != faction || s.Initial.Kind != UnitKind.Infantry) continue;
                if (kind == UnitKind.Infantry ? s.Class == 0 : s.Class == kind) count++;
            }
            return count;
        }

        private void PlaceBarracks(uint faction)
        {
            int origin = FindBarracksSite(faction);
            if (origin < 0) return; // no room near the core: try again next cycle
            PlaceBuildingAt(faction, BuildingKind.Barracks, origin, Facing.North, 0);
        }

        private void DecideTown(uint faction)
        {
            if (!TownsAllowed(faction)) return;
            var rules = world.Config.Economy;
            if (LargeEconomyOn && world.Tick % rules.LargeTownDecisionIntervalTicks != 0) return;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood - (BuildingReservationsOn ? ReservedConstructionWood(faction) : 0) < rules.TownWoodCost
                || economy.Stone - (BuildingReservationsOn ? ReservedConstructionStone(faction) : 0) < rules.TownStoneCost
                || TownCount(faction) >= TownLimitFor(faction)) return;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && (!world.Buildings[i].Reserved || BuildingReservationsOn)
                    && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Town && !world.Buildings[i].Complete)
                    return;
            int resourceCell = BestTownResourceCell(faction);
            if (resourceCell < 0) return;
            int origin = FindTownSite(faction, resourceCell);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Town, origin, Facing.North, 0);
        }

        private void DecideTownVillagers(uint faction)
        {
            if (!TownsAllowed(faction)) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            // Villagers only (not soldiers) count toward the villager target; CanTrainTownVillager checks the population cap.
            int villagers = LivingVillagers(faction) + economy.Queued + QueuedTownVillagers(faction);
            int target = PlanOf(faction).VillagerTarget + (LargeEconomyOn ? TownCount(faction) * rules.LargeVillagerBatch : 0);
            if (villagers >= target) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var town = ref world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.Held || town.FactionId != faction || town.Kind != BuildingKind.Town) continue;
                if (villagers >= target || !CanTrainTownVillager(faction, town)) return;
                int batch = LargeEconomyOn ? Math.Min(rules.LargeVillagerBatch, target - villagers) : 1;
                for (int q = 0; q < batch && villagers < target; q++)
                {
                    if (!CanTrainTownVillager(faction, town)) break;
                    EnqueueTownVillager(faction, ref town);
                    villagers++;
                }
            }
        }

        private int TownCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && (!world.Buildings[i].Reserved || BuildingReservationsOn)
                    && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Town) count++;
            return count;
        }

        private int BestTownResourceCell(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            var rules = world.Config.Economy;
            int best = -1, bestCount = 0, bestAmount = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var candidate = world.Nodes[i];
                if (!ResourceKnownToFaction(faction, i) || candidate.Remaining <= 0
                    || InRange(candidate.Definition.Position, core, Fix64.FromInt(rules.TownCoreDistance))) continue;
                int count = 0, amount = 0;
                for (int j = 0; j < world.Nodes.Length; j++)
                {
                    var node = world.Nodes[j];
                    if (!ResourceKnownToFaction(faction, j) || node.Remaining <= 0
                        || !InRange(candidate.Definition.Position, node.Definition.Position, Fix64.FromInt(rules.TownResourceReach))) continue;
                    count++; amount = checked(amount + node.Remaining);
                }
                if (count > bestCount || count == bestCount && (amount > bestAmount || amount == bestAmount && candidate.Definition.Id < world.Nodes[best].Definition.Id))
                { best = i; bestCount = count; bestAmount = amount; }
            }
            return best < 0 ? -1 : world.Map.Cell(world.Nodes[best].Definition.Position);
        }

        private void AssignTownWorkers(BuildingState town)
        {
            if (town.Held || !TownsOn || HumanRegionAt(town.FactionId, BuildingCenter(town))) return;
            int workerLimit = LargeEconomyOn ? world.Config.Economy.LargeTownAutoWorkers : 4;
            for (int assigned = 0; assigned < workerLimit; assigned++)
            {
                int villager = -1, node = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != town.FactionId || v.Held || !CanAiAssign(v) || v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building) continue;
                    int nearest = NearestTownNode(town, v.Position);
                    if (nearest < 0) continue;
                    if (villager < 0 || DistanceSquared(v.Position, world.Nodes[nearest].Definition.Position) < DistanceSquared(world.Villagers[villager].Position, world.Nodes[node].Definition.Position)
                        || DistanceSquared(v.Position, world.Nodes[nearest].Definition.Position) == DistanceSquared(world.Villagers[villager].Position, world.Nodes[node].Definition.Position)
                            && v.Id < world.Villagers[villager].Id)
                    { villager = i; node = nearest; }
                }
                if (villager < 0) return;
                SetWorkNode(ref world.Villagers[villager], node);
                world.Villagers[villager].HaulFrom = 0; world.Villagers[villager].HaulTo = 0;
            }
        }

        private int NearestTownNode(BuildingState town, SimPoint from)
        {
            int best = -1;
            BigInteger bestDistance = default;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (!ResourceKnownToFaction(town.FactionId, i) || node.Remaining <= 0
                    || !InRange(BuildingCenter(town), node.Definition.Position, Fix64.FromInt(world.Config.Economy.TownResourceReach))) continue;
                var distance = DistanceSquared(from, node.Definition.Position);
                if (best < 0 || distance < bestDistance || distance == bestDistance && node.Definition.Id < world.Nodes[best].Definition.Id)
                { best = i; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>V3-18 #1: the sanctuary's first automatic investment is one shrine for an owned outpost.</summary>
        private void DecideSanctuaryShrine(uint faction)
        {
            if (!SanctuaryAllowed(faction)) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood < rules.ShrineWoodCost || economy.Stone < rules.ShrineStoneCost) return;
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                var outpost = world.Outposts[i];
                if (outpost.OwnerFactionId != faction || SanctuaryOutpostHasShrine(faction, outpost.Definition.Id)) continue;
                int origin = FindSanctuarySite(faction, outpost.Definition.Id);
                if (origin < 0) continue;
                PlaceBuildingAt(faction, BuildingKind.Shrine, origin, Facing.North, 0);
                world.Buildings[world.BuildingCount - 1].SanctuaryOutpostId = outpost.Definition.Id;
                return;
            }
        }

        private bool SanctuaryOutpostHasShrine(uint faction, uint outpostId)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (IsActualBuilding(building) && building.FactionId == faction && building.Kind == BuildingKind.Shrine
                    && building.SanctuaryOutpostId == outpostId) return true;
            }
            return false;
        }

        /// <summary>Validates a manual shrine placement and fixes its outpost link by ascending outpost ID.</summary>
        private bool TrySanctuaryPlacement(uint faction, int origin, out uint outpostId)
        {
            outpostId = 0;
            if (!SanctuaryAllowed(faction)) return false;
            var centre = FootprintCenter(origin, world.Config.Economy.ShrineSizeCells);
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                var outpost = world.Outposts[i];
                if (outpost.OwnerFactionId != faction || SanctuaryOutpostHasShrine(faction, outpost.Definition.Id)) continue;
                if (!InRange(centre, outpost.Definition.Position, Fix64.FromInt(world.Config.Economy.ShrineOutpostReach))) continue;
                outpostId = outpost.Definition.Id;
                return true;
            }
            return false;
        }

        private int FindSanctuarySite(uint faction, uint outpostId)
        {
            if (outpostId == 0 || outpostId > world.Outposts.Length || SanctuaryOutpostHasShrine(faction, outpostId)) return -1;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.ShrineSizeCells;
            int postCell = world.Map.Cell(world.Outposts[outpostId - 1].Definition.Position);
            int cx = postCell % width, cz = postCell / width;
            int coreCell = world.Map.Cell(OwnCore(faction).Definition.Position);
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, coreCell, size) || !KeepsMapConnected(faction, origin, size)) continue;
                        if (InRange(FootprintCenter(origin, size), world.Outposts[outpostId - 1].Definition.Position,
                            Fix64.FromInt(world.Config.Economy.ShrineOutpostReach))) return origin;
                    }
            return -1;
        }

        /// <summary>V3-13 #1: a cult keeps one monastery before its automatic monk queue is opened.</summary>
        private bool DecideCultMonastery(uint faction)
        {
            int monastery = OwnBuildingIndex(faction, BuildingKind.Monastery);
            if (monastery >= 0) return world.Buildings[monastery].Complete && !world.Buildings[monastery].Held;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood < rules.MonasteryWoodCost) return false;
            int origin = FindSite(faction, rules.MonasterySizeCells);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Monastery, origin, Facing.North, 0);
            return false;
        }

        private bool DecideCultMonk(uint faction)
        {
            int monastery = OwnBuildingIndex(faction, BuildingKind.Monastery);
            if (monastery < 0) return false;
            ref var building = ref world.Buildings[monastery];
            if (!building.Complete || building.Held || building.Queued >= world.Config.Economy.QueueLimit) return false;
            if (QueuedOf(faction, UnitKind.Monk) + LivingClass(faction, UnitKind.Monk) >= CultMonkLimit(faction)) return false;
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + world.Economies[faction - 1].Queued + QueuedInfantry(faction) + QueuedTownVillagers(faction);
            if (population >= PopCapFor(faction) || !HasRoomFor(faction, UnitKind.Monk) || !CanPay(faction, UnitKind.Monk, BuildingKind.Monastery)
                || !CultBudgetAllows(faction, world.Config.Economy.MonasteryMonkFoodCost, world.Config.Economy.MonasteryMonkWoodCost)) return false;
            Enqueue(faction, ref building, UnitKind.Monk);
            return true;
        }

        /// <summary>V3-13 #2: one autonomous monk per four living non-scout escorts, up to four monks.</summary>
        private int CultMonkLimit(uint faction)
        {
            int escorts = 0;
            foreach (int i in world.SoldierTraversal)
            {
                var soldier = world.Soldiers[i];
                if (!soldier.Alive || soldier.Initial.FactionId != faction || soldier.Initial.Kind == UnitKind.Monk
                    || soldier.Initial.Kind == UnitKind.Scout) continue;
                escorts++;
            }
            return Math.Min(4, escorts / 4);
        }

        /// <summary>Pays, closes the footprint, and sends the builders. The caller has checked the site and the wood.</summary>
        private void PlaceBuildingAt(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId)
            => PlaceBuildingAt(faction, kind, origin, facing, nodeId, null, -1, -1);

        private void PlaceBuildingAt(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId, int[] bridgeCells, int requestedWorkCell = -1, int existingIndex = -1)
        {
            if (automaticEconomyAction && RegionsOn && !RegionAllowsAiBuilding(faction, origin, SizeOf(kind))) return;
            // LargeEconomy funds its next remote town before optional stone structures. Human placement and ordinary
            // maps keep their existing construction order; the town itself is allowed through this guard.
            if (automaticEconomyAction && LargeEconomyOn && kind != BuildingKind.Town && StoneOf(kind, faction) > 0
                && LargeTownNeedsFunding(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            bool plankBridge = kind == BuildingKind.Bridge && bridgeCells != null && PlankBridgeAvailable(faction, bridgeCells.Length);
            bool framesUsed = CraftTree1On && kind != BuildingKind.Wall && !plankBridge && economy.Frames >= world.Config.Economy.CraftTreeFramesPerBuilding;
            int woodCost = plankBridge ? checked(WoodOf(kind, faction) * world.Config.Economy.PlankBridgeWoodPermille / 1000) : WoodOfForPlacement(kind, faction, framesUsed);
            int stoneCost = StoneOf(kind, faction);
            bool siegeCastle = kind == BuildingKind.Castle && SiegePartsCastleAvailable(faction);
            bool brickCastle = kind == BuildingKind.Castle && !siegeCastle && BrickCastleDiscountAvailable(faction);
            bool brickDefence = BrickDefenceAvailable(faction, kind);
            int frameCost = framesUsed ? world.Config.Economy.CraftTreeFramesPerBuilding : 0;
            int plankCost = plankBridge ? checked(world.Config.Economy.PlanksPerBridgeCell * bridgeCells.Length) : 0;
            int siegeCost = siegeCastle ? world.Config.Economy.SiegePartsPerCastle : 0;
            int brickCost = brickCastle ? world.Config.Economy.BricksPerCastle
                : brickDefence ? (kind == BuildingKind.Wall ? world.Config.Economy.BricksPerWall : world.Config.Economy.BricksPerTower) : 0;
            if (automaticEconomyAction && !CanPayAutomaticConstruction(faction, woodCost, stoneCost)) return;
            if (NoNegativeStockOn && (economy.Wood < woodCost || economy.Stone < stoneCost
                || economy.Frames < frameCost || economy.Planks < plankCost || economy.Bricks < brickCost || economy.SiegeParts < siegeCost)) return;
            // A smelter is allowed through so an age-three infantry metal reserve can be replenished.
            if (automaticEconomyAction && kind != BuildingKind.Smelter
                && ArmyRecoveryWouldConsumeReserve(faction, ResourceKind.Wood, woodCost)) return;
            economy.Wood = checked(economy.Wood - woodCost);
            if (framesUsed)
            {
                economy.Frames = checked(economy.Frames - world.Config.Economy.CraftTreeFramesPerBuilding);
                economy.FramesConsumed = checked(economy.FramesConsumed + world.Config.Economy.CraftTreeFramesPerBuilding);
                economy.FramesConsumedBuildings = checked(economy.FramesConsumedBuildings + world.Config.Economy.CraftTreeFramesPerBuilding);
            }
            if (plankBridge)
            {
                int planks = plankCost;
                economy.Planks = checked(economy.Planks - planks);
                economy.PlanksConsumed = checked(economy.PlanksConsumed + planks);
                economy.PlanksConsumedBridges = checked(economy.PlanksConsumedBridges + planks);
            }
            economy.Stone = checked(economy.Stone - stoneCost);
            if (siegeCastle)
            {
                economy.SiegeParts = checked(economy.SiegeParts - world.Config.Economy.SiegePartsPerCastle);
                economy.SiegePartsConsumed = checked(economy.SiegePartsConsumed + world.Config.Economy.SiegePartsPerCastle);
            }
            if (brickCastle)
            {
                economy.Bricks = checked(economy.Bricks - world.Config.Economy.BricksPerCastle);
                economy.BricksConsumed = checked(economy.BricksConsumed + world.Config.Economy.BricksPerCastle);
                economy.BricksConsumedCastles = checked(economy.BricksConsumedCastles + world.Config.Economy.BricksPerCastle);
            }
            if (brickDefence)
            {
                int cost = brickCost;
                economy.Bricks = checked(economy.Bricks - cost);
                economy.BricksConsumed = checked(economy.BricksConsumed + cost);
                if (kind == BuildingKind.Wall) economy.BricksConsumedWalls = checked(economy.BricksConsumedWalls + cost);
                else economy.BricksConsumedTowers = checked(economy.BricksConsumedTowers + cost);
            }
            int index = existingIndex >= 0 ? existingIndex : world.BuildingCount;
            if (existingIndex < 0 && index == world.Buildings.Length) Array.Resize(ref world.Buildings, index == 0 ? 4 : checked(index * 2));
            int[] footprint = null;
            MeasureSubphase("AI.DecideEconomy.PlaceBuilding.Footprint", () =>
                footprint = kind == BuildingKind.Bridge ? bridgeCells
                    : kind == BuildingKind.Tollgate ? TollgateFootprint(origin, facing) : Footprint(origin, SizeOf(kind)));
            if (footprint == null || footprint.Length == 0) return;
            MeasureSubphase("AI.DecideEconomy.PlaceBuilding.CloseTerrain", () =>
            {
                foreach (int cell in footprint) world.Map.SetPassable(cell, false);
            });
            int workCell = -1;
            MeasureSubphase("AI.DecideEconomy.PlaceBuilding.WorkCell", () =>
                workCell = requestedWorkCell >= 0 ? requestedWorkCell
                    : kind == BuildingKind.Town ? NearestReachablePassableCell(FootprintCenter(origin, SizeOf(kind)), faction)
                    : NearestPassableCell(kind == BuildingKind.Bridge ? BridgeCenter(bridgeCells) : FootprintCenter(origin, SizeOf(kind))));
            uint buildingId = existingIndex >= 0 ? world.Buildings[index].Id : world.NextBuildingId;
            world.Buildings[index] = new BuildingState { Id = buildingId, FactionId = faction, Kind = kind, OriginCell = origin,
                BridgeCells = kind == BuildingKind.Bridge ? (int[])bridgeCells.Clone() : null,
                WorkCell = workCell,
                Alive = true, Hp = brickDefence ? BrickHpOf(kind, faction, origin) : HpOf(kind, faction, origin),
                BrickUpgraded = brickDefence, Level = 1, FramesUsed = framesUsed, Facing = facing, NodeId = nodeId,
                ConstructionWood = woodCost, ConstructionStone = stoneCost,
                ConstructionPlanks = plankCost, ConstructionBricks = brickCost,
                ConstructionSiegeParts = siegeCost, ConstructionFrames = frameCost };
            if (existingIndex < 0) world.NextBuildingId = checked(world.NextBuildingId + 1);
            if (nodeId != 0) ReleaseNode(nodeId);
            if (kind == BuildingKind.Farm) world.Buildings[index].Interval = FarmInterval(faction, origin);
            else if (kind == BuildingKind.LumberCamp) world.Buildings[index].Interval = world.Config.Economy.LumberCampIntervalTicks;
            else if (kind == BuildingKind.Quarry) world.Buildings[index].Interval = world.Config.Economy.QuarryIntervalTicks;
            MeasureSubphase("AI.DecideEconomy.PlaceBuilding.Evacuate", () => EvacuateFootprint(footprint));
            MeasureSubphase("AI.DecideEconomy.PlaceBuilding.TerrainChanged", TerrainChanged);
            MeasureSubphase("AI.DecideEconomy.PlaceBuilding.AssignBuilders", () => AssignBuilders(ref world.Buildings[index]));
        }

        private void RefundBuilding(uint faction, ref BuildingState building)
        {
            int maxHp = Math.Max(1, MaxHpOf(building));
            bool complete = building.Complete;
            int ratioHp = complete ? Math.Max(0, Math.Min(building.Hp, maxHp)) : maxHp;
            int Refund(int paid) => complete ? checked(paid * ratioHp / maxHp) : paid;
            AddStock(faction, ResourceKind.Wood, Refund(building.ConstructionWood));
            AddStock(faction, ResourceKind.Stone, Refund(building.ConstructionStone));
            AddStock(faction, ResourceKind.Planks, Refund(building.ConstructionPlanks));
            AddStock(faction, ResourceKind.Bricks, Refund(building.ConstructionBricks));
            AddStock(faction, ResourceKind.SiegeParts, Refund(building.ConstructionSiegeParts));
            AddStock(faction, ResourceKind.Frames, Refund(building.ConstructionFrames));
            RefundBuildingContents(ref building);
        }

        private void RefundBuildingContents(ref BuildingState building)
        {
            if (building.Kind == BuildingKind.Storage)
            {
                if (building.StorageKind != 0 && building.StorageCount > 0) AddStock(building.FactionId, building.StorageKind, building.StorageCount);
                return;
            }
            if (building.Kind == BuildingKind.Assembler)
            {
                RefundAssemblerInputs(building.FactionId, ref building);
                return;
            }
            ResourceKind first = building.Kind == BuildingKind.Smelter ? ResourceKind.Ore
                : building.Kind == BuildingKind.CharcoalKiln ? ResourceKind.Wood
                : building.Kind == BuildingKind.Steelworks ? ResourceKind.Metal
                : building.Kind == BuildingKind.Fletcher ? ResourceKind.Wood
                : building.Kind == BuildingKind.Mill ? ResourceKind.Food
                : building.Kind == BuildingKind.Saddlery ? ResourceKind.Food
                : building.Kind == BuildingKind.Sawmill ? ResourceKind.Wood
                : building.Kind == BuildingKind.Stonecutter ? ResourceKind.Stone
                : building.Kind == BuildingKind.Joinery ? ResourceKind.Planks
                : building.Kind == BuildingKind.IncenseHouse ? ResourceKind.Wood
                : building.Kind == BuildingKind.CharmWorkshop ? ResourceKind.Wood
                : building.Kind == BuildingKind.RepairYard ? ResourceKind.Wood
                : building.Kind == BuildingKind.ToolShop ? ResourceKind.Wood : 0;
            if (first != 0) AddStock(building.FactionId, first, building.Input);
            if (building.InputSecondary > 0)
            {
                ResourceKind second = building.InputSecondaryKind != 0 ? building.InputSecondaryKind
                    : building.Kind == BuildingKind.Steelworks ? ResourceKind.Charcoal
                    : building.Kind == BuildingKind.Fletcher ? ResourceKind.Food
                    : building.Kind == BuildingKind.Saddlery ? ResourceKind.Wood
                    : building.Kind == BuildingKind.IncenseHouse ? ResourceKind.Food : 0;
                if (second != 0) AddStock(building.FactionId, second, building.InputSecondary);
            }
            if (building.InputTertiary > 0 && building.Kind == BuildingKind.IncenseHouse) AddStock(building.FactionId, ResourceKind.Food, building.InputTertiary);
            if (building.Output > 0)
            {
                ResourceKind outputKind = building.Kind == BuildingKind.Drill && building.NodeId != 0 && building.NodeId <= world.Nodes.Length
                    ? world.Nodes[building.NodeId - 1].Definition.Kind : OutputKind(building.Kind);
                if (outputKind != 0) AddStock(building.FactionId, outputKind, building.Output);
            }
        }

        private bool TryMoveBuilding(uint faction, uint buildingId, int newOrigin, Facing facing)
        {
            if (faction == 0 || faction > world.Factions.Length || (byte)facing > 3 || !OwnBuilding(faction, buildingId, out int index)) return false;
            var building = world.Buildings[index];
            if (!building.Alive || !building.Complete || building.Kind == BuildingKind.Farm
                || building.Kind == BuildingKind.Bridge || building.Kind == BuildingKind.Tollgate || building.Kind == BuildingKind.Town
                || building.Kind == BuildingKind.Castle || building.Kind == BuildingKind.Shrine || building.Kind == BuildingKind.Caravanserai
                || building.Kind == BuildingKind.MineShaft || building.Kind == BuildingKind.GrandHouse || building.Kind == BuildingKind.EngineerCamp
                || building.Kind == BuildingKind.Academy || building.Kind == BuildingKind.Monastery || building.Kind == BuildingKind.Harbor) return false;
            int size = SizeOf(building.Kind);
            var oldFootprint = Footprint(building);
            var candidate = Footprint(newOrigin, size);
            if (candidate.Length == 0 || !MoveSiteIsClear(faction, index, oldFootprint, candidate)) return false;
            if (HasIndustryOutput(building.Kind) && !MovePortsAreClear(index, newOrigin, candidate, oldFootprint, facing)) return false;

            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            var saved = new bool[cells];
            var touched = new bool[cells];
            foreach (int cell in oldFootprint) if (cell >= 0 && cell < cells) { touched[cell] = true; saved[cell] = world.Map.IsPassable(cell); }
            foreach (int cell in candidate) if (cell >= 0 && cell < cells && !touched[cell]) { touched[cell] = true; saved[cell] = world.Map.IsPassable(cell); }
            foreach (int cell in oldFootprint) world.Map.SetPassable(cell, true);
            foreach (int cell in candidate) world.Map.SetPassable(cell, false);
            bool connected = KeepsMapConnected(faction, newOrigin, size);
            for (int cell = 0; cell < cells; cell++) if (touched[cell]) world.Map.SetPassable(cell, saved[cell]);
            if (!connected) return false;

            MarkLinesForBuilding(faction, building.Id);
            foreach (int cell in oldFootprint) world.Map.SetPassable(cell, true);
            foreach (int cell in candidate) world.Map.SetPassable(cell, false);
            building.OriginCell = newOrigin;
            building.Facing = facing;
            building.WorkCell = NearestPassableCell(FootprintCenter(newOrigin, size));
            long pauseTicks = world.Config.Economy.MoveBuildingPauseTicks;
            building.MovePauseUntilTick = pauseTicks == 0 ? 0 : checked(world.Tick + pauseTicks + 1);
            world.Buildings[index] = building;
            TerrainChanged();
            return true;
        }

        private bool MoveSiteIsClear(uint faction, int sourceIndex, int[] oldFootprint, int[] candidate)
        {
            int coreCell = world.Map.Cell(OwnCore(faction).Definition.Position);
            var source = world.Buildings[sourceIndex];
            int mapCells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            foreach (int cell in candidate) if (cell < 0 || cell >= mapCells) return false;
            int sourceNodeCell = source.NodeId == 0 || source.NodeId > world.Nodes.Length ? -1
                : world.Map.Cell(world.Nodes[source.NodeId - 1].Definition.Position);
            if ((source.Kind == BuildingKind.Mine || source.Kind == BuildingKind.Drill) && Array.IndexOf(candidate, sourceNodeCell) < 0) return false;
            foreach (int cell in candidate)
            {
                bool wasSource = Array.IndexOf(oldFootprint, cell) >= 0;
                if ((!wasSource && !world.Map.IsPassable(cell)) || IsRiverCell(cell) || Chebyshev(cell, coreCell) < CoreClearanceCells) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
                foreach (var node in world.Nodes)
                {
                    int nodeCell = world.Map.Cell(node.Definition.Position);
                    if (nodeCell == sourceNodeCell && (source.Kind == BuildingKind.Mine || source.Kind == BuildingKind.Drill)) continue;
                    if (Chebyshev(cell, nodeCell) < NodeClearanceCells) return false;
                }
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    if (i == sourceIndex || !world.Buildings[i].Alive) continue;
                    foreach (int other in Footprint(world.Buildings[i])) if (Chebyshev(cell, other) < BuildingClearanceCells) return false;
                }
            }
            return true;
        }

        private bool MovePortsAreClear(int sourceIndex, int newOrigin, int[] candidate, int[] oldFootprint, Facing facing)
        {
            var moved = world.Buildings[sourceIndex];
            int newPort = OutputCell(newOrigin, SizeOf(moved.Kind), facing);
            if (newPort < 0 || Array.IndexOf(oldFootprint, newPort) < 0 && !world.Map.IsPassable(newPort)
                || IsRiverCell(newPort) || world.Belts.Length != 0 && world.Belts[newPort].FactionId != 0
                || IsNodeCell(newPort) || InsideAnyCore(newPort)) return false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                if (i == sourceIndex || !world.Buildings[i].Alive || !HasIndustryOutput(world.Buildings[i].Kind)) continue;
                if (Array.IndexOf(candidate, OutputCell(world.Buildings[i])) >= 0) return false;
                if (Array.IndexOf(oldFootprint, newPort) < 0 && Array.IndexOf(Footprint(world.Buildings[i]), newPort) >= 0) return false;
            }
            return true;
        }

        private bool TryPlaceBridge(uint faction, EconomyCommand command)
        {
            if (!TryValidateBridge(faction, command.Cells, command.Facing, out int[] cells, out int workCell)) return false;
            if (world.Economies[faction - 1].Wood < BridgeWoodCostFor(faction, cells.Length)) return false;
            PlaceBuildingAt(faction, BuildingKind.Bridge, cells[0], command.Facing, 0, cells, workCell);
            world.Buildings[world.BuildingCount - 1].Held = true;
            return true;
        }

        /// <summary>V3-11 #2: the hand-placement rules are also the rules used by the automatic candidate search.</summary>
        private bool TryValidateBridge(uint faction, IReadOnlyList<int> requested, Facing facing, out int[] cells, out int workCell,
            bool requireCamp = true, bool requireCiv = true)
        {
            cells = null;
            workCell = -1;
            bool campReady = false;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].Complete && world.Buildings[i].FactionId == faction
                    && world.Buildings[i].Kind == BuildingKind.EngineerCamp) { campReady = true; break; }
            if ((requireCiv ? !BridgeAllowed(faction) : !BridgeOn) || requireCamp && !campReady
                || requireCamp && ActiveBridgeCount(faction) >= MaxActiveBridges || (byte)facing > 3 || requested == null
                || requested.Count == 0 || requested.Count > world.Config.Economy.MaxBridgeLength) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            cells = new int[requested.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = requested[i];
            int first = cells[0], second = cells.Length == 1 ? first + 1 : cells[1], delta = second - first;
            bool horizontal = delta == 1 || delta == -1;
            bool vertical = delta == width || delta == -width;
            if (!horizontal && !vertical) { cells = null; return false; }
            var seen = new HashSet<int>();
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i];
                if (cell < 0 || cell >= width * height || !seen.Add(cell) || !IsRiverCell(cell) || world.Map.IsPassable(cell)) { cells = null; return false; }
                if (i > 0 && cells[i] != cells[i - 1] + delta) { cells = null; return false; }
            }
            int before = first - delta, after = cells[cells.Length - 1] + delta;
            if (before < 0 || before >= width * height || after < 0 || after >= width * height
                || horizontal && (before / width != first / width || after / width != first / width)
                || vertical && (before % width != first % width || after % width != first % width)
                || !world.Map.IsPassable(before) || !world.Map.IsPassable(after)
                || IsRiverCell(before) || IsRiverCell(after)) { cells = null; return false; }
            foreach (int cell in cells)
            {
                foreach (var node in world.Nodes)
                    if (world.Map.Cell(node.Definition.Position) == cell) { cells = null; return false; }
                for (int i = 0; i < world.BuildingCount; i++)
                        if (world.Buildings[i].Alive && Array.IndexOf(Footprint(world.Buildings[i]), cell) >= 0) { cells = null; return false; }
            }
            for (int i = 0; i < world.VillagerCount && workCell < 0; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction) continue;
                int start = world.Map.Cell(villager.Position);
                if (world.Map.FindPath(start, world.Map.Center(before), faction).Length > 0) workCell = before;
                else if (world.Map.FindPath(start, world.Map.Center(after), faction).Length > 0) workCell = after;
            }
            if (workCell < 0) { cells = null; return false; }
            return true;
        }

        private int ActiveBridgeCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Bridge) count++;
            return count;
        }

        /// <summary>
        /// Rings around the core cell, nearest first; within a ring by z then x. The first footprint that is on free
        /// ground, keeps its distance from the core, resources and buildings, and does not cut the map apart wins.
        /// </summary>
        private int FindBarracksSite(uint faction) => FindSite(faction, world.Config.Economy.BarracksSizeCells);

        /// <summary>The same ring search for any square footprint of <paramref name="size"/> cells.</summary>
        private int FindSite(uint faction, int size)
        {
            int site = FindSiteNear(faction, size, world.Map.Cell(OwnCore(faction).Definition.Position));
            // S-3: when the core has no room left, a completed town is the next base for building (in ID order).
            if (site >= 0 || !TownsAllowed(faction)) return site;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != faction || town.Kind != BuildingKind.Town) continue;
                site = FindSiteNear(faction, size, world.Map.Cell(BuildingCenter(town)));
                if (site >= 0) return site;
            }
            return -1;
        }

        /// <summary>The ring search around any <paramref name="centre"/> cell (the clearance from the own core still applies).</summary>
        private int FindSiteNear(uint faction, int size, int centre, int searchRadius = SiteSearchRadiusCells)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position), cx = centre % width, cz = centre / width;
            for (int r = 0; r <= searchRadius; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        placementSiteCandidatesInspected++;
                        placementSiteClearChecks++;
                        if (SiteIsClear(origin, core, size) && KeepsMapConnected(faction, origin, size)) return origin;
                    }
            return -1;
        }

        private bool SiteIsClear(int origin, int coreCell, int size, int buildingClearanceCells = BuildingClearanceCells)
        {
            foreach (int cell in Footprint(origin, size))
            {
                if (!world.Map.IsPassable(cell) || IsRiverCell(cell) || Chebyshev(cell, coreCell) < CoreClearanceCells) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false; // V3-2: never on a belt
            }
            return ClearOfNodesAndBuildings(origin, size, buildingClearanceCells);
        }

        /// <summary>
        /// The node and building clearance of a square footprint, measured once per node and per building cell against the
        /// footprint rectangle instead of once per footprint cell (freeze3). The answer is the same as comparing every pair
        /// of cells: the Chebyshev distance from a point to a rectangle is the smallest distance to any of its cells.
        /// Nothing is cached, so reservations, belts and buildings placed earlier in the same tick are always seen.
        /// </summary>
        private bool ClearOfNodesAndBuildings(int origin, int size, int buildingClearanceCells)
        {
            int width = world.Config.Map.WidthCells;
            int x0 = origin % width, z0 = origin / width, x1 = x0 + size - 1, z1 = z0 + size - 1;
            foreach (var node in world.Nodes)
                if (RectangleDistance(x0, z0, x1, z1, world.Map.Cell(node.Definition.Position), width) < NodeClearanceCells) return false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                if (!world.Buildings[i].Alive) continue;
                foreach (int other in Footprint(world.Buildings[i]))
                    if (RectangleDistance(x0, z0, x1, z1, other, width) < buildingClearanceCells) return false;
            }
            return true;
        }

        private static int RectangleDistance(int x0, int z0, int x1, int z1, int cell, int width)
        {
            int x = cell % width, z = cell / width;
            int dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0;
            int dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
            return Math.Max(dx, dz);
        }

        private bool ManualPlacementPortsAreClear(BuildingKind kind, int origin, int size, Facing facing)
        {
            var candidate = Footprint(origin, size);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!building.Alive || !HasIndustryOutput(building.Kind)) continue;
                if (Array.IndexOf(candidate, OutputCell(building)) >= 0) return false;
            }
            if (!HasIndustryOutput(kind)) return true;
            int candidateOutput = OutputCell(origin, size, facing);
            if (candidateOutput < 0 || !world.Map.IsPassable(candidateOutput) || IsRiverCell(candidateOutput)
                || world.Belts.Length != 0 && world.Belts[candidateOutput].FactionId != 0
                || IsNodeCell(candidateOutput) || InsideAnyCore(candidateOutput)) return false;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && Array.IndexOf(Footprint(world.Buildings[i]), candidateOutput) >= 0) return false;
            return true;
        }

        private static bool HasIndustryOutput(BuildingKind kind)
            => kind == BuildingKind.Mine || kind == BuildingKind.Drill || kind == BuildingKind.Smelter || kind == BuildingKind.LumberCamp
                || kind == BuildingKind.Quarry || kind == BuildingKind.CharcoalKiln || kind == BuildingKind.Steelworks
                || kind == BuildingKind.Fletcher || kind == BuildingKind.Mill || kind == BuildingKind.BrickKiln
                || kind == BuildingKind.Saddlery || kind == BuildingKind.Sawmill || kind == BuildingKind.Scriptorium
                || kind == BuildingKind.DryingRack || kind == BuildingKind.Lapidary || kind == BuildingKind.Weaver
                || kind == BuildingKind.IncenseHouse || kind == BuildingKind.CharmWorkshop || kind == BuildingKind.RepairYard
                || kind == BuildingKind.ToolShop;

        private bool TownSiteIsClear(uint faction, int origin, int size)
        {
            var core = OwnCore(faction).Definition.Position;
            var coreDistance = Fix64.FromInt(world.Config.Economy.TownCoreDistance);
            if (DistanceSquared(FootprintCenter(origin, size), core) < new BigInteger(coreDistance.Raw) * coreDistance.Raw) return false;
            foreach (int cell in Footprint(origin, size))
            {
                if (LargeEconomyOn && !world.Factions[faction - 1].ExploredCells[cell]) return false;
                if (!world.Map.IsPassable(cell) || IsRiverCell(cell)) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
            }
            return ClearOfNodesAndBuildings(origin, size, BuildingClearanceCells);
        }

        private int FindTownSite(uint faction, int centre)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.TownSizeCells, cx = centre % width, cz = centre / width;
            int searchRadius = LargeEconomyOn ? Math.Max(width, height) : SiteSearchRadiusCells;
            for (int r = 0; r <= searchRadius; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        placementSiteCandidatesInspected++;
                        placementSiteClearChecks++;
                        if (TownSiteIsClear(faction, origin, size) && KeepsMapConnected(faction, origin, size)) return origin;
                    }
            return -1;
        }

        /// <summary>
        /// With the footprint closed, the own core still reaches the enemy core, both outposts and every resource - except
        /// a point under a footprint (a mine's own ore point, or one under this footprint), which nobody walks to.
        /// </summary>
        private bool KeepsMapConnected(uint faction, int origin, int size)
        {
            int passableVersion = world.Map.PassableVersion;
            if (placementConnectivityCacheVersion != passableVersion)
            {
                placementConnectivityCache.Clear();
                placementConnectivityCacheVersion = passableVersion;
            }
            var key = (faction, origin, size);
            if (placementConnectivityCacheEnabled && placementConnectivityCache.TryGetValue(key, out bool cached))
            {
                placementConnectivityCacheHits++;
                return cached;
            }

            placementConnectivityChecks++;
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells, width = world.Config.Map.WidthCells;
            if (placementConnectivitySeen == null || placementConnectivitySeen.Length != cells)
            {
                placementConnectivitySeen = new int[cells];
                placementConnectivityQueue = new int[cells];
                placementConnectivityClosed = new int[cells];
                placementConnectivityStamp = 0;
            }
            int stamp = ++placementConnectivityStamp;
            if (stamp <= 0)
            {
                Array.Clear(placementConnectivitySeen, 0, placementConnectivitySeen.Length);
                placementConnectivityStamp = stamp = 1;
            }
            int[] footprint = Footprint(origin, size);
            int head = 0, tail = 0, start = world.Map.Cell(OwnCore(faction).Definition.Position);
            for (int i = 0; i < footprint.Length; i++) placementConnectivityClosed[footprint[i]] = stamp;
            if (!world.Map.IsPassable(start) || placementConnectivityClosed[start] == stamp)
            {
                placementConnectivityCache[key] = false;
                return false;
            }
            placementConnectivityQueue[tail++] = start; placementConnectivitySeen[start] = stamp;
            while (head < tail)
            {
                int cell = placementConnectivityQueue[head++], x = cell % width;
                Visit(cell + width); if (x + 1 < width) Visit(cell + 1); Visit(cell - width); if (x > 0) Visit(cell - 1);
            }
            placementConnectivityVisitedCells += tail;
            bool result = Reached(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position);
            if (result)
                foreach (var post in world.Outposts)
                    if (!Reached(post.Definition.Position)) { result = false; break; }
            if (result)
                foreach (var node in world.Nodes)
                {
                    int c = world.Map.Cell(node.Definition.Position);
                    if (c < 0) { result = false; break; }
                    if (placementConnectivityClosed[c] == stamp || !world.Map.IsPassable(c)) continue;
                    if (!Reached(node.Definition.Position)) { result = false; break; }
                }
            if (placementConnectivityCacheEnabled) placementConnectivityCache[key] = result;
            return result;

            void Visit(int next)
            {
                if (next < 0 || next >= cells || placementConnectivitySeen[next] == stamp
                    || placementConnectivityClosed[next] == stamp || !world.Map.IsPassable(next)) return;
                placementConnectivitySeen[next] = stamp; placementConnectivityQueue[tail++] = next;
            }
            bool Reached(SimPoint p) { int c = world.Map.Cell(p); return c >= 0 && placementConnectivitySeen[c] == stamp; }
        }

        private static bool ContainsCell(int[] cells, int value)
        {
            for (int i = 0; i < cells.Length; i++) if (cells[i] == value) return true;
            return false;
        }

        /// <summary>Test-only comparison of placement search with and without the connectivity-result cache.</summary>
        internal int FindSiteForTest(uint faction, int size, bool useConnectivityCache)
        {
            bool previous = placementConnectivityCacheEnabled;
            placementConnectivityCacheEnabled = useConnectivityCache;
            try { return FindSite(faction, size); }
            finally { placementConnectivityCacheEnabled = previous; }
        }

        /// <summary>Test-only probe for cache reuse and PassableVersion invalidation.</summary>
        internal bool ProbePlacementConnectivityForTest(uint faction, int origin, int size, bool useConnectivityCache)
        {
            bool previous = placementConnectivityCacheEnabled;
            placementConnectivityCacheEnabled = useConnectivityCache;
            try { return KeepsMapConnected(faction, origin, size); }
            finally { placementConnectivityCacheEnabled = previous; }
        }

        /// <summary>Anyone standing on a new footprint steps to the nearest free cell (distance, then cell id).</summary>
        private void EvacuateFootprint(int[] footprint)
        {
            var nearestByPosition = new Dictionary<(long X, long Z), int>();
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                if (!s.Alive || Array.IndexOf(footprint, world.Map.Cell(s.Position)) < 0) continue;
                int destination = NearestPassableCellCached(s.Position, nearestByPosition);
                s.Position = s.MoveGoal = world.Map.Center(destination);
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || Array.IndexOf(footprint, world.Map.Cell(v.Position)) < 0) continue;
                int destination = NearestPassableCellCached(v.Position, nearestByPosition);
                v.Position = v.MoveGoal = world.Map.Center(destination);
            }
        }

        private int NearestPassableCellCached(SimPoint from, Dictionary<(long X, long Z), int> cache)
        {
            var key = (from.X.Raw, from.Z.Raw);
            if (cache.TryGetValue(key, out int destination)) return destination;
            destination = NearestPassableCell(from);
            cache.Add(key, destination);
            return destination;
        }

        /// <summary>Every route was planned on the old terrain: armies, soldiers' local routes and villagers plan again.</summary>
        private void TerrainChanged()
        {
            foreach (int a in world.ArmyTraversal) { world.Armies[a].HasPathGoal = false; world.Armies[a].PathCursor = 0; }
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                s.LocalPath = null; s.TacticalRoute = false; s.Joining = false;
            }
            for (int i = 0; i < world.VillagerCount; i++) { world.Villagers[i].Route = Array.Empty<int>(); world.Villagers[i].RouteCursor = 0; }
        }

        /// <summary>
        /// V3-5 (32.4): an own unfinished building nobody is building gets builders. A building placed while every
        /// villager was busy building something else was otherwise left unbuilt for good. Maps with ages only, so the
        /// older maps keep every tick; a building the player placed is theirs to staff.
        /// </summary>
        private void ResumeUnbuilt(uint faction)
        {
            if (!AgesOn) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.Reserved || b.Complete || b.Held || b.FactionId != faction || HumanRegionAt(faction, BuildingCenter(b))) continue;
                bool staffed = false;
                for (int j = 0; j < world.VillagerCount && !staffed; j++)
                {
                    var v = world.Villagers[j];
                    staffed = v.Alive && v.BuildingId == b.Id && (v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building);
                }
                if (!staffed) AssignBuilders(ref world.Buildings[i]);
            }
        }

        /// <summary>The nearest villagers (distance to the work cell, then id) stop what they do and go to build.</summary>
        private void AssignBuilders(ref BuildingState building)
        {
            var spot = world.Map.Center(building.WorkCell);
            for (int n = 0; n < BuildersFor(building.FactionId); n++)
            {
                int best = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != building.FactionId || v.Held || v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building) continue;
                    if (building.Kind == BuildingKind.Town
                        && world.Map.FindPath(world.Map.Cell(v.Position), world.Map.Center(building.WorkCell), v.FactionId).Length == 0) continue;
                    if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)) best = i;
                }
                if (best < 0) return;
                world.Villagers[best].HaulFrom = 0; world.Villagers[best].HaulTo = 0;
                world.Villagers[best].Task = VillagerTask.ToBuild;
                world.Villagers[best].BuildingId = building.Id;
            }
        }

        /// <summary>V3-17 #2: the metropolis sends two additional villagers to every construction site.</summary>
        private int BuildersFor(uint faction)
            => checked(world.Config.Economy.Builders + (MetropolisAllowed(faction) ? 2 : 0));

        /// <summary>Economy step: builders arrive, work adds up, and finished barracks train infantry.</summary>
        private void AdvanceBuildings()
        {
            var rules = world.Config.Economy;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || (v.Task != VillagerTask.ToBuild && v.Task != VillagerTask.Building)) continue;
                var b = world.Buildings[v.BuildingId - 1];
                // V3-5 (32 #15): a finished building that is hurt is still work - the villager repairs it instead.
                if (!IsActualBuilding(b) || (b.Complete && !NeedsRepair(b))) { v.Task = VillagerTask.Idle; continue; }
                if (v.Task == VillagerTask.ToBuild && InRange(v.Position, world.Map.Center(b.WorkCell), GatherReach)) v.Task = VillagerTask.Building;
            }
            RepairBuildings();
            bool opened = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || b.Reserved || b.Complete) continue;
                for (int j = 0; j < world.VillagerCount; j++)
                {
                    var v = world.Villagers[j];
                    if (v.Alive && v.Task == VillagerTask.Building && v.BuildingId == b.Id) b.Progress++;
                }
                if (b.Progress < WorkOf(b)) continue;
                b.Progress = WorkOf(b);
                b.Complete = true;
                if (b.Kind == BuildingKind.Bridge)
                {
                    foreach (int cell in Footprint(b)) world.Map.SetPassable(cell, true);
                    opened = true;
                }
                else if (b.Kind == BuildingKind.Tollgate)
                {
                    foreach (int cell in Footprint(b)) world.Map.SetPassable(cell, true);
                    opened = true;
                }
                else if (b.Kind == BuildingKind.Town) { AddTownRegion(b); AssignTownWorkers(b); }
                for (int j = 0; j < world.VillagerCount; j++)
                    if (world.Villagers[j].BuildingId == b.Id && (world.Villagers[j].Task == VillagerTask.Building || world.Villagers[j].Task == VillagerTask.ToBuild))
                        world.Villagers[j].Task = VillagerTask.Idle;
            }
            if (opened) TerrainChanged();
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || BuildingPaused(b) || b.Queued == 0) continue;
                if (b.TrainRemaining > 0) b.TrainRemaining--;
                if (b.TrainRemaining > 0) continue;
                int productionBatch = LargeEconomyOn ? (b.Kind == BuildingKind.Town ? rules.LargeVillagerBatch : rules.LargeInfantryBatch) : 1;
                if (b.Kind == BuildingKind.Town)
                {
                    for (int produced = 0; produced < productionBatch && b.Queued > 0; produced++)
                    {
                        if (LivingVillagers(b.FactionId) + LivingSoldiers(b.FactionId) >= PopCapFor(b.FactionId)) break;
                        SpawnVillager(b.FactionId, world.Map.Center(b.WorkCell));
                        if (LargeEconomyOn) largeTownVillagersProduced[b.FactionId - 1]++;
                        b.Queued--;
                        if (b.QueueTools != null && b.QueueTools.Length > 0)
                        {
                            var tools = new bool[b.QueueTools.Length - 1];
                            Array.Copy(b.QueueTools, 1, tools, 0, tools.Length); b.QueueTools = tools;
                        }
                        if (b.QueueKinds != null && b.QueueKinds.Length > 0)
                        {
                            var kinds = new UnitKind[b.QueueKinds.Length - 1];
                            Array.Copy(b.QueueKinds, 1, kinds, 0, kinds.Length);
                            b.QueueKinds = kinds;
                        }
                        b.TrainRemaining = b.Queued > 0 ? TownVillagerTrainTicksFor(b.FactionId, QueueToolsAt(b, 0)) : 0;
                    }
                    continue;
                }
                // A full population or full armies hold the finished soldier at the door until there is room.
                for (int produced = 0; produced < productionBatch && b.Queued > 0; produced++)
                {
                if (LivingVillagers(b.FactionId) + LivingSoldiers(b.FactionId) >= PopCapFor(b.FactionId)) break;
                var unit = QueueAt(b, 0);
                bool rationed = RationsOn && QueueRationsAt(b, 0);
                bool planked = PlanksOn && QueuePlanksAt(b, 0);
                bool charmed = CharmsOn && QueueCharmsAt(b, 0);
                bool armed = CraftTree2On && QueueArmsAt(b, 0);
                bool fineArmed = CraftTree2On && QueueFineArmsAt(b, 0);
                bool elite = CraftTree4On && QueueEliteKitAt(b, 0);
                bool siegeParts = CraftTree3On && QueueSiegePartsAt(b, 0);
                // Forged only when its metal was paid (a soldier queued in the primitive age paid none).
                int metal = InfantryMetalFor(b.FactionId);
                bool paid = unit == UnitKind.Infantry && metal > 0 && b.QueuedMetal >= metal;
                var spawnKind = unit == UnitKind.Scout || unit == UnitKind.Monk ? unit : UnitKind.Infantry;
                if (!Spawn(b.FactionId, GoalKind.None, 0, world.Map.Center(b.WorkCell), spawnKind, unit)) continue;
                ApplyClass(world.SoldierCount - 1, unit);
                if (elite || armed || fineArmed)
                {
                    ref var equipped = ref world.Soldiers[world.SoldierCount - 1];
                    int attack = elite ? world.Config.Economy.EliteKitAttack : fineArmed ? world.Config.Economy.FineArmsKitAttack : world.Config.Economy.ArmsKitAttack;
                    int hpPermille = elite ? world.Config.Economy.EliteKitHpPermille : fineArmed ? world.Config.Economy.FineArmsKitHpPermille : world.Config.Economy.ArmsKitHpPermille;
                    equipped.Parameters.Damage = checked(equipped.Parameters.Damage + attack);
                    int hp = checked((equipped.Parameters.Hp * hpPermille + 999) / 1000);
                    equipped.Parameters.Hp = hp; equipped.Hp = hp; equipped.Initial.Hp = hp;
                    if (elite) equipped.EliteKitEnhanced = true;
                }
                if (unit == UnitKind.Cannon) world.Economies[b.FactionId - 1].CannonProduced++;
                if (charmed && AgeStrength(b.FactionId, CivKind.Sanctuary, 3))
                {
                    ref var blessed = ref world.Soldiers[world.SoldierCount - 1];
                    int multiplier = AgeStrength(b.FactionId, CivKind.Sanctuary, 4)
                        ? world.Config.Economy.SanctuaryCharmHpAge4Permille
                        : world.Config.Economy.SanctuaryCharmHpPermille;
                    int hp = checked((blessed.Parameters.Hp * multiplier + 999) / 1000);
                    blessed.Parameters.Hp = hp; blessed.Hp = hp; blessed.Initial.Hp = hp;
                }
                if (siegeParts && unit == UnitKind.Ram)
                {
                    ref var ram = ref world.Soldiers[world.SoldierCount - 1];
                    int hp = checked((ram.Parameters.Hp * world.Config.Economy.SiegePartsHpPermille + 999) / 1000);
                    ram.Parameters.Hp = hp; ram.Hp = hp; ram.Initial.Hp = hp; ram.SiegePartsEnhanced = true;
                    world.Economies[b.FactionId - 1].SiegePartsEnhancedRams++;
                }
                if (planked && unit == UnitKind.Ram)
                {
                    ref var ram = ref world.Soldiers[world.SoldierCount - 1];
                    int hp = checked(ram.Parameters.Hp * world.Config.Economy.PlankRamHpPermille / 1000);
                    ram.Parameters.Hp = hp; ram.Hp = hp; ram.Initial.Hp = hp;
                    world.Economies[b.FactionId - 1].PlanksEnhancedRams++;
                }
                if (paid) ForgeIfMetallurgy(b.FactionId, world.SoldierCount - 1);
                ApplySoldierTechs(b.FactionId, world.SoldierCount - 1);
                if (charmed)
                {
                    ref var blessed = ref world.Soldiers[world.SoldierCount - 1];
                    int hp = checked(blessed.Parameters.Hp * world.Config.Economy.CharmHpPermille / 1000);
                    blessed.Parameters.Hp = hp; blessed.Hp = hp; blessed.Initial.Hp = hp;
                    world.Economies[b.FactionId - 1].CharmsTrainedSoldiers++;
                }
                if (rationed) world.Economies[b.FactionId - 1].RationsTrainedSoldiers++;
                if (unit == UnitKind.HeavyCavalry) world.Economies[b.FactionId - 1].TackTrainedHeavyCavalry++;
                Dequeue(b.FactionId, ref b, unit);
                }
            }
        }

        /// <summary>Every unit queued in the faction's buildings (for the population).</summary>
        private int QueuedInfantry(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++) if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind != BuildingKind.Town) count += world.Buildings[i].Queued;
            return count;
        }

        private int QueuedTownVillagers(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Town) count += world.Buildings[i].Queued;
            return count;
        }

        /// <summary>The same rule the reinforcement assignment uses: any non-scout army with a free slot.</summary>
        private bool HasInfantryRoom(uint faction)
        {
            if (HasRoomFor(faction, UnitKind.Infantry)) return true;
            // ArmyGrowth creates the next field army when the queued soldier is spawned. Allow the scaled economy
            // to reach that spawn point instead of treating a full existing army as a permanent queue prohibition.
            return EconomyScaleOn && world.Config.Economy.ArmyGrowth
                && world.Factions[faction - 1].ArmyIds.Length < 12;
        }

        private int SizeOf(BuildingKind kind)
        {
            var e = world.Config.Economy;
            return kind == BuildingKind.Mine ? e.MineSizeCells : kind == BuildingKind.Drill ? e.DrillSizeCells : kind == BuildingKind.LumberCamp ? e.LumberCampSizeCells : kind == BuildingKind.Quarry ? e.QuarrySizeCells : kind == BuildingKind.Smelter ? e.SmelterSizeCells : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnSizeCells
                : kind == BuildingKind.Steelworks ? e.SteelworksSizeCells : kind == BuildingKind.Farm ? e.FarmSizeCells
                : kind == BuildingKind.Fletcher ? e.FletcherSizeCells : kind == BuildingKind.Mill ? e.MillSizeCells : kind == BuildingKind.BrickKiln ? e.BrickKilnSizeCells : kind == BuildingKind.Saddlery ? e.SaddlerySizeCells : kind == BuildingKind.Sawmill ? e.SawmillSizeCells : kind == BuildingKind.Scriptorium ? e.ScriptoriumSizeCells : kind == BuildingKind.DryingRack ? e.DryingRackSizeCells : kind == BuildingKind.Lapidary ? e.LapidarySizeCells
                : kind == BuildingKind.House ? e.HouseSizeCells : kind == BuildingKind.DropSite ? e.DropSiteSizeCells
                : kind == BuildingKind.Wall ? 1 : kind == BuildingKind.Tower ? e.TowerSizeCells : kind == BuildingKind.Blacksmith ? e.BlacksmithSizeCells
                : kind == BuildingKind.Market ? e.MarketSizeCells : kind == BuildingKind.SiegeWorkshop ? e.WorkshopSizeCells
                : kind == BuildingKind.ArcheryRange ? e.RangeSizeCells : kind == BuildingKind.Stable ? e.StableSizeCells
                : kind == BuildingKind.Castle ? e.CastleSizeCells : kind == BuildingKind.Caravanserai ? e.CaravanseraiSizeCells
                : kind == BuildingKind.EngineerCamp ? e.EngineerCampSizeCells
                : kind == BuildingKind.Academy ? e.AcademySizeCells
                : kind == BuildingKind.Shrine ? e.ShrineSizeCells
                : kind == BuildingKind.Monastery ? e.MonasterySizeCells
                : kind == BuildingKind.Harbor ? e.HarborSizeCells
                : kind == BuildingKind.MineShaft ? e.MountainSizeCells
                : kind == BuildingKind.Tollgate ? e.TollgateLengthCells
                 : kind == BuildingKind.GrandHouse ? e.GrandHouseSizeCells
            : kind == BuildingKind.Town ? e.TownSizeCells : kind == BuildingKind.Storage ? e.StorageSizeCells : kind == BuildingKind.Weaver ? e.WeaverSizeCells : kind == BuildingKind.IncenseHouse ? e.IncenseSizeCells : kind == BuildingKind.CharmWorkshop ? e.CharmSizeCells : kind == BuildingKind.RepairYard ? e.RepairYardSizeCells : kind == BuildingKind.ToolShop ? e.ToolShopSizeCells
                : kind == BuildingKind.Stonecutter ? e.StonecutterSizeCells : kind == BuildingKind.Joinery ? e.JoinerySizeCells : kind == BuildingKind.Assembler ? e.AssemblerSizeCells
                : kind == BuildingKind.Bridge ? 1 : e.BarracksSizeCells;
        }

        private int HpOf(BuildingKind kind, uint faction, int origin = -1)
        {
            var e = world.Config.Economy;
            int hp = kind == BuildingKind.Mine ? e.MineHp : kind == BuildingKind.Drill ? e.DrillHp : kind == BuildingKind.LumberCamp ? e.LumberCampHp : kind == BuildingKind.Quarry ? e.QuarryHp : kind == BuildingKind.Smelter ? e.SmelterHp : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnHp
                : kind == BuildingKind.Steelworks ? e.SteelworksHp : kind == BuildingKind.Farm ? e.FarmHp
                : kind == BuildingKind.Fletcher ? e.FletcherHp : kind == BuildingKind.Mill ? e.MillHp : kind == BuildingKind.BrickKiln ? e.BrickKilnHp : kind == BuildingKind.Saddlery ? e.SaddleryHp : kind == BuildingKind.Sawmill ? e.SawmillHp : kind == BuildingKind.Scriptorium ? e.ScriptoriumHp : kind == BuildingKind.DryingRack ? e.DryingRackHp : kind == BuildingKind.Lapidary ? e.LapidaryHp
                : kind == BuildingKind.House ? e.HouseHp : kind == BuildingKind.DropSite ? e.DropSiteHp
                : kind == BuildingKind.Wall ? e.WallHp : kind == BuildingKind.Tower ? e.TowerHp : kind == BuildingKind.Blacksmith ? e.BlacksmithHp
                : kind == BuildingKind.Market ? e.MarketHp : kind == BuildingKind.SiegeWorkshop ? e.WorkshopHp
                : kind == BuildingKind.ArcheryRange ? e.RangeHp : kind == BuildingKind.Stable ? e.StableHp
                : kind == BuildingKind.Castle ? e.CastleHp : kind == BuildingKind.Caravanserai ? e.CaravanseraiHp
                : kind == BuildingKind.EngineerCamp ? e.EngineerCampHp : kind == BuildingKind.Academy ? e.AcademyHp
                : kind == BuildingKind.Shrine ? e.ShrineHp
                : kind == BuildingKind.Monastery ? e.MonasteryHp : kind == BuildingKind.Harbor ? e.HarborHp : kind == BuildingKind.MineShaft ? e.MountainHp
                 : kind == BuildingKind.Tollgate ? TollgateHpFor(e.TollgateHp, faction) : kind == BuildingKind.GrandHouse ? e.GrandHouseHp
            : kind == BuildingKind.Town ? e.TownHp : kind == BuildingKind.Storage ? e.StorageHp : kind == BuildingKind.Weaver ? e.WeaverHp : kind == BuildingKind.IncenseHouse ? e.IncenseHp : kind == BuildingKind.CharmWorkshop ? e.CharmHp : kind == BuildingKind.RepairYard ? e.RepairYardHp : kind == BuildingKind.ToolShop ? e.ToolShopHp : kind == BuildingKind.Stonecutter ? e.StonecutterHp : kind == BuildingKind.Joinery ? e.JoineryHp : kind == BuildingKind.Assembler ? e.AssemblerHp : kind == BuildingKind.Bridge ? BridgeHpForBuilding(faction) : e.BarracksHp;
            if (origin >= 0 && (kind == BuildingKind.Wall || kind == BuildingKind.Tower))
                hp = MountainFortHp(hp, faction, origin, SizeOf(kind));
            if (kind == BuildingKind.Wall && AgeStrength(faction, CivKind.Masonry, 2))
                hp = checked(hp * world.Config.Economy.MasonryWallHpPermille / 1000);
            if (kind == BuildingKind.Tollgate && AgeStrength(faction, CivKind.Tollgate, 3))
                hp = checked(hp * world.Config.Economy.TollgateHpPermille / 1000);
            return hp;
        }

        private bool IsMasonryDefence(uint faction, BuildingKind kind)
            => MasonryAllowed(faction) && (kind == BuildingKind.Wall || kind == BuildingKind.Tower || kind == BuildingKind.Castle);

        private int MasonryDiscount(int value, int permille)
        {
            // All defence discounts use integer multiplication followed by floor division by 1000; no floating point or rounding is involved.
            return checked(value * permille / 1000);
        }

        private int WorkOf(BuildingKind kind, uint faction)
        {
            var e = world.Config.Economy;
            int work = kind == BuildingKind.Mine ? e.MineWork : kind == BuildingKind.Drill ? e.DrillWork : kind == BuildingKind.LumberCamp ? e.LumberCampWork : kind == BuildingKind.Quarry ? e.QuarryWork : kind == BuildingKind.Smelter ? e.SmelterWork : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnWork
                : kind == BuildingKind.Steelworks ? e.SteelworksWork : kind == BuildingKind.Farm ? e.FarmWork
                : kind == BuildingKind.Fletcher ? e.FletcherWork : kind == BuildingKind.Mill ? e.MillWork : kind == BuildingKind.BrickKiln ? e.BrickKilnWork : kind == BuildingKind.Saddlery ? e.SaddleryWork : kind == BuildingKind.Sawmill ? e.SawmillWork : kind == BuildingKind.Scriptorium ? e.ScriptoriumWork : kind == BuildingKind.DryingRack ? e.DryingRackWork : kind == BuildingKind.Lapidary ? e.LapidaryWork
                : kind == BuildingKind.House ? e.HouseWork : kind == BuildingKind.DropSite ? e.DropSiteWork
                : kind == BuildingKind.Wall ? 1 : kind == BuildingKind.Tower ? e.TowerWork : kind == BuildingKind.Blacksmith ? e.BlacksmithWork
                : kind == BuildingKind.Market ? e.MarketWork : kind == BuildingKind.SiegeWorkshop ? e.WorkshopWork
                : kind == BuildingKind.ArcheryRange ? e.RangeWork : kind == BuildingKind.Stable ? e.StableWork
                : kind == BuildingKind.Castle ? e.CastleWork : kind == BuildingKind.Caravanserai ? e.CaravanseraiWork
                : kind == BuildingKind.EngineerCamp ? e.EngineerCampWork : kind == BuildingKind.Academy ? e.AcademyWork
                : kind == BuildingKind.Shrine ? e.ShrineWork
                : kind == BuildingKind.Monastery ? e.MonasteryWork : kind == BuildingKind.Harbor ? e.HarborWork : kind == BuildingKind.MineShaft ? e.MountainWork
                 : kind == BuildingKind.Tollgate ? e.TollgateWork : kind == BuildingKind.GrandHouse ? e.GrandHouseWork
            : kind == BuildingKind.Town ? e.TownWork : kind == BuildingKind.Storage ? e.StorageWork : kind == BuildingKind.Weaver ? e.WeaverWork : kind == BuildingKind.IncenseHouse ? e.IncenseWork : kind == BuildingKind.CharmWorkshop ? e.CharmWork : kind == BuildingKind.RepairYard ? e.RepairYardWork : kind == BuildingKind.ToolShop ? e.ToolShopWork : kind == BuildingKind.Stonecutter ? e.StonecutterWork : kind == BuildingKind.Joinery ? e.JoineryWork : kind == BuildingKind.Assembler ? e.AssemblerWork : kind == BuildingKind.Bridge ? BridgeWorkFor(faction) : e.BarracksWork;
            work = IsMasonryDefence(faction, kind) ? MasonryDiscount(work, e.MasonryDefenceWorkPermille) : work;
            if (kind == BuildingKind.Bridge && AgeStrength(faction, CivKind.Bridge, 2))
                work = Math.Max(1, checked((work * e.BridgeBuildingWorkPermille + 999) / 1000));
            if (AgeStrength(faction, CivKind.Bridge, 4))
                work = Math.Max(1, checked((work * e.BridgeGeneralWorkPermille + 999) / 1000));
            return work;
        }

        private int WorkOf(BuildingState building)
        {
            int work = WorkOf(building.Kind, building.FactionId);
            if (CraftTree1On && building.FramesUsed && building.Kind != BuildingKind.Wall)
                work = Math.Max(1, checked((work * world.Config.Economy.CraftTreeFrameWorkPermille + 999) / 1000));
            return work;
        }

        private int WoodOf(BuildingKind kind, uint faction)
        {
            var e = world.Config.Economy;
            int wood = kind == BuildingKind.Mine ? e.MineWoodCost : kind == BuildingKind.Drill ? e.DrillWoodCost : kind == BuildingKind.LumberCamp ? e.LumberCampWoodCost : kind == BuildingKind.Quarry ? e.QuarryWoodCost : kind == BuildingKind.Smelter ? e.SmelterWoodCost : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnWoodCost
                : kind == BuildingKind.Steelworks ? e.SteelworksWoodCost : kind == BuildingKind.Farm ? e.FarmWoodCost
                : kind == BuildingKind.Fletcher ? e.FletcherWoodCost : kind == BuildingKind.Mill ? e.MillWoodCost : kind == BuildingKind.BrickKiln ? e.BrickKilnWoodCost : kind == BuildingKind.Saddlery ? e.SaddleryWoodCost : kind == BuildingKind.Sawmill ? e.SawmillWoodCost : kind == BuildingKind.Scriptorium ? e.ScriptoriumWoodCost : kind == BuildingKind.DryingRack ? e.DryingRackWoodCost : kind == BuildingKind.Lapidary ? e.LapidaryWoodCost
                : kind == BuildingKind.House ? e.HouseWoodCost : kind == BuildingKind.DropSite ? e.DropSiteWoodCost
                : kind == BuildingKind.Wall ? 0 : kind == BuildingKind.Tower ? e.TowerWoodCost : kind == BuildingKind.Blacksmith ? e.BlacksmithWoodCost
                : kind == BuildingKind.Market ? e.MarketWoodCost : kind == BuildingKind.SiegeWorkshop ? e.WorkshopWoodCost
                : kind == BuildingKind.ArcheryRange ? e.RangeWoodCost : kind == BuildingKind.Stable ? e.StableWoodCost
                : kind == BuildingKind.Castle ? e.CastleWoodCost : kind == BuildingKind.Caravanserai ? e.CaravanseraiWoodCost
                 : kind == BuildingKind.EngineerCamp ? e.EngineerCampWoodCost : kind == BuildingKind.Academy ? e.AcademyWoodCost
                : kind == BuildingKind.Shrine ? e.ShrineWoodCost
                : kind == BuildingKind.Monastery ? e.MonasteryWoodCost : kind == BuildingKind.Harbor ? e.HarborWoodCost : kind == BuildingKind.MineShaft ? e.MountainWoodCost
                  : kind == BuildingKind.Tollgate ? e.TollgateWoodCost : kind == BuildingKind.GrandHouse ? e.GrandHouseWoodCost
            : kind == BuildingKind.Town ? e.TownWoodCost : kind == BuildingKind.Storage ? e.StorageWoodCost : kind == BuildingKind.Weaver ? e.WeaverWoodCost : kind == BuildingKind.IncenseHouse ? e.IncenseWoodCost : kind == BuildingKind.CharmWorkshop ? e.CharmWoodCost : kind == BuildingKind.RepairYard ? e.RepairYardWoodCost : kind == BuildingKind.ToolShop ? e.ToolShopWoodCost : kind == BuildingKind.Stonecutter ? e.StonecutterWoodCost : kind == BuildingKind.Joinery ? e.JoineryWoodCost : kind == BuildingKind.Assembler ? e.AssemblerWoodCost : kind == BuildingKind.Bridge ? e.BridgeWoodCost : e.BarracksWoodCost;
            wood = IsMasonryDefence(faction, kind) ? MasonryDiscount(wood, e.MasonryDefenceCostPermille) : wood;
            return AgeStrength(faction, CivKind.Forestry, 4)
                ? checked((wood * e.ForestryBuildingWoodPermille + 999) / 1000) : wood;
        }

        private int WoodOfForPlacement(BuildingKind kind, uint faction, bool framesUsed)
        {
            int wood = WoodOf(kind, faction);
            if (CraftTree1On && framesUsed && kind != BuildingKind.Wall)
                return checked((wood * world.Config.Economy.CraftTreeFrameWoodPermille + 999) / 1000);
            return wood;
        }

        private bool IsCraftProcessingBuilding(BuildingKind kind)
            => kind == BuildingKind.Smelter || kind == BuildingKind.CharcoalKiln || kind == BuildingKind.Steelworks
                || kind == BuildingKind.Fletcher || kind == BuildingKind.Mill || kind == BuildingKind.BrickKiln
                || kind == BuildingKind.Saddlery || kind == BuildingKind.Sawmill || kind == BuildingKind.Scriptorium
                || kind == BuildingKind.DryingRack || kind == BuildingKind.Lapidary || kind == BuildingKind.Weaver
                || kind == BuildingKind.IncenseHouse || kind == BuildingKind.CharmWorkshop || kind == BuildingKind.RepairYard
                || kind == BuildingKind.ToolShop || kind == BuildingKind.Stonecutter || kind == BuildingKind.Joinery || kind == BuildingKind.Assembler;

        private void UpgradeProcessingBuilding(uint faction, uint buildingId)
        {
            if (!CraftTree1On || !OwnBuilding(faction, buildingId, out int index)) return;
            ref var building = ref world.Buildings[index];
            if (!IsActualBuilding(building) || !building.Complete || !IsCraftProcessingBuilding(building.Kind) || building.Level >= 3) return;
            ref var economy = ref world.Economies[faction - 1];
            if (building.Level == 1)
            {
                int cost = world.Config.Economy.CraftTreeLevel2FramesCost;
                if (economy.Frames < cost) return;
                economy.Frames -= cost;
                economy.FramesConsumed = checked(economy.FramesConsumed + cost);
                economy.FramesConsumedUpgrades = checked(economy.FramesConsumedUpgrades + cost);
                building.Level = 2;
            }
            else
            {
                if (!CraftTree3On || economy.Mechanism < 1) return;
                economy.Mechanism--;
                economy.MechanismConsumed = checked(economy.MechanismConsumed + 1);
                building.Level = 3;
            }
        }

        private bool PlankBridgeAvailable(uint faction, int cells)
            => PlanksAllowed(faction) && cells > 0 && world.Economies[faction - 1].Planks >= checked(world.Config.Economy.PlanksPerBridgeCell * cells);

        private int BridgeWoodCostFor(uint faction, int cells)
            => PlankBridgeAvailable(faction, cells)
                ? checked(world.Config.Economy.BridgeWoodCost * world.Config.Economy.PlankBridgeWoodPermille / 1000)
                : WoodOf(BuildingKind.Bridge, faction);

        // Keep the age-tech effect local to bridge buildings. Existing non-bridge building rules,
        // including flag-off maps, continue to use their original values unchanged.
        private int BridgeHpForBuilding(uint faction)
            => BridgeOn && world.Economies[faction - 1].Civ == CivKind.Bridge ? BridgeHpFor(faction) : world.Config.Economy.BridgeHp;

        /// <summary>V3-5: the stone a building costs (walls and towers).</summary>
        private int StoneOf(BuildingKind kind, uint faction)
        {
            var e = world.Config.Economy;
            int stone = kind == BuildingKind.Drill ? e.DrillStoneCost : kind == BuildingKind.Wall ? e.WallStoneCost : kind == BuildingKind.Tower ? e.TowerStoneCost
                : kind == BuildingKind.Castle ? e.CastleStoneCost : kind == BuildingKind.Tollgate ? e.TollgateStoneCost : kind == BuildingKind.Shrine ? e.ShrineStoneCost
                : kind == BuildingKind.Town ? e.TownStoneCost : 0;
            if (kind == BuildingKind.Castle && SiegePartsCastleAvailable(faction))
                return checked((stone * e.SiegePartsCastleStonePermille + 999) / 1000);
            if (kind == BuildingKind.Castle && BrickCastleDiscountAvailable(faction))
                return MasonryDiscount(stone, e.BrickCastleStonePermille);
            return IsMasonryDefence(faction, kind) ? MasonryDiscount(stone, e.MasonryDefenceCostPermille) : stone;
        }

        private bool BrickCastleDiscountAvailable(uint faction)
            => BricksAllowed(faction) && world.Economies[faction - 1].Bricks >= world.Config.Economy.BricksPerCastle;

        private bool SiegePartsCastleAvailable(uint faction)
            => CraftTree3On && world.Economies[faction - 1].Age >= 3
                && world.Economies[faction - 1].SiegeParts >= world.Config.Economy.SiegePartsPerCastle;

        private bool BrickDefenceAvailable(uint faction, BuildingKind kind)
            => BricksAllowed(faction) && (kind == BuildingKind.Wall || kind == BuildingKind.Tower)
                && world.Economies[faction - 1].Bricks >= (kind == BuildingKind.Wall ? world.Config.Economy.BricksPerWall : world.Config.Economy.BricksPerTower);

        private int BrickHpOf(BuildingKind kind, uint faction, int origin = -1)
            => checked(HpOf(kind, faction, origin) * world.Config.Economy.BrickHpPermille / 1000);

        private int MaxHpOf(BuildingState building)
            => building.BrickUpgraded && (building.Kind == BuildingKind.Wall || building.Kind == BuildingKind.Tower)
                ? BrickHpOf(building.Kind, building.FactionId, building.OriginCell) : HpOf(building.Kind, building.FactionId, building.OriginCell);

        private int[] Footprint(BuildingState b) => b.Kind == BuildingKind.Bridge ? b.BridgeCells ?? System.Array.Empty<int>()
            : b.Kind == BuildingKind.Tollgate ? TollgateFootprint(b.OriginCell, b.Facing) : Footprint(b.OriginCell, SizeOf(b.Kind));

        private int[] TollgateFootprint(int origin, Facing facing)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            if (origin < 0 || origin >= width * height) return Array.Empty<int>();
            int x = origin % width, z = origin / width;
            if (facing == Facing.East && x + 1 >= width) return Array.Empty<int>();
            if (facing == Facing.West && x <= 0) return Array.Empty<int>();
            if (facing == Facing.North && z + 1 >= height) return Array.Empty<int>();
            if (facing == Facing.South && z <= 0) return Array.Empty<int>();
            int delta = facing == Facing.East ? 1 : facing == Facing.West ? -1 : facing == Facing.North ? width : -width;
            return new[] { origin, origin + delta };
        }

        private int[] Footprint(int origin, int size)
        {
            int width = world.Config.Map.WidthCells;
            var cells = new int[size * size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++) cells[z * size + x] = origin + z * width + x;
            return cells;
        }

        private SimPoint FootprintCenter(int origin, int size)
        {
            var corner = world.Map.Center(origin);
            long half = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw * (size - 1) / 2;
            return new SimPoint(Fix64.FromRaw(corner.X.Raw + half), Fix64.FromRaw(corner.Z.Raw + half));
        }

        private SimPoint BridgeCenter(int[] cells)
        {
            if (cells == null || cells.Length == 0) return world.Map.Center(0);
            var first = world.Map.Center(cells[0]);
            var last = world.Map.Center(cells[cells.Length - 1]);
            return new SimPoint(Fix64.FromRaw((first.X.Raw + last.X.Raw) / 2), Fix64.FromRaw((first.Z.Raw + last.Z.Raw) / 2));
        }

        private SimPoint BuildingCenter(BuildingState building)
            => building.Kind == BuildingKind.Bridge ? BridgeCenter(building.BridgeCells)
                : building.Kind == BuildingKind.Tollgate ? BridgeCenter(TollgateFootprint(building.OriginCell, building.Facing))
                : FootprintCenter(building.OriginCell, SizeOf(building.Kind));

        private bool IsRiverCell(int cell)
            => world.Config.Map.Terrain.Length != 0 && cell >= 0 && cell < world.Config.Map.Terrain.Length
                && world.Config.Map.Terrain[cell] == (byte)TerrainKind.River;

        private int NearestPassableCell(SimPoint from)
        {
            int best = -1;
            for (int i = 0; i < world.Config.Map.WidthCells * world.Config.Map.HeightCells; i++)
                if (world.Map.IsPassable(i) && (best < 0 || DistanceSquared(from, world.Map.Center(i)) < DistanceSquared(from, world.Map.Center(best)))) best = i;
            return best;
        }

        private int NearestReachablePassableCell(SimPoint from, uint faction)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            int width = world.Config.Map.WidthCells, start = world.Map.Cell(OwnCore(faction).Definition.Position);
            var seen = new bool[cells];
            var queue = new int[cells];
            int head = 0, tail = 0, best = -1;
            if (start >= 0 && start < cells && world.Map.IsPassable(start)) { seen[start] = true; queue[tail++] = start; }
            while (head < tail)
            {
                int cell = queue[head++];
                if (best < 0 || DistanceSquared(from, world.Map.Center(cell)) < DistanceSquared(from, world.Map.Center(best))
                    || DistanceSquared(from, world.Map.Center(cell)) == DistanceSquared(from, world.Map.Center(best)) && cell < best) best = cell;
                int x = cell % width;
                Visit(cell + width); if (x + 1 < width) Visit(cell + 1); Visit(cell - width); if (x > 0) Visit(cell - 1);
            }
            return best >= 0 ? best : NearestPassableCell(from);
            void Visit(int next)
            {
                if (next < 0 || next >= cells || seen[next] || !world.Map.IsPassable(next)) return;
                seen[next] = true; queue[tail++] = next;
            }
        }

        private int Chebyshev(int a, int b)
        {
            int width = world.Config.Map.WidthCells;
            return Math.Max(Math.Abs(a % width - b % width), Math.Abs(a / width - b / width));
        }
    }
}
