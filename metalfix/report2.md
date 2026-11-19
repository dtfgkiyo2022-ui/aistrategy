# MetalLineFix（metalfix2）検証報告

測定日: 2026-11-19

## 結論

前回の「製錬所が一度も建たない」は修正した。旗オンでは、製錬所を鉱山・本拠地・鉱山と本拠地の中間の順で探し、資材が揃わない場合も金属線の予約として保持し、Age 3 後も再試行する。

ただし、指定された合格条件（seed 1〜3 の **両陣営**が tick 12000 までに製錬所を完成し、本拠地の metal を 0 より大きくする）は未達である。未達の理由は場所が見つからないことではなく、陣営ごとの建設資材・建設進捗・ベルト／運搬の到達時刻である。旗オフの経路は変更していない。

## 本当の原因

一時診断で `EnsureAiFactoryMetalLine` の金属線分岐まで到達していることを確認した。seed 1 の faction 2、tick 9920 前後の値は次の通りだった。

- wood = 1450、planks = 11、stone = 0
- Smelter の配置費 = wood 84、planks 36（stone 0）
- 旧コードは `SmelterWoodCost` だけを見て `PlaceSmelter` を呼ぶため、wood は十分でも planks 36 が揃わず配置が成立しない
- Age 3 に移った tick 10800 以後、旧 `DecideAiFactoryAge3` は金属線を再試行しない

したがって、主因は「場所が無い」ではなく、製錬所の craft-required plank を建設可能条件に含めないまま一度だけ試行し、Age 3 で再試行も止まることだった。さらに、直接配置が予約・武器供給用の保持資材に阻まれる場合があり、金属線用の予約を世界状態に残す必要があった。

## 変更内容

- `UnityProject/Assets/RTS/Simulation/Simulation.AiFactory.cs`
  - 旗オンの Age 3 でも `EnsureAiFactoryMetalLine` を呼ぶ。
  - `aiFactoryRetiredMetalSmelters` のプロセス外 `List` を削除し、`world.Buildings` を ID 順に走査して、現在の線から外れた完成製錬所の入力／出力を再設定する。
  - 金属線の予約が資材条件を満たしたとき、予約 ID を維持したまま配置を進める。
- `UnityProject/Assets/RTS/Simulation/Simulation.IndustryAi.cs`
  - `MetalLineFixOn` のときだけ、鉱山・本拠地・中間点を検索する製錬所配置を追加した。中間点の探索半径は最大 64 セル。
  - 直接配置できない場合は、同じ候補地点を `CreateBuildingReservation` で金属線予約にする。
  - `LayRoutes` の長線の宛先側からの漸進敷設は維持した。
- `UnityProject/Assets/RTS/Simulation/Simulation.BuildingReservations.cs`
  - 金属線予約を進める一時状態の間だけ、既に保持されている建設資材と ArmsSupply の plank reserve が同じ金属線配置を二重に拒否しないようにした。旗オフ経路には到達しない。
- `Headless/Rts.Core.Tests/MetalLineFixTests.cs`
  - large map の `LiveGameRules` で製錬所配置が成立する小テストを追加した。
- `docs/technical-design-v3.md`
  - 第81章を今回の実装内容に合わせて更新した。

Contracts は変更していない。ScenarioBinary の extension 130 / version 1 と、旗オフ時に extension を出力しない処理も維持している。

## probe metal 1000（tick 12000）

変更後 CLI で `tactic-match --terrain --map-size large --ticks 12000` を seed ごとに実行し、`metalfix/probe` の `probe <rpl> metal 1000` で確認した。大きな rpl は出力を読んだ直後に削除した。

| seed | faction | tick 12000 の金属線 | 製錬所 | 金属 / 鉱石 / planks / wood | ベルト / 運搬役 | 判定 |
|---:|---:|---|---:|---:|---|---|
| 1 | 1 | `mine=27 smelter=0` | 0 | `metal=0 ore=0` | `0/0`, 0 | 未達。製錬所予約まで進まず |
| 1 | 2 | `mine=95 smelter=111` | 完成 | `metal=0 ore=0 planks=17 wood=4008` | `7/86`, 2 | 製錬所は完成、入力 8 だが tick 境界で output 0 |
| 2 | 1 | `mine=30 smelter=182` | 未完成 | `metal=0 ore=0 planks=0 wood=275` | `0/0`, 0 | 建設中で planks 不足 |
| 2 | 2 | `mine=0 smelter=0` | 0 | `metal=0 ore=0 planks=2 wood=20` | `0/0`, 0 | 鉱山未成立 |
| 3 | 1 | `mine=107 smelter=168` | 完成 | `metal=0 ore=0 planks=0 wood=2094` | `4/52`, 2 | 製錬所は完成、金属線が未到達 |
| 3 | 2 | `mine=36 smelter=0` | 0 | `metal=0 ore=0` | `0/0`, 0 | 製錬所未成立 |

seed 1 の faction 2 と seed 3 の faction 1 は「製錬所を建てる」段階までは直った。一方、残りは資材／鉱山／建設進捗が tick 12000 に間に合わず、全 seed の両陣営の本拠地 metal > 0 という最終条件は満たしていない。

## 旗オン・オフ決着測定

条件は Release CLI、`tactic-match`、`--terrain --map-size large --ticks 40000`、決着まで。p99 は timing CSV の `stepMs` を nearest-rank で集計し、兵数は CSV を削除する前に `westSoldiers` / `eastSoldiers` の最大値を読んだ。ON は今回の修正版、OFF は前回取得済みの bitwise-compatible baseline である。

| seed | flag | 決着 tick / winner | 最大兵数（f1/f2） | p99 / max ms | metal produced（f1/f2） | belt connected tick | manual ore | stall ticks | gear workshops / gears（12000 / end） |
|---:|:---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | ON | 13740 / 1 | 211 / 20 | 21.078 / 299.077 | 0 / — | — | — | — | 0 / 0 ; 0 / 0 |
| 1 | OFF | 12855 / 1 | — / — | 25.266 / 282.081 | 0 / 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 2 | ON | 13889 / 1 | 115 / 64 | 21.162 / 294.981 | 0 / — | — | — | — | 0 / 0 ; 0 / 0 |
| 2 | OFF | 13889 / 1 | — / — | 24.604 / 281.624 | 0 / 0 | — | — | 0 | 0 / 0 ; 0 / 0 |
| 3 | ON | 14238 / 1 | 169 / 20 | 22.079 / 289.893 | 0 / — | — | — | — | 0 / 0 ; 0 / 0 |
| 3 | OFF | 14653 / 1 | — / — | 26.551 / 282.610 | 0 / 0 | — | — | 0 | 0 / 0 ; 0 / 0 |

OFF の max soldiers、belt connected tick、manual ore は前回表で未取得だったため `—` とした。ON の faction 1 の metal produced は最終 byproducts 観測で 0、faction 2 は今回保存した最終出力から確定値を回収できなかったため `—` とした。12,000 tick の一時入力は含めない。p99 は ON/OFF 比で最大でも seed 1 の `21.078 / 25.266 = 0.834` で、1.2 倍以内だった。

## テストとリプレイ照合

すべて Release、`-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`、個別実行で確認した。全体テストは実行していない。

- `MetalLineFixTests`: 5/5
- `AiFactoryTests`: 8/8
- `CarriedInputsTests`: 14/14
- `BeltCraftTests`: 14/14
- `AiBeltPartsTests`: 10/10
- `CraftRequiredTests`: 15/15
- `ArmsSupplyTests`: 6/6
- LargeEconomy group: 14/14
- `ScenarioExtensionTests`: 11/11
- 歯車2件の個別テスト: 各 1/1
- `AiExpandTests`: 長時間実行後、失敗ではなく timeout として停止。完走結果なし。
- CLI Release build、probe Release build: 警告 0、エラー 0

旗オフ照合は旧 CLI で作成した seed 1/2 の rpl を現 CLIで `replay --allow-build-mismatch --no-states` 再生した。

```text
Replay matched S0..S12855
Replay matched S0..S13889
```

## 長線敷設の確認

`LayRoutes` の `incrementalMetalRoute` は、20 セル以上の金属線が1本でもあるときだけ金属線の事前全量支払いを使わず、宛先側のセルから1セルずつ敷設する。各セルの支払い前に資材残量を確認し、不足した位置で停止するため、資材は負数にならない。他の線については従来の `AgeFourAiWoodReserve` と `ArmsSupplyCanSpendPlanks` の判定を通り、金属線の漸進経路が他線の予約を消費しない。

今回の diff では、長線だけに `incrementalMetalRoute` を渡し、短線と旗オフの all-pay 順序は変更していない。また、金属線の `BeltRoute` は鉱山の `OutputCell(mine)` を始点、製錬所の footprint を終点として作るため、route の先頭は鉱山側、末尾は製錬所側である。敷設順はその route を反転して宛先側から行うので、逆順で鉱山出口が最後になる。

## 未確認・残課題

合格条件の「seed 1〜3、両陣営、tick 12000 までに metal > 0」は未達であり、今回の報告では合格とはしていない。次の追加作業では、金属線予約が plank 生産・建設作業員・鉱山搬送を優先できるかを faction/seed 別に追う必要がある。旗オフの保存互換・Contracts 非変更・既存テスト群は確認済みである。
