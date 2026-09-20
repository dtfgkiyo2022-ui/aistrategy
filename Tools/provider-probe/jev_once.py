"""Jev に戦況を1件だけ送り、返答の実際の形を確かめる（Issue #87）。

キーは環境変数 PROBE_KEY から読む。画面にも結果ファイルにも書かない。
接続先は PROBE_URL（未設定ならロリポップ！AIゲートウェイの既定）。

  python jev_once.py D:/rts-verify/87/once.json
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

url = os.environ.get("PROBE_URL", "https://ai-gateway.lolipop.jp/v1/systemone")
key = os.environ.get("PROBE_KEY")
if not key:
    sys.exit("PROBE_KEY が未設定です。")

body = {
    "model": os.environ.get("PROBE_MODEL", "typesafe/jev-latest"),
    "state": "西軍（自軍）は3軍団40人。北の拠点は自軍が確保済み、南の拠点は中立。敵は北の拠点の近くに約20人を確認、コアHPは自軍3000・敵3000。",
    "questions": {
        "focus": {
            "type": "choice",
            "instructions": "次に重点を置く目標を選んでください",
            "criteria": {"north_outpost": "北の拠点を守る", "south_outpost": "南の拠点を取る", "enemy_core": "敵のコアを攻める"},
        },
        "retreat": {"type": "noul", "instructions": "北の部隊は今すぐ撤退すべきである"},
    },
}
req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"),
                             headers={"Content-Type": "application/json", "Authorization": "Bearer " + key})
start = time.perf_counter()
try:
    with urllib.request.urlopen(req, timeout=30) as resp:
        status, text = resp.status, resp.read().decode("utf-8")
except urllib.error.HTTPError as e:
    status, text = e.code, e.read().decode("utf-8", "replace")
seconds = round(time.perf_counter() - start, 3)
result = {"url": url, "status": status, "seconds": seconds, "request": body, "response_text": text}
out = sys.argv[1] if len(sys.argv) > 1 else "once.json"
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
json.dump(result, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("status", status, "seconds", seconds)
print(text[:1500])
