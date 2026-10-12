using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Pure input rules for selecting villagers and translating map targets to economy orders.</summary>
    public static class VillagerControlRules
    {
        public readonly struct ScreenPoint
        {
            public uint Id { get; }
            public bool IsOwn { get; }
            public bool IsSoldier { get; }
            public float X { get; }
            public float Y { get; }

            public ScreenPoint(uint id, bool isOwn, bool isSoldier, float x, float y)
            {
                Id = id;
                IsOwn = isOwn;
                IsSoldier = isSoldier;
                X = x;
                Y = y;
            }
        }

        public readonly struct ScreenRect
        {
            public float XMin { get; }
            public float YMin { get; }
            public float XMax { get; }
            public float YMax { get; }

            public ScreenRect(float xMin, float yMin, float xMax, float yMax)
            {
                XMin = Math.Min(xMin, xMax);
                YMin = Math.Min(yMin, yMax);
                XMax = Math.Max(xMin, xMax);
                YMax = Math.Max(yMin, yMax);
            }

            public bool Contains(float x, float y)
            {
                return x >= XMin && x <= XMax && y >= YMin && y <= YMax;
            }
        }

        public enum TargetKind : byte
        {
            Ground = 0,
            ResourceNode = 1,
            Construction = 2,
            HaulSource = 3,
            Market = 4,
            Caravanserai = 5,
            OtherBuilding = 6
        }

        public readonly struct Target
        {
            public TargetKind Kind { get; }
            public uint Id { get; }
            public uint SecondaryId { get; }

            public Target(TargetKind kind, uint id)
                : this(kind, id, 0)
            {
            }

            public Target(TargetKind kind, uint id, uint secondaryId)
            {
                Kind = kind;
                Id = id;
                SecondaryId = secondaryId;
            }
        }

        public enum OrderKind : byte
        {
            None = 0,
            Gather = 1,
            Build = 2,
            Haul = 3,
            TradeRoute = 4,
            CaravanRoute = 5
        }

        /// <summary>Shift-click toggles one ID; an ordinary click replaces the current selection.</summary>
        public static List<uint> ApplyClick(IReadOnlyList<uint> current, uint id, bool additive)
        {
            var result = new List<uint>();
            if (id == 0) return result;
            if (!additive)
            {
                result.Add(id);
                return result;
            }

            bool removed = false;
            for (int i = 0; i < current.Count; i++)
            {
                uint existing = current[i];
                if (existing == id) { removed = true; continue; }
                if (existing != 0) result.Add(existing);
            }
            if (!removed) result.Add(id);
            return result;
        }

        public static List<uint> PruneMissing(IReadOnlyList<uint> selected, IReadOnlyCollection<uint> existing)
        {
            var result = new List<uint>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] == 0) continue;
                bool found = false;
                foreach (uint id in existing) if (id == selected[i]) { found = true; break; }
                if (found) result.Add(selected[i]);
            }
            return result;
        }

        /// <summary>
        /// A box containing an own soldier remains an army selection. Only when it contains no own soldier are own
        /// villagers returned.
        /// </summary>
        public static List<uint> SelectBox(IReadOnlyList<ScreenPoint> points, ScreenRect box)
        {
            var result = new List<uint>();
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point.IsOwn && point.IsSoldier && box.Contains(point.X, point.Y)) return result;
            }
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point.IsOwn && !point.IsSoldier && box.Contains(point.X, point.Y) && point.Id != 0)
                    result.Add(point.Id);
            }
            result.Sort();
            return result;
        }

        public static OrderKind ResolveOrder(Target target)
        {
            switch (target.Kind)
            {
                case TargetKind.ResourceNode: return OrderKind.Gather;
                case TargetKind.Construction: return OrderKind.Build;
                case TargetKind.HaulSource: return OrderKind.Haul;
                case TargetKind.Market: return OrderKind.TradeRoute;
                case TargetKind.Caravanserai: return OrderKind.CaravanRoute;
                default: return OrderKind.None;
            }
        }

        public static EconomyCommandKind ResolveCommandKind(Target target)
        {
            switch (target.Kind)
            {
                case TargetKind.ResourceNode:
                case TargetKind.Construction:
                case TargetKind.HaulSource: return EconomyCommandKind.AssignVillagers;
                case TargetKind.Market: return EconomyCommandKind.TradeRoute;
                case TargetKind.Caravanserai: return EconomyCommandKind.CaravanRoute;
                default: return (EconomyCommandKind)0;
            }
        }

        public static bool IsHaulSource(BuildingKind kind)
        {
            return kind == BuildingKind.Mine || kind == BuildingKind.LumberCamp || kind == BuildingKind.Quarry
                || kind == BuildingKind.Smelter || kind == BuildingKind.Fletcher || kind == BuildingKind.Farm
                || kind == BuildingKind.CharcoalKiln || kind == BuildingKind.Storage;
        }

        public static bool CanUseCaravanserai(bool complete, uint marketId, uint outpostId)
        {
            return complete && marketId != 0 && outpostId != 0;
        }
    }
}
