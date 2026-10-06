# outpost-first

序盤は自軍が所有している前哨を全部隊の守備目標にし、前哨を足場として維持します。`OUTPOST_ATTACK_TICK` に達し、`ATTACK_SOLDIERS` 人以上になったら、全軍を敵コアへ向けます。自陣の前哨がまだ見つからない場合は自軍コアを守ります。

動きを変える定数は `main.js` 上部の `OUTPOST_ATTACK_TICK`、`ATTACK_SOLDIERS`、`DEFEND_PRIORITY`、`ATTACK_PRIORITY` です。`allowedLossPermille` と `reservePermille` を変えると、前哨防衛の慎重さも変わります。内政は100tickごとに `Military` を指定します。

## お任せ（auto）相手の結果

`tactic-match --ticks 12000 --west-tactic TacticSamples/outpost-first --east-tactic auto` を map seed 1〜4 で実行した結果です。

| map seed | 西側の結果 | 決着（tick÷1200） |
|---:|---|---:|
| 1 | 負け（東側勝利） | 4.15分（4974÷1200） |
| 2 | 負け（東側勝利） | 4.52分（5421÷1200） |
| 3 | 負け（東側勝利） | 3.93分（4717÷1200） |
| 4 | 未決着 | — |

## 書きにくかったこと

前哨をまだ所有していないときに「最寄りの未所有前哨を取りに行く」命令は、`Scope=Army` と `Defend/Focus` の語彙だけでは安全に表現できません。可視の敵軍人数も直接はなく、拠点の距離や防衛兵力も渡されないため、時間と兵力の定数で攻撃へ切り替えています。`economy` の建設セルも地図上の前哨と結び付いていないため、前哨建設命令は出していません。
