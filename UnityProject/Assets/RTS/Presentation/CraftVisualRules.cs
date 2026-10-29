using Rts.Contracts;

namespace Rts.Presentation
{
    public struct CraftRecipe
    {
        public ResourceKind Input;
        public ResourceKind InputSecondary;
        public ResourceKind Output;

        public bool HasInput { get { return Input != 0; } }
        public bool HasSecondaryInput { get { return InputSecondary != 0; } }
        public bool HasOutput { get { return Output != 0; } }

        public CraftRecipe(ResourceKind input, ResourceKind inputSecondary, ResourceKind output)
        {
            Input = input;
            InputSecondary = inputSecondary;
            Output = output;
        }
    }

    public enum CraftBuildingState
    {
        Idle,
        Constructing,
        Working,
        WaitingForMaterial,
        OutputBlocked,
        StockFull
    }

    /// <summary>Display-only interpretation of the fields already exposed by EconomyView/BuildingView.</summary>
    public static class CraftVisualRules
    {
        public const int OutputBufferLimit = 10;

        public static CraftBuildingState Evaluate(BuildingView building, EconomyView economy = null)
        {
            if (!building.Complete) return CraftBuildingState.Constructing;
            if (IsStockFull(building, economy)) return CraftBuildingState.StockFull;
            if (building.Output >= OutputBufferLimit) return CraftBuildingState.OutputBlocked;
            if (RequiresInput(building.Kind) && building.Input == 0 && building.InputSecondary == 0)
                return CraftBuildingState.WaitingForMaterial;
            if (ProducesWithoutInput(building.Kind) || building.Input > 0 || building.InputSecondary > 0)
                return CraftBuildingState.Working;
            return CraftBuildingState.Idle;
        }

        public static bool RequiresInput(BuildingKind kind)
        {
            return kind == BuildingKind.Smelter || kind == BuildingKind.Steelworks || kind == BuildingKind.Fletcher
                || kind == BuildingKind.Mill || kind == BuildingKind.BrickKiln || kind == BuildingKind.Saddlery
                || kind == BuildingKind.Sawmill || kind == BuildingKind.Scriptorium || kind == BuildingKind.DryingRack
                || kind == BuildingKind.Lapidary || kind == BuildingKind.Weaver || kind == BuildingKind.IncenseHouse
                || kind == BuildingKind.CharmWorkshop || kind == BuildingKind.RepairYard || kind == BuildingKind.ToolShop
                || kind == BuildingKind.Stonecutter || kind == BuildingKind.Joinery || kind == BuildingKind.Hut
                || kind == BuildingKind.Metalworks || kind == BuildingKind.RopeHut || kind == BuildingKind.GearWorkshop;
        }

        public static ResourceKind MissingInputKind(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Smelter: return ResourceKind.Ore;
                case BuildingKind.Steelworks:
                case BuildingKind.Metalworks: return ResourceKind.Metal;
                case BuildingKind.Fletcher: return ResourceKind.Wood;
                case BuildingKind.Mill:
                case BuildingKind.DryingRack:
                case BuildingKind.IncenseHouse: return ResourceKind.Food;
                case BuildingKind.BrickKiln:
                case BuildingKind.Stonecutter:
                case BuildingKind.Lapidary: return ResourceKind.Stone;
                case BuildingKind.Joinery: return ResourceKind.Planks;
                case BuildingKind.Saddlery:
                case BuildingKind.Sawmill:
                case BuildingKind.Scriptorium:
                case BuildingKind.Weaver:
                case BuildingKind.CharmWorkshop:
                case BuildingKind.RepairYard:
                case BuildingKind.ToolShop: return ResourceKind.Wood;
                default: return ResourceKind.Wood;
            }
        }

        /// <summary>Display-only recipe map for the processing buildings exposed by the economy contract.</summary>
        public static bool TryGetRecipe(BuildingKind kind, out CraftRecipe recipe)
        {
            switch (kind)
            {
                case BuildingKind.Mine: recipe = new CraftRecipe(0, 0, ResourceKind.Ore); return true;
                case BuildingKind.LumberCamp: recipe = new CraftRecipe(0, 0, ResourceKind.Wood); return true;
                case BuildingKind.Quarry: recipe = new CraftRecipe(0, 0, ResourceKind.Stone); return true;
                case BuildingKind.Farm: recipe = new CraftRecipe(0, 0, ResourceKind.Food); return true;
                case BuildingKind.Smelter: recipe = new CraftRecipe(ResourceKind.Ore, 0, ResourceKind.Metal); return true;
                case BuildingKind.CharcoalKiln: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.Charcoal); return true;
                case BuildingKind.Steelworks: recipe = new CraftRecipe(ResourceKind.Metal, ResourceKind.Charcoal, ResourceKind.Steel); return true;
                case BuildingKind.Fletcher: recipe = new CraftRecipe(ResourceKind.Wood, ResourceKind.Food, ResourceKind.BowGear); return true;
                case BuildingKind.Mill: recipe = new CraftRecipe(ResourceKind.Food, 0, ResourceKind.Rations); return true;
                case BuildingKind.BrickKiln: recipe = new CraftRecipe(ResourceKind.Stone, ResourceKind.Charcoal, ResourceKind.Bricks); return true;
                case BuildingKind.Saddlery: recipe = new CraftRecipe(ResourceKind.Food, ResourceKind.Wood, ResourceKind.Tack); return true;
                case BuildingKind.Sawmill: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.Planks); return true;
                case BuildingKind.Scriptorium: recipe = new CraftRecipe(ResourceKind.Gold, ResourceKind.Wood, ResourceKind.Books); return true;
                case BuildingKind.DryingRack: recipe = new CraftRecipe(ResourceKind.Food, 0, ResourceKind.Preserves); return true;
                case BuildingKind.Lapidary: recipe = new CraftRecipe(ResourceKind.Stone, ResourceKind.Ore, ResourceKind.Gems); return true;
                case BuildingKind.Weaver: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.TradeGoods); return true;
                case BuildingKind.IncenseHouse: recipe = new CraftRecipe(ResourceKind.Wood, ResourceKind.Food, ResourceKind.Incense); return true;
                case BuildingKind.CharmWorkshop: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.Charms); return true;
                case BuildingKind.RepairYard: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.RepairKits); return true;
                case BuildingKind.ToolShop: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.Tools); return true;
                case BuildingKind.Stonecutter: recipe = new CraftRecipe(ResourceKind.Stone, 0, ResourceKind.CutStone); return true;
                case BuildingKind.Joinery: recipe = new CraftRecipe(ResourceKind.Planks, ResourceKind.CutStone, ResourceKind.Frames); return true;
                case BuildingKind.Hut: recipe = new CraftRecipe(ResourceKind.Stone, ResourceKind.Wood, ResourceKind.StoneTools); return true;
                case BuildingKind.Metalworks: recipe = new CraftRecipe(ResourceKind.Metal, 0, ResourceKind.Fittings); return true;
                case BuildingKind.RopeHut: recipe = new CraftRecipe(ResourceKind.Wood, 0, ResourceKind.Rope); return true;
                case BuildingKind.GearWorkshop: recipe = new CraftRecipe(ResourceKind.Metal, 0, ResourceKind.Gears); return true;
                default: recipe = default(CraftRecipe); return false;
            }
        }

        public static bool IsStockFull(int current, int limit)
        {
            return limit > 0 && current >= limit;
        }

        private static bool ProducesWithoutInput(BuildingKind kind)
        {
            return kind == BuildingKind.Mine || kind == BuildingKind.Drill || kind == BuildingKind.LumberCamp || kind == BuildingKind.Quarry || kind == BuildingKind.Farm;
        }

        public static ResourceKind DrillResource(BuildingView building, EconomyView economy)
        {
            if (economy == null || !economy.CommonDrill || economy.CellSizeMeters <= 0) return (ResourceKind)0;
            int size = economy.DrillSizeCells;
            long cellRaw = (long)economy.CellSizeMeters * 65536L;
            int x0 = (int)((building.Center.X.Raw - (long)size * cellRaw / 2) / cellRaw);
            int z0 = (int)((building.Center.Z.Raw - (long)size * cellRaw / 2) / cellRaw);
            for (int i = 0; i < economy.Resources.Count; i++)
            {
                var resource = economy.Resources[i];
                int x = (int)(resource.Position.X.Raw / cellRaw), z = (int)(resource.Position.Z.Raw / cellRaw);
                if (x >= x0 && x < x0 + size && z >= z0 && z < z0 + size
                    && (resource.Kind == ResourceKind.Wood || resource.Kind == ResourceKind.Stone || resource.Kind == ResourceKind.Ore || resource.Kind == ResourceKind.Gold))
                    return resource.Kind;
            }
            return (ResourceKind)0;
        }

        private static bool IsStockFull(BuildingView building, EconomyView economy)
        {
            if (economy == null) return false;
            if (building.Kind == BuildingKind.Storage)
                return IsStockFull(building.Output, economy.StorageCapacity);
            if (building.Kind == BuildingKind.IncenseHouse)
                return IsStockFull(economy.Incense, economy.IncenseStockLimit);
            if (building.Kind == BuildingKind.CharmWorkshop)
                return IsStockFull(economy.Charms, economy.CharmStockLimit);
            if (building.Kind == BuildingKind.RepairYard)
                return IsStockFull(economy.RepairKits, economy.RepairKitStockLimit);
            if (building.Kind == BuildingKind.ToolShop)
                return IsStockFull(economy.Tools, economy.ToolStockLimit);
            return false;
        }
    }
}
