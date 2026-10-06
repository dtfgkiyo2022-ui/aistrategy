# turtle-age

コアを守る `Defend` を続け、`Growth` で内政します。戦況の `economy.agesEnabled` と `economy.canAdvanceNow` が真のときだけ `AdvanceAge` を送り、送った回だけログを出します。`ATTACK_AGE` に到達し、`ATTACK_SOLDIERS` 人以上になったら、全軍を敵コアへ向けます。守備中は損害許容を低くし、予備を厚くしています。

`ATTACK_AGE`、`ATTACK_SOLDIERS`、`TURTLE_LOSS`、`COUNTER_LOSS` を変えると、反撃の時期と危険の許容度が変わります。より早く反撃したい場合は `ATTACK_AGE` を `Age2` にできます。

## お任せ（auto）相手の結果

`tactic-match --ticks 12000 --west-tactic TacticSamples/turtle-age --east-tactic auto` を map seed 1〜4 で実行した結果です。

| map seed | 西側の結果 | 決着（tick÷1200） |
|---:|---|---:|
| 1 | 負け（東側勝利） | 4.15分（4974÷1200） |
| 2 | 負け（東側勝利） | 4.52分（5421÷1200） |
| 3 | 負け（東側勝利） | 3.93分（4717÷1200） |
| 4 | 未決着 | — |

`economy.age` は数値（Age1=0、Age2=1、Age3=2）です。`agesEnabled`、`advancingTo`、`advanceRemainingTicks`、`nextAgeCost`、`canAdvanceNow` を使って、費用が揃い進行中でないときだけ時代進行を送ります。文明の選択肢一覧は `EconomyView` にないため、原始時代からは例として `Agrarian` を選びます。

## 書きにくかったこと

防壁や塔を置く命令自体は語彙にありますが、建設位置をコア周辺の地形から選ぶ情報と、防衛対象へ紐付ける仕組みが不足しています。時代進行の文明選択肢一覧は戦況に公開されていないため、文明を増やす場合はルールブックやマップ設定を確認してください。
