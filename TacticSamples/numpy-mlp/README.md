# numpyで「守る／攻める」を選ぶ戦術

同梱のPyodideとnumpyで `main.py` を実行します。普通のPythonやpipは試合中に起動しません。
4つの特徴（自軍兵力、可視敵の件数、経過tick、定数）を、小さな全結合モデルで2つのスコアへ変換します。
重みの見本は手書きです。学習済みの強い戦術を保証するものではありません。

```python
hidden = np.maximum(features(view) @ weights['w1'] + weights['b1'], 0)
attack = int(np.argmax(hidden @ weights['w2'] + weights['b2'])) == 1
```

## 学習して試合で使う

1. ゲームの外で `practice/` の `RtsEnv.reset` と `step` を使い、対戦を繰り返して重みを学習します。観測は `on_tick(view)` と同じ形です。
2. `np.savez` で `w1` (4,4)、`b1` (4,)、`w2` (4,2)、`b2` (2,) を `models/policy.npz` に保存します。特徴の順序・正規化は `features` に合わせてください。
3. この戦術フォルダをゲームで選択します。CLIでは次のように試せます。

```powershell
dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed 2 --ticks 1200 --west-tactic TacticSamples/numpy-mlp --east-tactic auto --log-out numpy-match.jsonl
```

実行環境は既定でリポジトリの `UnityProject/Assets/StreamingAssets/TacticRuntimes` を探します。
別の配置は `--runtimes <TacticRuntimesフォルダ>` で指定します。ゲームは常にStreamingAssetsから探します。
同梱の見本を再生成するには `python TacticSamples/numpy-mlp/make_model.py` を実行します（numpyが必要）。

戦術フォルダはPyodideの `/tactic` にマウントされ、作業フォルダになります。`models/*.npz` やJSON、同梱の純Pythonモジュールを読めます。
ホストPCへの書き込み・ネット・環境変数・プロセス起動は禁止です。`np.load` は `allow_pickle=False` で使います。
`random` と `numpy.random` は試合seedで初期化されます。`np.random.default_rng` を使う場合は `match_seed` を明示的に渡してください。

起動は10秒、コードの読み込み・`on_start`・各 `on_tick` は50msが上限です。連続3回の失敗でその試合の戦術を停止します。
時間切れの返事は破棄されます。終了できない呼び出しが残っている間も次の戦術判断の待ち時間は50msまでです。
`print` は1回20行・1行200文字まで記録されます。
