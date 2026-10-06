"""兵力しきい値を数通り試す、標準ライブラリだけの最小探索。"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from practice.actions import defend, focus
from practice.rts_practice import RtsEnv


def run_trial(cli: str | None, threshold: int, seed: int) -> int:
    env = RtsEnv(cli, max_ticks=12000)
    try:
        observation, _ = env.reset(seed=seed)
        while True:
            faction = observation["factionId"]
            enemy = 2 if faction == 1 else 1
            soldiers = sum(army["count"] for army in observation["ownArmies"])
            commands = [
                (focus(army["id"], "Core", enemy) if soldiers > threshold else defend(army["id"], "Core", faction))
                for army in observation["ownArmies"]
            ]
            observation, _, terminated, truncated, info = env.step(commands)
            if terminated or truncated:
                return 1 if info["winner"] == faction else 0
    finally:
        env.close()


def write_threshold(tactic_dir: Path, threshold: int) -> None:
    metadata_path = tactic_dir / "tactic.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    params = next((param for param in metadata.get("params", []) if param.get("name") == "attackThreshold"), None)
    if params is None:
        raise ValueError("tactic.json に attackThreshold params がありません")
    params["default"] = threshold
    metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cli", help="tactic-gym の DLL/EXE。省略時は RTS_CLI")
    parser.add_argument("--episodes", type=int, default=2)
    repo = Path(__file__).resolve().parents[2]
    parser.add_argument("--out", default=str(repo / "practice" / "out" / "best.json"))
    # The repository sample stays as it is: it is copied to practice/out/ and the copy is rewritten.
    parser.add_argument("--tactic-dir", default=str(repo / "practice" / "out" / "threshold-from-json"))
    args = parser.parse_args()
    tactic_dir = Path(args.tactic_dir)
    if not (tactic_dir / "main.js").exists():
        shutil.copytree(repo / "TacticSamples" / "threshold-from-json", tactic_dir, dirs_exist_ok=True)
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    thresholds = (10, 20, 30, 40, 50)
    scores = {threshold: sum(run_trial(args.cli, threshold, seed) for seed in range(1, args.episodes + 1)) for threshold in thresholds}
    best = max(thresholds, key=lambda threshold: (scores[threshold], -threshold))
    result = {"bestThreshold": best, "episodes": args.episodes, "wins": scores[best], "scores": scores}
    Path(args.out).write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    write_threshold(tactic_dir, best)
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
