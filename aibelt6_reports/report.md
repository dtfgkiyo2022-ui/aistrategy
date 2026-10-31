# aibelt6 実装・検証報告

実施日: 2026-10-31

## 実装

- `AiInserterParts` と `AiInserterPartsMinStock` を `EconomyRules` に追加し、`AiBeltParts` 依存の有効化、LiveGameRules の初期値、CLI の `--ai-inserter-parts-off` を実装した。
- Scenario extension 87 version 1 に旗と minimum stock を追加し、既存の extension 85 と同じ enable 条件・roundtrip 規則にした。
- 自動分岐器の加工側優先、processing line の材料別 FilterInserter、既存コア側セルが塞がった場合の LongInserter を追加した。`Held` の分岐器・ベルトは変更しない。
- 既存状態を直接走査し、判断ごとの全地図配列は生成しない。既存の投入機価格、ギア、fitting、加工深度、reserve 規則を利用した。
- `aibeltdiag:` に Filter/Long の条件・配置・失敗理由と優先分岐配置数を追記した。

主な変更ファイル:

- `UnityProject/Assets/RTS/Simulation/Simulation.Inserters.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.DropSites.cs`
- `UnityProject/Assets/RTS/Simulation/ScenarioBinary.cs`
- `UnityProject/Assets/RTS/Simulation/ScenarioDefinition.cs`
- `UnityProject/Assets/RTS/Simulation/LiveGameRules.cs`
- `UnityProject/Assets/RTS/Simulation/WorldState.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Ages.cs`
- `Headless/Rts.Headless.Cli/Program.cs`
- `Headless/Rts.Headless.Cli/TacticMatchCommand.cs`
- `Headless/Rts.Core.Tests/AiInserterPartsTests.cs`
- `Headless/Rts.Core.Tests/Rts.Core.Tests.csproj`
- `docs/technical-design-v3.md`

## ビルド・テスト

Release build は次で実行し、警告0・エラー0だった。

```text
dotnet build Headless/Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
```

全テストスイートは実行せず、指定グループを一つずつ実行した。

| グループ | 結果 |
|---|---:|
| AiInserterPartsTests | 5/5 |
| AiBeltPartsTests | 10/10 |
| BeltTests | 15/15 |
| BeltCraftTests | 14/14 |
| InserterTests | 9/9 |
| ScenarioExtensionTests | 11/11 |

専用テストは、優先分岐と `Held` 保護、FilterInserter の材料選択・加工建物到達、LongInserter の1セル先配置・到達、旗オフの毎 tick hash 同一、extension 87 roundtrip を含む。

## 指定 replay 比較

基準 CLI での `tactic-match --map-seed 2 --terrain --map-size large --ticks 40000` は exit code 0、終了 tick 25442、最後の出力行は faction 2 の `craft4` 行だった。

変更 CLI の replay は exit code 0、最後の出力行は次のとおりだった。

```text
Replay matched S0..S25442
```

確認後、`aibelt6\x.rpl` と `aibelt6\y.hashes` は削除済み。

## 計測

条件は各 seed の large terrain、最大40000 tick、`--timing-out`。ピーク兵数は faction 1+2。部品数は `splitter|sorter|underground|inserter|merger|filter|long|catapult|receiver` の順で、on の値を示す。今回の3 seed では on/off の部品数は同一だった。

| seed | 旗 | 終了 tick / winner | peak soldiers 合計 | p99 ms | max ms | faction 1 parts | faction 2 parts | priority/filter/long |
|---:|---|---:|---:|---:|---:|---|---|---|
| 1 | on | 25340 / 1 | 1469 | 41.619 | 206.308 | 0\|0\|3\|0\|39\|0\|0\|0\|0 | 0\|0\|4\|0\|41\|0\|0\|0\|0 | 0 / 0 / 0 |
| 1 | off | 25340 / 1 | 1469 | 41.541 | 207.520 | 0\|0\|3\|0\|39\|0\|0\|0\|0 | 0\|0\|4\|0\|41\|0\|0\|0\|0 | 0 / 0 / 0 |
| 2 | on | 25376 / 1 | 1176 | 25.352 | 203.776 | 0\|0\|0\|0\|0\|0\|0\|0\|0 | 0\|0\|0\|0\|28\|0\|0\|0\|0 | 0 / 0 / 0 |
| 2 | off | 25376 / 1 | 1176 | 25.796 | 207.563 | 0\|0\|0\|0\|0\|0\|0\|0\|0 | 0\|0\|0\|0\|28\|0\|0\|0\|0 | 0 / 0 / 0 |
| 3 | on | 25228 / 1 | 1160 | 26.762 | 315.772 | 0\|0\|1\|0\|42\|0\|0\|0\|0 | 0\|0\|0\|0\|54\|0\|0\|0\|0 | 0 / 0 / 0 |
| 3 | off | 25228 / 1 | 1160 | 27.021 | 306.294 | 0\|0\|1\|0\|42\|0\|0\|0\|0 | 0\|0\|0\|0\|54\|0\|0\|0\|0 | 0 / 0 / 0 |

`aibeltdiag:` では on の Filter/Long が全 seed・全 faction で `conditions=0, placed=0, unaffordable=0, noSpace>0, lowStock=0, noLine>0` だった。実戦マップでは automatic processing line と core-fed belt と加工建物への到達経路が同時に成立する配置候補が現れなかったためで、Long が0になった理由も同じである。専用の構造テストでは Filter/Long の配置と材料搬送を確認済みである。

## 合格基準との比較

- 優先分岐・Filter・Long のいずれかが各 seed で1件以上: 未達（各 seed 0件）。
- peak soldiers 合計が off の0.95倍以上: 達成（各 seed 1.00倍）。
- p99 が off の1.2倍以内: 達成。
- 1秒超の tick が無い: 達成（max は計測値で約316ms）。
- 決着が off の1.1倍以内: 達成（全 seed で同一 tick）。

実戦計測の第1基準だけ未達である。候補経路が存在する人工ワールドでは専用テストが通過しているため、今後は実戦 AI が加工線を core-fed belt として形成する時点、またはその線を `ProcessingLineState` に関連付ける時点を追加調査する必要がある。

未検証: 実機の突然の電源断、GUI/Unity 実行、全テストスイート、長時間40000 tickまで終わらないケース。
