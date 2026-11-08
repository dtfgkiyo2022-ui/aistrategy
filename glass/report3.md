# Glassworks seed2 F1 過剰・tick 調査報告 3

## 結論

Glassworks の後段を再構成し、砂・ガラス・金属・歯車・レンズの在庫接続を修正した。大 terrain の最終計測では、Glassworks が存在する全陣営で `lensProduced>0` を確認した。

| seed | tick | F1 lens / lensArchers | F2 lens / lensArchers | 判定 |
|---:|---:|---:|---:|---|
| 1 | 40000 | 56 / 0 | 45 / 0 | 成功 |
| 2 | 40000 | 27 / 0 | 6 / 5 | 成功 |
| 3 | 15409 | Glassworks なし | 1 / 0 | Glassworks 保有陣営は成功 |

seed3 F1 は core-destroyed 時点（15,409 tick）まで Glassworks 未建設だったため、レンズ条件の対象外である。seed2・3 は巨大な無期限待ちにならず、seed2 は 40,000 tick まで未決着、seed3 は 15,409 tick で決着した。

## seed2 の原因

- Glassworks と Lens assembler は配置されるが、CarriedInputs 旗オン時の Lens recipe は経済ストックから自動投入されない。
- GearWorkshop は生産物を faction の Gears stock に計上する一方、GearWorkshop の建物 Output から assembler へ搬送する前提ではなかった。
- Glassworks のガラスは Glassworks の Output に残り、economy.Glass だけを参照する Lens assembler へ届かなかった。
- `CanHaulTo`、`TryFeedBuilding`、`BeltCanFeedBuilding` の一部が GearWorkshop を BeltCraft 旗だけで許可していた。Glassworks が配置する工房ではこの旗が false の場合があり、金属経路と gear→assembler 経路が切れていた。
- BlastFurnace の ore/charcoal、CharcoalKiln の wood も carried line の回復時に不足する場合があり、F1 の metal source が ore だけで止まった。
- Lens assembler を既存の自動 recipe 判定が上書きし、経済判断周期ごとの route 再適用も不足していた。

## 実装した修正

- Lens assembler の CarriedInputs 経路に Glass と Gears の不足入力補充を追加し、既知の Glassworks Output と faction Gears stock を O(1) で受け渡すようにした。
- Lens assembler・GearWorkshop・BlastFurnace・CharcoalKiln について、Glassworks 旗オン時だけ既知の source/building id を使う補給フォールバックを追加した。通常の建物全走査や毎 tick の route 探索は追加していない。
- Glassworks の Lens line が存在した時点で専用復旧フラグを立て、ArmyRecovery の一般的な処理停止から Glassworks 専用ラインを除外した。
- GearWorkshop の配置は output port の全 facing を試し、失敗理由を `noSite` と `noOutputPort` に分離した。
- Smelter と BlastFurnace を metal source として認識し、Metal source→GearWorkshop→Lens assembler の route／hauler 設定を追加した。既存 route が不完全な場合は deterministic な hauler fallback を使う。
- Glassworks が存在する間は Lens recipe を再確認し、既存 Lens assembler があっても route を再設定する。
- `Headless/Rts.Core.Tests/GlassworksTests.cs` に、GearWorkshop の stock gears が Lens assembler に消費されるケースを追加した。

追加処理は Glassworks 旗オンに限定し、価格、Lens の材料必要量、Archer の必要条件は変更していない。Simulation の浮動小数・double・Dictionary 列挙は追加していない。

## 計測値

詳細は `glass/measurements3.csv` に記録した。timing CSV の nearest-rank p99 は以下の通りで、`na` は残していない。

- seed1: 79.182 ms（40,000 tick）
- seed2: 147.372 ms（40,000 tick）
- seed3: 54.256 ms（15,409 tick で決着）

seed1 は F1/F2 が `glassworks=3`、`lensAssemblers=1`、`gearWorkshops=1`、`lensProduced=56/45`。seed2 は同じく両陣営 `glassworks=2`、`lensAssemblers=1`、`gearWorkshops=1`、`lensProduced=27/6`。seed3 は F2 が `glassworks=3`、`lensAssemblers=1`、`gearWorkshops=1`、`lensProduced=1` だった。

## 旗オフ replay 検証

指定コマンドで base CLI の seed1 replay を生成し、変更 CLI に `--allow-build-mismatch --no-states` を付けて検証した。結果は `Mismatch at tick 17998` だった。したがって旗オフ replay の受入条件は未達であり、seed2 の同一手順は seed1 の不一致原因を解消するまで実行していない。

```text
glass/base-cli/Rts.Headless.Cli.exe tactic-match --map-seed 1 --terrain --map-size large --ticks 40000 --out glass/a1.rpl
glass/base-cli/Rts.Headless.Cli.exe tactic-match --map-seed 2 --terrain --map-size large --ticks 40000 --out glass/a2.rpl
Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.exe replay --in glass/a1.rpl --hash-out glass/a1.hash --allow-build-mismatch --no-states
Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.exe replay --in glass/a2.rpl --hash-out glass/a2.hash --allow-build-mismatch --no-states
```

検証後、生成した `glass/a1.rpl` と `glass/a1.hash` は削除した。

## ビルド・テスト

```text
dotnet build Headless\Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
成功、0 warnings、0 errors

dotnet test Headless\Rts.Core.Tests\Rts.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GlassworksTests -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
10 passed、0 failed
```
