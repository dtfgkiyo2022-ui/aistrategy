"""各部隊を無作為に守るか攻めるか選ぶ最小エージェント。"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from practice.actions import defend, focus
from practice.rts_practice import RtsEnv


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cli", help="tactic-gym の DLL/EXE。省略時は RTS_CLI")
    parser.add_argument("--seed", type=int, default=1)
    args = parser.parse_args()
    rng = random.Random(args.seed)
    env = RtsEnv(args.cli)
    try:
        observation, _ = env.reset(seed=args.seed)
        total = 0.0
        while True:
            faction = observation["factionId"]
            enemy = 2 if faction == 1 else 1
            commands = []
            for army in observation["ownArmies"]:
                if rng.choice((True, False)):
                    commands.append(focus(army["id"], "Core", enemy))
                else:
                    commands.append(defend(army["id"], "Core", faction))
            observation, reward, terminated, truncated, info = env.step(commands)
            total += reward
            if terminated or truncated:
                print({"winner": info["winner"], "reward": total, "tick": info["tick"]})
                break
    finally:
        env.close()


if __name__ == "__main__":
    main()

