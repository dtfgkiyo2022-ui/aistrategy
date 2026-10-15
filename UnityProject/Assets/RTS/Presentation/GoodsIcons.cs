using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Loads the replaceable 64px craft icons once and shares them everywhere in the presentation.</summary>
    public static class GoodsIcons
    {
        private const string GoodsRoot = "Icons/Goods/";
        private const string StatusRoot = "Icons/Status/";
        private static readonly Dictionary<ResourceKind, Texture2D> Goods = new Dictionary<ResourceKind, Texture2D>();
        private static readonly Dictionary<CraftBuildingState, Texture2D> Status = new Dictionary<CraftBuildingState, Texture2D>();
        private static readonly Dictionary<Texture2D, Material> Materials = new Dictionary<Texture2D, Material>();

        public static Texture2D Get(ResourceKind kind)
        {
            if (Goods.TryGetValue(kind, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(GoodsRoot + kind);
            Goods[kind] = texture;
            return texture;
        }

        public static Texture2D Get(TopBarResourceVisibility.ResourceKind kind)
        {
            return Get(ToContractKind(kind));
        }

        public static Texture2D GetStatus(CraftBuildingState state)
        {
            if (Status.TryGetValue(state, out var cached)) return cached;
            string name = state == CraftBuildingState.WaitingForMaterial ? "WaitingForMaterial"
                : state == CraftBuildingState.OutputBlocked ? "OutputBlocked"
                : state == CraftBuildingState.StockFull ? "StockFull"
                : state == CraftBuildingState.Working ? "Working" : null;
            var texture = string.IsNullOrEmpty(name) ? null : Resources.Load<Texture2D>(StatusRoot + name);
            Status[state] = texture;
            return texture;
        }

        public static Material GetMaterial(ResourceKind kind)
        {
            return GetMaterial(Get(kind));
        }

        public static Material GetMaterial(Texture2D texture)
        {
            if (texture == null) return null;
            if (Materials.TryGetValue(texture, out var material))
            {
                if (material != null) return material;
            }
            material = PresentationMaterials.NewUnlitTextured(texture);
            Materials[texture] = material;
            return material;
        }

        public static bool HasAllGoodsIcons()
        {
            foreach (ResourceKind kind in System.Enum.GetValues(typeof(ResourceKind)))
                if (Get(kind) == null) return false;
            return true;
        }

        private static ResourceKind ToContractKind(TopBarResourceVisibility.ResourceKind kind)
        {
            switch (kind)
            {
                case TopBarResourceVisibility.ResourceKind.Food: return ResourceKind.Food;
                case TopBarResourceVisibility.ResourceKind.Wood: return ResourceKind.Wood;
                case TopBarResourceVisibility.ResourceKind.Ore: return ResourceKind.Ore;
                case TopBarResourceVisibility.ResourceKind.Metal: return ResourceKind.Metal;
                case TopBarResourceVisibility.ResourceKind.Stone: return ResourceKind.Stone;
                case TopBarResourceVisibility.ResourceKind.Gems: return ResourceKind.Gems;
                case TopBarResourceVisibility.ResourceKind.Gold: return ResourceKind.Gold;
                case TopBarResourceVisibility.ResourceKind.Charcoal: return ResourceKind.Charcoal;
                case TopBarResourceVisibility.ResourceKind.Steel: return ResourceKind.Steel;
                case TopBarResourceVisibility.ResourceKind.BowGear: return ResourceKind.BowGear;
                case TopBarResourceVisibility.ResourceKind.Rations: return ResourceKind.Rations;
                case TopBarResourceVisibility.ResourceKind.Bricks: return ResourceKind.Bricks;
                case TopBarResourceVisibility.ResourceKind.Tack: return ResourceKind.Tack;
                case TopBarResourceVisibility.ResourceKind.Planks: return ResourceKind.Planks;
                case TopBarResourceVisibility.ResourceKind.Books: return ResourceKind.Books;
                case TopBarResourceVisibility.ResourceKind.Incense: return ResourceKind.Incense;
                case TopBarResourceVisibility.ResourceKind.Charms: return ResourceKind.Charms;
                case TopBarResourceVisibility.ResourceKind.RepairKits: return ResourceKind.RepairKits;
                case TopBarResourceVisibility.ResourceKind.Tools: return ResourceKind.Tools;
                default: return ResourceKind.Food;
            }
        }
    }
}
