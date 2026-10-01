using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Explicit week-one scenario binary schema. Also canonicalizes authoring enumeration order.</summary>
    public static class ScenarioBinary
    {
        public const string RulesVersion = "week3-reinforcements-4";
        private const int ForestryTailMarker = 0x46525354; // "FRST", after the existing optional tail chain.
        private const int MasonryTailMarker = 0x4D534F4E; // "MSON", after the forestry tail when present.
        private const int CaravanTailMarker = 0x4352564E; // "CRVN", after forestry/masonry tails when present.
        private const int CavalryTailMarker = 0x43564C59; // "CVLY", after the caravan tail when present.
        private const int BridgeTailMarker = 0x42524447; // "BRDG", after the masonry tail (always written with it) and the cavalry tail.
        private const int ExtensionMarker = 0x4E545845; // "EXTN", after every existing civilisation tail.
        private const int ExtensionSchemaVersion = 1;

        // Keep this an ordered array. Adding the eighth civilisation's record is one row here; neither reflection nor
        // dictionary enumeration may determine the binary order. ID 1 is deliberately a test-only integer payload.
        private const int AcademyExtensionId = 2;
        private const int AcademyExtensionVersion = 1;
        private const int AcademyExtensionDataLength = 13 * sizeof(int);
        private const int CultExtensionId = 3;
        private const int CultExtensionVersion = 1;
        private const int CultExtensionDataLength = 7 * sizeof(int);
        private const int MountainExtensionId = 5;
        private const int MountainExtensionVersion = 1;
        private const int MountainExtensionDataLength = 12 * sizeof(int);
        private const int TollgateExtensionId = 6;
        private const int TollgateExtensionVersion = 1;
        private const int TollgateExtensionDataLength = 7 * sizeof(int);
        private sealed class ExtensionRegistration
        {
            internal readonly int Id, Version, DataLength;
            internal ExtensionRegistration(int id, int version, int dataLength) { Id = id; Version = version; DataLength = dataLength; }
        }

        private static readonly ExtensionRegistration[] ExtensionRegistrations =
        {
            new ExtensionRegistration(1, 1, sizeof(int)),
            new ExtensionRegistration(AcademyExtensionId, AcademyExtensionVersion, AcademyExtensionDataLength),
            new ExtensionRegistration(CultExtensionId, CultExtensionVersion, CultExtensionDataLength),
            new ExtensionRegistration(MountainExtensionId, MountainExtensionVersion, MountainExtensionDataLength),
            new ExtensionRegistration(TollgateExtensionId, TollgateExtensionVersion, TollgateExtensionDataLength)
        };

        public static byte[] Encode(ScenarioDefinition source)
        {
            var c = new WorldState(source).Config;
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                // Preserve the v1 bytes (and Config.Hash) for historical default rules.
                // Binary v2 adds tuning values; the authoring JSON remains schema 1.
                bool tuned = c.Rules.OccupationThreatMemoryTicks != 200 || c.Rules.DefaultReservePermille != 100;
                // Binary v3 adds the Ver.3 resources and economy rules. Written only when a scenario has them, so every
                // Ver.1 scenario keeps its v1/v2 bytes and therefore its Config.Hash.
                bool economy = c.ResourceNodes.Length > 0 || c.Economy.Enabled;
                // Binary v4 adds the V3-2 industry rules and the starting belts; a V3-1 economy keeps its v3 bytes.
                // Binary v5 adds the V3-4 terrain kinds; a map without terrain keeps its v4 bytes.
                int schema = c.Map.Terrain.Length != 0 ? 5 : c.Economy.Industry ? 4 : economy ? 3 : tuned ? 2 : 1;
                w.Write(schema); Text(w, c.ScenarioId); w.Write(c.Seed); w.Write(c.TickRateHz); w.Write(c.VerificationTickLimit);
                var m = c.Map;
                w.Write(m.WidthMeters); w.Write(m.HeightMeters); w.Write(m.CellSizeMeters); w.Write(m.WidthCells); w.Write(m.HeightCells); w.Write(m.DefaultPassable);
                w.Write((uint)m.BlockedCellIds.Length); foreach (var id in m.BlockedCellIds) w.Write(id);
                var r = c.Rules;
                w.Write(r.FactionCap); w.Write(r.CoreRadius.Raw); w.Write(r.OwnedObjectiveVision.Raw); w.Write(r.CaptureRadius.Raw);
                w.Write(r.CaptureDurationTicks); w.Write(r.CoreReinforcementIntervalTicks); w.Write(r.OutpostReinforcementIntervalTicks);
                if (schema >= 2) { w.Write(r.OccupationThreatMemoryTicks); w.Write(r.DefaultReservePermille); }
                w.Write((uint)c.UnitParameters.Length);
                foreach (var p in c.UnitParameters) { w.Write((byte)p.Kind); w.Write(p.Hp); w.Write(p.Speed.Raw); w.Write(p.Vision.Raw); w.Write(p.Range.Raw); w.Write(p.Damage); w.Write(p.AttackIntervalTicks); }
                w.Write((uint)c.Factions.Length); foreach (var f in c.Factions) { w.Write(f.Id); w.Write(f.CoreId); w.Write((uint)f.ArmyIds.Length); foreach (var id in f.ArmyIds) w.Write(id); }
                w.Write((uint)c.Armies.Length); foreach (var a in c.Armies) { w.Write(a.Id); w.Write(a.FactionId); Text(w,a.Role); w.Write(a.Capacity); Goal(w,a.HomeObjective); }
                w.Write((uint)c.Soldiers.Length); foreach (var p in c.Soldiers) { w.Write(p.Id); w.Write(p.FactionId); w.Write(p.ArmyId); w.Write((byte)p.Kind); w.Write(p.Alive); Point(w,p.Position); w.Write(p.Hp); }
                w.Write((uint)c.Cores.Length); foreach (var p in c.Cores) { w.Write(p.Id); w.Write(p.FactionId); Point(w,p.Position); w.Write(p.Hp); }
                w.Write((uint)c.Outposts.Length); foreach (var p in c.Outposts) { w.Write(p.Id); Point(w,p.Position); w.Write(p.OwnerFactionId); }
                if (schema >= 3)
                {
                    w.Write((uint)c.ResourceNodes.Length); foreach (var n in c.ResourceNodes) { w.Write(n.Id); w.Write((byte)n.Kind); Point(w,n.Position); w.Write(n.Amount); }
                    var e = c.Economy;
                    w.Write(e.Enabled);
                    if (e.Enabled)
                    {
                        w.Write(e.StartFood); w.Write(e.StartWood); w.Write(e.PopulationCap); w.Write(e.VillagerHp); w.Write(e.VillagerSpeed.Raw);
                        w.Write(e.CarryCapacity); w.Write(e.GatherIntervalTicks); w.Write(e.VillagerFoodCost); w.Write(e.VillagerTrainTicks);
                        w.Write(e.QueueLimit); w.Write(e.AutoVillagerTarget); w.Write(e.DropOffMargin.Raw);
                        w.Write(e.BarracksSizeCells); w.Write(e.BarracksWoodCost); w.Write(e.BarracksWork); w.Write(e.BarracksHp); w.Write(e.Builders);
                        w.Write(e.InfantryFoodCost); w.Write(e.InfantryWoodCost); w.Write(e.InfantryTrainTicks); w.Write(e.AutoInfantryQueue);
                        w.Write((uint)c.Villagers.Length); foreach (var v in c.Villagers) { w.Write(v.Id); w.Write(v.FactionId); Point(w,v.Position); }
                    }
                }
                if (schema >= 4)
                {
                    var e = c.Economy;
                    w.Write(e.BeltWoodCost); w.Write(e.BeltTicksPerCell); w.Write(e.BeltHp); w.Write(e.BeltLimit);
                    w.Write(e.MineSizeCells); w.Write(e.MineWoodCost); w.Write(e.MineWork); w.Write(e.MineHp); w.Write(e.MineIntervalTicks);
                    w.Write(e.SmelterSizeCells); w.Write(e.SmelterWoodCost); w.Write(e.SmelterWork); w.Write(e.SmelterHp); w.Write(e.SmeltTicks); w.Write(e.OrePerMetal);
                    w.Write(e.BufferLimit); w.Write(e.InfantryMetalCost);
                    w.Write((uint)c.Belts.Length); foreach (var b in c.Belts) { w.Write(b.Cell); w.Write(b.FactionId); w.Write((byte)b.Facing); w.Write((byte)b.Item); }
                }
                if (schema >= 5)
                {
                    w.Write((uint)m.Terrain.Length); w.Write(m.Terrain);
                    var e = c.Economy;
                    w.Write(e.Ages); w.Write(e.AdvanceFoodCost); w.Write(e.AdvanceWoodCost); w.Write(e.AdvanceTicks);
                    w.Write(e.AgrarianInfantryFood); w.Write(e.AgrarianInfantryWood); w.Write(e.AgrarianInfantryTicks); w.Write(e.ForgedInfantryHp); w.Write(e.ForgedInfantryDamage);
                    w.Write(e.FarmSizeCells); w.Write(e.FarmWoodCost); w.Write(e.FarmWork); w.Write(e.FarmHp);
                    w.Write(e.FarmBaseTicks); w.Write(e.FarmStepTicks); w.Write(e.FarmMinTicks); w.Write(e.FarmFoodReach); w.Write(e.FarmRiverReach);
                    w.Write(e.ScoutFoodCost); w.Write(e.ScoutWoodCost); w.Write(e.ScoutTrainTicks);
                    w.Write(e.BasePopulation); w.Write(e.HousePopulation); w.Write(e.HouseSizeCells); w.Write(e.HouseWoodCost); w.Write(e.HouseWork); w.Write(e.HouseHp);
                    w.Write(e.DropSiteSizeCells); w.Write(e.DropSiteWoodCost); w.Write(e.DropSiteWork); w.Write(e.DropSiteHp);
                    w.Write(e.WallStoneCost); w.Write(e.WallHp); w.Write(e.WallReach); w.Write(e.StartStone); w.Write(e.StartMetal);
                    w.Write(e.TowerSizeCells); w.Write(e.TowerWoodCost); w.Write(e.TowerStoneCost); w.Write(e.TowerWork); w.Write(e.TowerHp);
                    w.Write(e.TowerRange); w.Write(e.TowerVision); w.Write(e.TowerDamage); w.Write(e.TowerIntervalTicks);
                    w.Write(e.BlacksmithSizeCells); w.Write(e.BlacksmithWoodCost); w.Write(e.BlacksmithWork); w.Write(e.BlacksmithHp);
                    for (int t = 0; t < 11; t++) { w.Write(e.TechFood[t]); w.Write(e.TechWood[t]); w.Write(e.TechTicks[t]); w.Write(e.TechMetal[t]); }
                    w.Write(e.WeaponsDamage); w.Write(e.ArmourHp); w.Write(e.ToolsGatherTicks); w.Write(e.CartsCarry); w.Write(e.IrrigationTicks); w.Write(e.BlastFurnaceTicks);
                    w.Write(e.Age2FoodCost); w.Write(e.Age2WoodCost); w.Write(e.Age2Ticks); w.Write(e.Age2PopulationBonus);
                    w.Write(e.ArcherFood); w.Write(e.ArcherWood); w.Write(e.ArcherTicks); w.Write(e.ArcherHp); w.Write(e.ArcherDamage); w.Write(e.ArcherInterval);
                    w.Write(e.ArcherRange.Raw); w.Write(e.ArcherSpeed.Raw); w.Write(e.ArcherVision.Raw);
                    w.Write(e.CavalryFood); w.Write(e.CavalryWood); w.Write(e.CavalryMetal); w.Write(e.CavalryTicks); w.Write(e.CavalryHp); w.Write(e.CavalryDamage); w.Write(e.CavalryInterval);
                    w.Write(e.CavalryRange.Raw); w.Write(e.CavalrySpeed.Raw); w.Write(e.CavalryVision.Raw);
                    w.Write(e.MarketSizeCells); w.Write(e.MarketWoodCost); w.Write(e.MarketWork); w.Write(e.MarketHp); w.Write(e.TradeLot); w.Write(e.TradeReturn);
                    w.Write(e.WorkshopSizeCells); w.Write(e.WorkshopWoodCost); w.Write(e.WorkshopWork); w.Write(e.WorkshopHp);
                    w.Write(e.RamFood); w.Write(e.RamWood); w.Write(e.RamTicks); w.Write(e.RamHp); w.Write(e.RamDamage); w.Write(e.RamSiegeDamage); w.Write(e.RamInterval);
                    w.Write(e.RamRange.Raw); w.Write(e.RamSpeed.Raw); w.Write(e.RamVision.Raw);
                    w.Write(e.Age3FoodCost); w.Write(e.Age3WoodCost); w.Write(e.Age3Ticks); w.Write(e.Age3PopulationBonus);
                    w.Write(e.SiegecraftSiegeDamage); w.Write(e.MasonryTowerDamage); w.Write(e.BankingTradeReturn);
                    w.Write(e.RangeSizeCells); w.Write(e.RangeWoodCost); w.Write(e.RangeWork); w.Write(e.RangeHp);
                    w.Write(e.StableSizeCells); w.Write(e.StableWoodCost); w.Write(e.StableWork); w.Write(e.StableHp);
                    w.Write(e.CounterBonusPermille); w.Write(e.SteelWeaponsDamage); w.Write(e.SteelArmourHp);
                    w.Write(e.RepairHpPerTick); w.Write(e.RepairAtPermille);
                    w.Write(e.CastleSizeCells); w.Write(e.CastleWoodCost); w.Write(e.CastleStoneCost); w.Write(e.CastleWork); w.Write(e.CastleHp);
                    w.Write(e.CastleRange); w.Write(e.CastleVision); w.Write(e.CastleDamage); w.Write(e.CastleIntervalTicks);
                    w.Write(e.TradeRouteWood); w.Write(e.TradeRouteMin);
                    // V3-5 #19: append Gems and the twelfth tech slot to schema 5, preserving the earlier field order.
                    w.Write(e.GemsTradeReturn); w.Write(e.GemArmorHp);
                    w.Write(e.TechFood[11]); w.Write(e.TechWood[11]); w.Write(e.TechTicks[11]); w.Write(e.TechMetal[11]);
                    for (int t = 0; t < 12; t++) w.Write(e.TechGems[t]);
                    w.Write(e.MercenaryGems); w.Write(e.MercenaryTicks); w.Write(e.MercenaryHp); w.Write(e.MercenaryDamage); w.Write(e.MercenaryInterval);
                    w.Write(e.AgeVictoryEnabled); w.Write(e.AgeVictoryTicks);
                }
                // V3-5 #22: append optional monk rules after every prior schema block. Defaults remain byte-for-byte
                // compatible with older maps, while an enabled/custom rule set survives schema 3/4 as well as schema 5.
                bool monkRules = c.Economy.MonksEnabled || c.Economy.ConversionTicks != 400
                    || c.Economy.MonkFoodCost != 60 || c.Economy.MonkGoldCost != 40 || c.Economy.MonkTrainTicks != 200;
                // The optional tails nest in order - monk, Age2SaveArmyFloor, fishing, gold, processing chain: each later
                // one writes the earlier ones (with their defaults) as its envelope, so a decoder can tell them apart by length alone.
            bool processingRules = c.Economy.ProcessingChain;
            bool forestryRules = c.Economy.Forestry;
            bool bridgeRules = c.Economy.Bridge;
            // The bridge civilisation carries the shared market reserves through the masonry tail, as it did on its own branch.
            bool masonryRules = c.Economy.Masonry || bridgeRules;
            bool caravanRules = c.Economy.Caravan;
            bool cavalryRules = c.Economy.Cavalry;
            // The extension section is read only at the end of the whole optional envelope; the monk/floor/fishing/gold
            // levels are told apart by "bytes remain". So a record with extensions writes the full envelope, exactly as
            // the civilisation tails do - otherwise a monk-only record would read the EXTN marker as its floor value.
            bool extensionRules = ExtensionsForEncode(c).Length > 0;
            bool goldRules = c.Economy.GoldEnabled || processingRules || forestryRules || masonryRules || caravanRules || cavalryRules
                || extensionRules;
                bool fishingRules = c.Economy.FishingEnabled || c.Economy.FishRegrowTicks != 100
                    || c.Economy.FishAgrarianBonusPermille != 300 || c.Economy.FishReach != 6 || goldRules;
                bool floorRules = c.Economy.Age2SaveArmyFloor != 0 || fishingRules;
                if (floorRules) monkRules = true;
                if (monkRules)
                {
                    w.Write(c.Economy.MonksEnabled); w.Write(c.Economy.ConversionTicks); w.Write(c.Economy.MonkFoodCost);
                    w.Write(c.Economy.MonkGoldCost); w.Write(c.Economy.MonkTrainTicks);
                    if (floorRules) w.Write(c.Economy.Age2SaveArmyFloor);
                    if (fishingRules)
                    {
                        w.Write(c.Economy.FishingEnabled); w.Write(c.Economy.FishRegrowTicks);
                        w.Write(c.Economy.FishAgrarianBonusPermille); w.Write(c.Economy.FishReach);
                    }
                    if (goldRules)
                    {
                        w.Write(c.Economy.GoldEnabled); w.Write(c.Economy.Age3GoldCostAgrarian); w.Write(c.Economy.Age3GoldCostMetallurgy); w.Write(c.Economy.GoldGatherers);
                        w.Write(c.Economy.GoldAmount); w.Write(c.Economy.GoldDangerMeters);
                    }
                    if (processingRules)
                    {
                        var e = c.Economy;
                        w.Write(e.CharcoalKilnSizeCells); w.Write(e.CharcoalKilnWoodCost); w.Write(e.CharcoalKilnWork); w.Write(e.CharcoalKilnHp); w.Write(e.CharcoalTicks);
                        w.Write(e.SteelworksSizeCells); w.Write(e.SteelworksWoodCost); w.Write(e.SteelworksWork); w.Write(e.SteelworksHp); w.Write(e.SteelTicks);
                        w.Write(e.HeavyInfantryFoodCost); w.Write(e.HeavyInfantryWoodCost); w.Write(e.HeavyInfantrySteelCost); w.Write(e.HeavyInfantryTrainTicks);
                        w.Write(e.HeavyInfantryHp); w.Write(e.HeavyInfantryDamage); w.Write(e.HeavyInfantryAttackIntervalTicks);
                        w.Write(e.HeavyInfantrySpeed.Raw); w.Write(e.HeavyInfantryVision.Raw); w.Write(e.HeavyInfantryRange.Raw);
                    }
                    if (forestryRules)
                    {
                        // A marker distinguishes this final extension from the older processing-chain tail. When
                        // processing is off, the earlier optional envelopes still end at this marker safely.
                        w.Write(ForestryTailMarker);
                        w.Write(c.Economy.Forestry);
                        w.Write(c.Economy.LumberCampSizeCells); w.Write(c.Economy.LumberCampWoodCost); w.Write(c.Economy.LumberCampWork);
                        w.Write(c.Economy.LumberCampHp); w.Write(c.Economy.LumberCampIntervalTicks);
                        w.Write(c.Economy.MarketFoodFloor); w.Write(c.Economy.MarketWoodReserve); w.Write(c.Economy.MarketStoneReserve);
                        w.Write(c.Economy.FletcherSizeCells); w.Write(c.Economy.FletcherWoodCost); w.Write(c.Economy.FletcherWork); w.Write(c.Economy.FletcherHp); w.Write(c.Economy.FletcherTicks);
                        w.Write(c.Economy.FletcherWoodInput); w.Write(c.Economy.FletcherFoodInput);
                        w.Write(c.Economy.SkirmishArcherFoodCost); w.Write(c.Economy.SkirmishArcherBowGearCost); w.Write(c.Economy.SkirmishArcherTrainTicks);
                        w.Write(c.Economy.SkirmishArcherHp); w.Write(c.Economy.SkirmishArcherDamage); w.Write(c.Economy.SkirmishArcherAttackIntervalTicks);
                        w.Write(c.Economy.SkirmishArcherSpeed.Raw); w.Write(c.Economy.SkirmishArcherVision.Raw); w.Write(c.Economy.SkirmishArcherRange.Raw);
                    }
                    if (masonryRules)
                    {
                        w.Write(MasonryTailMarker);
                        w.Write(c.Economy.Masonry);
                        w.Write(c.Economy.QuarrySizeCells); w.Write(c.Economy.QuarryWoodCost); w.Write(c.Economy.QuarryWork);
                        w.Write(c.Economy.QuarryHp); w.Write(c.Economy.QuarryIntervalTicks);
                        w.Write(c.Economy.MarketFoodFloor); w.Write(c.Economy.MarketWoodReserve); w.Write(c.Economy.MarketStoneReserve);
                        // V3-8 #2: the masonry-only defence discounts live in this existing tail, not a nested tail.
                        w.Write(c.Economy.MasonryDefenceCostPermille); w.Write(c.Economy.MasonryDefenceWorkPermille);
                    }
                    if (caravanRules)
                    {
                        // This marker is read before the payload, so a masonry tail's optional two defence values
                        // cannot consume the caravan flag. The pair and distance are runtime state, not scenario data.
                        w.Write(CaravanTailMarker);
                        w.Write(c.Economy.Caravan);
                        w.Write(c.Economy.CaravanseraiSizeCells); w.Write(c.Economy.CaravanseraiWoodCost); w.Write(c.Economy.CaravanseraiWork);
                        w.Write(c.Economy.CaravanseraiHp); w.Write(c.Economy.CaravanOutpostReach); w.Write(c.Economy.CaravanMinimumDistance);
                        w.Write(c.Economy.CaravanRewardDistanceStep); w.Write(c.Economy.CaravanRewardMaxWood); w.Write(c.Economy.CaravanAutoVillagers);
                    }
                    if (cavalryRules)
                    {
                        w.Write(CavalryTailMarker);
                        w.Write(c.Economy.Cavalry);
                        w.Write(c.Economy.LightCavalryFood); w.Write(c.Economy.LightCavalryWood); w.Write(c.Economy.LightCavalryTicks);
                        w.Write(c.Economy.LightCavalryHp); w.Write(c.Economy.LightCavalryDamage); w.Write(c.Economy.LightCavalryInterval);
                        w.Write(c.Economy.LightCavalryRange.Raw); w.Write(c.Economy.LightCavalrySpeed.Raw); w.Write(c.Economy.LightCavalryVision.Raw);
                        w.Write(c.Economy.CavalryDrillFood); w.Write(c.Economy.CavalryDrillWood); w.Write(c.Economy.CavalryDrillTicks);
                        w.Write(c.Economy.CavalryDrillSpeed.Raw);
                    }
                    if (bridgeRules)
                    {
                        w.Write(BridgeTailMarker);
                        w.Write(c.Economy.Bridge);
                        w.Write(c.Economy.EngineerCampSizeCells); w.Write(c.Economy.EngineerCampWoodCost); w.Write(c.Economy.EngineerCampWork); w.Write(c.Economy.EngineerCampHp);
                        w.Write(c.Economy.BridgeWoodCost); w.Write(c.Economy.BridgeWork); w.Write(c.Economy.BridgeHp); w.Write(c.Economy.MaxBridgeLength);
                        w.Write(c.Economy.BridgeworksFoodCost); w.Write(c.Economy.BridgeworksWoodCost); w.Write(c.Economy.BridgeworksTicks);
                        w.Write(c.Economy.BridgeworksHpBonus); w.Write(c.Economy.BridgeworksWorkReduction);
                        w.Write(c.Economy.SiegeDeploymentFoodCost); w.Write(c.Economy.SiegeDeploymentWoodCost); w.Write(c.Economy.SiegeDeploymentTicks);
                        w.Write(c.Economy.SiegeDeploymentRamTicksReduction); w.Write(c.Economy.SiegeDeploymentRamCapacityBonus);
                    }
                }
                WriteExtensionSection(w, ExtensionsForEncode(c));
                return s.ToArray();
            }
        }
        public static ScenarioDefinition Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Scenario size.");
            using (var s = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(s))
            {
                int schema = r.ReadInt32();
                if (schema < 1 || schema > 5) throw new InvalidDataException("Unknown scenario binary schema.");
                var c = new ScenarioDefinition { ScenarioId=Text(r), Seed=r.ReadUInt64(), TickRateHz=r.ReadInt32(), VerificationTickLimit=r.ReadInt64() };
                c.Map = new MapDefinition { WidthMeters=r.ReadInt32(), HeightMeters=r.ReadInt32(), CellSizeMeters=r.ReadInt32(), WidthCells=r.ReadInt32(), HeightCells=r.ReadInt32(), DefaultPassable=Bool(r), BlockedCellIds=new int[Count(r)] };
                for(int i=0;i<c.Map.BlockedCellIds.Length;i++) c.Map.BlockedCellIds[i]=r.ReadInt32();
                c.Rules = new RuleDefinition { FactionCap=r.ReadInt32(), CoreRadius=Fix(r), OwnedObjectiveVision=Fix(r), CaptureRadius=Fix(r), CaptureDurationTicks=r.ReadInt32(), CoreReinforcementIntervalTicks=r.ReadInt32(), OutpostReinforcementIntervalTicks=r.ReadInt32() };
                if (schema >= 2) { c.Rules.OccupationThreatMemoryTicks = r.ReadInt32(); c.Rules.DefaultReservePermille = r.ReadUInt16(); }
                c.UnitParameters=new UnitParameters[Count(r)]; for(int i=0;i<c.UnitParameters.Length;i++) c.UnitParameters[i]=new UnitParameters { Kind=(UnitKind)r.ReadByte(), Hp=r.ReadInt32(), Speed=Fix(r), Vision=Fix(r), Range=Fix(r), Damage=r.ReadInt32(), AttackIntervalTicks=r.ReadInt32() };
                c.Factions=new FactionDefinition[Count(r)]; for(int i=0;i<c.Factions.Length;i++) { var f=new FactionDefinition { Id=r.ReadUInt32(), CoreId=r.ReadUInt32(), ArmyIds=new uint[Count(r)] }; for(int j=0;j<f.ArmyIds.Length;j++) f.ArmyIds[j]=r.ReadUInt32(); c.Factions[i]=f; }
                c.Armies=new ArmyDefinition[Count(r)]; for(int i=0;i<c.Armies.Length;i++) c.Armies[i]=new ArmyDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Role=Text(r), Capacity=r.ReadInt32(), HomeObjective=Goal(r) };
                c.Soldiers=new SoldierDefinition[Count(r)]; for(int i=0;i<c.Soldiers.Length;i++) c.Soldiers[i]=new SoldierDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), ArmyId=r.ReadUInt32(), Kind=(UnitKind)r.ReadByte(), Alive=Bool(r), Position=Point(r), Hp=r.ReadInt32() };
                c.Cores=new CoreDefinition[Count(r)]; for(int i=0;i<c.Cores.Length;i++) c.Cores[i]=new CoreDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Position=Point(r), Hp=r.ReadInt32() };
                c.Outposts=new OutpostDefinition[Count(r)]; for(int i=0;i<c.Outposts.Length;i++) c.Outposts[i]=new OutpostDefinition { Id=r.ReadUInt32(), Position=Point(r), OwnerFactionId=r.ReadUInt32() };
                if (schema >= 3)
                {
                    c.ResourceNodes=new ResourceNodeDefinition[Count(r)]; for(int i=0;i<c.ResourceNodes.Length;i++) c.ResourceNodes[i]=new ResourceNodeDefinition { Id=r.ReadUInt32(), Kind=(ResourceKind)r.ReadByte(), Position=Point(r), Amount=r.ReadInt32() };
                    c.Economy=new EconomyRules { Enabled=Bool(r) };
                    if (c.Economy.Enabled)
                    {
                        var e=c.Economy;
                        e.StartFood=r.ReadInt32(); e.StartWood=r.ReadInt32(); e.PopulationCap=r.ReadInt32(); e.VillagerHp=r.ReadInt32(); e.VillagerSpeed=Fix(r);
                        e.CarryCapacity=r.ReadInt32(); e.GatherIntervalTicks=r.ReadInt32(); e.VillagerFoodCost=r.ReadInt32(); e.VillagerTrainTicks=r.ReadInt32();
                        e.QueueLimit=r.ReadInt32(); e.AutoVillagerTarget=r.ReadInt32(); e.DropOffMargin=Fix(r);
                        e.BarracksSizeCells=r.ReadInt32(); e.BarracksWoodCost=r.ReadInt32(); e.BarracksWork=r.ReadInt32(); e.BarracksHp=r.ReadInt32(); e.Builders=r.ReadInt32();
                        e.InfantryFoodCost=r.ReadInt32(); e.InfantryWoodCost=r.ReadInt32(); e.InfantryTrainTicks=r.ReadInt32(); e.AutoInfantryQueue=r.ReadInt32();
                        c.Villagers=new VillagerDefinition[Count(r)]; for(int i=0;i<c.Villagers.Length;i++) c.Villagers[i]=new VillagerDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Position=Point(r) };
                    }
                }
                if (schema >= 4)
                {
                    // v4 is written only for industry, which needs the economy the v3 block just read.
                    if (!c.Economy.Enabled) throw new InvalidDataException("Industry without an economy.");
                    var e=c.Economy;
                    e.Industry=true; e.BeltWoodCost=r.ReadInt32(); e.BeltTicksPerCell=r.ReadInt32(); e.BeltHp=r.ReadInt32(); e.BeltLimit=r.ReadInt32();
                    e.MineSizeCells=r.ReadInt32(); e.MineWoodCost=r.ReadInt32(); e.MineWork=r.ReadInt32(); e.MineHp=r.ReadInt32(); e.MineIntervalTicks=r.ReadInt32();
                    e.SmelterSizeCells=r.ReadInt32(); e.SmelterWoodCost=r.ReadInt32(); e.SmelterWork=r.ReadInt32(); e.SmelterHp=r.ReadInt32(); e.SmeltTicks=r.ReadInt32(); e.OrePerMetal=r.ReadInt32();
                    e.BufferLimit=r.ReadInt32(); e.InfantryMetalCost=r.ReadInt32();
                    c.Belts=new BeltDefinition[Count(r)]; for(int i=0;i<c.Belts.Length;i++) c.Belts[i]=new BeltDefinition { Cell=r.ReadInt32(), FactionId=r.ReadUInt32(), Facing=(Facing)r.ReadByte(), Item=(ResourceKind)r.ReadByte() };
                }
                if (schema >= 5)
                {
                    int n=Count(r); var terrain=r.ReadBytes(n); if(terrain.Length!=n) throw new EndOfStreamException();
                    c.Map.Terrain=terrain;
                    var e=c.Economy;
                    e.Ages=Bool(r); e.AdvanceFoodCost=r.ReadInt32(); e.AdvanceWoodCost=r.ReadInt32(); e.AdvanceTicks=r.ReadInt32();
                    e.AgrarianInfantryFood=r.ReadInt32(); e.AgrarianInfantryWood=r.ReadInt32(); e.AgrarianInfantryTicks=r.ReadInt32(); e.ForgedInfantryHp=r.ReadInt32(); e.ForgedInfantryDamage=r.ReadInt32();
                    e.FarmSizeCells=r.ReadInt32(); e.FarmWoodCost=r.ReadInt32(); e.FarmWork=r.ReadInt32(); e.FarmHp=r.ReadInt32();
                    e.FarmBaseTicks=r.ReadInt32(); e.FarmStepTicks=r.ReadInt32(); e.FarmMinTicks=r.ReadInt32(); e.FarmFoodReach=r.ReadInt32(); e.FarmRiverReach=r.ReadInt32();
                    e.ScoutFoodCost=r.ReadInt32(); e.ScoutWoodCost=r.ReadInt32(); e.ScoutTrainTicks=r.ReadInt32();
                    e.BasePopulation=r.ReadInt32(); e.HousePopulation=r.ReadInt32(); e.HouseSizeCells=r.ReadInt32(); e.HouseWoodCost=r.ReadInt32(); e.HouseWork=r.ReadInt32(); e.HouseHp=r.ReadInt32();
                    e.DropSiteSizeCells=r.ReadInt32(); e.DropSiteWoodCost=r.ReadInt32(); e.DropSiteWork=r.ReadInt32(); e.DropSiteHp=r.ReadInt32();
                    e.WallStoneCost=r.ReadInt32(); e.WallHp=r.ReadInt32(); e.WallReach=r.ReadInt32(); e.StartStone=r.ReadInt32(); e.StartMetal=r.ReadInt32();
                    e.TowerSizeCells=r.ReadInt32(); e.TowerWoodCost=r.ReadInt32(); e.TowerStoneCost=r.ReadInt32(); e.TowerWork=r.ReadInt32(); e.TowerHp=r.ReadInt32();
                    e.TowerRange=r.ReadInt32(); e.TowerVision=r.ReadInt32(); e.TowerDamage=r.ReadInt32(); e.TowerIntervalTicks=r.ReadInt32();
                    e.BlacksmithSizeCells=r.ReadInt32(); e.BlacksmithWoodCost=r.ReadInt32(); e.BlacksmithWork=r.ReadInt32(); e.BlacksmithHp=r.ReadInt32();
                    var defaults = new EconomyRules();
                    e.TechFood=defaults.TechFood; e.TechWood=defaults.TechWood; e.TechTicks=defaults.TechTicks; e.TechMetal=defaults.TechMetal; e.TechGems=defaults.TechGems;
                    for (int t = 0; t < 11; t++) { e.TechFood[t]=r.ReadInt32(); e.TechWood[t]=r.ReadInt32(); e.TechTicks[t]=r.ReadInt32(); e.TechMetal[t]=r.ReadInt32(); }
                    e.WeaponsDamage=r.ReadInt32(); e.ArmourHp=r.ReadInt32(); e.ToolsGatherTicks=r.ReadInt32(); e.CartsCarry=r.ReadInt32(); e.IrrigationTicks=r.ReadInt32(); e.BlastFurnaceTicks=r.ReadInt32();
                    e.Age2FoodCost=r.ReadInt32(); e.Age2WoodCost=r.ReadInt32(); e.Age2Ticks=r.ReadInt32(); e.Age2PopulationBonus=r.ReadInt32();
                    e.ArcherFood=r.ReadInt32(); e.ArcherWood=r.ReadInt32(); e.ArcherTicks=r.ReadInt32(); e.ArcherHp=r.ReadInt32(); e.ArcherDamage=r.ReadInt32(); e.ArcherInterval=r.ReadInt32();
                    e.ArcherRange=Fix(r); e.ArcherSpeed=Fix(r); e.ArcherVision=Fix(r);
                    e.CavalryFood=r.ReadInt32(); e.CavalryWood=r.ReadInt32(); e.CavalryMetal=r.ReadInt32(); e.CavalryTicks=r.ReadInt32(); e.CavalryHp=r.ReadInt32(); e.CavalryDamage=r.ReadInt32(); e.CavalryInterval=r.ReadInt32();
                    e.CavalryRange=Fix(r); e.CavalrySpeed=Fix(r); e.CavalryVision=Fix(r);
                    e.MarketSizeCells=r.ReadInt32(); e.MarketWoodCost=r.ReadInt32(); e.MarketWork=r.ReadInt32(); e.MarketHp=r.ReadInt32(); e.TradeLot=r.ReadInt32(); e.TradeReturn=r.ReadInt32();
                    e.WorkshopSizeCells=r.ReadInt32(); e.WorkshopWoodCost=r.ReadInt32(); e.WorkshopWork=r.ReadInt32(); e.WorkshopHp=r.ReadInt32();
                    e.RamFood=r.ReadInt32(); e.RamWood=r.ReadInt32(); e.RamTicks=r.ReadInt32(); e.RamHp=r.ReadInt32(); e.RamDamage=r.ReadInt32(); e.RamSiegeDamage=r.ReadInt32(); e.RamInterval=r.ReadInt32();
                    e.RamRange=Fix(r); e.RamSpeed=Fix(r); e.RamVision=Fix(r);
                    e.Age3FoodCost=r.ReadInt32(); e.Age3WoodCost=r.ReadInt32(); e.Age3Ticks=r.ReadInt32(); e.Age3PopulationBonus=r.ReadInt32();
                    e.SiegecraftSiegeDamage=r.ReadInt32(); e.MasonryTowerDamage=r.ReadInt32(); e.BankingTradeReturn=r.ReadInt32();
                    e.RangeSizeCells=r.ReadInt32(); e.RangeWoodCost=r.ReadInt32(); e.RangeWork=r.ReadInt32(); e.RangeHp=r.ReadInt32();
                    e.StableSizeCells=r.ReadInt32(); e.StableWoodCost=r.ReadInt32(); e.StableWork=r.ReadInt32(); e.StableHp=r.ReadInt32();
                    e.CounterBonusPermille=r.ReadInt32(); e.SteelWeaponsDamage=r.ReadInt32(); e.SteelArmourHp=r.ReadInt32();
                    e.RepairHpPerTick=r.ReadInt32(); e.RepairAtPermille=r.ReadInt32();
                    e.CastleSizeCells=r.ReadInt32(); e.CastleWoodCost=r.ReadInt32(); e.CastleStoneCost=r.ReadInt32(); e.CastleWork=r.ReadInt32(); e.CastleHp=r.ReadInt32();
                    e.CastleRange=r.ReadInt32(); e.CastleVision=r.ReadInt32(); e.CastleDamage=r.ReadInt32(); e.CastleIntervalTicks=r.ReadInt32();
                    e.TradeRouteWood=r.ReadInt32(); e.TradeRouteMin=r.ReadInt32();
                    if (s.Position < s.Length)
                    {
                        e.GemsTradeReturn=r.ReadInt32(); e.GemArmorHp=r.ReadInt32();
                        e.TechFood[11]=r.ReadInt32(); e.TechWood[11]=r.ReadInt32(); e.TechTicks[11]=r.ReadInt32(); e.TechMetal[11]=r.ReadInt32();
                        e.TechGems=new int[12]; for (int t = 0; t < 12; t++) e.TechGems[t]=r.ReadInt32();
                        if (s.Position < s.Length) { e.MercenaryGems=r.ReadInt32(); e.MercenaryTicks=r.ReadInt32(); e.MercenaryHp=r.ReadInt32(); e.MercenaryDamage=r.ReadInt32(); e.MercenaryInterval=r.ReadInt32(); }
                    }
                    if (s.Position < s.Length) { e.AgeVictoryEnabled=Bool(r); e.AgeVictoryTicks=r.ReadInt32(); }
                }
                if (s.Position < s.Length && !NextIsExtension(r))
                {
                    var e = c.Economy;
                    e.MonksEnabled = Bool(r); e.ConversionTicks = r.ReadInt32(); e.MonkFoodCost = r.ReadInt32();
                    e.MonkGoldCost = r.ReadInt32(); e.MonkTrainTicks = r.ReadInt32();
                    if (s.Position < s.Length)
                    {
                        e.Age2SaveArmyFloor = r.ReadInt32();
                        if (s.Position < s.Length)
                        {
                            e.FishingEnabled = Bool(r); e.FishRegrowTicks = r.ReadInt32();
                            e.FishAgrarianBonusPermille = r.ReadInt32(); e.FishReach = r.ReadInt32();
                            if (s.Position < s.Length)
                            {
                                e.GoldEnabled = Bool(r); e.Age3GoldCostAgrarian = r.ReadInt32(); e.Age3GoldCostMetallurgy = r.ReadInt32();
                                e.GoldGatherers = r.ReadInt32();
                                e.GoldAmount = r.ReadInt32(); e.GoldDangerMeters = r.ReadInt32();
                                if (s.Position < s.Length)
                                {
                                    long tailStart = s.Position;
                                    int marker = r.ReadInt32();
                                    if (marker == ExtensionMarker) s.Position = tailStart;
                                    else if (IsTailMarker(marker)) { ReadMarkedTail(r, e, marker); ReadMarkedTails(r, e); }
                                    else
                                    {
                                        s.Position = tailStart;
                                        e.ProcessingChain = true;
                                        e.CharcoalKilnSizeCells = r.ReadInt32(); e.CharcoalKilnWoodCost = r.ReadInt32(); e.CharcoalKilnWork = r.ReadInt32();
                                        e.CharcoalKilnHp = r.ReadInt32(); e.CharcoalTicks = r.ReadInt32();
                                        e.SteelworksSizeCells = r.ReadInt32(); e.SteelworksWoodCost = r.ReadInt32(); e.SteelworksWork = r.ReadInt32();
                                        e.SteelworksHp = r.ReadInt32(); e.SteelTicks = r.ReadInt32();
                                        e.HeavyInfantryFoodCost = r.ReadInt32(); e.HeavyInfantryWoodCost = r.ReadInt32(); e.HeavyInfantrySteelCost = r.ReadInt32();
                                        e.HeavyInfantryTrainTicks = r.ReadInt32(); e.HeavyInfantryHp = r.ReadInt32(); e.HeavyInfantryDamage = r.ReadInt32();
                                        e.HeavyInfantryAttackIntervalTicks = r.ReadInt32(); e.HeavyInfantrySpeed = Fix(r); e.HeavyInfantryVision = Fix(r); e.HeavyInfantryRange = Fix(r);
                                        ReadMarkedTails(r, e);
                                    }
                                }
                        }
                    }
                }
                }
                if (s.Position < s.Length) ReadExtensionSection(r, c);
                if(s.Position!=s.Length) throw new InvalidDataException("Trailing scenario data.");
                return new WorldState(c).Config;
            }
        }
        private static int Count(BinaryReader r) { uint n=r.ReadUInt32(); if(n>100000 || n>r.BaseStream.Length-r.BaseStream.Position) throw new InvalidDataException("Scenario count."); return (int)n; }
        private static bool Bool(BinaryReader r) { byte b=r.ReadByte(); if(b>1) throw new InvalidDataException("Boolean."); return b==1; }
        private static Fix64 Fix(BinaryReader r)=>Fix64.FromRaw(r.ReadInt64());
        private static SimPoint Point(BinaryReader r)=>new SimPoint(Fix(r),Fix(r));
        private static PolicyGoal Goal(BinaryReader r)=>new PolicyGoal((GoalKind)r.ReadByte(),r.ReadUInt32(),Point(r));
        private static void ReadForestryTail(BinaryReader r, EconomyRules e)
        {
            e.Forestry = Bool(r);
            e.LumberCampSizeCells = r.ReadInt32(); e.LumberCampWoodCost = r.ReadInt32(); e.LumberCampWork = r.ReadInt32();
            e.LumberCampHp = r.ReadInt32(); e.LumberCampIntervalTicks = r.ReadInt32();
            e.MarketFoodFloor = r.ReadInt32(); e.MarketWoodReserve = r.ReadInt32(); e.MarketStoneReserve = r.ReadInt32();
            if (r.BaseStream.Position < r.BaseStream.Length)
            {
                long next = r.BaseStream.Position;
                if (IsTailMarker(r.ReadInt32())) { r.BaseStream.Position = next; return; }
                r.BaseStream.Position = next;
                e.FletcherSizeCells = r.ReadInt32(); e.FletcherWoodCost = r.ReadInt32(); e.FletcherWork = r.ReadInt32(); e.FletcherHp = r.ReadInt32(); e.FletcherTicks = r.ReadInt32();
                e.FletcherWoodInput = r.ReadInt32(); e.FletcherFoodInput = r.ReadInt32();
                e.SkirmishArcherFoodCost = r.ReadInt32(); e.SkirmishArcherBowGearCost = r.ReadInt32(); e.SkirmishArcherTrainTicks = r.ReadInt32();
                e.SkirmishArcherHp = r.ReadInt32(); e.SkirmishArcherDamage = r.ReadInt32(); e.SkirmishArcherAttackIntervalTicks = r.ReadInt32();
                e.SkirmishArcherSpeed = Fix(r); e.SkirmishArcherVision = Fix(r); e.SkirmishArcherRange = Fix(r);
            }
        }
        private static void ReadMasonryTail(BinaryReader r, EconomyRules e)
        {
            e.Masonry = Bool(r);
            e.QuarrySizeCells = r.ReadInt32(); e.QuarryWoodCost = r.ReadInt32(); e.QuarryWork = r.ReadInt32();
            e.QuarryHp = r.ReadInt32(); e.QuarryIntervalTicks = r.ReadInt32();
            e.MarketFoodFloor = r.ReadInt32(); e.MarketWoodReserve = r.ReadInt32(); e.MarketStoneReserve = r.ReadInt32();
            // Older masonry tails ended here. Defaults keep those old records readable.
            if (r.BaseStream.Position < r.BaseStream.Length)
            {
                long next = r.BaseStream.Position;
                if (IsTailMarker(r.ReadInt32())) { r.BaseStream.Position = next; return; }
                r.BaseStream.Position = next;
                e.MasonryDefenceCostPermille = r.ReadInt32(); e.MasonryDefenceWorkPermille = r.ReadInt32();
            }
        }
        private static void ReadBridgeTail(BinaryReader r, EconomyRules e)
        {
            // The encoder always writes every bridge value, so they are read unconditionally (see ReadCavalryTail).
            e.Bridge = Bool(r);
            e.EngineerCampSizeCells = r.ReadInt32(); e.EngineerCampWoodCost = r.ReadInt32(); e.EngineerCampWork = r.ReadInt32(); e.EngineerCampHp = r.ReadInt32();
            e.BridgeWoodCost = r.ReadInt32(); e.BridgeWork = r.ReadInt32(); e.BridgeHp = r.ReadInt32(); e.MaxBridgeLength = r.ReadInt32();
            e.BridgeworksFoodCost = r.ReadInt32(); e.BridgeworksWoodCost = r.ReadInt32(); e.BridgeworksTicks = r.ReadInt32();
            e.BridgeworksHpBonus = r.ReadInt32(); e.BridgeworksWorkReduction = r.ReadInt32();
            e.SiegeDeploymentFoodCost = r.ReadInt32(); e.SiegeDeploymentWoodCost = r.ReadInt32(); e.SiegeDeploymentTicks = r.ReadInt32();
            e.SiegeDeploymentRamTicksReduction = r.ReadInt32(); e.SiegeDeploymentRamCapacityBonus = r.ReadInt32();
        }
        /// <summary>Every marked civilisation tail, in any order the encoder wrote them, until the end of the record.</summary>
        private static void ReadMarkedTails(BinaryReader r, EconomyRules e)
        {
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                long markerPosition = r.BaseStream.Position;
                int marker = r.ReadInt32();
                if (marker == ExtensionMarker) { r.BaseStream.Position = markerPosition; return; }
                ReadMarkedTail(r, e, marker);
            }
        }
        private static void ReadMarkedTail(BinaryReader r, EconomyRules e, int marker)
        {
            if (marker == ForestryTailMarker) ReadForestryTail(r, e);
            else if (marker == MasonryTailMarker) ReadMasonryTail(r, e);
            else if (marker == CaravanTailMarker) ReadCaravanTail(r, e);
            else if (marker == CavalryTailMarker) ReadCavalryTail(r, e);
            else if (marker == BridgeTailMarker) ReadBridgeTail(r, e);
            else throw new InvalidDataException("Invalid optional tail marker.");
        }
        private static bool IsTailMarker(int value)
            => value == ForestryTailMarker || value == MasonryTailMarker || value == CaravanTailMarker || value == CavalryTailMarker
                || value == BridgeTailMarker;

        private static void WriteExtensionSection(BinaryWriter w, ScenarioExtensionData[] extensions)
        {
            if (extensions == null) throw new InvalidDataException("Missing scenario extensions.");
            if (extensions.Length == 0) return;
            var byRegistration = new ScenarioExtensionData[ExtensionRegistrations.Length];
            for (int i = 0; i < extensions.Length; i++)
            {
                var extension = extensions[i];
                if (extension == null || extension.Data == null) throw new InvalidDataException("Invalid scenario extension.");
                int registrationIndex = FindExtensionRegistration(extension.Id);
                if (registrationIndex < 0) throw new InvalidDataException("Unknown scenario extension.");
                if (byRegistration[registrationIndex] != null) throw new InvalidDataException("Duplicate scenario extension.");
                var registration = ExtensionRegistrations[registrationIndex];
                if (extension.Version != registration.Version || extension.Data.Length != registration.DataLength)
                    throw new InvalidDataException("Invalid scenario extension version or length.");
                byRegistration[registrationIndex] = extension;
            }

            using (var payload = new MemoryStream())
            using (var payloadWriter = new BinaryWriter(payload))
            {
                for (int i = 0; i < byRegistration.Length; i++)
                {
                    var extension = byRegistration[i];
                    if (extension == null) continue;
                    payloadWriter.Write(extension.Id);
                    payloadWriter.Write(extension.Version);
                    payloadWriter.Write(extension.Data.Length);
                    payloadWriter.Write(extension.Data);
                }
                payloadWriter.Flush();
                if (payload.Length > int.MaxValue) throw new InvalidDataException("Scenario extension section is too large.");
                w.Write(ExtensionMarker);
                w.Write(ExtensionSchemaVersion);
                w.Write((int)payload.Length);
                w.Write(payload.ToArray());
            }
        }

        private static void ReadExtensionSection(BinaryReader r, ScenarioDefinition c)
        {
            int marker = ReadExtensionInt32(r);
            if (marker != ExtensionMarker) throw new InvalidDataException("Invalid scenario extension marker.");
            int version = ReadExtensionInt32(r);
            if (version != ExtensionSchemaVersion) throw new InvalidDataException("Unknown scenario extension schema.");
            int length = ReadExtensionInt32(r);
            long sectionEnd = checked(r.BaseStream.Position + (long)length);
            if (length < 0 || sectionEnd > r.BaseStream.Length) throw new InvalidDataException("Scenario extension length.");

            var seen = new bool[ExtensionRegistrations.Length];
            var extensions = new List<ScenarioExtensionData>();
            while (r.BaseStream.Position < sectionEnd)
            {
                if (sectionEnd - r.BaseStream.Position < 3L * sizeof(int))
                    throw new InvalidDataException("Truncated scenario extension record.");
                int id = ReadExtensionInt32(r), featureVersion = ReadExtensionInt32(r), dataLength = ReadExtensionInt32(r);
                int registrationIndex = FindExtensionRegistration(id);
                if (registrationIndex < 0) throw new InvalidDataException("Unknown scenario extension.");
                if (seen[registrationIndex]) throw new InvalidDataException("Duplicate scenario extension.");
                var registration = ExtensionRegistrations[registrationIndex];
                if (featureVersion != registration.Version || dataLength != registration.DataLength || dataLength < 0
                    || dataLength > sectionEnd - r.BaseStream.Position)
                    throw new InvalidDataException("Invalid scenario extension version or length.");
                var data = r.ReadBytes(dataLength);
                if (data.Length != dataLength) throw new InvalidDataException("Truncated scenario extension data.");
                seen[registrationIndex] = true;
                var extension = new ScenarioExtensionData { Id = id, Version = featureVersion, Data = data };
                 if (id == AcademyExtensionId) ReadAcademyExtension(extension, c.Economy);
                 else if (id == CultExtensionId) ReadCultExtension(extension, c.Economy);
                 else if (id == MountainExtensionId) ReadMountainExtension(extension, c.Economy);
                 else if (id == TollgateExtensionId) ReadTollgateExtension(extension, c.Economy);
                extensions.Add(extension);
            }
            if (r.BaseStream.Position != sectionEnd) throw new InvalidDataException("Scenario extension length mismatch.");
            c.Extensions = extensions.ToArray();
        }

        private static int ReadExtensionInt32(BinaryReader r)
        {
            if (r.BaseStream.Length - r.BaseStream.Position < sizeof(int)) throw new InvalidDataException("Truncated scenario extension.");
            return r.ReadInt32();
        }

        private static bool NextIsExtension(BinaryReader r)
        {
            if (r.BaseStream.Length - r.BaseStream.Position < sizeof(int)) return false;
            long position = r.BaseStream.Position;
            bool result = r.ReadInt32() == ExtensionMarker;
            r.BaseStream.Position = position;
            return result;
        }

        private static int FindExtensionRegistration(int id)
        {
            for (int i = 0; i < ExtensionRegistrations.Length; i++)
                if (ExtensionRegistrations[i].Id == id) return i;
            return -1;
        }

        private static ScenarioExtensionData[] ExtensionsForEncode(ScenarioDefinition c)
        {
            if (c.Extensions == null) throw new InvalidDataException("Missing scenario extensions.");
            var extensions = new List<ScenarioExtensionData>(c.Extensions);
            bool hasAcademy = false;
            for (int i = 0; i < extensions.Count; i++) if (extensions[i] != null && extensions[i].Id == AcademyExtensionId) hasAcademy = true;
            if (c.Economy.Academy && !hasAcademy) extensions.Add(CreateAcademyExtension(c.Economy));
            bool hasCult = false;
            for (int i = 0; i < extensions.Count; i++) if (extensions[i] != null && extensions[i].Id == CultExtensionId) hasCult = true;
            if (c.Economy.Cult && !hasCult) extensions.Add(CreateCultExtension(c.Economy));
            bool hasMountain = false;
            for (int i = 0; i < extensions.Count; i++) if (extensions[i] != null && extensions[i].Id == MountainExtensionId) hasMountain = true;
            if (c.Economy.Mountain && !hasMountain) extensions.Add(CreateMountainExtension(c.Economy));
            bool hasTollgate = false;
            for (int i = 0; i < extensions.Count; i++) if (extensions[i] != null && extensions[i].Id == TollgateExtensionId) hasTollgate = true;
            if (c.Economy.Tollgate && !hasTollgate) extensions.Add(CreateTollgateExtension(c.Economy));
            return extensions.ToArray();
        }

        private static ScenarioExtensionData CreateAcademyExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Academy ? 1 : 0);
                writer.Write(e.AcademySizeCells); writer.Write(e.AcademyWoodCost); writer.Write(e.AcademyWork); writer.Write(e.AcademyHp);
                writer.Write(e.AcademyToolsFoodCost); writer.Write(e.AcademyToolsWoodCost); writer.Write(e.AcademyToolsGoldCost); writer.Write(e.AcademyToolsTicks);
                writer.Write(e.AcademyCartsFoodCost); writer.Write(e.AcademyCartsWoodCost); writer.Write(e.AcademyCartsGoldCost); writer.Write(e.AcademyCartsTicks);
                return new ScenarioExtensionData { Id = AcademyExtensionId, Version = AcademyExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAcademyExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid academy flag.");
                e.Academy = enabled != 0;
                e.AcademySizeCells = reader.ReadInt32(); e.AcademyWoodCost = reader.ReadInt32(); e.AcademyWork = reader.ReadInt32(); e.AcademyHp = reader.ReadInt32();
                e.AcademyToolsFoodCost = reader.ReadInt32(); e.AcademyToolsWoodCost = reader.ReadInt32(); e.AcademyToolsGoldCost = reader.ReadInt32(); e.AcademyToolsTicks = reader.ReadInt32();
                e.AcademyCartsFoodCost = reader.ReadInt32(); e.AcademyCartsWoodCost = reader.ReadInt32(); e.AcademyCartsGoldCost = reader.ReadInt32(); e.AcademyCartsTicks = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing academy extension data.");
            }
        }

        private static ScenarioExtensionData CreateCultExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Cult ? 1 : 0);
                writer.Write(e.MonasterySizeCells); writer.Write(e.MonasteryWoodCost); writer.Write(e.MonasteryWork); writer.Write(e.MonasteryHp);
                writer.Write(e.MonasteryMonkFoodCost); writer.Write(e.MonasteryMonkWoodCost);
                return new ScenarioExtensionData { Id = CultExtensionId, Version = CultExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCultExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid cult flag.");
                e.Cult = enabled != 0;
                e.MonasterySizeCells = reader.ReadInt32(); e.MonasteryWoodCost = reader.ReadInt32(); e.MonasteryWork = reader.ReadInt32(); e.MonasteryHp = reader.ReadInt32();
                e.MonasteryMonkFoodCost = reader.ReadInt32(); e.MonasteryMonkWoodCost = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing cult extension data.");
            }
        }

        private static ScenarioExtensionData CreateMountainExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Mountain ? 1 : 0);
                writer.Write(e.MountainSizeCells); writer.Write(e.MountainWoodCost); writer.Write(e.MountainWork); writer.Write(e.MountainHp);
                writer.Write(e.MountainBaseIntervalTicks); writer.Write(e.MountainIntervalStepTicks); writer.Write(e.MountainMinIntervalTicks);
                writer.Write(e.MountainMaxBuildings); writer.Write(e.MountainMaxAdjacentCells);
                writer.Write(e.MountainStoneYield); writer.Write(e.MountainOreYield);
                return new ScenarioExtensionData { Id = MountainExtensionId, Version = MountainExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadMountainExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid mountain flag.");
                e.Mountain = enabled != 0;
                e.MountainSizeCells = reader.ReadInt32(); e.MountainWoodCost = reader.ReadInt32(); e.MountainWork = reader.ReadInt32(); e.MountainHp = reader.ReadInt32();
                e.MountainBaseIntervalTicks = reader.ReadInt32(); e.MountainIntervalStepTicks = reader.ReadInt32(); e.MountainMinIntervalTicks = reader.ReadInt32();
                e.MountainMaxBuildings = reader.ReadInt32(); e.MountainMaxAdjacentCells = reader.ReadInt32();
                e.MountainStoneYield = reader.ReadInt32(); e.MountainOreYield = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing mountain extension data.");
            }
        }
        private static ScenarioExtensionData CreateTollgateExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Tollgate ? 1 : 0);
                writer.Write(e.TollgateLengthCells); writer.Write(e.TollgateWoodCost); writer.Write(e.TollgateStoneCost);
                writer.Write(e.TollgateWork); writer.Write(e.TollgateHp); writer.Write(e.TollgateMaxBuildings);
                return new ScenarioExtensionData { Id = TollgateExtensionId, Version = TollgateExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadTollgateExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid tollgate flag.");
                e.Tollgate = enabled != 0;
                e.TollgateLengthCells = reader.ReadInt32(); e.TollgateWoodCost = reader.ReadInt32(); e.TollgateStoneCost = reader.ReadInt32();
                e.TollgateWork = reader.ReadInt32(); e.TollgateHp = reader.ReadInt32(); e.TollgateMaxBuildings = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing tollgate extension data.");
            }
        }
        private static void ReadCaravanTail(BinaryReader r, EconomyRules e)
        {
            e.Caravan = Bool(r);
            e.CaravanseraiSizeCells = r.ReadInt32(); e.CaravanseraiWoodCost = r.ReadInt32(); e.CaravanseraiWork = r.ReadInt32();
            e.CaravanseraiHp = r.ReadInt32(); e.CaravanOutpostReach = r.ReadInt32(); e.CaravanMinimumDistance = r.ReadInt32();
            e.CaravanRewardDistanceStep = r.ReadInt32(); e.CaravanRewardMaxWood = r.ReadInt32(); e.CaravanAutoVillagers = r.ReadInt32();
        }
        private static void ReadCavalryTail(BinaryReader r, EconomyRules e)
        {
            // The encoder always writes the drill values, so they are read unconditionally: a "bytes remain" test here
            // would swallow the marker of a later tail.
            e.Cavalry = Bool(r);
            e.LightCavalryFood = r.ReadInt32(); e.LightCavalryWood = r.ReadInt32(); e.LightCavalryTicks = r.ReadInt32();
            e.LightCavalryHp = r.ReadInt32(); e.LightCavalryDamage = r.ReadInt32(); e.LightCavalryInterval = r.ReadInt32();
            e.LightCavalryRange = Fix(r); e.LightCavalrySpeed = Fix(r); e.LightCavalryVision = Fix(r);
            e.CavalryDrillFood = r.ReadInt32(); e.CavalryDrillWood = r.ReadInt32(); e.CavalryDrillTicks = r.ReadInt32();
            e.CavalryDrillSpeed = Fix(r);
        }
        private static void Point(BinaryWriter w,SimPoint p) { w.Write(p.X.Raw); w.Write(p.Z.Raw); }
        private static void Goal(BinaryWriter w,PolicyGoal g) { w.Write((byte)g.Kind); w.Write(g.Id); Point(w,g.Point); }
        private static void Text(BinaryWriter w,string v) { var b=Encoding.UTF8.GetBytes(v); if(b.Length>1048576) throw new InvalidDataException("String size."); w.Write((uint)b.Length); w.Write(b); }
        private static string Text(BinaryReader r) { uint n=r.ReadUInt32(); if(n>1048576) throw new InvalidDataException("String size."); var b=r.ReadBytes((int)n); if(b.Length!=n) throw new EndOfStreamException(); return new UTF8Encoding(false,true).GetString(b); }
    }
}
