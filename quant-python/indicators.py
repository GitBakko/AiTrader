"""Indicator utilities for FREE mode quant workflows.

The goal of this module is to provide deterministic, vectorised indicator
computations that mirror the orchestrator's runtime expectations.  Wherever
possible we favour one-pass EMA/Wilder style calculations in order to avoid
look-ahead bias.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

import numpy as np
import pandas as pd


_EPS = 1e-12


@dataclass(frozen=True)
class IndicatorConfig:
    ema_len: int = 200
    sma_len: int = 20
    atr_len: int = 14
    rsi_fast: int = 7
    rsi_slow: int = 14
    sigma_window: int = 60
    spread_window: int = 15
    residual_window: int = 60


def ema(series: pd.Series, span: int) -> pd.Series:
    return series.ewm(span=span, adjust=False, min_periods=1).mean()


def sma(series: pd.Series, window: int) -> pd.Series:
    return series.rolling(window=window, min_periods=1).mean()


def _wilder_rma(series: pd.Series, length: int) -> pd.Series:
    return series.ewm(alpha=1 / length, adjust=False, min_periods=1).mean()


def rsi(close: pd.Series, length: int = 14) -> pd.Series:
    delta = close.diff().fillna(0.0)
    gains = delta.clip(lower=0.0)
    losses = -delta.clip(upper=0.0)
    avg_gain = _wilder_rma(gains, length)
    avg_loss = _wilder_rma(losses, length)
    rs = avg_gain / (avg_loss.replace(0.0, np.nan))
    rsi_out = 100 - (100 / (1 + rs))
    return rsi_out.clip(lower=0.0, upper=100.0).fillna(50.0)


def true_range(df: pd.DataFrame) -> pd.Series:
    prev_close = df["close"].shift(1)
    ranges = pd.concat(
        [
            df["high"] - df["low"],
            (df["high"] - prev_close).abs(),
            (df["low"] - prev_close).abs(),
        ],
        axis=1,
    )
    return ranges.max(axis=1)


def atr(df: pd.DataFrame, length: int = 14) -> pd.Series:
    tr = true_range(df)
    return _wilder_rma(tr, length)


def vwap(df: pd.DataFrame) -> pd.Series:
    price_volume = (df["close"] * df["volume"]).cumsum()
    volume = df["volume"].cumsum()
    volume = volume.replace(0, np.nan)
    return price_volume / volume


def session_vwap(df: pd.DataFrame, session_col: str) -> pd.Series:
    grouped = df[[session_col, "close", "volume"]].copy()
    grouped["pv"] = grouped["close"] * grouped["volume"]
    grouped["cum_pv"] = grouped.groupby(session_col, sort=False)["pv"].cumsum()
    grouped["cum_vol"] = grouped.groupby(session_col, sort=False)["volume"].cumsum()
    vwap_series = grouped["cum_pv"] / grouped["cum_vol"].replace(0, np.nan)
    vwap_series.index = df.index
    return vwap_series


def rolling_zscore(series: pd.Series, window: int) -> pd.Series:
    mean = series.rolling(window=window, min_periods=1).mean()
    std = series.rolling(window=window, min_periods=1).std(ddof=0).replace(0, np.nan)
    return (series - mean) / std


def slope(series: pd.Series, window: int) -> pd.Series:
    def _calc(window_values: np.ndarray) -> float:
        if len(window_values) < 2:
            return 0.0
        y = window_values
        x = np.arange(len(y))
        x_mean = x.mean()
        y_mean = y.mean()
        denom = ((x - x_mean) ** 2).sum()
        if denom == 0:
            return 0.0
        return float(((x - x_mean) * (y - y_mean)).sum() / denom)

    return series.rolling(window=window, min_periods=2).apply(_calc, raw=True).fillna(0.0)


def compute_indicator_frame(
    df: pd.DataFrame,
    *,
    config: Optional[IndicatorConfig] = None,
    timestamp_col: str = "timestamp",
    bid_col: str = "bid",
    ask_col: str = "ask",
    session_col: str = "session_id",
) -> pd.DataFrame:
    """Return a DataFrame enriched with indicators required by the strategies.

    Args:
        df: Input OHLCV frame. Must include columns ``open``, ``high``, ``low``,
            ``close``, ``volume``. Optional bid/ask columns are used for spread.
        config: Indicator parameters; defaults to FREE mode settings.
        timestamp_col: Column containing timezone-aware timestamps.
        bid_col / ask_col: Optional top-of-book prices.
        session_col: Column grouping bars into trading sessions (e.g. date).

    Returns:
        pandas.DataFrame with indicator columns appended.
    """

    if config is None:
        config = IndicatorConfig()

    required = {"open", "high", "low", "close", "volume"}
    missing = required.difference(df.columns)
    if missing:
        raise ValueError(f"Missing required columns: {sorted(missing)}")

    data = df.copy()
    if timestamp_col not in data.columns:
        data[timestamp_col] = data.index

    data = data.sort_values(timestamp_col).reset_index(drop=True)

    data["ema200"] = ema(data["close"], config.ema_len)
    data["sma20"] = sma(data["close"], config.sma_len)
    data["ema200_slope"] = slope(data["ema200"], window=5)
    data["rsi7"] = rsi(data["close"], config.rsi_fast)
    data["rsi14"] = rsi(data["close"], config.rsi_slow)
    data["atr14"] = atr(data, config.atr_len)

    if session_col not in data.columns:
        data[session_col] = pd.to_datetime(data[timestamp_col]).dt.floor("1D")

    data["vwap"] = session_vwap(data, session_col=session_col)
    residual = data["close"] - data["vwap"]
    data["vwap_residual"] = residual
    data["sigma"] = residual.rolling(window=config.sigma_window, min_periods=5).std(ddof=0)
    data["z_residual"] = rolling_zscore(residual, config.residual_window)

    spread = None
    if bid_col in data.columns and ask_col in data.columns:
        spread = (data[ask_col] - data[bid_col]).clip(lower=0.0)
    else:
        spread = (data["high"] - data["low"]).clip(lower=0.0)

    data["spread"] = spread
    data["spread_median"] = spread.rolling(window=config.spread_window, min_periods=1).median()

    data["returns"] = data["close"].pct_change().fillna(0.0)
    data["log_returns"] = np.log1p(data["returns"]).replace([-np.inf, np.inf], 0.0)

    timestamp = pd.to_datetime(data[timestamp_col])
    session_start = timestamp.groupby(data[session_col]).transform("min")
    minutes_from_open = (timestamp - session_start).dt.total_seconds() / 60.0
    data["minutes_from_open"] = minutes_from_open

    data["trend_ok"] = (np.abs(data["close"] - data["ema200"]) / data["close"].clip(lower=_EPS) >= 5e-4)
    data["volatility_ok"] = data["atr14"] >= (
        data["atr14"].rolling(window=60, min_periods=1).median() * 0.7
    )
    data["spread_ok"] = data["spread"] <= data["spread_median"] * 1.5

    return data

