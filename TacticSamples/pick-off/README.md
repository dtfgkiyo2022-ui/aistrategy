# pick-off

`pick-off` は、`ownArmies[].composition` の自軍兵数を合計し、現在可視の接触だけを候補にします。候補の `visibleComposition` が示す兵数が自軍より小さく、`visibleEnemies[].kindName` で確認した Archer が3分の1以下の接触を選び、全軍で `Focus` します。候補がなければ `Retreat` します。

`SIZE_MARGIN`、`PICK_PRIORITY`、`RETREAT_PRIORITY` は調整点です。

## 測定結果

実行コマンド:

`tactic-match --map-seed 1 --ticks 12000 --west-tactic TacticSamples/pick-off --east-tactic auto`

| map seed | 西軍の結果 | tick |
|---:|---|---:|
| 1 | 未決着 | 12000 |
| 2 | 東軍勝利 | 5402 |
| 3 | 東軍勝利 | 5050 |
| 4 | 未決着 | 12000 |

結果は兵種を見る前の版と tick まで同じでした。記録（`--log-out`）では判断は実際に切り替わっていて（種2では 1080 tick から「小さい接触へ集中」と「後退」を約350 tick ごとに繰り返す）、前の版と同じ接触を同じ時に選んでいるためです。auto に勝つには、狙う相手の選び方より、攻める・引くを繰り返さない仕組み（一度攻めたら一定時間続ける、など）が要りそうです。

## 書きにくかったこと

部隊と兵の対応は Contracts にないので、`composition` は「自軍の兵（村人を除く）を一番近い部隊に数えた」近似です。部隊が近くに重なっていると内訳が混ざります。敵部隊IDを `Focus` の目標に指定する語彙もないため、接触の位置を `Point` 目標にしています。
