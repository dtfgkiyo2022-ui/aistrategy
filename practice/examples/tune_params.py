"""戦術の数値パラメータを、短い対戦で探索する最小の進化的探索。"""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import math
import random
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Any, Callable, Iterable


_RESULT_RE = re.compile(r"ticks=(?P<ticks>\d+)\s+ended=(?P<ended>True|False)\s+winner=(?P<winner>\d+)")


def _number(value: Any) -> int | float:
    return int(value) if isinstance(value, int) and not isinstance(value, bool) else float(value)


def candidate_values(param: dict[str, Any]) -> list[int | float]:
    """params の min/max/step から、再現可能な全候補を作る。"""
    if param.get("type") not in ("int", "number"):
        raise ValueError("tune_params.py は int / number のパラメータに対応します")
    low, high, step = _number(param["min"]), _number(param["max"]), _number(param["step"])
    if step <= 0 or high < low:
        raise ValueError("パラメータの min/max/step が不正です")
    values: list[int | float] = []
    value = low
    while value <= high + step * 1e-9:
        values.append(int(value) if param["type"] == "int" else value)
        value += step
    return values


def _ordered_unique(values: Iterable[int | float]) -> list[int | float]:
    return list(dict.fromkeys(values))


def next_generation(
    current: list[int | float], all_values: list[int | float], scores: dict[int | float, dict[str, Any]],
    rng: random.Random, population: int,
) -> list[int | float]:
    """上位候補の近傍を中心に、(1+lambda) 風に次世代を作る。"""
    ranked = sorted(current, key=lambda v: _score_key(scores[v]), reverse=True)
    elite = ranked[: max(1, min(2, len(ranked)))]
    index = {value: i for i, value in enumerate(all_values)}
    proposed: list[int | float] = [elite[0]]
    for parent in elite:
        i = index[parent]
        for delta in (-2, -1, 1, 2):
            if 0 <= i + delta < len(all_values):
                proposed.append(all_values[i + delta])
    while len(proposed) < population:
        proposed.append(rng.choice(all_values))
    return _ordered_unique(proposed)[:population]


def _score_key(score: dict[str, Any]) -> tuple[float, int, float]:
    # 勝ち数を第一に、未決着を避け、平均決着tickの短さを第三にする。
    return (float(score["wins"]), -int(score["undecided"]), -float(score["averageDecisionTick"]))


def _run_match(command: list[str], tactic_dir: str, param: str, value: int | float,
               seed: int, west: bool, ticks: int) -> dict[str, Any]:
    side = "--west" if west else "--east"
    other = "--east-tactic" if west else "--west-tactic"
    own = "--west-tactic" if west else "--east-tactic"
    args = command + ["tactic-match", "--map-seed", str(seed), "--ticks", str(ticks), own, tactic_dir,
                      other, "auto", side + "-param", f"{param}={value}"]
    completed = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", check=False)
    match = _RESULT_RE.search(completed.stdout)
    if completed.returncode != 0 or match is None:
        raise RuntimeError(f"tactic-match に失敗しました (seed={seed}, value={value}): {completed.stderr.strip()}")
    ticks, ended, winner = int(match["ticks"]), match["ended"] == "True", int(match["winner"])
    faction = 1 if west else 2
    return {"seed": seed, "side": "west" if west else "east", "wins": int(ended and winner == faction),
            "ended": ended, "winner": winner, "decisionTick": ticks if ended else None}


def evaluate_value(command: list[str], tactic_dir: str, param: str, value: int | float,
                   seeds: list[int], jobs: int, ticks: int = 12000) -> dict[str, Any]:
    jobs = max(1, int(jobs))
    tasks = [(seed, west) for seed in seeds for west in (True, False)]
    with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
        futures = [pool.submit(_run_match, command, tactic_dir, param, value, seed, west, ticks) for seed, west in tasks]
        matches = [future.result() for future in futures]
    decisions = [m["decisionTick"] for m in matches if m["decisionTick"] is not None]
    wins = sum(m["wins"] for m in matches)
    return {"value": value, "wins": wins, "matches": len(matches),
            "undecided": len(matches) - len(decisions),
            "averageDecisionTick": (sum(decisions) / len(decisions) if decisions else math.inf),
            "matchResults": matches}


def search(parameter: dict[str, Any], evaluate: Callable[[int | float], dict[str, Any]],
           generations: int, population: int, seed: int) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    """評価関数を差し替えてテストできる、決定的な探索本体。"""
    all_values = candidate_values(parameter)
    population = max(1, min(population, len(all_values)))
    generations = max(1, generations)
    rng = random.Random(seed)
    initial = [parameter["default"]]
    initial.extend(all_values)
    current = _ordered_unique(initial)[:population]
    # 初期値を含めた候補から、seed に依存せず最初の世代を広げる。
    if len(current) < population:
        remaining = [v for v in all_values if v not in current]
        rng.shuffle(remaining)
        current.extend(remaining[: population - len(current)])
    trials: list[dict[str, Any]] = []
    for generation in range(1, generations + 1):
        scores: dict[int | float, dict[str, Any]] = {}
        for value in current:
            result = dict(evaluate(value))
            result.update({"value": value, "generation": generation})
            scores[value] = result
            trials.append(result)
        if generation < generations:
            current = next_generation(current, all_values, scores, rng, population)
    best = max(trials, key=_score_key)
    return trials, best


def _load_param(tactic_dir: Path, name: str) -> tuple[dict[str, Any], dict[str, Any]]:
    metadata_path = tactic_dir / "tactic.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    param = next((item for item in metadata.get("params", []) if item.get("name") == name), None)
    if param is None:
        raise ValueError(f"tactic.json にパラメータ {name!r} がありません")
    return metadata, param


def _apply(tactic_dir: Path, name: str, value: int | float, repo: Path) -> Path:
    output_dir = repo / "practice" / "out" / tactic_dir.name
    shutil.copytree(tactic_dir, output_dir, dirs_exist_ok=True)
    metadata, param = _load_param(output_dir, name)
    param["default"] = value
    (output_dir / "tactic.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n",
                                              encoding="utf-8", newline="\n")
    return output_dir


def main() -> None:
    parser = argparse.ArgumentParser(description="戦術の数値パラメータを対戦結果で探索します")
    parser.add_argument("tactic_dir", help="tactic.json とコードのある戦術フォルダ")
    parser.add_argument("param", help="調整する params の name")
    parser.add_argument("--cli", help="CLIのDLL/EXE。省略時は RTS_CLI")
    parser.add_argument("--seeds", type=int, default=2, help="各値を東西で試すmap seed数")
    parser.add_argument("--generations", type=int, default=3)
    parser.add_argument("--population", type=int, default=5)
    parser.add_argument("--ticks", type=int, default=12000)
    parser.add_argument("--jobs", type=int, default=2)
    parser.add_argument("--seed", type=int, default=20261006, help="探索乱数のseed")
    parser.add_argument("--apply", action="store_true", help="最良値を practice/out のコピーへ適用")
    parser.add_argument("--out", help="結果JSONの保存先")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    tactic_dir = Path(args.tactic_dir).resolve()
    metadata, parameter = _load_param(tactic_dir, args.param)
    cli = args.cli or __import__("os").environ.get("RTS_CLI")
    if not cli:
        raise SystemExit("--cli または RTS_CLI でCLIを指定してください")
    cli_path = Path(cli)
    command = ["dotnet", str(cli_path)] if cli_path.suffix.lower() == ".dll" else [str(cli_path)]
    seeds = list(range(1, max(1, args.seeds) + 1))
    started = time.perf_counter()
    evaluator = lambda value: evaluate_value(command, str(tactic_dir), args.param, value, seeds, args.jobs, args.ticks)
    trials, best = search(parameter, evaluator, args.generations, args.population, args.seed)
    applied_to = str(_apply(tactic_dir, args.param, best["value"], repo)) if args.apply else None
    result = {"tactic": metadata.get("name", tactic_dir.name), "tacticDir": str(tactic_dir),
              "parameter": args.param, "seeds": seeds, "orientations": ["west", "east"],
              "generations": args.generations, "population": args.population, "jobs": args.jobs,
              "elapsedSeconds": round(time.perf_counter() - started, 3), "trials": trials,
              "best": best, "appliedTo": applied_to}
    out = Path(args.out) if args.out else repo / "practice" / "out" / f"tune-{tactic_dir.name}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
                   encoding="utf-8", newline="\n")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
