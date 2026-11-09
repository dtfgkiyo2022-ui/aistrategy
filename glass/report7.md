# Glassworks glass7 検証報告

## 結論

Glassworks の既存 assembler recipe を Lens に書き換える処理を除去した。さらに、Glassworks AI が選択した Lens assembler に対して一般的な自動 recipe 選択処理が上書きしないよう、現在 recipe が Lens の場合は `SetAssemblerRecipe` の自動変更を抑止した。プレイヤー明示の recipe 設定経路は変更していない。Glassworks flag による共有処理分岐は追加していない。

既存 recipe の保持テストを追加し、GlassworksTests は 16 passed / 0 failed / 0 skipped だった。

## ガラス収支

最終状態の Glassworks output、村人搬送、死亡村人の搬送残、破壊済み建物在庫、破損ベルト、通常ベルト、storage、catapult、Lens assembler input、core 在庫、Lens 消費、建物消費を合算した。全 6 faction-row で `glassAccounted == glassProduced` になった。

| seed | faction | ticks | glassProduced | glassAccounted | 主な内訳 | lensAssemblers | lensProduced | lensConsumedByArchers | lensArchers |
|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|
| 1 | 1 | 40000 | 439 | 439 | core 407 / building 20 / belt 12 | 0 | 0 | 0 | 0 |
| 1 | 2 | 40000 | 220 | 220 | core 198 / belt 17 / broken-belt 3 / hauler 2 | 0 | 0 | 0 | 0 |
| 2 | 1 | 40000 | 257 | 257 | core 212 / belt 23 / building 20 / output 2 | 0 | 0 | 0 | 0 |
| 2 | 2 | 40000 | 51 | 51 | assembler input 10 / belt 19 / output 20 / hauler 2 | 1 | 0 | 0 | 8 |
| 3 | 1 | 21656 | 57 | 57 | core 38 / dead-villager 13 / destroyed-building 6 | 0 | 0 | 0 | 1 |
| 3 | 2 | 21656 | 173 | 173 | core 146 / building 20 / belt 5 / hauler 2 | 0 | 0 | 0 | 0 |

seed 3 faction 1 の不足に見えた 19 は、死亡村人の搬送中 13 と破壊済み建物在庫 6 で説明できた。seed 1 faction 2 の 3 は破損ベルト在庫として説明できた。

`lensArchers` は最終時点の自軍 Archer / SkirmishArcher 総数であり、Lens 消費者数ではない。Lens 消費は `lensConsumedByArchers` で別計上しており、今回の 3 seed では Lens 生産・消費はいずれも 0 だった。

## 40000 tick 計測

詳細値は [measurements7.csv](measurements7.csv) に保存した。p99 step time は nearest-rank で算出した。

- seed 1: 40000 tick、未決着、p99 131.081 ms
- seed 2: 40000 tick、未決着、p99 195.598 ms
- seed 3: 21656 tick、faction 2 の core-destroyed、p99 62.956 ms

## flag-off replay

- seed 1: `glass5_base` CLI で生成した RPL を変更版 CLI の `replay --allow-build-mismatch --no-states` で検証し、`Replay matched S0..S27420`。
- seed 2: `glass5_base` CLI の RPL 記録を開始したが、記録量が約 2.18 GB に達して実用時間内に完了しなかったため中断した。部分 RPL は削除済み。seed 2 の flag-off replay hash は未取得。

