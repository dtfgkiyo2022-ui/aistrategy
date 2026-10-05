using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Tactics
{
    public sealed class TacticRejectedCommand
    {
        public int Index { get; internal set; }
        public string Type { get; internal set; }
        public string Reason { get; internal set; }
        public override string ToString() => Index + ":" + (Type ?? "?") + ":" + Reason;
    }

    public sealed class TacticCommandResult
    {
        public IReadOnlyList<PolicyOrder> Policies { get; internal set; } = Array.Empty<PolicyOrder>();
        public IReadOnlyList<PolicyOrder> PolicyOrders => Policies;
        public IReadOnlyList<EconomyCommand> EconomyCommands { get; internal set; } = Array.Empty<EconomyCommand>();
        public IReadOnlyList<TacticRejectedCommand> Rejected { get; internal set; } = Array.Empty<TacticRejectedCommand>();
        public string GlobalPolicy { get; internal set; }
        public bool IsMalformed { get; internal set; }
        public string Error { get; internal set; }
        public bool HasCommands => Policies.Count != 0 || EconomyCommands.Count != 0 || !string.IsNullOrEmpty(GlobalPolicy);
    }

    /// <summary>Validates tactic version 1 output without exposing world state beyond the supplied frame.</summary>
    public static class TacticCommandReader
    {
        public static TacticCommandResult Read(string json, FactionFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            try
            {
                var root = TacticJson.Object(TacticJson.Parse(json), "root");
                if (TacticJson.Integer(root, "version", 0, true) != TacticViewWriter.Version) throw new FormatException("Unsupported tactic command version.");
                var commands = TacticJson.Array(root.TryGetValue("commands", out var raw) ? raw : null, "commands");
                var policies = new List<PolicyOrder>(); var economy = new List<EconomyCommand>(); var rejected = new List<TacticRejectedCommand>();
                string global = null;
                for (int i = 0; i < commands.Count; i++)
                {
                    string type = "?";
                    try
                    {
                        var command = TacticJson.Object(commands[i], "command"); type = TacticJson.String(command, "type", true);
                        if (Equals(type, "policy")) policies.Add(ParsePolicy(command, frame, i));
                        else if (Equals(type, "economy")) economy.Add(ParseEconomy(command, frame, i));
                        else if (Equals(type, "global") || Equals(type, "doctrine"))
                        {
                            if (global != null) throw new InvalidOperationException("全体方針は1つまでです。");
                            string value = TacticJson.String(command, type.Equals("doctrine", StringComparison.OrdinalIgnoreCase) ? "preset" : "policy", true);
                            if (value != "none" && value != "maintain" && value != "concentrate") throw new InvalidOperationException("全体方針が不明です。");
                            global = value;
                        }
                        else throw new InvalidOperationException("知らない命令の種類です。");
                    }
                    catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is OverflowException || e is ArgumentException)
                    { rejected.Add(new TacticRejectedCommand { Index = i, Type = type, Reason = e.Message }); }
                }
                return new TacticCommandResult { Policies = policies.AsReadOnly(), EconomyCommands = economy.AsReadOnly(), Rejected = rejected.AsReadOnly(), GlobalPolicy = global };
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is OverflowException || e is ArgumentException)
            { return new TacticCommandResult { IsMalformed = true, Error = e.Message, Rejected = Array.Empty<TacticRejectedCommand>() }; }
        }

        private static PolicyOrder ParsePolicy(Dictionary<string, object> c, FactionFrame frame, int index)
        {
            if (!EnumValue(TacticJson.String(c, "kind", true), out PolicyKind kind)) throw new InvalidOperationException("対応していない方針です。");
            var targetObject = c.TryGetValue("target", out var target) ? TacticJson.Object(target, "target") : TacticJson.Object(c.TryGetValue("scope", out var scope) ? scope : null, "target");
            ScopeKey targetScope = Scope(frame, targetObject);
            if (!ScopeAllowed(kind, targetScope.Kind)) throw new InvalidOperationException("この範囲では出せない方針です。");
            PolicyGoal goal = default(PolicyGoal);
            if (c.TryGetValue("goal", out var rawGoal) && rawGoal != null) goal = Goal(frame, TacticJson.Object(rawGoal, "goal"));
            bool needsGoal = kind == PolicyKind.Focus || kind == PolicyKind.Defend || kind == PolicyKind.Scout;
            if (needsGoal && goal.Kind == GoalKind.None) throw new InvalidOperationException("この方針には目標が必要です。");
            if ((kind == PolicyKind.Defend || kind == PolicyKind.Retreat) && goal.Kind != GoalKind.None && !OwnObjective(frame, goal)) throw new InvalidOperationException("自分が観測している目標だけ指定できます。");
            int priority = checked((int)TacticJson.Integer(c, "priority", 100)); if (priority < 0 || priority > 100) throw new InvalidOperationException("優先度は0から100です。");
            int loss = checked((int)TacticJson.Integer(c, "allowedLossPermille", TacticJson.Integer(c, "lossPermille", 1000))); if (loss < 0 || loss > 1000) throw new InvalidOperationException("損害の予算は0から1000です。");
            int reserve = checked((int)TacticJson.Integer(c, "reservePermille", 0)); if (reserve < 0 || reserve > 1000) throw new InvalidOperationException("予備の割合は0から1000です。");
            var end = End(c); long validUntil = TacticJson.Integer(c, "validUntilTick", checked(frame.Tick + 20)); int maxAge = checked((int)TacticJson.Integer(c, "maxObservationAgeTicks", 20));
            if (maxAge < 0 || validUntil < frame.Tick) throw new InvalidOperationException("命令の有効期間が不正です。");
            var flags = ExpireFlags.None;
            string flagText = TacticJson.String(c, "expire", false); if (!string.IsNullOrEmpty(flagText) && !EnumValue(flagText, out flags)) throw new InvalidOperationException("失効条件が不明です。");
            return new PolicyOrder(0, 0, CommandSource.Ai, targetScope, kind, goal, (byte)priority, new LossBudget((ushort)loss), end, (ushort)reserve, 0, Array.Empty<PolicyVersion>(), frame.Tick, new Expiration(validUntil, maxAge, flags));
        }

        private static EconomyCommand ParseEconomy(Dictionary<string, object> c, FactionFrame frame, int index)
        {
            var e = frame.Economy ?? throw new InvalidOperationException("この試合では内政命令を使えません。");
            string text = TacticJson.String(c, "kind", true); if (!EnumValue(text, out EconomyCommandKind kind)) throw new InvalidOperationException("対応していない内政命令です。");
            ulong sequence = checked((ulong)TacticJson.Integer(c, "sequence", (long)index + 1)); if (sequence == 0) throw new InvalidOperationException("sequenceは正数です。");
            switch (kind)
            {
                case EconomyCommandKind.PlaceBuilding:
                    BuildingKind building = EnumRequired<BuildingKind>(TacticJson.String(c, "building", true), "建物"); int cell = checked((int)TacticJson.Integer(c, "cell", -1, true));
                    if (cell < 0 || frame.Fog != null && frame.Fog.VisibleCells.Count != 0 && cell >= frame.Fog.VisibleCells.Count) throw new InvalidOperationException("建設セルが範囲外です。");
                    return EconomyCommand.Place(frame.FactionId, sequence, building, cell);
                case EconomyCommandKind.Train:
                    UnitKind unit = EnumRequired<UnitKind>(TacticJson.String(c, "unit", true), "兵種"); uint producer = UInt(c, "producerId", 0); if (producer != 0 && !OwnBuilding(e, frame.FactionId, producer)) throw new InvalidOperationException("訓練元が自分の建物ではありません。");
                    return EconomyCommand.Train(frame.FactionId, sequence, producer, unit);
                case EconomyCommandKind.AssignVillagers:
                    var villagerIds = UIntArray(c, "villagerIds", true); if (villagerIds.Length == 0 || villagerIds.Any(id => !OwnVillager(e, frame.FactionId, id))) throw new InvalidOperationException("村人IDが不正です。");
                    var targetObject = c.TryGetValue("target", out var rawTarget) ? TacticJson.Object(rawTarget, "target") : null;
                    EconomyTargetKind targetKind = targetObject == null ? EnumRequired<EconomyTargetKind>(TacticJson.String(c, "targetKind", true), "対象種類") : EnumRequired<EconomyTargetKind>(TacticJson.String(targetObject, "kind", true), "対象種類");
                    uint targetId = targetObject == null ? UInt(c, "targetId", 0) : UInt(targetObject, "id", 0); if (!ValidEconomyTarget(e, frame.FactionId, targetKind, targetId)) throw new InvalidOperationException("割り当て先が不正です。");
                    return EconomyCommand.Assign(frame.FactionId, sequence, villagerIds, targetKind, targetId);
                case EconomyCommandKind.Research:
                    uint blacksmith = UInt(c, "producerId", 0); if (!OwnBuildingKind(e, frame.FactionId, blacksmith, BuildingKind.Blacksmith)) throw new InvalidOperationException("鍛冶場が不正です。");
                    int tech;
                    if (c.TryGetValue("tech", out var rawTech) && rawTech is string techName && EnumValue(techName, out TechKind parsedTech)) tech = (int)parsedTech;
                    else tech = checked((int)TacticJson.Integer(c, "tech", 0, true));
                    if (tech < 1 || tech > 27) throw new InvalidOperationException("研究項目が範囲外です。");
                    return EconomyCommand.Research(frame.FactionId, sequence, blacksmith, (TechKind)tech);
                case EconomyCommandKind.AdvanceAge:
                    if (!e.Ages) throw new InvalidOperationException("この試合では時代を進められません。");
                    CivKind civ = EnumRequired<CivKind>(TacticJson.String(c, "civ", true), "文明"); if (civ == CivKind.Primitive) throw new InvalidOperationException("原始文明へは進めません。");
                    return EconomyCommand.Advance(frame.FactionId, sequence, civ);
                case EconomyCommandKind.SetEconomyPolicy:
                    EconomyPolicy policy = EnumRequired<EconomyPolicy>(TacticJson.String(c, "policy", true), "内政方針"); return EconomyCommand.SetPolicy(frame.FactionId, sequence, policy);
                case EconomyCommandKind.ReturnEconomyToAuto:
                    return EconomyCommand.ReturnToAuto(frame.FactionId, sequence);
                default: throw new InvalidOperationException("T-1では受け付けない内政命令です。");
            }
        }

        private static ScopeKey Scope(FactionFrame frame, Dictionary<string, object> c)
        {
            ScopeKind kind = EnumRequired<ScopeKind>(TacticJson.String(c, "kind", true), "対象範囲"); uint id = UInt(c, "id", 0);
            if (kind == ScopeKind.All && id != 0) throw new InvalidOperationException("Allのidは0です。");
            if (kind == ScopeKind.Army && !frame.Observation.OwnArmies.Any(x => x.Id == id)) throw new InvalidOperationException("存在しない自軍部隊です。");
            if (kind == ScopeKind.Outpost && !frame.Objectives.Any(x => x.Kind == GoalKind.Outpost && x.Id == id && x.IsOwnerKnown && x.OwnerFactionId == frame.FactionId)) throw new InvalidOperationException("存在しない自陣拠点です。");
            if (kind == ScopeKind.Region && !frame.Regions.Any(x => x.Id == id)) throw new InvalidOperationException("存在しない区域です。");
            return new ScopeKey(frame.FactionId, kind, id);
        }

        private static PolicyGoal Goal(FactionFrame frame, Dictionary<string, object> c)
        {
            GoalKind kind = EnumRequired<GoalKind>(TacticJson.String(c, "kind", true), "目標"); uint id = UInt(c, "id", 0); SimPoint point = default(SimPoint);
            if (c.TryGetValue("point", out var rawPoint) && rawPoint != null) point = Point(TacticJson.Object(rawPoint, "point"));
            if (kind == GoalKind.Point) { if (!c.ContainsKey("point")) throw new InvalidOperationException("Point目標には位置が必要です。"); return new PolicyGoal(kind, id, point); }
            if (kind == GoalKind.None) return default(PolicyGoal);
            var objective = frame.Objectives.FirstOrDefault(x => x.Kind == kind && x.Id == id);
            if (objective.Id == 0 && id != 0 || !frame.Objectives.Any(x => x.Kind == kind && x.Id == id)) throw new InvalidOperationException("観測していない目標です。");
            return new PolicyGoal(kind, id, point.RawEqual(default(SimPoint)) ? objective.Position : point);
        }

        private static EndCondition End(Dictionary<string, object> c)
        {
            if (!c.TryGetValue("end", out var raw) || raw == null) return new EndCondition(EndKind.UntilReplaced, 0);
            var end = TacticJson.Object(raw, "end"); EndKind kind = EnumRequired<EndKind>(TacticJson.String(end, "kind", true), "終了条件"); long tick = TacticJson.Integer(end, "tick", 0); if (kind == EndKind.AtTick && tick < 0) throw new InvalidOperationException("終了tickが不正です。"); return new EndCondition(kind, tick);
        }

        private static SimPoint Point(Dictionary<string, object> c) => new SimPoint(Fix(TacticJson.Number(c, "x", true)), Fix(TacticJson.Number(c, "z", true)));
        private static Fix64 Fix(decimal value) => Fix64.FromRatio(checked((long)decimal.Round(value * 1000m, 0, MidpointRounding.ToEven)), 1000);
        private static bool OwnObjective(FactionFrame frame, PolicyGoal goal) => frame.Objectives.Any(x => x.Kind == goal.Kind && x.Id == goal.Id && x.IsOwnerKnown && x.OwnerFactionId == frame.FactionId);
        private static bool OwnBuilding(EconomyView e, uint faction, uint id) => e.Buildings.Any(x => x.Id == id && x.FactionId == faction);
        private static bool OwnBuildingKind(EconomyView e, uint faction, uint id, BuildingKind kind) => e.Buildings.Any(x => x.Id == id && x.FactionId == faction && x.Kind == kind);
        private static bool OwnVillager(EconomyView e, uint faction, uint id) => e.Villagers.Any(x => x.Id == id && x.IsOwn);
        private static bool ValidEconomyTarget(EconomyView e, uint faction, EconomyTargetKind kind, uint id) => kind == EconomyTargetKind.ResourceNode ? e.Resources.Any(x => x.Id == id) : kind == EconomyTargetKind.Building && OwnBuilding(e, faction, id);
        private static uint UInt(Dictionary<string, object> c, string name, uint fallback) { long x = TacticJson.Integer(c, name, fallback); if (x < 0 || x > uint.MaxValue) throw new InvalidOperationException(name + "が範囲外です。"); return (uint)x; }
        private static uint[] UIntArray(Dictionary<string, object> c, string name, bool required)
        { var values = TacticJson.Array(c.TryGetValue(name, out var raw) ? raw : null, name); var result = new uint[values.Count]; for (int i = 0; i < result.Length; i++) { if (!(values[i] is long)) throw new FormatException(name + " must contain integers."); long x = (long)values[i]; if (x <= 0 || x > uint.MaxValue) throw new InvalidOperationException(name + "が範囲外です。"); result[i] = (uint)x; } return result; }

        private static bool ScopeAllowed(PolicyKind kind, ScopeKind scope) => kind == PolicyKind.Focus ? scope == ScopeKind.All || scope == ScopeKind.Army || scope == ScopeKind.Region : kind == PolicyKind.MaintainReserve ? scope != ScopeKind.Outpost : kind == PolicyKind.AllowAbandon ? scope == ScopeKind.Outpost || scope == ScopeKind.Region : true;
        private static bool Equals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static bool EnumValue<T>(string text, out T value) where T : struct
        { if (Enum.TryParse(text, true, out value)) return true; value = default(T); return false; }
        private static T EnumRequired<T>(string text, string name) where T : struct
        { if (EnumValue(text, out T value)) return value; throw new InvalidOperationException(name + "が不明です。"); }
    }

    internal static class SimPointExtensions
    {
        internal static bool RawEqual(this SimPoint a, SimPoint b) => a.X.Raw == b.X.Raw && a.Z.Raw == b.Z.Raw;
    }
}
