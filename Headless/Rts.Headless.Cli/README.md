# Headless 記録・再生 CLI

リポジトリ内から .NET 10 SDK で実行します。Unity は不要です。

```sh
dotnet test Headless/Rts.Headless.slnx --configuration Release
dotnet run --project Headless/Rts.Headless.Cli -c Release -- record --scenario TestData/week1-10v10.json --inputs TestData/week1-inputs.json --out run.rtsreplay --ticks 1200
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- replay --in run.rtsreplay --hash-out a.hashes --dump-dir diag-a
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- replay --in run.rtsreplay --hash-out b.hashes --dump-dir diag-b
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- compare --left a.hashes --right b.hashes --replay run.rtsreplay
```

`--inputs` を省くとお任せ（入力なし）。`--ticks` は 0〜シナリオの verificationTickLimit（最大10000000）。S0を含み、勝敗・Faultが先に確定したらそのtickで終了します。打切りは勝利や引分けに変換しません。
終了コードは **0: 正常・一致、2: 状態/EventHash/欠落tickの不一致、3: 形式・版・引数の不一致、4: Fault・I/O障害**。

## 入力 JSON

[完全な例](../../TestData/week1-inputs.json) は40tickで陣営1・軍団1を撤退させます。
トップレベルは `ScheduledInput` の配列。各要素に logIndex (uint64)、kind (byte)、acceptedTick / applyTick (int64)、requestId / issuerSequence (uint64)、orders 配列を指定します。orders は Contracts の PolicyOrder と同じ名前・数値enumで全フィールドを表します。parents は `{ "scope": {"factionId":1,"kind":1,"id":0}, "revision":1 }` の配列です。入力は ApplyTick→LogIndex に整列し、LogIndex重複と範囲外tickを拒否します。

Simulation が実行するのは **Resolve=2 / Proposal=4、軍団単位の Focus=1 / Retreat=3** です。Focusのgoalは Point=1、Outpost=2 または敵 Core=3。Reserve=1 / Cancel=3 もバイナリcodecは全フィールドを往復できますが、実行は既存Simulationの制限に従って拒否します。予約・拒否理由の新しい状態機械は後続Issueの範囲です。未対応の制御入力を黙って捨てることはありません。

座標 `point: {"x":80,"z":64}` は整数メートルです。シナリオは既存 `TestData/week1-10v10.json` 形式です。固定小数点の入力は整数のほか `{"raw":5242880}` または `{"numerator":3,"denominator":2}` を許可します。分数はBigIntegerで計算しゼロ方向に切り捨てます。小数JSON数値・float/double経由の変換は使いません。

## .rtsreplay v1

非圧縮・little endian。整数は宣言幅、boolは0/1のbyte、enumはbyte、Fix64はint64 Raw、文字列はuint32 UTF-8バイト長＋本文、配列はuint32件数＋要素です。文字列1 MiB、ヘッダー/レコード64 MiB、シナリオ/命令配列100000件を上限に検証します。

- ファイル: ASCII `RTSRPL01` (8 bytes)、uint32 schemaVersion=1、uint32 headerLength、header、records。
- ヘッダー: rulesVersion文字列、int32 tickRateHz、uint64 seed、int64 tickLimit、uint32 scenarioLength＋ScenarioBinary、ビルド情報。
- ビルド情報: commit文字列、dirty bool、純C#ソースSHA-256文字列、Editor版文字列、実行バックエンド文字列、package lock SHA-256文字列。
- レコード: uint32 recordLength（自身の4byteを除く）、byte kind、int64 tick、uint64 LogIndex、uint32 payloadLength、payload SHA-256 (32 bytes)、payload。`recordLength=53+payloadLength`。
- Input=1: `InputBinary` がScheduledInput、各PolicyOrder、依存版を宣言順に明示保存。
- TickHash=2: State SHA-256＋Event SHA-256（各32byte）。S0から毎tick。
- DiagnosticCheckpoint=3: 正規状態バイト列。S0と100tickごと。
- End=4: Fault bool。最終tickと最終入力LogIndexはレコードの共通部分。終了後のデータ、終了欠落、tick欠落・重複、未知種別、長さ不整合、SHA不一致は拒否。

ScenarioBinaryは schemaVersion、scenarioId、seed、tickRate、verificationTickLimit、map、rules、兵種、陣営、軍団、兵士、コア、拠点の順です。各型の全設定と全初期配置を保存し、ID順へ正規化します。1週目の次ID初期値は保存した各配列長+1（陣営は3）、乱数はseed、敵プリセットは既存のお任せ1種類で一意に決まります。外部シナリオへのパス参照はありません。

純C#ソース集合はContracts/Decision/Simulation/Replay/Applicationの全.csをビルド時にCLIへ埋め込み、CLI側で相対パスのOrdinal順にuint32 UTF-8パス長＋パス、uint32内容長＋内容を連結してSHA-256を計算します。内容はUTF-8、BOM除去、CRLF→LF。チェックアウト後の改行差やビルド後のソース編集で誤認しません。commit/dirty/Editor/package lockは起動時のリポジトリ情報です。
標準再生はrules/schema/ソースハッシュの不一致を拒否します。意図的な互換性確認だけ `--allow-build-mismatch` を replay/compare に指定できます（rules/schemaは緩和しません）。各実行の識別子はhashesヘッダーに残ります。

## 正規状態と差分

正規状態schema=1は uint32版の後に、名前（uint32 UTF-8長＋本文）、byte型タグ、値を順に書きます。タグ1=byte、2=bool、3=int32、4=uint32、5=int64、6=uint64、7=文字列、8=uint32長付きバイト列。名前もハッシュに含めます。

設定/ルール→tick/勝敗→次ID→乱数状態/回数→ID順の兵士→軍団→拠点→コア→陣営→入力カーソル/命令→接触対応→AIメモリの順です。命令はApplyTick→LogIndex→軍団ID。兵士には墓石、攻撃次tick、移動目標、移動量、撤退等のフラグ、兵種パラメータを含めます。Immutableな初期定義はConfig.Hashで覆います。独立したAIメモリは空です。全可視観測・フレーム・索引・ダメージ集計バッファは状態から再構築するため除外します。将来の霧・予約状態追加時は正規状態schemaとrulesの互換性を見直します。

`.hashes` はUTF-8 JSON Lines。先頭にschemaVersion / ReplayHash / ReplayPath / Build、以降にTick / StateHash / EventHash。対応する `.hashes.states` は各tickの int64 tick、uint32長、正規状態を保存します。**比較時には両ファイルを一緒に運びます**。全tickの診断を保存するためディスク量はtick数に比例します。`--dump-dir` は追加で100tickごとのバイナリと読みやすいテキストを出力します。

compareは最初の不一致/欠落tickを見つけ、元の再生ファイルが利用可能な両実行を先頭からそのtickまで再実行します。`<left>.diff/` にt−1/tの再実行結果と両実行の保存済み診断を出し、最初の異なるフィールドパスと左右の整数/Raw値、両ビルド、入力カーソル、乱数状態を表示します。元ファイルが移動した場合は `--replay` を指紋一致で代用できます。診断側のSHAは各hashes値と照合します。過去のビルドを再実行できなくても、保存された当tickの左右の状態があれば比較できます。状態がなければ不足を明記し、値をハッシュから捏造しません。

EventHashは1週目の公開済み終端イベント列を明示的にハッシュ化します。通常戦闘イベント・フェーズ別ハッシュ・不完全ファイルの部分診断再生は今回追加していません。終了欠落は常にコード3です。

## 2経路マップ

同じrecordコマンドのシナリオを `TestData/week2-2routes.json` にすると40人・2経路・占領ありで実行できます。配置・移動・占領の規則と実行例は [TestData README](../../TestData/README.md) を参照してください。FocusのgoalにOutpost=2も指定できます。ルール版はweek2-1で、旧week1-1再生は版不一致として拒否します。1週目のJSONの再記録は引き続き可能です。

正規状態には軍団のPath（セル列）・PathCursor・PathGoal・HasPathGoal・PathImpossible・AutoStage、および拠点のOwnerFactionId・CapturingFaction・CaptureTicksを追加しています。正規状態のフィールド表現はschema=1を継続し、ルール版で互換性を区別します。

## bench: tick時間の計測

ビルドを先に完了し、他のビルド・テストを停止してから実行します。

```powershell
dotnet build Headless/Rts.Headless.slnx --configuration Release
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll bench --scenario TestData/week3-80.json --ticks 20000 --warmup 200 --out D:/rts-verify/24/release-80.json
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll bench --scenario TestData/week3-80.json --ticks 20000 --warmup 200 --record D:/rts-verify/24/release-80.rtsreplay --out D:/rts-verify/24/release-80-record.json
```

出力先ディレクトリは事前に作成します。`--out` 省略時は標準出力だけにJSONを出します。`--record <file>` で実際のreplay書き出しを有効化し、省略時はファイル生成・replayレコード組立を行いません。両モードとも毎tickの正規状態とStateHash/EventHashを計算します。`--inputs <file>` はrecordと同じ入力JSONです。

`--ticks` は1〜シナリオ検証上限、`--warmup` は0〜シナリオ検証上限（既定200）。warmupは別インスタンスで実行し、その後S0から改めて指定tick数を測ります。S0は統計から除外し、終了した場合はそのtickまで。兵数は初期人数であり、通常の増援・死亡は継続します。

`Compute` はtick処理と正規状態・ハッシュ計算、`ReplayIO` はreplayレコード組立・書き出し区間、`TickWithIO` は両者を含むtick時間です。平均、nearest-rank方式のp50/p95/p99、最大、合計をミリ秒で出します。`WallTotalMs` は初期化、S0、ヘッダー・End、最終flush/closeも含み、warmup・結果JSON保存は除外します。通常のバッファ付きファイルI/Oであり、tickごとのディスク同期完了時間ではありません。

段階ごとの値は排他的時間です。経路探索は呼出元AI・移動時間から差し引き、同一tickの複数呼出しを合計します。詳細と実測結果は [performance.md](../../docs/performance.md) を参照してください。計測値はCLI側だけで保持し、Simulationには時間値を返しません。Contractsと正規状態の形式は変更しません。

## analyze: バランス指標

シナリオJSONの `rules.occupationThreatMemoryTicks`（既定200）と `rules.defaultReservePermille`（既定100）で比較値を指定できます。JSON schemaは1を維持します。ScenarioBinaryは既定値なら従来のv1をそのまま書き、異なる場合はv2としてrulesの末尾にint32の記憶tick・uint16の予備‰を追加します。読み込みはv1（200／100を補完）とv2の両方に対応します。外側の再生schema=4、rulesVersion、正規状態の形式は変更せず、比較値は既存のConfig.Hashに含まれます。旧ビルドはシナリオv2を拒否します。

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll analyze --in D:/rts-verify/59/none.rtsreplay --out D:/rts-verify/59/none.indicators.json
# ファイルを書けない環境ではメモリ内に通常形式で記録し、独立したSimulationで全tick再生する
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll analyze --scenario TestData/week2-2routes.json --ticks 20000 --east-preset maintain
```

`--out` 省略時はJSONを標準出力へ出します。`--in` と `--scenario` は排他です。シナリオ指定時は `--ticks` が必須で、`--west-preset` / `--east-preset` はrecordと同じ4種（既定none）です。既存記録の意図的なビルド間互換検証には `--allow-build-mismatch` が使えます。

計測はCLIの `IndicatorCounter` が既存の `DiagnosticComparison.Fields` を毎tick読みます。Simulation・Contracts・正規状態・再生形式・既定値は変更しません。通常のReplayRunnerで記録内のStateHash/EventHash・命令結果・チェックポイントも照合し、`FirstMismatchTick` を出します。終了コードは一致0、不一致2、形式不正3、Fault4です。`.hashes.states` は生成しません。

集計の定義：

- S0は初期値のみ。時間はS1〜最終tickの更新後状態を1tickずつ数え、終了tickを含みます（拠点ごとの中立＋西＋東の合計＝LastTick）。陣営1が西、2が東です。
- コアHPがS0より初めて低下したtickをFirstCoreHitTickに記録します。EndTickは実際にHasEndedになったtickのみで、上限未決着はnullです。Resultは破壊コアID／Draw／Undecided／Faultを区別します。
- 集合失敗は前tickがGatheringまたはWaitingToAdvanceで、その攻勢がIdleになったか、攻勢Idが変わった回数です。目標取得・配分による解除も含み、600tick待機失敗だけの値ではありません。Gatheringのまま終了した未解除攻勢は数えません。
- AdvancesはGathering／WaitingToAdvanceからAdvancingになった各tickの、進撃軍団に所属する全生存兵数です。集合半径内の人数ではありません。同一攻勢の再集合後の再進撃も数え、途中合流だけでは加算しません。同tickで新攻勢が進撃まで進んだ場合も記録します。合計は延べ人数、平均の分母は進撃回数です。
- Retriesは以前解除されたことのある同じGoal.Kind＋Goal.Idで新しい攻勢が作成された回数です。直前以外の解除目標も対象とし、同じIdの再集合・比較は数えません。Offensesに作成・解除のtickと目標を残します。
- 守備時間はAssignmentがGuard／Reserve／CoreDefenseの軍団tickです。軍団別内訳と陣営合計を出します。人数重み付けはせず、死亡・帰還中や人間命令下でも診断のAssignmentをそのまま数えます。そのため、人間Defendの実拘束時間すべてを表すものではありません。
- 拠点は中立0・西1・東2の所有tick数と所有変更履歴を記録します。
- FirstCoreHit／Finalは生存数・軍団別生存数とAssignment・コアHP・所有者を記録します。CoreDefenseAliveはコア防衛に割り当てられた軍団の生存兵数であり、物理的にコアに到着した人数ではありません。初回被弾の次tick以降の増援数・死亡数・CoreDefenseへの配分変更回数も陣営別に記録します。
- PhaseTicks、ReserveShortfallTicks、解除時の帰還軍団数・劣勢40tick到達軍団数・除外目標数は、未決着の原因調査用です。解除理由そのものは診断にないため、これらだけで原因を断定しません。

## grace: 判断猶予の測定

技術設計11章の判断猶予です。同じ命令1件を受付tick R を変えて先頭（tick 0）から何度も再生し、成功述語を満たす R の集合と最終成功tickを出します。**成功が R について単調とは仮定しないため、途中で失敗しても走査を打ち切りません。**

```powershell
dotnet build Headless/Rts.Headless.slnx --configuration Release
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll grace --scenario TestData/week2-2routes.json --ticks 3000 --faction 1 --criterion reinforcement --outpost 1 --observed-tick 0 --order-kind Defend --order-scope Outpost --order-scope-id 1 --order-goal Outpost --order-goal-id 1 --min-r 1 --max-r 41 --r-step 20 --out D:/rts-verify/29/reinforcement.json
```

オプション：`--scenario`（必須）、`--ticks`（1実行のtick上限。既定はシナリオのverificationTickLimit）、`--faction`（既定1）、`--criterion`（`core-defense` / `retreat` / `reinforcement` / `diversion`、既定core-defense）、`--army`（retreat用）、`--outpost`（reinforcement・diversion用）、`--observed-tick`（初観測tick。猶予の起点で、呼び出し側が与える入力です）、`--min-r`（既定1）、`--max-r`（既定は`--ticks`）、`--r-step`（既定1）、`--input-delay`（既定60）、`--out`。命令の中身は `--order-kind`（PolicyKind名、既定Defend）、`--order-scope`（All/Army/Outpost、既定All）、`--order-scope-id`、`--order-goal`（None/Point/Outpost/Core）、`--order-goal-id`、`--reserve-permille` で指定します。命令は測定側が毎回 Reserve+Resolve の組に合成し、CommandId・TargetRevision・ObservedTick を実行ごとに付け直します。

出力は `Immediate`（受付tick R でそのまま適用）と `Delayed`（理解・入力時間として **R + 60 tick** で適用。R自体は操作側の時計のまま記録）の2件です。各件に成功・失敗・未評価・未適用のR一覧、`LastSuccessTick`、`GraceTicks`（＝最終成功tick − 初観測tick）、`Verdict` が入ります。

- `Grace`：成功Rが1つ以上ある。`GraceTicks` が猶予です。
- `NoGrace`（猶予なし）：適用できたRがすべて判定済みの失敗。
- `Unevaluated`（未評価）：未判定のRが残る、または上限を越えて命令が適用されなかった（`NotAppliedTicks`）。

成功述語の判定基準：

- **コア防衛**：測定陣営のコアが破壊されなければ成功。破壊（相手勝利または両者コア0の引分け）が唯一の失敗で、上限まで無事なら成功です。「上限内で破壊を回避できた」という意味で、勝利ではありません。
- **撤退**：指定軍団の生存者 × 2 ≧ 開始時人数なら成功（丸めを避けた50%以上。8人なら4人、5人なら3人）。増援で補充され得るため、途中では判定せず実行終了時の人数で決めます。
- **増援**：拠点の所有者が敵になった時点で失敗。敵所有でないまま自陣営の歩兵が占領半径内に入った時点で成功（防衛到着）。どちらも起きないまま上限に達したら未評価です（争われなかっただけでは「間に合った」と言えないため）。拠点が中立で始まる既存シナリオを許容し、開始時点で敵所有の場合だけ引数エラーにします。
- **陽動対応**：指定拠点とコアの**両方**を失ったときだけ失敗。どちらかを保持していれば成功です。

拠点の所有者は「所有陣営は自拠点を常に視認できる」性質を使い、各陣営のフレームが自分の所有だと申告したかどうかで判定します（敗れた側の古い記憶を排除するため）。

実行時間は「1実行のtick数 × Rの候補数 × 2（即時・+60）」に比例します。week2-2routes（40人・経路探索あり）で概ね 7ms/tick 程度のため、R を1tick刻みで上限まで全探索すると実用的でない場合があります。`--max-r` と `--r-step` で候補を絞れます。終了コードは正常出力0（猶予あり・なし・未評価いずれも0）、形式・引数エラー3、Fault等の異常4です。`analyze` と違いハッシュ計算・ファイルI/Oの再生経路は通らず、Simulationを直接回します。
