# BeltCraft A 実装・検証報告

対象は technical-design-v3 の 38.16、実装範囲は指定された A（歯車、部品価格、とても速いベルト、合流機、優先指定、AI の歯車在庫維持）です。B（選択投入機、長い投入機、投石機）は今回の対象外です。

## 実装概要

- 契約値を追加：`ResourceKind.Gears=35`、`BuildingKind.GearWorkshop=51`、`ProcessingLineKind.Gears=23`、`BeltSpeed.VeryFast=2`、`BeltComponentKind.Merger=6`、`BeltPriority=None/Left/Right=0/1/2`、`EconomyCommandKind.SetBeltPriority=29`。
- 拡張欄 ID 80 / version 1 を追加し、歯車工房、歯車生産、各部品の歯車・板・鋼コスト、VeryFast の速度・時代条件、AI在庫目標を保存・復元する。
- 歯車工房は時代1・全文明で木材80、2マス、金属1→歯車2、40 tick。AI は各陣営1棟、歯車目標20を維持する。
- 合流機は横/後ろから受け、前へ出す。分岐器と合流機は None/Left/Right の優先指定を持ち、None は交互、優先側が詰まった場合は反対側へ送る。
- VeryFast は時代3、2 tick/セル、BeltStack と同じ積載制限。既存 Fast の payload は維持し、`--belt-craft-off` では従来挙動を維持する。
- CLI、診断、EconomyView、LargeEconomy の CSV/JSONL、表示層の資源・建物・ベルト表現を追加した。

## 検証

対象テストは合格した。

```text
dotnet test Headless/Rts.Headless.slnx -c Release --no-restore --filter FullyQualifiedName~BeltCraftTests
成功: 3/3

回帰フィルター: BeltTests|BeltStackTests|CraftDepthTests|BeltCraftTests
成功: 36/36

dotnet build Headless/Rts.Headless.slnx -c Release
警告 0 / エラー 0
```

固定値、旧 Fast payload、VeryFast payload、合流機優先 payload、extension 80 の exact round-trip、flag-off の省略、EconomyView の Gears/BeltCraft を `BeltCraftTests` で確認した。

## large-map 実測

条件は Release、large map、全文明、seed 101/202/303、`--ticks 40000`。実際には各試合が core-destroyed で自然終了したため、下表の tick が実到達 tick である。`mean/p95` は timing CSV の `stepMs`。

| mode | seed | 実到達 tick | 勝者 | mean ms | p95 ms |
|---|---:|---:|---:|---:|---:|
| on | 101 | 25,403 | 2 | 14.161 | 40.240 |
| on | 202 | 25,139 | 2 | 12.291 | 35.917 |
| on | 303 | 25,494 | 1 | 9.110 | 24.163 |
| off | 101 | 25,340 | 2 | 8.322 | 26.025 |
| off | 202 | 25,205 | 1 | 9.411 | 28.230 |
| off | 303 | 25,580 | 2 | 7.092 | 20.933 |

3 seed 合算の `stepMs` は on が mean 11.849 / p95 33.667、off が mean 8.269 / p95 25.294 だった。on 側の追加処理による計測差として記録する。

8,000 tick の追加確認では、Economy trace に `gears=4`、`gearsProduced=4`、`gearShortageFailedPlacements=0` が出力されることを確認した。

生成した timing CSV は `beltcraft_on_101_timing.csv`、`beltcraft_on_202_timing.csv`、`beltcraft_on_303_timing.csv`、`beltcraft_off_101_timing.csv`、`beltcraft_off_202_timing.csv`、`beltcraft_off_303_timing.csv`。追加確認ログは `beltcraft_on_allcivs_economy_v2.jsonl`。
