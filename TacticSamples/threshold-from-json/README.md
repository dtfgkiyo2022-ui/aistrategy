# threshold-from-json

自軍の兵が `ATTACK_THRESHOLD` 人を超えるまで自陣コアを守り、その後に敵コアを攻めます。

`practice/examples/simple_policy_search.py` が複数のしきい値を試し、最良の値を `best.json` に保存してから、`practice/out/threshold-from-json/` に写したこの戦術の `main.js` 先頭の `ATTACK_THRESHOLD` を書き換えます（このフォルダ自体は書き換えません）。生成されたフォルダを `tactic-gym` の `opponent` に指定するか、試合の戦術フォルダとして使えます。

