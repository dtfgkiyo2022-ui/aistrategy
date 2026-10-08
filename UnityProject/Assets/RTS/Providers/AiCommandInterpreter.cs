using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>固定語彙を返す参謀のJSON契約。LLMにはこのSchemaをそのまま渡せる。</summary>
    public static class AiCommandSchema
    {
        // Written for the strict modes of both Claude (strict tools) and OpenAI (strict json_schema): every property is
        // required and an optional one allows null, and there are no minimum/maximum (Claude rejects them on integers).
        // Ranges are checked by the interpreter instead (count 1-100, permille 0-1000, sequence >= 1).
        private const string LegacyJson = @"{
  ""type"": ""object"", ""additionalProperties"": false,
  ""required"": [""commands"", ""say"", ""reason"", ""unknown""],
  ""properties"": {
    ""commands"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"", ""additionalProperties"": false,
      ""required"": [""type"", ""kind"", ""scope"", ""goal"", ""region"", ""building"", ""location"", ""producer"", ""unit"", ""civ"", ""policy"", ""control"", ""sequence"", ""count"", ""reservePermille"", ""allowedLossPermille""],
      ""properties"": {
        ""type"": { ""type"": ""string"", ""enum"": [""policy"", ""economy""] },
        ""kind"": { ""type"": ""string"", ""enum"": [""Focus"", ""Defend"", ""AllowAbandon"", ""Retreat"", ""MaintainReserve"", ""Scout"", ""ReturnToAuto"", ""SetRegionControl"", ""SetEconomyPolicy"", ""AdvanceAge"", ""PlaceBuilding"", ""Train"", ""CancelTrain""] },
        ""scope"": { ""type"": [""string"", ""null""] }, ""goal"": { ""type"": [""string"", ""null""] },
        ""region"": { ""type"": [""string"", ""null""] }, ""building"": { ""type"": [""string"", ""null""] },
        ""location"": { ""type"": [""string"", ""null""] }, ""producer"": { ""type"": [""string"", ""null""] },
        ""unit"": { ""type"": [""string"", ""null""] }, ""civ"": { ""type"": [""string"", ""null""] },
        ""policy"": { ""type"": [""string"", ""null""] }, ""control"": { ""anyOf"": [ { ""type"": ""string"", ""enum"": [""Human"", ""Ai""] }, { ""type"": ""null"" } ] },
        ""sequence"": { ""type"": [""integer"", ""null""] }, ""count"": { ""type"": [""integer"", ""null""] },
        ""reservePermille"": { ""type"": [""integer"", ""null""] }, ""allowedLossPermille"": { ""type"": [""integer"", ""null""] }
      }
    } },
    ""say"": { ""type"": ""string"" }, ""reason"": { ""type"": [""string"", ""null""] },
    ""unknown"": { ""type"": ""boolean"" }
  }
}";

        public static string Json => Build(null, AiModelCatalog.DefaultComplexLimits);

        public static string Build(AiSituationSummary summary, AiModelPrice limits)
        {
            limits = limits ?? AiModelCatalog.DefaultComplexLimits;
            if (!limits.SupportsComplexInstructions) return BuildSmall(summary);
            var names = summary == null ? Array.Empty<AiNameTableEntry>() : summary.NameTable ?? Array.Empty<AiNameTableEntry>();
            var scopes = names.Where(n => n.HasScope && n.IsOwn).Select(n => n.Name);
            var goals = names.Where(n => n.HasGoal).Select(n => n.Name);
            var regions = names.Where(n => n.HasScope && n.Scope.Kind == ScopeKind.Region && n.IsOwn).Select(n => n.Name);
            var tacticParams = summary == null || summary.TacticInfo == null ? Array.Empty<string>() : summary.TacticInfo.Parameters.Select(p => p.Name);
            var tacticSignals = summary == null || summary.TacticInfo == null ? Array.Empty<string>() : summary.TacticInfo.Signals.Select(p => p.Name);
            var tactics = summary == null || summary.TacticInfo == null ? Array.Empty<string>() : summary.TacticInfo.AvailableNames;
            // Keep dynamic enums semantic: army/region names are not training producers, and every name does not
            // need to be repeated as a building location. This is both smaller and easier for small models to select.
            var producers = names.Where(n => n.Category == "建物").Select(n => n.Name).Concat(new[] { "コア" });
            var locations = names.Where(n => n.HasGoal || n.Category == "地点").Select(n => n.Name).Concat(new[] { "お任せ" });
            var command = CommandSchema(scopes, goals, regions, producers, locations, tacticParams, tacticSignals, tactics);
            var commandRef = new Dictionary<string, object> { ["$ref"] = "#/$defs/command" };
            var condition = ConditionSchema(goals);
            var operation = new Dictionary<string, object>
            {
                ["type"] = "object", ["additionalProperties"] = false,
                ["required"] = new List<object> { "when", "then", "once" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["when"] = condition,
                    ["then"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = commandRef },
                    ["once"] = new Dictionary<string, object> { ["type"] = "boolean" }
                }
            };
            var rootProperties = new Dictionary<string, object>
            {
                ["commands"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = commandRef },
                ["operations"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = operation },
                ["say"] = new Dictionary<string, object> { ["type"] = "string" },
                ["reason"] = NullableString(), ["unknown"] = new Dictionary<string, object> { ["type"] = "boolean" }
            };
            return JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["type"] = "object", ["additionalProperties"] = false,
                ["required"] = new List<object> { "commands", "operations", "say", "reason", "unknown" },
                ["properties"] = rootProperties,
                ["$defs"] = new Dictionary<string, object> { ["command"] = command }
            });
        }

        /// <summary>
        /// The contract for one-order models. It intentionally has no command array, operation object, nullable
        /// types, or range keywords: the response interpreter turns the selected vocabulary into the normal command
        /// shape before applying the G-1 validation.
        /// </summary>
        public static string BuildSmall(AiSituationSummary summary)
        {
            var names = summary == null ? Array.Empty<AiNameTableEntry>() : summary.NameTable ?? Array.Empty<AiNameTableEntry>();
            var scopes = names.Where(n => n.HasScope && n.IsOwn).Select(n => n.Name);
            var goals = names.Where(n => n.HasGoal).Select(n => n.Name);
            var regions = names.Where(n => n.HasScope && n.Scope.Kind == ScopeKind.Region && n.IsOwn).Select(n => n.Name);
            var properties = new Dictionary<string, object>
            {
                ["kind"] = EnumSchema("Focus", "Defend", "AllowAbandon", "Retreat", "ReturnToAuto", "SetRegionControl", "SetDoctrine", "SetTacticParam", "SwitchTactic", "SendTacticSignal", "unknown"),
                ["scope"] = NullableEnum(scopes),
                ["goal"] = NullableEnum(goals),
                ["region"] = NullableEnum(regions),
                ["control"] = NullableEnum(new[] { "Human", "Ai" }),
                ["doctrine"] = NullableEnum(new[] { "none", "maintain", "concentrate" }),
                ["tacticParam"] = NullableEnum(summary == null || summary.TacticInfo == null ? Array.Empty<string>() : summary.TacticInfo.Parameters.Select(p => p.Name)),
                ["tacticParamValue"] = new Dictionary<string, object> { ["type"] = "string" },
                ["tacticSignal"] = NullableEnum(summary == null || summary.TacticInfo == null ? Array.Empty<string>() : summary.TacticInfo.Signals.Select(p => p.Name)),
                ["tacticSignalX"] = new Dictionary<string, object> { ["type"] = "number" },
                ["tacticSignalZ"] = new Dictionary<string, object> { ["type"] = "number" },
                ["tactic"] = NullableEnum(summary == null || summary.TacticInfo == null ? Array.Empty<string>() : summary.TacticInfo.AvailableNames),
                ["reason"] = new Dictionary<string, object> { ["type"] = "string" }
            };
            return JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["type"] = "object", ["additionalProperties"] = false,
                ["required"] = properties.Keys.Select(k => (object)k).ToList(),
                ["properties"] = properties
            });
        }

        private static Dictionary<string, object> CommandSchema(IEnumerable<string> scopes, IEnumerable<string> goals,
            IEnumerable<string> regions, IEnumerable<string> producers, IEnumerable<string> locations,
            IEnumerable<string> tacticParams, IEnumerable<string> tacticSignals, IEnumerable<string> tactics)
        {
            var props = new Dictionary<string, object>
            {
                ["type"] = EnumSchema("policy", "economy", "doctrine", "tactic"),
                ["kind"] = EnumSchema("Focus", "Defend", "AllowAbandon", "Retreat", "MaintainReserve", "Scout", "ReturnToAuto", "SetRegionControl", "SetEconomyPolicy", "AdvanceAge", "PlaceBuilding", "Train", "CancelTrain", "SetTacticParam", "SwitchTactic", "SendTacticSignal", ""),
                ["scope"] = NullableEnum(scopes, "全部隊"), ["goal"] = NullableEnum(goals), ["region"] = NullableEnum(regions),
                ["building"] = NullableEnum(new[] { "兵舎", "鉱山", "溶鉱炉", "農場", "住居", "資源拠点", "壁", "塔", "鍛冶場", "市場", "攻城工房", "射手育成所", "騎兵育成所", "城", "支城", "Barracks", "Mine", "Smelter", "Farm", "House", "DropSite", "Wall", "Tower", "Blacksmith", "Market", "SiegeWorkshop", "ArcheryRange", "Stable", "Castle", "Town" }),
                ["location"] = NullableEnum(locations), ["producer"] = NullableEnum(producers),
                ["unit"] = NullableEnum(new[] { "歩兵", "斥候", "村人", "弓兵", "騎兵", "破城槌", "傭兵", "僧侶", "重歩兵", "散兵", "軽騎兵", "Infantry", "Scout", "Villager", "Archer", "Cavalry", "Ram", "Mercenary", "Monk", "HeavyInfantry", "SkirmishArcher", "LightCavalry" }),
                ["civ"] = NullableEnum(new[] { "原始", "農耕", "冶金", "森林", "石工", "商業", "騎兵", "橋梁", "学術", "信仰", "漁業", "山岳", "関所", "都市", "聖域", "Primitive", "Agrarian", "Metallurgy", "Forestry", "Masonry", "Caravan", "Cavalry", "Bridge", "Academy", "Cult", "Fishing", "Mountain", "Tollgate", "Metropolis", "Sanctuary" }),
                ["policy"] = NullableEnum(new[] { "軍事", "内政", "経済", "成長", "均衡", "バランス", "Military", "Growth", "Balanced" }),
                ["preset"] = NullableEnum(new[] { "none", "maintain", "concentrate" }),
                ["control"] = NullableEnum(new[] { "Human", "Ai" }), ["sequence"] = NullableInteger(), ["count"] = NullableInteger(),
                ["reservePermille"] = NullableInteger(), ["allowedLossPermille"] = NullableInteger(), ["enabled"] = NullableBoolean(),
                ["tacticParam"] = NullableEnum(tacticParams), ["tacticParamValue"] = new Dictionary<string, object> { ["type"] = "string" },
                ["tacticSignal"] = NullableEnum(tacticSignals), ["tacticSignalX"] = new Dictionary<string, object> { ["type"] = "number" },
                ["tacticSignalZ"] = new Dictionary<string, object> { ["type"] = "number" }, ["tactic"] = NullableEnum(tactics)
            };
            return new Dictionary<string, object> { ["type"] = "object", ["additionalProperties"] = false,
                ["required"] = props.Keys.Select(k => (object)k).ToList(), ["properties"] = props };
        }

        private static Dictionary<string, object> ConditionSchema(IEnumerable<string> objectives)
        {
            var leaf = new Dictionary<string, object>();
            var condition = new Dictionary<string, object>();
            FillConditionSchema(leaf, objectives, null);
            FillConditionSchema(condition, objectives, leaf);
            return condition;
        }

        private static void FillConditionSchema(Dictionary<string, object> condition, IEnumerable<string> objectives, Dictionary<string, object> child)
        {
            var properties = new Dictionary<string, object>
            {
                ["kind"] = EnumSchema("OwnerChangedToEnemy", "OwnerChangedToSelf", "EnemyNear", "OwnArmyBelowPercent", "TimeAfter", "JudgementTrue", "And"),
                ["objective"] = NullableEnum(objectives), ["count"] = NullableInteger(), ["permille"] = NullableInteger(),
                ["minutes"] = NullableInteger(), ["statement"] = NullableEnum(new[] { "operation_north_broken" }),
            };
            // A leaf condition has no "all"; the top level holds its parts in an array (empty when it is not an AND).
            if (child != null) properties["all"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = child };
            condition["type"] = "object"; condition["additionalProperties"] = false;
            condition["required"] = properties.Keys.Select(k => (object)k).ToList(); condition["properties"] = properties;
        }

        private static Dictionary<string, object> EnumSchema(params string[] values) => new Dictionary<string, object> { ["type"] = "string", ["enum"] = values.Distinct(StringComparer.Ordinal).Cast<object>().ToList() };
        // "None" is written without union types: an empty string for text, 0 for numbers, false for flags. Claude's strict
        // schemas allow at most 16 parameters with type arrays or anyOf (2026-10-05: 43 were rejected with HTTP 400), and
        // the command object alone has 17 optional fields. The interpreter reads "" and 0 as "not given".
        private static Dictionary<string, object> NullableEnum(IEnumerable<string> values, params string[] extra)
        {
            var all = (values ?? Array.Empty<string>()).Concat(extra ?? Array.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).Cast<object>().ToList();
            all.Add("");
            return new Dictionary<string, object> { ["type"] = "string", ["enum"] = all };
        }
        private static Dictionary<string, object> NullableString() => new Dictionary<string, object> { ["type"] = "string" };
        private static Dictionary<string, object> NullableInteger() => new Dictionary<string, object> { ["type"] = "integer" };
        private static Dictionary<string, object> NullableBoolean() => new Dictionary<string, object> { ["type"] = "boolean" };

        // Keep the stable part before the per-match schema and situation so provider prompt caches can reuse it.
        public const string StableInstructions = @"
この契約では「なし」を、文字の項目は空文字""""、数の項目は0、真偽の項目はfalseで表します（nullは使いません）。以下の説明の「null」は、この意味に読み替えてください。
あなたはRTSゲームの参謀です。人間の日本語の指示を、実行可能な命令のJSONに変換してください。
返答は必ず指定されたJSON Schemaに従うJSONだけにしてください。説明文、Markdown、コードフェンス、Schemaにないキーは返さないでください。

基本規則
1. commandsは実行する命令を順番に並べます。指示の意味が明確で、現在の戦況で実行できるものだけを出します。
2. scopeは動かす側です。指定できる値は名前表にある「全部隊」「第N軍」「北軍」「南軍」「斥候」「予備」などの部隊、拠点、区域です。
   方針命令ではscopeが、どの自軍の部隊・拠点・区域を操作するかを表します。scopeに敵の対象や名前表にない語を入れてはいけません。
3. goalは目標の場所です。拠点、コア、地点など、名前表に目標として載っている文字列だけを使います。目標が不要な命令ではnullにします。
4. regionは区域の担当または区域ごとの内政方針を変更するときだけ使います。区域を操作する命令以外ではnullにします。
5. buildingは建てる建物の種類、locationは建てる場所、producerは訓練元の建物、unitは作る兵種、civは進める文明、policyは内政方針です。該当しない項目はnullにします。
6. controlは区域の担当です。Humanは人間に任せる、AiはAIに任せる、該当しないときはnullです。
7. sequenceは通常nullで、commandsの順番に従います。countは訓練数または建設数で、1以上100以下の整数です。reservePermilleとallowedLossPermilleは0以上1000以下の千分率です。
8. 「ここ」「この部隊」などの指示語は、入力の選択中の対象が明示されている場合だけそれを使います。選択中の対象がないのに指示語だけで対象を指定していたらunknown=trueにします。
9. 「今どっちが優勢？」「敵はどこ？」のような質問は命令ではありません。commands=[]、unknown=true、reasonに「質問」と書きます。観測できないことを推測して命令にしません。
10. 曖昧、対象不明、敵の物を操作、未対応の操作は、勝手に補わずunknown=trueにします。commandsとoperationsは空配列にし、reasonを短く書きます。
11. 条件付きの作戦はoperationsに入れます。whenは固定語彙の条件オブジェクト、thenはcommandsと同じ命令の配列、onceは一度だけならtrueです。whenの使わない項目はnull、ANDはallに条件を2つまで入れます。
12. 文章に複数の独立した命令があるときは、各命令をcommandsに入れます。順番に意味がある場合は文の順番を保ちます。ひとつでも対象が不明なら、推測で一部だけ実行せずunknown=trueにします。
13. 戦術のつまみを変えるときはtype=tactic、kind=SetTacticParam、tacticParamに名前、tacticParamValueに文字列の値を入れます。戦術を替えるときはtype=tactic、kind=SwitchTactic、tacticに候補名を入れます。人から戦術へ合図を送るときはtype=tactic、kind=SendTacticSignal、tacticSignalに名前を入れます。地点が必要な合図はtacticSignalXとtacticSignalZにメートル座標を入れ、不要なときは0にします。空文字は戦術なし／合図なしです。これらは具体的な命令やSetDoctrineと同時に出しません。
14. 自軍の戦術に合図があり（「自軍の戦術:」の「合図=」）、指示がその合図の表示名や意味に当たるときは、具体的な命令ではなく必ずSendTacticSignalを出します。たとえば合図「allIn（総攻撃）」があれば「総攻撃して」「全軍で攻めて」はtacticSignal=allIn、「下がれ」「押し込め」も同様です。目標が書かれていない攻撃・撤退の指示でも、当たる合図があればunknownにしません。

項目の使い分け
- policy型のkindはFocus（攻撃・向かわせる）、Defend（守る）、AllowAbandon（放棄を許可）、Retreat（退く）、MaintainReserve（予備を残す）、Scout（偵察）、ReturnToAuto（自動方針に戻す）です。
- Focus/Defend/Scoutには通常goalが必要です。Retreat、AllowAbandon、MaintainReserve、ReturnToAutoではgoalはnullです。
- economy型のkindはSetRegionControl、SetEconomyPolicy、AdvanceAge、PlaceBuilding、Train、CancelTrainです。区域担当を変えるときはregionとcontrolを使います。
- 全体の内政方針はregion=null、区域ごとの内政方針はregionに区域名を入れます。policyはMilitary（軍事）、Growth（経済・内政重視）、Balanced（均衡）です。入力の「兵の生産を優先」「軍事重視」はMilitary、「村人を増やす」「稼ぎを伸ばす」「内政を優先」はGrowthにします。
 - PlaceBuildingではbuildingとlocationを使います。場所を指定しない「もう一つ建てる」はlocation=お任せです。名前表の場所の近くならその場所名をlocationにします。
- Trainではunitとcountを使います。producerが指定されたときだけ名前表の建物名を使い、指定がない村人の訓練などではproducer=nullにします。CancelTrainはproducerを使います。
- AdvanceAgeではcivを使います。文明が指定されない「次の時代へ」は、現在の文明やルールを確認できないためunknown=trueにします。

日本語名の変換
建物名は日本語でも英語でも受け付けます。兵舎=Barracks、鉱山=Mine、溶鉱炉=Smelter、農場=Farm、住居または家=House、資源拠点=DropSite、壁=Wall、塔=Tower、鍛冶場=Blacksmith、市場=Market、攻城工房=SiegeWorkshop、射手育成所=ArcheryRange、騎兵育成所=Stable、城=Castle、木材所=LumberCamp、石切場=Quarry、町の中心または支城=Townです。
兵種は日本語でも英語でも受け付けます。歩兵=Infantry、斥候=Scout、村人=Villager、弓兵=Archer、騎兵=Cavalry、破城槌=Ram、傭兵=Mercenary、僧侶=Monk、重歩兵=HeavyInfantry、散兵=SkirmishArcher、軽騎兵=LightCavalryです。
文明は日本語でも英語でも受け付けます。原始=Primitive、農耕=Agrarian、冶金=Metallurgy、森林=Forestry、石工=Masonry、商業=Caravan、騎兵=Cavalry、橋梁=Bridge、学術=Academy、信仰=Cult、漁業=Fishing、山岳=Mountain、関所=Tollgate、都市=Metropolis、聖域=Sanctuaryです。
内政方針は軍事=Military、兵站または内政=Growth、経済=Growth、均衡=Balancedです。JSONには必ず英語のenum値を入れます。

名前表の扱い
名前表の文字列はゲーム画面で表示される呼び方です。第N軍のNは自軍部隊IDを小さい順に並べた番号であり、IDそのものではありません。北軍・南軍・斥候・予備などの別名が表にあるときは、同じ行の同じ対象として扱います。
区域は区域N、建物は種類の日本語名と種類ごとの番号（兵舎1、住居1など）、資源の固まりは方角付き（東の資源の固まり）、支城に対応する区域は支城の区域です。表にない地名、敵の第2軍、存在しない西の拠点を作ってはいけません。

判定手順
最初に、入力が命令か質問かを判定します。疑問符がない場合でも、「どこ」「何体」「どちらが優勢」など情報を求める文は質問です。質問には推測で答えず、unknown=trueとします。
次に、文の中から操作対象、目標、操作の種類、数、担当、場所、条件を分けて読み取ります。「守る」はDefend、「攻める」「向かう」「取りに行く」はFocus、「下がる」「戻る」はRetreat、「任せる」は文脈に応じてReturnToAutoまたは区域のAi、「自分でやる」は区域のHumanです。
「捨てていい」「放棄していい」はAllowAbandonであり、直ちに部隊を消す命令ではありません。「予備を残す」はMaintainReserveで、reservePermilleに千分率を入れます。2割は200、半分は500、全部は1000です。
「見てきて」「偵察」はScoutです。偵察する部隊が名前表にない、または目標が名前表にない場合は実行しません。「総攻撃」はFocusに変換しますが、敵の名前をscopeにしてはいけません。敵を目標にできるのはgoalだけです。
「守備重視」「ここを守る」はDefendです。守る範囲と守る場所を混同しないでください。全軍で守る場合はscope=全部隊、部隊で守る場合はscope=その部隊です。区域を守る場合はscope=区域N、区域の中心が名前表にあるときだけgoalにその中心を入れます。
部隊名の数字は画面の番号です。戦況にID=7の部隊が一つだけあっても、それは第1軍です。名前表の「北軍」が第1軍の別名なら、第1軍と北軍を別の部隊として二重に命令しません。複数の別名が一つの対象を表す場合、それらを一つの対象として扱います。
区域の番号は地図の区域IDであり、部隊番号とは別です。区域3の担当変更ではscopeではなくregion=区域3を使い、control=HumanまたはAiを使います。区域の内政方針ではregion=区域3、policy=Military/Growth/Balancedです。
建設は建物の種類と場所を分けます。「塔を北側に」はbuilding=Tower、location=北側に対応する名前表の地点です。「もう一つ建てる」「空いている場所に建てる」はlocation=お任せです。お任せ配置はシステムが安全なセルを探すため、セル番号を推測して書きません。
建物の番号は種類ごとに数えます。兵舎1と住居1は別の建物です。訓練元が兵舎とだけ言われたとき、名前表に兵舎がなく兵舎1だけがあるなら、兵舎1を使います。候補が複数あり区別できないときはunknown=trueにします。
訓練数の表現は数字だけでなく、「一体」「二人」「3体」「5人」のような日本語も解釈できます。数がないときは通常1ですが、上限100を越える要求はunknownまたは命令を拒否します。負数、小数、極端な数を丸めてはいけません。
「村人を作る」はunit=Villagerです。村人の訓練元が明記されていない場合はproducer=nullにします。「兵舎で歩兵」はproducerを名前表から解決します。名前表にない建物を訓練元にしません。取り消しはCancelTrainで、現在の訓練を取り消す対象の建物をproducerにします。
文明の日本語名は、ゲームの文明名対応表に従って英語のenum値へ変換します。冶金とMetallurgy、石工とMasonryを混同しません。文明名の指定がなければ現在の文明を推測せず、次の時代へという命令をunknownにします。
内政方針の「兵」「軍事」「戦力」はMilitary、「村人」「稼ぎ」「内政」「経済」はGrowth、「均衡」「バランス」はBalancedです。「建物を建てる」「兵を訓練する」は内政方針変更ではなく、PlaceBuildingまたはTrainです。
普通の文体、短い電文、ひらがな、言い直し、言いよどみは意味が一つに定まれば同じ命令にします。言い直しでは最後の明確な内容を使います。例えば前半の場所を後半で訂正している場合、訂正前と訂正後の二つの命令を出しません。
複数命令では各命令のscopeとgoalを個別に埋めます。「第2軍と第3軍」は二つのFocus命令に分けます。「Aを守りつつBで攻める」は同時に実行可能なら二命令に分けます。ただし条件文や曖昧な代名詞が含まれる場合は全体をunknownにします。
許可される操作は、この前置きとkindの一覧にあるものだけです。チャット送信、ファイル操作、設定変更、相手への挑発、装備の交換、兵種の変換、ゲーム外の操作は未対応です。似た命令へ勝手に置き換えません。
対象の所有者が不明な場合は自分の物として扱いません。敵の部隊、敵の拠点、敵コアをscopeにする命令は拒否します。敵コアへ攻撃する場合だけ、敵コアをgoalに置き、scopeは自軍の全部隊または自軍部隊にします。
霧の中の敵や戦況に記載されていない対象について、位置、数、所有者を推測しません。見えている敵の報告を求められたときも、この契約では命令を作らず質問として扱います。観測事実と人間の希望を混ぜないでください。
選択中の対象は指示文の前に別行で示されます。選択中の対象が「北の拠点」なら「ここを守れ」は全軍が北の拠点を守る命令になります。選択中の対象が第3軍なら「この部隊を下げて」は第3軍のRetreatです。選択中の対象がなしなら、同じ文は対象不明として断ります。
選択中の対象が区域なら、区域の担当変更ではregionにその区域を入れます。選択中の対象が建物なら、訓練の取り消しではproducerに建物名を入れます。選択対象の種類と命令の種類が合わない場合、無理にscopeやgoalへ流用しません。
reasonは人間が読んで分かる短い理由です。成功時はnullまたは空文字でよく、unknown時は「曖昧」「対象が不明」「自分の物でない」「対応していない」「質問」など原因を明示します。sayは短い確認文で、命令の代わりにしません。
unknown=trueのときcommandsは必ず空配列にします。unknown=falseのときは少なくとも意味のあるcommandsを一つ返すか、命令が空である理由をsay/reasonに示します。成功した一部だけを出して残りを黙って捨てません。
JSONのすべてのrequired項目を出します。命令ごとに使わない項目はnullです。整数を文字列にせず、true/falseを文字列にせず、nullを「なし」という文字列にしません。commandsの外に命令を置きません。
出力前に、typeとkindの組み合わせ、名前表への一致、所有者、goalの必要性、regionとcontrolの組み合わせ、建物・兵・文明・policyのenum変換、数の範囲、unknownとcommandsの整合性を順に確認してください。
特に、scopeとgoalは役割が違います。北の拠点を守るならscope=全部隊、goal=北の拠点です。北軍で守るならscope=北軍、goal=北の拠点です。南軍を北へ向かわせるならscope=南軍、goal=北の拠点です。目標をscopeに入れたり、動かす部隊をgoalに入れたりしません。
特に、区域の命令は三種類を区別します。区域3を人間担当にするのはSetRegionControl、区域3の内政を軍事重視にするのはSetEconomyPolicy、区域3を守るのはpolicy型のDefendです。最初の二つはeconomy型で、三つ目はpolicy型です。
全体方針の命令も出せます。攻め気味・積極的に・押していくはtype=doctrine、preset=concentrate、守り気味・慎重に・維持はpreset=maintain、全部自分でやる・お任せをやめるはpreset=noneです。具体的な命令と同時に出してもかまいません。全体方針以外の命令ではpresetを空文字にします。sayで方針を変えたと言うときは、必ずcommandsにtype=doctrineの命令を入れます。sayだけで済ませません。
戦術の合図も同じです。「自軍の戦術:」の「合図=」に当たる合図があるときは、総攻撃・押し込め・下がれ・撤退などもFocusやRetreatではなく、必ずcommandsにtype=tactic、kind=SendTacticSignalの命令を入れます。sayで合図を送ると言うときもsayだけで済ませません。
特に、「任せる」は対象によって意味が変わります。全部隊や部隊ならReturnToAuto、区域ならSetRegionControlでcontrol=Aiです。内政全体を任せるという文だけでは、既存の自動内政へ戻す操作か方針変更かを区別できない場合があるため、文の対象を確認します。
特に、放棄の許可と撤退は違います。拠点を捨ててよいはAllowAbandonで、部隊が下がるはRetreatです。予備の保持と部隊の撤退も違うため、保持割合はMaintainReserveのreservePermilleにだけ入れます。
特に、場所の「近く」は目標goalではなくPlaceBuildingのlocationです。支城や資源の固まりの近くに建てる場合、その地点が名前表にあるときだけlocationに使います。名前表にない方角や距離からセル番号を作りません。
特に、同じ文に数と命令がある場合、countはTrainまたはPlaceBuildingにだけ使います。MaintainReserveの「2割」はreservePermille=200であり、count=2ではありません。allowedLossの半分はallowedLossPermille=500です。
特に、条件付きの命令は現在の命令へ単純化せず、固定語彙に変換できるときだけoperationsへ保存します。条件が自由文で、whenのkind・値に対応しない場合はunknown=trueにします。
最後に、返すJSONを読み直し、すべての文字列が名前表または指定されたenum語彙に合っていることを確認してください。人間向けの説明をJSONの外に付けず、sayが必要なときもJSONのsay文字列に入れてください。
入力の敬語、命令形、体言止めは同じ意味として扱えますが、意味を追加しません。文にない数、方向、対象、条件、所有者を補いません。複数候補から一つを選ぶ必要がある場合は、名前表と戦況で一意に決まるときだけ選び、決まらなければunknown=trueにします。
表現の揺れがあっても、JSONのkindは必ず指定された英語の値に統一します。日本語名を受け付けるのはbuilding、unit、civ、policyなどの値の読み取りであり、返すJSONの種類名を日本語にすることではありません。
値がnullの項目を省略しないでください。strictな構造化出力では省略された項目を受け取れないため、不要な項目にもnullを入れます。commandsが空のときもsay、reason、unknownを必ず返します。
この確認は、短い指示でも長い計画文でも同じです。戦況の後ろにある最新の名前表と選択対象を優先し、古い一般論や推測よりも現在のデータを優先します。
現在の戦況にない情報を前置きの例から持ち込まず、例は書式と判断の境界を示すだけのものとして扱います。実際の対象は必ず直後の名前表で照合します。

返答の例（以下は形式の説明用で、現在の戦況の命令を先取りするものではありません）
例1: 自軍のコアを守る → policy / Defend / scope=全部隊 / goal=自軍コア / その他はnull
例2: 第1軍を北の拠点へ集める → policy / Focus / scope=第1軍 / goal=北の拠点 / その他はnull
例3: 斥候を南の区域へ偵察に出す → policy / Scout / scope=斥候 / goal=区域2 / その他はnull
例4: 第2軍は撤退 → policy / Retreat / scope=第2軍 / goal=null / その他はnull
例5: 区域1を人間担当にする → economy / SetRegionControl / region=区域1 / control=Human / その他はnull
例6: 軍事重視に変える → economy / SetEconomyPolicy / region=null / policy=Military / その他はnull
例7: 住居を3つ建てる → economy / PlaceBuilding / building=House / location=お任せ / count=3 / その他はnull
例8: 兵舎1で弓兵を2体訓練 → economy / Train / producer=兵舎1 / unit=Archer / count=2 / その他はnull
例9: その件はどうなっている？ → commands=[] / unknown=true / reason=質問
例10: もし敵が来たら守る → operations=[when={kind=EnemyNear,objective=北の拠点,count=1,permille=null,minutes=null,statement=null,all=null},then=[Defend],once=true]
例11: 兵が半分になったら撤退 → operations=[when={kind=OwnArmyBelowPercent,objective=null,count=null,permille=500,minutes=null,statement=null,all=null},then=[Retreat],once=true]

以上の前置きの後に続く戦況、名前表、選択中の対象、指示を読み、JSONだけを返してください。
";

        public const string StableInstructionsShort = @"
あなたはRTSの参謀です。指示を1つの命令にします。指定されたJSON Schemaに従うJSONだけを返してください。
kindはFocus（向かわせる）、Defend（守る）、AllowAbandon（放棄を許可）、Retreat（退く）、ReturnToAuto（自動に戻す）、SetRegionControl（区域の担当を変える）、unknown（不明）です。scopeは動かす側、goalは目標の場所です。SetRegionControlではregionが区域名、controlがHumanまたはAiです。不要な項目は空文字にしてください。
全体方針も選べます。攻め気味・積極的に・押していくはkind=SetDoctrine、doctrine=concentrate、守り気味・慎重に・維持はdoctrine=maintain、全部自分でやる・お任せをやめるはdoctrine=noneです。具体的な命令と同時には出せません。全体方針以外ではdoctrineを空文字にしてください。
戦術を替える命令はkind=SwitchTactic、tacticは候補名（空文字は戦術なし）です。つまみを変える命令はkind=SetTacticParam、tacticParamとtacticParamValueを使います。定義された合図を送る命令はkind=SendTacticSignal、tacticSignalと、必要ならtacticSignalX/tacticSignalZを使います。これらは具体的な命令やSetDoctrineと同時に出せません。
戦術に合図があり、指示がその合図の表示名や意味に当たるとき（例：合図「allIn（総攻撃）」に「総攻撃して」）は、目標が書かれていなくても必ずkind=SendTacticSignalにします。
表にない名前の判定はゲーム側が行います。scope と goal は、必ず名前表の中から選んでください。指示文に出てきた名前と同じものを選んでください。曖昧、質問、条件付き、複数の命令もkind=unknownにします。Schemaにないキー、説明、Markdownは返さないでください。
";

        public static string ForModel(string model)
        {
            try
            {
                var limits = AiModelCatalog.Get(model);
                return limits.SupportsComplexInstructions ? StableInstructions : StableInstructionsShort;
            }
            catch (ArgumentException) { return StableInstructions; }
        }
    }

    public sealed class AiCommandInterpretationResult
    {
        /// <summary>An "I could not turn this into orders" result, for callers outside this assembly (the setters are internal).</summary>
        public static AiCommandInterpretationResult NoAnswer(string reason) => new AiCommandInterpretationResult { Unknown = true, Reason = reason ?? "" };

        public IReadOnlyList<UserPolicyIntent> Policies { get; internal set; } = Array.Empty<UserPolicyIntent>();
        public IReadOnlyList<EconomyCommand> EconomyCommands { get; internal set; } = Array.Empty<EconomyCommand>();
        public IReadOnlyList<OperationDefinition> Operations { get; internal set; } = Array.Empty<OperationDefinition>();
        public IReadOnlyList<AiRejectedCommand> Rejected { get; internal set; } = Array.Empty<AiRejectedCommand>();
        public IReadOnlyList<AiTacticCommand> TacticCommands { get; internal set; } = Array.Empty<AiTacticCommand>();
        public string Say { get; internal set; } = "";
        public string Reason { get; internal set; } = "";
        /// <summary>選ばれた自軍の全体方針。全体方針の命令がない場合はnull。</summary>
        public string Doctrine { get; internal set; }
        public bool Unknown { get; internal set; }

        public string Report
        {
            get
            {
                var b = new StringBuilder();
                if (!string.IsNullOrEmpty(Say)) b.Append(Say);
                if (!string.IsNullOrEmpty(Reason)) { if (b.Length != 0) b.Append(" "); b.Append(Reason); }
                foreach (var rejected in Rejected)
                {
                    if (b.Length != 0) b.Append(" ");
                    b.Append("命令を破棄: ").Append(rejected.Reason);
                }
                return b.ToString();
            }
        }
    }

    public sealed class AiTacticCommand
    {
        public string Kind { get; internal set; }
        public string ParamName { get; internal set; }
        public string ParamValue { get; internal set; }
        public string TacticName { get; internal set; }
        public string SignalName { get; internal set; }
        public SimPoint? Point { get; internal set; }
    }

    public sealed class AiRejectedCommand
    {
        public int Index { get; internal set; }
        public string Reason { get; internal set; }
        public string Kind { get; internal set; }
    }

    public sealed class AiNameTableEntry
    {
        public string Name { get; internal set; }
        /// <summary>短い名前表でモデルに示す対象の種類（部隊・拠点・区域・建物など）。</summary>
        public string Category { get; internal set; }
        /// <summary>座標や明細を含めない、現在の短い状態。</summary>
        public string State { get; internal set; }
        public ScopeKey Scope { get; internal set; }
        public bool HasScope { get; internal set; }
        public PolicyGoal Goal { get; internal set; }
        public bool HasGoal { get; internal set; }
        public uint Id { get; internal set; }
        public bool IsOwn { get; internal set; }
        public SimPoint Point { get; internal set; }
    }

    public sealed class AiTacticParameterInfo
    {
        internal AiTacticParameterInfo() { }

        /// <summary>Filled by the match host (another assembly), so it is built through this constructor.</summary>
        public AiTacticParameterInfo(string name, string label, string type, string value,
            decimal? min, decimal? max, decimal? step, IReadOnlyList<string> choices)
        {
            Name = name; Label = label; Type = type; Value = value;
            Min = min; Max = max; Step = step; Choices = choices ?? Array.Empty<string>();
        }

        public string Name { get; internal set; }
        public string Label { get; internal set; }
        public string Type { get; internal set; }
        public string Value { get; internal set; }
        public decimal? Min { get; internal set; }
        public decimal? Max { get; internal set; }
        public decimal? Step { get; internal set; }
        public IReadOnlyList<string> Choices { get; internal set; } = Array.Empty<string>();
    }

    public sealed class AiTacticInfo
    {
        public string CurrentName { get; internal set; } = "";
        public IReadOnlyList<string> AvailableNames { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<AiTacticParameterInfo> Parameters { get; internal set; } = Array.Empty<AiTacticParameterInfo>();
        public IReadOnlyList<AiTacticSignalInfo> Signals { get; internal set; } = Array.Empty<AiTacticSignalInfo>();
    }

    public sealed class AiTacticSignalInfo
    {
        public AiTacticSignalInfo(string name, string label, bool needsPoint)
        { Name = name; Label = label; NeedsPoint = needsPoint; }
        public string Name { get; }
        public string Label { get; }
        public bool NeedsPoint { get; }
    }

    /// <summary>指示文から安全に直接送信できる自軍戦術の合図。</summary>
    public sealed class AiDirectTacticSignal
    {
        public AiDirectTacticSignal(string name, string label, SimPoint? point)
        { Name = name; Label = label; Point = point; }
        public string Name { get; }
        public string Label { get; }
        public SimPoint? Point { get; }
    }

    public static class AiTacticSignalMatcher
    {
        /// <summary>
        /// Returns a signal only when the instruction contains exactly one signal label and no name-table name.
        /// Point-required signals also need a selected name-table goal with a usable point.
        /// </summary>
        public static bool TryMatch(string instruction, AiSituationSummary summary, ScopeKey? selectedTarget,
            out AiDirectTacticSignal match)
        {
            match = null;
            if (string.IsNullOrEmpty(instruction) || summary == null || summary.TacticInfo == null) return false;
            if ((summary.NameTable ?? Array.Empty<AiNameTableEntry>()).Any(x => x != null &&
                !string.IsNullOrEmpty(x.Name) && instruction.Contains(x.Name, StringComparison.Ordinal))) return false;

            var signals = summary.TacticInfo.Signals ?? Array.Empty<AiTacticSignalInfo>();
            var matches = signals.Where(x => x != null && !string.IsNullOrEmpty(x.Label) &&
                instruction.Contains(x.Label, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) return false;
            var signal = matches[0];
            SimPoint? point = null;
            if (signal.NeedsPoint)
            {
                if (!selectedTarget.HasValue) return false;
                var selected = (summary.NameTable ?? Array.Empty<AiNameTableEntry>()).FirstOrDefault(x =>
                    x != null && x.HasScope && x.Scope.Equals(selectedTarget.Value) && x.HasGoal);
                if (selected == null) return false;
                point = selected.Point;
            }
            match = new AiDirectTacticSignal(signal.Name, signal.Label, point);
            return true;
        }
    }

    /// <summary>参謀が見てよい情報だけから作った短い戦況と、その中の名前→ID表。</summary>
    public sealed class AiSituationSummary
    {
        private readonly Dictionary<string, AiNameTableEntry> names = new Dictionary<string, AiNameTableEntry>(StringComparer.Ordinal);
        public uint FactionId { get; private set; }
        public long Tick { get; private set; }
        public string Text { get; private set; }
        public IReadOnlyList<AiNameTableEntry> NameTable { get; private set; }
        public AiTacticInfo TacticInfo { get; private set; } = new AiTacticInfo();

        public void SetTacticInfo(string currentName, IEnumerable<string> availableNames, IEnumerable<AiTacticParameterInfo> parameters)
            => SetTacticInfo(currentName, availableNames, parameters, null);

        public void SetTacticInfo(string currentName, IEnumerable<string> availableNames, IEnumerable<AiTacticParameterInfo> parameters,
            IEnumerable<AiTacticSignalInfo> signals)
        {
            TacticInfo = new AiTacticInfo
            {
                CurrentName = currentName ?? "",
                AvailableNames = (availableNames ?? Array.Empty<string>()).Where(x => x != null).Distinct(StringComparer.Ordinal).ToArray(),
                Parameters = (parameters ?? Array.Empty<AiTacticParameterInfo>()).Where(x => x != null).ToArray(),
                Signals = (signals ?? Array.Empty<AiTacticSignalInfo>()).Where(x => x != null).ToArray()
            };
        }

        public bool TryGet(string name, out AiNameTableEntry entry) => names.TryGetValue(name ?? "", out entry);

        public string NameFor(ScopeKey scope)
        {
            foreach (var entry in NameTable ?? Array.Empty<AiNameTableEntry>())
                if (entry.HasScope && entry.Scope.Equals(scope)) return entry.Name;
            return null;
        }

        public string Prompt(string instruction, ScopeKey? fixedTarget = null)
            => AiCommandSchema.StableInstructions + "\n" + DynamicPrompt(instruction, fixedTarget);

        public string Prompt(string instruction, ScopeKey? fixedTarget, string model)
            => AiCommandSchema.ForModel(model) + "\n" + DynamicPrompt(instruction, fixedTarget, null, IsSmallModel(model));

        public string Prompt(string instruction, string fixedTargetName)
            => AiCommandSchema.StableInstructions + "\n" + DynamicPrompt(instruction, null, fixedTargetName);

        public string Prompt(string instruction, string fixedTargetName, string model)
            => AiCommandSchema.ForModel(model) + "\n" + DynamicPrompt(instruction, null, fixedTargetName, IsSmallModel(model));

        public string Prompt(string instruction, ScopeKey? fixedTarget, string model, string fixedTargetName)
            => AiCommandSchema.ForModel(model) + "\n" + DynamicPrompt(instruction, fixedTarget, fixedTargetName, IsSmallModel(model));

        public string SmallModelPrompt(string instruction, ScopeKey? fixedTarget = null, string fixedTargetName = null)
            => AiCommandSchema.StableInstructionsShort + "\n" + DynamicPrompt(instruction, fixedTarget, fixedTargetName, true);

        public string DynamicPrompt(string instruction, ScopeKey? fixedTarget = null)
            => DynamicPrompt(instruction, fixedTarget, null, false);

        public string DynamicPrompt(string instruction, ScopeKey? fixedTarget, string fixedTargetName)
            => DynamicPrompt(instruction, fixedTarget, fixedTargetName, false);

        private string DynamicPrompt(string instruction, ScopeKey? fixedTarget, string fixedTargetName, bool includeInstructionNames)
        {
            var names = new StringBuilder();
            foreach (var group in CompactNameTable())
            {
                if (names.Length != 0) names.Append('、');
                names.Append(group.Names).Append('[').Append(group.Category);
                if (!string.IsNullOrEmpty(group.State)) names.Append(':').Append(group.State);
                names.Append(']');
            }
            string selected = fixedTargetName;
            if (string.IsNullOrEmpty(selected) && fixedTarget.HasValue) selected = NameFor(fixedTarget.Value);
            string instructionNames = includeInstructionNames ? InstructionNamesHint(instruction) : "";
            return "名前表（この文字列だけを使う）:\n" + names +
                "\n戦況:\n" + Text + "\n選択中の対象：" + (selected ?? (fixedTarget.HasValue ? "不明" : "なし")) +
                "\n自軍の戦術:\n" + TacticPrompt() +
                (string.IsNullOrEmpty(instructionNames) ? "" : "\n" + instructionNames) +
                "\n指示:\n" + (instruction ?? "");
        }

        private string TacticPrompt()
        {
            var info = TacticInfo ?? new AiTacticInfo();
            string current = string.IsNullOrEmpty(info.CurrentName) ? "なし" : info.CurrentName;
            string candidates = info.AvailableNames == null || info.AvailableNames.Count == 0 ? "なし" : string.Join("、", info.AvailableNames);
            string signalText = info.Signals == null || info.Signals.Count == 0 ? "なし" : string.Join("；", info.Signals.Select(s => s.Name + "（" + s.Label + (s.NeedsPoint ? ",地点が必要" : "") + "）"));
            if (info.Parameters == null || info.Parameters.Count == 0)
                return "現在=" + current + "、切替候補=" + candidates + "、つまみ=なし、合図=" + signalText;
            var parameters = info.Parameters.Select(p =>
            {
                string range = p.Type == "choice" ? "選択肢=" + string.Join("/", p.Choices ?? Array.Empty<string>()) :
                    p.Type == "bool" ? "真偽" : "範囲=" + p.Min.Value.ToString(CultureInfo.InvariantCulture) + ".." + p.Max.Value.ToString(CultureInfo.InvariantCulture);
                return p.Name + "（" + p.Label + "）=" + p.Value + "、" + range;
            });
            return "現在=" + current + "、切替候補=" + candidates + "、つまみ=" + string.Join("；", parameters) + "、合図=" + signalText;
        }

        private static bool IsSmallModel(string model)
        {
            try { return !AiModelCatalog.Get(model).SupportsComplexInstructions; }
            catch (ArgumentException) { return false; }
        }

        /// <summary>
        /// 指示文にそのまま出てきた名前表の名前（別名を含む）を、長い名前から順に返します。
        /// 短い別名が長い名前の一部に含まれる場合は、長い名前だけを採用します。
        /// </summary>
        public IReadOnlyList<string> FindInstructionNames(string instruction)
        {
            var text = instruction ?? "";
            var found = new List<Tuple<int, string>>();
            var occupied = new bool[text.Length];
            foreach (var entry in (NameTable ?? Array.Empty<AiNameTableEntry>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.Name))
                .OrderByDescending(e => e.Name.Length)
                .ThenBy(e => e.Name, StringComparer.Ordinal))
            {
                int start = 0;
                while (start < text.Length)
                {
                    int index = text.IndexOf(entry.Name, start, StringComparison.Ordinal);
                    if (index < 0) break;
                    int end = index + entry.Name.Length;
                    bool overlaps = false;
                    for (int i = index; i < end; i++) if (occupied[i]) { overlaps = true; break; }
                    if (!overlaps)
                    {
                        found.Add(Tuple.Create(index, entry.Name));
                        for (int i = index; i < end; i++) occupied[i] = true;
                    }
                    start = end;
                }
            }
            found.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : b.Item2.Length.CompareTo(a.Item2.Length));
            return found.Select(x => x.Item2).ToArray();
        }

        /// <summary>
        /// 名前らしい形は「西の拠点」「第9軍」「区域3」「西軍」のような狭い形だけです。
        /// 一般の名詞や文章を未知名として扱わないため、任意のカタカナ語や形容詞は対象にしません。
        /// </summary>
        public string FindUnknownInstructionName(string instruction)
        {
            var text = instruction ?? "";
            foreach (Match match in Regex.Matches(text,
                @"[\p{IsCJKUnifiedIdeographs}々ー・]{1,12}の拠点|第[0-9０-９一二三四五六七八九十百]+軍|区域[0-9０-９一二三四五六七八九十百]+|[\p{IsCJKUnifiedIdeographs}々ー・]{1,12}軍"))
            {
                string candidate = match.Value;
                int suffix = candidate.LastIndexOf("の拠点", StringComparison.Ordinal);
                if (suffix > 0) candidate = candidate.Substring(Math.Max(0, suffix - 1));
                if (IsCommonWord(candidate)) continue;
                if (!FindInstructionNamesAt(text, match.Index, match.Length, candidate)) return candidate;
            }
            return null;
        }

        // Ordinary words that have the shape of a name but name no single thing (eval 10-05: 「そこに全軍で向かって」 was
        // refused as an unknown name). They go to the model as usual.
        private static readonly string[] CommonWords =
        {
            "全軍", "自軍", "敵軍", "援軍", "友軍", "味方軍", "我軍", "大軍", "本軍", "主力軍",
            "敵の拠点", "自分の拠点", "味方の拠点", "我の拠点", "近くの拠点", "次の拠点", "その拠点", "この拠点", "あの拠点"
        };

        private static bool IsCommonWord(string candidate)
        {
            foreach (var word in CommonWords)
                if (candidate.EndsWith(word, StringComparison.Ordinal)) return true;
            return false;
        }

        private bool FindInstructionNamesAt(string text, int index, int length, string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            bool known = (NameTable ?? Array.Empty<AiNameTableEntry>()).Any(e => e != null && e.Name == candidate);
            if (!known) return false;
            int found = text.IndexOf(candidate, index, StringComparison.Ordinal);
            return found >= index && found < index + length;
        }

        private string InstructionNamesHint(string instruction)
        {
            var mentioned = FindInstructionNames(instruction);
            return mentioned.Count == 0 ? "" : "指示に出てきた名前：" + string.Join("、", mentioned);
        }

        private sealed class CompactNameEntry
        {
            internal string Names;
            internal string Category;
            internal string State;
        }

        private IReadOnlyList<CompactNameEntry> CompactNameTable()
        {
            var result = new List<CompactNameEntry>();
            var byIdentity = new Dictionary<string, CompactNameEntry>(StringComparer.Ordinal);
            foreach (var entry in NameTable ?? Array.Empty<AiNameTableEntry>())
            {
                string identity = entry.HasScope ? "s:" + entry.Scope.FactionId + ":" + (int)entry.Scope.Kind + ":" + entry.Scope.Id
                    : entry.HasGoal ? "g:" + entry.Goal.Kind + ":" + entry.Goal.Id
                    : "i:" + entry.Id + ":" + entry.Category + ":" + entry.State;
                if (!byIdentity.TryGetValue(identity, out var compact))
                {
                    compact = new CompactNameEntry { Names = entry.Name, Category = entry.Category ?? "対象", State = entry.State ?? "" };
                    byIdentity.Add(identity, compact); result.Add(compact);
                }
                else if (compact.Names.IndexOf(entry.Name, StringComparison.Ordinal) < 0)
                    compact.Names += "/" + entry.Name;
            }
            return result;
        }

        /// <summary>Host adapters may add a visible alias without changing its contract identity.</summary>
        public void AddAlias(string alias, string existingName)
        {
            if (string.IsNullOrEmpty(alias) || !TryGet(existingName, out var existing)) return;
            if (names.TryGetValue(alias, out var oldAlias))
            {
                if (oldAlias.Scope.Equals(existing.Scope) && oldAlias.HasScope == existing.HasScope && oldAlias.Id == existing.Id) return;
                names.Remove(alias);
                var withoutAlias = new List<AiNameTableEntry>(NameTable ?? Array.Empty<AiNameTableEntry>());
                withoutAlias.RemoveAll(x => x.Name == alias);
                NameTable = withoutAlias.AsReadOnly();
            }
            var entry = new AiNameTableEntry { Name = alias, Scope = existing.Scope, HasScope = existing.HasScope,
                Goal = existing.Goal, HasGoal = existing.HasGoal, Id = existing.Id, IsOwn = existing.IsOwn, Point = existing.Point,
                Category = existing.Category, State = existing.State };
            names.Add(alias, entry);
            var list = new List<AiNameTableEntry>(NameTable ?? Array.Empty<AiNameTableEntry>()); list.Add(entry); NameTable = list.AsReadOnly();
        }

        public void AddGoalAlias(string alias, PolicyGoal goal, bool own, SimPoint point)
        {
            if (string.IsNullOrEmpty(alias) || names.ContainsKey(alias)) return;
            var entry = new AiNameTableEntry { Name = alias, Goal = goal, HasGoal = true, IsOwn = own, Id = goal.Id, Point = point,
                Category = "地点", State = "既知" };
            names.Add(alias, entry);
            var list = new List<AiNameTableEntry>(NameTable ?? Array.Empty<AiNameTableEntry>()); list.Add(entry); NameTable = list.AsReadOnly();
        }

        /// <summary>評価やHostが、実在する建物を名前表へ追加するときに使う。</summary>
        public void AddProducerName(string name, uint id, SimPoint point, string state = "")
        {
            if (string.IsNullOrEmpty(name) || names.ContainsKey(name)) return;
            var entry = new AiNameTableEntry { Name = name, Id = id, IsOwn = true, Point = point, Category = "建物", State = state ?? "" };
            names.Add(name, entry);
            var list = new List<AiNameTableEntry>(NameTable ?? Array.Empty<AiNameTableEntry>()); list.Add(entry); NameTable = list.AsReadOnly();
        }

        public static AiSituationSummary From(FactionFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var summary = new AiSituationSummary { FactionId = frame.FactionId, Tick = frame.Tick };
            var list = new List<AiNameTableEntry>();
            AddScope(summary, list, "全部隊", new ScopeKey(frame.FactionId, ScopeKind.All, 0), true, "部隊", "自軍全体");

            var armies = (frame.Observation?.OwnArmies ?? Array.Empty<OwnArmyView>()).OrderBy(a => a.Id).ToArray();
            for (int i = 0; i < armies.Length; i++)
                AddScope(summary, list, "第" + (i + 1).ToString(CultureInfo.InvariantCulture) + "軍",
                    new ScopeKey(frame.FactionId, ScopeKind.Army, armies[i].Id), true, "部隊", ArmyState(armies[i]));

            var objectives = (frame.Observation?.Objectives ?? Array.Empty<KnownObjective>()).OrderBy(o => o.Kind).ThenBy(o => o.Id).ToArray();
            int outpostNumber = 1;
            foreach (var objective in objectives)
            {
                if (objective.Kind == GoalKind.Outpost)
                {
                    string name = OutpostName(objective, objectives, outpostNumber++);
                    bool own = objective.IsOwnerKnown && objective.OwnerFactionId == frame.FactionId;
                    var goal = new PolicyGoal(GoalKind.Outpost, objective.Id, objective.Position);
                    AddScope(summary, list, name, new ScopeKey(frame.FactionId, ScopeKind.Outpost, objective.Id), own, "拠点", OwnerState(own, objective.IsOwnerKnown));
                    AddGoal(summary, list, name, goal, own, objective.Position, objective.OwnerFactionId == frame.FactionId);
                }
                else if (objective.Kind == GoalKind.Core && objective.IsOwnerKnown)
                {
                    string name = objective.OwnerFactionId == frame.FactionId ? "自軍コア" : "敵コア";
                    var goal = new PolicyGoal(GoalKind.Core, objective.Id, objective.Position);
                    AddGoal(summary, list, name, goal, objective.OwnerFactionId == frame.FactionId, objective.Position, objective.OwnerFactionId == frame.FactionId);
                    SetNameState(summary, name, "拠点", OwnerState(objective.OwnerFactionId == frame.FactionId, objective.IsOwnerKnown));
                }
                else if (objective.Kind == GoalKind.Point)
                {
                    AddGoal(summary, list, "地点" + objective.Id.ToString(CultureInfo.InvariantCulture),
                        new PolicyGoal(GoalKind.Point, objective.Id, objective.Position), true, objective.Position, true);
                    SetNameState(summary, "地点" + objective.Id.ToString(CultureInfo.InvariantCulture), "地点", "既知");
                }
            }

            foreach (var region in (frame.Regions ?? Array.Empty<RegionView>()).OrderBy(r => r.Id))
                AddScope(summary, list, "区域" + region.Id.ToString(CultureInfo.InvariantCulture),
                    new ScopeKey(frame.FactionId, ScopeKind.Region, region.Id), true, "区域", RegionState(region));

            if (frame.Economy != null)
            {
                var buildingNumbers = new Dictionary<BuildingKind, int>();
                foreach (var building in frame.Economy.Buildings.Where(b => b.FactionId == frame.FactionId).OrderBy(b => b.Id))
                {
                    int number = buildingNumbers.TryGetValue(building.Kind, out var previous) ? previous + 1 : 1;
                    buildingNumbers[building.Kind] = number;
                    string name = BuildingName(building.Kind) + number.ToString(CultureInfo.InvariantCulture);
                    AddProducer(summary, list, name, building.Id, building.Center, BuildingState(building));
                }
            }
            summary.NameTable = list.AsReadOnly();
            if (armies.Length > 1)
            {
                OwnArmyView north = armies.OrderByDescending(a => a.Position.Z.Raw).ThenBy(a => a.Id).First();
                OwnArmyView south = armies.OrderBy(a => a.Position.Z.Raw).ThenBy(a => a.Id).First();
                summary.AddAlias("北軍", ArmyName(armies, north.Id));
                if (south.Id != north.Id) summary.AddAlias("南軍", ArmyName(armies, south.Id));
            }
            foreach (var army in armies.Where(a => a.Kind == UnitKind.Scout))
                summary.AddAlias("斥候", ArmyName(armies, army.Id));
            summary.Text = BuildText(frame, armies, objectives);
            return summary;
        }

        private static string OutpostName(KnownObjective objective, IReadOnlyList<KnownObjective> all, int ordinal)
        {
            if (all.Count(o => o.Kind == GoalKind.Outpost) == 1) return "北の拠点";
            int north = all.Count(o => o.Kind == GoalKind.Outpost && o.Position.Z.Raw > objective.Position.Z.Raw);
            if (north == 0 && all.Count(o => o.Kind == GoalKind.Outpost) >= 2) return "北の拠点";
            int south = all.Count(o => o.Kind == GoalKind.Outpost && o.Position.Z.Raw < objective.Position.Z.Raw);
            if (south == 0 && all.Count(o => o.Kind == GoalKind.Outpost) >= 2) return "南の拠点";
            return "拠点" + ordinal.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildingName(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return "兵舎";
                case BuildingKind.Mine: return "鉱山";
                case BuildingKind.Smelter: return "溶鉱炉";
                case BuildingKind.Farm: return "農場";
                case BuildingKind.House: return "住居";
                case BuildingKind.DropSite: return "資源拠点";
                case BuildingKind.Wall: return "壁";
                case BuildingKind.Tower: return "塔";
                case BuildingKind.Blacksmith: return "鍛冶場";
                case BuildingKind.Market: return "市場";
                case BuildingKind.SiegeWorkshop: return "攻城工房";
                case BuildingKind.ArcheryRange: return "射手育成所";
                case BuildingKind.Stable: return "騎兵育成所";
                case BuildingKind.Castle: return "城";
                case BuildingKind.Town: return "支城";
                default: return kind.ToString();
            }
        }

        private static string ArmyName(IReadOnlyList<OwnArmyView> armies, uint id)
        {
            for (int i = 0; i < armies.Count; i++) if (armies[i].Id == id)
                return "第" + (i + 1).ToString(CultureInfo.InvariantCulture) + "軍";
            return null;
        }

        private static void AddScope(AiSituationSummary s, List<AiNameTableEntry> list, string name, ScopeKey scope, bool own,
            string category = "対象", string state = "")
        {
            if (!s.names.ContainsKey(name))
            {
                var entry = new AiNameTableEntry { Name = name, Scope = scope, HasScope = true, Id = scope.Id, IsOwn = own,
                    Category = category, State = state };
                s.names.Add(name, entry); list.Add(entry);
            }
        }
        private static void AddGoal(AiSituationSummary s, List<AiNameTableEntry> list, string name, PolicyGoal goal, bool own, SimPoint point, bool addAlias)
        {
            var entry = new AiNameTableEntry { Name = name, Goal = goal, HasGoal = true, Id = goal.Id, IsOwn = own, Point = point,
                Category = "拠点", State = OwnerState(own, true) };
            if (s.names.TryGetValue(name, out var old)) { old.Goal = goal; old.HasGoal = true; old.IsOwn = old.IsOwn || own; }
            else { s.names.Add(name, entry); list.Add(entry); }
        }
        private static void AddProducer(AiSituationSummary s, List<AiNameTableEntry> list, string name, uint id, SimPoint point, string state)
        {
            var entry = new AiNameTableEntry { Name = name, Id = id, IsOwn = true, Point = point, Category = "建物", State = state };
            if (!s.names.ContainsKey(name)) { s.names.Add(name, entry); list.Add(entry); }
        }
        private static void SetNameState(AiSituationSummary s, string name, string category, string state)
        {
            if (s.names.TryGetValue(name, out var entry)) { entry.Category = category; entry.State = state; }
        }

        private static string ArmyState(OwnArmyView army)
            => UnitName(army.Kind) + army.AliveCount.ToString(CultureInfo.InvariantCulture);

        private static string UnitName(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Infantry: return "歩兵";
                case UnitKind.Scout: return "斥候";
                case UnitKind.Villager: return "村人";
                case UnitKind.Archer: return "弓兵";
                case UnitKind.Cavalry: return "騎兵";
                default: return kind.ToString();
            }
        }

        private static string OwnerState(bool own, bool known) => known ? (own ? "自軍" : "敵/他") : "不明";

        private static string RegionState(RegionView region)
            => (region.Control == RegionControl.Human ? "人" : "AI") + "/" + region.EconomyPolicy;

        private static string BuildingState(BuildingView building)
            => (building.Complete ? "完成" : "建設中") + (building.Queued > 0 ? ",訓練" + building.Queued : "");

        private static string BuildText(FactionFrame frame, IReadOnlyList<OwnArmyView> armies, IReadOnlyList<KnownObjective> objectives)
        {
            var b = new StringBuilder();
            b.Append("tick ").Append(frame.Tick.ToString(CultureInfo.InvariantCulture)).Append("。自軍部隊=").Append(armies.Count).Append("。\n");
            foreach (var kind in armies.Select(a => a.Kind).Distinct().OrderBy(k => (int)k))
            {
                var same = armies.Where(a => a.Kind == kind).ToArray();
                b.Append(UnitName(kind)).Append(same.Sum(a => a.AliveCount)).Append("（").Append(same.Length).Append("部隊）。");
            }
            b.Append("\n");
            int visible = frame.Observation?.VisibleEnemies?.Count ?? 0;
            int contacts = frame.Observation?.Contacts?.Count ?? 0;
            b.Append("現在見えている敵=").Append(visible).Append("、既知の接触=").Append(contacts).Append("。\n");
            var objectiveGroups = objectives.GroupBy(o => o.Kind).OrderBy(g => (int)g.Key);
            foreach (var group in objectiveGroups)
            {
                b.Append(group.Key).Append("=").Append(group.Count()).Append("件（");
                b.Append(string.Join(",", group.Select(o => OwnerState(o.OwnerFactionId == frame.FactionId, o.IsOwnerKnown)).Distinct(StringComparer.Ordinal)));
                b.Append("）。");
            }
            b.Append("\n");
            if (frame.Economy != null)
            {
                b.Append("自軍内政: 食料=").Append(frame.Economy.Food).Append(" 木材=").Append(frame.Economy.Wood)
                    .Append(" 人口=").Append(frame.Economy.Population).Append('/').Append(frame.Economy.PopulationCap)
                    .Append(" 自動=").Append(frame.Economy.AutoEconomy ? "オン" : "オフ");
                var buildingCounts = frame.Economy.Buildings.Where(x => x.FactionId == frame.FactionId).GroupBy(x => x.Kind)
                    .OrderBy(x => (int)x.Key).Select(x => BuildingName(x.Key) + x.Count().ToString(CultureInfo.InvariantCulture));
                b.Append(" 建物=").Append(string.Join(",", buildingCounts));
                var resourceCounts = frame.Economy.Resources.GroupBy(x => x.Kind).OrderBy(x => (int)x.Key)
                    .Select(x => x.Key + x.Count().ToString(CultureInfo.InvariantCulture));
                b.Append(" 資源=").Append(string.Join(",", resourceCounts)).Append("。\n");
            }
            return b.ToString();
        }
    }

    public interface IAiPlacementFinder
    {
        bool TryFindCell(FactionFrame frame, string locationName, BuildingKind building, out int cell, out string reason);
    }

    /// <summary>地図のセル規則をApplicationに漏らさない差し替え口。お任せ配置はこれを通る。</summary>
    public sealed class DelegateAiPlacementFinder : IAiPlacementFinder
    {
        private readonly Func<FactionFrame, string, BuildingKind, Tuple<bool, int, string>> finder;
        public DelegateAiPlacementFinder(Func<FactionFrame, string, BuildingKind, Tuple<bool, int, string>> finder)
        { this.finder = finder ?? throw new ArgumentNullException(nameof(finder)); }
        public bool TryFindCell(FactionFrame frame, string locationName, BuildingKind building, out int cell, out string reason)
        {
            var result = finder(frame, locationName, building) ?? Tuple.Create(false, 0, "置き場所を決められない");
            cell = result.Item2; reason = result.Item3; return result.Item1;
        }
    }

    public sealed class AiInterpretationContext
    {
        public FactionFrame Frame { get; set; }
        public AiSituationSummary Summary { get; set; }
        public bool HasFixedTarget { get; set; }
        public ScopeKey FixedTarget { get; set; }
        /// <summary>ScopeKeyで表せない建物なども、選択した名前をそのままプロンプトへ渡す。</summary>
        public string FixedTargetName { get; set; }
        public long StartedTick { get; set; }
        public int MaxObservationAgeTicks { get; set; } = 240;
        public long DeadlineTick { get; set; }
        public IAiPlacementFinder PlacementFinder { get; set; }
        /// <summary>Caller marks autonomous proposals as Ai; human chat remains Human.</summary>
        public OperationSource OperationSource { get; set; } = OperationSource.Human;
    }

    /// <summary>LLMの返答を読み、検証に通ったものだけ既存Contractsへ変換する。</summary>
    public static class AiResponseInterpreter
    {
        public static AiCommandInterpretationResult Interpret(string json, AiInterpretationContext context)
        {
            if (context == null || context.Frame == null || context.Summary == null) throw new ArgumentNullException(nameof(context));
            try
            {
                var root = AiJson.AsObject(AiJson.Parse(json));
                if (root.ContainsKey("kind") && !root.ContainsKey("commands")) root = ExpandSmallAnswer(root);
                var result = new AiCommandInterpretationResult { Say = AiJson.String(root, "say") ?? "", Reason = AiJson.String(root, "reason") ?? "" };
                result.Unknown = AiJson.Bool(root, "unknown");
                if (result.Unknown) return result;
                var policies = new List<UserPolicyIntent>();
                var economy = new List<EconomyCommand>();
                var doctrines = new List<string>();
                var rejected = new List<AiRejectedCommand>();
                var tactics = new List<AiTacticCommand>();
                var commands = DistinctCommands(AiJson.Array(root, "commands"));
                var operationObjects = AiJson.Array(root, "operations");
                if (commands == null && operationObjects == null) throw new FormatException("commands is required");
                ulong nextSequence = 1;
                var operations = new List<OperationDefinition>();
                for (int i = 0; i < (commands == null ? 0 : commands.Count); i++)
                {
                    try
                    {
                        var command = AiJson.AsObject(commands[i]);
                        if (string.Equals(AiJson.String(command, "type"), "operation", StringComparison.OrdinalIgnoreCase))
                            operations.Add(ConvertOperation(command, context, ref nextSequence));
                        else ConvertCommand(command, i, context, ref nextSequence, policies, economy, doctrines, tactics);
                    }
                    catch (AiCommandException e) { rejected.Add(new AiRejectedCommand { Index = i, Kind = e.Kind, Reason = e.Message }); }
                }
                if (operationObjects != null)
                    for (int i = 0; i < operationObjects.Count; i++)
                    {
                        try { operations.Add(ConvertOperation(AiJson.AsObject(operationObjects[i]), context, ref nextSequence)); }
                        catch (AiCommandException e) { rejected.Add(new AiRejectedCommand { Index = i, Kind = "operation", Reason = e.Message }); }
                    }
                if (tactics.Count > 1)
                {
                    rejected.Add(new AiRejectedCommand { Index = -1, Kind = "tactic", Reason = "戦術の切り替え・つまみ変更は1回の返答に1つまでです。" });
                    tactics.Clear();
                }
                if (tactics.Count == 1 && (policies.Count != 0 || economy.Count != 0 || operations.Count != 0 || doctrines.Count != 0))
                {
                    rejected.Add(new AiRejectedCommand { Index = -1, Kind = "tactic", Reason = "戦術の切り替え・つまみ変更は具体的な命令や全体方針と同時に出せません。" });
                    tactics.Clear(); policies.Clear(); economy.Clear(); operations.Clear(); doctrines.Clear();
                }
                result.Policies = policies.AsReadOnly(); result.EconomyCommands = economy.AsReadOnly(); result.Operations = operations.AsReadOnly(); result.Rejected = rejected.AsReadOnly();
                result.TacticCommands = tactics.AsReadOnly();
                if (doctrines.Count != 0) result.Doctrine = doctrines[0];
                return result;
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is OverflowException)
            {
                return new AiCommandInterpretationResult { Reason = "読めない答え: " + e.Message };
            }
        }

        private static Dictionary<string, object> ExpandSmallAnswer(Dictionary<string, object> small)
        {
            string kind = AiJson.String(small, "kind");
            string reason = AiJson.String(small, "reason") ?? "";
            if (string.Equals(kind, "unknown", StringComparison.Ordinal) || string.IsNullOrEmpty(kind))
                return new Dictionary<string, object>
                {
                    ["commands"] = new List<object>(), ["operations"] = new List<object>(), ["say"] = "",
                    ["reason"] = reason, ["unknown"] = true
                };

            var allowed = new[] { "Focus", "Defend", "AllowAbandon", "Retreat", "ReturnToAuto", "SetRegionControl", "SetDoctrine", "SetTacticParam", "SwitchTactic", "SendTacticSignal" };
            if (!allowed.Contains(kind, StringComparer.Ordinal))
                return new Dictionary<string, object>
                {
                    ["commands"] = new List<object>(), ["operations"] = new List<object>(), ["say"] = "",
                    ["reason"] = string.IsNullOrEmpty(reason) ? "対応していない命令" : reason, ["unknown"] = true
                };

            var command = new Dictionary<string, object>();
            if (kind == "SetRegionControl")
            {
                command["type"] = "economy";
                command["kind"] = kind;
                command["region"] = AiJson.String(small, "region") ?? "";
                command["control"] = AiJson.String(small, "control") ?? "";
            }
            else if (kind == "SetDoctrine")
            {
                command["type"] = "doctrine";
                command["kind"] = "";
                command["preset"] = AiJson.String(small, "doctrine") ?? "";
            }
            else if (kind == "SetTacticParam")
            {
                command["type"] = "tactic"; command["kind"] = kind;
                command["tacticParam"] = AiJson.String(small, "tacticParam") ?? "";
                command["tacticParamValue"] = AiJson.String(small, "tacticParamValue") ?? "";
            }
            else if (kind == "SwitchTactic")
            {
                command["type"] = "tactic"; command["kind"] = kind;
                command["tactic"] = AiJson.String(small, "tactic") ?? "";
            }
            else if (kind == "SendTacticSignal")
            {
                command["type"] = "tactic"; command["kind"] = kind;
                command["tacticSignal"] = AiJson.String(small, "tacticSignal") ?? "";
                command["tacticSignalX"] = AiJson.Number(small, "tacticSignalX", 0);
                command["tacticSignalZ"] = AiJson.Number(small, "tacticSignalZ", 0);
            }
            else
            {
                command["type"] = "policy";
                command["kind"] = kind;
                command["scope"] = AiJson.String(small, "scope") ?? "";
                command["goal"] = AiJson.String(small, "goal") ?? "";
            }
            return new Dictionary<string, object>
            {
                ["commands"] = new List<object> { command }, ["operations"] = new List<object>(), ["say"] = "",
                ["reason"] = reason, ["unknown"] = false
            };
        }

        private static void ConvertCommand(Dictionary<string, object> command, int index, AiInterpretationContext c, ref ulong next, List<UserPolicyIntent> policies, List<EconomyCommand> economy, List<string> doctrines = null, List<AiTacticCommand> tactics = null)
        {
            string type = AiJson.String(command, "type"); string kindText = AiJson.String(command, "kind");
            if (string.Equals(type, "doctrine", StringComparison.OrdinalIgnoreCase))
            {
                ConvertDoctrine(command, doctrines);
                return;
            }
            if (string.Equals(type, "tactic", StringComparison.OrdinalIgnoreCase))
            {
                ConvertTactic(command, kindText, c, tactics);
                return;
            }
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(kindText)) throw new AiCommandException("形が違う", kindText);
            if (type == "policy") ConvertPolicy(command, kindText, c, ref next, policies);
            else if (type == "economy") ConvertEconomy(command, kindText, c, ref next, economy);
            else throw new AiCommandException("対応していない命令の種類", kindText);
        }

        private static void ConvertDoctrine(Dictionary<string, object> command, List<string> doctrines)
        {
            if (doctrines == null) throw new AiCommandException("全体方針は条件付き命令にできない", "doctrine");
            string preset = AiJson.String(command, "preset");
            if (preset != "none" && preset != "maintain" && preset != "concentrate")
                throw new AiCommandException("全体方針が不明", "doctrine");
            if (doctrines.Count != 0) throw new AiCommandException("全体方針は1つまで", "doctrine");
            doctrines.Add(preset);
        }

        private static OperationDefinition ConvertOperation(Dictionary<string, object> command, AiInterpretationContext c, ref ulong next)
        {
            object whenValue = command.TryGetValue("when", out var rawWhen) ? rawWhen : null;
            OperationCondition when = ParseCondition(whenValue, c);
            var then = DistinctCommands(AiJson.Array(command, "then"));
            if (then == null || then.Count == 0) throw new AiCommandException("作戦のthenが空", "Conditional");
            var policies = new List<UserPolicyIntent>(); var economy = new List<EconomyCommand>();
            for (int i = 0; i < then.Count; i++)
            {
                var nested = AiJson.AsObject(then[i]);
                if (string.Equals(AiJson.String(nested, "type"), "operation", StringComparison.OrdinalIgnoreCase))
                    throw new AiCommandException("作戦のthenに作戦は置けない", "Conditional");
                ConvertCommand(nested, i, c, ref next, policies, economy);
            }
            var actions = new List<OperationAction>();
            actions.AddRange(policies.Select(OperationAction.FromPolicy));
            actions.AddRange(economy.Select(OperationAction.FromEconomy));
            bool once = AiJson.Bool(command, "once");
            return new OperationDefinition(when, actions, once, c.OperationSource);
        }

        private static void ConvertTactic(Dictionary<string, object> command, string kindText, AiInterpretationContext c, List<AiTacticCommand> tactics)
        {
            if (tactics == null) throw new AiCommandException("戦術変更は条件付き命令にできない", kindText);
            var info = c.Summary.TacticInfo ?? new AiTacticInfo();
            if (kindText == "SetTacticParam")
            {
                if (string.IsNullOrEmpty(info.CurrentName)) throw new AiCommandException("自軍に戦術がないため、つまみを変えられません", kindText);
                string name = AiJson.String(command, "tacticParam");
                string value = command.TryGetValue("tacticParamValue", out var raw) && raw is string text ? text : null;
                var definition = info.Parameters.FirstOrDefault(p => p.Name == name);
                if (definition == null) throw new AiCommandException(string.IsNullOrEmpty(name) ? "自軍に戦術のつまみがありません" : "つまみが名前表にない", kindText);
                if (value == null || value.Length == 0) throw new AiCommandException("つまみの値が空です", kindText);
                tactics.Add(new AiTacticCommand { Kind = kindText, ParamName = name, ParamValue = value });
                return;
            }
            if (kindText == "SendTacticSignal")
            {
                if (string.IsNullOrEmpty(info.CurrentName)) throw new AiCommandException("自軍に戦術がないため、合図を送れません", kindText);
                string name = AiJson.String(command, "tacticSignal");
                var definition = info.Signals.FirstOrDefault(p => p.Name == name);
                if (definition == null) throw new AiCommandException(string.IsNullOrEmpty(name) ? "自軍に戦術の合図がありません" : "合図が名前表にない", kindText);
                double x = AiJson.Number(command, "tacticSignalX", 0);
                double z = AiJson.Number(command, "tacticSignalZ", 0);
                bool hasPoint = x != 0 || z != 0;
                if (definition.NeedsPoint && !hasPoint) throw new AiCommandException("この合図には地点が必要です", kindText);
                if (!definition.NeedsPoint && hasPoint) throw new AiCommandException("この合図には地点を付けられません", kindText);
                tactics.Add(new AiTacticCommand { Kind = kindText, SignalName = name, Point = hasPoint ? ToPoint(x, z) : (SimPoint?)null });
                return;
            }
            if (kindText == "SwitchTactic")
            {
                string name = command.TryGetValue("tactic", out var raw) && raw is string text ? text : "";
                if (name == "なし") name = "";
                if (name.Length != 0 && !info.AvailableNames.Contains(name, StringComparer.Ordinal))
                    throw new AiCommandException("戦術名が候補にありません", kindText);
                tactics.Add(new AiTacticCommand { Kind = kindText, TacticName = name });
                return;
            }
            throw new AiCommandException("対応していない戦術命令", kindText);
        }

        private static SimPoint ToPoint(double x, double z)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(z) || double.IsInfinity(z))
                throw new AiCommandException("合図の地点が不正", "SendTacticSignal");
            long rawX = checked((long)decimal.Round((decimal)x * 65536m, 0, MidpointRounding.ToEven));
            long rawZ = checked((long)decimal.Round((decimal)z * 65536m, 0, MidpointRounding.ToEven));
            return new SimPoint(Fix64.FromRaw(rawX), Fix64.FromRaw(rawZ));
        }

        private static List<object> DistinctCommands(List<object> commands)
        {
            if (commands == null) return null;
            var result = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in commands)
            {
                string key = CanonicalJson(command);
                if (seen.Add(key)) result.Add(command);
            }
            return result;
        }

        // JSON object key order is not part of equality. Sort keys so suppression depends on every field value,
        // rather than on how the model happened to order the required properties.
        private static string CanonicalJson(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                var parts = new List<string>();
                foreach (var pair in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                    parts.Add(JsonValueWriter.Write(pair.Key) + ":" + CanonicalJson(pair.Value));
                return "{" + string.Join(",", parts.ToArray()) + "}";
            }
            var list = value as List<object>;
            if (list != null) return "[" + string.Join(",", list.Select(CanonicalJson).ToArray()) + "]";
            return JsonValueWriter.Write(value);
        }

        private static OperationCondition ParseCondition(object raw, AiInterpretationContext c)
        {
            var obj = raw as Dictionary<string, object>;
            if (obj == null) throw new AiCommandException("作戦のwhenが不明", "Conditional");
            string kind = AiJson.String(obj, "kind");
            string name = AiJson.String(obj, "objective");
            uint id = ResolveObjective(name, c, "Conditional");
            switch (kind)
            {
                case "OwnerChangedToEnemy":
                    return OperationCondition.OwnerChangedToEnemy(name, id);
                case "OwnerChangedToSelf":
                    return OperationCondition.OwnerChangedToSelf(name, id);
                case "EnemyNear":
                    return OperationCondition.EnemyNear(name, id, Math.Max(1, Integer(obj, "count", 1)));
                case "OwnArmyBelowPercent":
                    if (obj.TryGetValue("permille", out var permille) && permille != null)
                        return OperationCondition.OwnArmyBelowPermille(Integer(obj, "permille", 0));
                    return OperationCondition.OwnArmyBelowPercent(50);
                case "TimeAfter":
                    return OperationCondition.TimeAfterSeconds((long)Integer(obj, "minutes", 0) * 60);
                case "JudgementTrue":
                    return OperationCondition.Judgement(AiJson.String(obj, "statement") ?? "operation_north_broken");
                case "And":
                    var values = obj.TryGetValue("all", out var all) ? all as List<object> : null;
                    if (values == null || values.Count < 2 || values.Count > 2) throw new AiCommandException("ANDは2つまで", "Conditional");
                    return OperationCondition.AllOf(values.Select(v => ParseCondition(v, c)).ToArray());
                default: throw new AiCommandException("作戦の条件語彙が不明", "Conditional");
            }
        }

        private static uint ResolveObjective(string name, AiInterpretationContext c, string kind)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (!c.Summary.TryGet(name, out var entry) || !entry.HasGoal || entry.Goal.Kind != GoalKind.Outpost)
                throw new AiCommandException("条件の拠点が名前表にない", kind);
            return entry.Goal.Id;
        }

        private static int Integer(Dictionary<string, object> obj, string key, int fallback)
        {
            if (!obj.TryGetValue(key, out var value) || value == null) return fallback;
            if (value is long number) return checked((int)number);
            throw new AiCommandException(key + "は整数でない", "Conditional");
        }

        private static void ConvertPolicy(Dictionary<string, object> command, string kindText, AiInterpretationContext c, ref ulong next, List<UserPolicyIntent> output)
        {
            if (!Enum.TryParse(kindText, false, out PolicyKind kind)) throw new AiCommandException("対応していない方針", kindText);
            ScopeKey scope;
            string scopeName = AiJson.String(command, "scope");
            if (string.IsNullOrEmpty(scopeName))
            {
                if (!c.HasFixedTarget) throw new AiCommandException("対象が不明", kindText);
                scope = c.FixedTarget;
            }
            else if (!c.Summary.TryGet(scopeName, out var scopeEntry) || !scopeEntry.HasScope || !scopeEntry.IsOwn)
                throw new AiCommandException("対象が名前表にないか、自分の物でない", kindText);
            else scope = scopeEntry.Scope;
            if (scope.FactionId != c.Frame.FactionId) throw new AiCommandException("自分の物でない対象", kindText);
            if (!IsScopeAllowed(kind, scope.Kind)) throw new AiCommandException("この範囲では出せない方針", kindText);
            PolicyGoal goal = default(PolicyGoal);
            string goalName = AiJson.String(command, "goal");
            bool needsGoal = kind == PolicyKind.Focus || kind == PolicyKind.Defend || kind == PolicyKind.Scout;
            if (!string.IsNullOrEmpty(goalName))
            {
                if (!c.Summary.TryGet(goalName, out var goalEntry) || !goalEntry.HasGoal)
                    throw new AiCommandException("目標が名前表にない", kindText);
                if ((kind == PolicyKind.Defend || kind == PolicyKind.Retreat) && !goalEntry.IsOwn)
                    throw new AiCommandException("自分の物でない目標", kindText);
                goal = goalEntry.Goal;
            }
            else if (needsGoal) throw new AiCommandException("目標が不明", kindText);
            var expiration = new Expiration(c.DeadlineTick == 0 ? c.StartedTick + c.MaxObservationAgeTicks : c.DeadlineTick,
                c.MaxObservationAgeTicks, ExpireFlags.ObservationTooOld);
            ushort reserve = AiJson.UInt16(command, "reservePermille", 0);
            ushort allowedLoss = AiJson.UInt16(command, "allowedLossPermille", 1000);
            if (allowedLoss == 0) allowedLoss = 1000; // 0 is "not given" in the schema
            if (reserve > 1000 || allowedLoss > 1000) throw new AiCommandException("割合が不正", kindText);
            output.Add(new UserPolicyIntent(next++, scope, kind, goal, 100, new LossBudget(allowedLoss),
                new EndCondition(EndKind.UntilReplaced, 0), reserve, expiration));
        }

        private static bool IsScopeAllowed(PolicyKind kind, ScopeKind scope)
        {
            if (kind == PolicyKind.Focus) return scope == ScopeKind.All || scope == ScopeKind.Army || scope == ScopeKind.Region;
            if (kind == PolicyKind.MaintainReserve) return scope != ScopeKind.Outpost;
            if (kind == PolicyKind.AllowAbandon) return scope == ScopeKind.Outpost || scope == ScopeKind.Region;
            return scope == ScopeKind.All || scope == ScopeKind.Army || scope == ScopeKind.Outpost || scope == ScopeKind.Region;
        }

        private static void ConvertEconomy(Dictionary<string, object> command, string kindText, AiInterpretationContext c, ref ulong next, List<EconomyCommand> output)
        {
            if (c.Frame.Economy == null) throw new AiCommandException("内政が対応していない", kindText);
            if (!Enum.TryParse(kindText, false, out EconomyCommandKind kind)) throw new AiCommandException("対応していない内政命令", kindText);
            ulong seq = AiJson.UInt64(command, "sequence", 0);
            if (seq == 0) seq = next++; // 0 is "not given" in the schema
            string targetName = AiJson.String(command, "region");
            if (kind == EconomyCommandKind.SetRegionControl)
            {
                if (!c.Summary.TryGet(targetName, out var region) || !region.HasScope || region.Scope.Kind != ScopeKind.Region)
                    throw new AiCommandException("区域が名前表にない", kindText);
                string control = AiJson.String(command, "control") ?? "Human";
                if (!Enum.TryParse(control, false, out RegionControl regionControl)) throw new AiCommandException("区域担当が不明", kindText);
                output.Add(EconomyCommand.SetRegionControl(c.Frame.FactionId, seq, region.Scope.Id, regionControl)); return;
            }
            if (kind == EconomyCommandKind.SetAutoEconomy)
            {
                output.Add(EconomyCommand.Auto(c.Frame.FactionId, seq, AiJson.Bool(command, "enabled"))); return;
            }
            if (kind == EconomyCommandKind.ReturnEconomyToAuto)
            {
                output.Add(EconomyCommand.ReturnToAuto(c.Frame.FactionId, seq)); return;
            }
            if (c.Frame.Economy.AutoEconomy && kind != EconomyCommandKind.SetEconomyPolicy)
                throw new AiCommandException("内政の手動操作がオフ", kindText);
            switch (kind)
            {
                case EconomyCommandKind.SetEconomyPolicy:
                    if (!string.IsNullOrEmpty(targetName))
                    {
                        if (!c.Summary.TryGet(targetName, out var regionPolicy) || !regionPolicy.HasScope || regionPolicy.Scope.Kind != ScopeKind.Region)
                            throw new AiCommandException("区域が名前表にない", kindText);
                        output.Add(EconomyCommand.SetRegionPolicy(c.Frame.FactionId, seq, regionPolicy.Scope.Id, ParseEconomyPolicy(command)));
                    }
                    else output.Add(EconomyCommand.SetPolicy(c.Frame.FactionId, seq, ParseEconomyPolicy(command)));
                    return;
                case EconomyCommandKind.AdvanceAge:
                    if (!c.Frame.Economy.Ages) throw new AiCommandException("今のルールでは時代を進められない", kindText);
                    output.Add(EconomyCommand.Advance(c.Frame.FactionId, seq, ParseEnum<CivKind>(command, "civ"))); return;
                case EconomyCommandKind.PlaceBuilding:
                    var building = ParseEnum<BuildingKind>(command, "building");
                    int cell;
                    string location = AiJson.String(command, "location");
                    string namedPlacementReason = null;
                    if (string.IsNullOrEmpty(location) || location == "お任せ")
                    {
                        string placementReason = null;
                        if (c.PlacementFinder == null || !c.PlacementFinder.TryFindCell(c.Frame, location ?? "お任せ", building, out cell, out placementReason))
                            throw new AiCommandException(placementReason ?? "置き場所を決められない", kindText);
                    }
                    else if (c.Summary.TryGet(location, out var locationEntry) && c.PlacementFinder != null &&
                        c.PlacementFinder.TryFindCell(c.Frame, location, building, out cell, out namedPlacementReason))
                    {
                    }
                    else if (!int.TryParse(location, NumberStyles.Integer, CultureInfo.InvariantCulture, out cell))
                        throw new AiCommandException(namedPlacementReason ?? "置き場所が名前表にない", kindText);
                    int placeCount = Count(command);
                    for (int i = 0; i < placeCount; i++) output.Add(EconomyCommand.Place(c.Frame.FactionId, seq++, building, cell));
                    return;
                case EconomyCommandKind.Train:
                    int trainCount = Count(command);
                    for (int i = 0; i < trainCount; i++) output.Add(EconomyCommand.Train(c.Frame.FactionId, seq++, ProducerId(command, c), ParseEnum<UnitKind>(command, "unit")));
                    return;
                case EconomyCommandKind.CancelTrain:
                    output.Add(EconomyCommand.CancelTrain(c.Frame.FactionId, seq, ProducerId(command, c))); return;
                default: throw new AiCommandException("G-1で対応していない内政命令", kindText);
            }
        }

        private static uint ProducerId(Dictionary<string, object> command, AiInterpretationContext c)
        {
            string producer = AiJson.String(command, "producer");
            if (string.IsNullOrEmpty(producer) || producer == "コア") return 0;
            if (!c.Summary.TryGet(producer, out var entry) || entry.Id == 0) throw new AiCommandException("訓練元が名前表にない", "Train");
            return entry.Id;
        }
        private static int Count(Dictionary<string, object> command)
        {
            double value = AiJson.Number(command, "count", 1);
            if (value == 0) value = 1; // 0 is "not given" in the schema
            if (value < 1 || value > 100 || value != Math.Truncate(value)) throw new AiCommandException("個数が不正", "count");
            return (int)value;
        }
        private static T ParseEnum<T>(Dictionary<string, object> obj, string key) where T : struct
        {
            string value = AiJson.String(obj, key);
            if (TryParseGameName(value, out T result)) return result;
            throw new AiCommandException(key + "が不明", key);
        }
        private static EconomyPolicy ParseEconomyPolicy(Dictionary<string, object> obj)
        {
            string value = AiJson.String(obj, "policy");
            if (string.Equals(value, "Economy", StringComparison.OrdinalIgnoreCase) || value == "経済" || value == "内政" || value == "成長") return EconomyPolicy.Growth;
            if (string.Equals(value, "Military", StringComparison.OrdinalIgnoreCase) || value == "軍事" || value == "兵事" || value == "兵の生産") return EconomyPolicy.Military;
            if (string.Equals(value, "Balanced", StringComparison.OrdinalIgnoreCase) || value == "均衡" || value == "バランス") return EconomyPolicy.Balanced;
            throw new AiCommandException("policyが不明", "SetEconomyPolicy");
        }
        private static bool TryParseGameName<T>(string value, out T result) where T : struct
        {
            if (Enum.TryParse(value, false, out result)) return true;
            object mapped = null;
            if (typeof(T) == typeof(BuildingKind))
            {
                switch (value)
                {
                    case "兵舎": mapped = BuildingKind.Barracks; break; case "鉱山": mapped = BuildingKind.Mine; break;
                    case "溶鉱炉": mapped = BuildingKind.Smelter; break; case "農場": mapped = BuildingKind.Farm; break;
                    case "住居": case "家": mapped = BuildingKind.House; break; case "資源拠点": mapped = BuildingKind.DropSite; break;
                    case "壁": mapped = BuildingKind.Wall; break; case "塔": mapped = BuildingKind.Tower; break;
                    case "鍛冶場": mapped = BuildingKind.Blacksmith; break; case "市場": mapped = BuildingKind.Market; break;
                    case "攻城工房": mapped = BuildingKind.SiegeWorkshop; break; case "射手育成所": mapped = BuildingKind.ArcheryRange; break;
                    case "騎兵育成所": mapped = BuildingKind.Stable; break; case "城": mapped = BuildingKind.Castle; break;
                    case "支城": case "町の中心": mapped = BuildingKind.Town; break;
                }
            }
            else if (typeof(T) == typeof(UnitKind))
            {
                switch (value)
                {
                    case "歩兵": mapped = UnitKind.Infantry; break; case "斥候": mapped = UnitKind.Scout; break;
                    case "村人": mapped = UnitKind.Villager; break; case "弓兵": mapped = UnitKind.Archer; break;
                    case "騎兵": mapped = UnitKind.Cavalry; break; case "破城槌": mapped = UnitKind.Ram; break;
                    case "傭兵": mapped = UnitKind.Mercenary; break; case "僧侶": mapped = UnitKind.Monk; break;
                    case "重歩兵": mapped = UnitKind.HeavyInfantry; break; case "散兵": mapped = UnitKind.SkirmishArcher; break;
                    case "軽騎兵": mapped = UnitKind.LightCavalry; break;
                }
            }
            else if (typeof(T) == typeof(CivKind))
            {
                switch (value)
                {
                    case "原始": mapped = CivKind.Primitive; break; case "農耕": mapped = CivKind.Agrarian; break;
                    case "冶金": mapped = CivKind.Metallurgy; break; case "森林": mapped = CivKind.Forestry; break;
                    case "石工": mapped = CivKind.Masonry; break; case "商業": mapped = CivKind.Caravan; break;
                    case "騎兵": mapped = CivKind.Cavalry; break; case "橋梁": mapped = CivKind.Bridge; break;
                    case "学術": mapped = CivKind.Academy; break; case "信仰": mapped = CivKind.Cult; break;
                    case "漁業": mapped = CivKind.Fishing; break; case "山岳": mapped = CivKind.Mountain; break;
                    case "関所": mapped = CivKind.Tollgate; break; case "都市": mapped = CivKind.Metropolis; break;
                    case "聖域": mapped = CivKind.Sanctuary; break;
                }
            }
            if (mapped == null) return false;
            result = (T)mapped; return true;
        }
        private sealed class AiCommandException : Exception
        { internal string Kind; internal AiCommandException(string message, string kind) : base(message) { Kind = kind; } }
    }

    public sealed class InterpreterRequest
    {
        public ulong RequestId { get; set; }
        public uint FactionId { get; set; }
        public string Instruction { get; set; }
        public ScopeKey FixedTarget { get; set; }
        public bool HasFixedTarget { get; set; }
        public string FixedTargetName { get; set; }
        public AiSituationSummary Summary { get; set; }
        public string Model { get; set; }
        public long StartedTick { get; set; }
        public long DeadlineTick { get; set; }
        public OperationSource OperationSource { get; set; } = OperationSource.Human;
    }

    public sealed class InterpreterReply
    {
        public ulong RequestId { get; internal set; }
        public long ReturnedTick { get; internal set; }
        public string Json { get; internal set; }
        public AiTokenUsage Usage { get; internal set; }
        public string Model { get; internal set; }
        public string FailureReason { get; internal set; }
    }

    public interface ICommandInterpreter
    {
        void Request(InterpreterRequest request);
        IReadOnlyList<InterpreterReply> Poll(long tick);
    }

    /// <summary>決まった答えをtick遅延で返す偽物の参謀。外部通信・時計・乱数を使わない。</summary>
    public sealed class FakeCommandInterpreter : ICommandInterpreter
    {
        private sealed class Pending { internal long Ready; internal InterpreterReply Reply; }
        private readonly int delayTicks;
        private readonly Func<InterpreterRequest, string> answer;
        private readonly List<Pending> pending = new List<Pending>();
        public FakeCommandInterpreter(int delayTicks, Func<InterpreterRequest, string> answer)
        { if (delayTicks < 0) throw new ArgumentOutOfRangeException(nameof(delayTicks)); this.delayTicks = delayTicks; this.answer = answer ?? throw new ArgumentNullException(nameof(answer)); }
        public void Request(InterpreterRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string json = answer(request);
            if (json == null) throw new InvalidOperationException("偽物の参謀がnullを返した");
            int inputTokens = AiCostCalculator.EstimateTokens(request.Summary == null ? request.Instruction : request.Summary.Prompt(request.Instruction,
                request.HasFixedTarget && string.IsNullOrEmpty(request.FixedTargetName) ? (ScopeKey?)request.FixedTarget : null, request.Model, request.FixedTargetName));
            int outputTokens = AiCostCalculator.EstimateTokens(json);
            pending.Add(new Pending { Ready = checked(request.StartedTick + delayTicks), Reply = new InterpreterReply { RequestId = request.RequestId, Json = json, Model = request.Model ?? "gpt-6-luna", Usage = new AiTokenUsage(inputTokens, outputTokens) } });
        }
        public IReadOnlyList<InterpreterReply> Poll(long tick)
        {
            var ready = pending.Where(p => p.Ready <= tick).OrderBy(p => p.Ready).ThenBy(p => p.Reply.RequestId).Select(p => { p.Reply.ReturnedTick = tick; return p.Reply; }).ToArray();
            pending.RemoveAll(p => p.Ready <= tick); return ready;
        }
    }

    public sealed class InterpretedReply
    {
        public ulong RequestId { get; internal set; }
        public bool Late { get; internal set; }
        public AiCommandInterpretationResult Result { get; internal set; }
        public AiTokenUsage Usage { get; internal set; }
        public decimal CostYen { get; internal set; }
    }

    /// <summary>解釈中の固定対象と戦況を保持し、遅い返答を締め切りで捨てる。</summary>
    public sealed class CommandInterpreterCoordinator
    {
        private sealed class Open { internal AiInterpretationContext Context; internal string Model; }
        private readonly ICommandInterpreter interpreter;
        private readonly Dictionary<ulong, Open> open = new Dictionary<ulong, Open>();
        private ulong nextId = 1;
        public CommandInterpreterCoordinator(ICommandInterpreter interpreter) { this.interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter)); }
        public ulong Request(string instruction, FactionFrame frame, ScopeKey? fixedTarget, string model, long startedTick, int deadlineTicks, IAiPlacementFinder placementFinder = null,
            OperationSource operationSource = OperationSource.Human, string fixedTargetName = null, AiSituationSummary suppliedSummary = null)
        {
            var summary = suppliedSummary ?? AiSituationSummary.From(frame); ulong id = nextId++;
            var selected = AiModelCatalog.Get(model ?? "gpt-6-luna");
            var context = new AiInterpretationContext { Frame = frame, Summary = summary, StartedTick = startedTick, DeadlineTick = checked(startedTick + deadlineTicks),
                MaxObservationAgeTicks = deadlineTicks, PlacementFinder = placementFinder, OperationSource = operationSource, FixedTargetName = fixedTargetName };
            if (fixedTarget.HasValue) { context.HasFixedTarget = true; context.FixedTarget = fixedTarget.Value; }
            string modelName = selected.Model;
            open.Add(id, new Open { Context = context, Model = modelName });
            interpreter.Request(new InterpreterRequest { RequestId = id, FactionId = frame.FactionId, Instruction = instruction ?? "", FixedTarget = fixedTarget.GetValueOrDefault(), HasFixedTarget = fixedTarget.HasValue || !string.IsNullOrEmpty(fixedTargetName),
                FixedTargetName = fixedTargetName, Summary = summary, Model = modelName, StartedTick = startedTick, DeadlineTick = context.DeadlineTick, OperationSource = operationSource });
            return id;
        }
        private static AiCommandInterpretationResult LimitToModel(string model, AiCommandInterpretationResult result) => AiModelLimits.Apply(model, result);

        public IReadOnlyList<InterpretedReply> Poll(long tick)
        {
            var result = new List<InterpretedReply>();
            foreach (var reply in interpreter.Poll(tick))
            {
                if (!open.TryGetValue(reply.RequestId, out var item)) continue;
                open.Remove(reply.RequestId);
                bool late = reply.ReturnedTick > item.Context.DeadlineTick;
                var usage = reply.Usage;
                AiCommandInterpretationResult parsed;
                if (late) parsed = new AiCommandInterpretationResult { Reason = "締め切りを過ぎた答え" };
                else if (!string.IsNullOrEmpty(reply.FailureReason)) parsed = new AiCommandInterpretationResult { Unknown = true, Reason = reply.FailureReason };
                else parsed = LimitToModel(item.Model, AiResponseInterpreter.Interpret(reply.Json, item.Context));
                result.Add(new InterpretedReply { RequestId = reply.RequestId, Late = late, Usage = usage, CostYen = AiCostCalculator.Calculate(item.Model, usage), Result = parsed });
            }
            return result;
        }
    }

    internal static class AiModelLimits
    {
        /// <summary>
        /// A model that only takes fixed short orders (Jev) may return at most one command. The check is on the answer, not
        /// on the characters of the instruction, so an ordinary short order is never refused for its wording.
        /// </summary>
        internal static AiCommandInterpretationResult Apply(string model, AiCommandInterpretationResult result)
        {
            if (result == null || result.Unknown) return result;
            var limits = AiModelCatalog.Get(model);
            int commands = result.Policies.Count + result.EconomyCommands.Count + result.TacticCommands.Count;
            int operationCommands = result.Operations.Sum(o => o.Then.Count);
            if ((!limits.AllowsOperations && result.Operations.Count != 0) || commands + operationCommands > limits.MaxCommands)
                return new AiCommandInterpretationResult { Unknown = true, Reason = "このモデルでは直せません。この AI では直せません。Claude・ChatGPT を選んでください" };
            return result;
        }
    }

    public sealed class AiModelPrice
    {
        public string Model { get; set; } public decimal InputUsdPerMillion { get; set; } public decimal OutputUsdPerMillion { get; set; }
        public decimal LongContextInputUsdPerMillion { get; set; } public decimal LongContextOutputUsdPerMillion { get; set; }
        public int DeadlineTicks { get; set; } public int MaxCommands { get; set; } public bool AllowsOperations { get; set; }
        public int MaxOutputTokens { get; set; } public bool DisableThinking { get; set; }
        public bool SupportsComplexInstructions => MaxCommands > 1 || AllowsOperations;
    }
    public static class AiModelCatalog
    {
        private static readonly Dictionary<string, AiModelPrice> fallbackPrices = new Dictionary<string, AiModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-haiku-4-5"] = P("claude-haiku-4-5", 1, 5, 240, 5, true, 2048, false), ["claude-haiku-5-5"] = LongContext(P("claude-haiku-5-5", .10m, .50m, 240, 5, true, 2048, false), .50m, 2.50m), ["claude-sonnet-5-5"] = P("claude-sonnet-5-5", 2, 10, 240, 5, true, 2048, false), ["claude-opus-5-5"] = P("claude-opus-5-5", 4, 20, 1200, 5, true, 2048, false), ["claude-fable-5-1"] = P("claude-fable-5-1", 10, 50, 1200, 5, true, 2048, false),
            ["gpt-6-luna"] = P("gpt-6-luna", .10m, .50m, 240, 5, true, 2048, false), ["gpt-6.1-sol"] = P("gpt-6.1-sol", 2, 10, 240, 5, true, 2048, false), ["gpt-6-astra"] = P("gpt-6-astra", 10, 50, 1200, 5, true, 2048, false),
            ["jev"] = P("jev", .042m, 0, 240, 1, false, 256, true), ["local-llm"] = P("local-llm", 0, 0, 600, 1, false, 2048, true)
        };
        private static decimal usdToYen;
        private static readonly Dictionary<string, AiModelPrice> prices = Load(out usdToYen);
        public static decimal UsdToYen => usdToYen;
        internal static AiModelPrice DefaultComplexLimits => P("default", 0, 0, 240, 5, true, 2048, false);
        private static AiModelPrice P(string model, decimal input, decimal output, int deadline, int maxCommands, bool operations, int maxOutputTokens, bool disableThinking)
            => new AiModelPrice { Model = model, InputUsdPerMillion = input, OutputUsdPerMillion = output, LongContextInputUsdPerMillion = input, LongContextOutputUsdPerMillion = output, DeadlineTicks = deadline, MaxCommands = maxCommands, AllowsOperations = operations, MaxOutputTokens = maxOutputTokens, DisableThinking = disableThinking };
        private static AiModelPrice LongContext(AiModelPrice price, decimal input, decimal output)
        { price.LongContextInputUsdPerMillion = input; price.LongContextOutputUsdPerMillion = output; return price; }
        public static AiModelPrice Get(string model)
        {
            if (prices.TryGetValue(model ?? "", out var p)) return p;
            throw new ArgumentException("未知のモデルです。選択肢にあるモデルを指定してください: " + (model ?? "(null)"), nameof(model));
        }
        public static IReadOnlyList<AiModelPrice> All => prices.Values.OrderBy(p => p.Model, StringComparer.Ordinal).ToArray();
        public static IReadOnlyList<AiModelPrice> Available(Func<string, bool> configured = null)
            => All.Where(p => configured == null || configured(p.Model)).ToArray();

        private static Dictionary<string, AiModelPrice> Load(out decimal yen)
        {
            var result = new Dictionary<string, AiModelPrice>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fallbackPrices) result[pair.Key] = pair.Value;
            yen = 150m;
            string path = Environment.GetEnvironmentVariable("AI_MODEL_PRICES_PATH");
            if (string.IsNullOrEmpty(path))
            {
                var dir = new DirectoryInfo(Environment.CurrentDirectory);
                for (int i = 0; i < 8 && dir != null && string.IsNullOrEmpty(path); i++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "Settings", "ai-model-prices.json");
                    if (File.Exists(candidate)) path = candidate;
                }
            }
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
                var root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return result;
                if (root.TryGetValue("usdToYen", out var yenValue) && yenValue is double y && y > 0) yen = (decimal)y;
                if (!(root.TryGetValue("models", out var list) && list is List<object> models)) return result;
                foreach (var value in models)
                {
                    var o = value as Dictionary<string, object>; string name = Text(o, "model");
                    if (o == null || string.IsNullOrEmpty(name)) continue;
                    decimal input = Decimal(o, "inputUsdPerMillion", 0), output = Decimal(o, "outputUsdPerMillion", 0);
                    int deadline = (int)Decimal(o, "deadlineTicks", 240);
                    int maxCommands = (int)Decimal(o, "maxCommands", name.Equals("jev", StringComparison.OrdinalIgnoreCase) || name.Equals("local-llm", StringComparison.OrdinalIgnoreCase) ? 1 : 5);
                    bool operations = Bool(o, "allowOperations", !name.Equals("jev", StringComparison.OrdinalIgnoreCase) && !name.Equals("local-llm", StringComparison.OrdinalIgnoreCase));
                    int maxTokens = (int)Decimal(o, "maxTokens", 2048);
                    bool disableThinking = Bool(o, "disableThinking", name.Equals("local-llm", StringComparison.OrdinalIgnoreCase) || name.Equals("jev", StringComparison.OrdinalIgnoreCase));
                    result[name] = LongContext(P(name, input, output, deadline, maxCommands, operations, maxTokens, disableThinking), Decimal(o, "longContextInputUsdPerMillion", input), Decimal(o, "longContextOutputUsdPerMillion", output));
                }
            }
            catch (Exception) { yen = 150m; return result; }
            return result;
        }
        private static string Text(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) ? v as string : null;
        private static decimal Decimal(Dictionary<string, object> o, string key, decimal fallback)
            => o != null && o.TryGetValue(key, out var v) && v is double d ? (decimal)d : fallback;
        private static bool Bool(Dictionary<string, object> o, string key, bool fallback)
            => o != null && o.TryGetValue(key, out var v) && v is bool b ? b : fallback;
    }
    public readonly struct AiTokenUsage
    {
        public int InputTokens { get; } public int OutputTokens { get; } public int CacheReadInputTokens { get; } public int CacheCreationInputTokens { get; }
        public AiTokenUsage(int input, int output) : this(input, output, 0, 0) { }
        public AiTokenUsage(int input, int output, int cacheRead, int cacheCreation)
        { InputTokens = Math.Max(0, input); OutputTokens = Math.Max(0, output); CacheReadInputTokens = Math.Max(0, cacheRead); CacheCreationInputTokens = Math.Max(0, cacheCreation); }
    }
    public static class AiCostCalculator
    {
        public static decimal Calculate(string model, int inputTokens, int outputTokens)
            => Calculate(model, new AiTokenUsage(inputTokens, outputTokens));
        public static decimal Calculate(string model, AiTokenUsage usage)
        { var p = AiModelCatalog.Get(model); bool longContext = usage.InputTokens > 100000; decimal inputRate = longContext ? p.LongContextInputUsdPerMillion : p.InputUsdPerMillion; decimal outputRate = longContext ? p.LongContextOutputUsdPerMillion : p.OutputUsdPerMillion; return (usage.InputTokens * inputRate + usage.OutputTokens * outputRate + usage.CacheReadInputTokens * inputRate * .1m + usage.CacheCreationInputTokens * inputRate * 1.25m) * AiModelCatalog.UsdToYen / 1000000m; }
        public static int EstimateTokens(string text) => Math.Max(1, (text ?? "").Length / 4);
        public static decimal EstimateYen(string model, string prompt, int expectedOutputTokens = 200) => Calculate(model, EstimateTokens(prompt), expectedOutputTokens);
    }
    public sealed class AiBudgetMeter
    {
        public decimal BudgetYen { get; } public decimal SpentYen { get; private set; } public decimal RemainingYen => BudgetYen - SpentYen;
        public AiBudgetMeter(decimal budgetYen) { if (budgetYen < 0) throw new ArgumentOutOfRangeException(nameof(budgetYen)); BudgetYen = budgetYen; }
        public void Add(decimal yen) { if (yen < 0) throw new ArgumentOutOfRangeException(nameof(yen)); SpentYen += yen; }
    }

    internal static class AiJson
    {
        internal static object Parse(string text) { if (text == null) throw new FormatException("null"); return new Parser(text).Read(); }
        internal static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object> ?? throw new FormatException("object required");
        internal static List<object> Array(Dictionary<string, object> obj, string key) => !obj.TryGetValue(key, out var v) || v == null ? null : v as List<object> ?? throw new FormatException(key + " must be array");
        // An empty string is the schema's way of saying "not given" (no union types, see NullableEnum).
        internal static string String(Dictionary<string, object> obj, string key) => obj.TryGetValue(key, out var v) && v is string text && text.Length != 0 ? text : null;
        internal static bool Bool(Dictionary<string, object> obj, string key) => obj.TryGetValue(key, out var v) && v is bool b && b;
        internal static double Number(Dictionary<string, object> obj, string key, double fallback)
        {
            if (!obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is long l) return l;
            if (v is double d) return d;
            throw new FormatException(key + " must be number");
        }
        internal static ushort UInt16(Dictionary<string, object> obj, string key, ushort fallback)
        {
            double value = Number(obj, key, fallback);
            if (value < 0 || value > ushort.MaxValue || value != Math.Truncate(value)) throw new FormatException(key + " must be integer");
            return (ushort)value;
        }
        internal static ulong UInt64(Dictionary<string, object> obj, string key, ulong fallback) { if (!obj.TryGetValue(key, out var v) || v == null) return fallback; if (v is long l && l >= 0) return checked((ulong)l); throw new FormatException(key + " must be integer"); }
        private sealed class Parser
        {
            private readonly string s; private int p; internal Parser(string text) { s = text; }
            internal object Read() { Skip(); var v = Value(); Skip(); if (p != s.Length) throw new FormatException("trailing data"); return v; }
            private object Value() { Skip(); if (p >= s.Length) throw new FormatException("unexpected end"); switch (s[p]) { case '{': return Object(); case '[': return List(); case '"': return Quoted(); case 't': Word("true"); return true; case 'f': Word("false"); return false; case 'n': Word("null"); return null; default: return Number(); } }
            private Dictionary<string, object> Object() { p++; var o = new Dictionary<string, object>(StringComparer.Ordinal); Skip(); if (Take('}')) return o; while (true) { Skip(); if (p >= s.Length || s[p] != '"') throw new FormatException("object key"); string k = Quoted(); Skip(); Need(':'); object v = Value(); if (!o.TryAdd(k, v)) throw new FormatException("duplicate key"); Skip(); if (Take('}')) return o; Need(','); } }
            private List<object> List() { p++; var a = new List<object>(); Skip(); if (Take(']')) return a; while (true) { a.Add(Value()); Skip(); if (Take(']')) return a; Need(','); } }
            private string Quoted() { Need('"'); var b = new StringBuilder(); while (p < s.Length) { char c = s[p++]; if (c == '"') return b.ToString(); if (c == '\\') { if (p >= s.Length) throw new FormatException("escape"); c = s[p++]; if (c == '"' || c == '\\' || c == '/') b.Append(c); else if (c == 'n') b.Append('\n'); else if (c == 'r') b.Append('\r'); else if (c == 't') b.Append('\t'); else if (c == 'u') { if (p + 4 > s.Length) throw new FormatException("escape"); int code = int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture); b.Append((char)code); p += 4; } else throw new FormatException("escape"); } else b.Append(c); } throw new FormatException("string"); }
            private long Number() { int start = p; if (p < s.Length && s[p] == '-') p++; while (p < s.Length && char.IsDigit(s[p])) p++; if (start == p) throw new FormatException("value"); return long.Parse(s.Substring(start, p - start), CultureInfo.InvariantCulture); }
            private void Word(string word) { if (p + word.Length > s.Length || !s.Substring(p, word.Length).Equals(word, StringComparison.Ordinal)) throw new FormatException("literal"); p += word.Length; }
            private void Skip() { while (p < s.Length && char.IsWhiteSpace(s[p])) p++; } private bool Take(char c) { if (p < s.Length && s[p] == c) { p++; return true; } return false; } private void Need(char c) { if (!Take(c)) throw new FormatException("expected " + c); }
        }
    }
}
