# SoldierRegen（regen1）報告

実施日: 2026-11-19（Release 構成、`UseSharedCompilation=false`、`nodeReuse=false`、`NuGetAudit=false`）。Git コマンドは実行していない。

## 実装

変更した主なファイル:

- `UnityProject/Assets/RTS/Simulation/ScenarioDefinition.cs`
- `UnityProject/Assets/RTS/Simulation/LiveGameRules.cs`
- `UnityProject/Assets/RTS/Simulation/WorldState.cs`
- `UnityProject/Assets/RTS/Simulation/ScenarioBinary.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Ages.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.SoldierRegen.cs`（新規）
- `UnityProject/Assets/RTS/Simulation/Simulation.Diagnostic.cs`
- `Headless/Rts.Headless.Cli/Program.cs`
- `Headless/Rts.Headless.Cli/TacticMatchCommand.cs`
- `Headless/Rts.Core.Tests/SoldierRegenTests.cs`（新規）
- `Headless/Rts.Core.Tests/Rts.Core.Tests.csproj`

処理の要所:

```csharp
public bool SoldierRegen;
public int SoldierRegenDelayTicks = 200, SoldierRegenIntervalTicks = 20, SoldierRegenPermille = 20;
```

`SoldierState` に `LastDamagedTick` と `LastAttackTick` を追加し、旗オン時だけ攻撃・被ダメージを記録した。中央 tick 処理を兵番号順に走査し、両方の最終時刻から200 tick以上経過した生存兵へ、`max(1, ceil(maxHp*20/1000))` を20 tickごとに加算して上限を `Parameters.Hp` とした。村人、死亡兵、旗オフでは処理しない。

`ScenarioBinary` 拡張欄132に旗と3定数を保存し、`LiveGameRules` の大規模マップでオンにした。CLIには `--soldier-regen-off` を追加した。診断欄も旗オン時だけ書き出す。

## テスト

- `SoldierRegenTests`: 2/2 合格
- `NeutralsTests`: 28/28 合格
- `ArmsScaleTests`（78章相当）: 7/7 合格
- `SkillsTests`: 21/21 合格
- Release CLI build: 成功、警告0・エラー0

## 旗オフ照合

指定された main CLI（`D:\rts-verify\metal2_cli`）で記録し、変更後CLIで `--allow-build-mismatch --no-states` 再生した。

- seed 1: `Replay matched S0..S13567`
- seed 2: `Replay matched S0..S17700`

## 測定

`--map-size large --terrain --ticks 40000 --timing-out`、種1〜3、旗オン/オフ。p99 は timing CSV の `stepMs` の最近順位99パーセンタイル。兵最大と中立撃破数は各ログの最終集計。

| 種 | 旗 | 決着 tick | 勝者 | 兵の最大（西/東） | 中立撃破数（西/東） | 1 tick p99 (ms) |
|---:|:---:|---:|:---:|:---:|:---:|---:|
| 1 | オフ | 13567 | 西 | 230 / 129 | 5 / 6 | 24.6900 |
| 1 | オン | 14072 | 西 | 213 / 114 | 10 / 10 | 37.0110 |
| 2 | オフ | 17700 | 東 | 20 / 141 | 4 / 4 | 29.7880 |
| 2 | オン | 14741 | 東 | 141 / 53 | 8 / 8 | 32.6910 |
| 3 | オフ | 16182 | 西 | 115 / 20 | 4 / 4 | 27.4070 |
| 3 | オン | 15465 | 西 | 68 / 20 | 11 / 9 | 38.1650 |

## 確認できていないこと

- 全体テストは依頼どおり実行していない。
- 表示側のUnity実機表示は変更しておらず、実機UIでの確認はしていない。
- 最大体力は既存の `SoldierState.Parameters.Hp` を現在の最大体力として使用した。将来これと別に装備・強化後最大体力を保持する仕様が追加される場合は、その値への切替が必要。
