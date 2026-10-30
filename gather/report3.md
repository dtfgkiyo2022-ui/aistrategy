# gatherspread3 実装・検証報告

## 実装

- `ScenarioDefinition`、`LiveGameRules`、`WorldState`、`ScenarioBinary` に、点上限0（無制限）と偵察窓・失敗回数・最大巡回tickを追加した。拡張欄86は version 1 のまま6整数を保存する。
- GatherSpreadオン時だけ、資源別の失敗窓、偵察資源・終了tick、発動理由・発見数を観測し、巡回を `ExploreScouting` の経路へ接続した。偵察情報は正規状態・ハッシュへ必要な最小限だけを保存し、オフ時の毎tickハッシュを維持した。
- 点・かたまり容量0の無制限処理、出発セル補正、代替資源、経路判定8回上限、AiExpand回帰シナリオ分離、CLIの `--timing-out` 列を実装した。
- 変更の中心は `UnityProject/Assets/RTS/Simulation/ScenarioDefinition.cs`、`LiveGameRules.cs`、`WorldState.cs`、`ScenarioBinary.cs`、`Simulation.cs`、`Simulation.Economy.cs`、`Simulation.Decision.cs`、`LargeEconomyObservation.cs`、`Headless/Rts.Headless.Cli/TacticMatchCommand.cs`、`Headless/Rts.Core.Tests/GatherSpreadTests.cs`、`Headless/Rts.Core.Tests/AiExpandTests.cs` である。

## テスト

Release、`-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`、グループごとに逐次実行した。全体テストは実行していない。

| グループ | 結果 |
|---|---:|
| GatherSpreadTests | 10/10 |
| StockAwareGatherTests | 9/9 |
| AiExpandTests | 13/13 |
| LargeEconomyTests | 6/6 |
| ScenarioExtensionTests | 11/11 |
| ExploreScoutingTests | 19/19 |

10分を超えた単体テストはない。

## 測定

種1〜3、広い地図、`--ticks 40000 --timing-out`。待機最大は西/東、原因と発見は資源順（食料/木/石）で、西/東を `残りなし / 経路なし / 発見` の順に記載する。

| 種 | 旗 | 決着tick/勝者 | 兵ピーク合計 | p99/max ms | 待機最大 | 50人以上最長 | 偵察巡回 西/東 | 残りなし 西/東 | 経路なし 西/東 | 発見 西/東 |
|---:|:---:|---:|---:|---:|---:|---:|---:|---|---|---|
| 1 | ON | 25749 / 1 | 861 | 22.860 / 212.814 | 88 / 72 | 1 / 1 | 2834 / 3094 | 0/260/0 / 0/249/18 | 0/0/0 / 2/16/0 | 275/42/4 / 227/37/4 |
| 1 | OFF | 25181 / 2 | 1412 | 36.176 / 210.044 | 71 / 102 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| 2 | ON | 25824 / 2 | 1435 | 32.625 / 215.700 | 91 / 89 | 1 / 1 | 1230 / 4105 | 0/361/2 / 0/301/2 | 2/0/0 / 2/0/0 | 234/50/1 / 121/45/0 |
| 2 | OFF | 25173 / 1 | 1096 | 23.132 / 204.550 | 83 / 86 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| 3 | ON | 25531 / 2 | 592 | 18.941 / 219.628 | 52 / 107 | 1 / 2 | 1919 / 864 | 0/273/17 / 0/470/2 | 2/8/7 / 2/0/0 | 193/34/2 / 277/47/0 |
| 3 | OFF | 25466 / 2 | 1141 | 33.160 / 214.427 | 85 / 83 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |

## 合格基準との比較

- 50人以上の待機が200tickを超えない：測定上は全ON条件で1〜2tickとなり達成。
- 兵ピーク0.95倍以上：種1は0.610倍、種2は1.309倍、種3は0.519倍。種2のみ達成。
- 決着tickがOFFより10%以上遅くならない：種1 1.023倍、種2 1.026倍、種3 1.003倍で達成。
- p99がOFFの1.1倍以内：種1 0.632倍、種2 1.410倍、種3 0.571倍。種2は未達。
- 1秒超のtickなし：最大219.628msで達成。

## tactic-match / replay

要求された base CLI コマンドは終了コード0、最終の tactic-match 行は `ticks=25442 ended=True winner=1`。変更後CLIで指定された replay を実行したところ、hashはtick25442まで生成されたが、終了コード1、最終エラーは `Format/version: Premature or inconsistent End.` だった。確認後、`gather\x.rpl` と `gather\y.hashes` は削除した。

## 未確認事項

全体テスト、Unity Editor上のビルド、実ゲーム接続での長時間動作は未確認。replayの形式エラーは未解決であり、測定の兵ピークと種2のp99は基準未達である。
