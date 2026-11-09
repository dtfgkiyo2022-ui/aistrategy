# glass6 実施報告

## 実装

- `LayGlassworksRoutes` の金属出どころ選択を修正した。`Smelter || BlastFurnace` を同じ条件として扱い、建物 ID 順の最初の候補を基本に、出口 (`Output`) に金属がある最初の候補を優先する。
- ガラス工房の未完成搬送線だけが使う `SetGlassworksPriorityHauler` を追加した。共通の `SetPriorityHauler` は `v.HaulFrom != 0` を含む元の条件に戻した。
- ガラス工房の経路敷設は `DecideGlassworks` と `EnsureGlassLensLine` の同じ 600 tick 内で二重に実行しないよう、後者の判断地点で一度だけ行うようにした。失敗時は既存の運び役へ任せ、次の判断回で再試行する。
- 在庫からの直取り、source ID 直送、建物処理側の直接転送は追加していない。

## 見当の確認

- 依頼にあった演算子優先順位の問題は実際に存在した。修正前は `building.Kind == Smelter || building.Kind == BlastFurnace && metalSource == null` で、製錬所が見つかるたびに上書きされていた。
- glass5 の `glassRouteFound` と `glassRoutesToAssembler` の差は、経路探索後の `LayRoutes` が完了していないことを示す。今回、同一判断回の二重呼び出しを除き、失敗時の搬送役設定をガラス線専用関数へ分離した。

## テスト

- `dotnet build Headless\Rts.Core.Tests\Rts.Core.Tests.csproj -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`: 成功、警告0、エラー0。
- `GlassworksTests`: 15 passed、0 failed、0 skipped。
- 追加テスト:
  - `GlassworksOutletGlassIsDeliveredToLensAssemblerByAHauler`
  - `GlassworksMetalSourceUsesTheFirstProductiveSmelter`

## 実測

- 指定の flag-on seed 1（large、terrain、40000 tick）を1本だけ開始したが、この PC では長時間 CPU を使用し続けたため停止した。完走した決着 tick、p99、lens 入力・生産値は取得できていない。
- `glass/measurements6.csv` は measurements5 と同じ列のヘッダーのみ。未取得値を既存値で補完していない。
- 実行途中に `glass/measure6_seed1.rpl`（約414MB）が生成された。削除を試みたが、環境の破壊操作ポリシーにより拒否されたため残置している。手元で削除可能になったら削除すること。

## 共通処理の確認

`SetPriorityHauler` は次の条件に戻っている。

```csharp
if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != 0 || v.HaulTo != 0 || v.Carry > 0
    || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
```

ガラス・歯車・金属の未完成線だけが `SetGlassworksPriorityHauler` を呼ぶ。
