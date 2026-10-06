# pick-off

見えている接触のうち、自軍兵力より `SIZE_MARGIN` 人分以上小さいと推定できるものを選び、位置への `Focus` で狙います。有利な接触がなければ、全軍を自軍コアへ `Retreat` させます。敵の接触が複数ある場合は、推定最大兵力が最小のものを選びます。

`SIZE_MARGIN`、`PICK_PRIORITY`、`RETREAT_PRIORITY` が調整点です。`allowedLossPermille` と `reservePermille` を下げると、より早く引く動きになります。

## お任せ（auto）相手の結果

`tactic-match --ticks 12000 --west-tactic TacticSamples/pick-off --east-tactic auto` を map seed 1〜4 で実行した結果です。

| map seed | 西側の結果 | 決着（tick÷1200） |
|---:|---|---:|
| 1 | 未決着 | — |
| 2 | 負け（東側勝利） | 4.50分（5402÷1200） |
| 3 | 負け（東側勝利） | 4.21分（5050÷1200） |
| 4 | 未決着 | — |

## 書きにくかったこと

`visibleEnemies` には敵の人数がなく、`contacts` の `min/max` も推定値です。そのため、確実な「小さい部隊」判定ではありません。敵部隊IDを `Focus` の目標に指定する語彙もないため、接触の位置を `Point` 目標にしています。命令実行側が接触を部隊へ対応付ける保証や、追撃を止める距離条件もありません。
