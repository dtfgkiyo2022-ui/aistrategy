# stuckfix2 報告

実施日: 2026-11-19

## 結論

`UnitStuckFix` 旗オン時の ArmyFlow 停止、建物目標の tick ごとの反転、後加入兵の合流を修正した。旗オフの既存分岐・Contracts・価格・試合規則は変更していない。Release build は警告0・エラー0、指定テストは93件すべて合格した。

## 入れ替わりの出どころ

旗オフ相当の 11/19 パックを一時計測欄付きで tick 13000〜13006 に読み、s17/s18 を追跡した。

| tick | 最終 target / MoveGoal | Joining / LocalGoal | 最後の書き手 | 補足 |
|---:|---|---|---|---|
| 偶数 | `4:214 Wall` / `(69,87)` | false | `PickReachableWall`（marker 107） | Farm 12 の `InRange` が拒否 |
| 奇数 | `4:12 Farm` / `(63,73)` | true / `(55,67)` | `PickRaidTarget`（marker 103） | Farm 12 の `InRange` が受理 |

偶奇差は兵の位置ではなく、射程境界上の固定小数点判定が tick ごとに受理/拒否を反転させたためである。Farm が拒否された偶数 tick は後段の壁選択へ進み、受理された奇数 tick は農場を残したまま ArmyFlow の合流処理が先頭経路を書いた。旧実装は前回建物の `MoveGoal` まで戻していたため、射程外建物を狙ったまま停止する場合もあった。

今回の差分の要点は次のとおり。

```diff
- s.MoveGoal = retainedBuildingMoveGoal;
+ PickRaidTarget(ref s, ..., previousBuildingTarget);
```

```diff
+ if (UnitStuckFixOn && stableBuildingId != 0
+     && IsStableBuildingTarget(faction, stableBuildingId)
+     && !BuildingTargetInRange(stableBuildingId, s.Position, s.Parameters.Range))
+ {
+     s.TargetKind = TargetBuilding;
+     s.TargetId = stableBuildingId;
+     s.MoveGoal = NearestFootprintPoint(stable, s.Position);
+     return;
+ }
```

該当箇所は `Simulation.cs:357,518`、`Simulation.Raids.cs:120-131`。一時 marker/debug 欄は削除済みで、ハッシュ・保存には入れていない。

11/18 の旗オフ replay2 では tick 12995〜13000 に `TargetKind=0/Joining=true` と `4:59 Sawmill` が交互に出た。旗オンの最終記録では s21 は tick 12990 時点ですでに死亡しており、同じ交互区間は発生しなかった。

## 実装

- `Simulation.Navigation.cs:450-480`: 全マップ走査を廃止し、目標セル中心の Chebyshev 半径16の輪を radius 0〜16、各輪内はセル番号順で探索。通行可能性版・陣営・目標セル単位の一時キャッシュを追加した。
- `Simulation.Navigation.cs:505-555`: 空経路または `ArmyFlowWaitTicks` の無進捗を検出し、回復 tick を兵ごとに保持して同じ兵は待ち tick ごとに一度だけ回復。
- `Simulation.Navigation.cs:505-508` と `Simulation.cs:408`: 後から作られた人間 Focus 兵を、意図先の上書き前に現在の軍経路へ参加させる。
- `WorldState.cs:33-34`: 旗オン専用の一時状態 `JoinRecoveryTick`、`UnitStuckLateHumanJoin` を追加。診断・シリアライズ対象外。
- `Headless/Rts.Core.Tests/UnitStuckFixTests.cs`: 射程外の建物目標を持った兵が前進するテストを追加。
- `docs/technical-design-v3.md`: 第80章を今回の原因・半径16・キャッシュ・射程外限定の目標保持に更新。

## テスト

Release 条件は `-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`。全体テストは実行せず、次の7グループを個別フィルターで実行した。

| グループ | 合格 |
|---|---:|
| UnitStuckFixTests | 5 |
| ArmyFlowTests | 4 |
| HumanOrderFlowTests | 6 |
| FortifyTests | 20 |
| RaidVillagersFirstTests | 9 |
| NeutralsTests | 28 |
| SkillsTests | 21 |
| 合計 | 93 |

## 指定 replay の照合

いずれも変更版 CLI、`--allow-build-mismatch --no-states`。

```text
Replay matched S0..S12855
Replay matched S0..S14622
```

1本目は旧 CLI で作った `stuckfix/compat-baseline.rpl`、2本目は指定された `D:\rts-verify\play1119\replay.rpl`。確認後、作業用 rpl は削除した。

## パック検証

最終コードでの旗オン記録は、11/19 が `Recorded S0..S13846`、11/18 が `Recorded S0..S14433`。11/19 は旗オン `probe 200` で600 tick以上の全軍停止が A3/A6 など Reserve の待機軍団だけであることを確認し、Advance/Focus の非待機全軍停止は確認されなかった。

### 11/19 `stall`

| 陣営 | 種別 | 旗オフ n200/n600/max | 旗オン n200/n600/max |
|---:|---|---:|---:|
| 1 | Scout | 15 / 8 / 3600 | 7 / 4 / 1150 |
| 2 | Scout | 4 / 4 / 3500 | 0 / 0 / 0 |
| 1 | その他 | 165 / 55 / 4700 | 58 / 9 / 6600 |
| 2 | その他 | 208 / 75 / 4050 | 134 / 65 / 4550 |

旗オンの長い非斥候区間には Reserve/Guard の待機軍団が含まれる。斥候 n600 は両陣営合計で旗オフ12件から旗オン4件へ減少した。

### 11/18 `stall`

| 陣営 | 種別 | 旗オフ n200/n600/max | 旗オン n200/n600/max |
|---:|---|---:|---:|
| 1 | Scout | 9 / 8 / 5100 | 2 / 0 / 450 |
| 2 | Scout | 4 / 3 / 9300 | 6 / 5 / 1100 |
| 1 | その他 | 27 / 7 / 6450 | 23 / 4 / 3800 |
| 2 | その他 | 94 / 48 / 12000 | 23 / 2 / 11600 |

11/18 の s21 は旗オフの交互区間を `sol` で特定したが、旗オンの同じ範囲（12990〜13010）では全 tick `alive=False` で、交互 target の継続はなかった。

## 大型 terrain 測定

条件は種1〜3、`--terrain --map-size large --ticks 40000 --timing-out`。p99/max は timing CSV の stepMs、units は totalUnits 最大、winner は outcome の core-destroyed から判定した。

| seed | flag | decision tick | winner | max units | p99 ms | max ms |
|---:|---|---:|---:|---:|---:|---:|
| 1 | on | 13567 | 1 | 971 | 28.552 | 310.035 |
| 1 | off | 12855 | 1 | 795 | 24.908 | 321.425 |
| 2 | on | 16075 | 1 | 830 | 38.706 | 297.971 |
| 2 | off | 13889 | 1 | 733 | 26.636 | 287.036 |
| 3 | on | 15520 | 1 | 560 | 21.915 | 282.659 |
| 3 | off | 14653 | 1 | 797 | 27.400 | 275.660 |

勝敗 tick が異なるため、処理経路の純粋な比較として各 seed の共通 tick まででも計算した。旗オン/オフの p99 は seed1 `25.385/24.908 = 1.019`、seed2 `25.255/26.636 = 0.948`、seed3 `18.618/27.400 = 0.679` で、いずれも1.2倍以内。全試合終了までの表では、オン側が長く続いた後半の兵数・経済処理を含むため seed2 の比率が1.2倍を超える。

## 未確認事項

`timing-out` の CSV は個体別の斥候位置・停止区間を出さず、`movedSoldiers` の総数だけである。そのため種1〜3の大型terrain測定について、個体別の「600 tick停止 tick」は算出していない。パック2本については指定 probe の個体別 `stall` と `sol` を実行済みである。

作業中に生成した作業用 `.rpl` は読み取り・probe・照合後に削除し、指定元の `D:\rts-verify\play1119\replay.rpl` と `D:\rts-verify\play1118\replay2.rpl` は保持した。git コマンドは一度も実行していない。
