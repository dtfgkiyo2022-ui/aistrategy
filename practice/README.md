# Python 練習場

このフォルダは Unity の `Assets` には入りません。ゲームの外でヘッドレスの戦術を試し、学習結果を戦術フォルダへ戻すための場所です。Python 3.10 以上が必要です。`gymnasium` と `numpy` は任意で、なくても標準ライブラリだけで動きます。

## 準備

リポジトリ直下で CLI をビルドします。

```powershell
dotnet build Headless/Rts.Headless.Cli/Rts.Headless.Cli.csproj -c Release
$env:RTS_CLI = (Resolve-Path Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll)
```

`RtsEnv` に DLL のパスを直接渡しても構いません。新しい Python パッケージのインストールは不要です。

## 最小の使い方

```python
from practice.rts_practice import RtsEnv

env = RtsEnv()                 # RTS_CLI を使う
observation, info = env.reset(seed=123)
observation, reward, terminated, truncated, info = env.step([])
env.close()
```

`observation` は `TacticViewWriter` 版1の JSON を Python の `dict` にしたものです。`step` の action は `TacticCommandReader` 版1の `commands` 配列です。よく使う命令は `practice.actions.focus`、`defend`、`train`、`place_building` で組み立てられます。

付属例は次のように実行します。

```powershell
python practice/examples/random_agent.py
python practice/examples/simple_policy_search.py --episodes 2
```

後者は兵力しきい値を5通り試し、勝率の集計を `best.json` に書き、`TacticSamples/threshold-from-json/main.js` の先頭の `ATTACK_THRESHOLD` を書き換えます。

つまみを対戦結果で自動調整する例もあります。これは機械学習の一番簡単な形である「探索」です。指定した `params` の候補を世代ごとに試し、東西を入れ替えた試合の勝ち数を主にして、決着の早さも使って選びます。既定では同時実行数が2です。

```powershell
python practice/examples/tune_params.py TacticSamples/guarded-spear guardLossPermille --generations 2 --seeds 2 --jobs 2
python practice/examples/tune_params.py TacticSamples/guarded-spear guardLossPermille --apply
```

結果は `practice/out/tune-guarded-spear.json` に保存されます。`--apply` を付けると、最良値を `practice/out/guarded-spear/tactic.json` のコピーへ書き込みます。`TacticSamples` の見本そのものは変更しません。探索の次の段階として、`TacticSamples/numpy-mlp` の見本では numpy の小さなニューラルネットワークで重みを学びます。

## CLI の要求と応答

CLI は標準入力・標準出力を JSON Lines として扱います。ログと `--profile` の測定値は標準エラーです。

```json
{"op":"reset","mapSeed":123,"faction":1,"opponent":"auto","maxTicks":12000}
{"op":"step","commands":[],"ticks":20}
{"op":"close"}
```

`reset` は `{"ok":true,"view":<戦況>,"tick":0}`、`step` は `view`、`tick`、`done`、`winner`、`reward`、`info` を返します。`info.rejected` に、個別に受け付けられなかった命令が入ります。`enemyCoreHpKnown` が `-1` のときは霧のため敵コアの HP を知らない状態です。`maxTicks` 到達は勝敗ではなく時間切れで、Python 側では `truncated=True`、報酬は0です。

相手は `auto`、`idle`、`rush`、または `tactic.json` と `main.js` のある戦術フォルダです。`ruleFlags` には `terrain`、`large`、`economy`、`industry`、`ages`、`processingChain`、`gold`、`masonry`、`bridge` を指定できます。

1回の処理時間を測るときは CLI に `--profile` を付けます。

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-gym --profile
```

## 報酬を変える

既定では、勝ちが `+1`、負けが `-1`、時間切れが `0` です。試合中は「敵コアへの損害 − 自コアへの損害」に `0.001` を掛けた小さな shaping 報酬を返します。内訳は `info.outcomeReward` と `info.damageReward` にあります。

Python 側では `reward` をそのまま使わず、例えば `info` の兵力差やコア HP 差から自分の報酬を再計算できます。CLI や Contracts を変更する必要はありません。

## 学習結果を試合で使う流れ

1. `RtsEnv` で `reset` と `step` を繰り返し、`best.json` などの重み・パラメータを Python で作る。
2. `TacticSamples/threshold-from-json` のように、モデルの値を戦術フォルダの JS の定数へ入れる。今回の例は `simple_policy_search.py` がこの書き換えを行う。
3. `tactic-match --west-tactic <フォルダ>`、またはゲーム側の戦術フォルダ選択で試合に使う。

試合中は Python の学習コードを実行せず、後から入れたモデルのデータを読む戦術だけを動かします。

## テスト

CLI をビルド済みにして、リポジトリ直下から次を実行します。

```powershell
python -m unittest practice.tests.test_env
```

テストは `RTS_CLI` が設定されているか、Release DLL が存在する場合に `reset` → `step` 5回 → `close` を確認します。
