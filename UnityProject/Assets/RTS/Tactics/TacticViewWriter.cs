using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rts.Contracts;

namespace Rts.Tactics
{
    /// <summary>Writes the version 1, faction-local tactic observation in a canonical order.</summary>
    public static class TacticViewWriter
    {
        public const int Version = 1;

        public static string Write(FactionFrame frame) => Write(frame, null);

        public static string Write(FactionFrame frame, IReadOnlyDictionary<string, object> parameters)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var b = new StringBuilder(2048);
            b.Append("{\"version\":1,\"tick\":").Append(frame.Tick.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"factionId\":").Append(frame.FactionId.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"ownArmies\":[");
            var armies = (frame.Observation?.OwnArmies ?? Array.Empty<OwnArmyView>()).OrderBy(x => x.Id).ToArray();
            var compositions = ArmyCompositions(frame, armies);
            for (int i = 0; i < armies.Length; i++)
            {
                if (i != 0) b.Append(',');
                var a = armies[i];
                b.Append("{\"id\":").Append(a.Id).Append(",\"kind\":").Append(TacticJson.Quote(a.Kind.ToString()));
                b.Append(",\"count\":").Append(a.AliveCount);
                b.Append(",\"controlledBy\":").Append(TacticJson.Quote(ControlledBy(frame, a)));
                b.Append(",\"composition\":"); Composition(b, compositions[i]);
                b.Append(",\"position\":"); Point(b, a.Position);
                b.Append(",\"homeObjective\":"); Goal(b, a.HomeObjective);
                b.Append('}');
            }
            b.Append("],\"visibleEnemies\":[");
            var enemies = (frame.Observation?.VisibleEnemies ?? Array.Empty<VisibleEnemy>()).OrderBy(x => x.ContactId).ToArray();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (i != 0) b.Append(','); var e = enemies[i];
                b.Append("{\"id\":").Append(e.ContactId).Append(",\"kind\":").Append(e.Kind).Append(",\"kindName\":").Append(TacticJson.Quote(((UnitKind)e.Kind).ToString())).Append(",\"position\":"); Point(b, e.Position); b.Append('}');
            }
            b.Append("],\"contacts\":[");
            var contacts = (frame.Observation?.Contacts ?? Array.Empty<EnemyContact>()).OrderBy(x => x.ContactId).ToArray();
            for (int i = 0; i < contacts.Length; i++)
            {
                if (i != 0) b.Append(','); var c = contacts[i];
                b.Append("{\"id\":").Append(c.ContactId).Append(",\"position\":"); Point(b, c.LastPosition);
                b.Append(",\"lastSeenTick\":").Append(c.LastSeenTick).Append(",\"min\":").Append(c.EstimateMin).Append(",\"max\":").Append(c.EstimateMax);
                b.Append(",\"visible\":").Append(c.IsCurrentlyVisible ? "true" : "false").Append(",\"uncertain\":").Append(c.IsUncertain ? "true" : "false");
                b.Append(",\"strengthUnknown\":").Append(c.IsStrengthUnknown ? "true" : "false").Append(",\"absent\":").Append(c.IsAbsentAtLastPosition ? "true" : "false");
                b.Append(",\"visibleComposition\":"); Composition(b, ContactComposition(c, enemies));
                b.Append(",\"covered\":["); var covered = (c.CoveredContactIds ?? Array.Empty<uint>()).OrderBy(x => x).ToArray();
                for (int j = 0; j < covered.Length; j++) { if (j != 0) b.Append(','); b.Append(covered[j]); }
                b.Append("]}");
            }
            b.Append("],\"enemySummary\":"); EnemySummary(b, enemies);
            b.Append(",\"objectives\":[");
            var objectives = (frame.Observation?.Objectives ?? Array.Empty<KnownObjective>()).OrderBy(x => x.Kind).ThenBy(x => x.Id).ToArray();
            for (int i = 0; i < objectives.Length; i++) { if (i != 0) b.Append(','); Objective(b, objectives[i]); }
            b.Append(']');
            b.Append(",\"orders\":[");
            var orders = (frame.Commands ?? Array.Empty<CommandView>())
                .Where(x => x.Target.FactionId == frame.FactionId && IsEffective(x))
                .OrderBy(x => x.CommandId).ToArray();
            for (int i = 0; i < orders.Length; i++)
            {
                if (i != 0) b.Append(',');
                Order(b, orders[i]);
            }
            b.Append(']');
            b.Append(",\"economy\":"); Economy(b, frame);
            b.Append(",\"regions\":[");
            var regions = (frame.Regions ?? Array.Empty<RegionView>()).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < regions.Length; i++) { if (i != 0) b.Append(','); var r = regions[i]; b.Append("{\"id\":").Append(r.Id).Append(",\"centerKind\":").Append(TacticJson.Quote(r.CenterKind.ToString())).Append(",\"centerId\":").Append(r.CenterId).Append(",\"center\":"); Point(b, r.Center); b.Append(",\"control\":").Append(TacticJson.Quote(r.Control.ToString())).Append(",\"policy\":").Append(TacticJson.Quote(r.Policy.ToString())).Append(",\"economyPolicy\":").Append(TacticJson.Quote(r.EconomyPolicy.ToString())).Append('}'); }
            b.Append("],\"params\":").Append(TacticParameterJson.Object(parameters));
            b.Append('}');
            return b.ToString();
        }

        // Interpreting, Pending and Executing are live commands. Completed and later statuses are terminal,
        // so they are deliberately absent from orders even though CommandView retains them.
        private static bool IsEffective(CommandView command)
            => command.Status == CommandStatus.Interpreting || command.Status == CommandStatus.Pending || command.Status == CommandStatus.Executing;

        private static string ControlledBy(FactionFrame frame, OwnArmyView army)
        {
            CommandView? selected = null;
            foreach (var command in frame.Commands ?? Array.Empty<CommandView>())
            {
                if (!IsEffective(command) || command.Target.FactionId != frame.FactionId || !IsCombat(command.Kind) || !AffectsArmy(frame, command, army)) continue;
                if (!selected.HasValue || (byte)command.Source < (byte)selected.Value.Source ||
                    (command.Source == selected.Value.Source && command.CommandId > selected.Value.CommandId)) selected = command;
            }
            return selected.HasValue ? selected.Value.Source.ToString() : "None";
        }

        private static bool IsCombat(PolicyKind kind)
            => kind == PolicyKind.Focus || kind == PolicyKind.Retreat || kind == PolicyKind.Defend || kind == PolicyKind.Scout;

        private static bool AffectsArmy(FactionFrame frame, CommandView command, OwnArmyView army)
        {
            switch (command.Target.Kind)
            {
                case ScopeKind.All:
                    return true;
                case ScopeKind.Army:
                    return command.Target.Id == army.Id;
                case ScopeKind.Outpost:
                    // The home objective is the stable outpost relation exposed by OwnArmyView.
                    return army.HomeObjective.Kind == GoalKind.Outpost && army.HomeObjective.Id == command.Target.Id;
                case ScopeKind.Region:
                    return RegionForArmy(frame, army) == command.Target.Id;
                default:
                    return false;
            }
        }

        // Simulation assigns cells to the nearest live region centre, with the lower region id breaking ties. The
        // faction frame exposes those same centres; using the army reference point gives tactics the same stable
        // region choice without adding map details to Contracts.
        private static uint RegionForArmy(FactionFrame frame, OwnArmyView army)
        {
            uint best = 0;
            long bestDistance = long.MaxValue;
            foreach (var region in frame.Regions ?? Array.Empty<RegionView>())
            {
                if (region.CenterKind == RegionCenterKind.None) continue;
                long dx = (long)army.Position.X.Raw - region.Center.X.Raw;
                long dz = (long)army.Position.Z.Raw - region.Center.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (best == 0 || distance < bestDistance || distance == bestDistance && region.Id < best)
                {
                    best = region.Id;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static void Order(StringBuilder b, CommandView x)
        {
            b.Append("{\"id\":").Append(x.CommandId);
            b.Append(",\"source\":").Append(TacticJson.Quote(x.Source.ToString()));
            b.Append(",\"kind\":").Append(TacticJson.Quote(x.Kind.ToString()));
            b.Append(",\"target\":"); Scope(b, x.Target);
            b.Append(",\"goal\":"); Goal(b, x.Goal);
            b.Append(",\"status\":").Append(TacticJson.Quote(x.Status.ToString()));
            b.Append(",\"acceptedTick\":").Append(x.AcceptedTick);
            b.Append(",\"applyTick\":").Append(x.ApplyTick);
            b.Append('}');
        }

        private static void Scope(StringBuilder b, ScopeKey x)
        {
            b.Append("{\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"id\":").Append(x.Id).Append('}');
        }

        // Contracts do not link a soldier to its army, so each own soldier (villagers excluded) counts toward the
        // nearest army; ties go to the lower army id. Armies are already sorted by id.
        private static Dictionary<string, int>[] ArmyCompositions(FactionFrame frame, OwnArmyView[] armies)
        {
            var result = new Dictionary<string, int>[armies.Length];
            for (int i = 0; i < armies.Length; i++) result[i] = new Dictionary<string, int>(StringComparer.Ordinal);
            if (frame.Units == null || armies.Length == 0) return result;
            foreach (var unit in frame.Units)
            {
                if (!unit.IsOwn || unit.Kind == UnitKind.Villager) continue;
                int nearest = 0;
                long best = long.MaxValue;
                for (int i = 0; i < armies.Length; i++)
                {
                    long dx = (long)unit.Position.X.Raw - armies[i].Position.X.Raw;
                    long dz = (long)unit.Position.Z.Raw - armies[i].Position.Z.Raw;
                    long distance = dx * dx + dz * dz;
                    if (distance < best) { best = distance; nearest = i; }
                }
                string name = unit.Kind.ToString();
                var counts = result[nearest];
                counts[name] = counts.TryGetValue(name, out var value) ? value + 1 : 1;
            }
            return result;
        }

        private static IReadOnlyDictionary<string, int> ContactComposition(EnemyContact contact, VisibleEnemy[] enemies)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var enemy in enemies)
            {
                bool belongs = enemy.ContactId == contact.ContactId ||
                    (contact.CoveredContactIds ?? Array.Empty<uint>()).Contains(enemy.ContactId);
                if (!belongs) continue;
                string name = ((UnitKind)enemy.Kind).ToString();
                counts[name] = counts.TryGetValue(name, out var value) ? value + 1 : 1;
            }
            return counts;
        }

        private static void Composition(StringBuilder b, IReadOnlyDictionary<string, int> counts)
        {
            b.Append('{');
            bool first = true;
            foreach (var pair in counts.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!first) b.Append(',');
                first = false;
                b.Append(TacticJson.Quote(pair.Key)).Append(':').Append(pair.Value);
            }
            b.Append('}');
        }

        private static void EnemySummary(StringBuilder b, VisibleEnemy[] enemies)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var enemy in enemies)
            {
                string name = ((UnitKind)enemy.Kind).ToString();
                counts[name] = counts.TryGetValue(name, out var value) ? value + 1 : 1;
            }
            b.Append("{\"visibleCount\":").Append(enemies.Length).Append(",\"byKind\":");
            Composition(b, counts);
            b.Append('}');
        }

        private static void Objective(StringBuilder b, KnownObjective x)
        {
            b.Append("{\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"id\":").Append(x.Id).Append(",\"position\":"); Point(b, x.Position);
            b.Append(",\"ownerKnown\":").Append(x.IsOwnerKnown ? "true" : "false").Append(",\"ownerFactionId\":").Append(x.OwnerFactionId).Append(",\"hpKnown\":").Append(x.IsHpKnown ? "true" : "false").Append(",\"hp\":").Append(x.Hp).Append(",\"lastSeenTick\":").Append(x.LastSeenTick);
            b.Append(",\"capturingFactionId\":").Append(x.CapturingFactionId).Append(",\"captureTicks\":").Append(x.CaptureTicks).Append(",\"captureDurationTicks\":").Append(x.CaptureDurationTicks).Append('}');
        }

        private static void Goal(StringBuilder b, PolicyGoal x)
        {
            b.Append("{\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"id\":").Append(x.Id).Append(",\"point\":"); Point(b, x.Point); b.Append('}');
        }

        private static void Economy(StringBuilder b, FactionFrame frame)
        {
            var e = frame.Economy;
            if (e == null) { b.Append("null"); return; }
            var next = NextAgeCost(e);
            bool canAdvanceNow = e.Ages && e.Age < 3 && e.AdvancingTo == CivKind.Primitive &&
                e.Food >= next.food && e.Wood >= next.wood && e.Gold >= next.gold;
            b.Append("{\"food\":").Append(e.Food).Append(",\"wood\":").Append(e.Wood).Append(",\"ore\":").Append(e.Ore).Append(",\"metal\":").Append(e.Metal).Append(",\"stone\":").Append(e.Stone).Append(",\"gold\":").Append(e.Gold).Append(",\"gems\":").Append(e.Gems).Append(",\"population\":").Append(e.Population).Append(",\"populationCap\":").Append(e.PopulationCap).Append(",\"civilisation\":").Append(TacticJson.Quote(e.Civ.ToString())).Append(",\"age\":").Append(e.Age).Append(",\"agesEnabled\":").Append(e.Ages ? "true" : "false").Append(",\"advancingTo\":").Append(TacticJson.Quote(e.AdvancingTo.ToString())).Append(",\"advanceRemainingTicks\":").Append(e.AdvanceRemaining).Append(",\"nextAgeCost\":{\"food\":").Append(next.food).Append(",\"wood\":").Append(next.wood).Append(",\"gold\":").Append(next.gold).Append("},\"canAdvanceNow\":").Append(canAdvanceNow ? "true" : "false").Append(",\"auto\":").Append(e.AutoEconomy ? "true" : "false").Append(",\"policy\":").Append(TacticJson.Quote(e.Policy.ToString()));
            b.Append(",\"villagers\":["); var villagers = e.Villagers.Where(x => x.IsOwn && x.Id != 0).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < villagers.Length; i++) { if (i != 0) b.Append(','); var v = villagers[i]; b.Append("{\"id\":").Append(v.Id).Append(",\"position\":"); Point(b, v.Position); b.Append(",\"activity\":").Append(TacticJson.Quote(v.Activity.ToString())).Append(",\"hp\":").Append(v.Hp).Append('}'); }
            b.Append("],\"buildings\":["); var buildings = e.Buildings.Where(x => x.FactionId == frame.FactionId).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < buildings.Length; i++) { if (i != 0) b.Append(','); var x = buildings[i]; b.Append("{\"id\":").Append(x.Id).Append(",\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"position\":"); Point(b, x.Center); b.Append(",\"complete\":").Append(x.Complete ? "true" : "false").Append(",\"queued\":").Append(x.Queued).Append(",\"researching\":").Append(TacticJson.Quote(x.Researching.ToString())).Append('}'); }
            b.Append("],\"resources\":["); var resources = e.Resources.OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < resources.Length; i++) { if (i != 0) b.Append(','); var x = resources[i]; b.Append("{\"id\":").Append(x.Id).Append(",\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"position\":"); Point(b, x.Position); b.Append(",\"remaining\":").Append(x.Remaining).Append('}'); }
            b.Append("]}");
        }

        private static (int food, int wood, int gold) NextAgeCost(EconomyView e)
        {
            if (!e.Ages || e.Age >= 3) return (0, 0, 0);
            if (e.Civ == CivKind.Primitive) return (e.AdvanceFoodCost, e.AdvanceWoodCost, 0);
            if (e.Age == 1) return (e.Age2FoodCost, e.Age2WoodCost, 0);
            return (e.Age3FoodCost, e.Age3WoodCost, e.NextAgeGoldCost);
        }

        private static void Point(StringBuilder b, SimPoint p)
        {
            b.Append("{\"x\":").Append(Fix(p.X)).Append(",\"z\":").Append(Fix(p.Z)).Append('}');
        }

        private static string Fix(Fix64 value)
        {
            decimal exact = (decimal)value.Raw / 65536m;
            return decimal.Round(exact, 3, MidpointRounding.ToEven).ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
