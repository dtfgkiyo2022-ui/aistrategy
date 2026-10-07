# UI Toolkit の HUD

## 器の作り

`Rts.Presentation.HudToolkit` はシーンを変更せず、`LiveMatchHost.Begin` から必要なときだけ実行時に追加されます。
`ScriptableObject.CreateInstance<PanelSettings>()` で `PanelSettings` を作り、`PanelScaleMode.ScaleWithScreenSize` と基準解像度 `1920x1080` を設定してから、同じ GameObject に `UIDocument` を `AddComponent` します。画面の大きさが変わっても HUD の基準寸法を保ち、既存の画面レイアウトと合わせやすくするためです。

UXML と USS は `Assets/RTS/UI/Resources/Hud/` にあり、`Resources.Load<VisualTreeAsset>` と `Resources.Load<StyleSheet>` で読みます。UXML は欄の骨組み、USS は見た目、`HudToolkit` はフレームデータを値に反映する役割です。

## USS の変数

- `--hud-panel`：帯の背景色
- `--hud-panel-border`：帯の枠色
- `--hud-accent`：時代名などの強調色
- `--hud-text`：通常の文字色
- `--hud-muted-text`：資源名の文字色
- `--hud-icon-food` / `--hud-icon-wood` / `--hud-icon-ore` / `--hud-icon-metal`
- `--hud-icon-stone` / `--hud-icon-gems` / `--hud-icon-gold`
- `--hud-icon-charcoal` / `--hud-icon-steel` / `--hud-icon-bow-gear`
- `--hud-font-size` / `--hud-small-font-size`：文字の大きさ
- `--hud-border-width`：枠の太さ
- `--hud-radius`：角の丸み
- `--hud-gap` / `--hud-padding`：欄間の間隔と帯の内側余白

資源の小さな四角は `.hud-resource-icon` です。画像を用意した段階で、この要素を背景画像などに差し替えます。

## 別の欄を移す手順

1. `Resources/Hud/` に欄の UXML と USS を追加する。
2. `HudToolkit` の `EnsureDocument` で読み込み、`Update` では表示データが変わったときだけ Label などを書き換える。
3. 判定・書式は `Rts.Presentation` 内の UnityEngine に依存しないクラスへ置く。
4. クリックを地図へ通さない欄は `UiHitAreas.Shared.Register` に同じ画面座標の矩形を登録する。
5. 既定の IMGUI と同時に表示しない切り替えを行い、必要なら既存の `OnGUI` 側を残したまま段階移行する。

## フォント

フォントファイルは同梱しません。`HudToolkit` が Unity 6 の `UnityEngine.TextCore.Text.FontAsset.CreateFontAsset` を使い、OS の `Yu Gothic UI`、`Meiryo`、`MS Gothic` を順に `Regular` で試します。すべて失敗した場合は UI Toolkit の既定フォントに戻し、警告を出します。将来同梱フォントへ差し替える場合も、この一か所を変更します。

## 切り替え

- 通常は `PlayerPrefs` の `rts.hud.toolkit` が `0`（未設定を含む）なので、従来どおり IMGUI です。
- `PlayerPrefs.SetInt("rts.hud.toolkit", 1)` を保存すると UI Toolkit の上の帯になります。
- 一時的な確認は起動引数 `-hud-toolkit` で有効にできます。

今回の実装では `TopBarResourceVisibility` と `TopBarDisplayText` が資源の出し分け・数の書式・時代表示を共有し、IMGUI と UI Toolkit の内容を揃えています。
