"""AIプロバイダー（Jev など）の応答時間・失敗率・出力の妥当性を測る計測スクリプト（Issue #87）。

標準ライブラリだけで動く。ゲームの Simulation には触らない。

使い方
  # 疑似サーバーで計測スクリプト自体の動作を確かめる（外部通信なし）
  python probe.py --snapshots snaps.jsonl --fake --concurrency 2 --out result.json

  # 実接続（キーは環境変数から読む。ログにも出力にも書かない）
  PROBE_URL   # 既定：https://ai-gateway.lolipop.jp/v1/systemone （ロリポップ！AIゲートウェイ経由の Jev）
  PROBE_KEY   # APIキー。オーナーが管理し、環境変数だけで渡す
  PROBE_MODEL # 既定：typesafe/jev-latest
  python probe.py --snapshots snaps.jsonl --concurrency 2 --timeout 12 --out result.json

snapshots は1行1件のJSON（`snapshot` コマンドの出力）。
接続先ごとの違いは QUESTIONS / build_request / parse_reply / answer_problems に閉じ込めてある。
Jev は文章を作らず、設問に choice（選択）・noul（はい／いいえの確率）で答える。答えをゲームの命令に変える処理は
ゲーム側の規則で行うため、この計測では「答えが設問の形として妥当か」と「戦況で答えが変わるか」を見る。
"""
import argparse
import json
import os
import statistics
import sys
import threading
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

# Jev への設問。答えの選択肢はゲームの重点目標（北拠点・南拠点・敵コア）に対応する。
QUESTIONS = {
    "focus": {
        "type": "choice",
        "instructions": "この陣営が次に重点を置く目標を選んでください。",
        "criteria": {
            "north_outpost": "北の拠点を守る、または取る",
            "south_outpost": "南の拠点を守る、または取る",
            "enemy_core": "敵のコアを攻める",
        },
    },
    "retreat": {"type": "noul", "instructions": "北の部隊は今すぐ撤退すべきである。"},
}


def build_request(observation, timeout):
    """Jev の /v1/systemone に送る本体。state は観測をそのままJSONで渡す。"""
    return {"model": os.environ.get("PROBE_MODEL", "typesafe/jev-latest"), "state": observation, "questions": QUESTIONS}


def parse_reply(body):
    """返答から answers を取り出す。"""
    data = json.loads(body)
    return data["answers"], data.get("usage", {})


def answer_problems(answers):
    """答えが設問の形として妥当か。問題があれば理由の一覧、なければ空。"""
    problems = []
    for name, q in QUESTIONS.items():
        a = answers.get(name)
        if not isinstance(a, dict):
            problems.append("missing-" + name)
            continue
        if q["type"] == "choice":
            if a.get("choice") not in q["criteria"]:
                problems.append("bad-choice-" + name)
            probs = a.get("probabilities") or {}
            if not probs or abs(sum(probs.values()) - 1.0) > 0.02:
                problems.append("bad-probabilities-" + name)
        elif not isinstance(a.get("noul"), (int, float)) or not 0 <= a["noul"] <= 1:
            problems.append("bad-noul-" + name)
    return problems


def call(url, key, payload, timeout):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    if key:
        req.add_header("Authorization", "Bearer " + key)
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8")
        return time.perf_counter() - start, "ok", body
    except urllib.error.HTTPError as e:
        return time.perf_counter() - start, "http-%d" % e.code, ""
    except TimeoutError:
        return time.perf_counter() - start, "timeout", ""
    except urllib.error.URLError as e:
        kind = "timeout" if isinstance(getattr(e, "reason", None), TimeoutError) else "network"
        return time.perf_counter() - start, kind, ""


def percentile(values, p):
    """nearest-rank（ceil(p*N)）。設計書の測定と同じ定義。"""
    if not values:
        return None
    ordered = sorted(values)
    rank = -(-len(ordered) * p // 100)
    return ordered[max(0, int(rank) - 1)]


def start_fake_server(latency_ms, fail_every):
    """疑似サーバー。応答時間と失敗を一定の規則で作り、計測スクリプトの動作確認に使う。"""
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
    counter = {"n": 0}
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            self.rfile.read(int(self.headers.get("Content-Length", 0)))
            with lock:
                counter["n"] += 1
                n = counter["n"]
            time.sleep(latency_ms / 1000.0)
            if fail_every and n % fail_every == 0:
                self.send_response(503); self.end_headers(); return
            answers = {"focus": {"type": "choice", "choice": "south_outpost", "confidence": 0.3,
                                 "probabilities": {"north_outpost": 0.2, "south_outpost": 0.5, "enemy_core": 0.3}},
                       "retreat": {"type": "noul", "noul": 0.2}}
            if n % 7 == 0:  # 一部わざと不正な答えを混ぜ、妥当性の判定を確かめる
                answers["focus"]["choice"] = "teleport"
            body = json.dumps({"model": "fake", "answers": answers, "usage": {"input_tokens": 100, "output_tokens": 10}}).encode("utf-8")
            self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

        def log_message(self, *args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, "http://127.0.0.1:%d/" % server.server_address[1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshots", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--concurrency", type=int, default=1)
    ap.add_argument("--timeout", type=float, default=12.0, help="秒。既定はゲームの締切240tick＝12秒")
    ap.add_argument("--repeat", type=int, default=1, help="同じ入力を繰り返す回数（出力のばらつきの確認用）")
    ap.add_argument("--fake", action="store_true")
    ap.add_argument("--fake-latency-ms", type=float, default=120)
    ap.add_argument("--fake-fail-every", type=int, default=0)
    args = ap.parse_args()

    if args.fake:
        server, url = start_fake_server(args.fake_latency_ms, args.fake_fail_every)
        key = None
    else:
        url = os.environ.get("PROBE_URL", "https://ai-gateway.lolipop.jp/v1/systemone")
        key = os.environ.get("PROBE_KEY")
        if not key:
            sys.exit("PROBE_KEY が未設定です（キーはオーナーが環境変数で渡す）。")

    snaps = [json.loads(line) for line in open(args.snapshots, encoding="utf-8") if line.strip()]
    jobs = [(s, r) for s in snaps for r in range(args.repeat)]

    def run(job):
        snap, rep = job
        payload = build_request(snap["observation"], args.timeout)
        size = len(json.dumps(payload))
        seconds, status, body = call(url, key, payload, args.timeout)
        problems, choice, noul, confidence, usage = [], None, None, None, {}
        if status == "ok":
            try:
                answers, usage = parse_reply(body)
                problems = answer_problems(answers)
                choice = (answers.get("focus") or {}).get("choice")
                confidence = (answers.get("focus") or {}).get("confidence")
                noul = (answers.get("retreat") or {}).get("noul")
            except (ValueError, KeyError, TypeError):
                problems = ["unparseable"]
        return {"tick": snap["tick"], "phase": snap.get("phase", ""), "faction": snap.get("faction"), "repeat": rep,
                "bytes": size, "seconds": round(seconds, 4), "status": status, "choice": choice, "confidence": confidence, "noul": noul,
                "input_tokens": usage.get("input_tokens"), "problems": problems,
                "reply": body if rep == 0 and args.repeat > 1 else None}

    wall = time.perf_counter()
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        rows = list(pool.map(run, jobs))
    wall = time.perf_counter() - wall

    ok = [r for r in rows if r["status"] == "ok"]
    times = [r["seconds"] for r in ok]
    by_phase = {}
    for r in ok:
        by_phase.setdefault(r["phase"], []).append(r["seconds"])
    statuses = {}
    for r in rows:
        statuses[r["status"]] = statuses.get(r["status"], 0) + 1
    valid = [r for r in ok if not r["problems"]]
    summary = {
        "calls": len(rows), "concurrency": args.concurrency, "timeout_s": args.timeout, "wall_s": round(wall, 2),
        "status_counts": statuses, "failure_rate": round(1 - len(ok) / len(rows), 4),
        "usable_rate": round(len(valid) / len(rows), 4),
        "latency_s": {"p50": percentile(times, 50), "p95": percentile(times, 95), "p99": percentile(times, 99),
                      "max": max(times) if times else None, "mean": round(statistics.mean(times), 4) if times else None},
        "latency_by_phase_p50": {k: percentile(v, 50) for k, v in sorted(by_phase.items())},
        "request_bytes": {"min": min(r["bytes"] for r in rows), "max": max(r["bytes"] for r in rows)},
        "problem_counts": {},
        "choice_counts": {},
        "input_tokens": {"min": min([r["input_tokens"] for r in ok if r["input_tokens"]] or [None]),
                         "max": max([r["input_tokens"] for r in ok if r["input_tokens"]] or [None])},
        "retreat_noul": {"min": min([r["noul"] for r in ok if r["noul"] is not None] or [None]),
                         "max": max([r["noul"] for r in ok if r["noul"] is not None] or [None])},
    }
    for r in ok:
        summary["choice_counts"][r["choice"]] = summary["choice_counts"].get(r["choice"], 0) + 1
    # 戦況が変わっても答えが変わらないなら、設問が戦況を見ていない可能性がある
    summary["choice_by_phase"] = {}
    for r in ok:
        d = summary["choice_by_phase"].setdefault(r["phase"], {})
        d[r["choice"]] = d.get(r["choice"], 0) + 1
    confs = [r["confidence"] for r in ok if r["confidence"] is not None]
    summary["focus_confidence"] = {"min": min(confs) if confs else None, "max": max(confs) if confs else None,
                                   "mean": round(statistics.mean(confs), 3) if confs else None}
    for r in ok:
        for p in r["problems"]:
            summary["problem_counts"][p] = summary["problem_counts"].get(p, 0) + 1
    json.dump({"summary": summary, "rows": rows}, open(args.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(json.dumps(summary, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
