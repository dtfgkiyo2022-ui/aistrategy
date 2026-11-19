# alert1b 実装報告

## 変えたファイル

- `UnityProject/Assets/RTS/Presentation/BattlefieldView.CombatAlerts.cs`
  - `BattlefieldView` の partial として、戦闘判定結果の表示を追加。
  - `Push(FactionFrame)` の同期直後に `CombatAlertRules` を更新。
  - 最大 8 個の `LineRenderer` を作成時に確保し、以後は半径・透明度・色だけを毎フレーム更新。
  - 左上の通知を `OnGUI` の `GUI.Button` で表示し、クリックで `BattlefieldCamera.ShowPoint`。
  - 画面外の戦闘を最大 4 個、1 枚の三角テクスチャを回転して表示。
  - 256×128 のミニマップテクスチャと `Color32[]` を一度だけ作成し、0.5 秒ごとに `SetPixels32` と `Apply`。
  - ミニマップのクリック・ドラッグ中は `UiHitAreas` に登録し、既存セレクタの選択処理を止める。
- `UnityProject/Assets/RTS/Presentation/BattlefieldView.cs`
  - 既存の `Push` と毎フレーム更新に、戦闘知らせの同期・波紋更新を追加。
- `UnityProject/Assets/RTS/Presentation/SettingsRuntime.cs`
  - `CombatAlerts` 表示設定を追加（既定オン、保存値をランタイムへ反映）。
- `UnityProject/Assets/RTS/Presentation/SettingsRules.cs`
  - `SettingsValues.CombatAlerts` を追加。
- `UnityProject/Assets/RTS/Presentation/Hud/SettingsOverlay.cs`
  - ゲーム設定タブに「戦闘の知らせ・ミニマップ」トグルを追加。
- `alert/report2.md`
  - 本報告。

既存の `BattlefieldView.Wards.cs` の地面印と `PresentationMaterials.GetUnlit`、既存の `OnGUI`・`UiHitAreas`・`BattlefieldCamera.ShowPoint`・`KeyBindings` の流儀に合わせています。Simulation、Contracts、Decision、Replay、Application は変更していません。

## 配置

- 左上の通知：画面左から 16 px、画面上から 74 px。2 行目以降は 28 px 刻み。既存の上部 HUD と重ならないよう、左上の通常 HUD より下に置きました。
- ミニマップ：画面右から 16 px、画面下から 16 px。サイズは 256×128 px。
- 端矢印：画面中央から 28 px 内側の端に配置。

## Space キー

`Input.GetKeyDown(KeyCode.Space)` を `OnGUI` で読みます。`UiHitAreas.Shared.KeyboardCaptured` が true（チャット・名前など既存の入力欄がキーボードを取得中）の場合は反応しません。押すたびに通知リストを新しい順に進み、末尾から先頭へ循環します。既存の `KeyBindings` に Space の割り当てはなく、既存のカメラ移動キーも変更していません。

## ミニマップの地形色

`BattlefieldView.SetTerrain` で表示側が保持している `currentTerrain`（`TerrainMap`）から、`KindAt(x, z)` を読みます。0 は草地、1 は森、2 は水、3 は山として色を塗っています。霧は `FactionFrame.Fog.VisibleCells` と `ExploredCells` を重ね、未探索は暗く、探索済みだが現在不可視の場所は暗くしています。ユニットは自軍、または `Fog.VisibleCells` で見えている相手だけを描きます。

## テスト・未確認事項

- 前回の Headless 6 件を変更していないため、テストコードは追加していません。
- Unity がこの環境で動かせないため、Unity コンパイル、実画面の描画、GUI のフォント・解像度差、カメラ枠の見え方、実機の重さは未確認です。
- ミニマップの地形・霧・ユニット表示は実装しましたが、経済建物・中立獣・カメラ枠の最終的な画面上の重なりは Unity 製品版での確認が必要です。
