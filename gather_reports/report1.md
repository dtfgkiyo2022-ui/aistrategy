# gatherspread1 実装・検証報告

## 実装

38.23 の 1〜4 を実装した。`GatherSpread` は `LiveGameRules` の大規模経済で初期オン、`--gather-spread-off` でオフにできる。点ごとの人数と、点容量から計算したかたまりの空き容量を `RefreshLargeWorkCounts` と同じ tick 集計で再利用配列に保持し、割り振り時は点・かたまりの満杯を飛ばす。決めた資源の行き先が無い場合は、食料・木・石を在庫/低い目安の小さい順に試す。

変更した主なファイルは次のとおり。

- `UnityProject/Assets/RTS/Simulation/ScenarioDefinition.cs`、`LiveGameRules.cs`、`WorldState.cs`：設定、初期値、検証、コピー。
- `UnityProject/Assets/RTS/Simulation/Simulation.cs`、`Simulation.Economy.cs`：点/かたまり容量、代替割り振り、待機計測。
- `UnityProject/Assets/RTS/Simulation/LargeEconomyObservation.cs`：理由別・待機・代替の診断値。
- `UnityProject/Assets/RTS/Simulation/ScenarioBinary.cs`：拡張欄 86 version 1 の読み書き。
- `Headless/Rts.Headless.Cli/Program.cs`、`TacticMatchCommand.cs`：CLI 旗と CSV 列。
- `Headless/Rts.Core.Tests/GatherSpreadTests.cs`、`Rts.Core.Tests.csproj`：新規テスト。
- `Headless/Rts.Core.Tests/StockAwareGatherTests.cs`、`AiExpandTests.cs`：既存テストを旗オフ固定。
- `docs/technical-design-v3.md`：38.23 末尾に実装上の決定を追記。

旗オフでは拡張欄 86 を出力せず、GatherSpread 固有の集計・診断を実行しない。価格、試合長、時代時計、攻撃、手動操作、Contracts は変更していない。

## テスト

Release 構成で、指定された `-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false` を付け、全体テストは実行せず 1 グループずつ実行した。

| グループ | 結果 |
|---|---:|
| GatherSpreadTests | 6/6 成功 |
| StockAwareGatherTests | 9/9 成功 |
| AiExpandTests | 13/13 成功 |
| LargeEconomyTests | 6/6 成功 |
| LargeEconomyEcon3Tests | 4/4 成功 |
| LargeEconomyEcon5Tests | 3/3 成功 |
| ScenarioExtensionTests | 11/11 成功 |

CLI の Release build も成功（警告 0、エラー 0）。AiExpandTests は 6 分 8 秒かかったが、10 分以内に完了した。全体テストは未実行。

## 照合

ベース CLI の指定コマンドは終了コード 0 で、`ticks=25442 ended=True winner=1`。最後の出力行は次のとおり。

`craft4: faction=2 assemblers=0 siegeWorkshops=1 siegeWorkshopComplete=1 siegeWorkshopQueued=0 siegeParts=0 mechanism=0 siegeEngine=0 siegeEngineProduced=0 siegeEngineConsumed=0 eliteKit=0 eliteKitProduced=0 eliteKitConsumed=0 cannonProduced=0 peakSoldiers=417`

その rpl を変更後 CLI で replay した結果も終了コード 0。最後の行は `Replay matched S0..S25442`。読み取り後、`gather/x.rpl` と `gather/y.hashes` は削除した。

## 決着までの測定

全て `--map-size large --terrain --ticks 40000 --timing-out`、種 1〜3、ON/OFF とも決着まで実行した。待機値は西/東、理由と代替は西+東の累計。OFF の理由・代替・待機値が 0 なのは、その診断を旗オン時だけ記録する仕様による。

| 種 | 旗 | 決着 tick / 勝者 | 兵ピーク合計 | p99 ms / 最大 ms | 待機最大 西/東 | 50人以上の最長 西/東 | 知らない / 残りなし / 道なし / 満杯 | 代替 |
|---:|:---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | ON | 25804 / 1 | 499 | 37.085 / 534.416 | 172 / 53 | 6924 / 26 | 39 / 0 / 688439 / 1912492 | 1636 |
| 1 | OFF | 25340 / 1 | 1469 | 62.982 / 329.091 | 0 / 0 | 0 / 0 | 0 / 0 / 0 / 0 | 0 |
| 2 | ON | 25486 / 2 | 820 | 52.534 / 1162.037 | 163 / 229 | 5247 / 15034 | 10 / 0 / 7046620 / 5065994 | 712 |
| 2 | OFF | 25376 / 1 | 1176 | 34.782 / 251.450 | 0 / 0 | 0 / 0 | 0 / 0 / 0 / 0 | 0 |
| 3 | ON | 24711 / 1 | 1123 | 72.926 / 1245.092 | 184 / 209 | 14153 / 14712 | 10 / 0 / 8674862 / 5036238 | 694 |
| 3 | OFF | 25228 / 1 | 1160 | 38.862 / 399.484 | 0 / 0 | 0 / 0 | 0 / 0 / 0 / 0 | 0 |

## 合格の目安との比較

- 「待機 50 人以上」が 200 tick を超えない：3 種すべて未達。ON の最長は種 1 が 6924、種 2 が 15034、種 3 が 14712。
- 兵ピークが OFF の 0.95 倍以上：種 1 は 0.340 倍、種 2 は 0.697 倍で未達、種 3 は 0.968 倍で達成。
- 決着が OFF より 10% を超えて遅くならない：種 1 は 1.018 倍、種 2 は 1.004 倍、種 3 は 0.980 倍で、全て目安内。ただし種 2 は勝者が ON=2、OFF=1 で異なる。
- p99 が OFF の 1.1 倍以内：種 1 は 0.589 倍で達成、種 2 は 1.510 倍、種 3 は 1.875 倍で未達。
- 1 tick が 1 秒を超えない：種 1 は達成、種 2（1162.037ms）と種 3（1245.092ms）は未達。

## 確認できていないこと

全体テスト、Unity Editor 上のビルド、実ゲーム接続での長時間動作は確認していない。測定では機能テストと拡張欄往復は通過した一方、上記の性能・待機・兵力目安には未達が残ったため、次段では容量配分後の再割り振りと大規模マップでの経路待ちの切り分けが必要である。
