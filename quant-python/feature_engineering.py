"""Feature engineering pipeline aligning with FREE-mode strategies."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Iterable, Optional, Sequence

import numpy as np
import pandas as pd

try:  # pragma: no cover - allow execution as script
    from .indicators import IndicatorConfig, compute_indicator_frame
except ImportError:  # pragma: no cover - fallback when package context missing
    from indicators import IndicatorConfig, compute_indicator_frame


@dataclass(frozen=True)
class LabelConfig:
    horizon: int = 12  # bars
    long_threshold: float = 0.003
    short_threshold: float = -0.003


@dataclass(frozen=True)
class FeaturePipelineConfig:
    indicator: IndicatorConfig = field(default_factory=IndicatorConfig)
    lags: Sequence[int] = (1, 5, 15)
    rolling_windows: Sequence[int] = (5, 15, 30)
    label: LabelConfig = field(default_factory=LabelConfig)


class FeaturePipeline:
    """Produce deterministic features/labels for quant research."""

    def __init__(self, config: Optional[FeaturePipelineConfig] = None):
        self.config = config or FeaturePipelineConfig()

    def transform(self, df: pd.DataFrame) -> pd.DataFrame:
        enriched = compute_indicator_frame(df, config=self.config.indicator)
        enriched = self._add_lagged_features(enriched)
        enriched = self._add_rolling_stats(enriched)
        enriched = self._add_labels(enriched)
        return enriched

    def _add_lagged_features(self, df: pd.DataFrame) -> pd.DataFrame:
        for lag in self.config.lags:
            df[f"return_lag_{lag}"] = df["returns"].shift(lag)
            df[f"z_residual_lag_{lag}"] = df["z_residual"].shift(lag)
            df[f"sigma_lag_{lag}"] = df["sigma"].shift(lag)
        return df

    def _add_rolling_stats(self, df: pd.DataFrame) -> pd.DataFrame:
        for window in self.config.rolling_windows:
            name = f"roll_mean_return_{window}"
            df[name] = df["returns"].rolling(window=window, min_periods=1).mean()
            df[f"roll_std_return_{window}"] = df["returns"].rolling(window=window, min_periods=1).std(ddof=0)
        return df

    def _add_labels(self, df: pd.DataFrame) -> pd.DataFrame:
        horizon = self.config.label.horizon
        future_price = df["close"].shift(-horizon)
        current_price = df["close"]
        future_return = (future_price - current_price) / current_price.replace(0, np.nan)
        df["future_return"] = future_return

        label = np.zeros(len(df), dtype=int)
        label[future_return >= self.config.label.long_threshold] = 1
        label[future_return <= self.config.label.short_threshold] = -1
        df["label"] = label
        return df


def make_features(df: pd.DataFrame, *, pipeline: Optional[FeaturePipeline] = None) -> pd.DataFrame:
    pipeline = pipeline or FeaturePipeline()
    return pipeline.transform(df)

