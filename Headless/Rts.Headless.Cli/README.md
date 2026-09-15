# Headless 記録・再生 CLI

リポジトリ内から .NET 10 SDK で実行します。Unity は不要です。

```sh
dotnet test Headless/Rts.Headless.slnx --configuration Release
dotnet run --project Headless/Rts.Headless.Cli -c Release -- record --scenario TestData/week1-10v10.json --inputs TestData/week1-inputs.json --out run.rtsreplay --ticks 1200
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- replay --in run.rtsreplay --hash-out a.hashes --dump-dir diag-a
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- replay --in run.rtsreplay --hash-out b.hashes --dump-dir diag-b
dotnet run --project Headless/Rts.Headless.Cli -c Release --no-build -- compare --left a.hashes --right b.hashes --replay run.rtsreplay
```

`--inputs` を省くとお任せ（入力なし）。`--ticks` は 0〜シナリオの verificationTickLimit（最大10000000）。S0を含み、勝敗・Faultが先に確定したらそのtickで終了します。打切りは勝利や引分けに変換しません。
終了コードは **0: 正常・一致、2: 状態/EventHash/欠落tickの不一致、3: 形式・版・引数の不一致、4: Fault・I/O障害**。

## 入力 JSON

[完全な例](../../TestData/week1-inputs.json) は40tickで陣営1・軍団1を撤退させます。
トップレベルは `ScheduledInput` の配列。各要素に logIndex (uint64)、kind (byte)、acceptedTick / applyTick (int64)、requestId / issuerSequence (uint64)、orders 配列を指定します。orders は Contracts の PolicyOrder と同じ名前・数値enumで全フィールドを表します。parents は `{ "scope": {"factionId":1,"kind":1,"id":0}, "revision":1 }` の配列です。入力は ApplyTick→LogIndex に整列し、LogIndex重複と範囲外tickを拒否します。

Simulation が実行するのは **Resolve=2 / Proposal=4、軍団単位の Focus=1 / Retreat=3** です。Focusのgoalは Point=1、Outpost=2 または敵 Core=3。Reserve=1 / Cancel=3 もバイナリcodecは全フィールドを往復できますが、実行は既存Simulationの制限に従って拒否します。予約・拒否理由の新しい状態機械は後続Issueの範囲です。未対応の制御入力を黙って捨てることはありません。

座標 `point: {"x":80,"z":64}` は整数メートルです。シナリオは既存 `TestData/week1-10v10.json` 形式です。固定小数点の入力は整数のほか `{"raw":5242880}` または `{"numerator":3,"denominator":2}` を許可します。分数はBigIntegerで計算しゼロ方向に切り捨てます。小数JSON数値・float/double経由の変換は使いません。

## .rtsreplay v1

非圧縮・little endian。整数は宣言幅、boolは0/1のbyte、enumはbyte、Fix64はint64 Raw、文字列はuint32 UTF-8バイト長＋本文、配列はuint32件数＋要素です。文字列1 MiB、ヘッダー/レコード64 MiB、シナリオ/命令配列100000件を上限に検証します。

- ファイル: ASCII `RTSRPL01` (8 bytes)、uint32 schemaVersion=1、uint32 headerLength、header、records。
- ヘッダー: rulesVersion文字列、int32 tickRateHz、uint64 seed、int64 tickLimit、uint32 scenarioLength＋ScenarioBinary、ビルド情報。
- ビルド情報: commit文字列、dirty bool、純C#ソースSHA-256文字列、Editor版文字列、実行バックエンド文字列、package lock SHA-256文字列。
- レコード: uint32 recordLength（自身の4byteを除く）、byte kind、int64 tick、uint64 LogIndex、uint32 payloadLength、payload SHA-256 (32 bytes)、payload。`recordLength=53+payloadLength`。
- Input=1: `InputBinary` がScheduledInput、各PolicyOrder、依存版を宣言順に明示保存。
- TickHash=2: State SHA-256＋Event SHA-256（各32byte）。S0から毎tick。
- DiagnosticCheckpoint=3: 正規状態バイト列。S0と100tickごと。
- End=4: Fault bool。最終tickと最終入力LogIndexはレコードの共通部分。終了後のデータ、終了欠落、tick欠落・重複、未知種別、長さ不整合、SHA不一致は拒否。

ScenarioBinaryは schemaVersion、scenarioId、seed、tickRate、verificationTickLimit、map、rules、兵種、陣営、軍団、兵士、コア、拠点の順です。各型の全設定と全初期配置を保存し、ID順へ正規化します。1週目の次ID初期値は保存した各配列長+1（陣営は3）、乱数はseed、敵プリセットは既存のお任せ1種類で一意に決まります。外部シナリオへのパス参照はありません。

純C#ソース集合はContracts/Decision/Simulation/Replay/Applicationの全.csをビルド時にCLIへ埋め込み、CLI側で相対パスのOrdinal順にuint32 UTF-8パス長＋パス、uint32内容長＋内容を連結してSHA-256を計算します。内容はUTF-8、BOM除去、CRLF→LF。チェックアウト後の改行差やビルド後のソース編集で誤認しません。commit/dirty/Editor/package lockは起動時のリポジトリ情報です。
標準再生はrules/schema/ソースハッシュの不一致を拒否します。意図的な互換性確認だけ `--allow-build-mismatch` を replay/compare に指定できます（rules/schemaは緩和しません）。各実行の識別子はhashesヘッダーに残ります。

## 正規状態と差分

正規状態schema=1は uint32版の後に、名前（uint32 UTF-8長＋本文）、byte型タグ、値を順に書きます。タグ1=byte、2=bool、3=int32、4=uint32、5=int64、6=uint64、7=文字列、8=uint32長付きバイト列。名前もハッシュに含めます。

設定/ルール→tick/勝敗→次ID→乱数状態/回数→ID順の兵士→軍団→拠点→コア→陣営→入力カーソル/命令→接触対応→AIメモリの順です。命令はApplyTick→LogIndex→軍団ID。兵士には墓石、攻撃次tick、移動目標、移動量、撤退等のフラグ、兵種パラメータを含めます。Immutableな初期定義はConfig.Hashで覆います。独立したAIメモリは空です。全可視観測・フレーム・索引・ダメージ集計バッファは状態から再構築するため除外します。将来の霧・予約状態追加時は正規状態schemaとrulesの互換性を見直します。

`.hashes` はUTF-8 JSON Lines。先頭にschemaVersion / ReplayHash / ReplayPath / Build、以降にTick / StateHash / EventHash。対応する `.hashes.states` は各tickの int64 tick、uint32長、正規状態を保存します。**比較時には両ファイルを一緒に運びます**。全tickの診断を保存するためディスク量はtick数に比例します。`--dump-dir` は追加で100tickごとのバイナリと読みやすいテキストを出力します。

compareは最初の不一致/欠落tickを見つけ、元の再生ファイルが利用可能な両実行を先頭からそのtickまで再実行します。`<left>.diff/` にt−1/tの再実行結果と両実行の保存済み診断を出し、最初の異なるフィールドパスと左右の整数/Raw値、両ビルド、入力カーソル、乱数状態を表示します。元ファイルが移動した場合は `--replay` を指紋一致で代用できます。診断側のSHAは各hashes値と照合します。過去のビルドを再実行できなくても、保存された当tickの左右の状態があれば比較できます。状態がなければ不足を明記し、値をハッシュから捏造しません。

EventHashは1週目の公開済み終端イベント列を明示的にハッシュ化します。通常戦闘イベント・フェーズ別ハッシュ・不完全ファイルの部分診断再生は今回追加していません。終了欠落は常にコード3です。

## 2経路マップ

同じrecordコマンドのシナリオを `TestData/week2-2routes.json` にすると40人・2経路・占領ありで実行できます。配置・移動・占領の規則と実行例は [TestData README](../../TestData/README.md) を参照してください。FocusのgoalにOutpost=2も指定できます。ルール版はweek2-1で、旧week1-1再生は版不一致として拒否します。1週目のJSONの再記録は引き続き可能です。

正規状態には軍団のPath（セル列）・PathCursor・PathGoal・HasPathGoal・PathImpossible・AutoStage、および拠点のOwnerFactionId・CapturingFaction・CaptureTicksを追加しています。正規状態のフィールド表現はschema=1を継続し、ルール版で互換性を区別します。
