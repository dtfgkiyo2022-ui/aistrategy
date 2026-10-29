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
        private const int BeltComponentsTailMarker = 0x424C5432; // "BLT2", after the optional economy tails.
        private const int LineRebuildTailMarker = 0x4C524244; // "LRBD", after the optional economy tails.
        private const int ExtensionMarker = 0x4E545845; // "EXTN", after every existing civilisation tail.
        private const int ExtensionSchemaVersion = 1;

        // Keep this an ordered array. Adding the eighth civilisation's record is one row here; neither reflection nor
        // dictionary enumeration may determine the binary order. ID 1 is deliberately a test-only integer payload.
        private const int AcademyExtensionId = 2;
        private const int AcademyExtensionVersion = 1;
        private const int AcademyExtensionDataLength = 13 * sizeof(int);
        private const int CultExtensionId = 3;
        private const int CultExtensionV1 = 1;
        private const int CultExtensionVersion = 2;
        private const int CultExtensionV1DataLength = 7 * sizeof(int);
        private const int CultExtensionDataLength = 14 * sizeof(int);
        private const int FishingCivExtensionId = 4;
        private const int FishingCivExtensionV1 = 1;
        private const int FishingCivExtensionVersion = 2;
        private const int FishingCivExtensionV1DataLength = 5 * sizeof(int);
        private const int FishingCivExtensionDataLength = 13 * sizeof(int);
        private const int MountainExtensionId = 5;
        private const int MountainExtensionV1 = 1;
        private const int MountainExtensionVersion = 2;
        private const int MountainExtensionV1DataLength = 12 * sizeof(int);
        private const int MountainExtensionDataLength = 22 * sizeof(int);
        private const int TollgateExtensionId = 6;
        private const int TollgateExtensionV1 = 1;
        private const int TollgateExtensionVersion = 2;
        private const int TollgateExtensionV1DataLength = 7 * sizeof(int);
        private const int TollgateExtensionDataLength = 22 * sizeof(int);
        private const int MetropolisExtensionId = 7;
        private const int MetropolisExtensionV1 = 1;
        private const int MetropolisExtensionVersion = 2;
        private const int MetropolisExtensionV1DataLength = 5 * sizeof(int);
        private const int MetropolisExtensionDataLength = 16 * sizeof(int);
        private const int SanctuaryExtensionId = 8;
        private const int SanctuaryExtensionV1 = 1;
        private const int SanctuaryExtensionVersion = 2;
        private const int SanctuaryExtensionV1DataLength = 9 * sizeof(int);
        private const int SanctuaryExtensionDataLength = 20 * sizeof(int);
        private const int CoreDefenceExtensionId = 9;
        private const int CoreDefenceExtensionVersion = 1;
        private const int CoreDefenceExtensionDataLength = 5 * sizeof(int);
        private const int ArmyGrowthExtensionId = 10;
        private const int ArmyGrowthExtensionVersion = 1;
        private const int ArmyGrowthExtensionDataLength = sizeof(int);
        private const int TownExtensionId = 11;
        private const int TownExtensionVersion = 1;
        private const int TownExtensionDataLength = 9 * sizeof(int);
        private const int EconomyScaleExtensionId = 12;
        private const int EconomyScaleExtensionVersion = 1;
        private const int EconomyScaleExtensionDataLength = sizeof(int);
        private const int RegionExtensionId = 13;
        private const int RegionExtensionVersion = 1;
        private const int RegionExtensionDataLength = sizeof(int);
        private const int LatePushExtensionId = 14;
        private const int LatePushExtensionVersion = 1;
        private const int LatePushExtensionDataLength = 4 * sizeof(int);
        private const int EarlyArmsExtensionId = 15;
        private const int EarlyArmsExtensionVersion = 1;
        private const int EarlyArmsExtensionDataLength = 10 * sizeof(int);
        private const int AgeClockExtensionId = 16;
        private const int AgeClockExtensionVersion = 1;
        private const int AgeClockExtensionDataLength = 4 * sizeof(int);
        private const int AgeClockUxExtensionId = 44;
        private const int AgeClockUxExtensionVersion = 1;
        private const int AgeClockUxExtensionDataLength = 2 * sizeof(int);
        private const int DirectPathMovementExtensionId = 45;
        private const int DirectPathMovementExtensionVersion = 1;
        private const int DirectPathMovementExtensionDataLength = sizeof(int);
        private const int CommonDrillExtensionId = 47;
        private const int CommonDrillExtensionVersion = 1;
        private const int CommonDrillExtensionDataLength = 7 * sizeof(int);
        private const int InserterExtensionId = 48;
        private const int InserterExtensionVersion = 1;
        private const int InserterExtensionDataLength = 4 * sizeof(int);
        private const int EasyRebuildExtensionId = 49;
        private const int EasyRebuildExtensionVersion = 1;
        private const int EasyRebuildExtensionDataLength = 2 * sizeof(int);
        private const int ExploreScoutingExtensionId = 50;
        private const int ExploreScoutingExtensionV1 = 1;
        private const int ExploreScoutingExtensionVersion = 2;
        private const int ExploreScoutingExtensionV1DataLength = 7 * sizeof(int);
        private const int ExploreScoutingExtensionDataLength = 10 * sizeof(int);
        private const int BuildingReservationsExtensionId = 51;
        private const int BuildingReservationsExtensionVersion = 1;
        private const int BuildingReservationsExtensionDataLength = 3 * sizeof(int);
        private const int AiArmyRecoveryExtensionId = 52;
        private const int AiArmyRecoveryExtensionVersion = 1;
        private const int AiArmyRecoveryExtensionDataLength = sizeof(int);
        private const int LineBuildingCheckExtensionId = 54;
        private const int LineBuildingCheckExtensionVersion = 1;
        private const int LineBuildingCheckExtensionDataLength = sizeof(int);
        private const int NoNegativeStockExtensionId = 55;
        private const int NoNegativeStockExtensionVersion = 1;
        private const int NoNegativeStockExtensionDataLength = sizeof(int);
        private const int AutoReturnAfterHumanExtensionId = 53;
        private const int AutoReturnAfterHumanExtensionVersion = 1;
        private const int AutoReturnAfterHumanExtensionDataLength = 3 * sizeof(int);
        private const int SiegeByAgeExtensionId = 17;
        private const int SiegeByAgeExtensionVersion = 1;
        private const int SiegeByAgeExtensionDataLength = 5 * sizeof(int);
        private const int PushWavesExtensionId = 18;
        private const int PushWavesExtensionVersion = 1;
        private const int PushWavesExtensionDataLength = 6 * sizeof(int);
        private const int ManualControlExtensionId = 19;
        private const int ManualControlExtensionVersion = 1;
        private const int ManualControlExtensionDataLength = sizeof(int);
        private const int RationsExtensionId = 20;
        private const int RationsExtensionVersion = 1;
        private const int RationsExtensionDataLength = 15 * sizeof(int);
        private const int BricksExtensionId = 21;
        private const int BricksExtensionVersion = 2;
        private const int BricksExtensionDataLength = 18 * sizeof(int);
        private const int BricksExtensionV1 = 1;
        private const int BricksExtensionV1DataLength = 14 * sizeof(int);
        private const int TackExtensionId = 22;
        private const int TackExtensionVersion = 2;
        private const int TackExtensionDataLength = 18 * sizeof(int) + 3 * sizeof(int);
        private const int TackExtensionV1 = 1;
        private const int TackExtensionV1DataLength = 17 * sizeof(int) + 3 * sizeof(int);
        private const int PlanksExtensionId = 23;
        private const int PlanksExtensionVersion = 1;
        private const int PlanksExtensionDataLength = 14 * sizeof(int);
        private const int BooksExtensionId = 24;
        private const int BooksExtensionVersion = 1;
        private const int BooksExtensionDataLength = 12 * sizeof(int);
        private const int PreservesExtensionId = 25;
        private const int PreservesExtensionVersion = 1;
        private const int PreservesExtensionDataLength = 11 * sizeof(int);
        private const int CivVarietyExtensionId = 26;
        private const int CivVarietyExtensionVersion = 1;
        private const int CivVarietyExtensionDataLength = 2 * sizeof(int);
        private const int CivVarietyCapsExtensionId = 35;
        private const int CivVarietyCapsExtensionVersion = 1;
        private const int CivVarietyCapsExtensionDataLength = 3 * sizeof(int);
        private const int JewelsExtensionId = 27;
        private const int JewelsExtensionVersion = 1;
        private const int JewelsExtensionDataLength = 14 * sizeof(int);
private const int TradeGoodsExtensionId = 28;
private const int TradeGoodsExtensionVersion = 1;
private const int TradeGoodsExtensionDataLength = 11 * sizeof(int);
private const int IncenseExtensionId = 29;
private const int IncenseExtensionVersion = 1;
private const int IncenseExtensionDataLength = 15 * sizeof(int);
private const int CharmsExtensionId = 30;
private const int CharmsExtensionVersion = 1;
private const int CharmsExtensionDataLength = 12 * sizeof(int);
private const int RepairKitsExtensionId = 31;
private const int RepairKitsExtensionVersion = 1;
private const int RepairKitsExtensionDataLength = 13 * sizeof(int);
private const int ToolsExtensionId = 32;
private const int ToolsExtensionVersion = 1;
private const int ToolsExtensionDataLength = 13 * sizeof(int);
private const int GoodsMarketExtensionId = 33;
private const int GoodsMarketExtensionVersion = 1;
private const int GoodsMarketExtensionDataLength = 17 * sizeof(int);
        private const int CraftTimingExtensionId = 34;
        private const int CraftTimingExtensionVersion = 1;
        private const int CraftTimingExtensionDataLength = 2 * sizeof(int);
        private const int AdjacentBuildingsExtensionId = 37;
        private const int AdjacentBuildingsExtensionVersion = 1;
        private const int AdjacentBuildingsExtensionDataLength = 2 * sizeof(int);
        private const int CraftBalanceExtensionId = 36;
        private const int CraftBalanceExtensionVersion = 1;
        private const int CraftBalanceExtensionDataLength = sizeof(int);
        private const int ResourceFogExtensionId = 39;
        private const int ResourceFogExtensionVersion = 1;
        private const int ResourceFogExtensionDataLength = sizeof(int);
private const int CraftTree1ExtensionId = 41;
private const int CraftTree1ExtensionVersion = 1;
private const int CraftTree1ExtensionDataLength = 22 * sizeof(int);
private const int PrimitiveCraftExtensionId = 66;
private const int PrimitiveCraftExtensionVersion = 1;
private const int PrimitiveCraftExtensionDataLength = 12 * sizeof(int);
private const int AiFactoryExtensionId = 67;
private const int AiFactoryExtensionVersion = 1;
private const int AiFactoryExtensionDataLength = 12 * sizeof(int);
private const int AiExpandExtensionId = 68;
private const int AiExpandExtensionVersion = 1;
        private const int AiExpandExtensionDataLength = 12 * sizeof(int);
        private const int OpenFarmsExtensionId = 69;
        private const int OpenFarmsExtensionVersion = 1;
        private const int OpenFarmsExtensionDataLength = 4 * sizeof(int);
         private const int FreeTownsExtensionId = 70;
         private const int FreeTownsExtensionVersion = 1;
         private const int FreeTownsExtensionDataLength = 5 * sizeof(int);
         private const int CraftRequiredExtensionId = 71;
         private const int CraftRequiredExtensionVersion = 1;
          private const int CraftRequiredExtensionDataLength = 18 * sizeof(int);
          private const int CraftRequiredExtensionV1DataLength = 6 * sizeof(int);
          private const int CraftRequiredExtensionV2DataLength = 7 * sizeof(int);
          private const int CraftRequiredExtensionV3DataLength = 16 * sizeof(int);
        private const int CraftDepthExtensionId = 72;
        private const int CraftDepthExtensionVersion = 1;
        private const int CraftDepthExtensionDataLength = 23 * sizeof(int);
        private const int EarlyStartExtensionId = 73;
        private const int EarlyStartExtensionVersion = 1;
        private const int EarlyStartExtensionDataLength = 4 * sizeof(int);
        private const int HouseFlowExtensionId = 74;
        private const int HouseFlowExtensionVersion = 1;
        private const int HouseFlowExtensionDataLength = 4 * sizeof(int);
        private const int FastMovementExtensionId = 75;
        private const int FastMovementExtensionVersion = 1;
        private const int FastMovementExtensionDataLength = 3 * sizeof(int);
        private const int BeltDropOffExtensionId = 76;
        private const int BeltDropOffExtensionV1 = 1;
        private const int BeltDropOffExtensionVersion = 2;
        private const int BeltDropOffExtensionV1DataLength = 3 * sizeof(int);
        private const int BeltDropOffExtensionDataLength = 10 * sizeof(int);
        private const int CraftFlowExtensionId = 77;
        private const int CraftFlowExtensionVersion = 1;
        private const int CraftFlowExtensionDataLength = sizeof(int);
        private const int ArmsScaleExtensionId = 78;
        private const int ArmsScaleExtensionVersion = 1;
        private const int ArmsScaleExtensionDataLength = 12 * sizeof(int);
        private const int BeltStackExtensionId = 79;
        private const int BeltStackExtensionVersion = 1;
        private const int BeltStackExtensionDataLength = 7 * sizeof(int);
        private const int BeltCraftExtensionId = 80;
         private const int BeltCraftExtensionV1 = 1;
         private const int BeltCraftExtensionVersion = 2;
         private const int BeltCraftExtensionV1DataLength = 20 * sizeof(int);
         private const int BeltCraftExtensionDataLength = 36 * sizeof(int);
private const int DropSiteLimitExtensionId = 38;
private const int DropSiteLimitExtensionVersion = 1;
private const int DropSiteLimitExtensionDataLength = 2 * sizeof(int);
private const int AiBeltsExtensionId = 40;
private const int AiBeltsExtensionVersion = 1;
private const int AiBeltsExtensionDataLength = 2 * sizeof(int);
private const int StorageFeedsProcessingExtensionId = 46;
private const int StorageFeedsProcessingExtensionVersion = 1;
private const int StorageFeedsProcessingExtensionDataLength = sizeof(int);
private const int CraftTree2ExtensionId = 42;
private const int CraftTree2ExtensionVersion = 1;
private const int CraftTree2ExtensionDataLength = 23 * sizeof(int);
private const int CraftTree3ExtensionId = 43;
private const int CraftTree3ExtensionV1 = 1;
private const int CraftTree3ExtensionVersion = 2;
private const int CraftTree3ExtensionV1DataLength = 18 * sizeof(int);
        private const int CraftTree3ExtensionDataLength = 19 * sizeof(int);
        private const int LargeEconomyExtensionId = 59;
        private const int LargeEconomyExtensionV1 = 1;
        private const int LargeEconomyExtensionV2 = 2;
        private const int LargeEconomyExtensionV3 = 3;
        private const int LargeEconomyExtensionVersion = 7;
        private const int LargeEconomyExtensionV1DataLength = 15 * sizeof(int);
        private const int LargeEconomyExtensionV2DataLength = 18 * sizeof(int);
        private const int LargeEconomyExtensionV3DataLength = 21 * sizeof(int);
        private const int LargeEconomyExtensionV4DataLength = 26 * sizeof(int);
        private const int LargeEconomyExtensionV5DataLength = 27 * sizeof(int);
        private const int LargeEconomyExtensionV6DataLength = 33 * sizeof(int);
        // Version 7 (10-21) adds the large-map scouting fields after version 6.
        private const int LargeEconomyExtensionDataLength = 36 * sizeof(int);
        private const int AgeFourExtensionId = 61;
        private const int AgeFourExtensionVersion = 1;
        private const int AgeFourExtensionDataLength = 15 * sizeof(int);
        private const int AgeStrengthsExtensionId = 62;
        private const int AgeStrengthsExtensionVersion = 2;
        private const int AgeStrengthsExtensionDataLength = 43 * sizeof(int);
        private const int CraftTree4ExtensionId = 63;
        private const int CraftTree4ExtensionVersion = 1;
        private const int CraftTree4ExtensionDataLength = 20 * sizeof(int) + 3 * sizeof(long);
        private const int AgeFourAiExtensionId = 64;
        private const int AgeFourAiExtensionVersion = 1;
        private const int AgeFourAiExtensionDataLength = 3 * sizeof(int);
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
            new ExtensionRegistration(FishingCivExtensionId, FishingCivExtensionVersion, FishingCivExtensionDataLength),
            new ExtensionRegistration(MountainExtensionId, MountainExtensionVersion, MountainExtensionDataLength),
            new ExtensionRegistration(TollgateExtensionId, TollgateExtensionVersion, TollgateExtensionDataLength),
            new ExtensionRegistration(MetropolisExtensionId, MetropolisExtensionVersion, MetropolisExtensionDataLength),
            new ExtensionRegistration(SanctuaryExtensionId, SanctuaryExtensionVersion, SanctuaryExtensionDataLength),
            new ExtensionRegistration(CoreDefenceExtensionId, CoreDefenceExtensionVersion, CoreDefenceExtensionDataLength),
            new ExtensionRegistration(ArmyGrowthExtensionId, ArmyGrowthExtensionVersion, ArmyGrowthExtensionDataLength),
            new ExtensionRegistration(TownExtensionId, TownExtensionVersion, TownExtensionDataLength),
            new ExtensionRegistration(EconomyScaleExtensionId, EconomyScaleExtensionVersion, EconomyScaleExtensionDataLength),
            new ExtensionRegistration(RegionExtensionId, RegionExtensionVersion, RegionExtensionDataLength),
            new ExtensionRegistration(LatePushExtensionId, LatePushExtensionVersion, LatePushExtensionDataLength),
            new ExtensionRegistration(EarlyArmsExtensionId, EarlyArmsExtensionVersion, EarlyArmsExtensionDataLength),
            new ExtensionRegistration(AgeClockExtensionId, AgeClockExtensionVersion, AgeClockExtensionDataLength),
            new ExtensionRegistration(SiegeByAgeExtensionId, SiegeByAgeExtensionVersion, SiegeByAgeExtensionDataLength),
            new ExtensionRegistration(PushWavesExtensionId, PushWavesExtensionVersion, PushWavesExtensionDataLength),
            new ExtensionRegistration(ManualControlExtensionId, ManualControlExtensionVersion, ManualControlExtensionDataLength),
            new ExtensionRegistration(RationsExtensionId, RationsExtensionVersion, RationsExtensionDataLength)
            ,new ExtensionRegistration(BricksExtensionId, BricksExtensionVersion, BricksExtensionDataLength)
            ,new ExtensionRegistration(TackExtensionId, TackExtensionVersion, TackExtensionDataLength)
            ,new ExtensionRegistration(PlanksExtensionId, PlanksExtensionVersion, PlanksExtensionDataLength)
            ,new ExtensionRegistration(BooksExtensionId, BooksExtensionVersion, BooksExtensionDataLength)
            ,new ExtensionRegistration(PreservesExtensionId, PreservesExtensionVersion, PreservesExtensionDataLength)
            ,new ExtensionRegistration(CivVarietyExtensionId, CivVarietyExtensionVersion, CivVarietyExtensionDataLength)
            ,new ExtensionRegistration(CivVarietyCapsExtensionId, CivVarietyCapsExtensionVersion, CivVarietyCapsExtensionDataLength)
            ,new ExtensionRegistration(JewelsExtensionId, JewelsExtensionVersion, JewelsExtensionDataLength)
,new ExtensionRegistration(TradeGoodsExtensionId, TradeGoodsExtensionVersion, TradeGoodsExtensionDataLength)
            ,new ExtensionRegistration(IncenseExtensionId, IncenseExtensionVersion, IncenseExtensionDataLength)
            ,new ExtensionRegistration(CharmsExtensionId, CharmsExtensionVersion, CharmsExtensionDataLength)
            ,new ExtensionRegistration(RepairKitsExtensionId, RepairKitsExtensionVersion, RepairKitsExtensionDataLength)
            ,new ExtensionRegistration(ToolsExtensionId, ToolsExtensionVersion, ToolsExtensionDataLength)
             ,new ExtensionRegistration(GoodsMarketExtensionId, GoodsMarketExtensionVersion, GoodsMarketExtensionDataLength)
             ,new ExtensionRegistration(CraftTimingExtensionId, CraftTimingExtensionVersion, CraftTimingExtensionDataLength)
             ,new ExtensionRegistration(AdjacentBuildingsExtensionId, AdjacentBuildingsExtensionVersion, AdjacentBuildingsExtensionDataLength)
             ,new ExtensionRegistration(CraftBalanceExtensionId, CraftBalanceExtensionVersion, CraftBalanceExtensionDataLength)
            ,new ExtensionRegistration(ResourceFogExtensionId, ResourceFogExtensionVersion, ResourceFogExtensionDataLength)
            ,new ExtensionRegistration(CraftTree1ExtensionId, CraftTree1ExtensionVersion, CraftTree1ExtensionDataLength)
            ,new ExtensionRegistration(PrimitiveCraftExtensionId, PrimitiveCraftExtensionVersion, PrimitiveCraftExtensionDataLength)
            ,new ExtensionRegistration(AiFactoryExtensionId, AiFactoryExtensionVersion, AiFactoryExtensionDataLength)
             ,new ExtensionRegistration(AiExpandExtensionId, AiExpandExtensionVersion, AiExpandExtensionDataLength)
              ,new ExtensionRegistration(OpenFarmsExtensionId, OpenFarmsExtensionVersion, OpenFarmsExtensionDataLength)
               ,new ExtensionRegistration(FreeTownsExtensionId, FreeTownsExtensionVersion, FreeTownsExtensionDataLength)
             ,new ExtensionRegistration(CraftRequiredExtensionId, CraftRequiredExtensionVersion, CraftRequiredExtensionDataLength)
             ,new ExtensionRegistration(CraftDepthExtensionId, CraftDepthExtensionVersion, CraftDepthExtensionDataLength)
              ,new ExtensionRegistration(EarlyStartExtensionId, EarlyStartExtensionVersion, EarlyStartExtensionDataLength)
            ,new ExtensionRegistration(HouseFlowExtensionId, HouseFlowExtensionVersion, HouseFlowExtensionDataLength)
              ,new ExtensionRegistration(FastMovementExtensionId, FastMovementExtensionVersion, FastMovementExtensionDataLength)
               ,new ExtensionRegistration(BeltDropOffExtensionId, BeltDropOffExtensionVersion, BeltDropOffExtensionDataLength)
             ,new ExtensionRegistration(CraftFlowExtensionId, CraftFlowExtensionVersion, CraftFlowExtensionDataLength)
             ,new ExtensionRegistration(ArmsScaleExtensionId, ArmsScaleExtensionVersion, ArmsScaleExtensionDataLength)
            ,new ExtensionRegistration(BeltStackExtensionId, BeltStackExtensionVersion, BeltStackExtensionDataLength)
             ,new ExtensionRegistration(BeltCraftExtensionId, BeltCraftExtensionVersion, BeltCraftExtensionDataLength)
            ,new ExtensionRegistration(DropSiteLimitExtensionId, DropSiteLimitExtensionVersion, DropSiteLimitExtensionDataLength)
            ,new ExtensionRegistration(AiBeltsExtensionId, AiBeltsExtensionVersion, AiBeltsExtensionDataLength)
            ,new ExtensionRegistration(CraftTree2ExtensionId, CraftTree2ExtensionVersion, CraftTree2ExtensionDataLength)
            ,new ExtensionRegistration(CraftTree3ExtensionId, CraftTree3ExtensionVersion, CraftTree3ExtensionDataLength)
            ,new ExtensionRegistration(AgeClockUxExtensionId, AgeClockUxExtensionVersion, AgeClockUxExtensionDataLength)
            ,new ExtensionRegistration(DirectPathMovementExtensionId, DirectPathMovementExtensionVersion, DirectPathMovementExtensionDataLength)
            ,new ExtensionRegistration(StorageFeedsProcessingExtensionId, StorageFeedsProcessingExtensionVersion, StorageFeedsProcessingExtensionDataLength)
            ,new ExtensionRegistration(CommonDrillExtensionId, CommonDrillExtensionVersion, CommonDrillExtensionDataLength)
            ,new ExtensionRegistration(InserterExtensionId, InserterExtensionVersion, InserterExtensionDataLength)
            ,new ExtensionRegistration(EasyRebuildExtensionId, EasyRebuildExtensionVersion, EasyRebuildExtensionDataLength)
            ,new ExtensionRegistration(ExploreScoutingExtensionId, ExploreScoutingExtensionVersion, ExploreScoutingExtensionDataLength)
             ,new ExtensionRegistration(BuildingReservationsExtensionId, BuildingReservationsExtensionVersion, BuildingReservationsExtensionDataLength)
             ,new ExtensionRegistration(AiArmyRecoveryExtensionId, AiArmyRecoveryExtensionVersion, AiArmyRecoveryExtensionDataLength)
             ,new ExtensionRegistration(AutoReturnAfterHumanExtensionId, AutoReturnAfterHumanExtensionVersion, AutoReturnAfterHumanExtensionDataLength)
             ,new ExtensionRegistration(LineBuildingCheckExtensionId, LineBuildingCheckExtensionVersion, LineBuildingCheckExtensionDataLength)
             ,new ExtensionRegistration(NoNegativeStockExtensionId, NoNegativeStockExtensionVersion, NoNegativeStockExtensionDataLength)
             ,new ExtensionRegistration(LargeEconomyExtensionId, LargeEconomyExtensionVersion, LargeEconomyExtensionDataLength)
             ,new ExtensionRegistration(AgeFourExtensionId, AgeFourExtensionVersion, AgeFourExtensionDataLength)
             ,new ExtensionRegistration(AgeStrengthsExtensionId, AgeStrengthsExtensionVersion, AgeStrengthsExtensionDataLength)
             ,new ExtensionRegistration(CraftTree4ExtensionId, CraftTree4ExtensionVersion, CraftTree4ExtensionDataLength)
             ,new ExtensionRegistration(AgeFourAiExtensionId, AgeFourAiExtensionVersion, AgeFourAiExtensionDataLength)
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
            bool beltComponentsRules = c.Economy.BeltComponents;
            bool lineRebuildRules = c.Economy.LineRebuildDelayTicks != 0 || c.Economy.RaidLinePriority != 0;
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
                || extensionRules || beltComponentsRules || lineRebuildRules;
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
                    if (beltComponentsRules)
                    {
                        w.Write(BeltComponentsTailMarker);
                        var e = c.Economy;
                        w.Write(e.BeltComponents); w.Write(e.SplitterWoodCost); w.Write(e.SorterWoodCost); w.Write(e.UndergroundBeltWoodCost);
                        w.Write(e.UndergroundBeltMaxLength); w.Write(e.FastBeltWoodCost); w.Write(e.FastBeltTicksPerCell); w.Write(e.FastBeltAge);
                        w.Write(e.StorageSizeCells); w.Write(e.StorageWoodCost); w.Write(e.StorageWork); w.Write(e.StorageHp); w.Write(e.StorageCapacity);
                        w.Write((uint)c.Belts.Length);
                        foreach (var b in c.Belts)
                        {
                            w.Write(b.Cell); w.Write((byte)b.Component); w.Write((byte)b.SorterKind); w.Write(b.PairCell); w.Write((byte)b.Speed);
                        }
                    }
                    if (lineRebuildRules)
                    {
                        w.Write(LineRebuildTailMarker);
                        w.Write(c.Economy.LineRebuildDelayTicks); w.Write(c.Economy.RaidLinePriority);
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
                    c.Belts=new BeltDefinition[Count(r)]; for(int i=0;i<c.Belts.Length;i++) c.Belts[i]=new BeltDefinition { Cell=r.ReadInt32(), FactionId=r.ReadUInt32(), Facing=(Facing)r.ReadByte(), Item=(ResourceKind)r.ReadByte(), PairCell=-1 };
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
                                    else if (IsTailMarker(marker)) { ReadMarkedTail(r, e, c, marker); ReadMarkedTails(r, e, c); }
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
                                        ReadMarkedTails(r, e, c);
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
        private static void ReadMarkedTails(BinaryReader r, EconomyRules e, ScenarioDefinition c)
        {
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                long markerPosition = r.BaseStream.Position;
                int marker = r.ReadInt32();
                if (marker == ExtensionMarker) { r.BaseStream.Position = markerPosition; return; }
                ReadMarkedTail(r, e, c, marker);
            }
        }
        private static void ReadMarkedTail(BinaryReader r, EconomyRules e, ScenarioDefinition c, int marker)
        {
            if (marker == ForestryTailMarker) ReadForestryTail(r, e);
            else if (marker == MasonryTailMarker) ReadMasonryTail(r, e);
            else if (marker == CaravanTailMarker) ReadCaravanTail(r, e);
            else if (marker == CavalryTailMarker) ReadCavalryTail(r, e);
            else if (marker == BridgeTailMarker) ReadBridgeTail(r, e);
            else if (marker == BeltComponentsTailMarker) ReadBeltComponentsTail(r, e, c);
            else if (marker == LineRebuildTailMarker) { e.LineRebuildDelayTicks = r.ReadInt32(); e.RaidLinePriority = r.ReadInt32(); }
            else throw new InvalidDataException("Invalid optional tail marker.");
        }
        private static bool IsTailMarker(int value)
            => value == ForestryTailMarker || value == MasonryTailMarker || value == CaravanTailMarker || value == CavalryTailMarker
                || value == BridgeTailMarker || value == BeltComponentsTailMarker || value == LineRebuildTailMarker;

        private static void ReadBeltComponentsTail(BinaryReader r, EconomyRules e, ScenarioDefinition c)
        {
            e.BeltComponents = Bool(r);
            e.SplitterWoodCost = r.ReadInt32(); e.SorterWoodCost = r.ReadInt32(); e.UndergroundBeltWoodCost = r.ReadInt32();
            e.UndergroundBeltMaxLength = r.ReadInt32(); e.FastBeltWoodCost = r.ReadInt32(); e.FastBeltTicksPerCell = r.ReadInt32(); e.FastBeltAge = r.ReadInt32();
            e.StorageSizeCells = r.ReadInt32(); e.StorageWoodCost = r.ReadInt32(); e.StorageWork = r.ReadInt32(); e.StorageHp = r.ReadInt32(); e.StorageCapacity = r.ReadInt32();
            // The base belt record already carries the stable cell/faction/facing/item tuple. This tail only adds new fields.
            int count = Count(r);
            var records = new BeltDefinition[count];
            for (int i = 0; i < records.Length; i++)
                records[i] = new BeltDefinition { Cell = r.ReadInt32(), Component = (BeltComponentKind)r.ReadByte(),
                    SorterKind = (ResourceKind)r.ReadByte(), PairCell = r.ReadInt32(), Speed = (BeltSpeed)r.ReadByte() };
            e.BeltComponents = true;
            for (int i = 0; i < records.Length; i++)
                for (int j = 0; j < c.Belts.Length; j++)
                    if (c.Belts[j].Cell == records[i].Cell)
                    {
                        var belt = c.Belts[j]; belt.Component = records[i].Component; belt.SorterKind = records[i].SorterKind;
                        belt.PairCell = records[i].PairCell; belt.Speed = records[i].Speed; c.Belts[j] = belt; break;
                    }
        }

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
                // 76 is written in its first version while the belt AI is off (see CreateBeltDropOffExtension).
                bool beltDropOffV1 = extension.Id == BeltDropOffExtensionId && extension.Version == BeltDropOffExtensionV1
                    && extension.Data.Length == BeltDropOffExtensionV1DataLength;
                bool beltCraftV1 = extension.Id == BeltCraftExtensionId && extension.Version == BeltCraftExtensionV1
                    && extension.Data.Length == BeltCraftExtensionV1DataLength;
                if (!beltDropOffV1 && !beltCraftV1 && (extension.Version != registration.Version || extension.Data.Length != registration.DataLength))
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
                bool oldCult = id == CultExtensionId && featureVersion == CultExtensionV1
                    && dataLength == CultExtensionV1DataLength;
                bool oldMountain = id == MountainExtensionId && featureVersion == MountainExtensionV1
                    && dataLength == MountainExtensionV1DataLength;
                bool oldFishing = id == FishingCivExtensionId && featureVersion == FishingCivExtensionV1
                    && dataLength == FishingCivExtensionV1DataLength;
                bool oldMetropolis = id == MetropolisExtensionId && featureVersion == MetropolisExtensionV1
                    && dataLength == MetropolisExtensionV1DataLength;
                bool oldTollgate = id == TollgateExtensionId && featureVersion == TollgateExtensionV1
                    && dataLength == TollgateExtensionV1DataLength;
                bool oldSanctuary = id == SanctuaryExtensionId && featureVersion == SanctuaryExtensionV1
                    && dataLength == SanctuaryExtensionV1DataLength;
                bool oldBricks = id == BricksExtensionId && featureVersion == BricksExtensionV1
                    && dataLength == BricksExtensionV1DataLength;
                bool oldTack = id == TackExtensionId && featureVersion == TackExtensionV1
                    && dataLength == TackExtensionV1DataLength;
                bool oldCraftTree3 = id == CraftTree3ExtensionId && featureVersion == CraftTree3ExtensionV1
                    && dataLength == CraftTree3ExtensionV1DataLength;
                bool oldExploreScouting = id == ExploreScoutingExtensionId && featureVersion == ExploreScoutingExtensionV1
                    && dataLength == ExploreScoutingExtensionV1DataLength;
                bool oldLargeEconomy = id == LargeEconomyExtensionId && featureVersion == LargeEconomyExtensionV1
                    && dataLength == LargeEconomyExtensionV1DataLength;
                bool oldLargeEconomyV2 = id == LargeEconomyExtensionId && featureVersion == LargeEconomyExtensionV2
                    && dataLength == LargeEconomyExtensionV2DataLength;
                bool oldLargeEconomyV3 = id == LargeEconomyExtensionId && featureVersion == 3
                    && dataLength == LargeEconomyExtensionV3DataLength;
                 bool oldLargeEconomyV4 = id == LargeEconomyExtensionId && featureVersion == 4
                     && dataLength == LargeEconomyExtensionV4DataLength;
                 bool oldLargeEconomyV5 = id == LargeEconomyExtensionId && featureVersion == 5
                     && dataLength == LargeEconomyExtensionV5DataLength;
                 bool oldLargeEconomyV6 = id == LargeEconomyExtensionId && featureVersion == 6
                       && dataLength == LargeEconomyExtensionV6DataLength;
                  bool oldCraftRequired = id == CraftRequiredExtensionId && featureVersion == CraftRequiredExtensionVersion
                     && (dataLength == CraftRequiredExtensionV1DataLength || dataLength == CraftRequiredExtensionV2DataLength
                         || dataLength == CraftRequiredExtensionV3DataLength);
                 bool oldBeltDropOff = id == BeltDropOffExtensionId && featureVersion == BeltDropOffExtensionV1
                     && dataLength == BeltDropOffExtensionV1DataLength;
                 bool oldBeltCraft = id == BeltCraftExtensionId && featureVersion == BeltCraftExtensionV1
                     && dataLength == BeltCraftExtensionV1DataLength;
                   bool old = oldCult || oldMountain || oldFishing || oldTollgate || oldMetropolis || oldSanctuary || oldBricks || oldTack || oldCraftTree3 || oldExploreScouting || oldLargeEconomy || oldLargeEconomyV2 || oldLargeEconomyV3 || oldLargeEconomyV4 || oldLargeEconomyV5 || oldLargeEconomyV6 || oldCraftRequired || oldBeltDropOff || oldBeltCraft;
                if ((!old && featureVersion != registration.Version)
                    || (!old && dataLength != registration.DataLength) || dataLength < 0
                    || dataLength > sectionEnd - r.BaseStream.Position)
                    throw new InvalidDataException("Invalid scenario extension version or length.");
                var data = r.ReadBytes(dataLength);
                if (data.Length != dataLength) throw new InvalidDataException("Truncated scenario extension data.");
                seen[registrationIndex] = true;
                var extension = new ScenarioExtensionData { Id = id, Version = featureVersion, Data = data };
                if (id == AcademyExtensionId) ReadAcademyExtension(extension, c.Economy);
                else if (id == CultExtensionId) ReadCultExtension(extension, c.Economy);
                else if (id == FishingCivExtensionId) ReadFishingCivExtension(extension, c.Economy);
                else if (id == MountainExtensionId) ReadMountainExtension(extension, c.Economy);
                else if (id == TollgateExtensionId) ReadTollgateExtension(extension, c.Economy);
                else if (id == MetropolisExtensionId) ReadMetropolisExtension(extension, c.Economy);
                else if (id == SanctuaryExtensionId) ReadSanctuaryExtension(extension, c.Economy);
                else if (id == CoreDefenceExtensionId) ReadCoreDefenceExtension(extension, c.Economy);
                else if (id == ArmyGrowthExtensionId) ReadArmyGrowthExtension(extension, c.Economy);
                else if (id == TownExtensionId) ReadTownExtension(extension, c.Economy);
                else if (id == EconomyScaleExtensionId) ReadEconomyScaleExtension(extension, c.Economy);
                 else if (id == RegionExtensionId) ReadRegionExtension(extension, c.Economy);
                  else if (id == LatePushExtensionId) ReadLatePushExtension(extension, c.Economy);
                    else if (id == EarlyArmsExtensionId) ReadEarlyArmsExtension(extension, c.Economy);
                    else if (id == AgeClockExtensionId) ReadAgeClockExtension(extension, c.Economy);
                    else if (id == AgeClockUxExtensionId) ReadAgeClockUxExtension(extension, c.Economy);
                    else if (id == SiegeByAgeExtensionId) ReadSiegeByAgeExtension(extension, c.Economy);
                     else if (id == PushWavesExtensionId) ReadPushWavesExtension(extension, c.Economy);
                      else if (id == ManualControlExtensionId) ReadManualControlExtension(extension, c.Economy);
                    else if (id == RationsExtensionId) ReadRationsExtension(extension, c.Economy);
                    else if (id == StorageFeedsProcessingExtensionId) ReadStorageFeedsProcessingExtension(extension, c.Economy);
                    else if (id == CommonDrillExtensionId) ReadCommonDrillExtension(extension, c.Economy);
                    else if (id == InserterExtensionId) ReadInserterExtension(extension, c.Economy);
                    else if (id == EasyRebuildExtensionId) ReadEasyRebuildExtension(extension, c.Economy);
                    else if (id == BricksExtensionId) ReadBricksExtension(extension, c.Economy);
                    else if (id == TackExtensionId) ReadTackExtension(extension, c.Economy);
                    else if (id == PlanksExtensionId) ReadPlanksExtension(extension, c.Economy);
                    else if (id == BooksExtensionId) ReadBooksExtension(extension, c.Economy);
                    else if (id == PreservesExtensionId) ReadPreservesExtension(extension, c.Economy);
                    else if (id == CivVarietyExtensionId) ReadCivVarietyExtension(extension, c.Economy);
                    else if (id == CivVarietyCapsExtensionId) ReadCivVarietyCapsExtension(extension, c.Economy);
                    else if (id == JewelsExtensionId) ReadJewelsExtension(extension, c.Economy);
                    else if (id == TradeGoodsExtensionId) ReadTradeGoodsExtension(extension, c.Economy);
                    else if (id == IncenseExtensionId) ReadIncenseExtension(extension, c.Economy);
                    else if (id == CharmsExtensionId) ReadCharmsExtension(extension, c.Economy);
                    else if (id == RepairKitsExtensionId) ReadRepairKitsExtension(extension, c.Economy);
                    else if (id == ToolsExtensionId) ReadToolsExtension(extension, c.Economy);
                     else if (id == GoodsMarketExtensionId) ReadGoodsMarketExtension(extension, c.Economy);
                     else if (id == CraftTimingExtensionId) ReadCraftTimingExtension(extension, c.Economy);
                     else if (id == AdjacentBuildingsExtensionId) ReadAdjacentBuildingsExtension(extension, c.Economy);
                     else if (id == CraftBalanceExtensionId) ReadCraftBalanceExtension(extension, c.Economy);
                    else if (id == ResourceFogExtensionId) ReadResourceFogExtension(extension, c.Economy);
                    else if (id == CraftTree1ExtensionId) ReadCraftTree1Extension(extension, c.Economy);
                    else if (id == PrimitiveCraftExtensionId) ReadPrimitiveCraftExtension(extension, c.Economy);
                     else if (id == AiFactoryExtensionId) ReadAiFactoryExtension(extension, c.Economy);
                     else if (id == AiExpandExtensionId) ReadAiExpandExtension(extension, c.Economy);
                      else if (id == OpenFarmsExtensionId) ReadOpenFarmsExtension(extension, c.Economy);
                       else if (id == FreeTownsExtensionId) ReadFreeTownsExtension(extension, c.Economy);
                      else if (id == CraftRequiredExtensionId) ReadCraftRequiredExtension(extension, c.Economy);
                      else if (id == CraftDepthExtensionId) ReadCraftDepthExtension(extension, c.Economy);
                      else if (id == EarlyStartExtensionId) ReadEarlyStartExtension(extension, c.Economy);
                      else if (id == FastMovementExtensionId) ReadFastMovementExtension(extension, c.Economy);
                      else if (id == BeltDropOffExtensionId) ReadBeltDropOffExtension(extension, c.Economy);
                      else if (id == CraftFlowExtensionId) ReadCraftFlowExtension(extension, c.Economy);
                    else if (id == ArmsScaleExtensionId) ReadArmsScaleExtension(extension, c.Economy);
                    else if (id == DropSiteLimitExtensionId) ReadDropSiteLimitExtension(extension, c.Economy);
                    else if (id == AiBeltsExtensionId) ReadAiBeltsExtension(extension, c.Economy);
                     else if (id == CraftTree2ExtensionId) ReadCraftTree2Extension(extension, c.Economy);
                    else if (id == CraftTree3ExtensionId) ReadCraftTree3Extension(extension, c.Economy);
                    else if (id == DirectPathMovementExtensionId) ReadDirectPathMovementExtension(extension, c.Economy);
                    else if (id == ExploreScoutingExtensionId) ReadExploreScoutingExtension(extension, c.Economy);
                    else if (id == BuildingReservationsExtensionId) ReadBuildingReservationsExtension(extension, c.Economy);
                     else if (id == AiArmyRecoveryExtensionId) ReadAiArmyRecoveryExtension(extension, c.Economy);
                     else if (id == LineBuildingCheckExtensionId) ReadLineBuildingCheckExtension(extension, c.Economy);
                     else if (id == AutoReturnAfterHumanExtensionId) ReadAutoReturnAfterHumanExtension(extension, c.Economy);
                      else if (id == NoNegativeStockExtensionId) ReadNoNegativeStockExtension(extension, c.Economy);
                      else if (id == LargeEconomyExtensionId) ReadLargeEconomyExtension(extension, c.Economy);
                       else if (id == AgeFourExtensionId) ReadAgeFourExtension(extension, c.Economy);
                    else if (id == AgeStrengthsExtensionId) ReadAgeStrengthsExtension(extension, c.Economy);
                    else if (id == CraftTree4ExtensionId) ReadCraftTree4Extension(extension, c.Economy);
                      else if (id == AgeFourAiExtensionId) ReadAgeFourAiExtension(extension, c.Economy);
                      else if (id == HouseFlowExtensionId) ReadHouseFlowExtension(extension, c.Economy);
                      else if (id == BeltStackExtensionId) ReadBeltStackExtension(extension, c.Economy);
                      else if (id == BeltCraftExtensionId) ReadBeltCraftExtension(extension, c.Economy);
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
            // ID 3 is feature-owned data.  A scenario that turns the cult flag off must
            // not retain a stale cult extension from a previous encode/decode round trip.
            extensions.RemoveAll(extension => extension != null && extension.Id == CultExtensionId && !c.Economy.Cult);
            extensions.RemoveAll(extension => extension != null && extension.Id == MetropolisExtensionId && !c.Economy.Metropolis);
            extensions.RemoveAll(extension => extension != null && extension.Id == SanctuaryExtensionId && !c.Economy.Sanctuary);
            extensions.RemoveAll(extension => extension != null && extension.Id == CoreDefenceExtensionId && !c.Economy.CoreDefence);
            extensions.RemoveAll(extension => extension != null && extension.Id == ArmyGrowthExtensionId && !c.Economy.ArmyGrowth);
            extensions.RemoveAll(extension => extension != null && extension.Id == TownExtensionId && !c.Economy.Towns);
            extensions.RemoveAll(extension => extension != null && extension.Id == EconomyScaleExtensionId && !c.Economy.EconomyScale);
            extensions.RemoveAll(extension => extension != null && extension.Id == RegionExtensionId && !c.Economy.Regions);
            extensions.RemoveAll(extension => extension != null && extension.Id == LatePushExtensionId && !c.Economy.LatePush);
             extensions.RemoveAll(extension => extension != null && extension.Id == EarlyArmsExtensionId && !c.Economy.EarlyArms);
             extensions.RemoveAll(extension => extension != null && extension.Id == AgeClockExtensionId && !c.Economy.AgeClock);
             extensions.RemoveAll(extension => extension != null && extension.Id == AgeClockUxExtensionId && !c.Economy.AgeClockUx);
              extensions.RemoveAll(extension => extension != null && extension.Id == SiegeByAgeExtensionId && !c.Economy.SiegeByAge);
               extensions.RemoveAll(extension => extension != null && extension.Id == PushWavesExtensionId && !c.Economy.PushWaves);
                extensions.RemoveAll(extension => extension != null && extension.Id == ManualControlExtensionId && !c.Economy.ManualControl);
                extensions.RemoveAll(extension => extension != null && extension.Id == RationsExtensionId && !c.Economy.Rations);
                extensions.RemoveAll(extension => extension != null && extension.Id == BricksExtensionId && !c.Economy.Bricks);
                extensions.RemoveAll(extension => extension != null && extension.Id == TackExtensionId && !c.Economy.Tack);
                extensions.RemoveAll(extension => extension != null && extension.Id == PlanksExtensionId && !c.Economy.Planks);
                extensions.RemoveAll(extension => extension != null && extension.Id == BooksExtensionId && !c.Economy.Books);
                extensions.RemoveAll(extension => extension != null && extension.Id == PreservesExtensionId && !c.Economy.Preserves);
                extensions.RemoveAll(extension => extension != null && extension.Id == CivVarietyExtensionId && !c.Economy.CivVariety);
                extensions.RemoveAll(extension => extension != null && extension.Id == CivVarietyCapsExtensionId && !c.Economy.CivVarietyCaps);
                extensions.RemoveAll(extension => extension != null && extension.Id == JewelsExtensionId && !c.Economy.Jewels);
                extensions.RemoveAll(extension => extension != null && extension.Id == TradeGoodsExtensionId && !c.Economy.TradeGoods);
                extensions.RemoveAll(extension => extension != null && extension.Id == IncenseExtensionId && !c.Economy.Incense);
                extensions.RemoveAll(extension => extension != null && extension.Id == CharmsExtensionId && !c.Economy.Charms);
                extensions.RemoveAll(extension => extension != null && extension.Id == RepairKitsExtensionId && !c.Economy.RepairKits);
                extensions.RemoveAll(extension => extension != null && extension.Id == ToolsExtensionId && !c.Economy.Tools);
                extensions.RemoveAll(extension => extension != null && extension.Id == GoodsMarketExtensionId && !c.Economy.GoodsMarket);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CraftTimingExtensionId && !c.Economy.CraftTiming);
                 extensions.RemoveAll(extension => extension != null && extension.Id == AdjacentBuildingsExtensionId && !c.Economy.AdjacentBuildings);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CraftBalanceExtensionId && !c.Economy.CraftBalance);
                extensions.RemoveAll(extension => extension != null && extension.Id == ResourceFogExtensionId && !c.Economy.ResourceFog);
                extensions.RemoveAll(extension => extension != null && extension.Id == CraftTree1ExtensionId && !c.Economy.CraftTree1);
                extensions.RemoveAll(extension => extension != null && extension.Id == PrimitiveCraftExtensionId && !c.Economy.PrimitiveCraft);
                 extensions.RemoveAll(extension => extension != null && extension.Id == AiFactoryExtensionId && !(c.Economy.PrimitiveCraft && c.Economy.AiFactory));
                 extensions.RemoveAll(extension => extension != null && extension.Id == AiExpandExtensionId && !(c.Economy.AiFactory && c.Economy.AiExpand));
                  extensions.RemoveAll(extension => extension != null && extension.Id == OpenFarmsExtensionId && !c.Economy.OpenFarms);
                   extensions.RemoveAll(extension => extension != null && extension.Id == FreeTownsExtensionId && !c.Economy.FreeTowns);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CraftRequiredExtensionId && !c.Economy.CraftRequired);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CraftDepthExtensionId && !c.Economy.CraftDepth);
                  extensions.RemoveAll(extension => extension != null && extension.Id == EarlyStartExtensionId && !c.Economy.EarlyStart);
                  extensions.RemoveAll(extension => extension != null && extension.Id == FastMovementExtensionId && !c.Economy.FastMovement);
                   extensions.RemoveAll(extension => extension != null && extension.Id == BeltDropOffExtensionId && !c.Economy.BeltDropOff);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CraftFlowExtensionId && !c.Economy.CraftFlow);
                 extensions.RemoveAll(extension => extension != null && extension.Id == ArmsScaleExtensionId && !c.Economy.ArmsScale);
                extensions.RemoveAll(extension => extension != null && extension.Id == DropSiteLimitExtensionId && !c.Economy.DropSiteLimit);
                extensions.RemoveAll(extension => extension != null && extension.Id == AiBeltsExtensionId && !c.Economy.AiBelts);
                extensions.RemoveAll(extension => extension != null && extension.Id == StorageFeedsProcessingExtensionId && !c.Economy.StorageFeedsProcessing);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CraftTree2ExtensionId && !(c.Economy.CraftTree1 && c.Economy.CraftTree2));
                extensions.RemoveAll(extension => extension != null && extension.Id == CraftTree3ExtensionId && !(c.Economy.CraftTree1 && c.Economy.CraftTree2 && c.Economy.CraftTree3));
                extensions.RemoveAll(extension => extension != null && extension.Id == DirectPathMovementExtensionId && !c.Economy.DirectPathMovement);
                 extensions.RemoveAll(extension => extension != null && extension.Id == CommonDrillExtensionId && !c.Economy.CommonDrill);
                 extensions.RemoveAll(extension => extension != null && extension.Id == InserterExtensionId && !c.Economy.Inserters);
                extensions.RemoveAll(extension => extension != null && extension.Id == EasyRebuildExtensionId && !c.Economy.EasyRebuild);
                extensions.RemoveAll(extension => extension != null && extension.Id == ExploreScoutingExtensionId && !c.Economy.ExploreScouting);
                extensions.RemoveAll(extension => extension != null && extension.Id == BuildingReservationsExtensionId && !c.Economy.BuildingReservations);
                extensions.RemoveAll(extension => extension != null && extension.Id == AiArmyRecoveryExtensionId && !c.Economy.AiArmyRecovery);
                extensions.RemoveAll(extension => extension != null && extension.Id == LineBuildingCheckExtensionId && !c.Economy.LineBuildingCheck);
                extensions.RemoveAll(extension => extension != null && extension.Id == AutoReturnAfterHumanExtensionId && !c.Economy.AutoReturnAfterHuman);
                 extensions.RemoveAll(extension => extension != null && extension.Id == NoNegativeStockExtensionId && !c.Economy.NoNegativeStock);
                 extensions.RemoveAll(extension => extension != null && extension.Id == LargeEconomyExtensionId && !c.Economy.LargeEconomy);
                  extensions.RemoveAll(extension => extension != null && extension.Id == AgeFourExtensionId && !c.Economy.AgeFour);
                  extensions.RemoveAll(extension => extension != null && extension.Id == AgeStrengthsExtensionId && !c.Economy.AgeStrengths);
                  extensions.RemoveAll(extension => extension != null && extension.Id == CraftTree4ExtensionId && !(c.Economy.CraftTree1 && c.Economy.CraftTree2 && c.Economy.CraftTree3 && c.Economy.AgeFour && c.Economy.CraftTree4));
                  extensions.RemoveAll(extension => extension != null && extension.Id == AgeFourAiExtensionId && !(c.Economy.AgeFour && c.Economy.AgeFourAi));
                  extensions.RemoveAll(extension => extension != null && extension.Id == HouseFlowExtensionId && !c.Economy.HouseFlow);
                  extensions.RemoveAll(extension => extension != null && extension.Id == BeltStackExtensionId && !c.Economy.BeltStack);
                  extensions.RemoveAll(extension => extension != null && extension.Id == BeltCraftExtensionId && !c.Economy.BeltCraft);
            bool hasLargeEconomy = false, hasAcademy = false, hasFishingCiv = false, hasAgeFourAi = false, hasHouseFlow = false;
            bool hasAiExpand = false, hasOpenFarms = false, hasFreeTowns = false, hasCraftRequired = false, hasCraftDepth = false, hasArmsScale = false, hasEarlyStart = false, hasFastMovement = false, hasBeltDropOff = false, hasCraftFlow = false, hasBeltStack = false, hasBeltCraft = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null) continue;
                if (extensions[i].Id == AiExpandExtensionId)
                {
                    hasAiExpand = true;
                    extensions[i] = CreateAiExpandExtension(c.Economy);
                }
                 if (extensions[i].Id == OpenFarmsExtensionId)
                {
                    hasOpenFarms = true;
                     extensions[i] = CreateOpenFarmsExtension(c.Economy);
                 }
                  if (extensions[i].Id == FreeTownsExtensionId)
                 {
                     hasFreeTowns = true;
                      extensions[i] = CreateFreeTownsExtension(c.Economy);
                  }
                 if (extensions[i].Id == CraftRequiredExtensionId)
                 {
                     hasCraftRequired = true;
                     extensions[i] = CreateCraftRequiredExtension(c.Economy);
                 }
                 if (extensions[i].Id == CraftDepthExtensionId)
                 {
                     hasCraftDepth = true;
                     extensions[i] = CreateCraftDepthExtension(c.Economy);
                 }
                 if (extensions[i].Id == EarlyStartExtensionId)
                 {
                     hasEarlyStart = true;
                     extensions[i] = CreateEarlyStartExtension(c.Economy);
                 }
                 if (extensions[i].Id == FastMovementExtensionId)
                 {
                     hasFastMovement = true;
                     extensions[i] = CreateFastMovementExtension(c.Economy);
                 }
                  if (extensions[i].Id == BeltDropOffExtensionId)
                 {
                     hasBeltDropOff = true;
                      extensions[i] = CreateBeltDropOffExtension(c.Economy);
                  }
                  if (extensions[i].Id == CraftFlowExtensionId)
                  {
                      hasCraftFlow = true;
                      extensions[i] = CreateCraftFlowExtension(c.Economy);
                  }
                  if (extensions[i].Id == ArmsScaleExtensionId)
                  {
                      hasArmsScale = true;
                      extensions[i] = CreateArmsScaleExtension(c.Economy);
                  }
                  if (extensions[i].Id == BeltStackExtensionId)
                  {
                      hasBeltStack = true;
                      extensions[i] = CreateBeltStackExtension(c.Economy);
                  }
                  if (extensions[i].Id == BeltCraftExtensionId)
                  {
                      hasBeltCraft = true;
                      extensions[i] = CreateBeltCraftExtension(c.Economy);
                  }
                if (extensions[i].Id == LargeEconomyExtensionId)
                {
                    hasLargeEconomy = true;
                    extensions[i] = CreateLargeEconomyExtension(c.Economy);
                }
                if (extensions[i].Id == HouseFlowExtensionId)
                {
                    hasHouseFlow = true;
                    extensions[i] = CreateHouseFlowExtension(c.Economy);
                }
                if (extensions[i].Id == AcademyExtensionId) hasAcademy = true;
                if (extensions[i].Id == FishingCivExtensionId) hasFishingCiv = true;
                if (extensions[i].Id == AgeFourAiExtensionId)
                {
                    hasAgeFourAi = true;
                    extensions[i] = CreateAgeFourAiExtension(c.Economy);
                }
            }
            if (c.Economy.AiFactory && c.Economy.AiExpand && !hasAiExpand)
                extensions.Add(CreateAiExpandExtension(c.Economy));
                 if (c.Economy.OpenFarms && !hasOpenFarms)
                     extensions.Add(CreateOpenFarmsExtension(c.Economy));
                 if (c.Economy.FreeTowns && !hasFreeTowns)
                     extensions.Add(CreateFreeTownsExtension(c.Economy));
                 if (c.Economy.CraftRequired && !hasCraftRequired)
                     extensions.Add(CreateCraftRequiredExtension(c.Economy));
                 if (c.Economy.CraftDepth && !hasCraftDepth)
                     extensions.Add(CreateCraftDepthExtension(c.Economy));
                 if (c.Economy.EarlyStart && !hasEarlyStart)
                     extensions.Add(CreateEarlyStartExtension(c.Economy));
                 if (c.Economy.FastMovement && !hasFastMovement)
                     extensions.Add(CreateFastMovementExtension(c.Economy));
                  if (c.Economy.BeltDropOff && !hasBeltDropOff)
                      extensions.Add(CreateBeltDropOffExtension(c.Economy));
                  if (c.Economy.CraftFlow && !hasCraftFlow)
                      extensions.Add(CreateCraftFlowExtension(c.Economy));
                 if (c.Economy.ArmsScale && !hasArmsScale)
                     extensions.Add(CreateArmsScaleExtension(c.Economy));
                  if (c.Economy.BeltStack && !hasBeltStack)
                      extensions.Add(CreateBeltStackExtension(c.Economy));
                  if (c.Economy.BeltCraft && !hasBeltCraft)
                      extensions.Add(CreateBeltCraftExtension(c.Economy));
                if (c.Economy.LargeEconomy && !hasLargeEconomy) extensions.Add(CreateLargeEconomyExtension(c.Economy));
                if (c.Economy.HouseFlow && !hasHouseFlow) extensions.Add(CreateHouseFlowExtension(c.Economy));
            bool hasAgeFour = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == AgeFourExtensionId)
                {
                    hasAgeFour = true;
                    extensions[i] = CreateAgeFourExtension(c.Economy);
                }
            if (c.Economy.AgeFour && !hasAgeFour) extensions.Add(CreateAgeFourExtension(c.Economy));
            bool hasAgeStrengths = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == AgeStrengthsExtensionId)
                {
                    hasAgeStrengths = true;
                    extensions[i] = CreateAgeStrengthsExtension(c.Economy);
                }
            if (c.Economy.AgeStrengths && !hasAgeStrengths) extensions.Add(CreateAgeStrengthsExtension(c.Economy));
            bool hasCraftTree4 = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == CraftTree4ExtensionId)
                {
                    hasCraftTree4 = true;
                    extensions[i] = CreateCraftTree4Extension(c.Economy);
                }
            if (c.Economy.CraftTree4 && c.Economy.AgeFour && c.Economy.CraftTree1 && c.Economy.CraftTree2 && c.Economy.CraftTree3 && !hasCraftTree4)
                extensions.Add(CreateCraftTree4Extension(c.Economy));
            if (c.Economy.AgeFour && c.Economy.AgeFourAi && !hasAgeFourAi)
                extensions.Add(CreateAgeFourAiExtension(c.Economy));
            // ID 4 is feature-owned data. It is meaningful only when both switches that make fishing selectable are
            // on; an old extension must not resurrect a disabled fishing civilisation on a subsequent round trip.
            if (!c.Economy.FishingCiv || !c.Economy.FishingEnabled)
            {
                extensions.RemoveAll(extension => extension != null && extension.Id == FishingCivExtensionId);
                hasFishingCiv = false;
            }
            if (c.Economy.Academy && !hasAcademy) extensions.Add(CreateAcademyExtension(c.Economy));
            if (c.Economy.FishingCiv && c.Economy.FishingEnabled && !hasFishingCiv) extensions.Add(CreateFishingCivExtension(c.Economy));
            bool hasCult = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != CultExtensionId) continue;
                hasCult = true;
                if (extensions[i].Version == CultExtensionV1) extensions[i] = CreateCultExtension(c.Economy);
            }
            if (c.Economy.Cult && !hasCult) extensions.Add(CreateCultExtension(c.Economy));
            bool hasMountain = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] != null && extensions[i].Id == FishingCivExtensionId && extensions[i].Version == FishingCivExtensionV1)
                    extensions[i] = CreateFishingCivExtension(c.Economy);
                if (extensions[i] != null && extensions[i].Id == MountainExtensionId)
                {
                    hasMountain = true;
                    // A decoded v1 record is upgraded on the next write even when the old
                    // flag was off; the current writer accepts only the registered v2 shape.
                    extensions[i] = CreateMountainExtension(c.Economy);
                }
            }
            if (c.Economy.Mountain && !hasMountain) extensions.Add(CreateMountainExtension(c.Economy));
            extensions.RemoveAll(extension => extension != null && extension.Id == TollgateExtensionId && !c.Economy.Tollgate);
            bool hasTollgate = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != TollgateExtensionId) continue;
                hasTollgate = true;
                if (extensions[i].Version == TollgateExtensionV1) extensions[i] = CreateTollgateExtension(c.Economy);
            }
            if (c.Economy.Tollgate && !hasTollgate) extensions.Add(CreateTollgateExtension(c.Economy));
            bool hasMetropolis = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != MetropolisExtensionId) continue;
                hasMetropolis = true;
                if (extensions[i].Version == MetropolisExtensionV1) extensions[i] = CreateMetropolisExtension(c.Economy);
            }
            if (c.Economy.Metropolis && !hasMetropolis) extensions.Add(CreateMetropolisExtension(c.Economy));
            bool hasSanctuary = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != SanctuaryExtensionId) continue;
                hasSanctuary = true;
                if (extensions[i].Version == SanctuaryExtensionV1) extensions[i] = CreateSanctuaryExtension(c.Economy);
            }
            if (c.Economy.Sanctuary && !hasSanctuary) extensions.Add(CreateSanctuaryExtension(c.Economy));
            bool hasCoreDefence = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == CoreDefenceExtensionId)
                {
                    hasCoreDefence = true;
                    extensions[i] = CreateCoreDefenceExtension(c.Economy);
                }
            if (c.Economy.CoreDefence && !hasCoreDefence) extensions.Add(CreateCoreDefenceExtension(c.Economy));
            bool hasArmyGrowth = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != ArmyGrowthExtensionId) continue;
                hasArmyGrowth = true;
                extensions[i] = CreateArmyGrowthExtension(c.Economy);
            }
            if (c.Economy.ArmyGrowth && !hasArmyGrowth) extensions.Add(CreateArmyGrowthExtension(c.Economy));
            bool hasTown = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == TownExtensionId)
                {
                    hasTown = true;
                    extensions[i] = CreateTownExtension(c.Economy);
                }
            if (c.Economy.Towns && !hasTown) extensions.Add(CreateTownExtension(c.Economy));
            bool hasEconomyScale = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != EconomyScaleExtensionId) continue;
                hasEconomyScale = true;
                extensions[i] = CreateEconomyScaleExtension(c.Economy);
            }
            if (c.Economy.EconomyScale && !hasEconomyScale) extensions.Add(CreateEconomyScaleExtension(c.Economy));
            bool hasRegions = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == RegionExtensionId)
                {
                    hasRegions = true;
                    extensions[i] = CreateRegionExtension(c.Economy);
                }
            if (c.Economy.Regions && !hasRegions) extensions.Add(CreateRegionExtension(c.Economy));
            bool hasLatePush = false;
            for (int i = 0; i < extensions.Count; i++)
                if (extensions[i] != null && extensions[i].Id == LatePushExtensionId)
                {
                    hasLatePush = true;
                    extensions[i] = CreateLatePushExtension(c.Economy);
                }
            if (c.Economy.LatePush && !hasLatePush) extensions.Add(CreateLatePushExtension(c.Economy));
            bool hasEarlyArms = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != EarlyArmsExtensionId) continue;
                hasEarlyArms = true;
                extensions[i] = CreateEarlyArmsExtension(c.Economy);
            }
            if (c.Economy.EarlyArms && !hasEarlyArms) extensions.Add(CreateEarlyArmsExtension(c.Economy));
            bool hasAgeClock = false;
            for (int i = 0; i < extensions.Count; i++)
            {
                if (extensions[i] == null || extensions[i].Id != AgeClockExtensionId) continue;
                hasAgeClock = true;
                extensions[i] = CreateAgeClockExtension(c.Economy);
            }
             if (c.Economy.AgeClock && !hasAgeClock) extensions.Add(CreateAgeClockExtension(c.Economy));
             bool hasAgeClockUx = false;
             for (int i = 0; i < extensions.Count; i++)
             {
                 if (extensions[i] == null || extensions[i].Id != AgeClockUxExtensionId) continue;
                 hasAgeClockUx = true;
                 extensions[i] = CreateAgeClockUxExtension(c.Economy);
             }
             if (c.Economy.AgeClockUx && !hasAgeClockUx) extensions.Add(CreateAgeClockUxExtension(c.Economy));
             bool hasSiegeByAge = false;
             for (int i = 0; i < extensions.Count; i++)
             {
                 if (extensions[i] == null || extensions[i].Id != SiegeByAgeExtensionId) continue;
                 hasSiegeByAge = true;
                 extensions[i] = CreateSiegeByAgeExtension(c.Economy);
             }
              if (c.Economy.SiegeByAge && !hasSiegeByAge) extensions.Add(CreateSiegeByAgeExtension(c.Economy));
              bool hasPushWaves = false;
              for (int i = 0; i < extensions.Count; i++)
              {
                  if (extensions[i] == null || extensions[i].Id != PushWavesExtensionId) continue;
                  hasPushWaves = true;
                  extensions[i] = CreatePushWavesExtension(c.Economy);
              }
               if (c.Economy.PushWaves && !hasPushWaves) extensions.Add(CreatePushWavesExtension(c.Economy));
               bool hasManualControl = false;
               for (int i = 0; i < extensions.Count; i++)
                   if (extensions[i] != null && extensions[i].Id == ManualControlExtensionId)
                   {
                       hasManualControl = true;
                       extensions[i] = CreateManualControlExtension(c.Economy);
                   }
                if (c.Economy.ManualControl && !hasManualControl) extensions.Add(CreateManualControlExtension(c.Economy));
                bool hasRations = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == RationsExtensionId)
                    {
                        hasRations = true;
                        extensions[i] = CreateRationsExtension(c.Economy);
                    }
                if (c.Economy.Rations && !hasRations) extensions.Add(CreateRationsExtension(c.Economy));
                bool hasEasyRebuild = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == EasyRebuildExtensionId)
                    {
                        hasEasyRebuild = true;
                        extensions[i] = CreateEasyRebuildExtension(c.Economy);
                    }
                if (c.Economy.EasyRebuild && !hasEasyRebuild) extensions.Add(CreateEasyRebuildExtension(c.Economy));
                bool hasCommonDrill = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CommonDrillExtensionId)
                    {
                        hasCommonDrill = true;
                        extensions[i] = CreateCommonDrillExtension(c.Economy);
                    }
                if (c.Economy.CommonDrill && !hasCommonDrill) extensions.Add(CreateCommonDrillExtension(c.Economy));
                bool hasInserters = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == InserterExtensionId)
                    {
                        hasInserters = true;
                        extensions[i] = CreateInserterExtension(c.Economy);
                    }
                if (c.Economy.Inserters && !hasInserters) extensions.Add(CreateInserterExtension(c.Economy));
                bool hasBricks = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == BricksExtensionId)
                    {
                        hasBricks = true;
                        extensions[i] = CreateBricksExtension(c.Economy);
                    }
                if (c.Economy.Bricks && !hasBricks) extensions.Add(CreateBricksExtension(c.Economy));
                bool hasTack = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == TackExtensionId)
                    {
                        hasTack = true;
                        extensions[i] = CreateTackExtension(c.Economy);
                    }
                if (c.Economy.Tack && !hasTack) extensions.Add(CreateTackExtension(c.Economy));
                bool hasPlanks = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == PlanksExtensionId)
                    {
                        hasPlanks = true;
                        extensions[i] = CreatePlanksExtension(c.Economy);
                    }
                if (c.Economy.Planks && !hasPlanks) extensions.Add(CreatePlanksExtension(c.Economy));
                bool hasBooks = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == BooksExtensionId)
                    {
                        hasBooks = true;
                        extensions[i] = CreateBooksExtension(c.Economy);
                    }
                if (c.Economy.Books && !hasBooks) extensions.Add(CreateBooksExtension(c.Economy));
                bool hasPreserves = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == PreservesExtensionId)
                    {
                        hasPreserves = true;
                        extensions[i] = CreatePreservesExtension(c.Economy);
                    }
                if (c.Economy.Preserves && !hasPreserves) extensions.Add(CreatePreservesExtension(c.Economy));
                bool hasCivVariety = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CivVarietyExtensionId)
                    {
                        hasCivVariety = true;
                        extensions[i] = CreateCivVarietyExtension(c.Economy);
                    }
                if (c.Economy.CivVariety && !hasCivVariety) extensions.Add(CreateCivVarietyExtension(c.Economy));
                bool hasCivVarietyCaps = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CivVarietyCapsExtensionId)
                    {
                        hasCivVarietyCaps = true;
                        extensions[i] = CreateCivVarietyCapsExtension(c.Economy);
                    }
                if (c.Economy.CivVarietyCaps && !hasCivVarietyCaps) extensions.Add(CreateCivVarietyCapsExtension(c.Economy));
                bool hasJewels = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == JewelsExtensionId)
                    {
                        hasJewels = true;
                        extensions[i] = CreateJewelsExtension(c.Economy);
                    }
                if (c.Economy.Jewels && !hasJewels) extensions.Add(CreateJewelsExtension(c.Economy));
                bool hasTradeGoods = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == TradeGoodsExtensionId)
                    {
                        hasTradeGoods = true;
                        extensions[i] = CreateTradeGoodsExtension(c.Economy);
                    }
                if (c.Economy.TradeGoods && !hasTradeGoods) extensions.Add(CreateTradeGoodsExtension(c.Economy));
                bool hasIncense = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == IncenseExtensionId)
                    {
                        hasIncense = true;
                        extensions[i] = CreateIncenseExtension(c.Economy);
                    }
                if (c.Economy.Incense && !hasIncense) extensions.Add(CreateIncenseExtension(c.Economy));
                bool hasCharms = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CharmsExtensionId)
                    {
                        hasCharms = true;
                        extensions[i] = CreateCharmsExtension(c.Economy);
                    }
                if (c.Economy.Charms && !hasCharms) extensions.Add(CreateCharmsExtension(c.Economy));
                bool hasRepairKits = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == RepairKitsExtensionId)
                    {
                        hasRepairKits = true;
                        extensions[i] = CreateRepairKitsExtension(c.Economy);
                    }
                if (c.Economy.RepairKits && !hasRepairKits) extensions.Add(CreateRepairKitsExtension(c.Economy));
                bool hasTools = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == ToolsExtensionId)
                    {
                        hasTools = true;
                        extensions[i] = CreateToolsExtension(c.Economy);
                    }
                if (c.Economy.Tools && !hasTools) extensions.Add(CreateToolsExtension(c.Economy));
                bool hasGoodsMarket = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == GoodsMarketExtensionId)
                    {
                        hasGoodsMarket = true;
                        extensions[i] = CreateGoodsMarketExtension(c.Economy);
                    }
                if (c.Economy.GoodsMarket && !hasGoodsMarket) extensions.Add(CreateGoodsMarketExtension(c.Economy));
                bool hasCraftTiming = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CraftTimingExtensionId)
                    {
                        hasCraftTiming = true;
                        extensions[i] = CreateCraftTimingExtension(c.Economy);
                    }
                 if (c.Economy.CraftTiming && !hasCraftTiming) extensions.Add(CreateCraftTimingExtension(c.Economy));
                 bool hasAdjacentBuildings = false;
                 for (int i = 0; i < extensions.Count; i++)
                     if (extensions[i] != null && extensions[i].Id == AdjacentBuildingsExtensionId)
                     {
                         hasAdjacentBuildings = true;
                         extensions[i] = CreateAdjacentBuildingsExtension(c.Economy);
                     }
                 if (c.Economy.AdjacentBuildings && !hasAdjacentBuildings) extensions.Add(CreateAdjacentBuildingsExtension(c.Economy));
                 bool hasBuildingReservations = false;
                 for (int i = 0; i < extensions.Count; i++)
                     if (extensions[i] != null && extensions[i].Id == BuildingReservationsExtensionId)
                     {
                         hasBuildingReservations = true;
                         extensions[i] = CreateBuildingReservationsExtension(c.Economy);
                     }
                if (c.Economy.BuildingReservations && !hasBuildingReservations) extensions.Add(CreateBuildingReservationsExtension(c.Economy));
                bool hasNoNegativeStock = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == NoNegativeStockExtensionId)
                    {
                        hasNoNegativeStock = true;
                        extensions[i] = CreateNoNegativeStockExtension(c.Economy);
                    }
                if (c.Economy.NoNegativeStock && !hasNoNegativeStock) extensions.Add(CreateNoNegativeStockExtension(c.Economy));
                bool hasCraftBalance = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CraftBalanceExtensionId)
                    {
                        hasCraftBalance = true;
                        extensions[i] = CreateCraftBalanceExtension(c.Economy);
                    }
                if (c.Economy.CraftBalance && !hasCraftBalance) extensions.Add(CreateCraftBalanceExtension(c.Economy));
                bool hasResourceFog = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == ResourceFogExtensionId)
                    {
                        hasResourceFog = true;
                        extensions[i] = CreateResourceFogExtension(c.Economy);
                    }
                if (c.Economy.ResourceFog && !hasResourceFog) extensions.Add(CreateResourceFogExtension(c.Economy));
                bool hasCraftTree1 = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CraftTree1ExtensionId)
                    {
                        hasCraftTree1 = true;
                        extensions[i] = CreateCraftTree1Extension(c.Economy);
                    }
                if (c.Economy.CraftTree1 && !hasCraftTree1) extensions.Add(CreateCraftTree1Extension(c.Economy));
                bool hasDropSiteLimit = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == DropSiteLimitExtensionId)
                    {
                        hasDropSiteLimit = true;
                        extensions[i] = CreateDropSiteLimitExtension(c.Economy);
                    }
                if (c.Economy.DropSiteLimit && !hasDropSiteLimit) extensions.Add(CreateDropSiteLimitExtension(c.Economy));
                bool hasAiBelts = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == AiBeltsExtensionId)
                    {
                        hasAiBelts = true;
                        extensions[i] = CreateAiBeltsExtension(c.Economy);
                    }
                if (c.Economy.AiBelts && !hasAiBelts) extensions.Add(CreateAiBeltsExtension(c.Economy));
                bool hasStorageFeedsProcessing = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == StorageFeedsProcessingExtensionId)
                    {
                        hasStorageFeedsProcessing = true;
                        extensions[i] = CreateStorageFeedsProcessingExtension(c.Economy);
                    }
                if (c.Economy.StorageFeedsProcessing && !hasStorageFeedsProcessing)
                    extensions.Add(CreateStorageFeedsProcessingExtension(c.Economy));
                bool hasCraftTree2 = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CraftTree2ExtensionId)
                    {
                        hasCraftTree2 = true;
                        extensions[i] = CreateCraftTree2Extension(c.Economy);
                    }
                if (c.Economy.CraftTree1 && c.Economy.CraftTree2 && !hasCraftTree2) extensions.Add(CreateCraftTree2Extension(c.Economy));
                bool hasCraftTree3 = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == CraftTree3ExtensionId)
                    {
                        hasCraftTree3 = true;
                        extensions[i] = CreateCraftTree3Extension(c.Economy);
                    }
                if (c.Economy.CraftTree1 && c.Economy.CraftTree2 && c.Economy.CraftTree3 && !hasCraftTree3) extensions.Add(CreateCraftTree3Extension(c.Economy));
                bool hasDirectPathMovement = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == DirectPathMovementExtensionId)
                    {
                        hasDirectPathMovement = true;
                        extensions[i] = CreateDirectPathMovementExtension(c.Economy);
                    }
                if (c.Economy.DirectPathMovement && !hasDirectPathMovement) extensions.Add(CreateDirectPathMovementExtension(c.Economy));
                bool hasExploreScouting = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == ExploreScoutingExtensionId)
                    {
                        hasExploreScouting = true;
                        extensions[i] = CreateExploreScoutingExtension(c.Economy);
                    }
                if (c.Economy.ExploreScouting && !hasExploreScouting) extensions.Add(CreateExploreScoutingExtension(c.Economy));
                bool hasAiArmyRecovery = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == AiArmyRecoveryExtensionId)
                    {
                        hasAiArmyRecovery = true;
                        extensions[i] = CreateAiArmyRecoveryExtension(c.Economy);
                    }
                if (c.Economy.AiArmyRecovery && !hasAiArmyRecovery) extensions.Add(CreateAiArmyRecoveryExtension(c.Economy));
                bool hasLineBuildingCheck = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == LineBuildingCheckExtensionId)
                    {
                        hasLineBuildingCheck = true;
                        extensions[i] = CreateLineBuildingCheckExtension(c.Economy);
                    }
                if (c.Economy.LineBuildingCheck && !hasLineBuildingCheck) extensions.Add(CreateLineBuildingCheckExtension(c.Economy));
                bool hasAutoReturnAfterHuman = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == AutoReturnAfterHumanExtensionId)
                    {
                        hasAutoReturnAfterHuman = true;
                        extensions[i] = CreateAutoReturnAfterHumanExtension(c.Economy);
                    }
                if (c.Economy.AutoReturnAfterHuman && !hasAutoReturnAfterHuman)
                    extensions.Add(CreateAutoReturnAfterHumanExtension(c.Economy));
                bool hasPrimitiveCraft = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == PrimitiveCraftExtensionId)
                    { hasPrimitiveCraft = true; extensions[i] = CreatePrimitiveCraftExtension(c.Economy); }
                if (c.Economy.PrimitiveCraft && !hasPrimitiveCraft) extensions.Add(CreatePrimitiveCraftExtension(c.Economy));
                bool hasAiFactory = false;
                for (int i = 0; i < extensions.Count; i++)
                    if (extensions[i] != null && extensions[i].Id == AiFactoryExtensionId)
                    { hasAiFactory = true; extensions[i] = CreateAiFactoryExtension(c.Economy); }
                if (c.Economy.PrimitiveCraft && c.Economy.AiFactory && !hasAiFactory)
                    extensions.Add(CreateAiFactoryExtension(c.Economy));
                return extensions.ToArray();
        }

        private static ScenarioExtensionData CreateDirectPathMovementExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.DirectPathMovement ? 1 : 0);
                return new ScenarioExtensionData { Id = DirectPathMovementExtensionId, Version = DirectPathMovementExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadDirectPathMovementExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid direct path movement flag.");
                e.DirectPathMovement = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing direct path movement extension data.");
            }
        }

        private static ScenarioExtensionData CreateDropSiteLimitExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.DropSiteLimit ? 1 : 0);
                writer.Write(e.DropSiteMax);
                return new ScenarioExtensionData { Id = DropSiteLimitExtensionId, Version = DropSiteLimitExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadDropSiteLimitExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid drop-site-limit flag.");
                e.DropSiteLimit = enabled != 0;
                e.DropSiteMax = reader.ReadInt32();
                if (e.DropSiteMax <= 0) throw new InvalidDataException("Invalid drop-site maximum.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing drop-site-limit extension data.");
            }
        }

        private static ScenarioExtensionData CreateAiBeltsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AiBelts ? 1 : 0);
                writer.Write(e.AiBeltWoodReserve);
                return new ScenarioExtensionData { Id = AiBeltsExtensionId, Version = AiBeltsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAiBeltsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid AI-belts flag.");
                e.AiBelts = enabled != 0;
                e.AiBeltWoodReserve = reader.ReadInt32();
                if (e.AiBeltWoodReserve < 0) throw new InvalidDataException("Invalid AI-belt wood reserve.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing AI-belts extension data.");
            }
        }

        private static ScenarioExtensionData CreateStorageFeedsProcessingExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.StorageFeedsProcessing ? 1 : 0);
                return new ScenarioExtensionData { Id = StorageFeedsProcessingExtensionId,
                    Version = StorageFeedsProcessingExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadStorageFeedsProcessingExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid storage-feeds-processing flag.");
                e.StorageFeedsProcessing = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing storage-feeds-processing extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftTimingExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftTiming ? 1 : 0);
                writer.Write(e.CraftTimingFoodPerWood);
                return new ScenarioExtensionData { Id = CraftTimingExtensionId, Version = CraftTimingExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftTimingExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-timing flag.");
                e.CraftTiming = enabled != 0;
                e.CraftTimingFoodPerWood = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-timing extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftBalanceExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftBalanceTollgateTiming);
                return new ScenarioExtensionData { Id = CraftBalanceExtensionId, Version = CraftBalanceExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftBalanceExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                e.CraftBalanceTollgateTiming = reader.ReadInt32();
                e.CraftBalance = true;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-balance extension data.");
            }
        }

        private static ScenarioExtensionData CreateResourceFogExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.ResourceFog ? 1 : 0);
                return new ScenarioExtensionData { Id = ResourceFogExtensionId, Version = ResourceFogExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadResourceFogExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid resource-fog flag.");
                e.ResourceFog = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing resource-fog extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftTree1Extension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftTree1 ? 1 : 0);
                writer.Write(e.CraftTreePlankWoodInput); writer.Write(e.CraftTreePlankTicks);
                writer.Write(e.CraftTreeCharcoalWoodInput); writer.Write(e.CraftTreeCharcoalTicks);
                writer.Write(e.StonecutterSizeCells); writer.Write(e.StonecutterWoodCost); writer.Write(e.StonecutterWork); writer.Write(e.StonecutterHp);
                writer.Write(e.CraftTreeStoneInput); writer.Write(e.CraftTreeStonecutterTicks);
                writer.Write(e.JoinerySizeCells); writer.Write(e.JoineryWoodCost); writer.Write(e.JoineryWork); writer.Write(e.JoineryHp);
                writer.Write(e.CraftTreeJoineryTicks); writer.Write(e.CraftTreeJoineryPlankInput); writer.Write(e.CraftTreeJoineryCutStoneInput);
                writer.Write(e.CraftTreeFramesPerBuilding); writer.Write(e.CraftTreeFrameWoodPermille); writer.Write(e.CraftTreeFrameWorkPermille);
                writer.Write(e.CraftTreeLevel2FramesCost);
                return new ScenarioExtensionData { Id = CraftTree1ExtensionId, Version = CraftTree1ExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftTree1Extension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-tree flag.");
                e.CraftTree1 = enabled != 0;
                e.CraftTreePlankWoodInput = reader.ReadInt32(); e.CraftTreePlankTicks = reader.ReadInt32();
                e.CraftTreeCharcoalWoodInput = reader.ReadInt32(); e.CraftTreeCharcoalTicks = reader.ReadInt32();
                e.StonecutterSizeCells = reader.ReadInt32(); e.StonecutterWoodCost = reader.ReadInt32();
                e.StonecutterWork = reader.ReadInt32(); e.StonecutterHp = reader.ReadInt32();
                e.CraftTreeStoneInput = reader.ReadInt32(); e.CraftTreeStonecutterTicks = reader.ReadInt32();
                e.JoinerySizeCells = reader.ReadInt32(); e.JoineryWoodCost = reader.ReadInt32();
                e.JoineryWork = reader.ReadInt32(); e.JoineryHp = reader.ReadInt32();
                e.CraftTreeJoineryTicks = reader.ReadInt32(); e.CraftTreeJoineryPlankInput = reader.ReadInt32();
                e.CraftTreeJoineryCutStoneInput = reader.ReadInt32(); e.CraftTreeFramesPerBuilding = reader.ReadInt32();
                e.CraftTreeFrameWoodPermille = reader.ReadInt32(); e.CraftTreeFrameWorkPermille = reader.ReadInt32();
                e.CraftTreeLevel2FramesCost = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-tree extension data.");
            }
        }

        private static ScenarioExtensionData CreatePrimitiveCraftExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.PrimitiveCraft ? 1 : 0);
                writer.Write(e.HutSizeCells); writer.Write(e.HutWoodCost); writer.Write(e.HutWork); writer.Write(e.HutHp);
                writer.Write(e.PrimitiveCraftTicks); writer.Write(e.StoneToolsStoneInput); writer.Write(e.StoneToolsWoodInput);
                writer.Write(e.JerkyFoodInput); writer.Write(e.JerkyOutput); writer.Write(e.StoneToolsGatherIntervalPermille); writer.Write(e.JerkyHpPermille);
                return new ScenarioExtensionData { Id = PrimitiveCraftExtensionId, Version = PrimitiveCraftExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadPrimitiveCraftExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid primitive-craft flag.");
                e.PrimitiveCraft = enabled != 0;
                e.HutSizeCells = reader.ReadInt32(); e.HutWoodCost = reader.ReadInt32(); e.HutWork = reader.ReadInt32(); e.HutHp = reader.ReadInt32();
                e.PrimitiveCraftTicks = reader.ReadInt32(); e.StoneToolsStoneInput = reader.ReadInt32(); e.StoneToolsWoodInput = reader.ReadInt32();
                e.JerkyFoodInput = reader.ReadInt32(); e.JerkyOutput = reader.ReadInt32(); e.StoneToolsGatherIntervalPermille = reader.ReadInt32(); e.JerkyHpPermille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing primitive-craft extension data.");
            }
        }

        private static ScenarioExtensionData CreateAiFactoryExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AiFactory ? 1 : 0);
                writer.Write(e.AiFactoryPrimitiveLineCount); writer.Write(e.AiFactoryAge1LineCount);
                writer.Write(e.AiFactoryAge2LineCount); writer.Write(e.AiFactoryAge3LineCount);
                writer.Write(e.AiFactoryBarracksIdleWindowTicks); writer.Write(e.AiFactoryBarracksIdlePermille);
                writer.Write(e.AiFactoryPrimitiveBarracksMaxBuildings); writer.Write(e.AiFactoryAge1BarracksMaxBuildings);
                writer.Write(e.AiFactoryAge2BarracksMaxBuildings); writer.Write(e.AiFactoryAge3BarracksMaxBuildings);
                writer.Write(e.AiFactoryAge4BarracksMaxBuildings);
                return new ScenarioExtensionData { Id = AiFactoryExtensionId, Version = AiFactoryExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAiFactoryExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid AI-factory flag.");
                e.AiFactory = enabled != 0;
                e.AiFactoryPrimitiveLineCount = reader.ReadInt32(); e.AiFactoryAge1LineCount = reader.ReadInt32();
                e.AiFactoryAge2LineCount = reader.ReadInt32(); e.AiFactoryAge3LineCount = reader.ReadInt32();
                e.AiFactoryBarracksIdleWindowTicks = reader.ReadInt32(); e.AiFactoryBarracksIdlePermille = reader.ReadInt32();
                e.AiFactoryPrimitiveBarracksMaxBuildings = reader.ReadInt32(); e.AiFactoryAge1BarracksMaxBuildings = reader.ReadInt32();
                e.AiFactoryAge2BarracksMaxBuildings = reader.ReadInt32(); e.AiFactoryAge3BarracksMaxBuildings = reader.ReadInt32();
                e.AiFactoryAge4BarracksMaxBuildings = reader.ReadInt32();
                if (e.AiFactoryPrimitiveLineCount < 0 || e.AiFactoryAge1LineCount < 0 || e.AiFactoryAge2LineCount < 0 || e.AiFactoryAge3LineCount < 0
                    || e.AiFactoryBarracksIdleWindowTicks <= 0 || e.AiFactoryBarracksIdlePermille < 0 || e.AiFactoryBarracksIdlePermille > 1000
                    || e.AiFactoryPrimitiveBarracksMaxBuildings < 0 || e.AiFactoryAge1BarracksMaxBuildings < 0
                    || e.AiFactoryAge2BarracksMaxBuildings < 0 || e.AiFactoryAge3BarracksMaxBuildings < 0 || e.AiFactoryAge4BarracksMaxBuildings < 0)
                    throw new InvalidDataException("Invalid AI-factory parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing AI-factory extension data.");
            }
        }

        private static ScenarioExtensionData CreateAiExpandExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AiExpand ? 1 : 0);
                writer.Write(e.AiExpandAge1TownCount); writer.Write(e.AiExpandAge2TownCount);
                writer.Write(e.AiExpandAge3TownCount); writer.Write(e.AiExpandDecisionIntervalTicks);
                writer.Write(e.AiExpandUnreachableWorkTimeoutTicks);
                writer.Write(e.AiExpandShareMinVillagers); writer.Write(e.AiExpandFoodWorkerPermille);
                writer.Write(e.AiExpandWoodWorkerPermille); writer.Write(e.AiExpandStoneWorkerPermille);
                writer.Write(e.AiExpandTownStoneWorkerPermille); writer.Write(e.AiExpandPrimitiveFoodWorkerPermille);
                return new ScenarioExtensionData { Id = AiExpandExtensionId, Version = AiExpandExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAiExpandExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid AI-expand flag.");
                e.AiExpand = enabled != 0;
                e.AiExpandAge1TownCount = reader.ReadInt32(); e.AiExpandAge2TownCount = reader.ReadInt32();
                e.AiExpandAge3TownCount = reader.ReadInt32(); e.AiExpandDecisionIntervalTicks = reader.ReadInt32();
                e.AiExpandUnreachableWorkTimeoutTicks = reader.ReadInt32();
                e.AiExpandShareMinVillagers = reader.ReadInt32(); e.AiExpandFoodWorkerPermille = reader.ReadInt32();
                e.AiExpandWoodWorkerPermille = reader.ReadInt32(); e.AiExpandStoneWorkerPermille = reader.ReadInt32();
                e.AiExpandTownStoneWorkerPermille = reader.ReadInt32(); e.AiExpandPrimitiveFoodWorkerPermille = reader.ReadInt32();
                if (e.AiExpandAge1TownCount < 0 || e.AiExpandAge2TownCount < e.AiExpandAge1TownCount
                    || e.AiExpandAge3TownCount < e.AiExpandAge2TownCount || e.AiExpandDecisionIntervalTicks <= 0
                    || e.AiExpandUnreachableWorkTimeoutTicks <= 0
                    || e.AiExpandShareMinVillagers < 0 || e.AiExpandFoodWorkerPermille < 0 || e.AiExpandFoodWorkerPermille > 1000
                     || e.AiExpandWoodWorkerPermille < 0 || e.AiExpandWoodWorkerPermille > 1000
                     || e.AiExpandStoneWorkerPermille < 0 || e.AiExpandStoneWorkerPermille > 1000
                     || e.AiExpandTownStoneWorkerPermille < 0 || e.AiExpandTownStoneWorkerPermille > 1000
                     || e.AiExpandPrimitiveFoodWorkerPermille < 0 || e.AiExpandPrimitiveFoodWorkerPermille > 1000)
                    throw new InvalidDataException("Invalid AI-expand parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing AI-expand extension data.");
            }
        }

        private static ScenarioExtensionData CreateOpenFarmsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.OpenFarms ? 1 : 0);
                writer.Write(e.OpenFarmsOtherCivExtraTicks);
                writer.Write(e.OpenFarmsPrimitiveAiFarms);
                writer.Write(e.OpenFarmsOtherCivAiFarms);
                return new ScenarioExtensionData { Id = OpenFarmsExtensionId, Version = OpenFarmsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadOpenFarmsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid open-farms flag.");
                e.OpenFarms = enabled != 0;
                e.OpenFarmsOtherCivExtraTicks = reader.ReadInt32();
                e.OpenFarmsPrimitiveAiFarms = reader.ReadInt32();
                e.OpenFarmsOtherCivAiFarms = reader.ReadInt32();
                if (e.OpenFarmsOtherCivExtraTicks <= 0 || e.OpenFarmsPrimitiveAiFarms < 0 || e.OpenFarmsOtherCivAiFarms < 0)
                    throw new InvalidDataException("Invalid open-farms parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing open-farms extension data.");
            }
        }

        private static ScenarioExtensionData CreateFreeTownsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.FreeTowns ? 1 : 0);
                writer.Write(e.FreeTownsPerAge);
                writer.Write(e.FreeTownsAiVillagersPerTown);
                writer.Write(e.FreeTownsAiTownBuilders);
                writer.Write(e.FreeTownsRouteRecheckTicks);
                return new ScenarioExtensionData { Id = FreeTownsExtensionId, Version = FreeTownsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadFreeTownsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid free-towns flag.");
                e.FreeTowns = enabled != 0;
                e.FreeTownsPerAge = reader.ReadInt32();
                e.FreeTownsAiVillagersPerTown = reader.ReadInt32();
                e.FreeTownsAiTownBuilders = reader.ReadInt32();
                e.FreeTownsRouteRecheckTicks = reader.ReadInt32();
                if (e.FreeTownsPerAge <= 0 || e.FreeTownsAiVillagersPerTown <= 0 || e.FreeTownsAiTownBuilders <= 0
                    || e.FreeTownsRouteRecheckTicks <= 0)
                    throw new InvalidDataException("Invalid free-towns parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing free-towns extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftRequiredExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftRequired ? 1 : 0);
                writer.Write(e.CraftRequiredAiArmsStockTarget);
                writer.Write(e.CraftRequiredAiArmsAssemblersAge1);
                writer.Write(e.CraftRequiredAiArmsAssemblersAge2);
                writer.Write(e.CraftRequiredAiArmsAssemblersAge3);
                writer.Write(e.CraftRequiredAiArmsAssemblersAge4);
                writer.Write(e.CraftRequiredAiWoodWorkerPermille);
                writer.Write(e.CraftRequiredBuildingPlankPermille);
                writer.Write(e.CraftRequiredResearchScholarlyAge2);
                writer.Write(e.CraftRequiredResearchScholarlyAge3);
                writer.Write(e.CraftRequiredResearchScholarlyAge4);
                writer.Write(e.CraftRequiredAiPlankSawmillsAge1);
                writer.Write(e.CraftRequiredAiPlankSawmillsAge2);
                writer.Write(e.CraftRequiredAiPlankSawmillsAge3);
                 writer.Write(e.CraftRequiredAiScholarlyAssemblers);
                 writer.Write(e.CraftRequiredAiFramesStockTarget);
                 writer.Write(e.CraftRequiredAiScholarlyStockResearches);
                 writer.Write(e.CraftRequiredAiScholarlyMinAssemblers);
                return new ScenarioExtensionData { Id = CraftRequiredExtensionId,
                    Version = CraftRequiredExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftRequiredExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-required flag.");
                e.CraftRequired = enabled != 0;
                e.CraftRequiredAiArmsStockTarget = reader.ReadInt32();
                e.CraftRequiredAiArmsAssemblersAge1 = reader.ReadInt32();
                e.CraftRequiredAiArmsAssemblersAge2 = reader.ReadInt32();
                e.CraftRequiredAiArmsAssemblersAge3 = reader.ReadInt32();
                e.CraftRequiredAiArmsAssemblersAge4 = reader.ReadInt32();
                e.CraftRequiredAiWoodWorkerPermille = stream.Position == stream.Length ? 50 : reader.ReadInt32();
                if (stream.Position != stream.Length)
                {
                    e.CraftRequiredBuildingPlankPermille = reader.ReadInt32();
                    e.CraftRequiredResearchScholarlyAge2 = reader.ReadInt32();
                    e.CraftRequiredResearchScholarlyAge3 = reader.ReadInt32();
                    e.CraftRequiredResearchScholarlyAge4 = reader.ReadInt32();
                    e.CraftRequiredAiPlankSawmillsAge1 = reader.ReadInt32();
                    e.CraftRequiredAiPlankSawmillsAge2 = reader.ReadInt32();
                     e.CraftRequiredAiPlankSawmillsAge3 = reader.ReadInt32();
                     e.CraftRequiredAiScholarlyAssemblers = reader.ReadInt32();
                     e.CraftRequiredAiFramesStockTarget = reader.ReadInt32();
                     if (stream.Position != stream.Length) e.CraftRequiredAiScholarlyStockResearches = reader.ReadInt32();
                     if (stream.Position != stream.Length) e.CraftRequiredAiScholarlyMinAssemblers = reader.ReadInt32();
                 }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-required extension data.");
            }
        }

        private static ScenarioExtensionData CreateArmsScaleExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.ArmsScale ? 1 : 0);
                writer.Write(e.ArmsScaleDecisionIntervalTicks);
                writer.Write(e.ArmsScaleArmsStockLow);
                writer.Write(e.ArmsScaleFoodReserve);
                writer.Write(e.ArmsScaleWoodReserve);
                writer.Write(e.ArmsScaleMaxArmsAssemblersAge2);
                writer.Write(e.ArmsScaleMaxArmsAssemblersAge3);
                writer.Write(e.ArmsScaleMaxArmsAssemblersAge4);
                writer.Write(e.ArmsScalePlankLow);
                writer.Write(e.ArmsScaleExtraSawmillsAge2);
                writer.Write(e.ArmsScaleExtraSawmillsAge3);
                writer.Write(e.ArmsScaleExtraSawmillsAge4);
                return new ScenarioExtensionData { Id = ArmsScaleExtensionId, Version = ArmsScaleExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadArmsScaleExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid arms-scale flag.");
                e.ArmsScale = enabled != 0;
                e.ArmsScaleDecisionIntervalTicks = reader.ReadInt32();
                e.ArmsScaleArmsStockLow = reader.ReadInt32();
                e.ArmsScaleFoodReserve = reader.ReadInt32();
                e.ArmsScaleWoodReserve = reader.ReadInt32();
                e.ArmsScaleMaxArmsAssemblersAge2 = reader.ReadInt32();
                e.ArmsScaleMaxArmsAssemblersAge3 = reader.ReadInt32();
                e.ArmsScaleMaxArmsAssemblersAge4 = reader.ReadInt32();
                e.ArmsScalePlankLow = reader.ReadInt32();
                e.ArmsScaleExtraSawmillsAge2 = reader.ReadInt32();
                e.ArmsScaleExtraSawmillsAge3 = reader.ReadInt32();
                e.ArmsScaleExtraSawmillsAge4 = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing arms-scale extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftDepthExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftDepth ? 1 : 0);
                writer.Write(e.CraftDepthFittingsTicks); writer.Write(e.CraftDepthFittingsMetalInput); writer.Write(e.CraftDepthFittingsOutput);
                writer.Write(e.CraftDepthRopeTicks); writer.Write(e.CraftDepthRopeWoodInput); writer.Write(e.CraftDepthRopeOutput);
                writer.Write(e.CraftDepthMetalworksSizeCells); writer.Write(e.CraftDepthMetalworksWoodCost); writer.Write(e.CraftDepthMetalworksWork); writer.Write(e.CraftDepthMetalworksHp);
                writer.Write(e.CraftDepthRopeHutSizeCells); writer.Write(e.CraftDepthRopeHutWoodCost); writer.Write(e.CraftDepthRopeHutWork); writer.Write(e.CraftDepthRopeHutHp);
                writer.Write(e.CraftDepthAiFittingsStockTarget); writer.Write(e.CraftDepthAiRopeStockTarget); writer.Write(e.CraftDepthAiStockCap);
                writer.Write(e.CraftDepthFramesRopeInput);
                writer.Write(e.CraftDepthSiegePartsFittingsInput); writer.Write(e.CraftDepthSiegePartsRopeInput); writer.Write(e.CraftDepthSiegePartsTicks);
                writer.Write(e.CraftDepthMechanismFittingsInput);
                return new ScenarioExtensionData { Id = CraftDepthExtensionId, Version = CraftDepthExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftDepthExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-depth flag.");
                e.CraftDepth = enabled != 0;
                e.CraftDepthFittingsTicks = reader.ReadInt32(); e.CraftDepthFittingsMetalInput = reader.ReadInt32(); e.CraftDepthFittingsOutput = reader.ReadInt32();
                e.CraftDepthRopeTicks = reader.ReadInt32(); e.CraftDepthRopeWoodInput = reader.ReadInt32(); e.CraftDepthRopeOutput = reader.ReadInt32();
                e.CraftDepthMetalworksSizeCells = reader.ReadInt32(); e.CraftDepthMetalworksWoodCost = reader.ReadInt32(); e.CraftDepthMetalworksWork = reader.ReadInt32(); e.CraftDepthMetalworksHp = reader.ReadInt32();
                e.CraftDepthRopeHutSizeCells = reader.ReadInt32(); e.CraftDepthRopeHutWoodCost = reader.ReadInt32(); e.CraftDepthRopeHutWork = reader.ReadInt32(); e.CraftDepthRopeHutHp = reader.ReadInt32();
                e.CraftDepthAiFittingsStockTarget = reader.ReadInt32(); e.CraftDepthAiRopeStockTarget = reader.ReadInt32(); e.CraftDepthAiStockCap = reader.ReadInt32();
                e.CraftDepthFramesRopeInput = reader.ReadInt32();
                e.CraftDepthSiegePartsFittingsInput = reader.ReadInt32(); e.CraftDepthSiegePartsRopeInput = reader.ReadInt32(); e.CraftDepthSiegePartsTicks = reader.ReadInt32();
                e.CraftDepthMechanismFittingsInput = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-depth extension data.");
            }
        }

        private static ScenarioExtensionData CreateEarlyStartExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.EarlyStart ? 1 : 0);
                writer.Write(e.EarlyStartVillagers);
                writer.Write(e.EarlyStartVillagersPerFarm);
                writer.Write(e.EarlyStartPrimitiveFarmMax);
                return new ScenarioExtensionData { Id = EarlyStartExtensionId, Version = EarlyStartExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadEarlyStartExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid early-start flag.");
                e.EarlyStart = enabled != 0;
                e.EarlyStartVillagers = reader.ReadInt32();
                e.EarlyStartVillagersPerFarm = reader.ReadInt32();
                e.EarlyStartPrimitiveFarmMax = reader.ReadInt32();
                if (e.EarlyStartVillagers < 3 || e.EarlyStartVillagersPerFarm <= 0 || e.EarlyStartPrimitiveFarmMax <= 0)
                    throw new InvalidDataException("Invalid early-start parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing early-start extension data.");
            }
        }

        private static ScenarioExtensionData CreateFastMovementExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.FastMovement ? 1 : 0);
                writer.Write(e.FastMovementUnitSpeedPermille);
                writer.Write(e.FastMovementVillagerSpeedPermille);
                return new ScenarioExtensionData { Id = FastMovementExtensionId, Version = FastMovementExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadFastMovementExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid fast-movement flag.");
                e.FastMovement = enabled != 0;
                e.FastMovementUnitSpeedPermille = reader.ReadInt32();
                e.FastMovementVillagerSpeedPermille = reader.ReadInt32();
                if (e.FastMovementUnitSpeedPermille <= 0 || e.FastMovementVillagerSpeedPermille <= 0)
                    throw new InvalidDataException("Invalid fast-movement parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing fast-movement extension data.");
            }
        }

        private static ScenarioExtensionData CreateBeltDropOffExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.BeltDropOff ? 1 : 0);
                writer.Write(e.BeltDropOffRangeCells);
                writer.Write(e.BeltDropOffWaitTicks);
                // Without the AI the first version is written, so scenarios and replays from before it keep
                // exactly the same bytes (and the same configuration hash).
                if (!e.BeltDropOffAi)
                    return new ScenarioExtensionData { Id = BeltDropOffExtensionId, Version = BeltDropOffExtensionV1, Data = stream.ToArray() };
                writer.Write(1);
                writer.Write(e.BeltDropOffAiMinGatherers);
                writer.Write(e.BeltDropOffAiMaxRouteCells);
                writer.Write(e.BeltDropOffAiMaxLinesAge0);
                writer.Write(e.BeltDropOffAiMaxLinesAge1);
                writer.Write(e.BeltDropOffAiMaxLinesAge2);
                writer.Write(e.BeltDropOffAiMaxLinesAge3);
                return new ScenarioExtensionData { Id = BeltDropOffExtensionId, Version = BeltDropOffExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadBeltDropOffExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid belt-dropoff flag.");
                e.BeltDropOff = enabled != 0;
                e.BeltDropOffRangeCells = reader.ReadInt32();
                e.BeltDropOffWaitTicks = reader.ReadInt32();
                if (e.BeltDropOffRangeCells < 0 || e.BeltDropOffWaitTicks < 0)
                    throw new InvalidDataException("Invalid belt-dropoff parameters.");
                if (extension.Version >= BeltDropOffExtensionVersion)
                {
                    int aiEnabled = reader.ReadInt32();
                    if (aiEnabled < 0 || aiEnabled > 1) throw new InvalidDataException("Invalid belt-dropoff AI flag.");
                    e.BeltDropOffAi = aiEnabled != 0;
                    e.BeltDropOffAiMinGatherers = reader.ReadInt32();
                    e.BeltDropOffAiMaxRouteCells = reader.ReadInt32();
                    e.BeltDropOffAiMaxLinesAge0 = reader.ReadInt32();
                    e.BeltDropOffAiMaxLinesAge1 = reader.ReadInt32();
                    e.BeltDropOffAiMaxLinesAge2 = reader.ReadInt32();
                    e.BeltDropOffAiMaxLinesAge3 = reader.ReadInt32();
                    if (e.BeltDropOffAiMinGatherers < 0 || e.BeltDropOffAiMaxRouteCells < 0
                        || e.BeltDropOffAiMaxLinesAge0 < 0 || e.BeltDropOffAiMaxLinesAge1 < 0
                        || e.BeltDropOffAiMaxLinesAge2 < 0 || e.BeltDropOffAiMaxLinesAge3 < 0)
                        throw new InvalidDataException("Invalid belt-dropoff AI parameters.");
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing belt-dropoff extension data.");
            }
        }

        private static ScenarioExtensionData CreateBeltStackExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.BeltStack ? 1 : 0);
                writer.Write(e.BeltStackSize);
                writer.Write(e.BeltStackAiMaxLinesAge0); writer.Write(e.BeltStackAiMaxLinesAge1);
                writer.Write(e.BeltStackAiMaxLinesAge2); writer.Write(e.BeltStackAiMaxLinesAge3);
                writer.Write(e.BeltStackAiMaxRouteCells);
                return new ScenarioExtensionData { Id = BeltStackExtensionId, Version = BeltStackExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadBeltStackExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid belt-stack flag.");
                e.BeltStack = enabled != 0;
                e.BeltStackSize = reader.ReadInt32();
                e.BeltStackAiMaxLinesAge0 = reader.ReadInt32(); e.BeltStackAiMaxLinesAge1 = reader.ReadInt32();
                e.BeltStackAiMaxLinesAge2 = reader.ReadInt32(); e.BeltStackAiMaxLinesAge3 = reader.ReadInt32();
                e.BeltStackAiMaxRouteCells = reader.ReadInt32();
                if (e.BeltStackSize < 1 || e.BeltStackSize > 16 || e.BeltStackAiMaxLinesAge0 < 0
                    || e.BeltStackAiMaxLinesAge1 < 0 || e.BeltStackAiMaxLinesAge2 < 0 || e.BeltStackAiMaxLinesAge3 < 0
                    || e.BeltStackAiMaxRouteCells < 0) throw new InvalidDataException("Invalid belt-stack parameters.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing belt-stack extension data.");
            }
        }

        private static ScenarioExtensionData CreateBeltCraftExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.BeltCraft ? 1 : 0);
                writer.Write(e.GearWorkshopSizeCells); writer.Write(e.GearWorkshopWoodCost); writer.Write(e.GearWorkshopWork); writer.Write(e.GearWorkshopHp);
                writer.Write(e.GearMetalInput); writer.Write(e.GearsPerBatch); writer.Write(e.GearTicks);
                writer.Write(e.FastBeltGears); writer.Write(e.SplitterGears); writer.Write(e.SorterGears); writer.Write(e.UndergroundBeltGears); writer.Write(e.InserterGears);
                writer.Write(e.MergerGears); writer.Write(e.MergerPlanks); writer.Write(e.VeryFastBeltGears); writer.Write(e.VeryFastBeltSteel);
                writer.Write(e.VeryFastBeltTicksPerCell); writer.Write(e.VeryFastBeltMinAge); writer.Write(e.BeltCraftAiGearsStockTarget);
                bool legacy = e.FilterInserterGears == 3 && e.LongInserterGears == 4 && e.CatapultGears == 20 && e.CatapultPlanks == 20
                    && e.CatapultFittings == 10 && e.CatapultRope == 10 && e.CatapultSteel == 10 && e.CatapultReceiverGears == 5
                    && e.CatapultReceiverPlanks == 10 && e.CatapultMinAge == 2 && e.CatapultRangeCells == 30 && e.CatapultLoad == 12
                    && e.CatapultMaxWaitTicks == 40 && e.CatapultIntervalTicks == 20 && e.CatapultFlightTicks == 30 && e.CatapultBuffer == 24;
                if (legacy) return new ScenarioExtensionData { Id = BeltCraftExtensionId, Version = BeltCraftExtensionV1, Data = stream.ToArray() };
                writer.Write(e.FilterInserterGears); writer.Write(e.LongInserterGears); writer.Write(e.CatapultGears); writer.Write(e.CatapultPlanks);
                writer.Write(e.CatapultFittings); writer.Write(e.CatapultRope); writer.Write(e.CatapultSteel); writer.Write(e.CatapultReceiverGears);
                writer.Write(e.CatapultReceiverPlanks); writer.Write(e.CatapultMinAge); writer.Write(e.CatapultRangeCells); writer.Write(e.CatapultLoad);
                writer.Write(e.CatapultMaxWaitTicks); writer.Write(e.CatapultIntervalTicks); writer.Write(e.CatapultFlightTicks); writer.Write(e.CatapultBuffer);
                return new ScenarioExtensionData { Id = BeltCraftExtensionId, Version = BeltCraftExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadBeltCraftExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid belt-craft flag.");
                e.BeltCraft = enabled != 0;
                e.GearWorkshopSizeCells = reader.ReadInt32(); e.GearWorkshopWoodCost = reader.ReadInt32();
                e.GearWorkshopWork = reader.ReadInt32(); e.GearWorkshopHp = reader.ReadInt32();
                e.GearMetalInput = reader.ReadInt32(); e.GearsPerBatch = reader.ReadInt32(); e.GearTicks = reader.ReadInt32();
                e.FastBeltGears = reader.ReadInt32(); e.SplitterGears = reader.ReadInt32(); e.SorterGears = reader.ReadInt32();
                e.UndergroundBeltGears = reader.ReadInt32(); e.InserterGears = reader.ReadInt32(); e.MergerGears = reader.ReadInt32();
                e.MergerPlanks = reader.ReadInt32(); e.VeryFastBeltGears = reader.ReadInt32(); e.VeryFastBeltSteel = reader.ReadInt32();
                e.VeryFastBeltTicksPerCell = reader.ReadInt32(); e.VeryFastBeltMinAge = reader.ReadInt32(); e.BeltCraftAiGearsStockTarget = reader.ReadInt32();
                if (extension.Version >= BeltCraftExtensionVersion)
                {
                    e.FilterInserterGears = reader.ReadInt32(); e.LongInserterGears = reader.ReadInt32(); e.CatapultGears = reader.ReadInt32(); e.CatapultPlanks = reader.ReadInt32();
                    e.CatapultFittings = reader.ReadInt32(); e.CatapultRope = reader.ReadInt32(); e.CatapultSteel = reader.ReadInt32(); e.CatapultReceiverGears = reader.ReadInt32();
                    e.CatapultReceiverPlanks = reader.ReadInt32(); e.CatapultMinAge = reader.ReadInt32(); e.CatapultRangeCells = reader.ReadInt32(); e.CatapultLoad = reader.ReadInt32();
                    e.CatapultMaxWaitTicks = reader.ReadInt32(); e.CatapultIntervalTicks = reader.ReadInt32(); e.CatapultFlightTicks = reader.ReadInt32(); e.CatapultBuffer = reader.ReadInt32();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing belt-craft extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftFlowExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftFlow ? 1 : 0);
                return new ScenarioExtensionData { Id = CraftFlowExtensionId, Version = CraftFlowExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftFlowExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-flow flag.");
                e.CraftFlow = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-flow extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftTree2Extension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftTree2 ? 1 : 0);
                writer.Write(e.AssemblerSizeCells); writer.Write(e.AssemblerWoodCost); writer.Write(e.AssemblerWork); writer.Write(e.AssemblerHp);
                writer.Write(e.ArmsKitTicks); writer.Write(e.ArmsKitPlankInput); writer.Write(e.ArmsKitCharcoalInput); writer.Write(e.ArmsKitSteelInput);
                writer.Write(e.ScholarlyTicks); writer.Write(e.ScholarlyPlankInput); writer.Write(e.ScholarlyCharcoalInput); writer.Write(e.ScholarlyFoodInput);
                writer.Write(e.ArmsKitAttack); writer.Write(e.ArmsKitHpPermille); writer.Write(e.FineArmsKitAttack); writer.Write(e.FineArmsKitHpPermille);
                writer.Write(e.ScholarlyPerResearch); writer.Write(e.ScholarlyCostPermille); writer.Write(e.ScholarlyTimePermille);
                writer.Write(e.GoodsValueArmsKit); writer.Write(e.GoodsValueScholarly); writer.Write(e.GoodsValueFineArmsKit);
                return new ScenarioExtensionData { Id = CraftTree2ExtensionId, Version = CraftTree2ExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftTree2Extension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-tree2 flag.");
                e.CraftTree2 = enabled != 0 && e.CraftTree1;
                e.AssemblerSizeCells = reader.ReadInt32(); e.AssemblerWoodCost = reader.ReadInt32();
                e.AssemblerWork = reader.ReadInt32(); e.AssemblerHp = reader.ReadInt32();
                e.ArmsKitTicks = reader.ReadInt32(); e.ArmsKitPlankInput = reader.ReadInt32();
                e.ArmsKitCharcoalInput = reader.ReadInt32(); e.ArmsKitSteelInput = reader.ReadInt32();
                e.ScholarlyTicks = reader.ReadInt32(); e.ScholarlyPlankInput = reader.ReadInt32();
                e.ScholarlyCharcoalInput = reader.ReadInt32(); e.ScholarlyFoodInput = reader.ReadInt32();
                e.ArmsKitAttack = reader.ReadInt32(); e.ArmsKitHpPermille = reader.ReadInt32();
                e.FineArmsKitAttack = reader.ReadInt32(); e.FineArmsKitHpPermille = reader.ReadInt32();
                e.ScholarlyPerResearch = reader.ReadInt32(); e.ScholarlyCostPermille = reader.ReadInt32();
                e.ScholarlyTimePermille = reader.ReadInt32();
                e.GoodsValueArmsKit = reader.ReadInt32(); e.GoodsValueScholarly = reader.ReadInt32(); e.GoodsValueFineArmsKit = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-tree2 extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftTree3Extension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftTree3 ? 1 : 0);
                writer.Write(e.SiegePartsTicks); writer.Write(e.SiegePartsFramesInput); writer.Write(e.SiegePartsArmsKitInput);
                writer.Write(e.MechanismTicks); writer.Write(e.MechanismArmsKitInput); writer.Write(e.MechanismScholarlyInput); writer.Write(e.MechanismCutStoneInput);
                writer.Write(e.SiegePartsHpPermille); writer.Write(e.SiegePartsDamagePermille); writer.Write(e.SiegePartsPerCastle); writer.Write(e.SiegePartsCastleStonePermille);
                writer.Write(e.MechanismBuildingTimePermille); writer.Write(e.MechanismBuildingMaterialPermille);
                writer.Write(e.MechanismResearchCostPermille); writer.Write(e.MechanismResearchTimePermille);
                writer.Write(e.SiegePartsStockLimit);
                writer.Write(e.GoodsValueSiegeParts); writer.Write(e.GoodsValueMechanism);
                return new ScenarioExtensionData { Id = CraftTree3ExtensionId, Version = CraftTree3ExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftTree3Extension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-tree3 flag.");
                e.CraftTree3 = enabled != 0 && e.CraftTree1 && e.CraftTree2;
                e.SiegePartsTicks = reader.ReadInt32(); e.SiegePartsFramesInput = reader.ReadInt32(); e.SiegePartsArmsKitInput = reader.ReadInt32();
                e.MechanismTicks = reader.ReadInt32(); e.MechanismArmsKitInput = reader.ReadInt32(); e.MechanismScholarlyInput = reader.ReadInt32(); e.MechanismCutStoneInput = reader.ReadInt32();
                e.SiegePartsHpPermille = reader.ReadInt32(); e.SiegePartsDamagePermille = reader.ReadInt32(); e.SiegePartsPerCastle = reader.ReadInt32(); e.SiegePartsCastleStonePermille = reader.ReadInt32();
                e.MechanismBuildingTimePermille = reader.ReadInt32(); e.MechanismBuildingMaterialPermille = reader.ReadInt32();
                e.MechanismResearchCostPermille = reader.ReadInt32(); e.MechanismResearchTimePermille = reader.ReadInt32();
                if (extension.Version >= CraftTree3ExtensionVersion) e.SiegePartsStockLimit = reader.ReadInt32();
                e.GoodsValueSiegeParts = reader.ReadInt32(); e.GoodsValueMechanism = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-tree3 extension data.");
            }
        }

        private static ScenarioExtensionData CreateToolsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Tools ? 1 : 0);
                writer.Write(e.ToolShopSizeCells); writer.Write(e.ToolShopWoodCost); writer.Write(e.ToolShopWork); writer.Write(e.ToolShopHp);
                writer.Write(e.ToolTicks); writer.Write(e.ToolWoodInput); writer.Write(e.ToolKeepWood); writer.Write(e.ToolStockLimit);
                writer.Write(e.ToolMaxShops); writer.Write(e.ToolWoodReserve); writer.Write(e.ToolVillagerFoodPermille); writer.Write(e.ToolVillagerTicksPermille);
                return new ScenarioExtensionData { Id = ToolsExtensionId, Version = ToolsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadToolsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid tools flag.");
                e.Tools = enabled != 0;
                e.ToolShopSizeCells = reader.ReadInt32(); e.ToolShopWoodCost = reader.ReadInt32(); e.ToolShopWork = reader.ReadInt32(); e.ToolShopHp = reader.ReadInt32();
                e.ToolTicks = reader.ReadInt32(); e.ToolWoodInput = reader.ReadInt32(); e.ToolKeepWood = reader.ReadInt32(); e.ToolStockLimit = reader.ReadInt32();
                e.ToolMaxShops = reader.ReadInt32(); e.ToolWoodReserve = reader.ReadInt32(); e.ToolVillagerFoodPermille = reader.ReadInt32(); e.ToolVillagerTicksPermille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing tools extension data.");
            }
        }

        private static ScenarioExtensionData CreateGoodsMarketExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.GoodsMarket ? 1 : 0);
                writer.Write(e.GoodsTradeLot); writer.Write(e.GoodsAutoSellAt); writer.Write(e.GoodsAutoSellIntervalTicks);
                writer.Write(e.GoodsAutoKeep); writer.Write(e.GoodsMarketWoodReserve);
                writer.Write(e.GoodsValueSteel); writer.Write(e.GoodsValueBowGear); writer.Write(e.GoodsValueRations);
                writer.Write(e.GoodsValueBricks); writer.Write(e.GoodsValueTack); writer.Write(e.GoodsValuePlanks);
                writer.Write(e.GoodsValueBooks); writer.Write(e.GoodsValueIncense); writer.Write(e.GoodsValueCharms);
                writer.Write(e.GoodsValueRepairKits); writer.Write(e.GoodsValueTools);
                return new ScenarioExtensionData { Id = GoodsMarketExtensionId, Version = GoodsMarketExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadGoodsMarketExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid goods market flag.");
                e.GoodsMarket = enabled != 0;
                e.GoodsTradeLot = reader.ReadInt32(); e.GoodsAutoSellAt = reader.ReadInt32(); e.GoodsAutoSellIntervalTicks = reader.ReadInt32();
                e.GoodsAutoKeep = reader.ReadInt32(); e.GoodsMarketWoodReserve = reader.ReadInt32();
                e.GoodsValueSteel = reader.ReadInt32(); e.GoodsValueBowGear = reader.ReadInt32(); e.GoodsValueRations = reader.ReadInt32();
                e.GoodsValueBricks = reader.ReadInt32(); e.GoodsValueTack = reader.ReadInt32(); e.GoodsValuePlanks = reader.ReadInt32();
                e.GoodsValueBooks = reader.ReadInt32(); e.GoodsValueIncense = reader.ReadInt32(); e.GoodsValueCharms = reader.ReadInt32();
                e.GoodsValueRepairKits = reader.ReadInt32(); e.GoodsValueTools = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing goods market extension data.");
            }
        }

        private static ScenarioExtensionData CreateRepairKitsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.RepairKits ? 1 : 0);
                writer.Write(e.RepairYardSizeCells); writer.Write(e.RepairYardWoodCost); writer.Write(e.RepairYardWork); writer.Write(e.RepairYardHp);
                writer.Write(e.RepairKitTicks); writer.Write(e.RepairKitWoodInput); writer.Write(e.RepairKitKeepWood); writer.Write(e.RepairKitStockLimit);
                writer.Write(e.RepairKitMaxYards); writer.Write(e.RepairKitWoodReserve); writer.Write(e.RepairKitIntervalTicks); writer.Write(e.RepairKitHp);
                return new ScenarioExtensionData { Id = RepairKitsExtensionId, Version = RepairKitsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadRepairKitsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid repair kits flag.");
                e.RepairKits = enabled != 0;
                e.RepairYardSizeCells = reader.ReadInt32(); e.RepairYardWoodCost = reader.ReadInt32(); e.RepairYardWork = reader.ReadInt32(); e.RepairYardHp = reader.ReadInt32();
                e.RepairKitTicks = reader.ReadInt32(); e.RepairKitWoodInput = reader.ReadInt32(); e.RepairKitKeepWood = reader.ReadInt32(); e.RepairKitStockLimit = reader.ReadInt32();
                e.RepairKitMaxYards = reader.ReadInt32(); e.RepairKitWoodReserve = reader.ReadInt32(); e.RepairKitIntervalTicks = reader.ReadInt32(); e.RepairKitHp = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing repair kits extension data.");
            }
        }

        private static ScenarioExtensionData CreateCharmsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Charms ? 1 : 0);
                writer.Write(e.CharmSizeCells); writer.Write(e.CharmWoodCost); writer.Write(e.CharmWork); writer.Write(e.CharmHp);
                writer.Write(e.CharmTicks); writer.Write(e.CharmWoodInput); writer.Write(e.CharmKeepWood); writer.Write(e.CharmStockLimit);
                writer.Write(e.CharmMaxWorkshops); writer.Write(e.CharmWoodReserve); writer.Write(e.CharmHpPermille);
                return new ScenarioExtensionData { Id = CharmsExtensionId, Version = CharmsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCharmsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid charms flag.");
                e.Charms = enabled != 0;
                e.CharmSizeCells = reader.ReadInt32(); e.CharmWoodCost = reader.ReadInt32(); e.CharmWork = reader.ReadInt32(); e.CharmHp = reader.ReadInt32();
                e.CharmTicks = reader.ReadInt32(); e.CharmWoodInput = reader.ReadInt32(); e.CharmKeepWood = reader.ReadInt32(); e.CharmStockLimit = reader.ReadInt32();
                e.CharmMaxWorkshops = reader.ReadInt32(); e.CharmWoodReserve = reader.ReadInt32(); e.CharmHpPermille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing charms extension data.");
            }
        }

        private static ScenarioExtensionData CreateIncenseExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Incense ? 1 : 0);
                writer.Write(e.IncenseSizeCells); writer.Write(e.IncenseWoodCost); writer.Write(e.IncenseWork); writer.Write(e.IncenseHp);
                writer.Write(e.IncenseTicks); writer.Write(e.IncenseWoodInput); writer.Write(e.IncenseFoodInput);
                writer.Write(e.IncenseKeepWood); writer.Write(e.IncenseKeepFood); writer.Write(e.IncenseStockLimit);
                writer.Write(e.IncenseHealIntervalTicks); writer.Write(e.IncenseHealHp); writer.Write(e.IncenseMaxHouses); writer.Write(e.IncenseWoodReserve);
                return new ScenarioExtensionData { Id = IncenseExtensionId, Version = IncenseExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadIncenseExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid incense flag.");
                e.Incense = enabled != 0;
                e.IncenseSizeCells = reader.ReadInt32(); e.IncenseWoodCost = reader.ReadInt32(); e.IncenseWork = reader.ReadInt32(); e.IncenseHp = reader.ReadInt32();
                e.IncenseTicks = reader.ReadInt32(); e.IncenseWoodInput = reader.ReadInt32(); e.IncenseFoodInput = reader.ReadInt32();
                e.IncenseKeepWood = reader.ReadInt32(); e.IncenseKeepFood = reader.ReadInt32(); e.IncenseStockLimit = reader.ReadInt32();
                e.IncenseHealIntervalTicks = reader.ReadInt32(); e.IncenseHealHp = reader.ReadInt32(); e.IncenseMaxHouses = reader.ReadInt32(); e.IncenseWoodReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing incense extension data.");
            }
        }

        private static ScenarioExtensionData CreateTradeGoodsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.TradeGoods ? 1 : 0);
                writer.Write(e.WeaverSizeCells); writer.Write(e.WeaverWoodCost); writer.Write(e.WeaverWork); writer.Write(e.WeaverHp);
                writer.Write(e.TradeGoodsTicks); writer.Write(e.TradeGoodsWoodInput); writer.Write(e.TradeGoodsKeepWood);
                writer.Write(e.TradeGoodsValue); writer.Write(e.TradeGoodsMaxWeavers); writer.Write(e.TradeGoodsWoodReserve);
                return new ScenarioExtensionData { Id = TradeGoodsExtensionId, Version = TradeGoodsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadTradeGoodsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid trade-goods flag.");
                e.TradeGoods = enabled != 0;
                e.WeaverSizeCells = reader.ReadInt32(); e.WeaverWoodCost = reader.ReadInt32(); e.WeaverWork = reader.ReadInt32(); e.WeaverHp = reader.ReadInt32();
                e.TradeGoodsTicks = reader.ReadInt32(); e.TradeGoodsWoodInput = reader.ReadInt32(); e.TradeGoodsKeepWood = reader.ReadInt32();
                e.TradeGoodsValue = reader.ReadInt32(); e.TradeGoodsMaxWeavers = reader.ReadInt32(); e.TradeGoodsWoodReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing trade-goods extension data.");
            }
        }

        private static ScenarioExtensionData CreateJewelsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Jewels ? 1 : 0);
                writer.Write(e.LapidarySizeCells); writer.Write(e.LapidaryWoodCost); writer.Write(e.LapidaryWork); writer.Write(e.LapidaryHp);
                writer.Write(e.JewelTicks); writer.Write(e.JewelStoneInput); writer.Write(e.JewelOreInput);
                writer.Write(e.JewelMaxLapidaries); writer.Write(e.JewelKeepStone); writer.Write(e.JewelWoodReserve);
                writer.Write(e.JewelMercenaryGems); writer.Write(e.JewelMercenaryFood); writer.Write(e.JewelMercenaryInfantryRatio);
                return new ScenarioExtensionData { Id = JewelsExtensionId, Version = JewelsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadJewelsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid jewels flag.");
                e.Jewels = enabled != 0;
                e.LapidarySizeCells = reader.ReadInt32(); e.LapidaryWoodCost = reader.ReadInt32(); e.LapidaryWork = reader.ReadInt32(); e.LapidaryHp = reader.ReadInt32();
                e.JewelTicks = reader.ReadInt32(); e.JewelStoneInput = reader.ReadInt32(); e.JewelOreInput = reader.ReadInt32();
                e.JewelMaxLapidaries = reader.ReadInt32(); e.JewelKeepStone = reader.ReadInt32(); e.JewelWoodReserve = reader.ReadInt32();
                e.JewelMercenaryGems = reader.ReadInt32(); e.JewelMercenaryFood = reader.ReadInt32(); e.JewelMercenaryInfantryRatio = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing jewels extension data.");
            }
        }

        private static ScenarioExtensionData CreateCivVarietyExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CivVariety ? 1 : 0);
                writer.Write(e.CivVarietyWindowPermille);
                return new ScenarioExtensionData { Id = CivVarietyExtensionId, Version = CivVarietyExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCivVarietyExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid civ variety flag.");
                e.CivVariety = enabled != 0;
                e.CivVarietyWindowPermille = reader.ReadInt32();
                if (e.CivVarietyWindowPermille < 0 || e.CivVarietyWindowPermille > 1000)
                    throw new InvalidDataException("Invalid civ variety window.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing civ variety extension data.");
            }
        }

        private static ScenarioExtensionData CreateCivVarietyCapsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CivVarietyCaps ? 1 : 0);
                writer.Write(e.CivVarietyCapMetallurgy);
                writer.Write(e.CivVarietyCapAgrarian);
                return new ScenarioExtensionData { Id = CivVarietyCapsExtensionId, Version = CivVarietyCapsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCivVarietyCapsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid civ variety caps flag.");
                e.CivVarietyCaps = enabled != 0;
                e.CivVarietyCapMetallurgy = reader.ReadInt32();
                e.CivVarietyCapAgrarian = reader.ReadInt32();
                if (e.CivVarietyCapMetallurgy <= 0 || e.CivVarietyCapAgrarian <= 0)
                    throw new InvalidDataException("Invalid civ variety caps.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing civ variety caps extension data.");
            }
        }

        private static ScenarioExtensionData CreateRationsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Rations ? 1 : 0);
                writer.Write(e.MillSizeCells); writer.Write(e.MillWoodCost); writer.Write(e.MillWork); writer.Write(e.MillHp);
                writer.Write(e.MillTicks); writer.Write(e.MillFoodInput);
                writer.Write(e.RationsPerSoldier); writer.Write(e.RationsTrainPermille);
                writer.Write(e.RationsHealIntervalTicks); writer.Write(e.RationsHealQuietTicks); writer.Write(e.RationsHealHp);
                writer.Write(e.RationsHealMaxPerInterval); writer.Write(e.RationsFarmMinimum); writer.Write(e.RationsFoodReserve);
                return new ScenarioExtensionData { Id = RationsExtensionId, Version = RationsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadRationsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid rations flag.");
                e.Rations = enabled != 0;
                e.MillSizeCells = reader.ReadInt32(); e.MillWoodCost = reader.ReadInt32(); e.MillWork = reader.ReadInt32(); e.MillHp = reader.ReadInt32();
                e.MillTicks = reader.ReadInt32(); e.MillFoodInput = reader.ReadInt32();
                e.RationsPerSoldier = reader.ReadInt32(); e.RationsTrainPermille = reader.ReadInt32();
                e.RationsHealIntervalTicks = reader.ReadInt32(); e.RationsHealQuietTicks = reader.ReadInt32(); e.RationsHealHp = reader.ReadInt32();
                e.RationsHealMaxPerInterval = reader.ReadInt32(); e.RationsFarmMinimum = reader.ReadInt32(); e.RationsFoodReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing rations extension data.");
            }
        }

        private static ScenarioExtensionData CreateEasyRebuildExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.EasyRebuild ? 1 : 0);
                writer.Write(e.MoveBuildingPauseTicks);
                return new ScenarioExtensionData { Id = EasyRebuildExtensionId, Version = EasyRebuildExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadEasyRebuildExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid easy rebuild flag.");
                e.EasyRebuild = enabled != 0;
                e.MoveBuildingPauseTicks = reader.ReadInt32();
                if (e.MoveBuildingPauseTicks <= 0) throw new InvalidDataException("Invalid easy rebuild pause.");
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing easy rebuild extension data.");
            }
        }

        private static ScenarioExtensionData CreateCommonDrillExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CommonDrill ? 1 : 0);
                writer.Write(e.DrillSizeCells); writer.Write(e.DrillWoodCost); writer.Write(e.DrillStoneCost);
                writer.Write(e.DrillWork); writer.Write(e.DrillHp); writer.Write(e.DrillIntervalTicks);
                return new ScenarioExtensionData { Id = CommonDrillExtensionId, Version = CommonDrillExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCommonDrillExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid common-drill flag.");
                e.CommonDrill = enabled != 0;
                e.DrillSizeCells = reader.ReadInt32(); e.DrillWoodCost = reader.ReadInt32(); e.DrillStoneCost = reader.ReadInt32();
                e.DrillWork = reader.ReadInt32(); e.DrillHp = reader.ReadInt32(); e.DrillIntervalTicks = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing common-drill extension data.");
            }
        }

        private static ScenarioExtensionData CreateInserterExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Inserters ? 1 : 0);
                writer.Write(e.InserterWoodCost);
                writer.Write(e.InserterIntervalTicks);
                writer.Write(e.AiInserterCoreReserve);
                return new ScenarioExtensionData { Id = InserterExtensionId, Version = InserterExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadInserterExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid inserter flag.");
                e.Inserters = enabled != 0;
                e.InserterWoodCost = reader.ReadInt32();
                e.InserterIntervalTicks = reader.ReadInt32();
                e.AiInserterCoreReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing inserter extension data.");
            }
        }

        private static ScenarioExtensionData CreateAdjacentBuildingsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AdjacentBuildings ? 1 : 0);
                writer.Write(e.AdjacentBuildingClearanceCells);
                return new ScenarioExtensionData { Id = AdjacentBuildingsExtensionId, Version = AdjacentBuildingsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAdjacentBuildingsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid adjacent-buildings flag.");
                e.AdjacentBuildings = enabled != 0;
                e.AdjacentBuildingClearanceCells = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing adjacent-buildings extension data.");
            }
        }

        private static ScenarioExtensionData CreateBuildingReservationsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.BuildingReservations ? 1 : 0);
                writer.Write(e.ReservationLimit);
                writer.Write(e.ReservationCheckTicks);
                return new ScenarioExtensionData { Id = BuildingReservationsExtensionId, Version = BuildingReservationsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadBuildingReservationsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid building-reservations flag.");
                e.BuildingReservations = enabled != 0;
                e.ReservationLimit = reader.ReadInt32();
                e.ReservationCheckTicks = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing building-reservations extension data.");
            }
        }

        private static ScenarioExtensionData CreateBricksExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Bricks ? 1 : 0);
                writer.Write(e.BrickKilnSizeCells); writer.Write(e.BrickKilnWoodCost); writer.Write(e.BrickKilnWork); writer.Write(e.BrickKilnHp);
                writer.Write(e.BrickTicks); writer.Write(e.BrickStoneInput); writer.Write(e.BrickCharcoalInput);
                writer.Write(e.BricksPerWall); writer.Write(e.BricksPerTower); writer.Write(e.BricksPerCastle);
                writer.Write(e.BrickHpPermille); writer.Write(e.BrickUpgradeIntervalTicks); writer.Write(e.BrickCastleStonePermille);
                writer.Write(e.BrickCoreRepairIntervalTicks); writer.Write(e.BricksPerCoreRepair); writer.Write(e.BrickCoreRepairHp); writer.Write(e.BrickExtraTowers);
                return new ScenarioExtensionData { Id = BricksExtensionId, Version = BricksExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadBricksExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid bricks flag.");
                e.Bricks = enabled != 0;
                e.BrickKilnSizeCells = reader.ReadInt32(); e.BrickKilnWoodCost = reader.ReadInt32(); e.BrickKilnWork = reader.ReadInt32(); e.BrickKilnHp = reader.ReadInt32();
                e.BrickTicks = reader.ReadInt32(); e.BrickStoneInput = reader.ReadInt32(); e.BrickCharcoalInput = reader.ReadInt32();
                e.BricksPerWall = reader.ReadInt32(); e.BricksPerTower = reader.ReadInt32(); e.BricksPerCastle = reader.ReadInt32();
                e.BrickHpPermille = reader.ReadInt32(); e.BrickUpgradeIntervalTicks = reader.ReadInt32(); e.BrickCastleStonePermille = reader.ReadInt32();
                if (extension.Version >= BricksExtensionVersion)
                {
                    e.BrickCoreRepairIntervalTicks = reader.ReadInt32(); e.BricksPerCoreRepair = reader.ReadInt32();
                    e.BrickCoreRepairHp = reader.ReadInt32(); e.BrickExtraTowers = reader.ReadInt32();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing bricks extension data.");
            }
        }

        private static ScenarioExtensionData CreateTackExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Tack ? 1 : 0);
                writer.Write(e.SaddlerySizeCells); writer.Write(e.SaddleryWoodCost); writer.Write(e.TackWoodReserve); writer.Write(e.SaddleryWork); writer.Write(e.SaddleryHp);
                writer.Write(e.TackTicks); writer.Write(e.TackFoodInput); writer.Write(e.TackWoodInput);
                writer.Write(e.HeavyCavalryTackCost); writer.Write(e.HeavyCavalryFoodCost); writer.Write(e.HeavyCavalryTrainTicks);
                writer.Write(e.HeavyCavalryHp); writer.Write(e.HeavyCavalryDamage); writer.Write(e.HeavyCavalryAttackIntervalTicks);
                writer.Write(e.HeavyCavalryRange.Raw); writer.Write(e.HeavyCavalrySpeed.Raw); writer.Write(e.HeavyCavalryVision.Raw);
                return new ScenarioExtensionData { Id = TackExtensionId, Version = TackExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadTackExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid tack flag.");
                e.Tack = enabled != 0;
                e.SaddlerySizeCells = reader.ReadInt32(); e.SaddleryWoodCost = reader.ReadInt32();
                if (extension.Version >= 2) e.TackWoodReserve = reader.ReadInt32();
                e.SaddleryWork = reader.ReadInt32(); e.SaddleryHp = reader.ReadInt32();
                e.TackTicks = reader.ReadInt32(); e.TackFoodInput = reader.ReadInt32(); e.TackWoodInput = reader.ReadInt32();
                e.HeavyCavalryTackCost = reader.ReadInt32(); e.HeavyCavalryFoodCost = reader.ReadInt32(); e.HeavyCavalryTrainTicks = reader.ReadInt32();
                e.HeavyCavalryHp = reader.ReadInt32(); e.HeavyCavalryDamage = reader.ReadInt32(); e.HeavyCavalryAttackIntervalTicks = reader.ReadInt32();
                e.HeavyCavalryRange = Fix(reader); e.HeavyCavalrySpeed = Fix(reader); e.HeavyCavalryVision = Fix(reader);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing tack extension data.");
            }
        }

        private static ScenarioExtensionData CreatePlanksExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Planks ? 1 : 0);
                writer.Write(e.SawmillSizeCells); writer.Write(e.SawmillWoodCost); writer.Write(e.SawmillWork); writer.Write(e.SawmillHp);
                writer.Write(e.PlankTicks); writer.Write(e.PlankWoodInput); writer.Write(e.PlanksPerRam);
                writer.Write(e.PlankRamHpPermille); writer.Write(e.PlankRamTrainPermille);
                writer.Write(e.PlanksPerBridgeCell); writer.Write(e.PlankBridgeWoodPermille);
                writer.Write(e.PlankExtraRams); writer.Write(e.PlankWoodReserve);
                return new ScenarioExtensionData { Id = PlanksExtensionId, Version = PlanksExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadPlanksExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid planks flag.");
                e.Planks = enabled != 0;
                e.SawmillSizeCells = reader.ReadInt32(); e.SawmillWoodCost = reader.ReadInt32();
                e.SawmillWork = reader.ReadInt32(); e.SawmillHp = reader.ReadInt32();
                e.PlankTicks = reader.ReadInt32(); e.PlankWoodInput = reader.ReadInt32(); e.PlanksPerRam = reader.ReadInt32();
                e.PlankRamHpPermille = reader.ReadInt32(); e.PlankRamTrainPermille = reader.ReadInt32();
                e.PlanksPerBridgeCell = reader.ReadInt32(); e.PlankBridgeWoodPermille = reader.ReadInt32();
                e.PlankExtraRams = reader.ReadInt32(); e.PlankWoodReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing planks extension data.");
            }
        }

        private static ScenarioExtensionData CreateBooksExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Books ? 1 : 0);
                writer.Write(e.ScriptoriumSizeCells); writer.Write(e.ScriptoriumWoodCost); writer.Write(e.ScriptoriumWork); writer.Write(e.ScriptoriumHp);
                writer.Write(e.BookTicks); writer.Write(e.BookGoldInput); writer.Write(e.BookWoodInput);
                writer.Write(e.BooksPerResearch); writer.Write(e.BookResearchPermille); writer.Write(e.BookResearchCostPermille); writer.Write(e.BookWoodReserve);
                return new ScenarioExtensionData { Id = BooksExtensionId, Version = BooksExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static ScenarioExtensionData CreatePreservesExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Preserves ? 1 : 0);
                writer.Write(e.DryingRackSizeCells); writer.Write(e.DryingRackWoodCost); writer.Write(e.DryingRackWork); writer.Write(e.DryingRackHp);
                writer.Write(e.PreserveTicks); writer.Write(e.PreserveFoodInput); writer.Write(e.PreserveFoodValue);
                writer.Write(e.PreserveKeepFood); writer.Write(e.PreserveMaxRacks); writer.Write(e.PreserveWoodReserve);
                return new ScenarioExtensionData { Id = PreservesExtensionId, Version = PreservesExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadPreservesExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid preserves flag.");
                e.Preserves = enabled != 0;
                e.DryingRackSizeCells = reader.ReadInt32(); e.DryingRackWoodCost = reader.ReadInt32();
                e.DryingRackWork = reader.ReadInt32(); e.DryingRackHp = reader.ReadInt32();
                e.PreserveTicks = reader.ReadInt32(); e.PreserveFoodInput = reader.ReadInt32(); e.PreserveFoodValue = reader.ReadInt32();
                e.PreserveKeepFood = reader.ReadInt32(); e.PreserveMaxRacks = reader.ReadInt32(); e.PreserveWoodReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing preserves extension data.");
            }
        }

        private static void ReadBooksExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid books flag.");
                e.Books = enabled != 0;
                e.ScriptoriumSizeCells = reader.ReadInt32(); e.ScriptoriumWoodCost = reader.ReadInt32();
                e.ScriptoriumWork = reader.ReadInt32(); e.ScriptoriumHp = reader.ReadInt32();
                e.BookTicks = reader.ReadInt32(); e.BookGoldInput = reader.ReadInt32(); e.BookWoodInput = reader.ReadInt32();
                e.BooksPerResearch = reader.ReadInt32(); e.BookResearchPermille = reader.ReadInt32();
                e.BookResearchCostPermille = reader.ReadInt32(); e.BookWoodReserve = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing books extension data.");
            }
        }

        private static ScenarioExtensionData CreateEarlyArmsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.EarlyArms ? 1 : 0);
                writer.Write(e.EarlyGatherIntervalTicks);
                writer.Write(e.EarlyAdvanceFoodCost); writer.Write(e.EarlyAdvanceWoodCost); writer.Write(e.EarlyAdvanceTicks);
                writer.Write(e.EarlyAge2FoodCost); writer.Write(e.EarlyAge2WoodCost); writer.Write(e.EarlyAge2Ticks);
                writer.Write(e.EarlyAdvanceVillagers); writer.Write(e.EarlyAge2Villagers);
                return new ScenarioExtensionData { Id = EarlyArmsExtensionId, Version = EarlyArmsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadEarlyArmsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid early-arms flag.");
                e.EarlyArms = enabled != 0;
                e.EarlyGatherIntervalTicks = reader.ReadInt32();
                e.EarlyAdvanceFoodCost = reader.ReadInt32(); e.EarlyAdvanceWoodCost = reader.ReadInt32(); e.EarlyAdvanceTicks = reader.ReadInt32();
                e.EarlyAge2FoodCost = reader.ReadInt32(); e.EarlyAge2WoodCost = reader.ReadInt32(); e.EarlyAge2Ticks = reader.ReadInt32();
                e.EarlyAdvanceVillagers = reader.ReadInt32(); e.EarlyAge2Villagers = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing early-arms extension data.");
            }
        }

        private static ScenarioExtensionData CreateAgeClockExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AgeClock ? 1 : 0);
                writer.Write(e.AgeClockTicks1); writer.Write(e.AgeClockTicks2); writer.Write(e.AgeClockTicks3);
                return new ScenarioExtensionData { Id = AgeClockExtensionId, Version = AgeClockExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAgeClockExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid age-clock flag.");
                e.AgeClock = enabled != 0;
                e.AgeClockTicks1 = reader.ReadInt32(); e.AgeClockTicks2 = reader.ReadInt32(); e.AgeClockTicks3 = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing age-clock extension data.");
            }
        }

        private static ScenarioExtensionData CreateAgeFourExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AgeFour ? 1 : 0);
                writer.Write(e.AgeClockTicks4);
                writer.Write(e.Age4FoodCost); writer.Write(e.Age4WoodCost); writer.Write(e.Age4Ticks); writer.Write(e.Age4PopulationBonus);
                writer.Write(e.SiegeAge4Permille);
                writer.Write(e.TotalWarFoodCost); writer.Write(e.TotalWarWoodCost); writer.Write(e.TotalWarMetalCost); writer.Write(e.TotalWarTicks);
                writer.Write(e.GrandWorkshopFoodCost); writer.Write(e.GrandWorkshopWoodCost); writer.Write(e.GrandWorkshopMetalCost); writer.Write(e.GrandWorkshopTicks);
                return new ScenarioExtensionData { Id = AgeFourExtensionId, Version = AgeFourExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAgeFourExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid age-four flag.");
                e.AgeFour = enabled != 0;
                e.AgeClockTicks4 = reader.ReadInt32();
                e.Age4FoodCost = reader.ReadInt32(); e.Age4WoodCost = reader.ReadInt32(); e.Age4Ticks = reader.ReadInt32(); e.Age4PopulationBonus = reader.ReadInt32();
                e.SiegeAge4Permille = reader.ReadInt32();
                e.TotalWarFoodCost = reader.ReadInt32(); e.TotalWarWoodCost = reader.ReadInt32(); e.TotalWarMetalCost = reader.ReadInt32(); e.TotalWarTicks = reader.ReadInt32();
                e.GrandWorkshopFoodCost = reader.ReadInt32(); e.GrandWorkshopWoodCost = reader.ReadInt32(); e.GrandWorkshopMetalCost = reader.ReadInt32(); e.GrandWorkshopTicks = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing age-four extension data.");
            }
        }

        private static ScenarioExtensionData CreateAgeStrengthsExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AgeStrengths ? 1 : 0);
                writer.Write(e.AgrarianFarmSpeedPermille); writer.Write(e.AgrarianRationsHealHp); writer.Write(e.AgrarianPopulationCapBonus);
                writer.Write(e.MetallurgySmeltSpeedPermille); writer.Write(e.MetallurgyForgedDamageBonus); writer.Write(e.MetallurgySiegePartsTimePermille);
                writer.Write(e.ForestryLumberSpeedPermille); writer.Write(e.ForestryArcherRangeBonus); writer.Write(e.ForestryBuildingWoodPermille);
                writer.Write(e.MasonryWallHpPermille); writer.Write(e.MasonryTowerDamageBonus); writer.Write(e.MasonryCoreHpPermille);
                writer.Write(e.CaravanTradeGoodsValue); writer.Write(e.CaravanMarketReturnPermille); writer.Write(e.CaravanRewardPermille);
                writer.Write(e.CavalryStableTimePermille); writer.Write(e.CavalrySpeedPermille); writer.Write(e.CavalryHeavyHpPermille);
                writer.Write(e.BridgeBuildingWorkPermille); writer.Write(e.BridgeRamSiegeDamagePermille); writer.Write(e.BridgeGeneralWorkPermille);
                writer.Write(e.AcademyResearchTimePermille); writer.Write(e.AcademyScholarlyCostPermille); writer.Write(e.AcademyAge4ResearchCostPermille);
                writer.Write(e.CultConversionTimePermille); writer.Write(e.CultIncenseHealHp); writer.Write(e.CultMonkHpPermille);
                writer.Write(e.FishingGatherTimePermille); writer.Write(e.FishingPreserveFoodValue); writer.Write(e.FishingFoodCarryBonus);
                writer.Write(e.MountainMiningTimePermille); writer.Write(e.MountainMercenaryGems); writer.Write(e.MountainMercenaryDamageBonus);
                writer.Write(e.TollgateFeePermille); writer.Write(e.TollgateHpPermille); writer.Write(e.TollgateRepairKitHp);
                writer.Write(e.MetropolisVillagerTimePermille); writer.Write(e.MetropolisTownLimitBonus); writer.Write(e.MetropolisToolTimePermille);
                writer.Write(e.SanctuaryPilgrimagePermille); writer.Write(e.SanctuaryCharmHpPermille); writer.Write(e.SanctuaryCharmHpAge4Permille);
                return new ScenarioExtensionData { Id = AgeStrengthsExtensionId, Version = AgeStrengthsExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAgeStrengthsExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid age-strengths flag.");
                e.AgeStrengths = enabled != 0;
                e.AgrarianFarmSpeedPermille = reader.ReadInt32(); e.AgrarianRationsHealHp = reader.ReadInt32(); e.AgrarianPopulationCapBonus = reader.ReadInt32();
                e.MetallurgySmeltSpeedPermille = reader.ReadInt32(); e.MetallurgyForgedDamageBonus = reader.ReadInt32(); e.MetallurgySiegePartsTimePermille = reader.ReadInt32();
                e.ForestryLumberSpeedPermille = reader.ReadInt32(); e.ForestryArcherRangeBonus = reader.ReadInt32(); e.ForestryBuildingWoodPermille = reader.ReadInt32();
                e.MasonryWallHpPermille = reader.ReadInt32(); e.MasonryTowerDamageBonus = reader.ReadInt32(); e.MasonryCoreHpPermille = reader.ReadInt32();
                e.CaravanTradeGoodsValue = reader.ReadInt32(); e.CaravanMarketReturnPermille = reader.ReadInt32(); e.CaravanRewardPermille = reader.ReadInt32();
                e.CavalryStableTimePermille = reader.ReadInt32(); e.CavalrySpeedPermille = reader.ReadInt32(); e.CavalryHeavyHpPermille = reader.ReadInt32();
                e.BridgeBuildingWorkPermille = reader.ReadInt32(); e.BridgeRamSiegeDamagePermille = reader.ReadInt32(); e.BridgeGeneralWorkPermille = reader.ReadInt32();
                e.AcademyResearchTimePermille = reader.ReadInt32(); e.AcademyScholarlyCostPermille = reader.ReadInt32(); e.AcademyAge4ResearchCostPermille = reader.ReadInt32();
                e.CultConversionTimePermille = reader.ReadInt32(); e.CultIncenseHealHp = reader.ReadInt32(); e.CultMonkHpPermille = reader.ReadInt32();
                e.FishingGatherTimePermille = reader.ReadInt32(); e.FishingPreserveFoodValue = reader.ReadInt32(); e.FishingFoodCarryBonus = reader.ReadInt32();
                e.MountainMiningTimePermille = reader.ReadInt32(); e.MountainMercenaryGems = reader.ReadInt32(); e.MountainMercenaryDamageBonus = reader.ReadInt32();
                e.TollgateFeePermille = reader.ReadInt32(); e.TollgateHpPermille = reader.ReadInt32(); e.TollgateRepairKitHp = reader.ReadInt32();
                e.MetropolisVillagerTimePermille = reader.ReadInt32(); e.MetropolisTownLimitBonus = reader.ReadInt32(); e.MetropolisToolTimePermille = reader.ReadInt32();
                e.SanctuaryPilgrimagePermille = reader.ReadInt32(); e.SanctuaryCharmHpPermille = reader.ReadInt32(); e.SanctuaryCharmHpAge4Permille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing age-strengths extension data.");
            }
        }

        private static ScenarioExtensionData CreateCraftTree4Extension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CraftTree4 ? 1 : 0);
                writer.Write(e.SiegeEngineTicks); writer.Write(e.SiegeEngineSiegePartsInput); writer.Write(e.SiegeEngineMechanismInput);
                writer.Write(e.EliteKitTicks); writer.Write(e.EliteKitArmsKitInput); writer.Write(e.EliteKitMechanismInput); writer.Write(e.EliteKitMetalInput);
                writer.Write(e.EliteKitAttack); writer.Write(e.EliteKitHpPermille);
                writer.Write(e.CannonSiegeEngineCost); writer.Write(e.CannonFoodCost); writer.Write(e.CannonWoodCost); writer.Write(e.CannonHp);
                writer.Write(e.CannonDamage); writer.Write(e.CannonSiegeDamage); writer.Write(e.CannonInterval);
                writer.Write(e.CannonRange.Raw); writer.Write(e.CannonSpeed.Raw); writer.Write(e.CannonVision.Raw); writer.Write(e.CannonAutoLimit);
                writer.Write(e.GoodsValueSiegeEngine); writer.Write(e.GoodsValueEliteKit);
                return new ScenarioExtensionData { Id = CraftTree4ExtensionId, Version = CraftTree4ExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCraftTree4Extension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid craft-tree4 flag.");
                e.CraftTree4 = enabled != 0;
                e.SiegeEngineTicks = reader.ReadInt32(); e.SiegeEngineSiegePartsInput = reader.ReadInt32(); e.SiegeEngineMechanismInput = reader.ReadInt32();
                e.EliteKitTicks = reader.ReadInt32(); e.EliteKitArmsKitInput = reader.ReadInt32(); e.EliteKitMechanismInput = reader.ReadInt32(); e.EliteKitMetalInput = reader.ReadInt32();
                e.EliteKitAttack = reader.ReadInt32(); e.EliteKitHpPermille = reader.ReadInt32();
                e.CannonSiegeEngineCost = reader.ReadInt32(); e.CannonFoodCost = reader.ReadInt32(); e.CannonWoodCost = reader.ReadInt32(); e.CannonHp = reader.ReadInt32();
                e.CannonDamage = reader.ReadInt32(); e.CannonSiegeDamage = reader.ReadInt32(); e.CannonInterval = reader.ReadInt32();
                e.CannonRange = Fix64.FromRaw(reader.ReadInt64()); e.CannonSpeed = Fix64.FromRaw(reader.ReadInt64()); e.CannonVision = Fix64.FromRaw(reader.ReadInt64()); e.CannonAutoLimit = reader.ReadInt32();
                e.GoodsValueSiegeEngine = reader.ReadInt32(); e.GoodsValueEliteKit = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing craft-tree4 extension data.");
            }
        }

        private static ScenarioExtensionData CreateAgeFourAiExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AgeFourAi ? 1 : 0);
                writer.Write(e.AgeFourAiAssemblers);
                writer.Write(e.AgeFourAiIndustry ? 1 : 0);
                return new ScenarioExtensionData { Id = AgeFourAiExtensionId, Version = AgeFourAiExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAgeFourAiExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid age-four-ai flag.");
                e.AgeFourAi = enabled != 0 && e.AgeFour;
                e.AgeFourAiAssemblers = reader.ReadInt32();
                int industry = reader.ReadInt32();
                if (industry < 0 || industry > 1) throw new InvalidDataException("Invalid age-four-ai industry flag.");
                e.AgeFourAiIndustry = industry != 0 && e.AgeFourAi;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing age-four-ai extension data.");
            }
        }

        private static ScenarioExtensionData CreateAgeClockUxExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AgeClockUx ? 1 : 0);
                writer.Write(e.AgeClockUxChoiceTicks);
                return new ScenarioExtensionData { Id = AgeClockUxExtensionId, Version = AgeClockUxExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadAgeClockUxExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid age-clock UX flag.");
                e.AgeClockUx = enabled != 0;
                e.AgeClockUxChoiceTicks = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing age-clock UX extension data.");
            }
        }

        private static ScenarioExtensionData CreateSiegeByAgeExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.SiegeByAge ? 1 : 0);
                writer.Write(e.SiegeAge0Permille); writer.Write(e.SiegeAge1Permille);
                writer.Write(e.SiegeAge2Permille); writer.Write(e.SiegeAge3Permille);
                return new ScenarioExtensionData { Id = SiegeByAgeExtensionId, Version = SiegeByAgeExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadSiegeByAgeExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid siege-by-age flag.");
                e.SiegeByAge = enabled != 0;
                e.SiegeAge0Permille = reader.ReadInt32(); e.SiegeAge1Permille = reader.ReadInt32();
                e.SiegeAge2Permille = reader.ReadInt32(); e.SiegeAge3Permille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing siege-by-age extension data.");
            }
        }

        private static ScenarioExtensionData CreateTownExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Towns ? 1 : 0);
                writer.Write(e.TownSizeCells); writer.Write(e.TownWoodCost); writer.Write(e.TownStoneCost);
                writer.Write(e.TownWork); writer.Write(e.TownHp); writer.Write(e.TownMaxBuildings);
                writer.Write(e.TownCoreDistance); writer.Write(e.TownResourceReach);
                return new ScenarioExtensionData { Id = TownExtensionId, Version = TownExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static ScenarioExtensionData CreateRegionExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Regions ? 1 : 0);
                return new ScenarioExtensionData { Id = RegionExtensionId, Version = RegionExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadRegionExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled != 0 && enabled != 1 || stream.Position != stream.Length) throw new InvalidDataException("Invalid regions extension.");
                e.Regions = enabled != 0;
            }
        }

        private static ScenarioExtensionData CreateLatePushExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.LatePush ? 1 : 0);
                writer.Write(e.LatePushAfterTicks); writer.Write(e.LatePushAdvantageAfterTicks);
                writer.Write(e.LatePushEnemyMultiplierPermille);
                return new ScenarioExtensionData { Id = LatePushExtensionId, Version = LatePushExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadLatePushExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid late-push flag.");
                e.LatePush = enabled != 0;
                e.LatePushAfterTicks = reader.ReadInt32(); e.LatePushAdvantageAfterTicks = reader.ReadInt32();
                e.LatePushEnemyMultiplierPermille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing late-push extension data.");
            }
        }

        private static ScenarioExtensionData CreatePushWavesExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.PushWaves ? 1 : 0);
                writer.Write(e.WaveStagingDistance); writer.Write(e.WaveSize); writer.Write(e.WaveMaxWaitTicks);
                writer.Write(e.WaveMinSize); writer.Write(e.WaveRetreatPermille);
                return new ScenarioExtensionData { Id = PushWavesExtensionId, Version = PushWavesExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadPushWavesExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid push-waves flag.");
                e.PushWaves = enabled != 0;
                e.WaveStagingDistance = reader.ReadInt32(); e.WaveSize = reader.ReadInt32();
                e.WaveMaxWaitTicks = reader.ReadInt32(); e.WaveMinSize = reader.ReadInt32(); e.WaveRetreatPermille = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing push-waves extension data.");
            }
        }

        private static ScenarioExtensionData CreateManualControlExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.ManualControl ? 1 : 0);
                return new ScenarioExtensionData { Id = ManualControlExtensionId, Version = ManualControlExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadManualControlExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid manual-control flag.");
                e.ManualControl = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing manual-control extension data.");
            }
        }

        private static void ReadTownExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid town flag.");
                e.Towns = enabled != 0;
                e.TownSizeCells = reader.ReadInt32(); e.TownWoodCost = reader.ReadInt32(); e.TownStoneCost = reader.ReadInt32();
                e.TownWork = reader.ReadInt32(); e.TownHp = reader.ReadInt32(); e.TownMaxBuildings = reader.ReadInt32();
                e.TownCoreDistance = reader.ReadInt32(); e.TownResourceReach = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing town extension data.");
            }
        }

        private static ScenarioExtensionData CreateCoreDefenceExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.CoreDefence ? 1 : 0);
                writer.Write(e.CoreDefenceRange); writer.Write(e.CoreDefenceDamage);
                writer.Write(e.CoreDefenceIntervalTicks); writer.Write(e.CoreDefenceMaxTargets);
                return new ScenarioExtensionData { Id = CoreDefenceExtensionId, Version = CoreDefenceExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadCoreDefenceExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid core defence flag.");
                e.CoreDefence = enabled != 0;
                e.CoreDefenceRange = reader.ReadInt32(); e.CoreDefenceDamage = reader.ReadInt32();
                e.CoreDefenceIntervalTicks = reader.ReadInt32(); e.CoreDefenceMaxTargets = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing core defence extension data.");
            }
        }

        private static ScenarioExtensionData CreateArmyGrowthExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.ArmyGrowth ? 1 : 0);
                return new ScenarioExtensionData { Id = ArmyGrowthExtensionId, Version = ArmyGrowthExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadArmyGrowthExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid army-growth flag.");
                e.ArmyGrowth = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing army-growth extension data.");
            }
        }

        private static ScenarioExtensionData CreateEconomyScaleExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.EconomyScale ? 1 : 0);
                return new ScenarioExtensionData { Id = EconomyScaleExtensionId, Version = EconomyScaleExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadEconomyScaleExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid economy-scale flag.");
                e.EconomyScale = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing economy-scale extension data.");
            }
        }

        private static ScenarioExtensionData CreateSanctuaryExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Sanctuary ? 1 : 0);
                writer.Write(e.ShrineSizeCells); writer.Write(e.ShrineWoodCost); writer.Write(e.ShrineStoneCost);
                writer.Write(e.ShrineWork); writer.Write(e.ShrineHp); writer.Write(e.ShrineOutpostReach);
                writer.Write(e.SanctuaryAttackBonusPermille); writer.Write(e.SanctuaryMaxBonusPermille);
                writer.Write(e.SanctuaryPilgrimageFoodCost); writer.Write(e.SanctuaryPilgrimageWoodCost); writer.Write(e.SanctuaryPilgrimageTicks);
                writer.Write(e.SanctuaryRelicFoodCost); writer.Write(e.SanctuaryRelicWoodCost); writer.Write(e.SanctuaryRelicTicks);
                writer.Write(e.SanctuaryPilgrimageRadius); writer.Write(e.SanctuaryPilgrimageIntervalTicks); writer.Write(e.SanctuaryPilgrimageHeal);
                writer.Write(e.SanctuaryRelicBonusPermille); writer.Write(e.SanctuaryRelicMaxBonusPermille);
                return new ScenarioExtensionData { Id = SanctuaryExtensionId, Version = SanctuaryExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadSanctuaryExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid sanctuary flag.");
                e.Sanctuary = enabled != 0;
                e.ShrineSizeCells = reader.ReadInt32(); e.ShrineWoodCost = reader.ReadInt32(); e.ShrineStoneCost = reader.ReadInt32();
                e.ShrineWork = reader.ReadInt32(); e.ShrineHp = reader.ReadInt32(); e.ShrineOutpostReach = reader.ReadInt32();
                e.SanctuaryAttackBonusPermille = reader.ReadInt32(); e.SanctuaryMaxBonusPermille = reader.ReadInt32();
                if (extension.Version >= SanctuaryExtensionVersion)
                {
                    e.SanctuaryPilgrimageFoodCost = reader.ReadInt32(); e.SanctuaryPilgrimageWoodCost = reader.ReadInt32(); e.SanctuaryPilgrimageTicks = reader.ReadInt32();
                    e.SanctuaryRelicFoodCost = reader.ReadInt32(); e.SanctuaryRelicWoodCost = reader.ReadInt32(); e.SanctuaryRelicTicks = reader.ReadInt32();
                    e.SanctuaryPilgrimageRadius = reader.ReadInt32(); e.SanctuaryPilgrimageIntervalTicks = reader.ReadInt32(); e.SanctuaryPilgrimageHeal = reader.ReadInt32();
                    e.SanctuaryRelicBonusPermille = reader.ReadInt32(); e.SanctuaryRelicMaxBonusPermille = reader.ReadInt32();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing sanctuary extension data.");
            }
        }

        private static ScenarioExtensionData CreateMetropolisExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.Metropolis ? 1 : 0);
                writer.Write(e.GrandHouseSizeCells); writer.Write(e.GrandHouseWoodCost);
                writer.Write(e.GrandHouseWork); writer.Write(e.GrandHouseHp);
                writer.Write(e.MetropolisMarketFoodCost); writer.Write(e.MetropolisMarketWoodCost); writer.Write(e.MetropolisMarketTicks);
                writer.Write(e.MetropolisMilitiaFoodCost); writer.Write(e.MetropolisMilitiaWoodCost); writer.Write(e.MetropolisMilitiaTicks);
                writer.Write(e.MetropolisMarketVillagerStep); writer.Write(e.MetropolisMarketBonusPermille); writer.Write(e.MetropolisMarketMaxBonusPermille);
                writer.Write(e.MetropolisMilitiaDamage); writer.Write(e.MetropolisMilitiaCoreRadius);
                return new ScenarioExtensionData { Id = MetropolisExtensionId, Version = MetropolisExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadMetropolisExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid metropolis flag.");
                e.Metropolis = enabled != 0;
                e.GrandHouseSizeCells = reader.ReadInt32(); e.GrandHouseWoodCost = reader.ReadInt32();
                e.GrandHouseWork = reader.ReadInt32(); e.GrandHouseHp = reader.ReadInt32();
                if (extension.Version >= MetropolisExtensionVersion)
                {
                    e.MetropolisMarketFoodCost = reader.ReadInt32(); e.MetropolisMarketWoodCost = reader.ReadInt32(); e.MetropolisMarketTicks = reader.ReadInt32();
                    e.MetropolisMilitiaFoodCost = reader.ReadInt32(); e.MetropolisMilitiaWoodCost = reader.ReadInt32(); e.MetropolisMilitiaTicks = reader.ReadInt32();
                    e.MetropolisMarketVillagerStep = reader.ReadInt32(); e.MetropolisMarketBonusPermille = reader.ReadInt32(); e.MetropolisMarketMaxBonusPermille = reader.ReadInt32();
                    e.MetropolisMilitiaDamage = reader.ReadInt32(); e.MetropolisMilitiaCoreRadius = reader.ReadInt32();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing metropolis extension data.");
            }
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

        private static ScenarioExtensionData CreateFishingCivExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.FishingCiv ? 1 : 0);
                writer.Write(e.HarborSizeCells); writer.Write(e.HarborWoodCost); writer.Write(e.HarborWork); writer.Write(e.HarborHp);
                writer.Write(e.FishingNetFoodCost); writer.Write(e.FishingNetWoodCost); writer.Write(e.FishingNetTicks);
                writer.Write(e.DriedFishFoodCost); writer.Write(e.DriedFishWoodCost); writer.Write(e.DriedFishTicks);
                writer.Write(e.FishingNetCarryBonusPermille); writer.Write(e.DriedFishRegrowIntervalPermille);
                return new ScenarioExtensionData { Id = FishingCivExtensionId, Version = FishingCivExtensionVersion, Data = stream.ToArray() };
            }
        }

        private static void ReadFishingCivExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid fishing civilisation flag.");
                e.FishingCiv = enabled != 0;
                e.HarborSizeCells = reader.ReadInt32(); e.HarborWoodCost = reader.ReadInt32();
                e.HarborWork = reader.ReadInt32(); e.HarborHp = reader.ReadInt32();
                if (extension.Version >= FishingCivExtensionVersion)
                {
                    e.FishingNetFoodCost = reader.ReadInt32(); e.FishingNetWoodCost = reader.ReadInt32(); e.FishingNetTicks = reader.ReadInt32();
                    e.DriedFishFoodCost = reader.ReadInt32(); e.DriedFishWoodCost = reader.ReadInt32(); e.DriedFishTicks = reader.ReadInt32();
                    e.FishingNetCarryBonusPermille = reader.ReadInt32(); e.DriedFishRegrowIntervalPermille = reader.ReadInt32();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing fishing civilisation extension data.");
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
                writer.Write(e.SermonFoodCost); writer.Write(e.SermonWoodCost); writer.Write(e.SermonTicks);
                writer.Write(e.MartyrBlessingFoodCost); writer.Write(e.MartyrBlessingWoodCost); writer.Write(e.MartyrBlessingTicks);
                writer.Write(e.MartyrBlessingHpPermille);
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
                if (extension.Version >= CultExtensionVersion)
                {
                    e.SermonFoodCost = reader.ReadInt32(); e.SermonWoodCost = reader.ReadInt32(); e.SermonTicks = reader.ReadInt32();
                    e.MartyrBlessingFoodCost = reader.ReadInt32(); e.MartyrBlessingWoodCost = reader.ReadInt32();
                    e.MartyrBlessingTicks = reader.ReadInt32(); e.MartyrBlessingHpPermille = reader.ReadInt32();
                }
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
                writer.Write(e.MountainDeepShaftFoodCost); writer.Write(e.MountainDeepShaftWoodCost); writer.Write(e.MountainDeepShaftTicks);
                writer.Write(e.MountainFortFoodCost); writer.Write(e.MountainFortWoodCost); writer.Write(e.MountainFortTicks);
                writer.Write(e.MountainDeepShaftIntervalPermille); writer.Write(e.MountainDeepShaftMaxBuildingsBonus);
                writer.Write(e.MountainFortHpPermille); writer.Write(e.MountainFortRangeBonus);
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
                if (extension.Version >= MountainExtensionVersion)
                {
                    e.MountainDeepShaftFoodCost = reader.ReadInt32(); e.MountainDeepShaftWoodCost = reader.ReadInt32(); e.MountainDeepShaftTicks = reader.ReadInt32();
                    e.MountainFortFoodCost = reader.ReadInt32(); e.MountainFortWoodCost = reader.ReadInt32(); e.MountainFortTicks = reader.ReadInt32();
                    e.MountainDeepShaftIntervalPermille = reader.ReadInt32(); e.MountainDeepShaftMaxBuildingsBonus = reader.ReadInt32();
                    e.MountainFortHpPermille = reader.ReadInt32(); e.MountainFortRangeBonus = reader.ReadInt32();
                }
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
                writer.Write(e.TollgateFeeRadiusMeters); writer.Write(e.TollgateFeeIntervalTicks);
                writer.Write(e.TollgateWoodPerEnemy); writer.Write(e.TollgateFoodPerEnemy);
                writer.Write(e.TollgateNetworkRadiusMeters); writer.Write(e.TollgateNetworkFeeBonusPermille);
                writer.Write(e.TollgateGateDefenceFoodCost); writer.Write(e.TollgateGateDefenceWoodCost); writer.Write(e.TollgateGateDefenceTicks);
                writer.Write(e.TollgateGateNetworkFoodCost); writer.Write(e.TollgateGateNetworkWoodCost); writer.Write(e.TollgateGateNetworkTicks);
                writer.Write(e.TollgateGateDefenceHpPermille); writer.Write(e.TollgateGateDefenceDamageReductionPermille);
                writer.Write(e.TollgateGateNetworkMaxBuildingsBonus);
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
                if (extension.Version >= TollgateExtensionVersion)
                {
                    e.TollgateFeeRadiusMeters = reader.ReadInt32(); e.TollgateFeeIntervalTicks = reader.ReadInt32();
                    e.TollgateWoodPerEnemy = reader.ReadInt32(); e.TollgateFoodPerEnemy = reader.ReadInt32();
                    e.TollgateNetworkRadiusMeters = reader.ReadInt32(); e.TollgateNetworkFeeBonusPermille = reader.ReadInt32();
                    e.TollgateGateDefenceFoodCost = reader.ReadInt32(); e.TollgateGateDefenceWoodCost = reader.ReadInt32(); e.TollgateGateDefenceTicks = reader.ReadInt32();
                    e.TollgateGateNetworkFoodCost = reader.ReadInt32(); e.TollgateGateNetworkWoodCost = reader.ReadInt32(); e.TollgateGateNetworkTicks = reader.ReadInt32();
                    e.TollgateGateDefenceHpPermille = reader.ReadInt32(); e.TollgateGateDefenceDamageReductionPermille = reader.ReadInt32();
                    e.TollgateGateNetworkMaxBuildingsBonus = reader.ReadInt32();
                }
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
        private static ScenarioExtensionData CreateExploreScoutingExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.ExploreScouting ? 1 : 0);
                writer.Write(e.ExploreChunkCells); writer.Write(e.ExploreMinUnexploredPermille);
                writer.Write(e.ExploreMinUnexploredPermilleStage2); writer.Write(e.ExploreMinUnexploredPermilleStage3);
                writer.Write(e.ExploreMinUnexploredPermilleStage4);
                writer.Write(e.ExploreRetargetTicks); writer.Write(e.ExploreAvoidChunks);
                writer.Write(e.ExploreAvoidTicks); writer.Write(e.ExploreAutoUntilTick);
                return new ScenarioExtensionData { Id = ExploreScoutingExtensionId, Version = ExploreScoutingExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadExploreScoutingExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid exploration flag.");
                e.ExploreScouting = enabled != 0;
                e.ExploreChunkCells = reader.ReadInt32(); e.ExploreMinUnexploredPermille = reader.ReadInt32();
                if (extension.Version == ExploreScoutingExtensionV1)
                {
                    e.ExploreMinUnexploredPermilleStage2 = 250;
                    e.ExploreMinUnexploredPermilleStage3 = 100;
                    e.ExploreMinUnexploredPermilleStage4 = 1;
                }
                else
                {
                    e.ExploreMinUnexploredPermilleStage2 = reader.ReadInt32();
                    e.ExploreMinUnexploredPermilleStage3 = reader.ReadInt32();
                    e.ExploreMinUnexploredPermilleStage4 = reader.ReadInt32();
                }
                e.ExploreRetargetTicks = reader.ReadInt32(); e.ExploreAvoidChunks = reader.ReadInt32();
                e.ExploreAvoidTicks = reader.ReadInt32(); e.ExploreAutoUntilTick = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing exploration extension data.");
            }
        }
        private static ScenarioExtensionData CreateAiArmyRecoveryExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AiArmyRecovery ? 1 : 0);
                return new ScenarioExtensionData { Id = AiArmyRecoveryExtensionId, Version = AiArmyRecoveryExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadAiArmyRecoveryExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid AI army recovery flag.");
                e.AiArmyRecovery = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing AI army recovery extension data.");
            }
        }
        private static ScenarioExtensionData CreateLineBuildingCheckExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.LineBuildingCheck ? 1 : 0);
                return new ScenarioExtensionData { Id = LineBuildingCheckExtensionId, Version = LineBuildingCheckExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadLineBuildingCheckExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid line building check flag.");
                e.LineBuildingCheck = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing line building check extension data.");
            }
        }
        private static ScenarioExtensionData CreateNoNegativeStockExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.NoNegativeStock ? 1 : 0);
                return new ScenarioExtensionData { Id = NoNegativeStockExtensionId, Version = NoNegativeStockExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadNoNegativeStockExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid no-negative-stock flag.");
                e.NoNegativeStock = enabled != 0;
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing no-negative-stock extension data.");
            }
        }
        private static ScenarioExtensionData CreateLargeEconomyExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.LargeEconomy ? 1 : 0);
                writer.Write(e.LargeVillagerTrainTicks); writer.Write(e.LargeInfantryTrainTicks);
                writer.Write(e.LargeVillagerBatch); writer.Write(e.LargeInfantryBatch);
                writer.Write(e.LargeBarracksMaxBuildings); writer.Write(e.LargeArmyCapacity);
                writer.Write(e.LargeTownDecisionIntervalTicks); writer.Write(e.LargeTownMinAge);
                writer.Write(e.LargeFactionCap);
                writer.Write(e.LargeResourceAmountPermille); writer.Write(e.LargeFoodAmountPermille);
                writer.Write(e.LargeOreAmountPermille); writer.Write(e.LargeStoneAmountPermille);
                writer.Write(e.LargeGoldAmountPermille);
                writer.Write(e.LargeFoodPerWood); writer.Write(e.LargeConstructionReserveWood); writer.Write(e.LargeTownStoneGatherers);
                 writer.Write(e.LargeTownVillagersPerTown); writer.Write(e.LargeBarracksVillagersPerBuilding); writer.Write(e.LargeTownAutoWorkers);
                 writer.Write(e.LargeTargetRoundTripTicks); writer.Write(e.LargeResourceClusterRadiusMeters);
                 writer.Write(e.LargeClusterDecisionIntervalTicks); writer.Write(e.LargeResourceClusterMaxDropSites);
                 writer.Write(e.LargeClusterMinGatherers); writer.Write(e.LargeDropSiteMax);
                 writer.Write(e.LargeDropSiteMinSpacingMeters); writer.Write(e.LargeClusterSwitchPermille);
                 writer.Write(e.LargeVillagersPerFarm); writer.Write(e.LargeFarmMinimum); writer.Write(e.LargeFarmMaximum);
                 writer.Write(e.LargeInfantryResourceReserveUnits);
                 writer.Write(e.LargeExploreTargetPermille); writer.Write(e.LargeExploreScoutCount);
                 writer.Write(e.LargeExploreChunkCells);
                return new ScenarioExtensionData { Id = LargeEconomyExtensionId,
                    Version = LargeEconomyExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadLargeEconomyExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid large economy flag.");
                e.LargeEconomy = enabled != 0;
                e.LargeVillagerTrainTicks = reader.ReadInt32(); e.LargeInfantryTrainTicks = reader.ReadInt32();
                e.LargeVillagerBatch = reader.ReadInt32(); e.LargeInfantryBatch = reader.ReadInt32();
                e.LargeBarracksMaxBuildings = reader.ReadInt32(); e.LargeArmyCapacity = reader.ReadInt32();
                e.LargeTownDecisionIntervalTicks = reader.ReadInt32(); e.LargeTownMinAge = reader.ReadInt32();
                e.LargeFactionCap = reader.ReadInt32();
                e.LargeResourceAmountPermille = reader.ReadInt32(); e.LargeFoodAmountPermille = reader.ReadInt32();
                e.LargeOreAmountPermille = reader.ReadInt32(); e.LargeStoneAmountPermille = reader.ReadInt32();
                e.LargeGoldAmountPermille = reader.ReadInt32();
                 if (extension.Version >= 2)
                {
                    e.LargeFoodPerWood = reader.ReadInt32(); e.LargeConstructionReserveWood = reader.ReadInt32();
                    e.LargeTownStoneGatherers = reader.ReadInt32();
                }
                if (extension.Version >= 3)
                {
                 e.LargeTownVillagersPerTown = reader.ReadInt32(); e.LargeBarracksVillagersPerBuilding = reader.ReadInt32();
                 e.LargeTownAutoWorkers = reader.ReadInt32();
                 }
                 if (extension.Version >= 4)
                 {
                     e.LargeTargetRoundTripTicks = reader.ReadInt32(); e.LargeResourceClusterRadiusMeters = reader.ReadInt32();
                     e.LargeClusterDecisionIntervalTicks = reader.ReadInt32(); e.LargeResourceClusterMaxDropSites = reader.ReadInt32();
                 e.LargeClusterMinGatherers = reader.ReadInt32();
                 }
                // Version 5 adds the large-map command cap. Older large scenarios remain readable and retain their
                // serialized DropSiteMax value from extension 38 (2 in econ4).
                e.LargeDropSiteMax = extension.Version >= 5 ? reader.ReadInt32() : e.DropSiteMax;
                if (extension.Version >= 6)
                {
                    e.LargeDropSiteMinSpacingMeters = reader.ReadInt32();
                    e.LargeClusterSwitchPermille = reader.ReadInt32();
                    e.LargeVillagersPerFarm = reader.ReadInt32(); e.LargeFarmMinimum = reader.ReadInt32();
                    e.LargeFarmMaximum = reader.ReadInt32(); e.LargeInfantryResourceReserveUnits = reader.ReadInt32();
                }
                if (extension.Version >= 7)
                {
                    e.LargeExploreTargetPermille = reader.ReadInt32(); e.LargeExploreScoutCount = reader.ReadInt32();
                    e.LargeExploreChunkCells = reader.ReadInt32();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing large economy extension data.");
            }
        }
        private static ScenarioExtensionData CreateHouseFlowExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.HouseFlow ? 1 : 0);
                writer.Write(e.HouseFlowStallTicks);
                writer.Write(e.HouseFlowMarginPerProducer);
                writer.Write(e.HouseFlowMaxConcurrent);
                return new ScenarioExtensionData { Id = HouseFlowExtensionId, Version = HouseFlowExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadHouseFlowExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid house-flow flag.");
                e.HouseFlow = enabled != 0;
                e.HouseFlowStallTicks = reader.ReadInt32();
                e.HouseFlowMarginPerProducer = reader.ReadInt32();
                e.HouseFlowMaxConcurrent = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing house-flow extension data.");
            }
        }
        private static ScenarioExtensionData CreateAutoReturnAfterHumanExtension(EconomyRules e)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(e.AutoReturnAfterHuman ? 1 : 0);
                writer.Write(e.AutoReturnArriveMeters);
                writer.Write(e.AutoReturnHoldTicks);
                return new ScenarioExtensionData { Id = AutoReturnAfterHumanExtensionId,
                    Version = AutoReturnAfterHumanExtensionVersion, Data = stream.ToArray() };
            }
        }
        private static void ReadAutoReturnAfterHumanExtension(ScenarioExtensionData extension, EconomyRules e)
        {
            using (var stream = new MemoryStream(extension.Data, false))
            using (var reader = new BinaryReader(stream))
            {
                int enabled = reader.ReadInt32();
                if (enabled < 0 || enabled > 1) throw new InvalidDataException("Invalid automatic human-order return flag.");
                e.AutoReturnAfterHuman = enabled != 0;
                e.AutoReturnArriveMeters = reader.ReadInt32();
                e.AutoReturnHoldTicks = reader.ReadInt32();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing automatic human-order return extension data.");
            }
        }
        private static void Point(BinaryWriter w,SimPoint p) { w.Write(p.X.Raw); w.Write(p.Z.Raw); }
        private static void Goal(BinaryWriter w,PolicyGoal g) { w.Write((byte)g.Kind); w.Write(g.Id); Point(w,g.Point); }
        private static void Text(BinaryWriter w,string v) { var b=Encoding.UTF8.GetBytes(v); if(b.Length>1048576) throw new InvalidDataException("String size."); w.Write((uint)b.Length); w.Write(b); }
        private static string Text(BinaryReader r) { uint n=r.ReadUInt32(); if(n>1048576) throw new InvalidDataException("String size."); var b=r.ReadBytes((int)n); if(b.Length!=n) throw new EndOfStreamException(); return new UTF8Encoding(false,true).GetString(b); }
    }
}
