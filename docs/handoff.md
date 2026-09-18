# オーナー側（A）作業の引き継ぎメモ

最終更新：2026-09-19。前のセッション（別アカウントの Claude Code）から、次のセッションへ渡すためのメモです。**作業を始める前に全部読んでください。**

このメモは「今どこまで進んでいて、次に何をするか」と「このリポジトリで作業するときの落とし穴」をまとめたものです。恒常的なルールは `CLAUDE.md`、仕様は `docs/technical-design.md`、進め方は `ROADMAP.md` と GitHub Issues が正です。このメモと矛盾したら、そちらを優先してください。

---

## 1. 最初にやること

1. このメモを最後まで読む
2. `git fetch` して `git log --oneline -5 origin/main` を確認する（下の「現状」から進んでいないか）
3. `gh issue list --state open` と `gh pr list` で、相方の動きがないか確認する
4. オーナーに「どこから再開するか」を確認する（候補は「5. 次にやること」）

---

## 2. 一時的な運用の変更（2026-09-23（水）11:38 まで）

**Codex（ChatGPT）が使えません。** 利用上限に達しており、復帰は 2026-09-23 11:38 です。上限はアカウント全体にかかり、`gpt-6-astra`・`gpt-5.6-terra`・`gpt-5.6-luna` のどれに切り替えても同じエラーになります（2026-09-17 に実測）。

普段の運用は「実装は Codex に発注し、Claude Code は発注文の作成とレビューに徹する」ですが、この期間は次のように変えます。

- **実装は Claude Code が行う。** 直接書くか、Opus のサブエージェント（`isolation: "worktree"`、背景実行）に委譲する。オーナーの指示は「クラウドの適切なモデルに作業依頼して」だった
- **Astra の設計レビューが取れない。** 設計の芯に関わる変更（勝敗を分ける仕組み、同期方式、AI の配分ルールなど）は、09-23 以降にレビューを取る前提で進めるか、保留する。進めた場合は報告に「Astra 未レビュー」と明記する
- 09-23 以降は元の運用に戻す（実装は Codex、Claude はレビュー）

---

## 3. 現状（2026-09-19 時点）

### ブランチと PR

- `main` は `3e8e20f`（PR #64「このメモ自体」・PR #65「判断猶予測定」のマージ済み）
- **PR #62（draft、未マージ）**：コア前の守衛（Sentry）。ブランチ `a/59-sentry`。**保留中**（→「4. 保留した実験」、状況は変わらず）
- ローカルのブランチは `main`・`a/59-sentry` のみ。作業用 worktree は片付け済み

### 2026-09-19 に完了した作業（#29）

- **PR #65 マージ済み**：Issue #29(2) 判断猶予の測定コードを実装。`UnityProject/Assets/RTS/Application/GraceMeasurement.cs`（測定本体）＋ Headless CLI の `grace` コマンド（`Headless/Rts.Headless.Cli/GraceCommand.cs`）。使い方・成功述語4種（コア防衛／撤退／増援／陽動対応）の判定基準は `Headless/Rts.Headless.Cli/README.md` の「grace: 判断猶予の測定」節に日本語で書いてある。Codex 利用上限中のため Opus サブエージェント（`isolation: "worktree"`、背景実行）に実装を委譲し、完了後にオーナー側 Claude Code がビルド・全体テスト（355件合格）を自分で再確認してからマージした。**Astra レビュー未実施**（設計判断は PR #65 の本文と README に明記）。
- Issue #29(1)（維持型・集中型プリセット）は既存の `PolicyPresets.cs` で完了条件を満たしていることを確認済み（新規実装は不要だった）。
- **Issue #29(3)（実際に猶予の境界が動く例を探す）に着手し、中断中。** `week2-2routes.json`（西=none, 東=none。基準：西コアが5229 tickで初被弾・5428 tickで破壊）で、西陣営に `MaintainReserve All reserve=300‰` を受付tick Rで与え、コア防衛（tick 6000生存）を成功述語として実測した：
  - R=1〜3975：成功／R=3990〜4000：失敗／**R=4020〜4300：成功（島）**／R=4400以降：失敗
  - **成功R集合が本当に非単調だった**（3990〜4000で一度失敗した後、4020〜4300で成功が戻る）。二分探索だけで境界を決めると4300の成功区間を見逃す。詳細は前アカウントのメモリ `issue29-grace-non-monotonic-finding.md` を参照（`C:\Users\きよ\.claude\projects\d----------------aistrategy-aistrategy\memory\` 配下）
  - **未確認**：島の左右端の厳密な1tick単位の境界（4000〜4020、4300〜4400はまだ粗い）、他の3述語（撤退・増援・陽動対応）、他のシナリオ・命令の組み合わせ、この「島」が起きる原因（自動配分AIとの干渉という仮説はダンプで未検証）
  - 次にやる場合の選択肢：(a) 島の幅を詰める、(b) 他の3述語でも同様の非単調性があるか確認する、(c) この結果をIssueにコメントして次へ進む、(d) Issue #27（共同）に切り替える。オーナーとは (a)〜(d) のどれにするか未決定のまま中断（次回セッションで確認する）

### 段階の進み具合（ROADMAP.md）

段階0〜3 の A 担当（シミュレーション）の実装はおおむね完了し、段階4 に入っています。B 担当（表示・UI）は相方が進めています。

### 開いている Issue（A 担当と共同のもの）

| Issue | 内容 | 状態 |
|---|---|---|
| #29 | 4-2 敵の方針2種類と判断猶予の測定 | (1)(2)完了、PR #65マージ済み。(3)探索作業中、中断。→「5. 次にやること」 |
| #28 | 4-1 2台のPC・Mono/IL2CPP・CLI での照合 | 相方の PC と Unity ビルドが必要 |
| #59 | お任せ AI のバランスの続き | **大半を5週目以降に延期**（→ 下の方針） |
| #27 | 3-7 段階3の統合と確認（共同） | #29・#28 の前提タスク。相方と一緒に行う |
| #63 | 【表示】守衛の見た目 | 相方へ「着手しないで」と連絡済み。保留 |
| #52・#54・#58 | 相方へのお知らせ（Contracts の変更） | 相方の確認待ち |
| #2 | Unity プロジェクトの骨組み | 相方の PC での確認待ち |

### オーナーが決めた方針：バランス調整は後回し

2026-09-17 にオーナーから「ゲームのバランスを調整する様な物は後でやるものではないか」と指摘があり、合意しました。

- **Ver.1 の目的は「AI 指揮という仕組みが成立するか」の検証**であって、強さや面白さの調整ではない。遊ぶ人も触った感触もない段階で数値を詰めても判断材料がない
- 数値を一要因ずつ比べる作業（HP 400／800／1600 のような比較）は **5週目以降**に回す
- 4週目のうちは **関門だけを見る**：設計書 9.2 章の合格の目安（お任せが関わる組み合わせは 20000 tick 以内にコア破壊で決着する）、Fault なし、決定論の維持
- ただし「測ろうとしたら不具合が出た」ときは、それは不具合として直す（バランスとは別物）

---

## 4. 保留した実験：コア前の守衛（PR #62）

### 何をしたか

Issue #59 で「コアが壊されるのは救援が間に合わないため」と実測で確定した（tick 5300 時点で救援は残 34.3 m・到着まで 343 tick、コアの残り寿命は 128 tick）。オーナーの案で、両陣営のコアの前に**動かないが強い兵（守衛）**を 2 体ずつ置いた。HP800・ダメージ40・射程8 m・速度0。

### 結果：両陣営が膠着したので保留

4 条件（`week2-2routes.json`、西=none 対 東=各プリセット、20000 tick）すべてが**未決着**になり、**コアが一度も攻撃されなかった**。守衛が敵を倒しきったのではなく、**戦闘がほとんど起きなくなった**（東の損失は 0〜3 人、両コア無傷）。

| 指標（none・陣営1） | 守衛が撃たない状態 | 守衛が撃つ状態 |
|---|---|---|
| 集合失敗 | 1 | 4 |
| 再挑戦 | 0 | 6 |
| 集合に費やした tick | 2447 | 10758 |
| 守備に拘束された tick | 12251 | 42391 |

守衛がコア付近で交戦すると 9.2 章手順1の危機判定が立ち続け、軍団が防衛に固定される。攻撃群が進撃判定を満たせず「集合失敗 → 600 tick 除外 → 再挑戦」を両陣営が対称に繰り返して膠着する。**実装の不具合ではなく、守衛が AI の脅威認識を変えてしまう設計上の副作用**。潰すには危機判定そのものの設計変更が必要で、Ver.1 の目的から外れるため保留した。**教訓：守る側を強くすると、両陣営対称なら攻める側も引き戻されて膠着する。守備を強くする案は、まず「AI が防衛に何 tick 拘束されるか」を見る。**

### PR #62 に残っている価値

- **`2b895ad` 実バグの修正**：`PolicyDecision.Tactics` の追撃サイクルは「任務位置の1 m以内へ帰還したら解除」なので、**速度0の兵は永久に抜け出せず、以後いっさい標的を選ばなくなる**。守衛は 5428 tick の間に一度も攻撃していなかった（`TargetKind=0`、`NextAttackTick=0`）。`TacticalInput.Immobile`（`StepDistance.Raw == 0`）で追撃に入れないようにした。今は速度0のユニットが存在しないので main では顕在化しないが、将来足すときは必ずこの修正を入れる
- `168cd52` 設計書の更新（数値の詰めを5週目以降に回す方針、速度0の兵の追撃の規則）
- テストは 350 件すべて成功していた

再開するかどうかはオーナーが決める。再開する場合も、先に #29 など段階4の本来の作業を進めてよい。

---

## 5. 次にやること（候補）

### 本命：#29(3) 判断猶予の境界探索の続き

`grace` コマンド（`Headless/Rts.Headless.Cli/GraceCommand.cs`、使い方は同ディレクトリの README）を使って、以下のどれかを進める。時間コストに注意：week2-2routes（40人）で約7〜10ms/tick、tick上限6000・R候補1件あたり即時＋遅延で約2分かかる。1tick刻みの全探索は非現実的なので、粗いステップ→二分探索→**非単調性を疑って周辺を再走査**、の順で進める（前回はこれで4020〜4300の成功の島を発見できた）。

- (a) 4000〜4020、4300〜4400の厳密な境界を詰める
- (b) 撤退・増援・陽動対応の3述語でも同様の非単調性があるか、別のシナリオ・命令で確認する
- (c) 現状の実測結果（3.節の内容）をIssue #29にコメントとして残し、(3)を一区切りにして #27 や他の作業に進む

前提タスク #27（3-7、共同）は未完了のまま。進めてよいかはオーナーに確認する

### その他

- **#28**：CLI 部分（差分診断の仕上げなど）は A だけで進められる。Mono／IL2CPP と2台照合は相方の協力が必要
- **#59**：オーナー方針により大半を5週目以降に延期。ただし「10人対10人が 36000 tick でも未決着」は膠着＝仕組みの問題の可能性もあるので、扱いをオーナーに確認する

---

## 6. 恒常的な運用ルール（前アカウントのメモリから転記）

前のアカウントのメモリは `C:\Users\きよ\.claude\projects\d----------------aistrategy-aistrategy\memory\` にあります（ただのテキストファイルなので、自動で読み込まれていなければ直接 Read してよい）。要点は以下です。

### 進め方

- **PR のマージは指示を待たずに行う。** 条件は CLAUDE.md の最低限（全体テストが通る、決定論テストを壊さない）。細かい懸念は保留の理由にせず、マージしてから直す。マージしたら報告に書く
- **積み重ねた PR** は ①下の PR をマージ（`--delete-branch` を付けない）→ ②上の PR を `gh pr edit <n> --base main` → ③マージ、の順。先にブランチを消すと上の PR が GitHub に閉じられる
- **Contracts を変えたら、相方への告知 Issue を必ず作る**（ラベル `B: 表示・UI`・`共同`、相方向けの平易な言葉で）
- **報告の末尾に、次の作業の推奨モデルを書く。** オーナーが `/model` で切り替える。Sonnet＝テスト・コミット・PR・報告・通常のレビュー、Opus＝設計判断・原因の切り分け・決定論や AI 配分など複雑なコードのレビュー
- **設計の芯を決めたら Astra にレビューを依頼する**（09-23 以降）：依頼文を scratchpad に書き、`codex exec -m gpt-6-astra -s read-only --skip-git-repo-check -o <結果ファイル> -` に stdin で渡す。結果は要約し、こちらの見解（同意・異論）を添えて伝える
- Codex のモデル（09-23 以降）：範囲の広い実装や長いテストを含む発注は `gpt-6-astra`、1〜2 ファイルの狭い修正は `gpt-5.6-terra`、疎通確認は `gpt-5.6-luna`。`-m` は必ず明示する（省略すると astra になる）。terra は範囲の広い発注を途中で止めやすい

### 人

- **オーナー**：Java は一通り、Python は開発経験あり、SQL はプロ級。プログラミングの基礎説明は不要。Unity は未経験なので、Unity 固有の概念は Java との対比で説明すると早い。「経験がないから品質を下げる」判断はしない（本人が明言）
- **相方**：プログラミング未経験、Claude Code のみ使用（Codex は使わない）。時間は多めに取れる。相方向けの文章は専門用語を言い換え、「画面でこう見える」「この操作でこうなる」で確認できる形で書く。シミュレーション層（決定論）は相方の担当にしない
- 見た目の方向性：素材パック優先。好みは Age of Empires IV → They Are Billions → Diplomacy is Not an Option → Bad North → Hearts of Iron IV の順

---

## 7. 作業環境の落とし穴（全部、実際に踏んだもの）

### git

- **push・commit・checkout の前に git-lfs を PATH に通す。** Bash ツールにも PowerShell ツールにも最初から入っていない。PowerShell で同じコマンド内に `$env:PATH = "C:\Program Files\Git LFS;" + $env:PATH` を先に書く。入っていないと pre-push フックで push が失敗する
- **`&&` でつながない。** post-checkout・post-commit・pre-push のフックが非ゼロで終わるため、`git checkout -b X && git add -A && git commit` は checkout の直後で止まり、**コミットされていないのに成功したように見える**。`;` か改行で区切り、最後に `git log --oneline -1` と `git status --short` で実際に入ったか確かめる
- **push の失敗は次のコマンドで別の顔をして現れる。** push が通らないまま `gh pr create` すると `No commits between main and <branch>` という無関係に見えるエラーになる
- commit は post-commit フックが警告を出すだけで成立する（警告文を見て失敗と誤認しない）
- `--no-verify` は使わない
- git stash は他のセッションと共有なので、素の `git stash` / `git stash pop` は使わない

### テストと長い実行

- テスト：リポジトリ直下で `dotnet test Headless/Rts.Headless.slnx --configuration Release`（15〜35 分）。一部だけなら `--filter "FullyQualifiedName~SentryTests"` のように絞る
- **長い実行の出力を `| tail` しない。** 終了コードが `tail` のものになって失敗が成功に見え、失敗したテスト名やメッセージも途中に出るので消える。`コマンド > "<scratchpad>/run.log" 2>&1; echo "EXITCODE=$?" >> "<scratchpad>/run.log"` の形にして、後から「失敗」「Failed」「error」で検索する。合計行だけ見て通ったと判断しない
- `dotnet test` には `--logger "trx;LogFileName=..."` と `--blame-hang-timeout 15m` を付けると後で拾いやすい
- **`TaskStop` はシェルを止めるだけで、`dotnet test` の testhost は生き残る。** DLL をロックし続け、次のビルドが `error MSB3027: ... "testhost (PID)" によってロックされています` で失敗する（テストではなくビルドの失敗）。止めた後は `Get-Process -Name testhost*,vstest* | Stop-Process -Force` で掃除する。**ただし殺す前に `StartTime` で持ち主を確かめる**（他の実行のものを殺すと、そちらが「テストホストがクラッシュした」と誤診断する）
- **同じ作業ツリーで2つのビルド・テストを並走させない。** ファイルロックや、走行中のツリー書き換えで結果が無意味になる
- **サブエージェントの「完了」通知は最終ではない。** 自分で起動した背景実行が終わると再び動き出してファイルを書き換える。本文に「まだ背景で実行中」とあれば一時停止にすぎない。そのエージェントの worktree でビルド・テストを始める前に静止を確かめ、止めるならエージェント自体を `TaskStop` する
- **テストは、そのコードにあるタイマーより長く回す。** 追撃60・配分20・劣勢撤退40・占領200・各種600 tick など。1 tick だけのテストは「初期状態では正しい」ことしか示さない（守衛の不具合はこれで素通りした）
- **全部のテストが通っても、実測で意図した効果が出ているか別に確かめる。** 決着 tick が基準値と完全に一致したら、効果ゼロを疑う
- Unity の EditMode テストは見届けのタスクが結果を返さず止まることがある。ログの `Test run completed` 行と `-testResults` の XML を直接読んで判定する。Unity 実行中に同じプロジェクトを編集しない

### Windows の環境

- シェルは PowerShell 5.1 が主、Bash（Git Bash）も使える。PowerShell では `&&`・`?:`・`??` は使えない。ネイティブ実行ファイルに `2>&1` を付けない
- Git Bash には `bc` が無い。計算は `awk` で行う
- 検証の出力先は **`D:\rts-verify\<Issue番号>`**。`replay` の `.hashes.states` は 1 試合で数 GB になり、以前 C ドライブが満杯になった。読み終わったら消す。現在 `D:\rts-verify\sentry`（1.7 GB、守衛の検証用）が残っている

---

## 8. コマンド早見表

### CLI（Headless）

```sh
# 記録（--inputs を省くとお任せ同士）
dotnet run --project Headless/Rts.Headless.Cli -c Release -- record --scenario TestData/week2-2routes.json --out D:/rts-verify/x/run.rtsreplay --ticks 20000 --west-preset none --east-preset maintain

# 再生（毎 tick のハッシュ照合、100 tick ごとのダンプ）
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- replay --in D:/rts-verify/x/run.rtsreplay --hash-out D:/rts-verify/x/run.hashes --dump-dir D:/rts-verify/x/dump

# 2つの再生の比較
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- compare --left a.hashes --right b.hashes --replay run.rtsreplay

# 指標の集計（記録から解析まで一括、JSON を出力）
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- analyze --scenario TestData/week2-2routes.json --ticks 20000 --west-preset none --east-preset maintain --out D:/rts-verify/x/maintain.json
```

- `--no-build` を使う前に `dotnet build Headless/Rts.Headless.Cli -c Release` を1回実行する（複数本を順に回すときはビルドを1回にしてロックを避ける）
- 終了コード：0 正常・一致、2 状態／ハッシュ／欠落 tick の不一致、3 形式・版・引数の不一致、4 Fault・I/O 障害
- 他に `bench`（性能計測、`docs/performance.md` 参照）、`--ai-delay 0|60|200|400` と `--ai-profile default|long`（擬似 AI 遅延、プリセットとは併用不可）がある
- プリセットは `none`・`maintain`（維持型）・`concentrate`（集中型）・`maintain-legacy`（改訂前の維持型）。陣営ごとに `--west-preset`・`--east-preset` で指定し、省略すると `none`
- シナリオ：`week1-10v10.json`（10 対 10、全マス通行可）、`week2-2routes.json`（40 人、2 ルート、基準）、`week3-20.json`・`week3-80.json`（20・80 人）、`week2-2routes-core6000.json`（コア HP 6000 の比較用）

### 基準値（main、`week2-2routes.json`、西=none）

| 東プリセット | 初回コア被弾 | 決着 | 結果 |
|---|---:|---:|---|
| none | 5229 | 5428 | 西コア破壊 |
| maintain | 8347 | 8683 | 東コア破壊 |
| concentrate | 3368 | 4193 | 西コア破壊 |
| maintain-legacy | 5229 | 5428 | 西コア破壊 |

### ダンプの読み方

- 1 行 1 項目の `名前=値`。座標や距離などの固定小数点は raw 値なので **65536 で割るとメートル**
- よく使う項目：`Cores[n].Hp`、`Soldiers[n].{FactionId,ArmyId,Kind,Alive,Hp,Position.X.Raw,Position.Z.Raw,TargetKind,TargetId,NextAttackTick,IsAttacking,IsRetreating}`、`Ai.Armies[n].Assignment`、`Ai.Soldiers[n].Pursuit.{Active,Returning}`、`Ai.Factions[n].Observation.Contacts[i].{Position.X.Raw,Position.Z.Raw,Visible}`
- **可視の敵（`VisibleEnemies`）は正規状態に含まれない**（状態から再構築されるため）。ダンプから読めるのは接触（Contacts）まで
- 100 tick ごとなので、**途中の変化を見落としやすい**。「集合が完了しない」「追撃に入らない」と結論する前に、全 tick の範囲を見る（前のセッションで2回、見る範囲が狭くて誤診断した）
- 抽出は `awk -F=` で行うと速い

---

## 9. 報告の書き方

- 日本語で書く。完了を報告するときは「確認した方法」と「確認できていないこと」を分ける（CLAUDE.md）
- 推測と実測を混ぜない。「〜のはず」で結論せず、ダンプや出力で確かめてから書く
- 自分の誤りは隠さず書く
- 末尾に「次の作業 → 推奨モデル：Sonnet／Opus」
