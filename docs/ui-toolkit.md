# UI Toolkit の HUD

## 器の作り

`Rts.Presentation.HudToolkit` はシーンを変更せず、`LiveMatchHost.Begin` から必要なときだけ実行時に追加されます。
`ScriptableObject.CreateInstance<PanelSettings>()` で `PanelSettings` を作り、`PanelScaleMode.ScaleWithScreenSize` と基準解像度 `1920x1080` を設定してから、同じ GameObject に `UIDocument` を `AddComponent` します。画面の大きさが変わっても HUD の基準寸法を保ち、既存の画面レイアウトと合わせやすくするためです。

UXML と USS は `Assets/RTS/UI/Resources/Hud/` にあり、`Resources.Load<VisualTreeAsset>` と `Resources.Load<StyleSheet>` で読みます。UXML は欄の骨組み、USS は見た目、`HudToolkit` はフレームデータを値に反映する役割です。

## USS の変数

`HudTheme.uss` は並び・位置・共通の大きさだけを持つ土台です。色、枠、角、文字の大きさ、帯の高さと余白は、選択中の次のテーマ USS の `:root` 変数で決まります。`HudToolkit` は `Resources/Hud/HudTheme` を常に読み、`Resources/Hud/Themes/` のテーマを一つだけ追加します。設定欄の「見た目の案」は `rts.hud.theme` に保存し、次のフレームに選択中のファイルを付け替えます。

- `Themes/ThemeStone.uss`：A 石と真鍮。`#241f19` の不透明な板、真鍮色の枠、明朝の見出し。
- `Themes/ThemeTable.uss`：B 作戦卓。半透明の青黒い板、青緑の枠、広い字間と BIZ UDGothic 系の文字。
- `Themes/ThemeParchment.uss`：C 羊皮紙の軍図。`#efe3c8` の板、濃い 2px 枠、右下へずらした濃い板を影の代わりに使用。

3案の名前・USS の Resources パス・見出し/本文フォントの候補順は、UnityEngine に依存しない `Rts.Presentation.HudThemeCatalog` にまとめています。フォントはテーマごとに `FontAsset.CreateFontAsset` で一度だけ試し、切り替え後もキャッシュを使います。

- `--hud-panel`：帯の背景色
- `--hud-panel-border`：帯の枠色
- `--hud-accent`：時代名などの強調色
- `--hud-text`：通常の文字色
- `--hud-muted-text`：資源名の文字色
- `--hud-font-size` / `--hud-small-font-size`：文字の大きさ
- `--hud-border-width`：枠の太さ
- `--hud-radius`：角の丸み
- `--hud-gap` / `--hud-padding`：欄間の間隔と帯の内側余白
- `--hud-top-height` / `--hud-age-width` / `--hud-status-width`：案ごとの帯の高さと左右欄の幅
- `--hud-heading-size` / `--hud-age-stage-size` / `--hud-number-width`：見出し、段表示、桁をそろえる数値欄の寸法
- `--hud-heading` / `--hud-good` / `--hud-warning`：見出し、良い状態、注意状態の色
- `--hud-resource-border` / `--hud-input` / `--hud-button` / `--hud-button-text`：資源欄、入力、ボタン用の色
- `--hud-staff-*`：参謀欄の板、吹き出し（あなた／参謀／詳しく／悪い）、札、入力欄、選択欄、ボタンの地・字・枠・角

資源の小さな四角は `.hud-resource-icon` です。画像を用意した段階で、この要素を背景画像などに差し替えます。

## 別の欄を移す手順

1. `Resources/Hud/` に欄の UXML と USS を追加する。
2. `HudToolkit` の `EnsureDocument` で読み込み、`Update` では表示データが変わったときだけ Label などを書き換える。
3. 判定・書式は `Rts.Presentation` 内の UnityEngine に依存しないクラスへ置く。
4. クリックを地図へ通さない欄は `UiHitAreas.Shared.Register` に同じ画面座標の矩形を登録する。
5. 既定の IMGUI と同時に表示しない切り替えを行い、必要なら既存の `OnGUI` 側を残したまま段階移行する。

## フォント

フォントファイルは同梱しません。`HudThemeCatalog` の案ごとの候補を `HudToolkit` が Unity 6 の `UnityEngine.TextCore.Text.FontAsset.CreateFontAsset` に渡し、`Regular` で試します。見出し用が失敗した場合は本文用、本文用も失敗した場合は UI Toolkit の既定フォントに戻します。見出しは UXML の `.hud-heading` 要素にだけ見出し用 FontAsset を付けます。

## 切り替え

- 通常は `PlayerPrefs` の `rts.hud.toolkit` が `0`（未設定を含む）なので、従来どおり IMGUI です。
- `PlayerPrefs.SetInt("rts.hud.toolkit", 1)` を保存すると UI Toolkit の上の帯になります。
- 一時的な確認は起動引数 `-hud-toolkit` で有効にできます。

今回の実装では `TopBarResourceVisibility` と `TopBarDisplayText` が資源の出し分け・数の書式・時代表示を共有し、IMGUI と UI Toolkit の内容を揃えています。

## 参謀の欄

`Resources/Hud/Staff.uxml` と `HudTheme.uss` が、右側の `UiLayout.Strategist` と同じ位置に参謀の欄を作ります。`HudToolkit` は `IStaffControl` だけを読み、AI の選択、対象、見積もり、費用、予算、やり取りを表示します。やり取りの行は増えたときだけ VisualElement を追加し、既存行の状態や本文だけを更新してから新しい行へスクロールします。

表示側の口は `Presentation/IStaffControl.cs` です。`LiveMatchHost` が対象と選択中 AI を保持し、送信時には従来どおり `LiveAiCommandPort` の予約・解釈経路へ渡します。履歴の状態判定と文面生成は Unity に依存しない `StaffChatText` にまとめています。

`TextField` はフォーカス中に IME を有効にし、Enter は `Input.compositionString` が空のときだけ送信します。変換中の Enter は候補確定に任せます。フォーカス中は `UiHitAreas` のキーボード捕捉を有効にするため、地図の WASD／矢印操作は止まります。マウスクリックについても、従来の IMGUI と同じ `UiLayout.Calculate(...).Strategist` を `UiHitAreas.Shared.Register` に登録します。

AI の一覧は押すと開く自前の選択欄です。利用できない AI は無効なボタンとして表示します。見た目の案ごとの吹き出し、札、入力欄、ボタンの色・枠・角は `ThemeStone.uss`、`ThemeTable.uss`、`ThemeParchment.uss` の `--hud-staff-*` 変数で切り替わります。

`rts.hud.toolkit` が有効なときだけこの欄を表示し、同時に `LiveMatchHost.OnGUI` の参謀欄を描きません。既定の設定では従来の IMGUI を使います。
