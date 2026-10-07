"""ヘッドレス RTS のための、Gymnasium 風の小さな Python 環境。"""

from __future__ import annotations

import json
import os
import shlex
import subprocess
from pathlib import Path
from typing import Any

try:
    import gymnasium as gym

    _EnvBase = gym.Env
except ImportError:  # pragma: no cover - optional dependency
    _EnvBase = object


class RtsEnv(_EnvBase):
    """tactic-gym を子プロセスで動かす環境。

    action は TacticCommandReader 版1の commands 配列です。gymnasium と numpy は
    必須ではなく、観測は常に Python の dict で返します。
    """

    metadata = {"render_modes": []}

    def __init__(
        self,
        cli_path: str | os.PathLike[str] | None = None,
        *,
        faction: int = 1,
        opponent: str = "auto",
        max_ticks: int = 12000,
        ticks: int = 20,
        rule_flags: dict[str, bool] | None = None,
        human_orders: str | os.PathLike[str] | None = None,
    ) -> None:
        self.cli_path = str(cli_path or os.environ.get("RTS_CLI", ""))
        if not self.cli_path:
            raise ValueError("CLI の場所を cli_path または RTS_CLI で指定してください")
        if faction not in (1, 2):
            raise ValueError("faction は 1 または 2 です")
        if max_ticks < 1:
            raise ValueError("max_ticks は正数です")
        if not 1 <= ticks <= 600:
            raise ValueError("ticks は1から600です")
        self.faction = faction
        self.opponent = opponent
        self.max_ticks = max_ticks
        self.ticks = ticks
        self.rule_flags = dict(rule_flags or {})
        self.human_orders = str(human_orders) if human_orders is not None else None
        self._process: subprocess.Popen[str] | None = None
        self._last_tick = 0

    def reset(self, *, seed: int | None = None, options: dict[str, Any] | None = None):
        options = dict(options or {})
        if seed is None:
            seed = int(options.pop("map_seed", options.pop("mapSeed", 0)))
        map_seed = int(seed)
        faction = int(options.pop("faction", self.faction))
        opponent = str(options.pop("opponent", self.opponent))
        max_ticks = int(options.pop("max_ticks", options.pop("maxTicks", self.max_ticks)))
        rule_flags = dict(self.rule_flags)
        rule_flags.update(options.pop("rule_flags", options.pop("ruleFlags", {})))
        human_orders = options.pop("human_orders", options.pop("humanOrders", self.human_orders))
        if options:
            raise ValueError(f"未対応の reset options: {', '.join(sorted(options))}")
        self._ensure_process()
        response = self._request(
            {
                "op": "reset",
                "mapSeed": map_seed,
                "faction": faction,
                "opponent": opponent,
                "maxTicks": max_ticks,
                "ruleFlags": rule_flags,
                "humanOrders": str(human_orders) if human_orders else "",
            }
        )
        self._last_tick = int(response["tick"])
        self.faction = faction
        self.opponent = opponent
        self.max_ticks = max_ticks
        self.human_orders = str(human_orders) if human_orders else None
        return response["view"], {"tick": self._last_tick}

    def step(self, action):
        if not isinstance(action, list):
            raise TypeError("action は命令 dict の list です")
        response = self._request({"op": "step", "commands": action, "ticks": self.ticks})
        self._last_tick = int(response["tick"])
        info = dict(response.get("info", {}))
        info.setdefault("tick", self._last_tick)
        terminated = bool(response.get("done", False) and not info.get("timeLimit", False))
        truncated = bool(response.get("done", False) and info.get("timeLimit", False))
        info["winner"] = int(response.get("winner", 0))
        return response["view"], float(response["reward"]), terminated, truncated, info

    def close(self):
        if self._process is None:
            return
        try:
            self._request({"op": "close"})
        except (BrokenPipeError, EOFError, OSError, ValueError):
            pass
        finally:
            process, self._process = self._process, None
            if process.poll() is None:
                process.terminate()
            try:
                process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()

    def _ensure_process(self) -> None:
        if self._process is not None and self._process.poll() is None:
            return
        self._process = subprocess.Popen(
            self._command(), stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=None, text=True, encoding="utf-8", bufsize=1,
        )

    def _command(self) -> list[str]:
        path = Path(self.cli_path)
        if path.suffix.lower() == ".dll":
            return ["dotnet", str(path), "tactic-gym"]
        if path.suffix.lower() == ".exe":
            return [str(path), "tactic-gym"]
        return shlex.split(self.cli_path, posix=False) + ["tactic-gym"]

    def _request(self, request: dict[str, Any]) -> dict[str, Any]:
        if self._process is None or self._process.stdin is None or self._process.stdout is None:
            raise RuntimeError("tactic-gym は起動していません")
        self._process.stdin.write(json.dumps(request, ensure_ascii=False) + "\n")
        self._process.stdin.flush()
        line = self._process.stdout.readline()
        if not line:
            raise EOFError("tactic-gym が応答せず終了しました")
        response = json.loads(line)
        if not response.get("ok", False):
            raise ValueError(response.get("error", "tactic-gym の要求が失敗しました"))
        return response
