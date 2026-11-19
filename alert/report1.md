# alert1 実装報告

## 変えたファイル

- `UnityProject/Assets/RTS/Presentation/CombatAlertRules.cs`
  - 可視ユニットのHP減少から戦闘地点を抽出。
  - 30m以内のクラスタ統合、5秒の表示寿命、攻撃を受けている側の判定、8方位（日本語・英語）を実装。
  - UnityEngine、Simulation、Contractsの状態変更には依存しない純粋なC#。
- `UnityProject/Assets/RTS/Presentation/MinimapRules.cs`
  - 地図座標からミニマップ座標への変換と、霧中の敵を描かない可視性判定を実装。
  - UnityEngine非依存。
- `Headless/Rts.Core.Tests/CombatAlertRulesTests.cs`
  - 依頼された6項目（30m統合、50m分離、5秒寿命、北東、日本語・英語、座標変換、霧中敵非表示）をテスト。
- `Headless/Rts.Core.Tests/Rts.Core.Tests.csproj`
  - 上記2ルールとテストをCompile許可リストへ追加。

## テスト結果

指定されたRelease設定で対象プロジェクトをビルド：成功（警告0、エラー0）。

追加テストのみを実行：合計6、合格6、失敗0、スキップ0。

全体テストは実行していない。

## Spaceキーの確認結果

既存のPresentationコードとKeyBindingsを確認した範囲では、Spaceキーの既存割り当ては見つからなかった。既存のキー割り当ては変更していない。なお、今回の変更ではSpaceキーからカメラ移動を呼ぶUnity側接続までは追加していない。

## 画面での見え方の予想

純粋ルール層は、被弾地点をまとめて戦闘アラートとして返し、5秒後に消せる状態までを提供する。方向文は自分のコアから見た「北東」など8方位（英語設定では`Northeast`など）になる。ミニマップ座標は地図の左下・右上・中央をそのままミニマップの対応位置へ写し、霧中の敵は描画対象から除外できる。

ただし、赤い波紋、画面端矢印、左上3行HUD、クリック／Spaceでの`BattlefieldCamera.ShowPoint`、地形・霧・ユニットを描くミニマップ本体は、Unity側の描画コンポーネントへまだ接続されていないため、現時点では画面には出ない。

## 確認できていないこと

- Unityでの描画、UI配置、既存UIとの重なり。
- ミニマップのテクスチャ再利用とドラッグ操作。
- Unity実行時の描画負荷・メモリ増加。

これらは依頼どおりUnityでClaudeが確認する必要がある。
