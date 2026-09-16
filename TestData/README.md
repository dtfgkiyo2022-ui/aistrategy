# 1週目の10対10シナリオ

`week1-10v10.json` は技術設計書5.1章の初期配置を展開した試験データです。各陣営は北歩兵4・南歩兵4・予備歩兵1・偵察1。全8192セルを通行可能とします。シミュレーションとJSONローダーはHeadless CLIから利用できます。

## 数値と単位

座標はX/Z平面、原点・東西の向きは設計書どおりです。`positionMeters` の `x` / `z`、名前に `Meters` を含む距離はメートルの整数で、**Raw値ではありません**。`speedMetersPerSecond` は整数m/sです。Fix64への変換は `checked(整数値 * 65536)`。速度から1tick移動量への変換は設計書4章に従い `speedRaw / tickRateHz` をゼロ方向に切り捨てます。HP・ダメージは整数、`Ticks` / `TickLimit` はtick数です。

## フィールド

| フィールド | 意味 |
|---|---|
| `schemaVersion` | このJSON形式の初版=1。再生ファイルのschemaとは別管理 |
| `scenarioId` | シナリオ識別名 |
| `seed` | ulongの初期seed。設計書7章の既知ベクトルに合わせ0を選択。初期配置生成に乱数は使わない |
| `tickRateHz` | 20tick/秒 |
| `verificationTickLimit` | 検証打切り36000tick。未決着終了であり引分けではない |
| `map.widthMeters`, `heightMeters`, `cellSizeMeters` | マップ256×128m、セル一辺2m |
| `map.widthCells`, `heightCells` | 128×64セル |
| `map.defaultPassable`, `blockedCellIds` | 全セルの既定通行可否と通行不可セル一覧。今回はtrueと空配列。セルIDは `z*128+x`、0始まり |
| `rules.factionCap` | 陣営の生存人数上限40（初期人数10とは別） |
| `rules.coreRadiusMeters`, `ownedObjectiveVisionMeters` | コア半径4m、所有拠点・生存コアの視界24m |
| `rules.captureRadiusMeters`, `captureDurationTicks` | 占領半径8m、継続200tick |
| `rules.coreReinforcementIntervalTicks`, `outpostReinforcementIntervalTicks` | 歩兵1人の増援間隔100/200tick |
| `rules.occupationThreatMemoryTicks`, `defaultReservePermille` | 占領脅威の記憶（既定200tick、0以上）／お任せの既定予備（既定100‰、0〜1000）。明示したMaintainReserve命令を優先 |
| `unitParameters` | 兵種ごとの `kind`, `hp`, `speedMetersPerSecond`, `visionMeters`, `rangeMeters`, `damage`, `attackIntervalTicks` |
| `factions` | 陣営の `id`, `coreId`, `armyIds` |
| `cores` | コアの `id`, `factionId`, `positionMeters`, `hp`（初期3000） |
| `outposts` | 拠点の `id`, `positionMeters`, `ownerFactionId`（0=中立）。HPなし |
| `armies` | 軍団の `id`, `factionId`, `role`, `capacity`, `homeObjective` |
| `armies.role` | `north` / `south` / `reserve` / `scout`。このJSON内の役割名でありwire enumではない |
| `armies.capacity` | 編成枠16/16/6/2 |
| `armies.homeObjective` | `kind` はGoalKind（3=Core）、`id` は対象ID。初期所属は自コアとした（設計書に明記なし） |
| `soldiers` | 兵士の `id`, `factionId`, `armyId`, `kind`, `alive`, `positionMeters`, `hp`。全20人を明示保存 |

兵種の数値は設計書に指定がないため `UnitKind.Infantry=1`, `UnitKind.Scout=2` と定義しています。HP等の兵種パラメータは設計書どおりです。

IDは種類ごとのuintで0は無効（中立所有者・セルIDは例外）。陣営1=西、2=東。軍団は西の北・南・予備・偵察、続いて東の同順で1〜8。兵士は軍団順・ローカル番号順で1〜20。コアは西/東=1/2、拠点は北/南=1/2です。兵士位置は基準点に `(i%4, i/4)` mを加え、東側のみXを `256-X` にした結果です。ロード・再生時は保存座標とIDを使い、配置を再生成しません。

これは初期シナリオのデータで、実行途中の完全なWorldStateではありません。S0の視界、クールダウン、増援時計等の初期化と妥当性検査は後続のシミュレーション実装が担当します。

## Headless CLI

シナリオの読込み・記録・再生は [Headless CLI README](../Headless/Rts.Headless.Cli/README.md) を参照してください。
`week1-inputs.json` は40tickに軍団1を撤退させる確定入力列です。数値enumと全PolicyOrderフィールドを記述しています。

## 2週目の2経路・40人シナリオ

`week2-2routes.json` は北道z=[88,104)、南道z=[24,40)、接続路x=[16,32)・[224,240)のセル中心判定で、通行不可セルIDを全て明示しています。各陣営は北8・南8・予備2・偵察2、兵士IDは1〜40。配置は設計書5.1章の生成結果を保存し、CLIと再生は保存済みの座標を使います。純C#の `WeekTwoScenario.Create()` も同じ定義を生成します。

北・南軍団のHomeObjectiveは担当拠点（GoalKind.Outpost=2）、予備・偵察は自コアです。仮AIは北・南の道を敵側接続路まで進んでから敵コアへ向かいます。予備は自コア付近で待機し、偵察は自側の北道へ出てから敵側へ前進します。本格AI・増援・霧は後続Issueの範囲です。

軍団で経路とカーソルを共有し、全生存兵が各隊列目標の0.1m以内に入ると次セルへ進みます。隊列番号は初期兵士ID順で固定し、死亡で詰め直しません。通行不可または直線で壁をまたぐ隊列目標は経路セル中心へ縮めます。道のない目標や分断された目標は到達可能なセル中心との距離二乗→セルIDで代替し、Pointの4m・Outpostの占領半径・Coreの射程＋半径を満たせなければImpossible/NoPathとして停止します。境界での移動停止は通行可能側に留めます（正方向は境界の1 Raw手前）。

占領開始tickを1として200tick連続で所有が変わり、完了時は進捗と挑戦者を0に戻します。所有者だけが範囲内にいる間も進捗は0です。表示は `FactionFrame.Objectives`（`Observation.Objectives`と同じ不変リスト）のOwnerFactionId・CapturingFactionId・CaptureTicks・CaptureDurationTicksを参照します。現在は既存の全可視観測を継続しています。

ルール版は `week2-1`。シナリオ／再生バイナリの構造は変更していませんが、旧ルールで記録した再生はルール版不一致として拒否します。1週目のJSONは引き続き記録・再生でき、全セル通行可時の直進移動を維持します。

出力先ディレクトリを用意して、リポジトリ直下で次をそれぞれ別プロセスとして実行します。

```powershell
dotnet run --project Headless/Rts.Headless.Cli --configuration Release -- record --scenario TestData/week2-2routes.json --out run.rtsreplay --ticks 4000
dotnet run --project Headless/Rts.Headless.Cli --configuration Release -- replay --in run.rtsreplay --hash-out a.hashes
dotnet run --project Headless/Rts.Headless.Cli --configuration Release -- replay --in run.rtsreplay --hash-out b.hashes
dotnet run --project Headless/Rts.Headless.Cli --configuration Release -- compare --left a.hashes --right b.hashes --replay run.rtsreplay
```

## 3週目の規模計測

`week3-20.json` は2経路・各陣営4/4/1/1の20人設定。`week3-80.json` は同じマップ・配置規則で各陣営16/16/6/2の80体設定。40人設定は `week2-2routes.json` を使用し、いずれも陣営上限40・検証上限36000tick。
`week2-2routes-core6000.json` は `week2-2routes.json` の両コアHPだけを3000から6000に変更した比較用シナリオです。
