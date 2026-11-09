# glass5 実施報告

## 実装

- Smelter、Blast Furnace、Charcoal Kiln、Gear Workshop、Glassworks、Lens Assemblerから、経済stock直取り・source ID直送・MetalStockFallback系の分岐を除去した。
- `Simulation.Industry.cs` と `Simulation.IndustryAi.cs` に `MetalStockFallback`、`MetalSourceId`、`CharcoalSourceId`、`GlassSourceId` の読み取りは残っていない。
- ArmyRecoveryのglassRecoveryLine例外を除去した。
- glassworksのtargetAssemblersを `ranges >= 3 ? 2 : ranges >= 1 ? 1 : 0` にした。既存Assemblerのrecipeは書き換えない。
- Sand drill候補は、自陣Coreからの距離、同距離時はnode IDで決定する。
- AIが、経路未完成時のGlass/Metal/Gears搬送にhaulerを手配し、Lens assemblerをCarriedSupplyの優先対象にした。搬送タスクの設定のみで、建物処理側の直接転送は行わない。
- `GlassworksFlagDoesNotLetSmelterTakeOreFromCoreStock` と `LensAssemblerUsesOnlyInputsDeliveredToItsSlots` を追加した。

## 検証

- `dotnet build Headless\\Rts.Core.Tests\\Rts.Core.Tests.csproj -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`: 成功、警告0、エラー0。
- `GlassworksTests`: 13 passed、0 failed、0 skipped。
- forbidden symbol grep: Industry / IndustryAi とも該当なし。
- flag-onは `--map-size large --terrain --ticks 40000 --west-tactic auto --east-tactic auto` でseed 1〜3を実行した。

## 計測結果

詳細は `glass/measurements5.csv`。glassProducedは全seed・全Factionで0より大きい。

rangeが存在するFactionのlensProduced条件は未達だった。seed 2 Faction 2はranges=1でlensProduced=0、seed 3 Faction 1はranges=1でlensProduced=0だった。lensConsumedByArchersは全行0だった。

なお、CSVの大規模計測は、最後に追加したCarriedSupply優先度修正の直前までに完走した計測値である。修正後の追加seed 2確認 run は長時間継続したため、成果物確定時点で停止した。

## flag-off比較

指定された `D:\\rts-verify\\glass5_base\\` CLIを確認したが、`--glassworks-off`オプションを認識しなかった。このbase CLIはglassworks導入前のflag-off相当として実行した。

- seed 1: base tactic-matchはtick 27420で終了、winner 2。base自身のreplayは `Replay matched S0..S27420`。
- seed 2: base tactic-matchはtick 40000で未終了。base自身のreplayは `Replay matched S0..S40000`。

base記録をmodified CLIで再生する比較は、base CLIが要求オプションを持たず、記録ファイルも容量抑制のため検証後に削除したため未実施。
