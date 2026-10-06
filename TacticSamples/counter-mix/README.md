# counter-mix

見えている敵の兵種を `enemySummary.byKind` で集計し、1種類が設定した割合以上なら相性のよい兵種へ生産を切り替える JavaScript 戦術です。敵が見えない、または混成で判断できない間は `Infantry` を標準にします。軍団は兵力が整うまで自コアを守り、しきい値に達すると敵コアへ反攻します。

## 相性と生産

相性はリポジトリの `docs/technical-design-v3.md` 32.13 の三すくみに従います。弓兵は歩兵に、騎兵は弓兵に、歩兵は騎兵に強く、強い側の攻撃に +50% の補正があります。ルールブック（`tactic-rulebook` の `rulebook.md`）で確認できる生産命令は `PlaceBuilding` と `Train`、建物には `Barracks`・`ArcheryRange`・`Stable` が含まれます。

生産前に建物の完成、食料・木材、人口上限、訓練キューを確認します。弓兵・騎兵は `Age2` 以降だけ選び、未到達時は歩兵へ戻します。専用建物が無ければ `PlaceBuilding` を先に出します。

## つまみ

| 名前 | 既定値 | 意味 |
|---|---:|---|
| `counterRatioPermille` | 600 | 見えている敵のうち、最多兵種がこの割合（‰）以上なら切替 |
| `attackThreshold` | 18 | 自軍兵力がこの数以上になったら反攻 |
| `counterSharePermille` | 700 | 相性兵を自軍に混ぜる上限割合（‰）。超えたら歩兵を挟む |

## 結果

実行コマンド：

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed <1..4> --ticks 12000 --west-tactic TacticSamples/counter-mix --east-tactic auto
```

| 自軍 | seed 1 | seed 2 | seed 3 | seed 4 |
|---|---|---|---|---|
| west（counter-mix） | 勝ち・3:17（3941 tick） | 未決着・10:00 | 未決着・10:00 | 未決着・10:00 |
| east（counter-mix） | 未決着・10:00 | 未決着・10:00 | 未決着・10:00 | 未決着・10:00 |

実測ログでは8試合すべて `rejected=0` でした。`Train` の送信数は west seed 1/2/3/4 が `0/2/1/0`（すべて Infantry）、east seed 1/2/3/4 が `1/0/0/2`（すべて Infantry）です。

`console` は全試合で、状況に応じて「未確認/混成 → Infantry」「Scout → Infantry」「Infantry → Infantry」と判断を切り替えました。seed 1〜4 の12000 tickでは相手に弓兵・騎兵が見えず、また専用兵を作れる `Age2` と建物・資源が揃う前に試合が終わるか時間切れになったため、Archer/Cavalry の実生産への切替は0件でした。これはログ上の事実であり、三すくみの分岐自体は `main.js` に実装しています。

## 弱点

霧の外の敵構成は見えず、`enemySummary.byKind` は見えている敵だけです。今回の4種では相手が弓兵・騎兵を出さなかったため、相性分岐を実戦で検証できませんでした。相手が早く接敵すると、時代・専用建物・資源が揃う前に歩兵へ戻るため、相性を活かせません。相手が混成で各兵種の割合がしきい値未満の場合も標準歩兵のままです。

## 戦況・命令で足りなかったこと

`view` には敵の見えている構成と自軍の経済状態はありますが、文明ごとの「今この兵種を作れるか」を直接返す能力情報がありません。そのため本戦術は時代と建物完成、資源・人口だけで保守的に判断しています。また `tactic-match` のログは戦術が送った命令と `rejected` の理由を記録するため、受理済み `Train` の完了数は別途スナップショットが必要です。
