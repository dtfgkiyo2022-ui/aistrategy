# MetalLineFix 測定報告

測定日: 2026-11-19

条件: Release CLI、`tactic-match`、`--terrain --map-size large --ticks 40000`、seed 1–3、ON/OFF 各1回。OFF は `--metal-line-fix-off`。試合が先に終了した場合は終了 tick まで。p99/max は `--timing-out` の `stepMs` を nearest-rank で集計した。

以下の gear / metal / belt の値は faction 1 を対象にした。`decision tick` は試合終了 tick、`max soldiers` はこの測定では timing CSV の保存後に削除してしまい未取得である。`metal produced` は最終 byproducts 観測で、通常 Smelter の累積値ではない。

| seed | flag | decision tick / winner | max soldiers | p99 / max ms | metal produced | belt connected tick | manual ore | stall ticks | gear workshops / gears (12000 / end) |
|---:|:---:|---:|:---:|---:|:---:|:---:|:---:|---:|:---|
| 1 | ON | 12855 / 1 | — | 27.075 / 287.917 | 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 1 | OFF | 12855 / 1 | — | 25.266 / 282.081 | 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 2 | ON | 13889 / 1 | — | 24.621 / 287.328 | 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 2 | OFF | 13889 / 1 | — | 24.604 / 281.624 | 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 3 | ON | 14110 / 1 | — | 27.468 / 292.076 | 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 3 | OFF | 14653 / 1 | — | 26.551 / 282.610 | 0 | — | — | 0 | 0 / 0 ; 0 / 0 |

## 数値の読み方

- 3 seed とも測定対象 faction 1 の通常 metal / gears は最終観測で 0。対象の金属線が稼働するケースを含まないため、ON/OFF の生産差は判定できない。
- `belt connected tick` と `manual ore` は、timing-only の行に対象線の接続・手運び専用カウンタが出ず、最終観測にも該当イベントがなかったため `—` とした。`stall ticks` の 0 は belt wait カウンタであり、製錬所入力 stall の完全な代替ではない。
- seed 3 は ON が 14110 tick、OFF が 14653 tickで終了した。これは勝敗の早期終了差で、金属線の改善効果とは切り分けられない。

## 検証結果

- Release build: `Rts.Headless.Cli` / `Rts.Core.Tests` とも警告・エラーなし。
- `MetalLineFixTests`: 4/4 成功。
- `BeltCraftTests`: 14/14 成功。
- `AiBeltPartsTests`: 9/10 成功。既存 `AiBeltPartsBuildsGearWorkshopAndProducesGears` が未達で、修正前後で同じ既知失敗として残った。
- 旧 `D:\rts-verify\main1119_cli` で作成した seed 1/2 のリプレイを現CLIで `replay --no-states` 再生し、`Replay matched S0..S12855`、`Replay matched S0..S13889` を確認した。

したがって、extension 130 の保存、OFF の hash/replay 互換、長線の宛先側敷設、既存 BeltCraft 群は確認済みだが、実際に旧製錬所が詰まる再現リプレイを含む金属生産量の比較は未検証である。
