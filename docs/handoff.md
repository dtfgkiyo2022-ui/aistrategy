# オーナー側（A）作業の引き継ぎメモ

最終更新：2026-09-21。前のセッション（別アカウントの Claude Code）から、次のセッションへ渡すためのメモです。**作業を始める前に全部読んでください。**

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

## 3. 現状（2026-09-21 時点。以降の節に 09-20〜21 の追記あり）

### ブランチと PR

- `main` は PR #105 まで入っている（表示側の #11〜#31 の一式、実シミュレーションとの結線、段階3・4の検証基盤、試遊の修正、購入素材の表示 `LocalVisualPack`、判断猶予の追加述語、Jev の試験道具、再生ビューアの「照合ではない」表示）。最新は `git log --oneline -3 origin/main` で確認。**開いている PR は保留中の #62（守衛）だけ**
- GitHub Actions は 2026-09-19 に課金／利用上限で一時的に動かなかったが、リポジトリを**公開**にしたため（公開リポジトリは実行時間が無料）復旧した。再び上限に達したら、ジョブが2秒で失敗し注釈に「recent account payments have failed or your spending limit needs to be increased」と出る（`gh run view <id>` で確認）。そのときは手元で `dotnet test Headless/Rts.Headless.slnx --configuration Release` を実行して代替する
- **PR #62（draft、未マージ）**：コア前の守衛（Sentry）。ブランチ `a/59-sentry`。**保留中**（→「4. 保留した実験」、状況は変わらず）
- ローカルのブランチは `main`・`a/59-sentry` のみ。作業用 worktree は片付け済み

### 2026-09-19 に完了した作業（#29）

- **PR #65 マージ済み**：Issue #29(2) 判断猶予の測定コードを実装。`UnityProject/Assets/RTS/Application/GraceMeasurement.cs`（測定本体）＋ Headless CLI の `grace` コマンド（`Headless/Rts.Headless.Cli/GraceCommand.cs`）。使い方・成功述語4種（コア防衛／撤退／増援／陽動対応）の判定基準は `Headless/Rts.Headless.Cli/README.md` の「grace: 判断猶予の測定」節に日本語で書いてある。Codex 利用上限中のため Opus サブエージェント（`isolation: "worktree"`、背景実行）に実装を委譲し、完了後にオーナー側 Claude Code がビルド・全体テスト（355件合格）を自分で再確認してからマージした。**Astra レビュー未実施**（設計判断は PR #65 の本文と README に明記）。
- Issue #29(1)（維持型・集中型プリセット）は既存の `PolicyPresets.cs` で完了条件を満たしていることを確認済み（新規実装は不要だった）。
- **Issue #29(3)（実際に猶予の境界が動く例を探す）に着手し、中断中。** `week2-2routes.json`（西=none, 東=none。基準：西コアが5229 tickで初被弾・5428 tickで破壊）で、西陣営に `MaintainReserve All reserve=300‰` を受付tick Rで与え、コア防衛（tick 6000生存）を成功述語として実測した：
  - R=1〜3975：成功／R=3990〜4000：失敗／**R=4020〜4300：成功（島）**／R=4400以降：失敗
  - **成功R集合が本当に非単調だった**（3990〜4000で一度失敗した後、4020〜4300で成功が戻る）。二分探索だけで境界を決めると4300の成功区間を見逃す。詳細は前アカウントのメモリ `issue29-grace-non-monotonic-finding.md` を参照（`C:\Users\きよ\.claude\projects\d----------------aistrategy-aistrategy\memory\` 配下）
  - **未確認**：島の左右端の厳密な1tick単位の境界（4000〜4020、4300〜4400はまだ粗い）、他の3述語（撤退・増援・陽動対応）、他のシナリオ・命令の組み合わせ、この「島」が起きる原因（自動配分AIとの干渉という仮説はダンプで未検証）
  - **(b) 他の3述語（同シナリオ・西陣営・tick上限6000、500刻み走査）**：
    - 撤退（軍団1へ撤退命令）：R=1〜401失敗（10/50刻みで確認）、R=451・501・1001・1501成功、R=2001以降失敗。早すぎても遅すぎても失敗する「成功の窓」。+60tick遅延では左端が約50tick早い側に出た。左端の1tick境界・右端(1501〜2001)・早期失敗の原因は未確認
    - 増援（軍団1で拠点1優先）：R=1〜1501成功、R=2001以降は全て「未適用」（試合か判定が早く終わった疑い、原因未確認）。境界測定には使えなかった
    - 陽動対応（拠点1、防衛）：全R失敗＝猶予なし。ただしtick上限6000がコア破壊基準5428を超えるため測定条件の問題。命令・上限の再検討が必要
    - 生データは `D:/rts-verify/29b/`（リポジトリ外）
  - コア防衛と撤退の結果は Issue #29 にコメント済み（2026-09-19）。(3)は「一区切り」として扱ってよい状態。残りは(a)島の端の1tick境界、撤退の右端、増援の未適用原因、陽動対応の測り直し、(d) Issue #27（共同）への切り替え。オーナーはこの後の方針を未決定

### 段階の進み具合（ROADMAP.md）

段階0〜3 の A 担当（シミュレーション）の実装はおおむね完了し、段階4 に入っています。

**B 担当（表示・UI）**は相方が進める前提でしたが、相方のブランチは未pushだったため、2026-09-19 にオーナーの依頼でオーナー側 Claude Code が #11・#12・#13・#18・#19・#25・#26・#30・#31 を実装してマージした（相方の作業と重ならないことを確認済み）。
- **2種類のシーン**：`Scenes/MockBattlefield.unity`（仮データ）、`Scenes/LiveBattlefield.unity`（**本物の Simulation＋CommandGateway で試合が動く**。西＝自分、東＝`maintain`）、`Scenes/ReplayView.unity`（`.rtsreplay` を選んで再生）。3つとも `Editor/MockBattlefieldSceneBuilder.cs` の `Create*Scene` で作り直せる
- 場所：`Presentation/`（BattlefieldView・BattlefieldCamera・BattlefieldSelector・CommandPanel・TimelinePanel・MatchTimeline・GroundPointQuantizer・TerrainMap・SelectionTarget・PresentationMaterials・IMatchClock・ICommandDelayControl）、`UnityHost/`（LiveMatchHost・LiveCommandPort・ReplayViewHost・ScenarioTerrain・Mock*）、`Application/ReplayPlayer.cs`（画面で見る用の再生。照合は従来どおり `ReplayRunner.Replay`／CLI）、Blender の仮モデル `Models/Placeholder/*.fbx`（`Tools/blender/make_placeholders.py`）
- **検証**：`VerifyFogLeak`（Unity バッチ）が両陣営の視点で本物の試合を3000tick進め、霧の情報漏れを数える。結果は PASS。実行すると `D:/rts-verify/31/report.txt`。**この検証で、補間中に敵が霧に描かれる漏れを1件見つけて直した**（Issue #31 にコメント済み）。表示や霧を触ったら必ず再実行する
- 見た目の確認は Unity をバッチ実行して撮影画像で行った（手順は `memory/unity-blender-batch-tools.md`）。**未確認**：実際に再生ボタンで動かした操作感、IMGUI（ボタン・状態一覧）の見た目、遅延3/10/20秒を選んだときの実挙動（翻訳スタブ経路）、長い記録の再生の重さ、試合の決着まで進めたときの表示
- **設計判断（Astra 未レビュー）**：①遅延0は直接の定型命令、3秒以上は `SubmitInterpreted`（人間の翻訳スタブが操作側の命令をそのまま返す）。遅延の切り替えは試合の作り直し ②照合と表示の分離（`ReplayPlayer` を新設し `ReplayRunner` は変更なし）③敵は今見えないマスへ補間しない
- 表示側の残り：#27・#32（相方との共同）。#30 の「再生ファイルの選択」まで完了した
- 相方へ：進捗をまだ伝えていない（**2026-09-21 オーナー判断：相方はしばらくPCを触らなさそうなので、連絡は保留**。下書きは `D:/rts-verify/partner_message.md`。相方が動き出す前に伝える）。相方が着手する前に「表示側は #11〜#31 まで一通りマージ済み。相方の作業は、実際に画面で触って気になる点を Issue に書くこと」と伝えるのを忘れないこと
- Unity 公式プラグイン（skills 29個）はカードを出したが、有効になったかは未確認。Blender 5.2.2 は `D:/Program Files (x86)/Blender/` にある

### 開いている Issue（A 担当と共同のもの）

| Issue | 内容 | 状態 |
|---|---|---|
| #29 | 4-2 敵の方針2種類と判断猶予の測定 | (1)(2)完了。(3)は実測をIssueにコメント済み（コア防衛の島・撤退の帯）。残りは「5. 次にやること」 |
| #28 | 4-1 2台のPC・Mono/IL2CPP・CLI での照合 | **CLI・Mono・IL2CPP の3環境で全tick一致（20001tick）＋差分診断のフェーズ表示は完了（テストも PR #105 で追加済み）**。残りは「2台目のPC」だけ。渡すフォルダは `D:/rts-verify/28/for-partner.zip`（相方は run.bat を押して `result` を返すだけ。返ってきた `result/*.hashes.txt` を `D:/rts-verify/28/long_mono.hashes.txt`・`mono.hashes.txt` と直接比較する） |
| #59 | お任せ AI のバランスの続き | **大半を5週目以降に延期**（→ 下の方針） |
| #27 | 3-7 段階3の統合と確認（共同） | 自動で確かめられる2項目（見えない敵を追わない／全遅延で試合が続き新しい指示が守られる）は完了・コメント済み。残りは**試遊の感想**（相方） |
| #32 | 4-5 介入方法の比較と試遊評価（共同） | 機械的な24通り＋専用設定4通りを実測しコメント済み（PR #81 の `intervene`）。残りは「説明できるか」の人の評価と試遊 |
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

### 2026-09-20 に進めたこと（このPC単独で進められる分）

- **#28**：CLI(.NET)・Unity Editor(Mono)・Windows Player(IL2CPP) で同じ記録を再生し、**20001tickすべて一致**（人の命令つき）。`compare` が不一致tickの「最初に異なるフェーズ」を出すようにした（`Simulation.PhaseHashObserver`、通常は何も計算しない）。手順は `memory/cross-runtime-replay-check.md`。IL2CPP ビルドは Unity が URP の生成物と ProjectSettings を書き換えるので `git checkout` で戻す
- **#27**：`FogDelayIntegrationTests`（狙った敵は必ず見えていた／全遅延で試合が続き新しい命令が守られる／同じ介入は入力ログが同一／締切500の専用設定）
- **#32**：`intervene` コマンド（お任せ／開始時だけ／途中で変更 × 敵の方針 × 遅延、`--ai-profile long` で締切500）で24通り＋4通りを20000tick実測。**結果の表と読み取りは Issue #32 のコメント**。要点：集中型に完全お任せは負ける／開始時の1回の命令で「負けない」になるが勝てない／途中で防衛を厚くすると**膠着**（未決着）になる／遅延400は既定の締切だと介入不能
- **#29(3)**：撤退の窓を1tick単位で全部測った（適用tick 1801〜2060の260点）。**結果は適用tickだけで決まる**。1〜430失敗／431〜1800成功／1801〜2060は成功率がだんだん落ちる帯（最終成功tickは2059で、測る範囲を広げるたびに動いた）。詳細は Issue #29 のコメント2本。増援述語はこのシナリオではR≥1800が全部「未適用」で境目が動かない、陽動対応は測定条件が悪い（再測定していない）

### 2026-09-20 の試遊（オーナーが実際に画面で遊んだ）

**Unity の起動方法**：Unity Hub にプロジェクトが登録されていないので、エディタを直接起動する。
`D:\Unity\Editor\6000.3.24f1\Editor\Unity.exe -projectPath <UnityProject> -executeMethod Rts.Editor.MockBattlefieldSceneBuilder.OpenLive`（`PlayLive` なら再生まで自動）。エディタのメニュー「RTS」からも開ける。

- **想定どおり動いた**：戦場・霧・軍団・拠点の表示、カメラ（WASD＋ホイール）、軍団のクリック選択、定型命令、命令の7状態（`Interpreting`→`Pending`→`Executing`、上書きは `Cancelled (Superseded)`）、進路の矢印、増援、時系列。遅延3秒・10秒は通り、**遅延20秒は `Expired (Deadline)` で命令が通らない**（締切12秒。設計書11章どおり）
- **試遊で見つけて直した表示の問題（PR #85）**：選択中の対象がボタンから離れた左上隅で気づけない／カメラ操作が分からない／時系列が戦場の中央を覆う／敵ラベルが重なって読めない／解釈中が「applies in 0.0s」でだまされる／やり直しで時系列に前の試合が残る。詳細は Issue #27 のコメント
- **オーナーの判断待ち**：遅延20秒を「指揮不能」として見せるか、締切を500tick（25秒）に延ばして通るようにするか（#32 のコメントに実測あり）
- **素材の見た目（2026-09-21 オーナー確認）**：購入素材（Toony Tiny RTS Set）の表示は、今のところ問題なし（OK）。旗や兵士の大きさの調整は、気になる点が出てから
- **まだ見ていない**：敵の残像・推定兵力をじっくり見ての評価、陣営視点の切り替え、決着までの長時間試遊、集中型（`concentrate`）相手

### 2026-09-20 の続き：膠着の診断と Jev の試験

- **膠着の原因（#32）**：`analyze --trace-out x.csv --trace-every N`（PR #86）で時系列を書ける。`intervene --style change`（予備50%＋コア防衛）は、tick 800 以降に西の3軍団がすべて `Reserve` になり、攻勢に加われる `Advance` の軍団が0のまま20000tick続く。攻勢の資格は `Advance` の歩兵軍団だけ（`OffenseDecision.Eligible`）。設計書どおりの挙動で、不具合とは見ていない
- **予備の割合の測定**：`intervene --change-reserve N`（‰）。東=maintain：0‰は膠着、100〜300‰は9817tickで西勝ち（同一結果）、400‰も西勝ち、500‰（既定）は膠着。東=concentrate は 200・300・400‰の3試合が `D:/rts-verify/32/rsweep/` で実行中だった（結果は #32 に書く）。0‰で膠着する理由は未確認
- **Jev の試験（Issue #87）**：ロリポップ！AIゲートウェイは 2026-09-18 から Jev に対応（`POST https://ai-gateway.lolipop.jp/v1/systemone`、モデル `typesafe/jev-latest`。ホストは検証済み）。Jev は文章でなく設問（choice／noul／score）に答える。`snapshot` コマンドで実戦況を書き出し、`Tools/provider-probe/probe.py`（応答時間・失敗率・答えの妥当性）と `scenarios.py`（正解のある戦況）で測る。結果：応答 p50 約1秒・p95 約4秒（70〜500ms には届かない）、同時4件で悪化なし、確信度は戦況に追従、選択式は正解のある3戦況で15/15、noul は設問の書き方で差が大きく変わる。詳細は #87 のコメント
- **キー**：ロリポップのAPIキーはオーナーのWindowsユーザー環境変数 `PROBE_KEY`（有効期限 2026/10/20）。スクリプトは環境変数から読み、記録には書かない。不要になれば管理画面で削除する
- **#29(3) の続き（判断猶予）**：成功述語を2つ追加した（既存の4つは不変）。`outpost-held`（試合の最後に指定拠点が自陣営の所有。PR #90）と `core-and-outpost-held`（コアが無事で、かつ拠点が自陣営の所有。2つ目の命令 `--order2-*` を同じtickに出せる。PR #91）。理由：増援の述語は味方の歩兵が着いた時点（tick 1760〜1800）で命令なしでも成功が決まり、命令が間に合ったかを測れない。陽動対応の述語は、コアを守れる命令ではコア防衛と同じ結果に縮まる。測定結果は Issue #29 のコメント：コア防衛の島は左端が適用4000|4001で確定・右端は成功と失敗が入れ替わる、拠点保持は適用約1340付近まで成功（1311・1321だけ失敗を挟む）、両方保持は成功が数点だけ。**成功・失敗は適用tickだけで決まる**（遅延は無関係）。**陽動が起きる新シナリオは5週目以降**（東の一部が拠点、主力がコアへ向かう配置の設計が要る）
- **購入素材の扱い（2026-09-20 決定）**：Asset Store の素材（第一候補 Toony Tiny RTS Set $35、URP対応・Extension Asset）は、**オーナーが1つだけ購入し、`UnityProject/Assets/ThirdParty/` に置く。このフォルダは `.gitignore` 済みでリポジトリに入れない**（Extension は「元ファイルに触れる人ごとに1席」のため、相方に元ファイルを渡さない）。Asset Store から取り込むと素材は `Assets/<素材名>/` に入るので、Unity のエディタ内で `Assets/ThirdParty/` へ移す（`.meta` の GUID が保たれる）。**素材を参照するシーン・Prefab もコミットしない**（相方のプロジェクトで参照切れになる）。素材を使った見た目は、オーナーのPCの中で確認するか、素材を参照しない仮素材のままコミットする。「元ファイルに触れなければ席は不要」は規約に明記がなく、確実にするなら Unity サポートへ問い合わせる。購入前に無料デモ版（Toony Tiny RTS Demo）で見た目を確認する
- **素材の表示の仕組み（PR #94・#95）**：`Presentation/LocalVisualPack.cs`。購入素材（`Assets/ThirdParty/ToonyTinyPeople/...`）があるPCだけ、兵士（`TT_Light_Infantry`／`TT_Scout`）・コア（`Castle.FBX`）・拠点（`Tower_A.FBX`）を素材で表示し、色違いマテリアル（西=青、東=赤、中立の拠点=白）を当てる。素材がなければ従来の仮モデル（フォールバック確認済み）。素材はパス（文字列）で参照するだけで、Prefab・シーンに参照を持たせない。エディタ専用（`AssetDatabase`）で、プレイヤービルドは常に仮モデル。**撮影のコツ**：バッチの最初の描画はシェーダー準備中でピンクになるので、`MockBattlefieldSceneBuilder.Render` は空撃ちを1回入れてから撮る。Unity を素材フォルダの取り込み中に開くと、`Assets` の中身を誤って `ThirdParty` に入れやすいので注意（素材フォルダ `ToonyTinyPeople` だけを移す）
- **計算の重さの調査と直し（PR #97・#98、2026-09-21）**：試合の途中で接敵すると、エディタ上でカクついた。原因は素材ではなく、シミュレーションが **1tick あたり約26MB** のメモリを確保していたこと（経路探索14.5MB・AI 8.8MB）。エディタの Mono は GC が重く、1tick が序盤約31ms→試合の途中約90ms（予算50ms）になっていた。直したのは3点：①`GridMap.FindPath` の結果を覚える（純粋関数。1tick約123回、同じ問い合わせの繰り返し）②`GridMap` の距離・補間を `BigInteger`→`long`（地図の一辺は 2^30 raw＝16384m まで）③AI（Decision）の距離・経路長・近さの判定から `BigInteger` を外す。**`double`・`decimal` は決定論のテスト（`PureSourcesContainNoForbiddenApis`）が禁止しているので使えない**ため、自作の128ビット整数 `Decision/WideMath.cs`（`Wide`：long2つ分の掛け算・足し算・比較、桁ごとの整数平方根）を作った。収まらない値は誤った値でなくエラー。元の BigInteger 実装との一致は `DecisionArithmeticTests`（乱数＋境界＋端の値）で固定。結果：確保量 26.4MB→3.5MB/tick（残りは記録用の状態ハッシュ）、エディタの1tick 約2〜3ms、決定論は記録済み2試合の全tick一致で確認。**測る道具**：`bench --alloc-types`（型ごとの確保量）、`bench` の `AllocKBPerTick`（処理ごと）、Unity バッチの `PerfProbe`（`-perfNoPack` `-perfTicks N`。エディタでの1tick・GC回数）。**教訓**：処理時間の平均でなく、確保量とGCの回数を見ると原因が早く分かった。`Random.NextInt64` などは Unity 側の .NET にないので、テストでは使わない
- **背景実行の落とし穴**：セッションが切れると Bash の背景プロセスも止まる。長い試合は PowerShell の `Start-Process`（独立プロセス）で動かす

### 2026-09-21 の続き：判断猶予の読み方と診断のテスト

- **判断猶予に「成功の密度」を併記（PR #103）**：#29 の実測で成功Rの集合が非単調だと分かったため、`grace` の出力に最終成功tickだけでなく、①成功が連続する区間（`SuccessRuns`・最長区間）②決着した候補に対する成功率（`SuccessPermille`、‰）③適用tickを等分した帯ごとの成功率（`Bands`）を足した。**帯の割り当ては、印字した境界を走査して決める**。整数除算だと「この帯はどのtickから」と「このtickはどの帯か」が食い違い、端のtickが隣の帯に入って 0% と誤表示される（実際に tick 4380 の最終成功が「4360-4379」の帯に数えられていた）。テスト `EveryCandidateIsCountedInTheBandThatPrintsItsTick` で固定した
- **帯の指標を作り直した（PR #104）**：最初に入れた「成功率90%を下回る最初の帯」は、**6つの実測すべてで最初の帯を指して役に立たなかった**。撤退のような「窓型」の述語は早い適用tickで失敗するため、最初の帯が常に90%未満になる。直した形は「頼れる区間の**始まり**（`FirstBandAtLeast900Tick`）と**終わり**（`FirstBandBelow900Tick`／`FirstBandBelow500Tick`、始まりが見つかった後だけ探す）」。この欠陥は自分で見つけて Issue #29 に報告した
- **`compare` の差分診断にテストを付けた（PR #105、#28）**：「最初に異なるフェーズ」を出す `Program.FirstDifferentPhase` はテストが1つもなく、`Value[..16]` が **16文字未満や null のハッシュで例外を投げた**。決定論が壊れたときに動く診断が、その異常なデータで落ちるのは本末転倒。`Short()` で防御し、`Headless/Rts.Core.Tests/PhaseDiffTests.cs` に6件（値の相違・フェーズ名の食い違い・短い/null のハッシュ・片方のtickが早く終わる・全一致・再実行できない側）を足した
- **判断猶予の「島」の正体が分かった（#29、コメント済み）**：コア防衛の島の右端を **1 tick 刻みで全数（4301〜4500 の200点）** 測ったところ、成功・失敗は **`(20k, 20k+20]` のブロック単位でしか変わらなかった**（200点すべてブロック内で均一）。原因は `Simulation/Simulation.Decision.cs:96`：軍団の配分 `PolicyDecision.Allocate` は **`world.Tick % 20 == 0` のときしか走らない**（他の tick は前回の評価を使い回す）。受付tickが 4321 でも 4340 でも、効くのは同じ tick 4340。これまで「端は成功と失敗が入れ替わる」「最終成功tickが測る範囲で動く」と書いていたのは、ばらつきではなく**配分周期の櫛**だった。続けて 4420〜5420 を20 tick刻みで全ブロック確認し、**右端は 4420 で確定**（4440 以降に飛び地なし）。`Delayed`(R) は `Immediate`(R+60) と140点全数一致（＝遅延そのものは結果を変えない）。**今後は `--r-step 20` で測る。1 tick 刻みは20倍の時間を使って同じ答えしか出ない。** ただし刻みを20の倍数にすると全部同じ位相を踏むので、位相をずらした2点（`20k` と `20k+1`）の確認を1回は入れる
- **失敗ブロックの中身を見た（#29、コメント済み）**：島の中の失敗ブロックに**特別な仕組みはなかった**。命令は失敗側でも普通に効き（配分tickで軍団2が `Advance`→`Reserve`、攻勢が `Gathering`→`Idle`）、成功側は同じ変化が20 tick 遅れて起こるだけ。時系列が最初に食い違う tick は配分tickそのもの（4360／4400）。決着を分けるのは約900 tick 後で、両者は tick 5060〜5220 をほぼ同一に推移し、**西が約27人を失い東の損失は0〜2人**という一方的な戦闘のあと、失敗側は2〜3人まで削られてコアを叩かれ（5342／5347）、成功側は13〜20人で損失が止まってコアに届かせない。**tick 5300 時点の差は4〜11人**。つまり**猶予 4420 は「反応の締切」ではなく「紙一重の戦闘に負ける側へ転ぶ点」**で、頑健な閾値として読んではいけない。2組（4341/4361、4381/4401）で同じ形を確認。**ここから出た大きな疑問：同数（40対40）でなぜ西が27人失って東は0〜2人なのか。** 猶予の測り方より直すべきはこちらの可能性が高く、#32 の0‰膠着・#59 と同じ根かもしれない
- **診断の道具（再利用できる）**：`grace` は `sim.Step` を直接叩くので記録が残らず時系列が見られない。`GraceMeasurement.ComposeInputs` と同じ Reserve/Resolve の組を JSON で組み立てて `record --inputs` に渡せば、同じ試合を記録として再現でき、`analyze --trace-out` や `replay --dump-dir` がそのまま使える。**必ず `grace` の判定（成功/失敗と決着tick）と突き合わせて再現できていることを先に確かめる**。生成スクリプトの形は `D:/rts-verify/29/blocks/` の inputs\_r\*.json を参照（revision は先行命令がなければ Reserve=0・Resolve=1、`logIndex` は `R*100+1`・`R*100+2`、`acceptedTick`・`observedTick` は R-1）
- **#32 の予備割合スイープの結果を書いた（コメント済み）**：`intervene --style change --change-reserve N`、10試合。東=maintain では基準（命令なし）がもともと 8683 tick で西の勝ちで、介入は 100〜400‰ なら勝ちを **1100 tick ほど遅らせるだけ**、0‰ と 500‰ は**勝ちを未決着に変える**。東=concentrate は 0〜500‰ のどれでも未決着（基準は 4193 tick で西の負けなので「負けは回避するが勝てない」）。**膠着には少なくとも2つの形がある**：500‰ は「西の軍団が全部 `Reserve`」、**0‰ は西の軍団が `Advance` にいるのに両軍40人ずつ健在・両コア無傷でほとんど戦闘が起きない**。前者だけを原因とみていた前回の見立ての修正。100・200・300‰ は初回被弾も決着tickも完全に同一で、予備の割合はこの範囲でほとんど効いていない。未確認：0‰ で戦闘が起きない理由、割合が実際に何を動かしているか、0〜100‰ の間
- **Astra への依頼文を整理した**：`D:/rts-verify/astra/request_0920.md` を優先度 A〜D に組み直した。A＝決定論に直結（`WideMath`／`BigInteger` 除去、`GridMap` の経路キャッシュ）、B＝測定の妥当性（判断猶予の述語と密度、`PhaseHashObserver`、`ReplayPlayer`）、C＝新しい設計（Jev の組み込み `jev_design_sketch.md`、膠着の仮説、介入の代役）、D＝決着済み。**Codex 停止中は実装者と設計レビュー者が同一人物だったことを依頼文に明記した**。新たに気づいた2点も足した：①`PolicyDecision.Distance`（`BigInteger` 版）は**本番の呼び出し元が残っておらず、参照はテスト2本だけ** ②`GridMap` の経路キャッシュは `PathCacheLimit = 4096` の FIFO（`Queue`）で、決定論に影響しないことの確認が要る

### 次にやること（候補）

- **相方向けの連絡（保留：オーナー判断 2026-09-21）**：①表示側は #11〜#31 まで一通りマージ済み、画面で触って気になる点を Issue に書いてほしい ②#28 の2台目：`D:/rts-verify/28/for-partner.zip`（46MB）を渡し、フォルダ内の「はじめにお読みください.txt」の手順（run.bat を押す）を実行してもらう
- **#29(3) の残り**：増援・陽動対応の測定用シナリオ（拠点を争う／主攻側拠点を攻める）を作る。**コア防衛の島の端は左 4000|4001・右 4420 で確定済み**、成功率の帯の併記も PR #103・#104 で完了（→ 上の 09-21 の節）。残っているのは「なぜ 4341〜4360 と 4381〜4400 のブロックだけ失敗するのか」の中身（ダンプ未確認）だけ
- **#32 の次**：防衛を厚くする命令が膠着に直結する。決着に向かう介入（拠点確保の優先、進撃を早める）の代役も比べる。#59（お任せAIのバランス・未決着の扱い）と同じ根の可能性。オーナーが5週目以降と決めているので扱いを相談する
- **Astra 未レビューの設計判断（09-23 11:38 以降にレビュー）**：遅延の扱い（0は直接の定型命令、3秒以上は翻訳スタブ）、照合と表示の分離（`ReplayPlayer`）、`PhaseHashObserver`、`intervene` の介入3種類（人の判断を評価するものではなく機械的な代役）

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
