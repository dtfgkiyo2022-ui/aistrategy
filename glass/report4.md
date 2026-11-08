# glass4 実施報告

## 結論

- flag-off の base CLI 自己記録→自己再生は seed1/2 とも一致した。
- 変更後 CLI で base の記録を再生しても seed1 は `Replay matched S0..S20000`、seed2 は `Replay matched S0..S27087` になった。
- レンズ弓兵の訓練判定を設計 38.31 に合わせ、レンズ在庫・自動訓練・時代条件の診断カウンタを追加した。
- seed1〜3 の最終測定値は `glass/measurements4.csv` に記録した。

## 1. flag-off replay 不一致

まず `glass/base-cli` 自身で seed1 を記録し、同じ `glass/base-cli` で再生して一致することを確認した。その後、tick 17998 前後を限定ダンプして比較した。最初の差は村人 604 の Carry と belt 15210 の状態で、base は村人 Carry=5／belt が空、変更後は村人 Carry=4／belt に Slag(36) が1個あった。

原因は `BeltDropOffReachable` の flag-off キャッシュ stride だった。変更後は Sand 追加分を含む 38 スロットで添字を計算していたが、base は旧来の 33 スロットを使っていた。flag-off では base と同じ 33 スロットに戻し、Glassworks on のときだけ Sand を含む stride を使うようにした。

共通処理については、`CanHaulTo`、`TryFeedBuilding`、`BeltCanFeedBuilding`、運び役、自動 recipe 判定、ArmyRecovery の Glassworks 専用条件を確認した。GearWorkshop の出力ポート予備配置は実測で一度も使われていなかったため削除し、seed1/2 replay の一致も最終コードで再確認した。

## 2. レンズ弓兵の診断と修正

`CanPay` と自動 `Enqueue` に、Lens Archer のみを対象とする固定カウンタを追加した。カウンタは次の4段階を区別する。

- `lensTrainingChecks`: レンズ弓兵の訓練可否を調べた回数
- `lensAgeBlocked` / `lensAgeEligible`: 時代条件で止まった回数／時代条件を通過した回数
- `lensInventoryBlocked`: レンズ在庫不足で止まった回数
- `lensPayBlocked` / `lensCanPayPassed`: 最終支払判定の失敗／通過

原因は、Glassworks on で Archer が special unit として選ばれても、通常歩兵の Metal 不足チェックが先に返り、レンズだけで訓練できる Archer まで自動訓練候補から落ちていたことだった。`DecideInfantry` で、Glassworks on かつ Archer のときだけ `CanPay` を通せるようにした。Lens の価格、Lens の必要量、時代2条件そのものは変更していない。

診断値の要点は次のとおり。

| seed / faction | glassworks | ranges | lensProduced | lensArchers | checks | ageBlocked | ageEligible | inventoryBlocked | payBlocked / passed |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 / 1 | 3 | 0 | 56 | 0 | 0 | 0 | 0 | 0 | 0 / 0 |
| 1 / 2 | 3 | 0 | 45 | 0 | 0 | 0 | 0 | 0 | 0 / 0 |
| 2 / 1 | 2 | 0 | 27 | 0 | 0 | 0 | 0 | 0 | 0 / 0 |
| 2 / 2 | 2 | 1 | 6 | 5 | 17 | 17 | 0 | 0 | 8 / 9 |
| 3 / 1 | 0 | 1 | 0 | 4 | 9 | 9 | 0 | 0 | 5 / 4 |
| 3 / 2 | 3 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 / 0 |

seed2 faction2 では Lens が6個生産され、弓兵5体が存在する。診断上、同 run で訓練可否を調べた17回はすべて時代条件側で止まり、レンズ在庫不足は0回だった。seed1 は両陣営とも ArcheryRange が0で、訓練判定まで到達していない。seed3 は faction1 が時代条件を通過した弓兵4体を持つが、Glassworks を建てた faction2 は決着までに ArcheryRange を建てていない。

追加テスト `AgeTwoArcherQueuesAndConsumesOneLensWhileAgeOneDoesNotRequireIt` で、時代1では Lens cost が0、時代2では設定値の Lens cost が必要で、訓練 enqueue 時にその量だけ消費されることを検証した。

## 3. Glassworks 測定

実行条件は全 seed 共通で、次のとおり。

```text
--map-size large --terrain --ticks 40000 --west-tactic auto --east-tactic auto --timing-out
```

`p99StepMs` は timing CSV の stepMs に対する nearest-rank p99。seed2/3 は glass3 と同じく、seed2 は 40000 tick で未決着、seed3 は 15409 tick で core-destroyed になった。seed2 の flag-off 27087 tickに対して on は未決着まで延びているが、Glassworks の建設・レンズ生産と F2 の弓兵／防衛線への資源配分で戦闘推移が変わったためである。seed3 は flag-off 16391 tickに対して 15409 tickで、延長していない。

| seed | ticks | ended / reason | p99 ms | faction別 lensProduced / lensArchers |
|---:|---:|---|---:|---|
| 1 | 40000 | no / undecided | 100.139 | F1 56 / 0, F2 45 / 0 |
| 2 | 40000 | no / undecided | 228.410 | F1 27 / 0, F2 6 / 5 |
| 3 | 15409 | yes / core-destroyed | 59.338 | F1 0 / 4, F2 1 / 0 |

GearWorkshop の「出力ポートが塞がっていても最初の facing で強制配置する」予備道は、seed1〜3の全陣営で `gearNoOutputPort=0` だったため削除した。削除前後で、seed1〜3の ticks、勝敗、Glassworks、Lens、lensProduced、lensArchers、およびピーク兵数は変わらないことを再計測で確認した。

## 検証

```text
dotnet build Headless\Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
成功、警告0、エラー0

dotnet test Headless\Rts.Core.Tests\Rts.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GlassworksTests -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
11 passed、0 failed、0 skipped
```

Simulation には float/double や Dictionary 列挙順による判定を追加していない。毎 tick の全ノード・全建物走査も追加していない。全体テストは実行していない。
