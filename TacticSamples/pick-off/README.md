# pick-off

`pick-off` は、`ownArmies[].composition` の自軍兵数を合計し、現在可視の接触だけを候補にします。候補の `visibleComposition` が示す兵数が自軍より小さく、`visibleEnemies[].kindName` で確認した Archer が3分の1以下の接触を選び、全軍で `Focus` します。

攻撃を選んだら `commitTicks` の間は判断を変えません。接触が見えなくなっても最後に見た地点へ向かいます。攻撃期限が切れた時点で候補がなければ `retreatTicks` の間は `Retreat` に固定し、その後に再評価します。これにより、接触の見え隠れだけで攻める・引くを繰り返さないようにします。

`SIZE_MARGIN`、`PICK_PRIORITY`、`RETREAT_PRIORITY` はコード上の定数です。`commitTicks` と `retreatTicks` は `tactic.json` から調整できます。

## 測定結果

実行コマンド（西 `pick-off` ／東 `auto`）:

`dotnet Headless/Rts.Headless.Cli/bin/Release/net10.0/Rts.Headless.Cli.dll tactic-match --map-seed 1 --ticks 12000 --west-tactic TacticSamples/pick-off --east-tactic auto`

| map seed | 西軍の結果 | tick |
|---:|---|---:|
| 1 | 未決着 | 12000 |
| 2 | 東軍勝利 | 5402 |
| 3 | 東軍勝利 | 5050 |
| 4 | 未決着 | 12000 |

上表は改良前の記録です。種2では1080 tick以降、「小さい接触へ集中」と「後退」を約350 tickごとに繰り返していました。

## 改良後の測定結果

実行条件は `--ticks 12000`、`commitTicks=600`、`retreatTicks=300` です。8試合を実行し、結果を下表に記録します。

| 配置 | map seed | 結果 | 決着 tick | 決着の分 |
|---|---:|---|---:|---:|
| 西 `pick-off` ／ 東 auto | 1 | 未決着 | 12000 | 10.00 |
| 西 `pick-off` ／ 東 auto | 2 | 未決着 | 12000 | 10.00 |
| 西 `pick-off` ／ 東 auto | 3 | 未決着 | 12000 | 10.00 |
| 西 `pick-off` ／ 東 auto | 4 | 未決着 | 12000 | 10.00 |
| 西 auto ／ 東 `pick-off` | 1 | 未決着 | 12000 | 10.00 |
| 西 auto ／ 東 `pick-off` | 2 | 未決着 | 12000 | 10.00 |
| 西 auto ／ 東 `pick-off` | 3 | 未決着 | 12000 | 10.00 |
| 西 auto ／ 東 `pick-off` | 4 | 未決着 | 12000 | 10.00 |

集計は勝ち0、負け0、未決着8、決着0試合です。改良前に記録されていた西 `pick-off` ／ 東 auto の4試合（勝ち0、負け2、未決着2）と比べると、敗北はなくなりましたが、勝ちに持ち込む力も不足しています。改良前の東西入替の4試合は記録がないため、同条件での直接比較はできません。

## 効いたこと・効かなかったこと

- 効いたこと：攻撃中は `commitTicks`、後退中は `retreatTicks` が経過するまで判断を固定し、接触が一時的に消えても最後に見た地点へ向かうため、見え隠れによる短周期の攻め・引きの往復を止められました。8試合で敗北が発生しなかったことも確認できました。
- 効かなかったこと：後退時間を置くぶん攻撃の再開が遅く、攻撃地点も最後に見た点のままです。全試合が未決着で、auto のコアを破壊する決定力は得られませんでした。`commitTicks` と `retreatTicks` の値調整や、接触を見失ったときの再接近方針が次の改善候補です。

## 書きにくかったこと

部隊と兵の対応は Contracts にないので、`composition` は「自軍の兵（村人を除く）を一番近い部隊に数えた」近似です。部隊が近くに重なっていると内訳が混ざります。敵部隊IDを `Focus` の目標に指定する語彙もないため、接触の位置を `Point` 目標にしています。
