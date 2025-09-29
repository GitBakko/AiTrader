import sys
import unittest
from pathlib import Path

import numpy as np
import pandas as pd

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
if str(PACKAGE_ROOT) not in sys.path:
    sys.path.append(str(PACKAGE_ROOT))

from indicators import IndicatorConfig, compute_indicator_frame


class IndicatorPipelineTests(unittest.TestCase):
    def setUp(self) -> None:
        timestamps = pd.date_range("2024-01-01", periods=120, freq="1min", tz="UTC")
        self.df = pd.DataFrame(
            {
                "timestamp": timestamps,
                "open": np.linspace(100, 110, len(timestamps)),
                "high": np.linspace(101, 111, len(timestamps)),
                "low": np.linspace(99, 109, len(timestamps)),
                "close": np.linspace(100, 112, len(timestamps)) + np.sin(np.linspace(0, 10, len(timestamps))),
                "volume": np.random.randint(1_000, 5_000, size=len(timestamps)),
            }
        )

    def test_compute_indicator_frame(self):
        frame = compute_indicator_frame(self.df, config=IndicatorConfig())
        required_columns = {
            "ema200",
            "sma20",
            "rsi7",
            "rsi14",
            "atr14",
            "vwap",
            "vwap_residual",
            "sigma",
            "spread",
            "minutes_from_open",
        }
        self.assertTrue(required_columns.issubset(frame.columns))
        self.assertFalse(frame[["ema200", "sma20", "rsi7", "atr14"]].isna().all().any())


if __name__ == "__main__":
    unittest.main()
