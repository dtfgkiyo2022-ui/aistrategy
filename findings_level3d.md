# level3d 調査・修正結果

## 結論

地図の向きによる勝敗差は、ユニットの基礎性能ではなく、総攻撃の集合開始とコア前への到着順の差だった。段2では、同じ AI 判断でも地形の向きによって一方の軍が自陣側に残り、相手軍だけが中間地点・敵コア前へ先着する。

段3だけに、観測情報に基づく次の判断を追加した。

- tick 9000 以降、観測できる自軍の生存軍勢が100以上なら総攻撃の集合を開始する。
- 自軍コアから60m以内に観測した敵の非農民・非斥候がいる間は、自動軍の目的地を自軍コアへ戻す。
- 通常の共有経路が壁で詰まる場合は、破城可能経路の最初の閉塞セル手前を段3の集合地点にする。

いずれも `AiLevelAtLeast(faction, 3)` の段3分岐内で、観測済み情報だけを使う。ユニット性能、価格、体力、速度、コスト、採集量、地形生成は変更していない。

## 再現計測

全ケースは `tactic-match --terrain --map-size large --ticks 40000 --west-tactic auto --east-tactic auto` で実行した。

段2対段2の基準では、winner=1 になる seed 2/3/5/10 について、tick 12000 の軍勢は次の状態だった。

| seed | 西側 | 東側 | 地形・軍勢の差 |
|---:|---|---|---|
| 2 | 238 fighters、home 33、敵コア前 44、中間 161 | 108 fighters、home 83、中間 25 | 東側が自陣に多く残り、西側が先着 |
| 3 | 161 fighters、home 33、中間 128 | 135 fighters、home 33、中間 102 | 西側の中間到達が早い |
| 5 | 200 fighters、home 13、中間 187 | 183 fighters、home 14、中間 169 | 西側が先行 |
| 10 | 214 fighters、home 21、敵コア前 6、中間 187 | 66 fighters、home 12、中間 54 | 西側が大幅に先行 |

tick 12000 の通常経路長と破城可能経路長も確認したが、各 seed では同値だった（seed 2/3 は118、seed 5 は108、seed 10 は130）。したがって、この再現で勝敗を分けた主因は経路長そのものではなく、集合開始・軍勢配置・到着タイミングだった。最終的な要塞状態でも、seed 2/3/5/10 で ringGaps や breachUses に差があり、先着側が敵要塞の開口部を利用していた。

## 実装

- `Simulation.LatePush.cs`
  - 段3限定の早期総攻撃ラッチ。
  - 段3限定のコア脅威時の帰還判断。
  - 段3限定の破城可能経路を使った集合地点選択。
- `StageThreeWaveTests.cs`
  - 実シミュレーションで段3が最初の破城地点へ集合することを検証。
  - 段0〜2では従来のコア集合を維持することを検証。
- `Rts.Core.Tests.csproj`
  - 上記テストを Compile allowlist に追加。

## 最終 CLI 検証

### 段3を敗北側に置いた seed 2/3/5/10

```
[seed2_w2_e3]
tactic-match: ticks=15106 ended=True winner=1 westAiLevel=2 eastAiLevel=3
outcome: reason=core-destroyed tick=15106 core1Hp=6000 core1Attackers=0 core2Hp=0 core2Attackers=103

[seed3_w3_e2]
tactic-match: ticks=15242 ended=True winner=2 westAiLevel=3 eastAiLevel=2
outcome: reason=core-destroyed tick=15242 core1Hp=0 core1Attackers=69 core2Hp=5914 core2Attackers=0

[seed5_w2_e3]
tactic-match: ticks=12343 ended=True winner=2 westAiLevel=2 eastAiLevel=3
outcome: reason=core-destroyed tick=12343 core1Hp=0 core1Attackers=41 core2Hp=6000 core2Attackers=0

[seed10_w2_e3]
tactic-match: ticks=13654 ended=True winner=1 westAiLevel=2 eastAiLevel=3
outcome: reason=core-destroyed tick=13654 core1Hp=6000 core1Attackers=0 core2Hp=0 core2Attackers=70
```

### seed 1/4、段3対段2の両向き

```
[seed1_w3_e2]
tactic-match: ticks=14724 ended=True winner=2 westAiLevel=3 eastAiLevel=2
outcome: reason=core-destroyed tick=14724 core1Hp=0 core1Attackers=195 core2Hp=5511 core2Attackers=0

[seed1_w2_e3]
tactic-match: ticks=14052 ended=True winner=1 westAiLevel=2 eastAiLevel=3
outcome: reason=core-destroyed tick=14052 core1Hp=4377 core1Attackers=0 core2Hp=0 core2Attackers=219

[seed4_w3_e2]
tactic-match: ticks=15668 ended=True winner=2 westAiLevel=3 eastAiLevel=2
outcome: reason=core-destroyed tick=15668 core1Hp=0 core1Attackers=22 core2Hp=5573 core2Attackers=0

[seed4_w2_e3]
tactic-match: ticks=12134 ended=True winner=2 westAiLevel=2 eastAiLevel=3
outcome: reason=core-destroyed tick=12134 core1Hp=0 core1Attackers=33 core2Hp=6000 core2Attackers=0
```

### 既存基準の維持

```
tactic-match: ticks=15191 ended=True winner=1 westAiLevel=1 eastAiLevel=2
outcome: reason=core-destroyed tick=15191 core1Hp=5559 core1Attackers=63 core2Hp=0 core2Attackers=212
```

## テスト・ビルド

追加テストは 4 件すべて合格した。ビルド・テストには常に次のオプションを付けた。

`-p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false`

変更ファイル:

- `UnityProject/Assets/RTS/Simulation/Simulation.LatePush.cs`
- `Headless/Rts.Core.Tests/StageThreeWaveTests.cs`
- `Headless/Rts.Core.Tests/Rts.Core.Tests.csproj`
- `findings_level3d.md`
