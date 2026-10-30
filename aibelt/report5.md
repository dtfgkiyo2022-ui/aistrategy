# aibelt5 実装・検証報告

実施日：2026-10-30

## 実装

- 拡張欄85（version 1）に `AiBeltPartsGearsStockTarget`（既定値8）を追加し、拡張長・読書き・検証・コピーを更新した。
- `AiBeltParts` がオンのときのギア在庫目標を、既存の BeltCraft 目標と新しい目標の最大値にした。ギア工場の建設、ギア生産、在庫上限判定を同じ目標で扱う。
- 自動線終端の自陣ベルト横に置ける合流機を、資材不足時に陣営別FIFOキュー（最大32件）へ遅延登録し、次のベルト判断の経路探索前に最古の有効候補を1件だけ再試行するようにした。キューは旗オン時だけ正規状態ハッシュへ含めた。
- 既存の自陣ベルトを横切る直進地下ベルトを、入口から最大長内の最初の空きセルまでとして実装した。1経路あたり最大2組、経路開始時に必要資材を払える場合だけ使用し、通常経路探索と処理線探索の双方に適用した。
- ルート予算、乱数シード、世代スタンプ、ワークスペース配列の再利用を維持した。診断には合流キュー、地下横断（採り場線・加工線）、地下失敗理由、ギア生産・在庫を追加し、既存のaibelt4カウンターは維持した。
- docs 38.19 に aibelt5 の実装判断を追記した。

主な変更ファイル：

- `UnityProject/Assets/RTS/Simulation/ScenarioDefinition.cs`
- `UnityProject/Assets/RTS/Simulation/LiveGameRules.cs`
- `UnityProject/Assets/RTS/Simulation/ScenarioBinary.cs`
- `UnityProject/Assets/RTS/Simulation/WorldState.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Industry.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.IndustryAi.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.AiFactory.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.DropSites.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Diagnostic.cs`
- `UnityProject/Assets/RTS/Simulation/LargeEconomyObservation.cs`
- `Headless/Rts.Headless.Cli/TacticMatchCommand.cs`
- `Headless/Rts.Core.Tests/AiBeltPartsTests.cs`
- `Headless/Rts.Core.Tests/BeltCraftTests.cs`
- `docs/technical-design-v3.md`

## ビルドとテスト

Release構成を、指定オプション付きでビルドした。

`dotnet build Headless/Rts.Core.Tests/Rts.Core.Tests.csproj -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false --no-restore`

結果は警告0、エラー0。対象グループを個別に実行し、全体スイートは実行していない。

| グループ | 成功 / 合計 |
|---|---:|
| `AiBeltPartsTests` | 10 / 10 |
| `BeltTests` | 15 / 15 |
| `BeltStackTests` | 6 / 6 |
| `BeltCraftTests` | 14 / 14 |
| `BeltDropOffAiTests` | 4 / 4 |
| `ScenarioExtensionTests` | 11 / 11 |
| 合計 | **60 / 60** |

追加テストでは、ギア工場と生産、資材不足合流機の遅延・再接続、既存ベルトを横切る地下経路、拡張欄85の往復を確認した。旗オフ時の既存ハッシュ挙動も確認した。

## 旗オフ照合

指定された基準CLIを実行し、変更後CLIでreplayを再生した。

- 基準 `tactic-match`：終了コード0、`ticks=25442 ended=True winner=1`
- 変更後 `replay`：終了コード0
- 最終行：`Replay matched S0..S25442`

確認後、`aibelt/x.rpl` と `aibelt/y.hashes` は削除した。

## 計測

条件は `--terrain --map-size large --ticks 40000 --timing-out`。p99は `stepMs` の昇順で `ceil(0.99N)` 番目とした。オンの3試合は10分上限に達したため、その時点で停止した。

| seed | 旗 | 結果 | 最終tick | p99 (ms) | 最大 (ms) | 兵ピーク合計 |
|---:|:---:|---|---:|---:|---:|---:|
| 1 | オン | 10分上限、未決着 | 12057 | 24.376 | 261.342 | — |
| 1 | オフ | 決着、勝者1 | 25332 | 51.388 | 271.699 | 1101 |
| 2 | オン | 10分上限、未決着 | 25313 | 22.568 | 219.855 | — |
| 2 | オフ | 決着、勝者1 | 25215 | 51.148 | 369.048 | 1032 |
| 3 | オン | 10分上限、未決着 | 9658 | 15.573 | 224.561 | — |
| 3 | オフ | 決着、勝者1 | 24860 | 33.662 | 222.797 | 1359 |

旗オンseed1のtick12000付近では、ギア生産が陣営1=20・陣営2=8、在庫はいずれも8で、合流キューの登録・接続・破棄も発生した。一方、同時点の実戦ログでは地下横断は0件で、地下失敗は資材不足が主だった。オンの3試合はいずれも決着まで完走できず、実戦での地下横断の発生と決着後の兵ピークは未確認である。

計測CSVは読み取り後に削除し、`aibelt/` に残していない。

## 比較と未確認事項

- 旗オフ3試合はすべて決着し、基準再生照合も一致した。
- 旗オンではギア生産、合流キュー、専用テスト上の地下横断は確認できたが、large実戦の3試合は10分上限で完走しなかった。
- したがって、large実戦で各seedに合流機・地下横断が1件以上発生すること、決着tick、兵ピーク、旗オフ比1.2倍以内のp99を完走状態で判定することは未確認である。
- 全テストスイートと、長時間実戦の全診断カウンターは実行していない。
