using Rts.Contracts;

namespace Rts.Simulation
{
    internal readonly struct UniqueGoodsSnapshot
    {
        internal readonly uint FactionId;
        internal readonly ResourceKind Resource;
        internal readonly int Produced, Consumed, FinalStock, MaxStock;

        internal UniqueGoodsSnapshot(uint factionId, ResourceKind resource, int produced, int consumed, int finalStock, int maxStock)
        {
            FactionId = factionId; Resource = resource; Produced = produced; Consumed = consumed; FinalStock = finalStock; MaxStock = maxStock;
        }
    }

    public sealed partial class Simulation
    {
        private static int UniqueGoodsStock(FactionEconomy e, ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Charcoal: return e.Charcoal;
                case ResourceKind.Steel: return e.Steel;
                case ResourceKind.BowGear: return e.BowGear;
                case ResourceKind.Rations: return e.Rations;
                case ResourceKind.Bricks: return e.Bricks;
                case ResourceKind.Tack: return e.Tack;
                case ResourceKind.Planks: return e.Planks;
                case ResourceKind.Books: return e.Books;
                case ResourceKind.Preserves: return e.Preserves;
                case ResourceKind.TradeGoods: return e.TradeGoods;
                case ResourceKind.Incense: return e.Incense;
                case ResourceKind.Charms: return e.Charms;
                case ResourceKind.RepairKits: return e.RepairKits;
                case ResourceKind.Tools: return e.Tools;
                case ResourceKind.CutStone: return e.CutStone;
                case ResourceKind.Frames: return e.Frames;
                case ResourceKind.ArmsKit: return e.ArmsKit;
                case ResourceKind.Scholarly: return e.Scholarly;
                case ResourceKind.FineArmsKit: return e.FineArmsKit;
                case ResourceKind.SiegeParts: return e.SiegeParts;
                case ResourceKind.Mechanism: return e.Mechanism;
                case ResourceKind.SiegeEngine: return e.SiegeEngine;
                case ResourceKind.EliteKit: return e.EliteKit;
                case ResourceKind.StoneTools: return e.StoneTools;
                case ResourceKind.Jerky: return e.Jerky;
                case ResourceKind.Fittings: return e.Fittings;
                case ResourceKind.Rope: return e.Rope;
                case ResourceKind.Gears: return e.Gears;
                case ResourceKind.Slag: return e.Slag;
                case ResourceKind.Sand: return e.Sand;
                case ResourceKind.Glass: return e.Glass;
                case ResourceKind.Lens: return e.Lens;
                case ResourceKind.Precision: return e.Precision;
                default: return 0;
            }
        }

        private static int UniqueGoodsProduced(FactionEconomy e, ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Rations: return e.RationsProduced;
                case ResourceKind.Bricks: return e.BricksProduced;
                case ResourceKind.Tack: return e.TackProduced;
                case ResourceKind.Planks: return e.PlanksProduced;
                case ResourceKind.Books: return e.BooksProduced;
                case ResourceKind.Preserves: return e.PreservesProduced;
                case ResourceKind.TradeGoods: return e.TradeGoodsProduced;
                case ResourceKind.Incense: return e.IncenseProduced;
                case ResourceKind.Charms: return e.CharmsProduced;
                case ResourceKind.RepairKits: return e.RepairKitsProduced;
                case ResourceKind.Tools: return e.ToolsProduced;
                case ResourceKind.CutStone: return e.CutStoneProduced;
                case ResourceKind.Frames: return e.FramesProduced;
                case ResourceKind.ArmsKit: return e.ArmsKitProduced;
                case ResourceKind.Scholarly: return e.ScholarlyProduced;
                case ResourceKind.FineArmsKit: return e.FineArmsKitProduced;
                case ResourceKind.SiegeParts: return e.SiegePartsProduced;
                case ResourceKind.Mechanism: return e.MechanismProduced;
                case ResourceKind.SiegeEngine: return e.SiegeEngineProduced;
                case ResourceKind.EliteKit: return e.EliteKitProduced;
                case ResourceKind.StoneTools: return e.StoneToolsProduced;
                case ResourceKind.Jerky: return e.JerkyProduced;
                case ResourceKind.Fittings: return e.FittingsProduced;
                case ResourceKind.Rope: return e.RopeProduced;
                case ResourceKind.Gears: return e.GearsProduced;
                case ResourceKind.Sand: return e.SandGathered;
                case ResourceKind.Glass: return e.GlassProduced;
                case ResourceKind.Lens: return e.LensProduced;
                case ResourceKind.Precision: return e.PrecisionProduced;
                default: return 0;
            }
        }

        private static int UniqueGoodsConsumed(FactionEconomy e, ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Rations: return e.RationsConsumed;
                case ResourceKind.Bricks: return e.BricksConsumed;
                case ResourceKind.Tack: return e.TackConsumed;
                case ResourceKind.Planks: return e.PlanksConsumed;
                case ResourceKind.Books: return e.BooksConsumed;
                case ResourceKind.TradeGoods: return e.TradeGoodsConvertedFood + e.TradeGoodsConvertedStone + e.LivelihoodTradeGoodsPaid;
                case ResourceKind.Preserves: return e.PreservesConvertedFood + e.LivelihoodPreservesPaid;
                case ResourceKind.Incense: return e.IncenseConsumed + e.LivelihoodIncensePaid;
                case ResourceKind.Charms: return e.CharmsConsumed;
                case ResourceKind.RepairKits: return e.RepairKitsConsumed;
                case ResourceKind.Tools: return e.ToolsConsumed + e.LivelihoodToolsPaid;
                case ResourceKind.CutStone: return e.CutStoneConsumed;
                case ResourceKind.Frames: return e.FramesConsumed;
                case ResourceKind.ArmsKit: return e.ArmsKitConsumed;
                case ResourceKind.Scholarly: return e.ScholarlyConsumed;
                case ResourceKind.FineArmsKit: return e.FineArmsKitConsumed;
                case ResourceKind.SiegeParts: return e.SiegePartsConsumed;
                case ResourceKind.Mechanism: return e.MechanismConsumed;
                case ResourceKind.SiegeEngine: return e.SiegeEngineConsumed;
                case ResourceKind.EliteKit: return e.EliteKitConsumed;
                case ResourceKind.StoneTools: return e.StoneToolsConsumed;
                case ResourceKind.Jerky: return e.JerkyConsumed;
                case ResourceKind.Fittings: return e.FittingsConsumed;
                case ResourceKind.Rope: return e.RopeConsumed;
                case ResourceKind.Gears: return e.GearsConsumed;
                case ResourceKind.Glass: return e.GlassConsumed;
                case ResourceKind.Lens: return e.LensConsumed;
                case ResourceKind.Precision: return e.PrecisionConsumed;
                default: return 0;
            }
        }

        private void TrackUniqueGoodsMax()
        {
            for (int f = 0; f < 2; f++)
                for (int raw = 8; raw <= 40; raw++)
                {
                    int stock = UniqueGoodsStock(world.Economies[f], (ResourceKind)raw);
                    if (stock > uniqueGoodsMax[f, raw]) uniqueGoodsMax[f, raw] = stock;
                }
        }

        internal UniqueGoodsSnapshot[] CaptureUniqueGoodsStats()
        {
            var result = new UniqueGoodsSnapshot[66];
            int at = 0;
            for (uint f = 1; f <= 2; f++)
            {
                var e = world.Economies[f - 1];
                for (int raw = 8; raw <= 40; raw++)
                {
                    var resource = (ResourceKind)raw;
                    result[at++] = new UniqueGoodsSnapshot(f, resource, UniqueGoodsProduced(e, resource), UniqueGoodsConsumed(e, resource), UniqueGoodsStock(e, resource), uniqueGoodsMax[f - 1, raw]);
                }
            }
            return result;
        }
    }
}
