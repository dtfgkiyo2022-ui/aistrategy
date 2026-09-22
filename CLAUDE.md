# AI Command RTS — 共通ルール

このリポジトリは2人で開発する、AI指揮型の大規模対戦RTS（Unity・PC向け）です。
Claude Code はこのファイルのルールを必ず守ってください。

## 正となる資料

- 企画：`AI指揮型_大規模対戦RTS_企画書.md`
- 技術設計（MVP Ver.1）：`docs/technical-design.md`
- 技術設計（MVP Ver.3：内政と文明）：`docs/technical-design-v3.md`（Ver.1 の契約に足すもの。矛盾したら Ver.1 を優先）
- 進め方：`ROADMAP.md`、タスクは GitHub Issues（マイルストーン＝段階、ラベル＝担当）

資料と矛盾する実装はしない。矛盾に気づいたら、実装せずに報告する。

## 作業の再開（オーナー側）

オーナー側（A）の作業を再開するときは、**最初に `docs/handoff.md` を読む**。現状、次の作業、一時的な運用の変更、このリポジトリの環境で実際に踏んだ落とし穴がまとめてある。

## 開発者と役割

| 担当 | 人 | 範囲 | 使うAI |
|---|---|---|---|
| A：シミュレーション・命令・再生 | オーナー | Contracts原案、Decision、Simulation、Application、Replay、Headless | 実装は Codex に発注し、Claude Code はレビュー |
| B：Unityの表示・入力・検証UI | 相方 | Presentation、UnityHost、Scenes、Prefabs、仮素材、UI | Claude Code |

相方はプログラミング未経験です。相方のセッションでは：
- 専門用語は短く言い換えて説明する
- 変更したら「何を変えたか」「どう確認すればよいか（画面でどう見えるか）」を必ず伝える
- 担当外（A の範囲）のファイルは変更しない。必要なら Issue に書いてオーナーに依頼する

## 絶対に守る技術ルール

1. **Simulation 系アセンブリ（Contracts / Decision / Simulation / Replay / Application）は UnityEngine・UnityEditor を参照しない。** float・double・`UnityEngine.Vector3`・`Mathf`・`Time`・`Random` を判定に使わない。数値は `Fix64`（固定小数点）。
2. **表示側からシミュレーションの状態を書き換えない。** Presentation は `FactionFrame` などの読み取り専用データを表示するだけ。入力は `ICommandPort` 経由の命令としてだけ送る。
3. Unity の Physics・Collider・NavMesh・Transform・アニメーションの結果をゲームの判定に戻さない。
4. 兵士ごとの `MonoBehaviour.Update()` に戦闘判断を書かない。更新は中央の tick 処理だけ。
5. `Dictionary`/`HashSet` の列挙順、`GetHashCode`、壁時計、GUID、`Thread.Sleep` に判定を依存させない。
6. AI（Decision）には、その陣営が観測できた情報（`FactionObservation`）だけを渡す。
7. `Contracts`（A と B の共通の型）は、2人の合意なしに変更しない。変更時は相手側の利用箇所も直す。

## Git の進め方

- 作業は Issue ごとにブランチを作る（例：`b/12-camera`）。main に直接コミットしない。
- Pull Request を作り、テストが通り、もう一人（またはオーナー側の Claude Code）のレビューを受けてからマージする。
- **シーン（.unity）と Prefab は担当者を決め、2人で同じファイルを同時に編集しない。** 共有が必要なら先に声をかける。
- 大きな素材ファイルは Git LFS で管理する。`Library/`、`Temp/`、`Obj/`、`Logs/`、`UserSettings/` はコミットしない。
- 有料素材は利用許諾を確認してから追加する。

## テスト

- Headless は `Headless/Rts.Core.Tests/Rts.Core.Tests.csproj` の `Compile` 許可リストに列挙した Unity 非依存テストだけをリンクする。追加時は UnityEngine／UnityEditor に依存しないことを確認し、このリストにも追加する。
- リポジトリ直下で `dotnet test Headless/Rts.Headless.slnx --configuration Release` を実行する（SDK は直下の `global.json` で固定）。
- Simulation 系の変更には EditMode テストか Headless のテストを必ず付ける。
- 決定論テスト（同じ入力で全 tick のハッシュが一致すること）を壊す変更はマージしない。
- 表示の確認は、手順と期待する見え方を PR に書く。

## コミュニケーション

- 日本語で書く。
- 完了を報告するときは、確認した方法と、確認できていないことを分けて書く。
