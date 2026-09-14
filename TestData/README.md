# 1週目の10対10シナリオ

`week1-10v10.json` は技術設計書5.1章の初期配置を展開した試験データです。各陣営は北歩兵4・南歩兵4・予備歩兵1・偵察1。全8192セルを通行可能とします。シミュレーションやJSONローダーは後続Issueで実装します。

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
