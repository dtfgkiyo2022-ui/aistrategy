# boom-then-strike

序盤は `Growth` の内政方針で資源と時代を伸ばします。`economy.agesEnabled` と `economy.canAdvanceNow` が真のときだけ `AdvanceAge` を送り、送った回だけログを出します。`STRIKE_TICK` に達して自軍兵力が `STRIKE_SOLDIERS` 人以上になった時点で全軍を敵コアへ送ります。

`STRIKE_TICK`、`STRIKE_SOLDIERS`、`GROWTH_PRIORITY`、`STRIKE_PRIORITY` を変えると、攻撃開始の早さと慎重さが変わります。時代進行を急がず兵を増やしたい場合は `AdvanceAge` の行を削除できます。

## お任せ（auto）相手の結果

`tactic-match --ticks 12000 --west-tactic TacticSamples/boom-then-strike --east-tactic auto` を map seed 1〜4 で実行した結果です。

| map seed | 西側の結果 | 決着（tick÷1200） |
|---:|---|---:|
| 1 | 負け（東側勝利） | 4.15分（4974÷1200） |
| 2 | 負け（東側勝利） | 4.52分（5421÷1200） |
| 3 | 負け（東側勝利） | 3.93分（4717÷1200） |
| 4 | 未決着 | — |

`AdvanceAge` は `canAdvanceNow` が真のときだけ送ります。次の時代の費用は `nextAgeCost`、進行中かどうかは `advancingTo` と `advanceRemainingTicks` で確認できます。文明の選択肢一覧は `EconomyView` にないため、原始時代からは例として `Agrarian` を選びます。

## 書きにくかったこと

文明の選択肢一覧は戦況に公開されていません。村人の自動配分や訓練キューの残り時間など、費用以外の実行条件はシミュレーション側にも残るため、命令送信後に `advancingTo` と `advanceRemainingTicks` の変化を確認します。
