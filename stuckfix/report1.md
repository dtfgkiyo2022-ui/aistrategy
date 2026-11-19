# stuckfix report1

実施日: 2026-11-19（UTF-8 の request_stuckfix1.md を全読了して実施）

## 実装

- `EconomyRules.UnitStuckFix` を追加し、wide map ではオン、拡張欄 129 に round-trip 対応。
- `Simulation.Navigation` に ArmyFlow 参加の進捗監視・空経路回復・決定的な通行可能セル fallback・後加入の人間指揮兵の合流を追加。
- `Simulation` / `Simulation.Raids` で、旗オン時のみ生存・可視の建物目標と接近セルを保持。
- CLI に `--unit-stuck-fix`（record）と `--unit-stuck-fix-off`（tactic-match）を追加。
- 追加テスト: `Headless/Rts.Core.Tests/UnitStuckFixTests.cs`（4 件）。
- 技術設計: `docs/technical-design-v3.md` に第80章を追記。既存章は書き換えていない。

実装上の原因を表す変更点（抜粋）:

```csharp
// Navigation: 旗オン時だけ、空経路または無進捗を回復対象にする
if (UnitStuckFixOn)
{
    bool joinStalled = LocalPath == null || LocalPath.Length == 0
        || !movedSinceJoinSample && tick - JoinProgressSinceTick >= waitTicks;
    if (joinStalled) { RecoverArmyFlowJoin(ref soldier, a, pathCenter, splitScout); continue; }
}
```

```csharp
// Simulation: 旗オン時だけ、まだ有効な建物目標を再選択から保護する
uint retainedBuildingTarget = UnitStuckFixOn && s.TargetKind == TargetBuilding ? s.TargetId : 0;
...
if (retainedBuildingTarget != 0 && IsStableBuildingTarget(s.Initial.FactionId, retainedBuildingTarget))
{
    s.TargetKind = TargetBuilding;
    s.TargetId = retainedBuildingTarget;
    s.MoveGoal = retainedBuildingMoveGoal;
}
```

旗オフでは新フィールドの診断書き込みを行わず、拡張欄 129 も encode から除去する。既存の判断順・攻撃・価格・Contracts・試合長は変更していない。

## ビルドとテスト

Release build（`UseSharedCompilation=false`, `nodeReuse=false`, `NuGetAudit=false`）は警告 0、エラー 0。テストは全て個別 allowlist で実行した。

| グループ | 合格 |
|---|---:|
| UnitStuckFixTests | 4 |
| ArmyFlowTests | 4 |
| HumanOrderFlowTests | 6 |
| FortifyTests | 20 |
| RaidVillagersFirstTests | 9 |
| NeutralsTests | 28 |
| SkillsTests | 21 |
| 合計 | 92 |

UnitStuckFixTests は拡張欄 129 round-trip、空経路回復、後加入兵の合流、旗オフ tick hash を含む。

## 指定 replay 比較

```text
Replay matched S0..S12855
Replay matched S0..S14622
```

1 本目は baseline CLI で作った `stuckfix/a.rpl` を変更版 CLI で `--no-states --allow-build-mismatch` replay。2 本目は指定された `D:\rts-verify\play1119\replay.rpl` を変更版 CLI で同条件 replay。いずれも mismatch なし。

## pack on

`pack/scenario.bin` と `pack/inputs.json` を `record --unit-stuck-fix` で記録し、`Recorded S0..S14622`。最終 tick の診断では `HasEnded=0`, `WinnerFactionId=0` で、14622 tick 時点では勝敗未確定だった。replay も `Replay matched S0..S14622`。

## 大型 terrain 測定

コマンドは seed 1–3、`--terrain --map-size large --ticks 40000 --timing-out`。全 run はコア破壊で 40000 tick より前に終了した。p99 は nearest-rank 99%、max は tick stepMs、units は totalUnits の最大値。

| seed | flag | decision tick | winner | max units | p99 ms | max ms | 600+ scout 停止 tick（west/east） |
|---:|---|---:|---:|---:|---:|---:|---|
| 1 | on | 12828 | 1 | 868 | 23.168 | 296.967 | 210 / 195 |
| 1 | off | 12855 | 1 | 795 | 21.492 | 290.341 | 4520 / 0 |
| 2 | on | 12197 | 1 | 584 | 139.854 | 293.863 | 2479 / 0 |
| 2 | off | 13889 | 1 | 733 | 21.210 | 290.402 | 5057 / 0 |
| 3 | on | 22395 | 1 | 310 | 23.507 | 290.911 | 13627 / 37546 |
| 3 | off | 14653 | 1 | 797 | 22.355 | 290.735 | 2001 / 1640 |

CSV は `stuckfix/seed{1,2,3}-{on,off}.csv`、ログは同名 `.log`。

## 未検証・制限

- CLI の timing CSV は `movedSoldiers` の総数しか出さないため、要求された「兵士 200+ stationary intervals」「斥候 600+ stationary intervals」の個体別 interval 数は算出できなかった。表の scout 値は既存 `ScoutOnly*StoppedTicks` の停止 tick 合計であり、個体別 interval 数ではない。
- 11-19 s17 の wall-hit/core-range を tick 13300 まで確認する probe7 実行は、指定 probe が大きな replay 読み込み中に出力せず、完了確認できなかった。
- seed3/on の CSV では `movedSoldiers == 0` の tick 行が 2842 件あったが、これは個体別 stationary interval 数ではないため合否判定には使っていない。
- 実行中に生成した rpl と dump は読み取り・検証後に削除する。
