# boom-then-strike

序盤は `Growth` の内政方針で資源と時代を伸ばし、`STRIKE_TICK` に達して自軍兵力が `STRIKE_SOLDIERS` 人以上になった時点で全軍を敵コアへ送ります。時代進行命令は資源などの条件を満たしたときだけ実行側に受理されます。

`STRIKE_TICK`、`STRIKE_SOLDIERS`、`GROWTH_PRIORITY`、`STRIKE_PRIORITY` を変えると、攻撃開始の早さと慎重さが変わります。時代進行を急がず兵を増やしたい場合は `AdvanceAge` の行を削除できます。

## お任せ（auto）相手の結果

`tactic-match --ticks 12000 --west-tactic TacticSamples/boom-then-strike --east-tactic auto` を map seed 1〜4 で実行した結果です。

| map seed | 西側の結果 | 決着（tick÷1200） |
|---:|---|---:|
| 1 | 未決着 | — |
| 2 | 負け（東側勝利） | 4.61分（5533÷1200） |
| 3 | 未決着 | — |
| 4 | 未決着 | — |

## 書きにくかったこと

資源の残量だけでなく、時代進行の必要資源や研究の完了見込みを一度に問い合わせる命令はありません。`AdvanceAge` は文明名しか指定できず、現在の資源を見てから厳密に予約できないため、100tickごとに試み、条件不成立の命令は実行側に捨ててもらう形です。村人の自動配分や訓練キューの残り時間も直接制御できません。
