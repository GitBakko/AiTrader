from __future__ import annotations

import argparse
import json
from pathlib import Path

import pandas as pd

from . import (
    EventDrivenBacktester,
    FeaturePipeline,
    MLExperiment,
    VectorizedBacktester,
    make_features,
)


def load_dataset(path: Path) -> pd.DataFrame:
    df = pd.read_csv(path)
    if "timestamp" in df.columns:
        df["timestamp"] = pd.to_datetime(df["timestamp"], utc=True, errors="coerce")
    return df


def cmd_features(args: argparse.Namespace) -> None:
    pipeline = FeaturePipeline()
    df = load_dataset(Path(args.input))
    features = make_features(df, pipeline=pipeline)
    features.to_parquet(Path(args.output))
    print(f"Features saved to {args.output}")


def cmd_backtest(args: argparse.Namespace) -> None:
    df = load_dataset(Path(args.input))
    pipeline = FeaturePipeline()
    if args.mode == "event":
        report = EventDrivenBacktester(pipeline=pipeline).run(df, args.instrument)
    else:
        report = VectorizedBacktester(pipeline=pipeline).run(df)
    print(json.dumps(report.metrics.__dict__, indent=2, default=str))


def cmd_train(args: argparse.Namespace) -> None:
    df = load_dataset(Path(args.input))
    experiment = MLExperiment.from_yaml(Path(args.config)) if args.config else MLExperiment()
    features = experiment.prepare_features(df)
    metrics = experiment.train(features)
    print(json.dumps(metrics, indent=2))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="FREE mode quant toolkit")
    sub = parser.add_subparsers(dest="command", required=True)

    feat = sub.add_parser("features", help="Generate feature set and save to Parquet")
    feat.add_argument("--input", required=True, help="CSV source with OHLCV data")
    feat.add_argument("--output", required=True, help="Destination parquet path")
    feat.set_defaults(func=cmd_features)

    back = sub.add_parser("backtest", help="Run backtest (event|vector)")
    back.add_argument("--input", required=True, help="CSV source with OHLCV data")
    back.add_argument("--instrument", required=True, help="Instrument symbol")
    back.add_argument("--mode", choices=["event", "vector"], default="event")
    back.set_defaults(func=cmd_backtest)

    train = sub.add_parser("train", help="Run ML training pipeline")
    train.add_argument("--input", required=True, help="CSV source with OHLCV data")
    train.add_argument("--config", help="Optional YAML config path")
    train.set_defaults(func=cmd_train)

    return parser


def main(argv: list[str] | None = None) -> None:
    parser = build_parser()
    args = parser.parse_args(argv)
    args.func(args)


if __name__ == "__main__":  # pragma: no cover
    main()
