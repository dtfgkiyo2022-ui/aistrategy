import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from practice.examples.tune_params import search


class TuneParamsTests(unittest.TestCase):
    def test_seeded_search_is_reproducible_with_fake_evaluation(self):
        parameter = {"type": "int", "default": 1, "min": 0, "max": 10, "step": 1}

        def evaluate(value):
            return {"wins": 10 - abs(value - 7), "undecided": 0,
                    "averageDecisionTick": 1000 + abs(value - 7)}

        first = search(parameter, evaluate, generations=4, population=5, seed=42)
        second = search(parameter, evaluate, generations=4, population=5, seed=42)
        self.assertEqual(first, second)
        self.assertEqual(first[1]["value"], 7)
        self.assertEqual([trial["value"] for trial in first[0]],
                         [trial["value"] for trial in second[0]])


if __name__ == "__main__":
    unittest.main()
