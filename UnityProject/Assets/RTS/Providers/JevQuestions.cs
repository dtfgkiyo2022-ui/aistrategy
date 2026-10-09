using System;
using System.Collections.Generic;
using System.Text;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>Which exceptional questions are needed for one judgement.</summary>
    public sealed class JevQuestionContext
    {
        /// <summary>True only while a G-4 operation condition is being checked.</summary>
        public bool OperationConditionNeeded;

        /// <summary>True only for a human instruction being translated by Jev.</summary>
        public bool InstructionTranslationNeeded;

        /// <summary>Null means the provider's configured first/every-N cadence.</summary>
        public bool? IncludeComprehension;

        /// <summary>Own tactic signals to expose to an instruction translation request.</summary>
        public IReadOnlyList<AiTacticSignalInfo> SignalDefinitions;

        /// <summary>Processing lines that are currently legal for this faction.</summary>
        public IReadOnlyList<string> AvailableLineNames;

        /// <summary>Region names currently present in the state name table.</summary>
        public IReadOnlyList<string> RegionNames;
    }

    /// <summary>
    /// The questions sent to Jev. The set is assembled per call: the regular judgement is small, while operation,
    /// instruction, and comprehension questions are included only when their caller needs them.
    /// <para>
    /// q8 sent every question on every call. That made the state (about 250 tokens) the small part of a roughly 2,000
    /// token request. q9 keeps the observable meaning of the fact and judgement questions, but makes the regular set
    /// short. The wording remains versioned because a wording change changes model behaviour.
    /// </para>
    /// </summary>
    public static class JevQuestions
    {
        public const string Version = "q11";

        private const string DecisivePoint =
            "\"decisive_point\":{\"type\":\"choice\",\"instructions\":\"次の1分間に勝敗を左右する場所はどこであるか。stateの観測だけで判断する。\",\"criteria\":{\"north_outpost\":\"北の拠点\",\"south_outpost\":\"南の拠点\",\"my_core\":\"自分のコア\",\"enemy_core\":\"敵のコア\"}}";

        private const string DangerousOutpost =
            "\"dangerous_outpost\":{\"type\":\"choice\",\"instructions\":\"次の1分間に最も危険な拠点はどこであるか。\",\"criteria\":{\"north_outpost\":\"北の拠点\",\"south_outpost\":\"南の拠点\"}}";

        private const string RetreatNorth =
            "\"retreat_north\":{\"type\":\"noul\",\"instructions\":\"北の部隊は撤退すべきである。\"}";

        private const string OperationNorthBroken =
            "\"operation_north_broken\":{\"type\":\"noul\",\"instructions\":\"北の拠点が崩れた状態である。\"}";

        private const string InstructionKind =
            "\"instruction_kind\":{\"type\":\"choice\",\"instructions\":\"指示文の種類は次の1つである。複雑な指示はunknownである。\",\"criteria\":{\"focus\":\"攻める\",\"defend\":\"守る\",\"retreat\":\"引く\",\"economy\":\"内政\",\"doctrine\":\"全体方針を変える\",\"unknown\":\"わからない\"}}";

        private const string InstructionDoctrine =
            "\"instruction_doctrine\":{\"type\":\"choice\",\"instructions\":\"全体方針は次の1つである。\",\"criteria\":{\"concentrate\":\"攻め気味・積極的に・押していく\",\"maintain\":\"守り気味・慎重に・維持\",\"none\":\"全部自分でやる・お任せをやめる\",\"unknown\":\"わからない\"}}";

        private const string InstructionTarget =
            "\"instruction_target\":{\"type\":\"choice\",\"instructions\":\"指示文の対象はstateに載っている名前の1つである。\",\"criteria\":{\"north_outpost\":\"北の拠点\",\"south_outpost\":\"南の拠点\",\"my_core\":\"自分のコア\",\"enemy_core\":\"敵のコア\",\"unknown\":\"わからない\"}}";

        private const string InstructionGoal =
            "\"instruction_goal\":{\"type\":\"choice\",\"instructions\":\"指示文の目標は次の1つである。複雑な目標はunknownである。\",\"criteria\":{\"hold\":\"保持\",\"capture\":\"占領\",\"retreat\":\"撤退\",\"unknown\":\"わからない\"}}";

        private const string InstructionLineKind =
            "\"instruction_kind\":{\"type\":\"choice\",\"instructions\":\"指示文の種類は次の1つである。複雑な指示はunknownである。\",\"criteria\":{\"focus\":\"攻める\",\"defend\":\"守る\",\"retreat\":\"引く\",\"economy\":\"内政\",\"line\":\"加工のラインを作る\",\"doctrine\":\"全体方針を変える\",\"unknown\":\"わからない\"}}";

        private const string Outnumbering =
            "\"outnumbering\":{\"type\":\"noul\",\"instructions\":\"myTotalSoldiersがenemy.knownSoldiersAtMostより多い状態である。\"}";

        private const string EnemyNearMyCore =
            "\"enemy_near_my_core\":{\"type\":\"noul\",\"instructions\":\"kindが\\\"core\\\"かつheldByが\\\"me\\\"であるobjectivesのnearestEnemyMetersが40以下である。値または要素がない場合は偽である。\"}";

        private const string OutpostHeldByEnemy =
            "\"outpost_held_by_enemy\":{\"type\":\"noul\",\"instructions\":\"kindが\\\"outpost\\\"のobjectivesに、heldByが\\\"the enemy\\\"である要素が1つ以上ある。\\\"core\\\"、\\\"nobody\\\"、\\\"unknown\\\"は数えない。\"}";

        /// <summary>Compatibility full set, used by direct transport callers and the versioned schema tests.</summary>
        public static string Json => Build(new JevQuestionContext
        {
            OperationConditionNeeded = true,
            InstructionTranslationNeeded = true,
            IncludeComprehension = true
        });

        /// <summary>Builds the smallest valid question object for this request.</summary>
        public static string Build(JevQuestionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var parts = new List<string> { DecisivePoint, DangerousOutpost, RetreatNorth };
            if (context.OperationConditionNeeded) parts.Add(OperationNorthBroken);
            if (context.InstructionTranslationNeeded)
            {
                bool hasSignals = context.SignalDefinitions != null && context.SignalDefinitions.Count != 0;
                bool hasLines = context.AvailableLineNames != null && context.AvailableLineNames.Count != 0;
                parts.Add(hasLines ? (hasSignals ? InstructionKindWithSignalAndLine : InstructionLineKind) : (hasSignals ? InstructionKindWithSignal : InstructionKind));
                if (hasSignals) parts.Add(BuildInstructionSignal(context.SignalDefinitions));
                parts.Add(InstructionTarget);
                parts.Add(InstructionGoal);
                parts.Add(InstructionDoctrine);
                if (hasLines)
                {
                    parts.Add(BuildInstructionLine(context.AvailableLineNames));
                    if (context.RegionNames != null && context.RegionNames.Count != 0)
                        parts.Add(BuildInstructionRegion(context.RegionNames));
                }
            }
            if (context.IncludeComprehension == true)
            {
                parts.Add(Outnumbering);
                parts.Add(EnemyNearMyCore);
                parts.Add(OutpostHeldByEnemy);
            }
            return "{" + string.Join(",", parts) + "}";
        }

        private const string InstructionKindWithSignal =
            "\"instruction_kind\":{\"type\":\"choice\",\"instructions\":\"指示文の種類は次の1つである。複雑な指示はunknownである。\",\"criteria\":{\"focus\":\"攻める\",\"defend\":\"守る\",\"retreat\":\"引く\",\"economy\":\"内政\",\"doctrine\":\"全体方針を変える\",\"signal\":\"戦術の合図を送る\",\"unknown\":\"わからない\"}}";

        private const string InstructionKindWithSignalAndLine =
            "\"instruction_kind\":{\"type\":\"choice\",\"instructions\":\"指示文の種類は次の1つである。複雑な指示はunknownである。\",\"criteria\":{\"focus\":\"攻める\",\"defend\":\"守る\",\"retreat\":\"引く\",\"economy\":\"内政\",\"line\":\"加工のラインを作る\",\"doctrine\":\"全体方針を変える\",\"signal\":\"戦術の合図を送る\",\"unknown\":\"わからない\"}}";

        private static string BuildInstructionLine(IReadOnlyList<string> lines)
        {
            var criteria = new List<string>();
            foreach (string line in lines ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(line)) continue;
                string label = line == nameof(ProcessingLineKind.CoreMetal) ? "金属" :
                    line == nameof(ProcessingLineKind.Steel) ? "鋼" :
                    line == nameof(ProcessingLineKind.CoreWood) ? "木材" :
                    line == nameof(ProcessingLineKind.BowGear) ? "弓具" : line;
                criteria.Add(JsonString(line) + ":" + JsonString(label));
            }
            criteria.Add("\"unknown\":\"わからない\"");
            return "\"instruction_line\":{\"type\":\"choice\",\"instructions\":\"作る加工のラインは次の1つである。\",\"criteria\":{" + string.Join(",", criteria) + "}}";
        }

        private static string BuildInstructionRegion(IReadOnlyList<string> regions)
        {
            var criteria = new List<string>();
            foreach (string region in regions ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(region)) criteria.Add(JsonString(region) + ":" + JsonString(region));
            criteria.Add("\"unknown\":\"わからない\"");
            return "\"instruction_region\":{\"type\":\"choice\",\"instructions\":\"ラインを置く区域はstateの名前表にある区域の1つである。区域がなければ場所はお任せである。\",\"criteria\":{" + string.Join(",", criteria) + "}}";
        }

        private static string BuildInstructionSignal(IReadOnlyList<AiTacticSignalInfo> signals)
        {
            var criteria = new List<string>();
            foreach (var signal in signals)
            {
                if (signal == null || string.IsNullOrEmpty(signal.Name)) continue;
                criteria.Add(JsonString(signal.Name) + ":" + JsonString(signal.Label ?? signal.Name));
            }
            criteria.Add("\"unknown\":\"わからない\"");
            return "\"instruction_signal\":{\"type\":\"choice\",\"instructions\":\"送る戦術の合図は次の1つである。\",\"criteria\":{" + string.Join(",", criteria) + "}}";
        }

        private static string JsonString(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        /// <summary>Returns the question keys, in the same order as the JSON object.</summary>
        public static IReadOnlyList<string> Names(string questionsJson)
        {
            if (questionsJson == null) throw new ArgumentNullException(nameof(questionsJson));
            var root = MiniJson.Parse(questionsJson) as Dictionary<string, object>
                ?? throw new FormatException("The questions are not an object.");
            return new List<string>(root.Keys);
        }

        /// <summary>Builds the local model's strict answer schema for only the selected questions.</summary>
        public static string AnswerSchema(string questionsJson)
        {
            var sb = new StringBuilder("{\"type\":\"object\",\"properties\":{\"answers\":{\"type\":\"object\",\"properties\":{");
            bool first = true;
            foreach (string name in Names(questionsJson))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('\"').Append(name).Append("\":").Append(Answer);
            }
            return sb.Append("},\"additionalProperties\":false}},\"required\":[\"answers\"],\"additionalProperties\":false}").ToString();
        }

        private const string Answer = "{\"type\":\"object\",\"properties\":{\"choice\":{\"type\":\"string\"},\"confidence\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1},\"noul\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1},\"score\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}},\"additionalProperties\":false}";
    }
}
