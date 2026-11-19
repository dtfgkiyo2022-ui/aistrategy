# huntward1 測定報告

## 実装

- `HuntWardFix` は `EconomyRules` の拡張欄131（version 1）に追加し、`LiveGameRules` の large map だけで有効化した。`--hunt-ward-fix-off` は `tactic-match` と `record` で無効化できる。
- `HuntRoleBlocked` は旗オン時だけ `reserve` ロールを狩りから除外しない。`Assignment=Reserve` など既存の除外条件は維持した。
- 見張りは陣営・種類ごとに pending を1件に制限し、村人は `VillagerRouteTarget`、斥候は兵の local path を使う。600 tick で破棄し、セルは3000 tick冷却する。旗オン時だけ pending開始tick、破棄セル冷却、破棄数、pending村人数を `WorldState` と正規診断に含めた。
- `HuntWardFixTests.cs` を追加した。Contracts は変更していない。

## 測定（large / terrain / ticks 40000）

`--timing-out huntward\seedN-{on,off}.csv` の `stepMs` から tick p99 を算出した。狩った数はログの中立種類別 kill 数の合計、見張りの placed は hidden/revealing の placed 合計。兵最大は timing CSV の `westSoldiers/eastSoldiers` 最大値。

| seed | flag | 決着tick / 勝者 | 狩り f1 / f2 | 見張り placed f1 / f2 | abandoned f1 / f2 | pending村最大 f1 / f2 | 村人非移動* f1 / f2 | 兵最大 f1 / f2 | tick p99 ms |
|---:|:---:|---:|---:|---:|---:|---:|:---:|---:|---:|
| 1 | on | 14402 / 2 | 21 / 21 | 4 / 4 | 23 / 18 | 2 / 2 | — / — | 206 / 271 | 57.896 |
| 1 | off | 13702 / 2 | 5 / 6 | 2 / 0 | 0 / 0 | 0 / 0 | — / — | 235 / 139 | 28.744 |
| 2 | on | 19794 / 2 | 4 / 4 | 7 / 10 | 22 / 23 | 1 / 1 | — / — | 23 / 139 | 28.757 |
| 2 | off | 16500 / 2 | 4 / 4 | 8 / 11 | 0 / 0 | 0 / 0 | — / — | 20 / 138 | 37.038 |
| 3 | on | 15846 / 2 | 4 / 8 | 4 / 11 | 11 / 8 | 1 / 1 | — / — | 53 / 96 | 52.521 |
| 3 | off | 15686 / 1 | 4 / 4 | 5 / 0 | 0 / 0 | 0 / 0 | — / — | 122 / 20 | 21.842 |

\* 6試合は timing-only で記録したため、tick 13000→15000 の村人位置を含まない。packの再記録については下記に実測を記す。

## テスト

専用テストは5件中5件成功した。

- 拡張欄131の往復、および旗オフ時の欄不在
- 旗オフの毎tick hash安定性
- reserve軍の狩り命令
- pending見張りの重複抑止と600 tick後のIdle復帰
- pending村人のRouteTarget設定（直線ClipMoveではなく経路を使う）

既存全体テストは、依頼の「全体テストは実行しない」に従い実行していない。

## 照合

指定された旧CLIで `a.rpl`（seed1）と `b.rpl`（seed2）を終了コード0で記録し、変更後CLIで `--allow-build-mismatch --no-states` replay を実行した。

- seed1: 現行シミュレーションは tick 13567 まで読み、終端で `Premature or inconsistent End.`。`Replay matched ...` には到達しなかった。
- seed2: 現行シミュレーションは tick 16500 で旧記録と異なる終端となり `TickHash order/size.`。`Replay matched ...` には到達しなかった。

これは旧CLIとこのworktreeの既存main部分の決着が一致しないためで、HuntWardFixオフの専用往復/hashテスト（同一現行コード内）は成功している。照合結果を成功とは記載していない。

## play pack / Cの確認

指定packを変更後CLIで記録し、`huntward\probe` の `config` で `HuntWardFix=True Wards=True LargeEconomy=True` を確認した。記録は `S0..S13864` で決着したため、指定の `vilmove 13000 15000 1/2` は比較終点がなく結果行を出さなかった。

終端内の補助確認（13000→13800）は次の通り。

- f1: 12/315人が移動なし。ward pending はなく、主な内訳は `ToNode` 5、`Gathering` 2、`ToPickup` 4。
- f2: 47/311人が移動なし。`ward=False` で、`ToPickup` 28（うち同じ集積地点付近26）、`ToNode` 8が中心だった。
- したがってCの停止は見張りpendingではなく、資源ノードの `ToPickup`/`ToNode` 状態で目的地が現在位置近傍のまま進まない現象として再現した。今回の見張り修正の範囲を越えるため、資源採集ルールは変更していない。

## 成果物

- `huntward/probe/`（probe7nの現行DLL参照版）
- `huntward/` の一時 `.rpl`・`.hashes`・timing CSVは読取と報告作成を完了した。削除は環境のファイル削除ポリシーに拒否されたため、残存している。
