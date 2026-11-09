# glass2 砂搬送不具合の修正・検証報告

## 結論

glass1 の `sandGathered=0` は解消した。旗オンの 40,000 tick 計測では、seed 1〜3 の全陣営で砂が Glassworks に届き、`glassProduced` も正値になった。一方、`lensProduced>0` は全陣営ではまだ達成できていない。Glassworks の砂入口より後ろ、歯車供給またはレンズ組立所への入力搬送が残課題である。

## 原因

glass1 の6試合はすべて `sandGathered=0` だった。コード追跡と tick 内カウンタで、砂が石と同じ経路を通っていない箇所を特定した。

- `FinishDropOff`、村人の `WorkKindFor`／`LargeWorkKindFor`、倉庫の資源接続、ベルト荷下ろし資源の許可集合に Sand が含まれていなかった。
- `CanHaulTo` に Drill→Glassworks の Sand と CharcoalKiln→Glassworks の木炭がなく、Glassworks の搬入先も `DropOff` で認識されていなかった。
- Glassworks の後段では、通常の自動クラフト割当が Lens recipe を上書きし得た。また、Glassworks の出力と GearWorkshop の出力を assembler へ運ぶ明示的なフォールバックがなかった。

## tick 計測

failure 配列の順序は `None/NoNode/Depleted/WrongResource/MultipleNodes/Covered/GoldUnavailable/Impassable`。

- 最終 seed1: F1 は砂ドリル試行 56、配置 1、Covered 27、砂村人 1、`sandGathered=196`、`glassProduced=35`、`lensProduced=4`。F2 は試行 12、配置 0、Covered 6、砂村人 1、`sandGathered=674`、`glassProduced=20`、`lensProduced=0`。
- seed2 の計測: F1 は試行 7／配置 3、Covered 3、`sandGathered=906`、`glassProduced=347`。F2 は試行 262／配置 1、Covered 215、`sandGathered=312`、`glassProduced=22`。両陣営とも lensProduced は 0 だった。
- seed3 の計測: F1 は `sandGathered=64`、`glassProduced=3`、F2 は `sandGathered=724`、`glassProduced=143`。F2 は砂村人 1 人で、F1 は Glassworks 建設数 0 のまま終了した。

ドリルが置けない陣営でも村人フォールバックが動き、砂を直接 Glassworks 入口へ投入できることを確認した。ドリルが置けた seed2 F1 では、砂ルート試行 135、ルート発見 45、木炭ルート発見 45 だった。ベルト全区間を新設できない場合は、既存の carried hauler へフォールバックする実装にした。

## 実装

- Sand を Stone と同じ村人採取、倉庫、ベルト、荷下ろし、加工入口の許可集合へ追加。
- `DrillSiteIsClear` を失敗理由付きで呼び出せるようにし、Sand drill の試行・配置・理由別失敗を観測カウンタへ追加。
- Drill が無い／置けない場合、完成した Glassworks があれば Sand 村人を割り当てる。
- Drill／CharcoalKiln→Glassworks、Glassworks→Lens assembler、GearWorkshop→Lens assembler、Lens assembler→core のベルト経路と運び役フォールバックを追加。
- carried-supply の優先順位で Glassworks を assembler／kiln と同じ優先帯に変更。
- Glassworks が存在する間は Lens recipe の自動割当を経済判断周期ごとに再確認し、GearWorkshop 配置時は `StartBeltCraftLine` を開始。
- `Headless/Rts.Core.Tests/GlassworksTests.cs` に、砂ドリルが置けない場合の村人採取と、全砂サイトが阻害された場合の理由別カウンタのテストを追加。

## ビルド・テスト

```text
dotnet build Headless\Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
成功、0 warnings、0 errors

dotnet test Headless\Rts.Core.Tests\Rts.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GlassworksTests -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
9 passed、0 failed
```

全体テストは実行していない。tactic-match は常に1本ずつ実行した。

## 旗オフと replay

旗オフの分岐は既存の map／binary の処理を変更しない構造にし、追加テストでも旗オフの map／binary 安定性を確認した。`glass` フォルダには `a.hash` と `b.hash` はあるが、入力 replay ファイル `a.rpl`／`b.rpl` が存在しないため、依頼指定の `replay --no-states` は実行できなかった。hash の再生成や推測による replay は行っていない。

## 計測ファイルについて

詳細な1行1陣営の値は `glass/measurements2.csv` に記録した。旗オンの最終計測は p99 timing 出力を同時保存していないため、該当欄は `na` とした。旗オフ行は glass1 の既存記録を引き継いでいる。

受入目安のうち、`sandGathered>0` と `glassProduced>0` は全計測陣営で確認できた。`lensProduced>0` の両陣営達成、および種2・3の旗オフからの決着 tick 比較は、レンズ入力搬送の残課題により未達である。
