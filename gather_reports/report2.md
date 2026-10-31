# gatherspread2 実装・検証報告

## 段階1：種2で「道なし」を数えた結果

診断カウンタだけを追加した状態で、`--map-seed 2 --terrain --map-size large --ticks 40000 --timing-out` を旗オンで決着まで実行した。a/b/c は西・東の累計で、資源順は食料・木・石。c の括弧内は、通れるマスのかたまりが 2000 未満だった件数と、その境界が建物・地形だった件数である。

| 陣営 | a 出発マスが非通行 | b 行き先マスが非通行 | c 真の非連結 | c の囲まれ（建物/地形） |
|---|---:|---:|---:|---:|
| 西 | 0 / 557 / 19 | 0 / 192 / 13 | 0 / 0 / 0 | 0 / 0 |
| 東 | 0 / 367 / 53 | 0 / 231 / 77 | 0 / 0 / 0 | 0 / 0 |
| 合計 | 0 / 924 / 72 | 0 / 423 / 90 | 0 / 0 / 0 | 0 / 0 |

したがって種2の「道なし」は c の囲まれではなく、主に木・石の出発側または行き先側のセルが非通行だった。建物種別・地形種別の内訳は c が 0 件のため該当なし。

## 実装

- `Simulation.Economy.cs`：GatherSpread の点・かたまりの受け入れ上限を優先条件に変更。空きが全くない場合は上限を無視して最寄りの点を選ぶ。旗オンの割り振り1回あたりの経路判定は8回までとし、既存キャッシュを使う。
- `Simulation.Economy.cs`：旗オンで出発点が非通行なら、半径3マス以内の最寄りの通行可能セルへ判定開始点を補正。村人の実移動も同じ補正セルから開始する。集積所向けの判定にも適用した。
- `Simulation.cs`、`LargeEconomyObservation.cs`、`TacticMatchCommand.cs`：旗オン時だけ、資源別の a/b/c、2000未満の囲まれ、建物・地形境界を観測し、`--timing-out` の列へ追加。観測値は状態・ハッシュには入れていない。
- `GatherSpreadTests.cs`：全点満杯時の最寄り点継続、非通行セル上の村人の隣接セル補正を追加。
- `docs/technical-design-v3.md`：38.23 末尾に実装決定を追記。

値段、試合長、時代時計、攻め、手動操作、Contracts、gatherspread1 の代替資源選択は変更していない。旗オフでは GatherSpread 固有の集計・補正・拡張欄出力を実行しない。

## テスト

Release 構成、`-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false` でグループごとに実行した。全体テストは実行していない。

| グループ | 結果 |
|---|---:|
| GatherSpreadTests | 8/8 成功 |
| StockAwareGatherTests | 9/9 成功 |
| AiExpandTests | 12/13 成功（`UnreachableLargeWorkPointIsNotKeptAsTheWoodAssignment` が失敗） |
| LargeEconomyTests | 6/6 成功 |
| ScenarioExtensionTests | 11/11 成功 |

AiExpand の失敗では、最終状態の対象村人に16セルの経路が残っており、既存の「到達不能」期待と実経路判定の境界を確認できなかった。10分制限を超えたテストはない。

## 旗オフの測定

各行とも決着まで、`--map-size large --terrain --ticks 40000 --timing-out`。待機最大・「50人以上」の最長は西/東。原因 a/b/c は資源順が食料/木/石で、ON の最終累計。

| 種 | 旗 | 決着 tick / 勝者 | 兵ピーク合計 | p99 / 最大 ms | 待機最大 西/東 | 50人以上最長 西/東 | 代替 | 原因 a 西/東 | 原因 b 西/東 | 原因 c 西/東 |
|---:|:---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | ON | 25591 / 1 | 836 | 28.360 / 299.783 | 65 / 93 | 32 / 903 | 473 / 513 | 18 / 65 | 245 / 239 | 1099 / 963 |
| 1 | OFF | 25181 / 2 | 1412 | 43.422 / 247.478 | 0 / 0 | 0 / 0 | 0 | 0 | 0 | 0 |
| 2 | ON | 25148 / 2 | 1210 | 43.513 / 340.136 | 76 / 151 | 421 / 4828 | 663 / 988 | 45 / 57 | 2784 / 3103 | 7361 / 4262 |
| 2 | OFF | 25173 / 1 | 1096 | 27.021 / 239.118 | 0 / 0 | 0 / 0 | 0 | 0 | 0 | 0 |
| 3 | ON | 25198 / 2 | 558 | 29.715 / 391.014 | 69 / 52 | 626 / 213 | 1421 / 692 | 129 / 69 | 5973 / 2412 | 5884 / 6535 |
| 3 | OFF | 25466 / 2 | 1141 | 40.037 / 260.184 | 0 / 0 | 0 / 0 | 0 | 0 | 0 | 0 |

原因 c の内訳は、全測定で 2000 未満の囲まれが 0 件だった。資源別の段階1表に示したとおり、c は主に非連結の木・石で、建物/地形の囲まれではない。

## 合格の目安との比較

- 「50人以上の待機が200 tick以内」：全種で未達。ON の最長は種1 903、種2 4828、種3 626 tick。
- 兵ピーク 0.95 倍以上：種1 は 0.592 倍、種2 は 1.104 倍、種3 は 0.489 倍。種2のみ達成。
- 決着が OFF より10%を超えて遅くならない：種1 1.016倍、種2 0.999倍、種3 0.989倍で目安内。
- p99 が OFF の1.1倍以内：種1 0.653倍、種2 1.610倍、種3 0.742倍。種2は未達。
- 1 tick が1秒超：今回の最大値はいずれも1秒未満。

## 照合

ベース CLI の tactic-match は終了コード0、最終行は次のとおりだった。

`craft4: faction=2 assemblers=0 siegeWorkshops=1 siegeWorkshopComplete=1 siegeWorkshopQueued=0 siegeParts=0 mechanism=0 siegeEngine=0 siegeEngineProduced=0 siegeEngineConsumed=0 eliteKit=0 eliteKitProduced=0 eliteKitConsumed=0 cannonProduced=0 peakSoldiers=417`

その rpl を変更後 CLI で replay した結果は、空き約76GBを確認した再実行を含め、終了コード1、`Format/version: Premature or inconsistent End.` となった。ベース tactic-match は2回とも終了コード0で `ticks=25442 ended=True winner=1`。rpl と hash は各回とも読み取り後に削除した。

## 確認できていないこと

変更後 CLI の旗オフ replay は形式エラーで成功していない。全体テスト、Unity Editor 上のビルド、実ゲーム接続での長時間動作も未確認。AiExpandTests の1件も未解決。測定では待機・兵ピーク・種2の p99 が合格目安に届いていない。
