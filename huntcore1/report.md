# huntcore1 検証報告

## 実装概要

- `AiHuntUntilCore` と拡張 ID 127 を追加。Live の large map では既定オン、`--ai-hunt-until-core-off` で明示オフ。
- `AiHuntGiveUpTicks=12000`、`AiHuntThreatMeters=30`、`AiHuntThreatSoldiers=5` を `EconomyRules` に追加。
- 敵コア視認 tick のラッチ、タイムアウト後の `AiHuntGaveUp`、中立キャンプの予約・戦力・年齢条件を WorldState/Simulation に追加。
- 視認前の自動攻撃・PushWave・LatePush の敵コア行き先を抑止し、中立狩り、再出現待ち、自己コア待機へ切り替え。
- 視認前後の被攻撃時挙動、敵コアと脅威建物への距離比較、既存の Reserve/CoreDefense/Scout/手動指示の優先を実装。
- 診断出力に視認 tick、give-up、視認前の中立撃破数を追加。Contracts は変更していない。
- 既存の中立テストは、従来挙動を検証するケースだけ明示的に機能オフへ固定し、新規 `AiHuntUntilCoreTests` を追加。

## ビルドとテスト

使用したビルドオプション:

```text
-c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
```

- `Rts.Headless.Cli` ビルド: 成功、警告 0、エラー 0。
- 指定フィルタ済みテスト: **217 passed / 0 failed / 0 skipped**、34分24秒。

実行フィルタ:

```text
FullyQualifiedName~AiHuntUntilCore|FullyQualifiedName~Neutral|FullyQualifiedName~Augment|FullyQualifiedName~Great|FullyQualifiedName~PushWave|FullyQualifiedName~LatePush|FullyQualifiedName~SideBias|FullyQualifiedName~SideFair|FullyQualifiedName~Explore|FullyQualifiedName~ScenarioBinary|FullyQualifiedName~ContractsTests|FullyQualifiedName~Determinism
```

## flag-off リプレイ

```text
replay --in D:\rts-verify\sidebias\base9000.rpl --hash-out <temp> --no-states --allow-build-mismatch
```

結果: **Replay matched S0..S9000**。

## 10 seed 計測

共通条件は `--map-size large --terrain --side-fair --west-tactic idle --east-tactic idle --ticks 40000`。オンは既定値、オフは `--ai-hunt-until-core-off` を付与した。各実行は逐次実行し、exit 0 かつ `core-destroyed` で自然終了した。40,000 tick は上限であり、全件が上限到達前に勝敗終了した。

| seed | on: ticks / winner | off: ticks / winner |
|---:|---:|---:|
| 1 | 13,510 / 1 | 16,198 / 2 |
| 2 | 14,372 / 2 | 22,200 / 1 |
| 3 | 15,140 / 2 | 12,903 / 2 |
| 4 | 35,700 / 2 | 15,982 / 2 |
| 5 | 13,800 / 1 | 23,400 / 2 |
| 6 | 20,700 / 2 | 16,500 / 2 |
| 7 | 12,823 / 2 | 13,352 / 2 |
| 8 | 18,953 / 1 | 12,300 / 2 |
| 9 | 13,357 / 1 | 13,289 / 1 |
| 10 | 16,520 / 2 | 15,044 / 2 |
| 平均 | **17,487.5** | **16,116.8** |

オン時の出力では、各陣営について `seenTick` または `gaveUp=1` が記録された。オフ時は全件で `seenTick=0 / gaveUp=0` となった。これは10 seedの小標本であり、勝率や tick 数の改善を意味する統計的結論ではない。

計測中に、CLI の新フラグが許可リストにはあるものの真偽値パーサーに未登録だった不具合を発見し、`Program.cs` で真偽値オプションとして修正後、全計測を再実行した。

## 出力管理

リプレイの hash と計測の大出力は一時領域へ出力し、リポジトリには追加していない。リポジトリ内に追加した検証成果物は本報告書のみ。
