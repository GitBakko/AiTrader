import sys
import unittest
from pathlib import Path

import numpy as np
import pandas as pd

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
if str(PACKAGE_ROOT) not in sys.path:
    sys.path.append(str(PACKAGE_ROOT))

from feature_engineering import make_features
from strategies import TPBVWAPStrategy, generate_all_signals


class StrategySignalTests(unittest.TestCase):
    def setUp(self) -> None:
        np.random.seed(42)
        timestamps = pd.date_range("2024-01-01", periods=200, freq="1min", tz="UTC")
        close = 100 + np.cumsum(np.random.normal(0, 0.2, len(timestamps)))
        self.df = pd.DataFrame(
            {
                "timestamp": timestamps,
                "open": close - 0.1,
                "high": close + 0.2,
                "low": close - 0.2,
                "close": close,
                "volume": np.random.randint(1_000, 3_000, size=len(timestamps)),
            }
        )

    def test_tpb_generates_signals(self):
        features = make_features(self.df)
        strategy = TPBVWAPStrategy()
        signals = strategy.generate_signals(features, "TEST")
        self.assertIsInstance(signals, list)

    def test_generate_all_signals_combines(self):
        features = make_features(self.df)
        signals = generate_all_signals(features, "TEST")
        self.assertIsInstance(signals, list)


if __name__ == "__main__":
    unittest.main()
