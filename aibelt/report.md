# aibelt3 実装報告

実施日：2026-10-30

## 実装

途中まで入っていた aibelt3 の変更を確認し、指定された実装・テスト・照合・測定を完了した。

- `BeltRoute` の合流判定から始点距離の制限を外し、非 `Held`・非部品の自陣ベルトに当たった地点から `AiMergeMaxCells`（初期値 400）まで世代スタンプ付き幅優先探索を行うようにした。各セルは探索ごとに一度だけ調べる。
- 採り場線で通常経路が縦横距離×`AiUndergroundDetourPermille`（1500‰）＋4を超える場合、地下経路を試して置くマス数を比較するようにした。
- 時代2以上で通常経路が `AiCatapultMinRouteCells`（40）を超える場合、投石機・受け台経路を試し、通常経路の半分以下の配置マスで購入可能なときだけ採用するようにした。
- 地下ベルト・投石機の試行は採り場線 `DecideBeltDropOffAi` に限定し、加工線は合流判定だけを使用する。判断ごとの部品経路探索上限12回、候補ごとの上限6回、探索配列の再利用と変更セルだけの復元を維持した。
- 拡張欄85（version 1）に `AiUndergroundDetourPermille` と `AiCatapultMinRouteCells` を追加し、CLI と `LiveGameRules` の初期値を設定した。
- 旗 `AiBeltParts` がオフのときは従来経路・状態を使用し、拡張欄85も書き出さない。

主な変更ファイル：

- `UnityProject/Assets/RTS/Simulation/Simulation.IndustryAi.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.DropSites.cs`
- `UnityProject/Assets/RTS/Simulation/LiveGameRules.cs`
- `UnityProject/Assets/RTS/Simulation/ScenarioDefinition.cs`
- `UnityProject/Assets/RTS/Simulation/ScenarioBinary.cs`
- `UnityProject/Assets/RTS/Simulation/WorldState.cs`
- `Headless/Rts.Headless.Cli/TacticMatchCommand.cs`
- `Headless/Rts.Core.Tests/AiBeltPartsTests.cs`
- `docs/technical-design-v3.md`

## ビルドとテスト

Release 構成で次のオプションを付けてビルドした。

`dotnet build Headless\Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`

結果：成功、警告0、エラー0。

対象テストは全て1グループずつ実行した。

| グループ | 成功 / 合計 |
|---|---:|
| `AiBeltPartsTests` | 7 / 7 |
| `ScenarioExtensionTests` | 11 / 11 |
| `BeltTests` | 15 / 15 |
| `BeltStackTests` | 6 / 6 |
| `BeltCraftTests` | 14 / 14 |
| `BeltDropOffAiTests` | 4 / 4 |
| 合計 | **57 / 57** |

`AiBeltPartsTests` では、12マス超の合流、川越し地下ベルト、時代2の長距離投石機・受け台、荷の核への到達、拡張欄85、判断上限、旗オフの毎tickハッシュ一致を確認した。

## 旗オフ照合

指定コマンドでベース CLI の seed 2・large・terrain・40000 tick を実行した。

- ベース `tactic-match`：終了コード 0、決着 tick 25442、勝者 陣営1
- 変更後 CLI の `replay`：終了コード 0
- 最終行：`Replay matched S0..S25442`

確認後、`aibelt/x.rpl` と `aibelt/y.hashes` を削除した。

## 測定

条件は全て `--map-size large --terrain --ticks 40000 --timing-out`、決着まで。p99 は tick の `stepMs` を昇順にした最近傍順位（ceil(0.99N)）で計算した。部品欄の順序は「分岐器 / 仕分け器 / 地下入口 / 投入機 / 合流機 / 絞り込み投入機 / 長い投入機 / 投石機 / 受け台」で、値は「陣営1 / 陣営2」。

| 種 | 旗 | 決着tick | 勝者 | p99 (ms) | 最大 (ms) | 1秒超tick | 兵ピーク合計 | 部品数（陣営1 / 陣営2） | 投石機配送 | 上限停止 |
|---:|:---:|---:|:---:|---:|---:|---:|---:|:---|---:|---:|
| 1 | オン | 25340 | 1 | 56.706 | 414.948 | 0 | 1455 | 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 | 0 / 0 | 0 / 0 |
| 1 | オフ | 25332 | 1 | 50.483 | 286.065 | 0 | 1100 | 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 | 0 / 0 | 0 / 0 |
| 2 | オン | 25248 | 1 | 36.117 | 290.357 | 0 | 732 | 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 | 0 / 0 | 0 / 0 |
| 2 | オフ | 25215 | 1 | 49.910 | 381.482 | 0 | 1031 | 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 | 0 / 0 | 0 / 0 |
| 3 | オン | 25129 | 1 | 90.752 | 257.006 | 0 | 996 | 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 | 0 / 0 | 0 / 0 |
| 3 | オフ | 24860 | 1 | 51.209 | 302.154 | 0 | 1359 | 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 / 0/0 | 0 / 0 | 0 / 0 |

旗オンのクラスタ追加試行は、種1が陣営1/2で215/237回、種2が100/208回、種3が64/236回だった。部品配置・投石機配送・上限停止は全6試合で0だった。実戦測定では、通常経路が地下・投石機の採用入口に入った回数と、採用しなかった理由の内訳を個別カウンターとしては記録していないため、0件の理由はコード経路上の判定から「通常経路が長さ条件に達しなかった」と推定している。

## 合格目安との比較

- 合流機・地下ベルト・投石機を種1〜3の各実戦で1つ以上：**未達**。6試合とも0。合流・地下・投石機そのものの配置と荷の到達は専用テストで確認済み。
- p99 は旗オフの1.2倍以内：種1は約1.12倍、種2は約0.72倍、種3は約1.77倍。**種3で未達**。
- 1秒を超える tick：全6試合で0。**達成**。
- 決着の遅延：種1 +0.03%、種2 +0.13%、種3 +1.08%。**達成**。
- 兵ピーク合計：旗オン 3183、旗オフ 3490、比率約0.912。全体合計では0.9倍以上。種ごとでは種2・3が0.9倍未満。

## 確認できていないこと

- 種1〜3の長時間実戦で、合流機・地下ベルト・投石機が実際に置かれるケースは確認できなかった。
- 実戦での地下・投石機の条件発生数と不採用理由の個別カウンターは未実装・未計測。
- 全テストスイート、`BeltDropOffTests` 全体、長時間の `AiReasonsTests` は実行していない。

測定 CSV は集計後に削除し、`aibelt/` には本報告書だけを残した。
