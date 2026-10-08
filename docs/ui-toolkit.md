# UI Toolkit の HUD

## 器の作り

`Rts.Presentation.HudToolkit` はシーンを変更せず、`LiveMatchHost.Begin` から必要なときだけ実行時に追加されます。
`ScriptableObject.CreateInstance<PanelSettings>()` で `PanelSettings` を作り、`PanelScaleMode.ScaleWithScreenSize` と基準解像度 `1920x1080` を設定してから、同じ GameObject に `UIDocument` を `AddComponent` します。画面の大きさが変わっても HUD の基準寸法を保ち、既存の画面レイアウトと合わせやすくするためです。

UXML と USS は `Assets/RTS/UI/Resources/Hud/` にあり、`Resources.Load<VisualTreeAsset>` と `Resources.Load<StyleSheet>` で読みます。UXML は欄の骨組み、USS は見た目、`HudToolkit` はフレームデータを値に反映する役割です。

## USS の変数

`HudTheme.uss` は並び・位置・共通の大きさだけを持つ土台です。色、枠、角、文字の大きさ、帯の高さと余白は、選択中の次のテーマ USS の `.hud-document` 変数で決まります。`HudToolkit` は `Resources/Hud/HudTheme` を常に読み、`Resources/Hud/Themes/` のテーマを一つだけ追加します。設定欄の「見た目の案」は `rts.hud.theme` に保存し、次のフレームに選択中のファイルを付け替えます。

- `Themes/ThemeStone.uss`：A 石と真鍮。`#241f19` の不透明な板、真鍮色の枠、明朝の見出し。
- `Themes/ThemeTable.uss`：B 作戦卓。半透明の青黒い板、青緑の枠、広い字間と BIZ UDGothic 系の文字。
- `Themes/ThemeParchment.uss`：C 羊皮紙の軍図。`#efe3c8` の板、濃い 2px 枠、右下へずらした濃い板を影の代わりに使用。

4案の名前・USS の Resources パス・見出し/本文フォントの候補順と、同梱フォントを使うかどうかは、UnityEngine に依存しない `Rts.Presentation.HudThemeCatalog` にまとめています。A 石と真鍮だけは見出しをOSの明朝候補から作り、失敗したときに同梱 Boldへ戻します。本文は同梱 Regularを使います。B/C/Dは見出し・本文とも同梱フォントを優先し、読めないときだけ各テーマのOS候補へ戻します。

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

資源の絵は `.hud-resource-icon` に表示します。資源種別ごとの画像、色付け、読み込み方法は「資源と人口の絵」にまとめています。

## 別の欄を移す手順

1. `Resources/Hud/` に欄の UXML と USS を追加する。
2. `HudToolkit` の `EnsureDocument` で読み込み、`Update` では表示データが変わったときだけ Label などを書き換える。
3. 判定・書式は `Rts.Presentation` 内の UnityEngine に依存しないクラスへ置く。
4. クリックを地図へ通さない欄は `UiHitAreas.Shared.Register` に同じ画面座標の矩形を登録する。
5. 既定の IMGUI と同時に表示しない切り替えを行い、必要なら既存の `OnGUI` 側を残したまま段階移行する。

## フォント

`Resources/Hud/Fonts/` の `NotoSansJP-Regular.otf` と `NotoSansJP-Bold.otf` を、`HudToolkit` が起動時にそれぞれ一度だけ `Resources.Load<Font>("Hud/Fonts/NotoSansJP-Regular")`／`Bold` で読みます。その `Font` を `UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(Font)` に渡して動的な FontAssetを作り、通常の文字には Regular、UXMLの `.hud-heading` 要素には Boldを付けます。作成に失敗した場合だけ、`HudThemeCatalog` のOSフォント候補を試し、それも失敗した場合はUI Toolkitの既定フォントに任せます。テーマ切り替え後も作成済みのFontAssetを使い、毎フレームは読みません。

## 資源と人口の絵

`Resources/Hud/Icons/` のPNGを起動時に資源種別ごとに一度だけ `Resources.Load<Texture2D>` で読みます。`HudToolkit` が資源欄の `.hud-resource-icon` の `style.backgroundImage` に対応するTexture2Dを設定し、人口の前には `person.png` を同じ方法で置きます。`.hud-resource-icon` と人口アイコンは USS の `-unity-background-image-tint-color: var(--hud-accent)` で案の強調色に染め、テーマごとの大きさはおよそ18pxです。絵が読めない場合もHUDを止めず、色付きの空欄を残します。

設定欄の下部には、同梱フォントと game-icons.net の作者・ライセンスを短い出典行で表示します。詳しい対応表は `Assets/RTS/UI/CREDITS.md` にあります。

## 切り替え

- **既定は新しい画面（UI Toolkit）**（オーナー決定 2026-10-09）。`PlayerPrefs` の `rts.hud.toolkit` が未設定か `1` なら新しい画面、`0`（試合の設定で「使わない」）なら古い画面（IMGUI）。
- 起動引数 `-hud-legacy` で、その回だけ古い画面で始める。
- 一時的な確認は起動引数 `-hud-toolkit` で有効にできます。

今回の実装では `TopBarResourceVisibility` と `TopBarDisplayText` が資源の出し分け・数の書式・時代表示を共有し、IMGUI と UI Toolkit の内容を揃えています。

## 試合の設定・言語・命令の記録・結果

`Resources/Hud/Setup.uxml` は上部右側の「試合の設定」と言語切り替え、中央の設定欄を持ちます。設定欄は `ScrollView` の中に作り、返答の遅延、相手・自軍の方針、両側の戦術、戦術の状態・読み直し・つまみ・合図、Workshop、フォルダ、マップ、追加ルール、文明、外部AI、素材の出典を表示します。戦術の選択は2列、数値のつまみは `Slider`／`SliderInt` と −／＋ボタンです。`Resources/Hud/Log.uxml` は折りたたみボタン、命令の7状態、命令の記録を持ち、`Resources/Hud/Result.uxml` は試合結果と再試合を持ちます。

これらの値の読み取り・文面・操作は `CommandPanel` の公開ヘルパーと、既存の `ICommandDelayControl`、`IOpponentControl`、`ITacticControl`、`IMapChoice`、`IMatchRuleChoice`、`IPlayerFilesControl`、`IWorkshopControl` などを共用します。命令と戦術の合図は従来どおり `ICommandPort`／戦術の既存口を通り、UI Toolkit からシミュレーション状態を直接変更しません。

`HudToolkit` が有効な間は `CommandPanel.OnGUI` が何も描かず、何も `UiHitAreas.Shared.Register` しません。設定欄が閉じている間は `Setup` の矩形を登録せず、ログが閉じている間は状態・記録の矩形を登録しません。表示中の上部ボタン、設定、ログ、結果だけを `UiHitAreas.Shared.Register` に登録するため、閉じた欄の範囲が地図入力を遮断しません。無効時は従来の IMGUI がこれらを描きます。

## 兵站・試合の時間・時系列

新しい画面では `Resources/Hud/Supply.uxml`、`Clock.uxml`、`Timeline.uxml` が、それぞれ従来の `UiLayout.Supply`、`UiLayout.TopCenter`、`UiLayout.Timeline` に配置されます。4案のテーマ USS が共通の `--hud-*` 変数を使うため、板・枠・文字・ボタンは他の欄と同じ見た目の案に切り替わります。

兵站の数値とコア・拠点の次の増援までの文は `CommandPanel` の共有表示メソッドを使います。時系列の行は `MatchTimeline` が作った内容を使い、新しい行が増えたときだけ VisualElement を追加して一番下へスクロールします。時計の停止、1 tick、速度、見ている陣営の切り替えは `TimelinePanel` の共有操作メソッドを通り、従来どおり `IMatchClock` だけを操作します。

新しい画面が有効な間は、`CommandPanel` の兵站欄と `TimelinePanel` の時計・時系列欄を IMGUI で描きません。3つの Toolkit の矩形は `UiHitAreas.Shared.Register` に登録されるため、欄の下へ地図クリックが抜けません。Toolkit 側から Contracts やシミュレーション状態を直接変更することはありません。

## 参謀の欄

`Resources/Hud/Staff.uxml` と `HudTheme.uss` が、右側の `UiLayout.Strategist` と同じ位置に参謀の欄を作ります。`HudToolkit` は `IStaffControl` だけを読み、AI の選択、対象、見積もり、費用、予算、やり取りを表示します。やり取りの行は増えたときだけ VisualElement を追加し、既存行の状態や本文だけを更新してから新しい行へスクロールします。

表示側の口は `Presentation/IStaffControl.cs` です。`LiveMatchHost` が対象と選択中 AI を保持し、送信時には従来どおり `LiveAiCommandPort` の予約・解釈経路へ渡します。履歴の状態判定と文面生成は Unity に依存しない `StaffChatText` にまとめています。

`TextField` はフォーカス中に IME を有効にし、Enter は `Input.compositionString` が空のときだけ送信します。変換中の Enter は候補確定に任せます。フォーカス中は `UiHitAreas` のキーボード捕捉を有効にするため、地図の WASD／矢印操作は止まります。マウスクリックについても、従来の IMGUI と同じ `UiLayout.Calculate(...).Strategist` を `UiHitAreas.Shared.Register` に登録します。

AI の一覧は押すと開く自前の選択欄です。利用できない AI は無効なボタンとして表示します。見た目の案ごとの吹き出し、札、入力欄、ボタンの色・枠・角は `ThemeStone.uss`、`ThemeTable.uss`、`ThemeParchment.uss` の `--hud-staff-*` 変数で切り替わります。

`rts.hud.toolkit` が有効なときだけこの欄を表示し、同時に `LiveMatchHost.OnGUI` の参謀欄を描きません。既定の設定では従来の IMGUI を使います。

## 命令の欄

`Resources/Hud/Commands.uxml` と `HudTheme.uss` が、`UiLayout.Commands` と同じ場所に命令の欄を作ります。左側に現在の選択（軍団なら軍団番号と生存数、拠点・コアならその説明、未選択なら選び方）を表示し、右側に攻撃・撤退・自コア防衛・拠点放棄・予備30%・お任せ復帰・取消を配置します。ボタン横の小さな空欄は、後でキー割り当てを表示するための場所です。

`HudToolkit` は `CommandPanel` を受け取り、`CommandPanel.BeginAttackPick`、`IssueRetreat`、`IssueDefendOwnCore`、`IssueAllowAbandon`、`IssueMaintainReserve`、`IssueReturnToAuto`、`CancelGroundPick` をボタンから呼びます。従来の IMGUI も同じメソッドを呼ぶため、命令の組み立てと送信は一重です。最終的な送信は従来どおり `ICommandPort` 経由だけで、UI Toolkit はシミュレーション状態を直接変更しません。

新しい画面が有効な間は `CommandPanel.OnGUI` が命令だけでなく、試合の設定・言語・命令の記録・補給も描きません。命令の矩形は `UiHitAreas.Shared.Register` に登録するため、ボタンの外側を含めて地図の選択へクリックが抜けません。攻撃の地点待ちは既存の `TryConsumeGroundClick` を使い、地面のクリック処理も変えていません。

## 内政の欄

`Resources/Hud/Economy.uxml` と `HudTheme.uss` が、`UiLayout.Economy`（画面下中央の横長の矩形）に内政の欄を作ります。上段は建てる・作る・研究・方針のタブ、下段は選択中タブのアクション一覧です。行は横に詰めず、ボタンと押せない理由を一行に置いて、行が多いときは縦にスクロールします。時代を進める・文明の選択、待機中の村人の割り当て、地面に置く途中の説明、最後の知らせもこの欄に含めます。

`EconomyPanel` の `GetToolkitActions` が、現在の `EconomyView` と既存の判定条件からボタン文言・押せるか・理由を返します。`SelectToolkitTab` と `ExecuteToolkitAction` は IMGUI と UI Toolkit の両方の入口で、実行時には従来どおり `EconomyCommand` を `IEconomyPort` へ送り、表示側からシミュレーションを直接変更しません。建物の場所、ベルト・壁のドラッグ、R による向き、取消は既存の `TryConsumeGroundClick` と `Update` の仕組みを使います。

アクションの VisualElement は ID と行種別が変わったときだけ作り直し、資源量・キュー・研究状態・押せるか・理由・文言は既存行へ反映します。UI Toolkit が有効なときだけ `EconomyPanel.OnGUI` の内政の箱を止め、同じ `UiLayout.Economy` を `UiHitAreas.Shared.Register` に登録します。`rts.hud.toolkit` が未設定または 0 の場合は、従来の IMGUI が既定です。
