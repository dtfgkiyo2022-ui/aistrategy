# defend-then-push

最初は全部隊を自陣のコアへ向かわせる `Defend` を出し、自軍の兵が30人を超えたら全部隊を敵のコアへ向かわせる `Focus` に切り替えます。内政命令は出さず、お任せ内政を使います。

切り替え人数は `main.js` の `soldiers > 30` を変更します。守る／攻める目標、優先度、損害許容も同ファイルの `kind`、`goal`、`priority`、`allowedLossPermille` で変更できます。
