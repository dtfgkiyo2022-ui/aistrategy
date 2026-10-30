# aibelt4 診断報告

実施日：2026-10-30

## 実装

`AiBeltParts` がオンのときだけ、`Simulation` 内に診断専用カウンターを追加した。状態ハッシュ・正規状態・判定結果には入れていない。オフのときはカウンターを更新せず、`aibeltdiag:` 行も出力しない。

`tactic-match` の最後に陣営ごとの `aibeltdiag:` 行を追加した。配列欄の順序は次のとおり。

| 欄 | 順序 |
|---|---|
| `routeLen` | 〜10 / 11〜20 / 21〜30 / 31〜40 / 41〜60 / 61〜 |
| `routeRatio` | 〜1.2 / 〜1.5 / 〜2 / 2超 |
| `undergroundReasons` | その他 / 入口・出口が取れない / 入口までの道が無い / 出口からの道が無い / 地下の方が長いか同じ / 払えない / 判断の上限 |
| `catapultReasons` | その他 / 発射台の場所が無い / 受け台が無い / 受け台からの道が無い / 半分以下にならない / 払えない / 判断の上限 |
| `destinations` | 核 / 投下所 / その他 |
| `destinationDistance` | 0〜9 / 10〜19 / 20〜29 / 30〜39 / 40〜49 / 50〜 |
| `mergeReasons` | taken / Held / 部品付き / MergerSide が取れない / そこから行き先に届かない |

## 種1測定

条件：`--map-seed 2 --terrain --map-size large --ticks 40000`。終了コードは0、決着は tick 25248、勝者は陣営1。

実際の最終行：

```text
aibeltdiag: faction=1 decisions=252 normalFound=110 normalMissing=1 normalTooLong=8 routeLen=101,8,0,0,1,0 routeRatio=98,10,2,0 undergroundTried=2 undergroundReasons=0,1,0,0,0,0,0 catapultAge=41 catapultShort=69 catapultTried=1 catapultReasons=0,0,0,0,0,1,0 destinations=2,36,72 destinationDistance=75,34,0,0,1,0 mergeAdjacent=746155 mergeReasons=81220,0,0,352179,291856 merged=20900
aibeltdiag: faction=2 decisions=252 normalFound=219 normalMissing=0 normalTooLong=0 routeLen=102,103,13,0,1,0 routeRatio=112,107,0,0 undergroundTried=2 undergroundReasons=0,1,0,0,0,0,0 catapultAge=40 catapultShort=179 catapultTried=0 catapultReasons=0,0,0,0,0,0,0 destinations=2,45,172 destinationDistance=47,161,10,0,1,0 mergeAdjacent=407330 mergeReasons=16104,0,0,205992,164651 merged=20583
```

部品配置数は両陣営とも全9種類が0だった。

## そこから読めること

- 地下ベルトは両陣営とも2回試したが、いずれも `入口・出口が取れない` で終わった。通常経路が無い場合に地下候補へ進んだものの、候補の入口・出口の組を作れていない。
- 投石機は陣営1で1回試し、`払えない` が1回。陣営2は試行0で、年齢不足40回、通常経路が短い179回だった。陣営1も年齢不足41回、短経路69回で、試行に入る前の最大の理由は通常経路が40以下であることだった。
- 通常経路は陣営1で110件、陣営2で219件が見つかり、長さ超過は陣営1の8件だけだった。したがって「通常経路が無い・長すぎる」入口自体が実戦では少ない。
- 合流候補の検査は非常に多く、到達可能と数えた候補は陣営1で20900、陣営2で20583。ただし実際の合流機配置数は0だった。診断上は合流候補が無いのではなく、候補の後段（配置・費用・線全体の採用）まで含めて合流機になっていない。`MergerSide が取れない` と「そこから行き先に届かない」が多い。

以上から、今回の実戦で部品が使われない一番大きな理由は、採り場線の通常経路が短く、地下・投石機の試行条件に達しないことと見立てられる。合流機は候補検査までは到達しているため、別途、合流候補を選んだ後に配置されない経路を追う必要がある。これは診断からの見立てであり、判定変更は行っていない。

## テスト・旗オフ照合

| 項目 | 結果 |
|---|---|
| Release ビルド | 成功、警告0、エラー0 |
| `AiBeltPartsTests` | 7 / 7 成功 |
| `ScenarioExtensionTests` | 11 / 11 成功 |
| 旗オフ base `tactic-match` | 終了コード0、決着 tick 25442、勝者 陣営1 |
| 変更後 CLI `replay` | 終了コード0 |
| replay 最終行 | `Replay matched S0..S25442` |

照合用の `x.rpl`、`y.hashes` と一時ログは確認後に削除した。

## 確認できていないこと

- 種2・種3の実戦測定は今回行っていない。
- 合流候補が数えられた後に実配置へ進まない全ての理由は、今回の数え欄だけでは分離できていない。
- 全テストスイートは実行していない。

直し方の案としては、合流候補を採用しなかった後段を「合流機の費用」「線全体の `LayRoutes` 成功」「別候補への置き換え」に分けて数えること、地下候補について入口・出口候補生成時の各失敗をさらに分けることが考えられる。今回は実装しない。
