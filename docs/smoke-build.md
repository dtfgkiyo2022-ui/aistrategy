# 製品版スモーク

Unity 6 で IL2CPP / Windows 64bit のプレイヤーを作ります。`-smokeOut` は出力フォルダです（省略時は `D:/rts-verify/smoke/player/Game.exe`）。

```powershell
Unity.exe -batchmode -nographics -quit -projectPath <リポジトリ>/UnityProject -executeMethod Rts.Editor.SmokeBuild.Build -smokeOut D:/rts-verify/smoke/player
```

プレイヤーは `-rts-smoke` を付けたときだけ画面なしのシミュレーションを実行し、指定した JSON を書いて終了します。戦術名は `TacticCatalog` のフォルダ名または `tactic.json` の `name` です。

```powershell
D:/rts-verify/smoke/player/Game.exe -batchmode -nographics -rts-smoke -rts-smoke-west guarded-spear -rts-smoke-east numpy-mlp -rts-smoke-out D:/rts-verify/smoke/result.json
```

`-rts-smoke-ticks` の既定値は 2400、`-rts-smoke-seed` を指定するとマップを再現できます。JSON には両陣営の読み込み状態、呼び出し・命令・失敗・ログ、`FactionFrame.Commands` の Ai 命令数、勝者、tick、ランタイム呼び出し時間の p50 が入ります。

`firstFailureReason` は最初の失敗（起動時の失敗を含む）の理由です。`lastFailureReason` だけだと「停止しています」しか見えないことがあります。

ビルドすると `ProjectSettings/ProjectSettings.asset`（スクリプトの方式が IL2CPP になる）や URP の設定ファイルが書き換わります。コミットせずに `git checkout` で戻してください。

## 結果（10-06、種1・2400 tick）

| 戦術 | 言語 | 結果 |
|---|---|---|
| guarded-spear | JS（Jint） | 動いた。120回呼び、480件の命令がすべて受け付けられ実行された。1回 p50 0.66ms |
| numpy-mlp | Python（Deno＋Pyodide） | 最初は**動かなかった**（Deno を起動できない）。下の修正のあとは動いた：120回呼び、失敗0、480件の命令がすべて受け付けられた（467件が実行まで進んだ）。1回 p50 0.89ms。終了後に deno.exe は残らない |

Python の原因：Unity 6（6000.3）の IL2CPP は、Windows で `System.Diagnostics.Process.Start` を実装していません（`libil2cpp/icalls/System/System.Diagnostics/Process.cpp` の `CreateProcess_internal` が「未実装」で false を返す）。そのため `Win32Exception ... Native error= Success` になります。修正は Windows だけ `CreateProcessW`・`CreatePipe`・Job Object を直接呼び、UTF-8 の標準入出力をつなぐ方法です。

子プロセスの環境変数は `DENO_DIR` だけにしています。`SystemRoot` を渡すと最初の `start` の呼び出しが約 27ms から 50ms に遅くなり、1回 50ms の上限を超えました。標準出力は専用のスレッドで行ごとに読み、スレッドプールを経由しません。
