import os
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from practice.rts_practice import RtsEnv


class RtsEnvTests(unittest.TestCase):
    def test_reset_step_five_close(self):
        root = Path(__file__).resolve().parents[2]
        cli = os.environ.get("RTS_CLI") or str(root / "Headless" / "Rts.Headless.Cli" / "bin" / "Release" / "net10.0" / "Rts.Headless.Cli.dll")
        if not os.environ.get("RTS_CLI") and not Path(cli).exists():
            self.skipTest("Release CLI が未ビルドです。README の手順でビルドしてください")
        env = RtsEnv(cli, max_ticks=12000)
        try:
            observation, info = env.reset(seed=123)
            self.assertEqual(observation["version"], 1)
            self.assertEqual(info["tick"], 0)
            for _ in range(5):
                observation, reward, terminated, truncated, step_info = env.step([])
                self.assertEqual(observation["version"], 1)
                self.assertGreater(step_info["tick"], 0)
                if terminated or truncated:
                    break
        finally:
            env.close()


if __name__ == "__main__":
    unittest.main()

