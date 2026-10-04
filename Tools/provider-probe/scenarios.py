"""正解のある戦況でJevの答えを確かめる（Issue #87）。

実際の観測（snapshot の出力）の1件を土台に、敵の位置・拠点の持ち主・兵力を加工して
「どう答えるべきか」がほぼ決まる戦況を作り、答えが期待どおりに変わるかを数える。
正解は人（この試験の作成者）の判断であり、ゲーム内の唯一の正解ではない。

  python scenarios.py --snapshots snaps.jsonl --base-tick 2400 --repeat 5 --out result.json --fake
"""
import argparse
import copy
import json
import os
import statistics

import probe


def pt(x, z):
    return {"X": x, "Z": z}


def near(observation, x, z, count):
    """敵（見えている敵と接触記録）を、指定の位置の近くに count 体だけ集める。"""
    o = copy.deepcopy(observation)
    enemies = o["VisibleEnemies"][:count]
    for i, e in enumerate(enemies):
        e["Position"] = pt(x + (i % 5) * 2 - 4, z + (i // 5) * 2 - 4)
    o["VisibleEnemies"] = enemies
    o["Contacts"] = [c for c in o["Contacts"] if c["ContactId"] in {e["ContactId"] for e in enemies}]
    for c in o["Contacts"]:
        e = next(e for e in enemies if e["ContactId"] == c["ContactId"])
        c["LastPosition"] = e["Position"]
    return o


def own_outposts(o):
    for g in o["Objectives"]:
        if g["Kind"] == "Outpost":
            g["IsOwnerKnown"], g["OwnerFactionId"] = True, o["FactionId"]
    return o


def build(base):
    out = {}
    a = own_outposts(near(base, 128, 96, 20))   # 北の拠点に敵20体
    out["A_north_attacked"] = (a, {"focus": "north_outpost"})
    b = own_outposts(near(base, 128, 32, 20))   # 南の拠点に敵20体
    out["B_south_attacked"] = (b, {"focus": "south_outpost"})
    c = own_outposts(near(base, 0, 0, 0))       # 敵は見えず、両拠点は自軍、敵コアのHPは低い
    for g in c["Objectives"]:
        if g["Kind"] == "Core" and g["OwnerFactionId"] != c["FactionId"]:
            g["IsHpKnown"], g["Hp"], g["LastSeenTick"] = True, 600, c["Tick"]
    out["C_enemy_core_weak"] = (c, {"focus": "enemy_core"})
    d = own_outposts(near(base, 128, 96, 30))   # 北の自軍は少数で、敵30体に囲まれる
    for army in d["OwnArmies"]:
        if army["HomeObjective"]["Id"] == 1:
            army["AliveCount"] = 3
    out["D_north_outnumbered"] = (d, {"retreat": "high"})
    e = own_outposts(near(base, 128, 96, 3))    # 北の自軍16体に対して敵は3体
    out["E_north_advantage"] = (e, {"retreat": "low"})
    f = own_outposts(near(base, 128, 96, 16))   # 北の自軍16体に対して敵も16体（境界。期待は中間）
    out["F_north_even"] = (f, {"retreat": "middle"})
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshots", required=True)
    ap.add_argument("--base-tick", type=int, required=True)
    ap.add_argument("--repeat", type=int, default=5)
    ap.add_argument("--out", required=True)
    ap.add_argument("--retreat-instructions", help="retreat 設問の文言を差し替える（設問の書き方の比較用）")
    ap.add_argument("--fake", action="store_true", help="外部通信をせず疑似サーバーで最後まで動かす")
    ap.add_argument("--key-env", default="TYPESAFE_API_KEY")
    args = ap.parse_args()
    if args.retreat_instructions:
        probe.QUESTIONS["retreat"]["instructions"] = args.retreat_instructions
    server = None
    if args.fake:
        server, url = probe.start_fake_server(20, 0)
        key = None
    else:
        key = os.environ.get(args.key_env)
        if not key:
            raise SystemExit(args.key_env + " が未設定です。")
        url = os.environ.get("PROBE_URL", "https://api.typesafe.ai/v1/systemone")
    base = next(json.loads(l) for l in open(args.snapshots, encoding="utf-8") if json.loads(l)["tick"] == args.base_tick)["observation"]

    results = {}
    for name, (obs, expect) in build(base).items():
        rows = []
        for _ in range(args.repeat):
            seconds, status, body = probe.call(url, key, probe.build_request(obs, 30), 30)
            if status != "ok":
                rows.append({"status": status, "seconds": round(seconds, 3)})
                continue
            answers, usage = probe.parse_reply(body)
            choice = answers["focus"]["choice"]
            confidence = answers["focus"]["confidence"]
            noul = answers["retreat"]["noul"]
            expected_focus = expect.get("focus")
            expected_retreat = expect.get("retreat")
            correct = (expected_focus is not None and choice == expected_focus) or (
                expected_retreat == "high" and noul >= 0.5) or (
                expected_retreat == "low" and noul < 0.5) or (
                expected_retreat == "middle" and 0.25 <= noul <= 0.75)
            rows.append({"status": "ok", "seconds": round(seconds, 3), "choice": choice,
                         "confidence": confidence, "probabilities": answers["focus"]["probabilities"],
                         "noul": noul, "input_tokens": usage.get("input_tokens"),
                         "output_tokens": usage.get("output_tokens"), "correct": correct})
        ok = [r for r in rows if r["status"] == "ok"]
        results[name] = {"expect": expect, "n_ok": len(ok), "rows": rows,
                         "accuracy": round(sum(r["correct"] for r in ok) / len(ok), 3) if ok else None,
                         "latency_seconds": {"p50": probe.percentile([r["seconds"] for r in ok], 50),
                                             "p95": probe.percentile([r["seconds"] for r in ok], 95)},
                         "input_tokens_mean": round(statistics.mean(r["input_tokens"] for r in ok if r.get("input_tokens") is not None), 1) if any(r.get("input_tokens") is not None for r in ok) else None,
                         "output_tokens_mean": round(statistics.mean(r["output_tokens"] for r in ok if r.get("output_tokens") is not None), 1) if any(r.get("output_tokens") is not None for r in ok) else None,
                         "choices": {c: [r["choice"] for r in ok].count(c) for c in sorted({r["choice"] for r in ok})},
                         "noul_mean": round(statistics.mean(r["noul"] for r in ok), 3) if ok else None,
                         "confidence_mean": round(statistics.mean(r["confidence"] for r in ok), 3) if ok else None}
        print(name, expect, "正解率", results[name]["accuracy"], "p50", results[name]["latency_seconds"]["p50"],
              "tokens", results[name]["input_tokens_mean"], "/", results[name]["output_tokens_mean"], flush=True)
    all_rows = [r for result in results.values() for r in result["rows"] if r["status"] == "ok"]
    results["summary"] = {"calls": len(all_rows), "accuracy": round(sum(r["correct"] for r in all_rows) / len(all_rows), 3) if all_rows else None,
                           "confidence_mean": round(statistics.mean(r["confidence"] for r in all_rows), 3) if all_rows else None,
                           "latency_seconds": {"p50": probe.percentile([r["seconds"] for r in all_rows], 50),
                                               "p95": probe.percentile([r["seconds"] for r in all_rows], 95)},
                           "input_tokens": sum(r.get("input_tokens") or 0 for r in all_rows),
                           "output_tokens": sum(r.get("output_tokens") or 0 for r in all_rows)}
    json.dump(results, open(args.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
