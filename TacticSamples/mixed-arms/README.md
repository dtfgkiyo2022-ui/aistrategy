# mixed-arms

`Growth` で内政を育て、`AdvanceAge` で時代を進めてから弓兵と騎兵を中心に作る JavaScript 戦術です。Age2到達前は歩兵を作れる場合だけ歩兵で守り、到達後は `archerPermille` を目標割合として弓兵場と騎兵の厩を使い分けます。軍団は `attackThreshold` 人に達するまで自コアを守り、達した後に敵コアへ反攻します。

## つまみ

| 名前 | 既定値 | 意味 |
|---|---:|---|
| `archerPermille` | 550 | 自軍の弓兵を目標にする割合（‰）。未達なら Archer、達成後は Cavalry を訓練 |
| `attackThreshold` | 28 | 反攻を始める自軍兵力 |
| `ageTarget` | 1 | 専用兵を作り始める時代。1 は Age2、2 は Age3 |

## 実行例

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed 1 --terrain --ages --ticks 12000 --west-tactic TacticSamples/mixed-arms --east-tactic auto
```

時代を使うので `--ages` を付けます（付けないと時代が無効で、弓兵・騎兵を作れません）。`--terrain` はゲーム本体に近い地形のマップです。

## 結果（10-07、`--terrain --ages --ticks 12000`）

### mixed-arms 対 auto

| 自軍 | seed 1 | seed 2 | seed 3 | seed 4 |
|---|---|---|---|---|
| west（mixed-arms） | 勝ち（8129 tick・6.8分） | 負け（5418 tick・4.5分） | 未決着 | 未決着 |
| east（mixed-arms） | 負け（7411 tick・6.2分） | 未決着 | 負け（4882 tick・4.1分） | 未決着 |

### mixed-arms 対 counter-mix

8試合（種1〜4・東西入れ替え）すべて未決着。

### 実際に作った兵

- 16試合すべてで捨てられた命令は0件
- **時代を進めたのは16試合中2回だけ**（どちらも東の mixed-arms：対 auto の種4、対 counter-mix の種3）。どちらも弓兵・騎兵を作る前に試合が終わった。10分の試合では Age2 の費用がたまらないことがほとんど
- そのため `Train` は全部 Infantry（1試合 3〜15 体）。**弓兵・騎兵は1体も作れていない**
- 相手の counter-mix も弓兵・騎兵を見ないので、相性による切り替えは起きていない

弓兵・騎兵を作れるかは、内政の釣り合い（Age2 の費用と、たまる速さ）次第です。序盤の釣り合いの調整のあとで測り直します。

## 限界

`economy` には兵種ごとの解禁条件一覧がないため、時代、建物の完成、資源、人口、訓練キューだけを確認して命令します。建設位置はルールブックの例に合わせて `cell: 0` としています。戦況には見えている敵しか含まれず、相手の全軍構成を直接知ることはできません。
