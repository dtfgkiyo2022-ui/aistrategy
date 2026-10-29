using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-2 belts (technical-design-v3 11). Items are single things, at most one per cell, and each waits
    /// BeltTicksPerCell ticks on a cell before it moves on. Belts do not block movement. Every method returns at once
    /// without industry, so a V3-1 economy runs exactly as before.
    /// </summary>
    public sealed partial class Simulation
    {
        private bool IndustryOn => world.Config.Economy.Enabled && world.Config.Economy.Industry;
        private bool ProcessingOn => IndustryOn && world.Config.Economy.ProcessingChain;
        private bool BeltDropOffOn => IndustryOn && world.Config.Economy.BeltDropOff;
        private bool BeltStackOn => IndustryOn && world.Config.Economy.BeltStack;
        private bool BeltCraftOn => BeltStackOn && CraftRequiredOn && world.Config.Economy.BeltCraft;

        private int BeltItemCount(BeltState belt)
        => belt.Item == 0 ? 0 : BeltStackOn ? Math.Max(1, (int)belt.StackCount) : 1;

        private int BeltCapacity => BeltStackOn ? world.Config.Economy.BeltStackSize : 1;

        private bool BeltCanReceiveLoad(BeltState belt, ResourceKind item)
            => belt.FactionId != 0 && !IsSpecialComponent(belt.Component)
                && (belt.Item == 0 || BeltStackOn && belt.Item == item && BeltItemCount(belt) < BeltCapacity);

        private bool BeltCanWaitForLoad(BeltState belt, ResourceKind item)
            => belt.FactionId != 0 && !IsSpecialComponent(belt.Component)
                && (!BeltStackOn || belt.Item == 0 || belt.Item == item);

        private void PutOnBelt(ref BeltState belt, ResourceKind item, int amount)
        {
            if (amount <= 0) return;
            if (belt.Item == 0)
            {
                belt.Item = item;
                belt.StackCount = (byte)Math.Min(BeltCapacity, amount);
                belt.Progress = 0;
            }
            else
            {
                belt.StackCount = (byte)Math.Min(BeltCapacity, BeltItemCount(belt) + amount);
            }
        }

        private void TakeFromBelt(ref BeltState belt, int amount)
        {
            if (amount <= 0 || belt.Item == 0) return;
            if (!BeltStackOn || BeltItemCount(belt) <= amount)
            {
                belt.Item = 0; belt.StackCount = 0; belt.Progress = 0;
                return;
            }
            belt.StackCount = (byte)(BeltItemCount(belt) - amount);
            belt.Progress = 0;
        }
        // Indexed by faction/resource. Each used entry is allocated once and reused after invalidation.
        private bool[][] beltDropOffReachability;
        private bool[] beltDropOffReachabilityValid;
        private int[] beltDropOffSeen, beltDropOffQueue;
        private int beltDropOffSeenStamp;
        private ulong beltDropOffDestinationSignature;
        private bool beltDropOffDestinationSignatureSet;
        private int[] beltProcessingDistance, beltProcessingWalk, beltProcessingOrderWorkspace;
        private byte[] beltProcessingState;
        private long[] beltProcessingSortKeys;
        private int beltProcessingOrderCount;
        private bool beltProcessingOrderValid;

        private void InvalidateBeltCaches()
        {
            beltProcessingOrderValid = false;
            InvalidateBeltDropOffReachability();
        }

        private void InvalidateBeltDropOffReachability()
        {
            if (beltDropOffReachabilityValid != null) Array.Clear(beltDropOffReachabilityValid, 0, beltDropOffReachabilityValid.Length);
        }

        private void RefreshBeltDropOffDestinationSignature()
        {
            if (!BeltDropOffOn) return;
            unchecked
            {
                ulong signature = 1469598103934665603UL;
                signature = BeltDropOffSignatureMix(signature, (uint)world.BuildingCount);
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var building = world.Buildings[i];
                    signature = BeltDropOffSignatureMix(signature, (uint)i);
                    signature = BeltDropOffSignatureMix(signature, building.Id);
                    signature = BeltDropOffSignatureMix(signature, (uint)building.Kind);
                    signature = BeltDropOffSignatureMix(signature, building.FactionId);
                    signature = BeltDropOffSignatureMix(signature, building.Complete ? 1U : 0U);
                    signature = BeltDropOffSignatureMix(signature, (uint)building.OriginCell);
                    signature = BeltDropOffSignatureMix(signature, building.Alive ? 1U : 0U);
                    signature = BeltDropOffSignatureMix(signature, (uint)building.Recipe);
                }
                for (int f = 0; f < 2; f++)
                {
                    signature = BeltDropOffSignatureMix(signature, world.Economies[f].Age);
                    signature = BeltDropOffSignatureMix(signature, (uint)world.Economies[f].Civ);
                    signature = BeltDropOffSignatureMix(signature, world.Economies[f].Techs);
                }
                if (!beltDropOffDestinationSignatureSet || beltDropOffDestinationSignature != signature)
                {
                    beltDropOffDestinationSignature = signature;
                    beltDropOffDestinationSignatureSet = true;
                    InvalidateBeltDropOffReachability();
                }
            }
        }

        private static ulong BeltDropOffSignatureMix(ulong hash, ulong value)
            => (hash ^ value) * 1099511628211UL;

        /// <summary>Economy step, before gathering (11.3): downstream belts first, so a full line moves without gaps.</summary>
        private void AdvanceBelts()
        {
            if (!IndustryOn) return;
            EnsureBeltProcessingOrder();
            for (int orderIndex = 0; orderIndex < beltProcessingOrderCount; orderIndex++)
            {
                int cell = world.BeltOrder[orderIndex];
                ref var belt = ref world.Belts[cell];
                if (IsSpecialComponent(belt.Component)) continue;
                if (belt.Item == 0 || BeltItemCount(belt) == 0) continue;
                int ticksPerCell = BeltTicksFor(belt);
                if (belt.Progress < ticksPerCell) belt.Progress++;
                if (belt.Progress < ticksPerCell) continue;
                int next = BeltOutputCell(cell, ref belt);
                if (next < 0) continue;
                // An underground entrance transfers to its paired exit without inspecting the cells between them.
                if (belt.Component == BeltComponentKind.UndergroundEntrance)
                {
                    if (next >= world.Belts.Length || world.Belts[next].FactionId != belt.FactionId) continue;
                }
                ref var target = ref world.Belts[next];
                ResourceKind item = belt.Item;
                int amount = BeltItemCount(belt);
                if (target.FactionId == belt.FactionId)
                {
                    if (target.Component == BeltComponentKind.Merger && !MergerMayReceive(next, cell, ref target)) continue;
                    if (!BeltCanReceiveLoad(target, item)) continue;
                    int moved = target.Item == 0 ? Math.Min(amount, BeltCapacity) : Math.Min(amount, BeltCapacity - BeltItemCount(target));
                    if (moved <= 0) continue;
                    PutOnBelt(ref target, item, moved);
                    TakeFromBelt(ref belt, moved);
                    if (belt.Item == 0 && belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                    if (belt.Item == 0 && target.Component == BeltComponentKind.Merger) target.SplitterRightNext = !target.SplitterRightNext;
                }
                else if (target.FactionId == 0 && (FeedsOwnCore(next, belt.FactionId)
                    || AiExpandOn && FeedsCompletedTown(next, belt.FactionId)))
                {
                    AddStock(belt.FactionId, item, amount);
                    RecordLargeBeltDelivery(belt.FactionId, item, amount);
                    TakeFromBelt(ref belt, amount);
                    if (belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                }
                else if (target.FactionId == 0 && BeltDropOffOn && BeltDropOffStockDestination(next, belt.FactionId, item))
                {
                    AddStock(belt.FactionId, item, amount);
                    RecordLargeBeltDelivery(belt.FactionId, item, amount);
                    TakeFromBelt(ref belt, amount);
                    if (belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                }
                else if (target.FactionId == 0 && TryFeedBuilding(next, belt.FactionId, item))
                {
                    int accepted = 1;
                    if (BeltStackOn)
                    {
                        accepted = 0;
                        while (accepted < amount && TryFeedBuilding(next, belt.FactionId, item)) accepted++;
                    }
                    RecordLargeBeltDelivery(belt.FactionId, item, accepted);
                    TakeFromBelt(ref belt, accepted);
                    if (belt.Item == 0 && belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                }
                // Anything else - bare ground, an enemy belt, a building that does not take it - holds the item where it is.
            }
        }

        // A fast belt is never slower than a normal one, even where a scenario speeds normal belts up (test fixtures do).
        private int BeltTicksFor(BeltState belt)
        {
            if (belt.VeryFast && BeltCraftOn)
                return Math.Min(world.Config.Economy.VeryFastBeltTicksPerCell, world.Config.Economy.BeltTicksPerCell);
            return belt.Fast && world.Config.Economy.BeltComponents
                ? Math.Min(world.Config.Economy.FastBeltTicksPerCell, world.Config.Economy.BeltTicksPerCell)
                : world.Config.Economy.BeltTicksPerCell;
        }

        private int BeltOutputCell(int cell, ref BeltState belt)
        {
            if (!world.Config.Economy.BeltComponents || belt.Component == BeltComponentKind.None)
                return BeltNext(cell, belt.Facing);
            if (IsSpecialComponent(belt.Component)) return -1;
            if (belt.Component == BeltComponentKind.UndergroundEntrance) return belt.PairCell;
            if (belt.Component == BeltComponentKind.Merger) return BeltNext(cell, belt.Facing);
            Facing direction = belt.Facing;
            if (belt.Component == BeltComponentKind.Splitter)
            {
                // The forward exit is the first choice; the left exit is the deterministic fallback.
                if (belt.Priority == BeltPriority.Left) direction = TurnLeft(belt.Facing);
                else if (belt.Priority == BeltPriority.Right) direction = belt.Facing;
                else if (belt.SplitterRightNext) direction = TurnLeft(belt.Facing);
                int preferred = BeltNext(cell, direction);
                if (BeltCanReceive(preferred, belt.FactionId, belt.Item)) return preferred;
                Facing alternateFacing = direction == belt.Facing ? TurnLeft(belt.Facing) : belt.Facing;
                int alternate = BeltNext(cell, alternateFacing);
                return BeltCanReceive(alternate, belt.FactionId, belt.Item) ? alternate : preferred;
            }
            if (belt.Component == BeltComponentKind.Sorter && belt.Item == belt.SorterKind)
                direction = TurnLeft(belt.Facing);
            return BeltNext(cell, direction);
        }

        private bool MergerMayReceive(int mergerCell, int sourceCell, ref BeltState merger)
        {
            if (!BeltCraftOn) return true;
            BeltPriority side = MergerSide(mergerCell, sourceCell, merger.Facing);
            if (side == BeltPriority.None) return true; // the rear input is always admitted.
            if (merger.Priority == side) return true;
            if (merger.Priority != BeltPriority.None)
                return !MergerHasItemOnSide(mergerCell, merger.Facing, merger.Priority);
            BeltPriority next = merger.SplitterRightNext ? BeltPriority.Right : BeltPriority.Left;
            return side == next || !MergerHasItemOnSide(mergerCell, merger.Facing, next);
        }

        private bool MergerHasItemOnSide(int mergerCell, Facing facing, BeltPriority side)
        {
            int cell = BeltNext(mergerCell, side == BeltPriority.Left ? TurnLeft(facing) : TurnRight(facing));
            return cell >= 0 && cell < world.Belts.Length && world.Belts[cell].FactionId != 0 && world.Belts[cell].Item != 0;
        }

        private BeltPriority MergerSide(int mergerCell, int sourceCell, Facing facing)
        {
            if (sourceCell == BeltNext(mergerCell, TurnLeft(facing))) return BeltPriority.Left;
            if (sourceCell == BeltNext(mergerCell, TurnRight(facing))) return BeltPriority.Right;
            return BeltPriority.None;
        }

        private bool BeltCanReceive(int cell, uint faction, ResourceKind item = 0)
            => cell >= 0 && cell < world.Belts.Length && world.Belts[cell].FactionId == faction
                && !IsSpecialComponent(world.Belts[cell].Component)
                && (world.Belts[cell].Item == 0 || BeltStackOn && item != 0
                    && world.Belts[cell].Item == item && BeltItemCount(world.Belts[cell]) < BeltCapacity);

        private static Facing TurnLeft(Facing facing)
            => (Facing)(((int)facing + 3) % 4);

        private static Facing TurnRight(Facing facing)
            => (Facing)(((int)facing + 1) % 4);

        /// <summary>
        /// Belts by distance to the end of their line, then cell id (11.3). A line that runs into a loop has no end and
        /// goes last. Depends on the belts alone, so it is a cache that any change to a belt clears.
        /// </summary>
        private void BeltProcessingOrder()
        {
            EnsureBeltProcessingWorkspace();
            int cells = world.Belts.Length, count = 0;
            Array.Clear(beltProcessingState, 0, beltProcessingState.Length);
            for (int start = 0; start < cells; start++)
            {
                if (world.Belts[start].FactionId == 0 || beltProcessingState[start] == 2) continue;
                int length = 0, cell = start, end;
                while (true)
                {
                    if (beltProcessingState[cell] == 1) { end = int.MaxValue; break; }                 // closed a loop
                    if (beltProcessingState[cell] == 2) { end = beltProcessingDistance[cell]; break; } // joined a known line
                    beltProcessingState[cell] = 1;
                    beltProcessingWalk[length++] = cell;
                    int next = BeltPrimaryNext(cell, world.Belts[cell]);
                    if (next < 0 || next >= cells || world.Belts[next].FactionId != world.Belts[cell].FactionId) { end = -1; break; } // this cell is the end
                    cell = next;
                }
                for (int i = length - 1; i >= 0; i--)
                {
                    end = end == int.MaxValue ? int.MaxValue : end + 1;
                    beltProcessingDistance[beltProcessingWalk[i]] = end;
                    beltProcessingState[beltProcessingWalk[i]] = 2;
                }
            }
            for (int i = 0; i < cells; i++) if (world.Belts[i].FactionId != 0) count++;
            count = 0;
            for (int i = 0; i < cells; i++)
                if (world.Belts[i].FactionId != 0)
                    beltProcessingSortKeys[count++] = ((long)(uint)beltProcessingDistance[i] << 32) | (uint)i;
            Array.Sort(beltProcessingSortKeys, 0, count);
            for (int i = 0; i < count; i++) beltProcessingOrderWorkspace[i] = (int)(uint)beltProcessingSortKeys[i];
            beltProcessingOrderCount = count;
            world.BeltOrder = beltProcessingOrderWorkspace;
        }

        private void EnsureBeltProcessingOrder()
        {
            if (beltProcessingOrderValid) return;
            BeltProcessingOrder();
            beltProcessingOrderValid = true;
        }

        private void EnsureBeltProcessingWorkspace()
        {
            if (beltProcessingDistance != null) return;
            int cells = world.Belts.Length;
            beltProcessingDistance = new int[cells];
            beltProcessingState = new byte[cells];
            beltProcessingWalk = new int[cells];
            beltProcessingSortKeys = new long[cells];
            beltProcessingOrderWorkspace = new int[cells];
        }

        private int BeltPrimaryNext(int cell, BeltState belt)
        {
            if (belt.Component == BeltComponentKind.UndergroundEntrance) return belt.PairCell;
            if (IsSpecialComponent(belt.Component)) return -1;
            return BeltNext(cell, belt.Facing);
        }

        private static bool IsInserterComponent(BeltComponentKind component)
            => component == BeltComponentKind.Inserter || component == BeltComponentKind.FilterInserter || component == BeltComponentKind.LongInserter;

        private static bool IsSpecialComponent(BeltComponentKind component)
            => IsInserterComponent(component) || component == BeltComponentKind.Catapult || component == BeltComponentKind.CatapultReceiver;

        /// <summary>The cell a belt on <paramref name="cell"/> hands to, or -1 off the map.</summary>
        private int BeltNext(int cell, Facing facing)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, x = cell % width, z = cell / width;
            switch (facing)
            {
                case Facing.North: return z + 1 < height ? cell + width : -1;
                case Facing.East: return x + 1 < width ? cell + 1 : -1;
                case Facing.South: return z > 0 ? cell - width : -1;
                default: return x > 0 ? cell - 1 : -1;
            }
        }

        /// <summary>A cell inside the core's radius delivers to that core.</summary>
        private bool FeedsOwnCore(int cell, uint faction)
        {
            var core = OwnCore(faction);
            if (core.Hp <= 0) return false;
            if (cell < 0 || cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells)
                return InRange(world.Map.Center(cell), core.Definition.Position, world.Config.Rules.CoreRadius);
            return CoreRadiusCells((int)world.Factions[faction - 1].CoreId - 1)[cell];
        }

        private bool FeedsCompletedTown(int cell, uint faction)
        {
            var point = world.Map.Center(cell);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != faction || town.Kind != BuildingKind.Town) continue;
                if (InRange(point, BuildingCenter(town), world.Config.Rules.CoreRadius)) return true;
            }
            return false;
        }

        // Cores and resource nodes are fixed when the world is built and never move, so whether a cell is inside a core
        // or holds a node is worked out once per map. Belt searches ask for every cell they reach, and scanning every
        // node (and a BigInteger range test per core) each time made route searches the heaviest part of a decision.
        private bool[][] coreRadiusCells;
        private bool[] nodeCells;

        private bool InsideAnyCore(int cell)
        {
            if (cell < 0 || cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells) return InsideAnyCoreScan(cell);
            for (int i = 0; i < world.Cores.Length; i++) if (CoreRadiusCells(i)[cell]) return true;
            return false;
        }

        /// <summary>Exactly the cells whose centre is within the core radius of core <paramref name="coreIndex"/>.</summary>
        private bool[] CoreRadiusCells(int coreIndex)
        {
            if (coreRadiusCells == null) coreRadiusCells = new bool[world.Cores.Length][];
            if (coreRadiusCells[coreIndex] != null) return coreRadiusCells[coreIndex];
            // A cell whose centre lies outside the square around the core is farther than the radius from it, so only the
            // cells inside the square need the exact test; every other cell stays false.
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            long size = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw, radius = world.Config.Rules.CoreRadius.Raw;
            var position = world.Cores[coreIndex].Definition.Position;
            var inside = new bool[width * height];
            long x = position.X.Raw, z = position.Z.Raw;
            int x0 = (int)Math.Max(0, (x - radius) / size - 1), x1 = (int)Math.Min(width - 1, (x + radius) / size + 1);
            int z0 = (int)Math.Max(0, (z - radius) / size - 1), z1 = (int)Math.Min(height - 1, (z + radius) / size + 1);
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                    inside[cz * width + cx] = InRange(world.Map.Center(cz * width + cx), position, world.Config.Rules.CoreRadius);
            return coreRadiusCells[coreIndex] = inside;
        }

        private bool InsideAnyCoreScan(int cell)
        {
            foreach (var core in world.Cores)
                if (InRange(world.Map.Center(cell), core.Definition.Position, world.Config.Rules.CoreRadius)) return true;
            return false;
        }

        private void AddStock(uint faction, ResourceKind kind, int amount)
        {
            ref var economy = ref world.Economies[faction - 1];
            switch (kind)
            {
                case ResourceKind.Food: economy.Food = checked(economy.Food + amount); break;
                case ResourceKind.Wood: economy.Wood = checked(economy.Wood + amount); break;
                case ResourceKind.Ore: economy.Ore = checked(economy.Ore + amount); break;
                case ResourceKind.Metal:
                    economy.Metal = checked(economy.Metal + amount);
                    if (AiFactoryOn) economy.MetalProduced = checked(economy.MetalProduced + amount);
                    break;
                case ResourceKind.Stone: economy.Stone = checked(economy.Stone + amount); break;
                case ResourceKind.Gems: economy.Gems = checked(economy.Gems + amount); break;
                case ResourceKind.Charcoal: economy.Charcoal = checked(economy.Charcoal + amount); break;
                case ResourceKind.Steel: economy.Steel = checked(economy.Steel + amount); break;
                case ResourceKind.BowGear: economy.BowGear = checked(economy.BowGear + amount); break;
            case ResourceKind.Rations: economy.Rations = checked(economy.Rations + amount); break;
            case ResourceKind.Bricks: economy.Bricks = checked(economy.Bricks + amount); break;
            case ResourceKind.Tack: economy.Tack = checked(economy.Tack + amount); break;
                case ResourceKind.Planks: economy.Planks = checked(economy.Planks + amount); break;
                case ResourceKind.CutStone: economy.CutStone = checked(economy.CutStone + amount); break;
                case ResourceKind.Frames: economy.Frames = checked(economy.Frames + amount); break;
                case ResourceKind.ArmsKit: economy.ArmsKit = checked(economy.ArmsKit + amount); break;
                case ResourceKind.Scholarly: economy.Scholarly = checked(economy.Scholarly + amount); break;
                case ResourceKind.FineArmsKit: economy.FineArmsKit = checked(economy.FineArmsKit + amount); break;
                case ResourceKind.SiegeParts: economy.SiegeParts = checked(economy.SiegeParts + amount); break;
                case ResourceKind.Mechanism: economy.Mechanism = checked(economy.Mechanism + amount); break;
                case ResourceKind.SiegeEngine: economy.SiegeEngine = checked(economy.SiegeEngine + amount); break;
                case ResourceKind.EliteKit: economy.EliteKit = checked(economy.EliteKit + amount); break;
                case ResourceKind.StoneTools: economy.StoneTools = checked(economy.StoneTools + amount); break;
                case ResourceKind.Jerky: economy.Jerky = checked(economy.Jerky + amount); break;
                case ResourceKind.Books: economy.Books = checked(economy.Books + amount); break;
                case ResourceKind.Incense: economy.Incense = Math.Min(world.Config.Economy.IncenseStockLimit, checked(economy.Incense + amount)); break;
                case ResourceKind.Charms: economy.Charms = Math.Min(world.Config.Economy.CharmStockLimit, checked(economy.Charms + amount)); break;
                case ResourceKind.RepairKits: economy.RepairKits = Math.Min(world.Config.Economy.RepairKitStockLimit, checked(economy.RepairKits + amount)); break;
                case ResourceKind.Tools: economy.Tools = Math.Min(world.Config.Economy.ToolStockLimit, checked(economy.Tools + amount)); break;
                case ResourceKind.Fittings: economy.Fittings = checked(economy.Fittings + amount); break;
                case ResourceKind.Rope: economy.Rope = checked(economy.Rope + amount); break;
                case ResourceKind.Gears: economy.Gears = checked(economy.Gears + amount); break;
                case ResourceKind.Preserves:
                int preserveValue = AgeStrength(faction, CivKind.Fishing, 3) ? world.Config.Economy.FishingPreserveFoodValue : world.Config.Economy.PreserveFoodValue;
                economy.Food = checked(economy.Food + amount * preserveValue);
                economy.PreservesConvertedFood = checked(economy.PreservesConvertedFood + amount * preserveValue);
                    break;
                case ResourceKind.TradeGoods:
                    int value = checked(amount * (AgeStrength(faction, CivKind.Caravan, 2) ? world.Config.Economy.CaravanTradeGoodsValue : world.Config.Economy.TradeGoodsValue));
                    if (economy.Food <= economy.Stone)
                    {
                        economy.Food = checked(economy.Food + value);
                        economy.TradeGoodsConvertedFood = checked(economy.TradeGoodsConvertedFood + value);
                    }
                    else
                    {
                        economy.Stone = checked(economy.Stone + value);
                        economy.TradeGoodsConvertedStone = checked(economy.TradeGoodsConvertedStone + value);
                    }
                    break;
                case ResourceKind.Gold: economy.Gold = checked(economy.Gold + amount); break;
            }
        }

        /// <summary>
        /// PlaceBelt (14): each cell of the run is checked on its own, in order - on the map, open ground, no resource
        /// point, outside every core, no belt yet, under the faction's limit, wood in stock. A cell that fails is skipped.
        /// </summary>
        private void PlaceBelts(uint faction, EconomyCommand c, bool held)
        {
            var aiReason = TakeAiReason(automaticEconomyAction && !held);
            if (!IndustryOn) return;
            var rules = world.Config.Economy;
            bool veryFast = BeltCraftOn && c.VeryFastBelt;
            if (veryFast && world.Economies[faction - 1].Age < rules.VeryFastBeltMinAge) return;
            bool fast = rules.BeltComponents && (c.FastBelt || veryFast) && world.Economies[faction - 1].Age >= rules.FastBeltAge;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            int placedInCommand = 0;
            bool craftRequiredBelts = CraftRequiredOn && world.Economies[faction - 1].Age >= 1;
            for (int i = 0; i < c.Cells.Count; i++)
            {
                int cell = c.Cells[i];
                var facing = c.Facings[i];
                int woodCost = craftRequiredBelts ? 0 : fast ? rules.FastBeltWoodCost : rules.BeltWoodCost;
                int plankCost = craftRequiredBelts ? fast ? 1 : placedInCommand % 2 == 0 ? 1 : 0 : 0;
                int gearCost = BeltCraftOn && fast ? veryFast ? rules.VeryFastBeltGears : rules.FastBeltGears : 0;
                int steelCost = veryFast ? rules.VeryFastBeltSteel : 0;
                if (!BeltStackOn && owned >= rules.BeltLimit || economy.Wood < woodCost || economy.Planks < plankCost
                    || economy.Gears < gearCost || economy.Steel < steelCost)
                {
                    if (BeltCraftOn && gearCost > 0 && economy.Gears < gearCost && faction <= 2) gearShortageFailedPlacements[faction - 1]++;
                    return;
                }
                if (cell < 0 || cell >= world.Belts.Length || (byte)facing > 3 || world.Belts[cell].FactionId != 0
                    || !world.Map.IsPassable(cell) || IsReservedBuildingCell(cell) || IsRiverCell(cell) || IsNodeCell(cell) || InsideAnyCore(cell)) continue;
                economy.Wood = checked(economy.Wood - woodCost);
                RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Belt, woodCost);
                if (plankCost > 0)
                {
                    economy.Planks = checked(economy.Planks - plankCost);
                    economy.PlanksConsumed = checked(economy.PlanksConsumed + plankCost);
                    RecordCraftTracePlanks(faction, CraftTraceOther, plankCost);
                }
                if (gearCost > 0)
                {
                    economy.Gears = checked(economy.Gears - gearCost);
                    economy.GearsConsumed = checked(economy.GearsConsumed + gearCost);
                }
                if (steelCost > 0) economy.Steel = checked(economy.Steel - steelCost);
                world.Belts[cell] = new BeltState { FactionId = faction, Facing = facing, Hp = rules.BeltHp, Held = held, Fast = fast, VeryFast = veryFast };
                if (AiReasonsOn && automaticEconomyAction && !held)
                {
                    world.Belts[cell].AiReason = aiReason.reason == AiReason.Unspecified ? AiReason.ResourceBelt : aiReason.reason;
                    world.Belts[cell].AiReasonValue = aiReason.reason == AiReason.Unspecified ? (int)c.Cells.Count : aiReason.value;
                }
                if (held) MarkLinesForBelt(faction, cell);
                owned++;
                placedInCommand++;
                InvalidateBeltCaches();
            }
        }

        /// <summary>V3-20 placement. Components are one-cell belt endpoints; underground pairs are placed atomically.</summary>
        private void PlaceBeltComponent(uint faction, EconomyCommand c, bool held)
        {
            var aiReason = TakeAiReason(automaticEconomyAction && !held);
            var rules = world.Config.Economy;
            if (!rules.BeltComponents || !IndustryOn || (byte)c.Facing > 3) return;
            if (c.Cells.Count != 0 && c.Cells.Count != 2) return;
            if (c.IsCatapult)
            {
                PlaceCatapult(faction, c, held);
                return;
            }
            if (!c.IsMerger && c.SorterKind != 0 && (byte)c.SorterKind > (byte)(ToolsOn ? ResourceKind.Tools : CharmsOn ? ResourceKind.Charms : IncenseOn ? ResourceKind.Incense : TradeGoodsOn ? ResourceKind.TradeGoods : PreservesOn ? ResourceKind.Preserves : BooksOn ? ResourceKind.Books : PlanksOn ? ResourceKind.Planks : TackOn ? ResourceKind.Tack : RationsOn ? ResourceKind.Rations : ResourceKind.BowGear)) return;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            if (c.Cells.Count == 2)
            {
                int entrance = c.Cells[0], exit = c.Cells[1];
                int woodCost = CraftRequiredOn && economy.Age >= 1 ? 0 : rules.UndergroundBeltWoodCost;
                int undergroundPlankCost = CraftRequiredOn && economy.Age >= 1 ? (rules.UndergroundBeltWoodCost + 1) / 2 : 0;
                int undergroundGearCost = BeltCraftOn ? rules.UndergroundBeltGears : 0;
                if (!BeltStackOn && owned + 2 > rules.BeltLimit || !UndergroundPairIsValid(entrance, exit, c.Facing)
                    || economy.Wood < woodCost || economy.Planks < undergroundPlankCost || economy.Gears < undergroundGearCost)
                {
                    if (BeltCraftOn && undergroundGearCost > 0 && economy.Gears < undergroundGearCost && faction <= 2) gearShortageFailedPlacements[faction - 1]++;
                    return;
                }
                world.Belts[entrance] = new BeltState { FactionId = faction, Facing = c.Facing, Hp = rules.BeltHp, Held = held,
                    Component = BeltComponentKind.UndergroundEntrance, PairCell = exit };
                world.Belts[exit] = new BeltState { FactionId = faction, Facing = c.Facing, Hp = rules.BeltHp, Held = held,
                    Component = BeltComponentKind.UndergroundExit, PairCell = entrance };
                if (AiReasonsOn && automaticEconomyAction && !held)
                {
                    world.Belts[entrance].AiReason = world.Belts[exit].AiReason = aiReason.reason;
                    world.Belts[entrance].AiReasonValue = world.Belts[exit].AiReasonValue = Math.Max(1, Math.Abs(exit - entrance));
                }
                economy.Wood = checked(economy.Wood - woodCost);
                RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Belt, woodCost);
                if (undergroundPlankCost > 0)
                {
                    economy.Planks = checked(economy.Planks - undergroundPlankCost);
                    economy.PlanksConsumed = checked(economy.PlanksConsumed + undergroundPlankCost);
                    RecordCraftTracePlanks(faction, CraftTraceOther, undergroundPlankCost);
                }
                if (undergroundGearCost > 0) { economy.Gears -= undergroundGearCost; economy.GearsConsumed = checked(economy.GearsConsumed + undergroundGearCost); }
                InvalidateBeltCaches();
                return;
            }
            int cell = c.Cell;
            if (!BeltStackOn && owned >= rules.BeltLimit) return;
            if (cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != 0 || !world.Map.IsPassable(cell) || IsReservedBuildingCell(cell)
                || IsRiverCell(cell) || IsNodeCell(cell) || InsideAnyCore(cell)) return;
            BeltComponentKind component = c.IsMerger ? BeltComponentKind.Merger : c.IsInserter ? c.InserterComponent
                : c.SorterKind == 0 ? BeltComponentKind.Splitter : BeltComponentKind.Sorter;
            if (IsInserterComponent(component) && !rules.Inserters) return;
            if (component == BeltComponentKind.Merger && !BeltCraftOn) return;
            if ((component == BeltComponentKind.FilterInserter || component == BeltComponentKind.LongInserter) && !BeltCraftOn) return;
            if (component == BeltComponentKind.FilterInserter && c.SorterKind == 0) return;
            int cost = IsInserterComponent(component) ? rules.InserterWoodCost
                : component == BeltComponentKind.Splitter ? rules.SplitterWoodCost : rules.SorterWoodCost;
            bool craftDepthInserter = CraftDepthOn && economy.Age >= 2 && IsInserterComponent(component);
            bool craftRequiredComponent = CraftRequiredOn && economy.Age >= 1
                && !IsInserterComponent(component);
            int gearCost = BeltCraftOn ? component == BeltComponentKind.Merger ? rules.MergerGears
                : component == BeltComponentKind.FilterInserter ? rules.FilterInserterGears
                : component == BeltComponentKind.LongInserter ? rules.LongInserterGears
                : component == BeltComponentKind.Inserter ? rules.InserterGears
                : component == BeltComponentKind.Splitter ? rules.SplitterGears : rules.SorterGears : 0;
            int plankCost = component == BeltComponentKind.Merger ? rules.MergerPlanks : craftRequiredComponent ? (cost + 1) / 2 : 0;
            int woodToPay = component == BeltComponentKind.Merger ? 0 : craftRequiredComponent ? 0 : cost;
            if (economy.Wood < woodToPay || economy.Planks < plankCost || economy.Gears < gearCost || economy.Fittings < (craftDepthInserter ? 1 : 0))
            {
                if (BeltCraftOn && gearCost > 0 && economy.Gears < gearCost && faction <= 2) gearShortageFailedPlacements[faction - 1]++;
                return;
            }
            world.Belts[cell] = new BeltState { FactionId = faction, Facing = c.Facing, Hp = rules.BeltHp, Held = held,
                Component = component, SorterKind = IsInserterComponent(component) ? c.SorterKind : c.SorterKind, Priority = c.BeltPriority, SplitterRightNext = false };
            if (AiReasonsOn && automaticEconomyAction && !held)
            {
                world.Belts[cell].AiReason = aiReason.reason;
                world.Belts[cell].AiReasonValue = aiReason.value;
            }
            int woodPaid = woodToPay;
            economy.Wood = checked(economy.Wood - woodPaid);
            if (craftDepthInserter)
            {
                economy.Fittings--;
                economy.FittingsConsumed = checked(economy.FittingsConsumed + 1);
                RecordCraftTraceResourceUse(faction, ResourceKind.Fittings, CraftTraceUseCost, 1);
            }
            if (gearCost > 0) { economy.Gears = checked(economy.Gears - gearCost); economy.GearsConsumed = checked(economy.GearsConsumed + gearCost); }
            RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Belt, woodPaid);
            if (plankCost > 0)
            {
                economy.Planks = checked(economy.Planks - plankCost);
                economy.PlanksConsumed = checked(economy.PlanksConsumed + plankCost);
                RecordCraftTracePlanks(faction, CraftTraceOther, plankCost);
            }
            InvalidateBeltCaches();
        }

        private void SetBeltPriority(uint faction, int cell, BeltPriority priority)
        {
            if (!BeltCraftOn || cell < 0 || cell >= world.Belts.Length || (byte)priority > (byte)BeltPriority.Right) return;
            ref var belt = ref world.Belts[cell];
            if (belt.FactionId != faction || (belt.Component != BeltComponentKind.Splitter && belt.Component != BeltComponentKind.Merger)) return;
            belt.Priority = priority;
        }

        private void PlaceCatapult(uint faction, EconomyCommand c, bool held)
        {
            if (!BeltCraftOn || c.Cells == null || c.Cells.Count != 2 || c.Facings == null || c.Facings.Count != 2
                || world.Economies[faction - 1].Age < world.Config.Economy.CatapultMinAge) return;
            int launcher = c.Cells[0], receiver = c.Cells[1];
            if (!CatapultPairIsValid(launcher, receiver)) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int steel = CraftDepthOn ? 0 : rules.CatapultSteel;
            if (economy.Gears < rules.CatapultGears + rules.CatapultReceiverGears
                || economy.Planks < rules.CatapultPlanks + rules.CatapultReceiverPlanks
                || economy.Fittings < (CraftDepthOn ? rules.CatapultFittings : 0)
                || economy.Rope < (CraftDepthOn ? rules.CatapultRope : 0) || economy.Steel < steel) return;
            world.Belts[launcher] = new BeltState { FactionId = faction, Facing = c.Facings[0], Hp = rules.BeltHp, Held = held,
                Component = BeltComponentKind.Catapult, PairCell = receiver };
            world.Belts[receiver] = new BeltState { FactionId = faction, Facing = c.Facings[1], Hp = rules.BeltHp, Held = held,
                Component = BeltComponentKind.CatapultReceiver, PairCell = launcher };
            economy.Gears = checked(economy.Gears - rules.CatapultGears - rules.CatapultReceiverGears);
            economy.GearsConsumed = checked(economy.GearsConsumed + rules.CatapultGears + rules.CatapultReceiverGears);
            economy.Planks = checked(economy.Planks - rules.CatapultPlanks - rules.CatapultReceiverPlanks);
            economy.PlanksConsumed = checked(economy.PlanksConsumed + rules.CatapultPlanks + rules.CatapultReceiverPlanks);
            RecordCraftTracePlanks(faction, CraftTraceOther, rules.CatapultPlanks + rules.CatapultReceiverPlanks);
            if (CraftDepthOn)
            {
                economy.Fittings = checked(economy.Fittings - rules.CatapultFittings);
                economy.Rope = checked(economy.Rope - rules.CatapultRope);
                economy.FittingsConsumed = checked(economy.FittingsConsumed + rules.CatapultFittings);
                economy.RopeConsumed = checked(economy.RopeConsumed + rules.CatapultRope);
            }
            else economy.Steel = checked(economy.Steel - steel);
            AddCatapultState(faction, launcher, receiver);
            InvalidateBeltCaches();
        }

        private bool CatapultPairIsValid(int launcher, int receiver)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            if (launcher < 0 || receiver < 0 || launcher >= cells || receiver >= cells || launcher == receiver
                || world.Belts[launcher].FactionId != 0 || world.Belts[receiver].FactionId != 0
                || !world.Map.IsPassable(launcher) || !world.Map.IsPassable(receiver)
                || IsReservedBuildingCell(launcher) || IsReservedBuildingCell(receiver)
                || IsRiverCell(launcher) || IsRiverCell(receiver) || IsNodeCell(launcher) || IsNodeCell(receiver)
                || InsideAnyCore(launcher) || InsideAnyCore(receiver)) return false;
            int width = world.Config.Map.WidthCells;
            int dx = Math.Abs(receiver % width - launcher % width), dz = Math.Abs(receiver / width - launcher / width);
            return Math.Max(dx, dz) <= world.Config.Economy.CatapultRangeCells;
        }

        private void AddCatapultState(uint faction, int launcher, int receiver)
        {
            var states = world.Catapults;
            for (int i = 0; i < states.Length; i++) if (!states[i].Active)
            {
                states[i] = NewCatapultState(faction, launcher, receiver); return;
            }
            var expanded = new CatapultState[states.Length + 1];
            System.Array.Copy(states, expanded, states.Length);
            expanded[states.Length] = NewCatapultState(faction, launcher, receiver);
            world.Catapults = expanded;
        }

        private CatapultState NewCatapultState(uint faction, int launcher, int receiver)
            => new CatapultState { Active = true, FactionId = faction, LauncherCell = launcher, ReceiverCell = receiver,
                FirstLoadTick = -1, LastThrowTick = -world.Config.Economy.CatapultIntervalTicks, Shots = new CatapultShotState[4] };

        private void RemoveCatapultState(int launcher, int receiver)
        {
            for (int i = 0; i < world.Catapults.Length; i++)
                if (world.Catapults[i].Active && world.Catapults[i].LauncherCell == launcher && world.Catapults[i].ReceiverCell == receiver)
                { var state = world.Catapults[i]; state.Active = false; state.BufferedCount = 0; state.ReceiverCount = 0; state.BufferKind = 0; state.ReceiverKind = 0; state.Shots = new CatapultShotState[4]; world.Catapults[i] = state; return; }
        }

        private bool UndergroundPairIsValid(int entrance, int exit, Facing facing)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            if (entrance < 0 || exit < 0 || entrance >= cells || exit >= cells || entrance == exit
                || world.Belts[entrance].FactionId != 0 || world.Belts[exit].FactionId != 0
                || !world.Map.IsPassable(entrance) || !world.Map.IsPassable(exit)
                || IsReservedBuildingCell(entrance) || IsReservedBuildingCell(exit)
                || IsRiverCell(entrance) || IsRiverCell(exit) || IsNodeCell(entrance) || IsNodeCell(exit)
                || InsideAnyCore(entrance) || InsideAnyCore(exit)) return false;
            int width = world.Config.Map.WidthCells, ex = entrance % width, ez = entrance / width;
            int xx = exit % width, xz = exit / width, distance;
            if (facing == Facing.North && xx == ex && xz > ez) distance = xz - ez;
            else if (facing == Facing.East && xz == ez && xx > ex) distance = xx - ex;
            else if (facing == Facing.South && xx == ex && xz < ez) distance = ez - xz;
            else if (facing == Facing.West && xz == ez && xx < ex) distance = ex - xx;
            else return false;
            return distance <= world.Config.Economy.UndergroundBeltMaxLength;
        }

        /// <summary>RemoveBelt: removes an own belt and, with EasyRebuild, refunds its placement wood and item.</summary>
        private void RemoveBelt(uint faction, int cell)
        {
            if (!IndustryOn || cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != faction) return;
            var belt = world.Belts[cell];
            int pair = belt.PairCell;
            if (belt.Component == BeltComponentKind.CatapultReceiver && pair >= 0 && pair < world.Belts.Length
                && world.Belts[pair].FactionId == faction && world.Belts[pair].Component == BeltComponentKind.Catapult
                && world.Belts[pair].PairCell == cell)
            {
                int receiver = cell;
                cell = pair;
                pair = receiver;
                belt = world.Belts[cell];
            }
            bool catapult = belt.Component == BeltComponentKind.Catapult && pair >= 0 && pair < world.Belts.Length
                && world.Belts[pair].FactionId == faction && world.Belts[pair].Component == BeltComponentKind.CatapultReceiver
                && world.Belts[pair].PairCell == cell;
            if (catapult)
            {
                MarkLinesForBelt(faction, cell); MarkLinesForBelt(faction, pair);
                RemoveCatapultState(cell, pair);
                if (world.Config.Economy.EasyRebuild)
                {
                    var r = world.Config.Economy;
                    AddStock(faction, ResourceKind.Gears, r.CatapultGears + r.CatapultReceiverGears);
                    AddStock(faction, ResourceKind.Planks, r.CatapultPlanks + r.CatapultReceiverPlanks);
                    if (CraftDepthOn) { AddStock(faction, ResourceKind.Fittings, r.CatapultFittings); AddStock(faction, ResourceKind.Rope, r.CatapultRope); }
                    else AddStock(faction, ResourceKind.Steel, r.CatapultSteel);
                }
                world.Belts[cell] = default; world.Belts[pair] = default; InvalidateBeltCaches(); return;
            }
            // Removing both ends of an underground pair is part of EasyRebuild; without it one end stays, as before.
            bool paired = world.Config.Economy.EasyRebuild && pair >= 0 && pair < world.Belts.Length && world.Belts[pair].FactionId == faction
                && world.Belts[pair].PairCell == cell
                && (belt.Component == BeltComponentKind.UndergroundEntrance && world.Belts[pair].Component == BeltComponentKind.UndergroundExit
                    || belt.Component == BeltComponentKind.UndergroundExit && world.Belts[pair].Component == BeltComponentKind.UndergroundEntrance);
            if (paired && belt.Component == BeltComponentKind.UndergroundExit)
            {
                int entrance = pair;
                pair = cell;
                cell = entrance;
                belt = world.Belts[cell];
            }
            MarkLinesForBelt(faction, cell);
            if (paired) MarkLinesForBelt(faction, pair);
            if (world.Config.Economy.EasyRebuild)
            {
                if (belt.Item != 0) AddStock(faction, belt.Item, BeltItemCount(belt));
                bool craftRequiredBelts = CraftRequiredOn && world.Economies[faction - 1].Age >= 1;
                int refundWood = paired ? world.Config.Economy.UndergroundBeltWoodCost
                    : belt.Component == BeltComponentKind.Merger ? 0
                    : belt.Component == BeltComponentKind.Splitter ? world.Config.Economy.SplitterWoodCost
                    : belt.Component == BeltComponentKind.Sorter ? world.Config.Economy.SorterWoodCost
                    : belt.Fast ? world.Config.Economy.FastBeltWoodCost : world.Config.Economy.BeltWoodCost;
                // A plain belt paid in planks is not refunded; the inserter still pays and refunds wood.
                if (!craftRequiredBelts || IsInserterComponent(belt.Component))
                    AddStock(faction, ResourceKind.Wood, refundWood);
                if (craftRequiredBelts && (paired || belt.Fast || belt.Component == BeltComponentKind.Splitter || belt.Component == BeltComponentKind.Sorter || belt.Component == BeltComponentKind.Merger))
                {
                    int refundPlanks = paired ? (world.Config.Economy.UndergroundBeltWoodCost + 1) / 2
                        : belt.Component == BeltComponentKind.Merger ? world.Config.Economy.MergerPlanks
                        : belt.Fast ? 1 : (belt.Component == BeltComponentKind.Splitter ? (world.Config.Economy.SplitterWoodCost + 1) / 2 : (world.Config.Economy.SorterWoodCost + 1) / 2);
                    AddStock(faction, ResourceKind.Planks, refundPlanks);
                }
                if (BeltCraftOn)
                {
                    int gearRefund = paired ? world.Config.Economy.UndergroundBeltGears
                        : belt.VeryFast ? world.Config.Economy.VeryFastBeltGears
                        : belt.Fast ? world.Config.Economy.FastBeltGears
                        : belt.Component == BeltComponentKind.Merger ? world.Config.Economy.MergerGears
                        : belt.Component == BeltComponentKind.Splitter ? world.Config.Economy.SplitterGears
                        : belt.Component == BeltComponentKind.Sorter ? world.Config.Economy.SorterGears
                        : belt.Component == BeltComponentKind.Inserter ? world.Config.Economy.InserterGears
                        : belt.Component == BeltComponentKind.FilterInserter ? world.Config.Economy.FilterInserterGears
                        : belt.Component == BeltComponentKind.LongInserter ? world.Config.Economy.LongInserterGears : 0;
                    if (gearRefund > 0) AddStock(faction, ResourceKind.Gears, gearRefund);
                    if (belt.VeryFast) AddStock(faction, ResourceKind.Steel, world.Config.Economy.VeryFastBeltSteel);
                }
                if (paired && world.Belts[pair].Item != 0) AddStock(faction, world.Belts[pair].Item, BeltItemCount(world.Belts[pair]));
            }
            world.Belts[cell] = default;
            if (paired) world.Belts[pair] = default;
            InvalidateBeltCaches();
        }

        private bool IsNodeCell(int cell)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            if (cell < 0 || cell >= cells) return IsNodeCellScan(cell);
            if (nodeCells == null)
            {
                var marked = new bool[cells];
                foreach (var node in world.Nodes)
                {
                    int nodeCell = world.Map.Cell(node.Definition.Position);
                    if (nodeCell >= 0 && nodeCell < cells) marked[nodeCell] = true;
                }
                nodeCells = marked;
            }
            return nodeCells[cell];
        }

        private bool IsNodeCellScan(int cell)
        {
            foreach (var node in world.Nodes) if (world.Map.Cell(node.Definition.Position) == cell) return true;
            return false;
        }

        private bool TryFeedBeltDropOffStock(int cell, uint faction, ResourceKind item)
        {
            if (!BeltDropOffStockDestination(cell, faction, item)) return false;
            AddStock(faction, item, 1);
            RecordLargeBeltDelivery(faction, item, 1);
            return true;
        }

        private bool BeltDropOffDestination(int cell, uint faction, ResourceKind item)
        {
            if (cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != 0) return false;
            if (BeltDropOffStockDestination(cell, faction, item)) return true;
            return BeltCanFeedBuilding(cell, faction, item);
        }

        private bool BeltDropOffStockDestination(int cell, uint faction, ResourceKind item)
        {
            if (cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != 0) return false;
            if (FeedsOwnCore(cell, faction)) return true;
            int width = world.Config.Map.WidthCells, x = cell % width, z = cell / width;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || b.FactionId != faction) continue;
                int size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
                if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size) continue;
                if (b.Kind == BuildingKind.Town || b.Kind == BuildingKind.DropSite) return true;
                // A belt-dropoff storage deposits raw resources directly into the faction stock. Its capacity and
                // current contents must not affect whether the belt route is reachable.
                if (b.Kind == BuildingKind.Storage) return IsRawResource(item);
                return false;
            }
            return false;
        }

        private int FindBeltDropOffCell(VillagerState villager)
        {
            if (!BeltDropOffOn) return -1;
            int resource = (int)villager.CarryKind;
            if (resource <= 0 || resource >= 33) return -1;
            var reachable = BeltDropOffReachable(resource, villager.FactionId);
            int origin = world.Map.Cell(villager.Position);
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int range = Math.Max(0, world.Config.Economy.BeltDropOffRangeCells);
            int best = -1, bestDistance = 0;
            int ox = origin % width, oz = origin / width;
            for (int dz = -range; dz <= range; dz++)
                for (int dx = -range; dx <= range; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) > range) continue;
                    int x = ox + dx, z = oz + dz;
                    if (x < 0 || z < 0 || x >= width || z >= height) continue;
                    int cell = z * width + x;
                    var belt = world.Belts[cell];
                    if (!reachable[cell] || !BeltCanWaitForLoad(belt, villager.CarryKind)
                        || belt.FactionId != villager.FactionId
                        || IsSpecialComponent(belt.Component) || belt.Component == BeltComponentKind.UndergroundExit
                        || belt.Component == BeltComponentKind.Splitter) continue;
                    int distance = dx * dx + dz * dz;
                    if (best < 0 || distance < bestDistance || distance == bestDistance && cell < best)
                    { best = cell; bestDistance = distance; }
                }
            return best;
        }

        private bool[] BeltDropOffReachable(int resource, uint faction)
        {
            EnsureBeltDropOffReachabilityWorkspace();
            int index = checked((int)((faction - 1) * 33 + (uint)resource));
            if (beltDropOffReachability[index] == null) beltDropOffReachability[index] = new bool[world.Belts.Length];
            if (beltDropOffReachabilityValid[index]) return beltDropOffReachability[index];
            var reachable = beltDropOffReachability[index];
            Array.Clear(reachable, 0, reachable.Length);
            EnsureBeltProcessingOrder();
            int limit = BeltStackOn ? world.Belts.Length : Math.Max(0, world.Config.Economy.BeltLimit);
            for (int orderIndex = 0; orderIndex < beltProcessingOrderCount; orderIndex++)
            {
                int start = world.BeltOrder[orderIndex];
                var source = world.Belts[start];
                if (source.FactionId != faction || IsSpecialComponent(source.Component)
                    || source.Component == BeltComponentKind.UndergroundExit || source.Component == BeltComponentKind.Splitter
                    || limit == 0) continue;
                if (++beltDropOffSeenStamp == 0)
                {
                    Array.Clear(beltDropOffSeen, 0, beltDropOffSeen.Length);
                    beltDropOffSeenStamp = 1;
                }
                int stamp = beltDropOffSeenStamp;
                int head = 0, tail = 0, visited = 0;
                beltDropOffSeen[start] = stamp; beltDropOffQueue[tail++] = start;
                while (head < tail && visited < limit && !reachable[start])
                {
                    int cell = beltDropOffQueue[head++]; visited++;
                    var belt = world.Belts[cell];
                    if (IsSpecialComponent(belt.Component)) continue;
                    int first = -1, second = -1;
                    if (belt.Component == BeltComponentKind.Splitter)
                    {
                        first = BeltNext(cell, belt.Facing);
                        second = BeltNext(cell, TurnLeft(belt.Facing));
                    }
                    else if (belt.Component == BeltComponentKind.UndergroundEntrance) first = belt.PairCell;
                    else first = BeltOutputCellForItem(cell, ref belt, (ResourceKind)resource);
                    if (Visit(first) || Visit(second)) break;
                }

                bool Visit(int next)
                {
                    if (next < 0 || next >= world.Belts.Length) return false;
                    var target = world.Belts[next];
                    if (target.FactionId == 0)
                    {
                        if (BeltDropOffDestination(next, faction, (ResourceKind)resource))
                        {
                            reachable[start] = true;
                            return true;
                        }
                        return false;
                    }
                    if (target.FactionId != faction || IsSpecialComponent(target.Component)) return false;
                    if (beltDropOffSeen[next] != stamp) { beltDropOffSeen[next] = stamp; beltDropOffQueue[tail++] = next; }
                    return false;
                }
            }
            beltDropOffReachabilityValid[index] = true;
            return reachable;
        }

        private void EnsureBeltDropOffReachabilityWorkspace()
        {
            if (beltDropOffReachability == null)
            {
                beltDropOffReachability = new bool[2 * 33][];
                beltDropOffReachabilityValid = new bool[2 * 33];
                beltDropOffSeen = new int[world.Belts.Length];
                beltDropOffQueue = new int[world.Belts.Length];
            }
        }
    }
}
