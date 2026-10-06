# threshold-from-json

自軍の兵が `ATTACK_THRESHOLD` 人を超えるまで自陣コアを守り、その後に敵コアを攻めます。

`practice/examples/simple_policy_search.py` が複数のしきい値を試し、最良の値を `best.json` に保存してから、`practice/out/threshold-from-json/` に写したこの戦術の `main.js` 先頭の `ATTACK_THRESHOLD` を書き換えます（このフォルダ自体は書き換えません）。生成されたフォルダを `tactic-gym` の `opponent` に指定するか、試合の戦術フォルダとして使えます。

## お任せ（auto）相手の結果

| map seed | 西側の結果 | 決着（tick÷1200） |
|---:|---|---:|
| 1 | 負け（東側勝利） | 4.15分（4974÷1200） |
| 2 | 負け（東側勝利） | 4.52分（5421÷1200） |
| 3 | 負け（東側勝利） | 3.93分（4717÷1200） |
| 4 | 未決着 | — |
