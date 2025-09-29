from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Dict, Iterable, List, Optional

import numpy as np
import pandas as pd


class Side(str, Enum):
    BUY = "BUY"
    SELL = "SELL"


@dataclass(frozen=True)
class StrategySignal:
    timestamp: pd.Timestamp
    instrument: str
    strategy: str
    side: Side
    entry: float
    stop: float
    target: float
    score: float
    risk_fraction: float
    metadata: Dict[str, float]


class BaseStrategy:
    name: str = "BASE"

    def __init__(self, *, risk_fraction: float = 0.0025):
        self.risk_fraction = risk_fraction

    def generate_signals(self, df: pd.DataFrame, instrument: str) -> List[StrategySignal]:
        raise NotImplementedError

    def _make_signal(
        self,
        *,
        row: pd.Series,
        instrument: str,
        side: Side,
        entry: float,
        stop: float,
        target: float,
        score: float,
        metadata: Dict[str, float],
    ) -> StrategySignal:
        return StrategySignal(
            timestamp=pd.to_datetime(row["timestamp"]),
            instrument=instrument,
            strategy=self.name,
            side=side,
            entry=float(entry),
            stop=float(stop),
            target=float(target),
            score=float(np.clip(score, 0.0, 3.0)),
            risk_fraction=float(self.risk_fraction),
            metadata={k: float(v) for k, v in metadata.items()},
        )


class TPBVWAPStrategy(BaseStrategy):
    name = "TPB_VWAP"

    def __init__(
        self,
        *,
        pullback_tolerance: float = 0.005,
        stop_atr_multiplier: float = 0.8,
        take_profit_multipliers: Iterable[float] = (1.0, 2.0),
        risk_fraction: float = 0.002,
    ) -> None:
        super().__init__(risk_fraction=risk_fraction)
        self.pullback_tolerance = pullback_tolerance
        self.stop_atr_multiplier = stop_atr_multiplier
        self.take_profit = tuple(take_profit_multipliers)

    def generate_signals(self, df: pd.DataFrame, instrument: str) -> List[StrategySignal]:
        signals: List[StrategySignal] = []
        if df.empty:
            return signals

        for idx in range(1, len(df)):
            row = df.iloc[idx]
            prev = df.iloc[idx - 1]

            if not bool(row.get("trend_ok", True)):
                continue
            if not bool(row.get("volatility_ok", True)) or not bool(row.get("spread_ok", True)):
                continue

            ema200 = row["ema200"]
            sma20 = row["sma20"]
            vwap = row["vwap"]
            atr = row["atr14"]
            price = row["close"]

            if np.isnan([ema200, sma20, vwap, atr, price]).any():
                continue

            dist_sma = abs(price - sma20) / price
            dist_vwap = abs(price - vwap) / price
            min_dist = min(dist_sma, dist_vwap)
            if min_dist > self.pullback_tolerance:
                continue

            rsi_fast = row.get("rsi7", 50.0)
            prev_rsi = prev.get("rsi7", 50.0)

            side: Optional[Side] = None
            stop: float
            target: float

            if price >= ema200 and row.get("ema200_slope", 0.0) >= 0 and prev_rsi <= 35 and rsi_fast > 35:
                side = Side.BUY
            elif price <= ema200 and row.get("ema200_slope", 0.0) <= 0 and prev_rsi >= 65 and rsi_fast < 65:
                side = Side.SELL

            if side is None:
                continue

            stop_distance = max(1e-4, min(atr * self.stop_atr_multiplier, abs(price - sma20)))
            stop = price - stop_distance if side is Side.BUY else price + stop_distance
            target = price + stop_distance * self.take_profit[-1] if side is Side.BUY else price - stop_distance * self.take_profit[-1]

            score = 1.0 - min_dist / self.pullback_tolerance
            metadata = {
                "dist_sma": dist_sma,
                "dist_vwap": dist_vwap,
                "rsi_fast": rsi_fast,
                "atr14": atr,
            }
            signals.append(
                self._make_signal(
                    row=row,
                    instrument=instrument,
                    side=side,
                    entry=price,
                    stop=stop,
                    target=target,
                    score=score,
                    metadata=metadata,
                )
            )

        return signals


class ORB15Strategy(BaseStrategy):
    name = "ORB_15"

    def __init__(
        self,
        *,
        minutes: int = 15,
        buffer_ticks: float = 0.5,
        risk_fraction: float = 0.0025,
    ) -> None:
        super().__init__(risk_fraction=risk_fraction)
        self.minutes = minutes
        self.buffer_ticks = buffer_ticks

    def generate_signals(self, df: pd.DataFrame, instrument: str) -> List[StrategySignal]:
        if df.empty:
            return []

        signals: List[StrategySignal] = []
        session_col = df.get("session_id")
        if session_col is None:
            session_id = pd.to_datetime(df["timestamp"]).dt.floor("1D")
        else:
            session_id = df["session_id"]

        df = df.copy()
        df["session_id"] = session_id

        for session, session_df in df.groupby("session_id", sort=False):
            session_df = session_df.reset_index(drop=True)
            opening = session_df[session_df["minutes_from_open"] < self.minutes]
            if opening.empty:
                continue

            or_high = opening["high"].max()
            or_low = opening["low"].min()
            or_range = max(or_high - or_low, 1e-4)

            buffer_price = self.buffer_ticks * session_df["spread"].rolling(5, min_periods=1).median().iloc[0]
            buffer_price = max(buffer_price, 1e-4)

            for idx in range(len(opening), len(session_df)):
                row = session_df.iloc[idx]
                atr = row.get("atr14", np.nan)
                if np.isnan(atr):
                    continue

                price = row["close"]
                side: Optional[Side] = None
                entry = price
                stop = price
                target = price

                if price > or_high + buffer_price:
                    side = Side.BUY
                    stop_distance = max(1e-4, min(or_range / 2, atr))
                    stop = entry - stop_distance
                    target = entry + or_range
                elif price < or_low - buffer_price:
                    side = Side.SELL
                    stop_distance = max(1e-4, min(or_range / 2, atr))
                    stop = entry + stop_distance
                    target = entry - or_range

                if side is None:
                    continue

                volume_trend = session_df["volume"].iloc[max(idx - 2, 0): idx + 1].diff().dropna().mean()
                score = float(np.clip(or_range / (atr + 1e-6), 0.0, 3.0))
                metadata = {
                    "or_high": or_high,
                    "or_low": or_low,
                    "or_range": or_range,
                    "volume_trend": volume_trend if not np.isnan(volume_trend) else 0.0,
                }

                signals.append(
                    self._make_signal(
                        row=row,
                        instrument=instrument,
                        side=side,
                        entry=entry,
                        stop=stop,
                        target=target,
                        score=score,
                        metadata=metadata,
                    )
                )

        return signals


class VRBStrategy(BaseStrategy):
    name = "VRB"

    def __init__(self, *, band_k: float = 2.0, stop_sigma_mult: float = 1.2, risk_fraction: float = 0.0015) -> None:
        super().__init__(risk_fraction=risk_fraction)
        self.band_k = band_k
        self.stop_sigma_mult = stop_sigma_mult

    def generate_signals(self, df: pd.DataFrame, instrument: str) -> List[StrategySignal]:
        signals: List[StrategySignal] = []
        if df.empty:
            return signals

        for idx in range(1, len(df)):
            row = df.iloc[idx]
            prev = df.iloc[idx - 1]

            sigma = row.get("sigma", np.nan)
            vwap = row.get("vwap", np.nan)
            price = row.get("close", np.nan)

            if np.isnan(sigma) or sigma <= 0 or np.isnan(vwap) or np.isnan(price):
                continue

            upper = vwap + self.band_k * sigma
            lower = vwap - self.band_k * sigma

            side: Optional[Side] = None
            entry = price

            if prev["close"] > upper and price <= upper:
                side = Side.SELL
            elif prev["close"] < lower and price >= lower:
                side = Side.BUY

            if side is None:
                continue

            stop = entry + self.stop_sigma_mult * sigma if side is Side.BUY else entry - self.stop_sigma_mult * sigma
            target = vwap
            distance = abs(entry - vwap)
            score = float(np.clip(distance / (sigma + 1e-6), 0.0, 3.0))

            metadata = {
                "sigma": sigma,
                "distance": distance,
                "vwap": vwap,
            }

            signals.append(
                self._make_signal(
                    row=row,
                    instrument=instrument,
                    side=side,
                    entry=entry,
                    stop=stop,
                    target=target,
                    score=score,
                    metadata=metadata,
                )
            )

        return signals


def generate_all_signals(
    df: pd.DataFrame,
    instrument: str,
    *,
    strategies: Optional[List[BaseStrategy]] = None,
) -> List[StrategySignal]:
    strategies = strategies or [TPBVWAPStrategy(), ORB15Strategy(), VRBStrategy()]
    signals: List[StrategySignal] = []
    for strat in strategies:
        signals.extend(strat.generate_signals(df, instrument))
    signals.sort(key=lambda s: s.timestamp)
    return signals