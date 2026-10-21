using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    public enum CargoShape
    {
        Sack,
        Log,
        RoughStone,
        Ingot,
        StoneBlock,
        Gem,
        Coin,
        Charcoal,
        Bow,
        Crate,
        Book,
        Jar,
        Charm,
        RepairKit,
        Tool
    }

    public readonly struct CargoVisual
    {
        public CargoShape Shape { get; }
        public Color Color { get; }

        public CargoVisual(CargoShape shape, Color color)
        {
            Shape = shape;
            Color = color;
        }
    }

    /// <summary>One explicit visual row for every contract resource; geometry and materials are reused by the layer.</summary>
    public static class GoodsCargoVisuals
    {
        private static readonly Dictionary<ResourceKind, CargoVisual> Table = BuildTable();

        public static CargoVisual Get(ResourceKind kind)
        {
            return Table.TryGetValue(kind, out var visual) ? visual : Table[ResourceKind.Food];
        }

        public static bool HasAllKinds
        {
            get
            {
                foreach (ResourceKind kind in System.Enum.GetValues(typeof(ResourceKind)))
                    if (!Table.ContainsKey(kind)) return false;
                return true;
            }
        }

        private static Dictionary<ResourceKind, CargoVisual> BuildTable()
        {
            var table = new Dictionary<ResourceKind, CargoVisual>();
            table[ResourceKind.Food] = new CargoVisual(CargoShape.Sack, new Color(0.86f, 0.62f, 0.18f));
            table[ResourceKind.Wood] = new CargoVisual(CargoShape.Log, new Color(0.48f, 0.25f, 0.10f));
            table[ResourceKind.Ore] = new CargoVisual(CargoShape.RoughStone, new Color(0.38f, 0.30f, 0.27f));
            table[ResourceKind.Metal] = new CargoVisual(CargoShape.Ingot, new Color(0.68f, 0.72f, 0.80f));
            table[ResourceKind.Stone] = new CargoVisual(CargoShape.StoneBlock, new Color(0.52f, 0.55f, 0.59f));
            table[ResourceKind.Gems] = new CargoVisual(CargoShape.Gem, new Color(0.12f, 0.76f, 0.78f));
            table[ResourceKind.Gold] = new CargoVisual(CargoShape.Coin, new Color(0.92f, 0.68f, 0.16f));
            table[ResourceKind.Charcoal] = new CargoVisual(CargoShape.Charcoal, new Color(0.10f, 0.10f, 0.12f));
            table[ResourceKind.Steel] = new CargoVisual(CargoShape.Ingot, new Color(0.34f, 0.48f, 0.64f));
            table[ResourceKind.BowGear] = new CargoVisual(CargoShape.Bow, new Color(0.62f, 0.31f, 0.12f));
            table[ResourceKind.Rations] = new CargoVisual(CargoShape.Sack, new Color(0.72f, 0.37f, 0.12f));
            table[ResourceKind.Bricks] = new CargoVisual(CargoShape.Crate, new Color(0.68f, 0.23f, 0.14f));
            table[ResourceKind.Tack] = new CargoVisual(CargoShape.Crate, new Color(0.30f, 0.16f, 0.10f));
            table[ResourceKind.Planks] = new CargoVisual(CargoShape.Log, new Color(0.72f, 0.46f, 0.18f));
            table[ResourceKind.Books] = new CargoVisual(CargoShape.Book, new Color(0.34f, 0.20f, 0.55f));
            table[ResourceKind.Preserves] = new CargoVisual(CargoShape.Jar, new Color(0.82f, 0.34f, 0.23f));
            table[ResourceKind.TradeGoods] = new CargoVisual(CargoShape.Crate, new Color(0.18f, 0.52f, 0.30f));
            table[ResourceKind.Incense] = new CargoVisual(CargoShape.Jar, new Color(0.56f, 0.28f, 0.66f));
            table[ResourceKind.Charms] = new CargoVisual(CargoShape.Charm, new Color(0.82f, 0.32f, 0.66f));
            table[ResourceKind.RepairKits] = new CargoVisual(CargoShape.RepairKit, new Color(0.20f, 0.39f, 0.52f));
            table[ResourceKind.Tools] = new CargoVisual(CargoShape.Tool, new Color(0.22f, 0.55f, 0.60f));
            table[ResourceKind.CutStone] = new CargoVisual(CargoShape.StoneBlock, new Color(0.70f, 0.71f, 0.74f));
            table[ResourceKind.Frames] = new CargoVisual(CargoShape.Crate, new Color(0.77f, 0.59f, 0.36f));
            table[ResourceKind.ArmsKit] = new CargoVisual(CargoShape.Crate, new Color(0.59f, 0.36f, 0.25f));
            table[ResourceKind.FineArmsKit] = new CargoVisual(CargoShape.Crate, new Color(0.38f, 0.50f, 0.67f));
            table[ResourceKind.Scholarly] = new CargoVisual(CargoShape.Book, new Color(0.44f, 0.34f, 0.59f));
            table[ResourceKind.SiegeParts] = new CargoVisual(CargoShape.Crate, new Color(0.47f, 0.38f, 0.27f));
            table[ResourceKind.SiegeEngine] = new CargoVisual(CargoShape.Crate, new Color(0.25f, 0.25f, 0.30f));
            table[ResourceKind.EliteKit] = new CargoVisual(CargoShape.Crate, new Color(0.75f, 0.55f, 0.12f));
            table[ResourceKind.Mechanism] = new CargoVisual(CargoShape.Tool, new Color(0.69f, 0.59f, 0.27f));
            return table;
        }
    }
}
