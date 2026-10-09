# 戦術を作って遊ぶ手引き

この手引きは、プログラミングが少しできる人にも、ほとんど初めての人にも向けたものです。

> **今の版について（2026-10）**：`dotnet ...` で始まるコマンド（ルールブックを作る・試合を画面なしで回す・練習場）は、開発中のリポジトリから動かす前提です。製品版ではこれらをゲームに組み込む予定です。それまでは、試合の記録パックの中にある `rulebook.md`（その試合のルールブック）を使ってください。

## 目次

1. [戦術とは何か](#戦術とは何か)
2. [戦術を置く場所](#戦術を置く場所)
3. [はじめての戦術](#はじめての戦術)
4. [Claude Codeで直す → 読み直す](#claude-codeで直す--読み直す)
5. [Claude Code・ChatGPTに頼む文例](#claude-codechatgptに頼む文例)
6. [試合の後の振り返り](#試合の後の振り返り)
7. [練習場で学習して戦術に入れる](#練習場で学習して戦術に入れる)
8. [戦術のつまみ](#戦術のつまみ)
9. [人の操作と組み合わせる](#人の操作と組み合わせる)
10. [戦術の合図](#戦術の合図)
11. [決まりごと](#決まりごと)

## 戦術とは何か

戦術は「お任せの戦い方」を自分のプログラムで決める仕組みです。試合中はおよそ1秒ごとに見えている戦況を受け取り、方針や内政の命令を返します。兵士一人ひとりを操作するのではなく、「守る」「集める」「攻める」のような大きな判断を任せます。

## 戦術を置く場所

戦術は次のフォルダに1つずつ置きます。`<名前>` は半角英数字などの分かりやすい名前にしてください。

```text
Documents\AiCommandRts\Tactics\<名前>\
  tactic.json
  main.js          （JavaScriptの場合）
  または main.py   （Pythonの場合）
```

`tactic.json` には名前、作者、言語、入口、APIの版などを書きます。既存の `TacticSamples` のフォルダをコピーして始めると安全です。ゲームの試合設定を開いて「一覧を更新」を押すと、選択肢だけが読み直されます。試合はやり直しません。戦術のフォルダがなければ、パネルの「戦術のフォルダを開く」で作成できます。

## はじめての戦術

同梱の見本には、戦術と人の操作の分担を考えるための型があります。`guarded-spear` は**全部お任せ型**で、部隊を選ばなくても攻撃と守備を進めます。`adjutant` は**相棒型**で、人が動かしている部隊には触らず、残りの部隊を守ります。`rally` は別の相棒型で、合図がない間は守備と兵力温存を続け、`rally`・`push`・`fallback` の合図で残りの部隊を集結・攻撃・撤退させます。まず全部お任せ型では画面を見守り、相棒型では1部隊を選んで自分で動かし、`rally` では合図のタイミングも試してみてください。どれが強いと決めつけず、自分がどこを分担したいかを比べるための出発点です。

まず、同梱の `defend-then-push` を自分のフォルダへコピーします。PowerShellなら次のようにします。

```powershell
$tactics = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'AiCommandRts\Tactics'
New-Item -ItemType Directory -Force $tactics | Out-Null
Copy-Item -Recurse -Force '.\TacticSamples\defend-then-push' (Join-Path $tactics 'my-first-tactic')
```

`my-first-tactic\tactic.json` をメモ帳やClaude Codeで開き、`attackThreshold` の `default` を `30` から `40` に変えます。これは「兵が40人を超えるまで守る」という変更です。ゲームを起動して、試合の設定で「一覧を更新」を押し、自軍の戦術に `my-first-tactic` を選びます。何度か試合を行い、守る時間や攻め始める時期が変わったかを記録パックで確かめてください。

## Claude Codeで直す → 読み直す

試合を止めたりやり直したりせずに、記録パックを見ながら戦術を直せます。まずパック内の `tactic-log.jsonl` と `snapshots.jsonl` をClaude Codeに読ませ、事実と推測を分けて次に試す変更を相談します。Claude Codeで `main.js`、`main.py`、または `tactic.json` を保存したら、ゲームの試合設定にある対象戦術の「読み直す」を押してください。現在の戦術をその場で作り直し、同じフォルダの新しい定義を次の呼び出しから使います。試合はtick 0に戻りません。

同じ名前のつまみが新しい定義にもあり、値が新しい範囲とstep（choiceならchoices）に合っていれば、現在値を引き継ぎます。引き継げないつまみは新しい既定値になります。読み込みに失敗した場合は前の戦術のまま試合を続け、パネルに理由を表示します。

「自動で読み直す」を入れると、ゲームが数秒に一度、戦術フォルダの `tactic.json`・`main.js`・`main.py` の更新日時を確認し、変化があったときだけ同じ読み直しを行います。既定は切です。成功・失敗・時刻・対象戦術は記録パックの `tactic-log.jsonl` に残ります。

## Claude Code・ChatGPTに頼む文例

コードが分からなくても、ルールと目的を伝えれば作れます。次の文をそのまま貼り、必要な部分だけ書き換えてください。

```text
ルールブック（tactic-rulebook で作る rulebook.md）を読んで、敵の兵が自軍コアに近づいたら最寄りの軍団を守備に回し、危険がなくなったら敵コアを攻める戦術を書いて。tactic.json と main.js を含むフォルダで出力して。
```

```text
この記録パック（Documents\AiCommandRts\Packs\＜試合のフォルダ＞）を読んで、負けた理由と直し方を考えて。原因を推測と事実に分け、my-first-tactic\main.js を直して。変更点と試す数字も説明して。
```

```text
ルールブックとこの戦術フォルダを読んで、Python（numpy）で同じ方針を書いて。Pyodideの試合内ではファイル・ネット・環境変数を使わず、tactic.json、main.py、必要ならmodelsフォルダだけを作って。
```

```text
defend-then-push を元に、守る人数のしきい値を試合の開始時の兵力と経過tickから計算する戦術へ変更して。既存の命令形式を守り、変更したファイルだけを示して。
```

ルールブックはCLIで作れます。

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-rulebook --out .\rulebook
Get-Content .\rulebook\rulebook.md
```

ゲーム内でも、試合設定の「フォルダ」欄にある「ルールブックを書き出す」を押せます。現在の試合シナリオからルールブックを作り、`Documents\AiCommandRts\Rulebook\rulebook.md` と `rulebook.json` に保存して、そのフォルダを開きます。Claude Codeに戦術を書いてもらうときは、この `rulebook.md` を先に読ませてください。

## 戦術のつまみ

`tactic.json` の `params` に、試合中に変えられる設定を宣言できます。数値は `min`、`max`、`step` と `default` を書き、`int` は整数、`number` は小数です。`bool` は真偽値、`choice` は `choices` 配列から選ぶ文字列です。名前は半角英数字だけにします。

メタデータには任意で `style` と `recommended` も書けます。`style` は `auto`（全部お任せ型）または `partner`（相棒型）のどちらかで、画面に型の説明を出します。`recommended: true` にした戦術は、試合設定の一覧の先頭に「おすすめ」として表示されます。省略した場合は従来どおりで、標準のおすすめにはなりません。

```json
"params": [
  { "name": "attackThreshold", "label": "攻めに切り替える兵の数", "type": "int", "default": 30, "min": 5, "max": 100, "step": 5 },
  { "name": "aggressive", "label": "積極攻撃", "type": "bool", "default": false },
  { "name": "mode", "label": "攻撃モード", "type": "choice", "default": "safe", "choices": ["safe", "rush"] }
]
```

`onStart(setup)` では `setup.params.attackThreshold`、毎回の `onTick(view)` では `view.params.attackThreshold` のように読みます。変更した値は次の呼び出しから戦術へ渡されます。試し遊びのパネルでは自軍のつまみだけ操作でき、相手は現在値だけ表示されます。

CLIの開始値は次のように指定します（同じ側の指定は複数書けます）。

```powershell
dotnet ... tactic-match --map-seed 2 --ticks 1200 --west-tactic TacticSamples/defend-then-push --west-param attackThreshold=50
```

`tactic-match` でゲーム本体に近い地形マップと時代進行を使う場合は、`--terrain --ages` を追加します。ゲーム本体の `LiveMatchHost.Begin` は通常、経済ありの地形マップ（`MapGenerator.GenerateTerrain`）を使うため、CLIでも次の組み合わせを基本にします。

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed 2 --terrain --ages --ticks 12000 --west-tactic TacticSamples/mixed-arms --east-tactic auto
```

`--ages` は `economy.agesEnabled` を有効にし、`--terrain` は森・川・山を含む地形マップを選びます。時代を使わない従来の経済マップを再現する場合は、これらのフラグを付けません。

## 人の操作と組み合わせる

`view.orders` には、いま効いている自分の命令が並びます。`source` と `kind` を見ると、人が攻めているのか、戦術や定型方針が動いているのかを判断できます。`view.ownArmies[i].controlledBy` は、その部隊をいま誰の命令が動かしているか（`Human`＝人 / `Doctrine`＝お任せの方針 / `Tactic`＝戦術（あなたの戦術を含む） / `Ai`＝参謀（言葉で話しかけた AI） / `None`）です。`Tactic` は 2026-10-09 に足しました（それまで戦術の命令も `Ai` でした）。

人が動かしている部隊には命令を出さず、残りの部隊で守りや別の仕事を考えてみてください。人が攻めているときは、コアや拠点をどの部隊で守るかを見直すのも手です。人が何もしていないときは、全軍を同じ仕事に固定せず、守備と内政の組み合わせを試してみましょう。

### 内政のラインを頼む

戦術からは、資源地点に近い加工ラインの建設を自動内政へ依頼できます。区域番号は戦況で確認した自軍区域を使い、`line` は `CoreMetal`、`Steel`、`CoreWood`、`BowGear` のいずれかです。たとえば次の命令は、区域3の資源に近い鋼ラインを頼みます。

```json
{"type":"economy","kind":"RequestLine","line":"Steel","region":3}
```

戦術の JSON では `region` は数値です。対象文明・時代・資源・木材が不足している場合は、シミュレーションが命令を変えずに却下し、理由をタイムラインへ記録します。ラインは一度頼んだ後も自動管理のままで、人が建物・ベルト・運び手を直接変更したときだけ手動管理へ移ります。

内政を見本から試すなら `TacticSamples/steel-economy` が使えます。冶金に入った後の金属ラインと、第2時代に入った後の鋼ラインを一度ずつ頼みます。`metalDelaySeconds`／`steelDelaySeconds` で頼む時期を、`regionChoice` で自コアに近い区域か資源の多い区域かを変えられます。

## 戦術の合図

`tactic.json` の任意の `signals` に、人が戦術へ送る合図を宣言できます。`name` は英数字、`label` は画面に出す名前、`needsPoint` は地図上の地点が必要かどうかです。合図は8個までで、同じ名前は使えません。

```json
"signals": [
  { "name": "allIn", "label": "総攻撃", "needsPoint": false },
  { "name": "holdHere", "label": "ここを守れ", "needsPoint": true }
]
```

自軍の戦術欄にボタンが並び、地点が必要な合図はボタンを押してから地図をクリックします。Escまたは右クリックで取り消せます。合図は次に戦術を呼ぶときだけ `view.signals` に入り、古い合図は次の回には残りません。各要素は `name` と送信時の `tick` を持ち、地点付きなら `point.x` / `point.z` も持ちます。戦術側では、たとえば `view.signals.some(s => s.name === "allIn")` のように、合図を受けた回の判断へ使います。

CLIで試すときは `--west-signal "1200:allIn"`、地点付きなら `--west-signal "2400:holdHere@120,80"` とします。同じtickに複数指定した合図は指定順に届きます。記録パックの `tactic-log.jsonl` にも、渡されたtick・名前・地点が `signals` として残ります。合図に何をさせるかは戦術作者が決め、ルールブックは名前と地点の意味を決めません。

## 試合の後の振り返り

試合が終わると記録パックが `Documents\AiCommandRts\Packs\` に保存されます。パネルの「記録パックのフォルダを開く」から開けます。まずパック内の `README.md` を読み、その指示どおりに進めてください。次に見る順番の目安は次のとおりです。

1. `README.md`：このパックの版、試合条件、見るべきファイル。
2. 時系列のJSONやCSV：いつ接敵し、拠点を失い、命令を出したか。
3. 命令の記録：どのtickにどの戦術の命令が出て、受理・破棄・失敗になったか。
4. `replay` と状態ハッシュ：同じ試合を再生できるか。
5. 振り返り：霧を外した診断と、敗因の候補。推測は事実と分けて読む。

「兵力が足りなかった」のような結果だけでなく、「北を守る命令を出した直後に南を失った」「返事が遅れて命令が間に合わなかった」のように、次に試す数字や方針を1つ決めるのがコツです。

`credit.json` では、部隊が人（`Human`）・お任せ（`Ai`）・戦術（`Doctrine`）のどの命令に従っていたかを、時間、損害、見えていた敵の撃破、拠点の出来事、命令の結果に分けて読めます。時間の割合が大きい出どころで損害が集中していないか、目立つtickを `timeline.jsonl` とリプレイで確かめます。人が動かした部隊の損害が多いなら、その場面を戦術に任せられないか考える、という使い方です。撃破は各陣営が見えていた範囲だけなので、帰属不能欄も推測と混ぜません。

## 練習場で学習して戦術に入れる

ゲームの外にある `practice/` は、試合を何度も回して数字やモデルを試す場所です。まずCLIをビルドし、PowerShellでDLLの場所を設定します。

```powershell
dotnet build Headless/Rts.Headless.Cli/Rts.Headless.Cli.csproj -c Release -p:UseSharedCompilation=false -nodeReuse:false
$env:RTS_CLI = (Resolve-Path Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll)
python -m unittest practice.tests.test_env
```

しきい値を探す例は `practice/examples/simple_policy_search.py` です。結果を `TacticSamples/threshold-from-json` のような戦術フォルダへ入れ、ゲームで選びます。`threshold-from-json` は、兵力がしきい値を超えるまで守り、その後に攻めます。

戦術の `params` を対戦結果から自動で調整するなら、`practice/examples/tune_params.py` を使います。これは機械学習の一番簡単な形である「探索」の見本で、候補値を世代ごとに試し、東西を入れ替えた数試合の勝ち数と決着の早さから次の候補を選びます。結果は `practice/out/tune-<戦術名>.json` に残り、`--apply` を付けた場合だけ `practice/out/<戦術名>/` にコピーした戦術の既定値へ反映します。

```powershell
python practice/examples/tune_params.py TacticSamples/guarded-spear guardLossPermille --generations 2 --seeds 2 --jobs 2
```

探索の次は、`TacticSamples/numpy-mlp` の見本のように numpy で重みを学習し、試合中は学習済みデータを読む流れです。つまり、まず数字の探索、次にモデルの重みの学習、最後に戦術へ組み込む、という順番です。

機械学習を使うなら `TacticSamples/numpy-mlp` を見本にします。`practice/` で学習した重みを `models/policy.npz` に保存し、`main.py` がそのデータを読む形にします。試合中に学習を実行するのではなく、試合の外で学習したデータを試合中に推論するだけにしてください。

```powershell
python practice/examples/simple_policy_search.py --episodes 2
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed 2 --ticks 1200 --west-tactic TacticSamples/numpy-mlp --east-tactic auto
```

実際に遊んだ試合の人の操作を練習場へ持ち込むには、まず記録パックの `replay.rpl` から指定陣営の方針命令を取り出します。

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll human-orders --pack Documents/AiCommandRts/Packs/<試合> --faction 1 --out D:/rts-verify/humanreplay/human-orders.jsonl
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed 2 --ticks 1200 --west-tactic TacticSamples/adjutant --west-human D:/rts-verify/humanreplay/human-orders.jsonl
```

Python の練習場では `RtsEnv(human_orders="...")` として渡せます。自分の操作を入れた試合と入れない試合で、戦術の成績がどう変わるか比べてみてください。記録した操作は相手が変わると対象の部隊・拠点が無くなり、合わなくなることがあります。記録パックの内政命令は入力に出どころが残らないため、この変換では対象外です。

## 決まりごと

- 戦術の判断は1回50ms以内を目安にします。返事が間に合わない回は命令なしで、今の方針が続きます。
- 失敗が続くと戦術は停止し、試合の残りは標準のお任せに戻ります。
- 戦術はファイル、ネットワーク、環境変数、キー、プロセス起動に触れません。共有された戦術も箱の中で動きます。
- 試合に使うのは `tactic.json` とコード、同梱したデータだけです。npmやpipを試合中に取りに行きません。
- 人の命令が戦術より優先されます。困ったときは一度戦術を外して、人の命令だけで同じ状況を試してください。
- Pythonでnumpyを使う場合はPyodideの見本に合わせます。普通のPythonは自分のPCで試す用途に限り、他人と共有する戦術には使いません。
