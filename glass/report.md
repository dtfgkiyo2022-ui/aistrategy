# Glassworks 実装・検証レポート

## 実施内容

- `ResourceKind` に `Sand=37`, `Glass=38`, `Lens=39`、`BuildingKind.GlassWorks=54`、`RecipeKind.Lens=10` を末尾追加。
- `Glassworks` フラグを `ScenarioDefinition`／`EconomyRules`／LiveGameRules／CLI に追加。`--glassworks-off` は既存の large map 生成と状態を変更しない分岐にした。
- large map の履歴ノードを先に生成し、Glassworks 有効時だけ、各陣営に Sand ノードを近傍4個＋クラスター8個追加。左右点対称・既存ノードとのセル重複なし。
- Glassworks は Sand 2＋Charcoal 1 を搬入して80 tickでGlass 1を生産。Lens は Glass 2＋Gears 1を100 tickで生産し、Age 2以降の Archer／SkirmishArcher に Lens 1を要求。
- Age 2以降の Tower／Castle は Glass を窓材として要求。Tower は Glass 1で視界+4、Castle はGlass 4、最大4ラインを上限とした。
- `ScenarioBinary` extension ID 96、version 1、22個のInt32、末尾厳密検査を追加。flag off時はID96を除去。
- Presentation UIに Glassworks の英日表示、建設ボタン、在庫／不足材表示を追加。
- `docs/technical-design-v3.md` に 38.31 の設計・比率・判断・計測欄を追加。

## ビルドとテスト

```text
dotnet build Headless\Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
0 warnings, 0 errors

dotnet test Headless\Rts.Core.Tests\Rts.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GlassworksTests -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
7 passed, 0 failed
```

GlassworksTests は、enumの追記値、Sandノードの点対称性と24個構成、flag-offのmap／binary安定性、ID96の厳密往復、GlassWorksの入出力、Lens生産、EconomyView公開値を検査する。

## Replay

変更前のRelease CLIを `glass/base-cli` に退避してから作成した baseline replayについて、現行CLIで次を実施した。

```text
replay --in glass\a.rpl --hash-out glass\a.hash --allow-build-mismatch --no-states
Replay matched S0..S40000

replay --in glass\b.rpl --hash-out glass\b.hash --allow-build-mismatch --no-states
Replay matched S0..S27087
```

## 40,000 tick large terrain 計測

`west-tactic auto`／`east-tactic auto`、seed 1〜3、オン／オフ各1回を逐次実行した。p99は timing CSV の `stepMs` を昇順にし、`ceil(0.99*N)` 番目を採用した。詳細な1行1陣営の値は `measurements.csv` にある。

| seed | mode | 終了 tick / winner | peak soldiers F1/F2 | p99 ms | Glassworks F1/F2 | Lens弓兵 F1/F2 |
|---:|:---:|---:|---:|---:|---:|---:|
| 1 | on | 40000 / 0 (未決着) | 312 / 237 | 181.239 | 2 / 2 | 0 / 0 |
| 1 | off | 40000 / 0 (未決着) | 343 / 267 | 160.024 | 0 / 0 | 0 / 0 |
| 2 | on | 40000 / 0 (未決着) | 532 / 477 | 148.982 | 2 / 2 | 0 / 5 |
| 2 | off | 27087 / 1 | 367 / 130 | 66.724 | 0 / 0 | 0 / 7 |
| 3 | on | 24280 / 2 | 164 / 240 | 53.476 | 0 / 2 | 3 / 0 |
| 3 | off | 16391 / 2 | 160 / 268 | 45.093 | 0 / 0 | 4 / 0 |

今回の auto AI では全6 runで `sandGathered=0`、Glass在庫／GlassProduced／Lens在庫／LensProducedは0だった。Glassworksの建設自体は seed 1 on、seed 2 on、seed 3 onの一部で確認できたため、今回のシナリオではSand採取ラインの接続がボトルネックになった。単体テストでは実際のGlass／Lens処理を検証済みである。

## 受け入れ判断

- 追加enum値は末尾追記で既存値を移動していない。
- flag-offは専用テストでmap／binaryの安定性を確認し、baseline replayも全tick一致した。
- extension 96は有効時のみ存在し、無効時は残らない。未知ID・version・長さ・末尾バイトは拒否する。
- 入力はCarriedInputsと搬入容量で制限し、Glassworks／Lensともに部分入力では進行しない。
- UIはGlassworks無効時にボタン・コストを表示しない。
