using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tactics
{
    public sealed partial class MatchPackWriter
    {
        private static readonly CommandSource[] CreditSources =
            { CommandSource.Human, CommandSource.Doctrine, CommandSource.Ai };
        private readonly CreditFaction[] creditFactions = { new CreditFaction(1), new CreditFaction(2) };
        private readonly List<CreditHighlight> creditHighlights = new List<CreditHighlight>();
        private bool creditStarted;
        private long creditLastTick;
        private FactionFrame[] creditPreviousFrames;
        private MatchPackCreditSnapshot creditPreviousPolicies;

        // Capturing both factions every tick doubles the live match's frame work, so credit is sampled once a second.
        // Time is attributed from the difference between samples, so the totals stay whole.
        private const int CreditIntervalSteps = 20;
        private int creditStepsSinceSample;

        private void RecordCredit(Battle simulation, bool force = false)
        {
            if (!force && creditStarted && ++creditStepsSinceSample < CreditIntervalSteps) return;
            creditStepsSinceSample = 0;
            var frames = new[] { simulation.Capture(1), simulation.Capture(2) };
            var policies = simulation.CaptureMatchPackCredit();
            foreach (var frame in frames) CollectCommands(frame);

            if (!creditStarted)
            {
                creditStarted = true;
                creditLastTick = frames[0].Tick;
                creditPreviousFrames = frames;
                creditPreviousPolicies = policies;
                return;
            }

            if (frames[0].Tick < creditLastTick)
                throw new InvalidOperationException("記録パックのcredit観測tickが逆行しました。");
            if (frames[0].Tick == creditLastTick) return;

            long elapsed = frames[0].Tick - creditLastTick;
            for (int i = 0; i < frames.Length; i++)
            {
                AttributeInterval(creditPreviousFrames[i], frames[i], creditPreviousPolicies, elapsed);
                ObserveObjectives(creditPreviousFrames[i], frames[i], policies);
                ObserveCoreLoss(creditPreviousFrames[i], frames[i], policies);
            }
            creditLastTick = frames[0].Tick;
            creditPreviousFrames = frames;
            creditPreviousPolicies = policies;
        }

        private void CollectCommands(FactionFrame frame)
        {
            var faction = creditFactions[(int)frame.FactionId - 1];
            foreach (var command in frame.Commands)
            {
                if (!faction.Commands.TryGetValue(command.CommandId, out var record))
                {
                    record = new CreditCommandRecord(command.CommandId, command.Source);
                    faction.Commands.Add(command.CommandId, record);
                }
                record.Observe(command.Status, command.Reason);
            }
            foreach (var gameEvent in frame.Events.Where(e => e.Kind == EventKind.CommandChanged && e.CommandId != 0))
            {
                if (faction.Commands.TryGetValue(gameEvent.CommandId, out var record) &&
                    gameEvent.Value >= byte.MinValue && gameEvent.Value <= byte.MaxValue &&
                    Enum.IsDefined(typeof(CommandStatus), (byte)gameEvent.Value))
                    record.Observe((CommandStatus)gameEvent.Value, gameEvent.Reason);
            }
        }

        private void AttributeInterval(FactionFrame previous, FactionFrame current, MatchPackCreditSnapshot previousPolicies, long elapsed)
        {
            var faction = creditFactions[(int)previous.FactionId - 1];
            var currentArmies = current.Observation.OwnArmies.ToDictionary(a => a.Id);
            var policyByArmy = previousPolicies.Armies.Where(a => a.FactionId == previous.FactionId)
                .ToDictionary(a => a.ArmyId);
            foreach (var oldArmy in previous.Observation.OwnArmies)
            {
                if (!currentArmies.TryGetValue(oldArmy.Id, out var newArmy)) continue;
                if (!faction.Armies.TryGetValue(oldArmy.Id, out var army))
                {
                    army = new CreditArmy(oldArmy.Id);
                    faction.Armies.Add(oldArmy.Id, army);
                }
                faction.TotalArmyTicks = checked(faction.TotalArmyTicks + elapsed);
                policyByArmy.TryGetValue(oldArmy.Id, out var policy);
                if (policy.HasCommand)
                {
                    faction.GetSource(policy.Source).TimeTicks = checked(faction.GetSource(policy.Source).TimeTicks + elapsed);
                    army.GetSource(policy.Source).TimeTicks = checked(army.GetSource(policy.Source).TimeTicks + elapsed);
                }
                else army.UnassignedTicks = checked(army.UnassignedTicks + elapsed);

                int losses = Math.Max(0, oldArmy.AliveCount - newArmy.AliveCount);
                if (losses == 0) continue;
                if (policy.HasCommand)
                {
                    faction.GetSource(policy.Source).Losses += losses;
                    army.GetSource(policy.Source).Losses += losses;
                    AddHighlight(current.Tick, previous.FactionId, policy.Source, "losses", losses);
                }
                else
                {
                    faction.UnattributedLosses += losses;
                    army.UnattributedLosses += losses;
                    AddHighlight(current.Tick, previous.FactionId, null, "losses", losses);
                }
            }

            var currentContacts = new HashSet<uint>(current.Observation.Contacts.Select(c => c.ContactId));
            var previousPolicyByArmy = creditPreviousPolicies.Armies.Where(a => a.FactionId == previous.FactionId)
                .ToDictionary(a => a.ArmyId);
            foreach (var enemy in previous.Observation.VisibleEnemies)
            {
                if (currentContacts.Contains(enemy.ContactId)) continue;
                if (TryNearestSource(previous, previousPolicyByArmy, enemy.Position, out var source))
                {
                    faction.GetSource(source).EnemyKills++;
                    AddHighlight(current.Tick, previous.FactionId, source, "enemyKills", 1);
                }
                else
                {
                    faction.UnattributedEnemyKills++;
                    AddHighlight(current.Tick, previous.FactionId, null, "enemyKills", 1);
                }
            }
        }

        private void ObserveObjectives(FactionFrame previous, FactionFrame current, MatchPackCreditSnapshot currentPolicies)
        {
            var faction = creditFactions[(int)current.FactionId - 1];
            var oldObjectives = previous.Objectives.Where(o => o.IsOwnerKnown).ToDictionary(o => ObjectiveKey(o.Kind, o.Id));
            var policyByArmy = currentPolicies.Armies.Where(a => a.FactionId == current.FactionId).ToDictionary(a => a.ArmyId);
            foreach (var objective in current.Objectives.Where(o => o.IsOwnerKnown))
            {
                oldObjectives.TryGetValue(ObjectiveKey(objective.Kind, objective.Id), out var old);
                bool changed = old.IsOwnerKnown && old.OwnerFactionId != objective.OwnerFactionId;
                if (changed && objective.Kind == GoalKind.Outpost)
                {
                    if (objective.OwnerFactionId == current.FactionId)
                        AddObjectiveEvent(faction, current, currentPolicies, objective, "captured", policyByArmy);
                    else if (old.OwnerFactionId == current.FactionId)
                        AddObjectiveEvent(faction, current, currentPolicies, objective, "lost", policyByArmy);
                }

                if (objective.Kind != GoalKind.Outpost) continue;
                string key = ObjectiveKey(objective.Kind, objective.Id);
                if (objective.CapturingFactionId != 0 && objective.CapturingFactionId != current.FactionId &&
                    objective.OwnerFactionId == current.FactionId)
                {
                    if (!faction.Defending.ContainsKey(key))
                        faction.Defending[key] = new CreditDefenseAttempt(current.Tick, TryNearestSource(current, policyByArmy, objective.Position, out var source) ? source : (CommandSource?)null);
                }
                else if (faction.Defending.TryGetValue(key, out var attempt) &&
                    objective.CapturingFactionId == 0 && objective.OwnerFactionId == current.FactionId && !changed)
                {
                    AddObjectiveEvent(faction, current.Tick, objective, "defended", attempt.Source);
                    faction.Defending.Remove(key);
                }
                if (changed) faction.Defending.Remove(key);
            }
        }

        private void ObserveCoreLoss(FactionFrame previous, FactionFrame current, MatchPackCreditSnapshot policies)
        {
            if (!current.Result.HasEnded || current.Result.WinnerFactionId == 0 ||
                current.Result.WinnerFactionId == current.FactionId) return;
            var faction = creditFactions[(int)current.FactionId - 1];
            if (faction.CoreLossRecorded) return;
            var core = current.Objectives.FirstOrDefault(o => o.Kind == GoalKind.Core && o.Id == current.FactionId);
            if (!core.IsOwnerKnown) return;
            var sourceByArmy = policies.Armies.Where(a => a.FactionId == current.FactionId).ToDictionary(a => a.ArmyId);
            var source = TryNearestSource(current, sourceByArmy, core.Position, out var found) ? found : (CommandSource?)null;
            AddObjectiveEvent(faction, current.Tick, core, "lost", source);
            faction.CoreLossRecorded = true;
        }

        private void AddObjectiveEvent(CreditFaction faction, FactionFrame frame, MatchPackCreditSnapshot policies,
            KnownObjective objective, string action, Dictionary<uint, MatchPackArmyPolicy> policyByArmy)
        {
            var source = TryNearestSource(frame, policyByArmy, objective.Position, out var found) ? (CommandSource?)found : null;
            AddObjectiveEvent(faction, frame.Tick, objective, action, source);
        }

        private void AddObjectiveEvent(CreditFaction faction, long tick, KnownObjective objective, string action, CommandSource? source)
        {
            faction.ObjectiveEvents.Add(new CreditObjectiveEvent(tick, objective.Kind, objective.Id, action, source));
            if (source.HasValue)
            {
                var stats = faction.GetSource(source.Value);
                if (action == "captured") stats.Captured++;
                else if (action == "defended") stats.Defended++;
                else if (action == "lost") stats.Lost++;
            }
        }

        private static bool TryNearestSource(FactionFrame frame, IDictionary<uint, MatchPackArmyPolicy> policies,
            SimPoint position, out CommandSource source)
        {
            source = CommandSource.Human;
            bool found = false;
            double best = double.MaxValue;
            uint bestArmyId = uint.MaxValue;
            foreach (var army in frame.Observation.OwnArmies)
            {
                if (!policies.TryGetValue(army.Id, out var policy) || !policy.HasCommand) continue;
                double dx = (double)army.Position.X.Raw - position.X.Raw;
                double dz = (double)army.Position.Z.Raw - position.Z.Raw;
                double distance = dx * dx + dz * dz;
                if (!found || distance < best || distance == best && army.Id < bestArmyId)
                {
                    found = true;
                    best = distance;
                    bestArmyId = army.Id;
                    source = policy.Source;
                }
            }
            return found;
        }

        private void AddHighlight(long tick, uint factionId, CommandSource? source, string kind, int count)
        {
            creditHighlights.Add(new CreditHighlight(tick, factionId, source, kind, count));
        }

        private string CreditJson()
        {
            var b = new StringBuilder(); b.Append('{');
            Field(b, "schemaVersion", "1");
            b.Append(",\"factions\":[");
            for (int i = 0; i < creditFactions.Length; i++)
            {
                if (i != 0) b.Append(',');
                b.Append(FactionCreditJson(creditFactions[i]));
            }
            b.Append(']');
            Field(b, "highlights", HighlightsJson());
            Field(b, "limitations", "[\"敵撃破は各陣営が見えていた個体の消失だけを数え、霧の外の情報は使わない\",\"撃破はGameEventに攻撃者がないため、その時点の最寄りの実効命令へ近似帰属し、決められないものはunattributedEnemyKillsに分ける\",\"コアの守備成功や、GameEventに出ない戦闘の細かな因果は記録できない\"]");
            b.Append('}'); return b.ToString();
        }

        private string FactionCreditJson(CreditFaction faction)
        {
            var b = new StringBuilder(); b.Append('{');
            Field(b, "factionId", faction.FactionId.ToString(CultureInfo.InvariantCulture));
            Field(b, "totalArmyTicks", faction.TotalArmyTicks.ToString(CultureInfo.InvariantCulture));
            Field(b, "unattributedEnemyKills", faction.UnattributedEnemyKills.ToString(CultureInfo.InvariantCulture));
            Field(b, "unattributedLosses", faction.UnattributedLosses.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"sources\":[");
            for (int i = 0; i < CreditSources.Length; i++)
            {
                if (i != 0) b.Append(',');
                b.Append(SourceCreditJson(faction, CreditSources[i]));
            }
            b.Append(']');
            Field(b, "armies", ArmiesJson(faction));
            Field(b, "commands", CommandsJson(faction));
            Field(b, "objectiveEvents", ObjectiveEventsJson(faction));
            b.Append('}'); return b.ToString();
        }

        private string SourceCreditJson(CreditFaction faction, CommandSource source)
        {
            var s = faction.GetSource(source); var b = new StringBuilder(); b.Append('{');
            Field(b, "source", Quote(source.ToString()));
            Field(b, "timeTicks", s.TimeTicks.ToString(CultureInfo.InvariantCulture));
            Field(b, "timeFraction", Fraction(s.TimeTicks, faction.TotalArmyTicks));
            Field(b, "enemyKills", s.EnemyKills.ToString(CultureInfo.InvariantCulture));
            Field(b, "losses", s.Losses.ToString(CultureInfo.InvariantCulture));
            Field(b, "objectives", "{\"captured\":" + s.Captured + ",\"defended\":" + s.Defended + ",\"lost\":" + s.Lost + "}");
            b.Append('}'); return b.ToString();
        }

        private string ArmiesJson(CreditFaction faction)
        {
            return "[" + string.Join(",", faction.Armies.Values.OrderBy(a => a.ArmyId).Select(army =>
            {
                var b = new StringBuilder(); b.Append('{');
                Field(b, "armyId", army.ArmyId.ToString(CultureInfo.InvariantCulture));
                Field(b, "unassignedTicks", army.UnassignedTicks.ToString(CultureInfo.InvariantCulture));
                Field(b, "unattributedLosses", army.UnattributedLosses.ToString(CultureInfo.InvariantCulture));
                b.Append(",\"sources\":[");
                for (int i = 0; i < CreditSources.Length; i++) { if (i != 0) b.Append(','); b.Append(ArmySourceJson(army.GetSource(CreditSources[i]), CreditSources[i])); }
                b.Append(']'); b.Append('}'); return b.ToString();
            })) + "]";
        }

        private static string ArmySourceJson(CreditSourceStats stats, CommandSource source)
        {
            var b = new StringBuilder(); b.Append('{');
            Field(b, "source", Quote(source.ToString())); Field(b, "timeTicks", stats.TimeTicks.ToString(CultureInfo.InvariantCulture));
            Field(b, "enemyKills", stats.EnemyKills.ToString(CultureInfo.InvariantCulture)); Field(b, "losses", stats.Losses.ToString(CultureInfo.InvariantCulture));
            b.Append('}'); return b.ToString();
        }

        private static string CommandsJson(CreditFaction faction)
        {
            return "{" + string.Join(",", CreditSources.Select(source => Quote(source.ToString()) + ":" + CommandSourceJson(faction, source))) + "}";
        }

        private static string CommandSourceJson(CreditFaction faction, CommandSource source)
        {
            var records = faction.Commands.Values.Where(c => c.Source == source).ToArray();
            int accepted = records.Count(c => c.Accepted), executed = records.Count(c => c.Executed), discarded = records.Count(c => c.Discarded);
            var reasons = records.SelectMany(c => c.DiscardReasons).GroupBy(r => r).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.ToString()).Take(3);
            var b = new StringBuilder(); b.Append('{');
            Field(b, "issued", records.Length.ToString(CultureInfo.InvariantCulture)); Field(b, "accepted", accepted.ToString(CultureInfo.InvariantCulture));
            Field(b, "executed", executed.ToString(CultureInfo.InvariantCulture)); Field(b, "discarded", discarded.ToString(CultureInfo.InvariantCulture));
            Field(b, "discardReasons", "[" + string.Join(",", reasons.Select(g => "{\"reason\":" + Quote(g.Key.ToString()) + ",\"count\":" + g.Count().ToString(CultureInfo.InvariantCulture) + "}")) + "]");
            b.Append('}'); return b.ToString();
        }

        private static string ObjectiveEventsJson(CreditFaction faction)
        {
            return "[" + string.Join(",", faction.ObjectiveEvents.OrderBy(e => e.Tick).ThenBy(e => e.Kind).ThenBy(e => e.Id).Select(e =>
                "{\"tick\":" + e.Tick.ToString(CultureInfo.InvariantCulture) + ",\"kind\":" + Quote(e.Kind.ToString()) +
                ",\"id\":" + e.Id.ToString(CultureInfo.InvariantCulture) + ",\"action\":" + Quote(e.Action) +
                ",\"source\":" + Quote(e.Source.HasValue ? e.Source.Value.ToString() : "Unknown") + "}")) + "]";
        }

        private string HighlightsJson()
        {
            var highlights = creditHighlights.GroupBy(h => h.Tick + ":" + h.FactionId + ":" + h.Source + ":" + h.Kind)
                .Select(g => new CreditHighlight(g.First().Tick, g.First().FactionId, g.First().Source, g.First().Kind, g.Sum(h => h.Count)))
                .OrderByDescending(h => h.Count).ThenBy(h => h.Tick).ThenBy(h => h.FactionId).Take(8).ToArray();
            return "[" + string.Join(",", highlights.Select(h => "{\"tick\":" + h.Tick.ToString(CultureInfo.InvariantCulture) + ",\"factionId\":" + h.FactionId.ToString(CultureInfo.InvariantCulture) +
                ",\"source\":" + Quote(h.Source.HasValue ? h.Source.Value.ToString() : "Unknown") + ",\"kind\":" + Quote(h.Kind) + ",\"count\":" + h.Count.ToString(CultureInfo.InvariantCulture) + "}")) + "]";
        }

        private string CreditReadmeTable()
        {
            var b = new StringBuilder("## 誰の手柄か\n\n`credit.json` の集計です。割合はその陣営の全部隊tickを分母にし、命令なし・帰属不能分は別欄に残します。\n\n");
            b.Append("|陣営|出どころ|時間の割合|倒した数|失った数|拠点（取/守/失）|\n|---|---|---:|---:|---:|---:|\n");
            foreach (var faction in creditFactions)
                foreach (var source in CreditSources)
                {
                    var s = faction.GetSource(source);
                    b.Append('|').Append(faction.FactionId == 1 ? "西" : "東").Append('|').Append(SourceLabel(source)).Append('|')
                        .Append((FractionValue(s.TimeTicks, faction.TotalArmyTicks) * 100m).ToString("0.##", CultureInfo.InvariantCulture)).Append("%|")
                        .Append(s.EnemyKills).Append('|').Append(s.Losses).Append('|').Append(s.Captured).Append('/').Append(s.Defended).Append('/').Append(s.Lost).Append("|\n");
                }
            b.Append("\n目立つ出来事（tick、件数）:\n\n");
            var highlights = creditHighlights.GroupBy(h => h.Tick + ":" + h.FactionId + ":" + h.Source + ":" + h.Kind)
                .Select(g => new CreditHighlight(g.First().Tick, g.First().FactionId, g.First().Source, g.First().Kind, g.Sum(h => h.Count)))
                .OrderByDescending(h => h.Count).ThenBy(h => h.Tick).Take(5).ToArray();
            if (highlights.Length == 0) b.Append("（該当する観測可能な大きな変化なし）\n");
            else foreach (var h in highlights) b.Append("- tick ").Append(h.Tick).Append("：陣営").Append(h.FactionId).Append(' ').Append(h.Source.HasValue ? h.Source.Value.ToString() : "Unknown").Append(' ').Append(h.Kind).Append(" ×").Append(h.Count).Append("\n");
            if (creditFactions.Any(f => f.UnattributedEnemyKills > 0 || f.UnattributedLosses > 0)) b.Append("\n※見えていたが命令へ結び付けられない撃破・損失は `credit.json` の帰属不能欄にあります。\n");
            return b.ToString();
        }

        private static string ObjectiveKey(GoalKind kind, uint id) => ((byte)kind).ToString(CultureInfo.InvariantCulture) + ":" + id.ToString(CultureInfo.InvariantCulture);
        private static string SourceLabel(CommandSource source) => source == CommandSource.Human ? "人（Human）" : source == CommandSource.Ai ? "お任せ（Ai）" : "戦術（Doctrine）";
        private static string Fraction(long numerator, long denominator) => FractionValue(numerator, denominator).ToString("0.####", CultureInfo.InvariantCulture);
        private static decimal FractionValue(long numerator, long denominator) => denominator == 0 ? 0m : numerator / (decimal)denominator;

        private sealed class CreditFaction
        {
            internal readonly uint FactionId;
            internal long TotalArmyTicks;
            internal int UnattributedEnemyKills, UnattributedLosses;
            internal bool CoreLossRecorded;
            internal readonly Dictionary<CommandSource, CreditSourceStats> Sources = new Dictionary<CommandSource, CreditSourceStats>();
            internal readonly Dictionary<uint, CreditArmy> Armies = new Dictionary<uint, CreditArmy>();
            internal readonly Dictionary<ulong, CreditCommandRecord> Commands = new Dictionary<ulong, CreditCommandRecord>();
            internal readonly Dictionary<string, CreditDefenseAttempt> Defending = new Dictionary<string, CreditDefenseAttempt>(StringComparer.Ordinal);
            internal readonly List<CreditObjectiveEvent> ObjectiveEvents = new List<CreditObjectiveEvent>();
            internal CreditFaction(uint factionId) { FactionId = factionId; foreach (var source in CreditSources) Sources[source] = new CreditSourceStats(); }
            internal CreditSourceStats GetSource(CommandSource source) => Sources[source];
        }

        private sealed class CreditArmy
        {
            internal readonly uint ArmyId;
            internal long UnassignedTicks;
            internal int UnattributedLosses;
            internal readonly Dictionary<CommandSource, CreditSourceStats> Sources = new Dictionary<CommandSource, CreditSourceStats>();
            internal CreditArmy(uint armyId) { ArmyId = armyId; foreach (var source in CreditSources) Sources[source] = new CreditSourceStats(); }
            internal CreditSourceStats GetSource(CommandSource source) => Sources[source];
        }

        private sealed class CreditSourceStats { internal long TimeTicks; internal int EnemyKills, Losses, Captured, Defended, Lost; }

        private sealed class CreditCommandRecord
        {
            internal readonly ulong CommandId;
            internal readonly CommandSource Source;
            internal bool Accepted, Executed;
            internal readonly List<ReasonCode> DiscardReasons = new List<ReasonCode>();
            internal CreditCommandRecord(ulong commandId, CommandSource source) { CommandId = commandId; Source = source; }
            internal bool Discarded => DiscardReasons.Count != 0;
            internal void Observe(CommandStatus status, ReasonCode reason)
            {
                if (status != CommandStatus.Interpreting) Accepted = true;
                if (status == CommandStatus.Executing) Executed = true;
                if (status >= CommandStatus.Completed && !Executed && status != CommandStatus.Completed && !DiscardReasons.Contains(reason)) DiscardReasons.Add(reason);
            }
        }

        private sealed class CreditDefenseAttempt
        {
            internal readonly long Tick;
            internal readonly CommandSource? Source;
            internal CreditDefenseAttempt(long tick, CommandSource? source) { Tick = tick; Source = source; }
        }

        private sealed class CreditObjectiveEvent
        {
            internal readonly long Tick; internal readonly GoalKind Kind; internal readonly uint Id; internal readonly string Action; internal readonly CommandSource? Source;
            internal CreditObjectiveEvent(long tick, GoalKind kind, uint id, string action, CommandSource? source) { Tick = tick; Kind = kind; Id = id; Action = action; Source = source; }
        }

        private sealed class CreditHighlight
        {
            internal readonly long Tick; internal readonly uint FactionId; internal readonly CommandSource? Source; internal readonly string Kind; internal readonly int Count;
            internal CreditHighlight(long tick, uint factionId, CommandSource? source, string kind, int count) { Tick = tick; FactionId = factionId; Source = source; Kind = kind; Count = count; }
        }
    }
}
