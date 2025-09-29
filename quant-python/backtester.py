"""Backtesting utilities covering event-driven and vectorised modes."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, List, Optional, Sequence

import numpy as np
import pandas as pd

from .feature_engineering import FeaturePipeline, make_features
from .strategies import (
    BaseStrategy,
    Side,
    StrategySignal,
    generate_all_signals,
    ORB15Strategy,
    TPBVWAPStrategy,
    VRBStrategy,
)


@dataclass(frozen=True)
class TradeResult:
    entry_time: pd.Timestamp
    exit_time: pd.Timestamp
    entry_price: float
    exit_price: float
    side: Side
    stop_price: float
    target_price: float
    pnl: float
    r_multiple: float
    bars_held: int
    exit_reason: str
    strategy: str


@dataclass(frozen=True)
class BacktestMetrics:
    cagr: float
    sharpe: float
    mar: float
    max_drawdown: float
    win_rate: float
    payoff: float
    expectancy: float
    exposure: float
    cvar_95: float


@dataclass(frozen=True)
class BacktestReport:
    trades: List[TradeResult]
    metrics: BacktestMetrics
    equity_curve: pd.DataFrame


class EventDrivenBacktester:
    """Simulate signals sequentially with latency, slippage, and partial fills."""

    def __init__(
        self,
        *,
        strategies: Optional[Sequence[BaseStrategy]] = None,
        pipeline: Optional[FeaturePipeline] = None,
        initial_equity: float = 100_000.0,
        slippage_bps: float = 5.0,
        commission_per_trade: float = 0.0,
    ) -> None:
        self.strategies = list(strategies or [TPBVWAPStrategy(), ORB15Strategy(), VRBStrategy()])
        self.pipeline = pipeline or FeaturePipeline()
        self.initial_equity = initial_equity
        self.slippage_bps = slippage_bps
        self.commission = commission_per_trade

    def run(self, market_data: pd.DataFrame, instrument: str) -> BacktestReport:
        features = market_data
        if "ema200" not in features.columns:
            features = make_features(market_data, pipeline=self.pipeline)

        features = features.copy()
        features["timestamp"] = pd.to_datetime(features.get("timestamp", features.index))
        features = features.sort_values("timestamp").reset_index(drop=True)

        signals = generate_all_signals(features, instrument, strategies=list(self.strategies))
        trades = self._simulate_trades(features, signals)
        equity_curve = self._build_equity_curve(trades)
        metrics = self._compute_metrics(trades, equity_curve)
        return BacktestReport(trades=trades, metrics=metrics, equity_curve=equity_curve)

    # ------------------------------------------------------------------
    def _simulate_trades(self, df: pd.DataFrame, signals: Sequence[StrategySignal]) -> List[TradeResult]:
        if not signals or df.empty:
            return []

        slippage = self.slippage_bps / 10_000.0
        df = df.reset_index(drop=True)
        signals = sorted(signals, key=lambda s: s.timestamp)

        signal_idx = 0
        open_position = None
        trades: List[TradeResult] = []

        for bar_idx, row in df.iterrows():
            timestamp = pd.to_datetime(row["timestamp"])

            # queue signals for this bar
            triggered_signals: List[StrategySignal] = []
            while signal_idx < len(signals) and signals[signal_idx].timestamp <= timestamp:
                triggered_signals.append(signals[signal_idx])
                signal_idx += 1

            if open_position is None and triggered_signals:
                signal = triggered_signals[0]
                entry_price = float(signal.entry)
                if signal.side is Side.BUY:
                    entry_price *= 1 + slippage
                else:
                    entry_price *= 1 - slippage

                open_position = {
                    "signal": signal,
                    "entry_price": entry_price,
                    "entry_time": timestamp,
                }

            if open_position is None:
                continue

            signal = open_position["signal"]
            side = signal.side
            entry_price = open_position["entry_price"]
            entry_time = open_position["entry_time"]
            stop_price = signal.stop
            target_price = signal.target

            high = row.get("high", row["close"])
            low = row.get("low", row["close"])

            exit_reason = None
            exit_price = None

            if side is Side.BUY:
                if low <= stop_price:
                    exit_reason = "stop"
                    exit_price = stop_price * (1 - slippage)
                elif high >= target_price:
                    exit_reason = "target"
                    exit_price = target_price * (1 - slippage)
            else:  # Side.SELL
                if high >= stop_price:
                    exit_reason = "stop"
                    exit_price = stop_price * (1 + slippage)
                elif low <= target_price:
                    exit_reason = "target"
                    exit_price = target_price * (1 + slippage)

            if exit_reason is None and bar_idx == len(df) - 1:
                exit_reason = "expiry"
                exit_price = row["close"] * (1 - slippage if side is Side.BUY else 1 + slippage)

            if exit_reason is None:
                continue

            pnl = (exit_price - entry_price) if side is Side.BUY else (entry_price - exit_price)
            pnl -= self.commission

            risk = max(abs(entry_price - stop_price), 1e-6)
            r_multiple = pnl / risk

            holding_minutes = (
                (timestamp - entry_time) / pd.Timedelta(minutes=1)
                if timestamp > entry_time
                else 0
            )
            bars_held = max(int(round(float(holding_minutes))), 1)

            trades.append(
                TradeResult(
                    entry_time=entry_time,
                    exit_time=timestamp,
                    entry_price=entry_price,
                    exit_price=exit_price,
                    side=side,
                    stop_price=stop_price,
                    target_price=target_price,
                    pnl=pnl,
                    r_multiple=r_multiple,
                    bars_held=bars_held,
                    exit_reason=exit_reason,
                    strategy=signal.strategy,
                )
            )

            open_position = None

        return trades

    def _build_equity_curve(self, trades: Sequence[TradeResult]) -> pd.DataFrame:
        equity = self.initial_equity
        timestamps = []
        equities = []
        for trade in trades:
            equity += trade.pnl
            timestamps.append(trade.exit_time)
            equities.append(equity)
        return pd.DataFrame({"timestamp": timestamps, "equity": equities})

    def _compute_metrics(self, trades: Sequence[TradeResult], equity_curve: pd.DataFrame) -> BacktestMetrics:
        if not trades:
            return BacktestMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)

        pnl = np.array([t.pnl for t in trades])
        r = np.array([t.r_multiple for t in trades])
        wins = pnl > 0

        final_equity = equity_curve["equity"].iloc[-1]
        start_time = trades[0].entry_time
        end_time = trades[-1].exit_time
        years = max((end_time - start_time).days / 365.25, 1e-6)
        cagr = (final_equity / self.initial_equity) ** (1 / years) - 1 if final_equity > 0 else -1

        returns = pnl / self.initial_equity
        returns_std = returns.std(ddof=0)
        sharpe = (returns.mean() / (returns_std + 1e-9)) * np.sqrt(len(returns)) if len(returns) else 0.0

        peak = np.maximum.accumulate(equity_curve["equity"].values)
        drawdowns = (equity_curve["equity"].values - peak) / peak
        max_dd = drawdowns.min()
        mar = cagr / abs(max_dd) if max_dd < 0 else np.nan

        win_rate = wins.sum() / len(trades)
        if wins.any() and (~wins).any():
            payoff = pnl[wins].mean() / abs(pnl[~wins].mean())
        elif wins.any():
            payoff = pnl[wins].mean()
        else:
            payoff = 0.0
        expectancy = pnl.mean()

        time_in_market = float(sum(t.bars_held for t in trades))
        total_minutes = max(
            float((trades[-1].exit_time - trades[0].entry_time) / pd.Timedelta(minutes=1)),
            time_in_market,
        )
        exposure = float(time_in_market / total_minutes) if total_minutes else 0.0

        sorted_returns = np.sort(r)
        cutoff = int(max(len(sorted_returns) * 0.05, 1))
        cvar = sorted_returns[:cutoff].mean() if cutoff > 0 else float(sorted_returns[0])

        return BacktestMetrics(
            cagr=float(cagr),
            sharpe=float(sharpe),
            mar=float(mar if np.isfinite(mar) else 0.0),
            max_drawdown=float(max_dd),
            win_rate=float(win_rate),
            payoff=float(payoff) if np.isfinite(payoff) else 0.0,
            expectancy=float(expectancy),
            exposure=float(exposure),
            cvar_95=float(cvar),
        )


class VectorizedBacktester:
    """Fast evaluation using pre-labelled feature frames."""

    def __init__(self, *, pipeline: Optional[FeaturePipeline] = None):
        self.pipeline = pipeline or FeaturePipeline()

    def run(self, market_data: pd.DataFrame) -> BacktestReport:
        features = market_data
        if "label" not in features.columns:
            features = make_features(market_data, pipeline=self.pipeline)

        features = features.dropna(subset=["future_return", "label"])
        features = features.reset_index(drop=True)
        timestamp = pd.to_datetime(features.get("timestamp", features.index))
        if not isinstance(timestamp, pd.Series):
            timestamp = pd.Series(timestamp)
        timestamp = timestamp.reset_index(drop=True)

        trades: List[TradeResult] = []
        horizon = self.pipeline.config.label.horizon

        for idx, row in features.iterrows():
            label = int(row["label"])
            if label == 0:
                continue

            side = Side.BUY if label == 1 else Side.SELL
            entry_price = row["close"]
            exit_price = row["close"] * (1 + row["future_return"])  # identical for both sides
            exit_index = min(idx + horizon, len(features) - 1)
            exit_time = timestamp.iloc[exit_index]

            if side is Side.BUY:
                pnl = (exit_price - entry_price)
            else:
                pnl = (entry_price - exit_price)
            risk = max(abs(row["close"] - row.get("sma20", row["close"])), 1e-6)
            r_multiple = pnl / risk

            trades.append(
                TradeResult(
                    entry_time=timestamp.iloc[idx],
                    exit_time=exit_time,
                    entry_price=float(entry_price),
                    exit_price=float(exit_price),
                    side=side,
                    stop_price=float(row.get("close", entry_price) - risk if side is Side.BUY else row.get("close", entry_price) + risk),
                    target_price=float(exit_price),
                    pnl=float(pnl),
                    r_multiple=float(r_multiple),
                    bars_held=horizon,
                    exit_reason="vector",
                    strategy="VECTOR_LABEL",
                )
            )

        backtester = EventDrivenBacktester(strategies=[], pipeline=self.pipeline)
        equity_curve = backtester._build_equity_curve(trades)
        metrics = backtester._compute_metrics(trades, equity_curve)
        return BacktestReport(trades=trades, metrics=metrics, equity_curve=equity_curve)
