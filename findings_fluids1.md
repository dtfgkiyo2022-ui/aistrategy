# fluids1（request3）実装結果

## A〜E の対応

- `ApplyFluidCommand` の自動建設用の木材・石の補填を削除し、資材不足時は置かず `wood` を数えるようにした。
- パイプ・流体建物を自動完成させる処理を削除した。村人の建設を待ち、`FluidPumpCount` などは完成 tick にだけ加算する。
- `PlaceBuildingAt` の流体専用 bypass と、醸造所の資材取り置き追加ブロックを削除した。`Livelihood` の時代 2 の Ale 減算・`AlePaid` 増加も削除した。
- 自動配置の実配置判定は人と同じ `SiteIsClear` とし、候補選択だけが周辺を探索する。醸造所の地図全体 fallback は削除した。
- 管網の接続対象を `IsFluidBuilding` と冷却対象（製錬所・高炉・ガラス工房・煉瓦窯）に拡張した。冷却対象もパイプに辺で接して複数網を橋渡しする方式を採用した。完成・破壊・撤去で dirty にする。
- 冷却テストは、井戸—パイプ—製錬所を実際に接続して `FluidNetworkId` を手書きせず、時間が `ceil(基準×0.8)` になること、パイプ撤去後に基準へ戻ることを確認する形に書き直した。
- パイプ所有・建物占有・セル→管網番号を再利用配列で保持し、経路探索とパイプ配置の 1 マス判定を O(1) 化した。管網再構築の旧網との重なりはセル→旧網配列で数え、建物×網のリスト作成と二重ループを除去した。`AdvanceFluids` は液体建物一覧だけを走査する。
- AI は 600 tick 周期かつ時代 2 以降だけ動作し、初回水源は川が本拠地から 40 マス以内なら揚水機、無ければ井戸を選ぶ。初回に両方を置かない。醸造所上限・時代 3 前の準備・1 回 2 経路・冷却 1 棟制限・`site/water/route/wood` 別カウンタを実装した。
- `Belts` の Ale 在庫加算は `FluidsOn` 時だけにした。旗オフでは Ale が流入しない。

醸造所の値は設計値どおり、2×2、木材 120、石 30、仕事 240、HP 500、食料 2、水 5、80 tick、毎分 15、生産上限 10 である。冷却は水 3、時間 800‰ である。

## テスト・ビルド

使用した共通オプション:

```text
-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
```

- `FluidsTests`（A の資材不足・未完成醸造所、B の冷却、管網分割、旗オフを含む）: **8/8 合格**。
- 指定フィルター（Fluids / Livelihood / Glassworks / Belt / ScenarioBinary）: **137/137 合格、失敗 0、スキップ 0**。
- `Headless/Rts.Headless.slnx` Release build: **警告 0、エラー 0**。
- 全テストは長時間シナリオが継続したため中断した。必須の指定フィルターは完走している。

## 旗オフ照合

- 既定の小さい地図: base CLI で記録し、現行 CLI の `replay --no-states` で **`Replay matched S0..S3241`**。
- 広い地図: base CLI は旧版のため `--fluids-off` オプションを受け付けなかった。同じ旧版の流体なし既定経路で seed 1 を記録し、現行 CLI で **`Replay matched S0..S13904`**。

## 指定対戦（流体オン）

実行条件は `--terrain --map-size large --ticks 40000 --west-tactic auto --east-tactic auto`。いずれも決着が 40000 tick より前だった。

### seed 1

```text
tactic-match: ticks=13904 ended=True winner=1 westAiLevel=2 eastAiLevel=2
outcome: reason=core-destroyed tick=13904 core1Hp=5747 core1Attackers=23 core2Hp=0 core2Attackers=78
fluids: faction=1 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=106 water=0 route=0 wood=0
fluids: faction=2 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=168 water=0 route=0 wood=0
```

### seed 2

```text
tactic-match: ticks=13491 ended=True winner=1 westAiLevel=2 eastAiLevel=2
outcome: reason=core-destroyed tick=13491 core1Hp=6000 core1Attackers=0 core2Hp=0 core2Attackers=30
fluids: faction=1 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=6287 water=0 route=0 wood=0
fluids: faction=2 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=5805 water=0 route=0 wood=20663
```

### seed 3

```text
tactic-match: ticks=13719 ended=True winner=1 westAiLevel=2 eastAiLevel=2
outcome: reason=core-destroyed tick=13719 core1Hp=1856 core1Attackers=54 core2Hp=0 core2Attackers=97
fluids: faction=1 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=72 water=0 route=0 wood=0
fluids: faction=2 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=74 water=0 route=0 wood=0
```

## 指定対戦（`--fluids-off`）

同じ seed・条件で現行 CLI を実行した結果は次のとおり。旗オフでも勝敗・決着 tick はオンと一致した。

```text
seed 1
tactic-match: ticks=13904 ended=True winner=1 westAiLevel=2 eastAiLevel=2
outcome: reason=core-destroyed tick=13904 core1Hp=5747 core1Attackers=23 core2Hp=0 core2Attackers=78
fluids: faction=1 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=0 water=0 route=0 wood=0
fluids: faction=2 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=0 water=0 route=0 wood=0

seed 2
tactic-match: ticks=13491 ended=True winner=1 westAiLevel=2 eastAiLevel=2
outcome: reason=core-destroyed tick=13491 core1Hp=6000 core1Attackers=0 core2Hp=0 core2Attackers=30
fluids: faction=1 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=0 water=0 route=0 wood=0
fluids: faction=2 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=0 water=0 route=0 wood=0

seed 3
tactic-match: ticks=13719 ended=True winner=1 westAiLevel=2 eastAiLevel=2
outcome: reason=core-destroyed tick=13719 core1Hp=1856 core1Attackers=54 core2Hp=0 core2Attackers=97
fluids: faction=1 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=0 water=0 route=0 wood=0
fluids: faction=2 pipeCells=0 networks= buildings= pumps=0 wells=0 breweries=0 tanks=0 waterProduced=0 aleProduced=0 alePaid=0 cooling=0 site=0 water=0 route=0 wood=0
```

## 満たせなかった点と原因

指定対戦では、A で復元した共通の `SiteIsClear`、地域、敵回避、資材待ち、兵力取り置きの条件を自動建設にも適用した結果、時代 2 到達時点で水源の候補地が確保できず、全 seed で流体設備が 0 になった。seed 2 の faction 2 は同時に資材待ちも発生した。したがってこの実測では 65.7 の「パイプ・水源・醸造・水生成を全陣営で非 0」と、冷却回数非 0 を満たしていない。これを満たすための無料資材、早期完成、SiteIsClear の緩和、旗オンだけの bypass は追加していない。

## 変更ファイル

- `UnityProject/Assets/RTS/Simulation/Simulation.Fluids.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Buildings.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.EconomyCommands.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Livelihood.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Belts.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Raids.cs`
- `UnityProject/Assets/RTS/Simulation/WorldState.cs`
- `UnityProject/Assets/RTS/Simulation/Simulation.Diagnostic.cs`
- `Headless/Rts.Headless.Cli/TacticMatchCommand.cs`
- `Headless/Rts.Core.Tests/FluidsTests.cs`
- `findings_fluids1.md`
