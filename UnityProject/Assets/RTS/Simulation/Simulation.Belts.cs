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

        /// <summary>Economy step, before gathering (11.3): downstream belts first, so a full line moves without gaps.</summary>
        private void AdvanceBelts()
        {
            if (!IndustryOn) return;
            if (world.BeltOrder == null) world.BeltOrder = BeltProcessingOrder();
            foreach (int cell in world.BeltOrder)
            {
                ref var belt = ref world.Belts[cell];
                if (belt.Item == 0) continue;
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
                if (target.FactionId == belt.FactionId)
                {
                    if (target.Item != 0) continue; // jammed until the cell ahead moves
                    target.Item = belt.Item;
                    target.Progress = 0;
                    belt.Item = 0;
                    belt.Progress = 0;
                    if (belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                }
                else if (target.FactionId == 0 && FeedsOwnCore(next, belt.FactionId))
                {
                    AddStock(belt.FactionId, belt.Item, 1);
                    belt.Item = 0;
                    belt.Progress = 0;
                    if (belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                }
                else if (target.FactionId == 0 && TryFeedBuilding(next, belt.FactionId, belt.Item))
                {
                    belt.Item = 0;
                    belt.Progress = 0;
                    if (belt.Component == BeltComponentKind.Splitter) belt.SplitterRightNext = !belt.SplitterRightNext;
                }
                // Anything else - bare ground, an enemy belt, a building that does not take it - holds the item where it is.
            }
        }

        // A fast belt is never slower than a normal one, even where a scenario speeds normal belts up (test fixtures do).
        private int BeltTicksFor(BeltState belt)
            => belt.Fast && world.Config.Economy.BeltComponents
                ? Math.Min(world.Config.Economy.FastBeltTicksPerCell, world.Config.Economy.BeltTicksPerCell)
                : world.Config.Economy.BeltTicksPerCell;

        private int BeltOutputCell(int cell, ref BeltState belt)
        {
            if (!world.Config.Economy.BeltComponents || belt.Component == BeltComponentKind.None)
                return BeltNext(cell, belt.Facing);
            if (belt.Component == BeltComponentKind.UndergroundEntrance) return belt.PairCell;
            Facing direction = belt.Facing;
            if (belt.Component == BeltComponentKind.Splitter)
            {
                // The forward exit is the first choice; the left exit is the deterministic fallback.
                if (belt.SplitterRightNext) direction = TurnLeft(belt.Facing);
                int preferred = BeltNext(cell, direction);
                if (BeltCanReceive(preferred, belt.FactionId)) return preferred;
                int alternate = BeltNext(cell, direction == belt.Facing ? TurnLeft(belt.Facing) : belt.Facing);
                return BeltCanReceive(alternate, belt.FactionId) ? alternate : preferred;
            }
            if (belt.Component == BeltComponentKind.Sorter && belt.Item == belt.SorterKind)
                direction = TurnLeft(belt.Facing);
            return BeltNext(cell, direction);
        }

        private bool BeltCanReceive(int cell, uint faction)
            => cell >= 0 && cell < world.Belts.Length && world.Belts[cell].FactionId == faction && world.Belts[cell].Item == 0;

        private static Facing TurnLeft(Facing facing)
            => (Facing)(((int)facing + 3) % 4);

        /// <summary>
        /// Belts by distance to the end of their line, then cell id (11.3). A line that runs into a loop has no end and
        /// goes last. Depends on the belts alone, so it is a cache that any change to a belt clears.
        /// </summary>
        private int[] BeltProcessingOrder()
        {
            int cells = world.Belts.Length, count = 0;
            var distance = new int[cells];
            var state = new byte[cells]; // 0 unvisited, 1 on the current walk, 2 done
            var walk = new int[cells];
            for (int start = 0; start < cells; start++)
            {
                if (world.Belts[start].FactionId == 0 || state[start] == 2) continue;
                int length = 0, cell = start, end;
                while (true)
                {
                    if (state[cell] == 1) { end = int.MaxValue; break; }                 // closed a loop
                    if (state[cell] == 2) { end = distance[cell]; break; }               // joined a known line
                    state[cell] = 1;
                    walk[length++] = cell;
                    int next = BeltPrimaryNext(cell, world.Belts[cell]);
                    if (next < 0 || next >= cells || world.Belts[next].FactionId != world.Belts[cell].FactionId) { end = -1; break; } // this cell is the end
                    cell = next;
                }
                for (int i = length - 1; i >= 0; i--)
                {
                    end = end == int.MaxValue ? int.MaxValue : end + 1;
                    distance[walk[i]] = end;
                    state[walk[i]] = 2;
                }
            }
            for (int i = 0; i < cells; i++) if (world.Belts[i].FactionId != 0) count++;
            var order = new int[count];
            count = 0;
            for (int i = 0; i < cells; i++) if (world.Belts[i].FactionId != 0) order[count++] = i;
            Array.Sort(order, (a, b) => { int c = distance[a].CompareTo(distance[b]); return c != 0 ? c : a.CompareTo(b); });
            return order;
        }

        private int BeltPrimaryNext(int cell, BeltState belt)
        {
            if (belt.Component == BeltComponentKind.UndergroundEntrance) return belt.PairCell;
            return BeltNext(cell, belt.Facing);
        }

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
            return core.Hp > 0 && InRange(world.Map.Center(cell), core.Definition.Position, world.Config.Rules.CoreRadius);
        }

        private bool InsideAnyCore(int cell)
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
                case ResourceKind.Metal: economy.Metal = checked(economy.Metal + amount); break;
                case ResourceKind.Stone: economy.Stone = checked(economy.Stone + amount); break;
                case ResourceKind.Gems: economy.Gems = checked(economy.Gems + amount); break;
                case ResourceKind.Charcoal: economy.Charcoal = checked(economy.Charcoal + amount); break;
                case ResourceKind.Steel: economy.Steel = checked(economy.Steel + amount); break;
                case ResourceKind.BowGear: economy.BowGear = checked(economy.BowGear + amount); break;
                case ResourceKind.Gold: economy.Gold = checked(economy.Gold + amount); break;
            }
        }

        /// <summary>
        /// PlaceBelt (14): each cell of the run is checked on its own, in order - on the map, open ground, no resource
        /// point, outside every core, no belt yet, under the faction's limit, wood in stock. A cell that fails is skipped.
        /// </summary>
        private void PlaceBelts(uint faction, EconomyCommand c, bool held)
        {
            if (!IndustryOn) return;
            var rules = world.Config.Economy;
            bool fast = rules.BeltComponents && c.FastBelt && world.Economies[faction - 1].Age >= rules.FastBeltAge;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            for (int i = 0; i < c.Cells.Count; i++)
            {
                int cell = c.Cells[i];
                var facing = c.Facings[i];
                int cost = fast ? rules.FastBeltWoodCost : rules.BeltWoodCost;
                if (owned >= rules.BeltLimit || economy.Wood < cost) return;
                if (cell < 0 || cell >= world.Belts.Length || (byte)facing > 3 || world.Belts[cell].FactionId != 0
                    || !world.Map.IsPassable(cell) || IsRiverCell(cell) || IsNodeCell(cell) || InsideAnyCore(cell)) continue;
                economy.Wood = checked(economy.Wood - cost);
                world.Belts[cell] = new BeltState { FactionId = faction, Facing = facing, Hp = rules.BeltHp, Held = held, Fast = fast };
                if (held) MarkLinesForBelt(faction, cell);
                owned++;
                world.BeltOrder = null;
            }
        }

        /// <summary>V3-20 placement. Components are one-cell belt endpoints; underground pairs are placed atomically.</summary>
        private void PlaceBeltComponent(uint faction, EconomyCommand c, bool held)
        {
            var rules = world.Config.Economy;
            if (!rules.BeltComponents || !IndustryOn || (byte)c.Facing > 3) return;
            if (c.Cells.Count != 0 && c.Cells.Count != 2) return;
            if (c.SorterKind != 0 && (byte)c.SorterKind > (byte)ResourceKind.BowGear) return;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            if (c.Cells.Count == 2)
            {
                int entrance = c.Cells[0], exit = c.Cells[1];
                if (owned + 2 > rules.BeltLimit || !UndergroundPairIsValid(entrance, exit, c.Facing) || economy.Wood < rules.UndergroundBeltWoodCost) return;
                world.Belts[entrance] = new BeltState { FactionId = faction, Facing = c.Facing, Hp = rules.BeltHp, Held = held,
                    Component = BeltComponentKind.UndergroundEntrance, PairCell = exit };
                world.Belts[exit] = new BeltState { FactionId = faction, Facing = c.Facing, Hp = rules.BeltHp, Held = held,
                    Component = BeltComponentKind.UndergroundExit, PairCell = entrance };
                economy.Wood = checked(economy.Wood - rules.UndergroundBeltWoodCost);
                world.BeltOrder = null;
                return;
            }
            int cell = c.Cell;
            if (owned >= rules.BeltLimit) return;
            if (cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != 0 || !world.Map.IsPassable(cell)
                || IsRiverCell(cell) || IsNodeCell(cell) || InsideAnyCore(cell)) return;
            BeltComponentKind component = c.SorterKind == 0 ? BeltComponentKind.Splitter : BeltComponentKind.Sorter;
            int cost = component == BeltComponentKind.Splitter ? rules.SplitterWoodCost : rules.SorterWoodCost;
            if (economy.Wood < cost) return;
            world.Belts[cell] = new BeltState { FactionId = faction, Facing = c.Facing, Hp = rules.BeltHp, Held = held,
                Component = component, SorterKind = c.SorterKind, SplitterRightNext = false };
            economy.Wood = checked(economy.Wood - cost);
            world.BeltOrder = null;
        }

        private bool UndergroundPairIsValid(int entrance, int exit, Facing facing)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            if (entrance < 0 || exit < 0 || entrance >= cells || exit >= cells || entrance == exit
                || world.Belts[entrance].FactionId != 0 || world.Belts[exit].FactionId != 0
                || !world.Map.IsPassable(entrance) || !world.Map.IsPassable(exit)
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

        /// <summary>RemoveBelt: only the own belt; the item on it is lost and the wood is not returned.</summary>
        private void RemoveBelt(uint faction, int cell)
        {
            if (!IndustryOn || cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != faction) return;
            MarkLinesForBelt(faction, cell);
            world.Belts[cell] = default;
            world.BeltOrder = null;
        }

        private bool IsNodeCell(int cell)
        {
            foreach (var node in world.Nodes) if (world.Map.Cell(node.Definition.Position) == cell) return true;
            return false;
        }
    }
}
