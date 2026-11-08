# Glassworks glass9 実装・検証レポート

## 実施内容

- `LensGearStockBonus` の陣営別値を `EconomyRules` に追加し、Glassworks が有効なときだけ `WorldState` の初期状態へ復元するようにした。
- `Simulation.Diagnostic` で、Glassworks が有効なときだけ `Economy[1/2].LensGearStockBonus` を正規状態ハッシュへ追加した。旗オフ時の canonical bytes は従来どおり。
- `ScenarioBinary` の拡張欄96を22個から24個のInt32へ拡張し、末尾に西・東の `LensGearStockBonus` を保存・読み込みするようにした。旗オフでは拡張欄96自体を従来どおり書かない。
- `GlassworksTests` に、拡張欄96の24値往復と、Glassworks 有効時の保存・読み込み後のボーナス復元および1200 tickの状態ハッシュ一致を追加した。

## ビルドとテスト

```text
dotnet build Headless\Rts.Headless.slnx -c Release -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
0 warnings, 0 errors

dotnet test Headless\Rts.Core.Tests\Rts.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GlassworksTests -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false
18 passed, 0 failed
```

## 旗オフ照合

旧 CLI で seed 1 の基準記録を作成し、変更後 CLI で `--no-states` replay を実行した。

```text
Replay matched S0..S27420
```

基準 `.rpl` とハッシュ一時ファイルは照合直後に削除した。
