from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterator, List, Optional, Tuple

import importlib
import numpy as np
import pandas as pd

yaml = importlib.import_module("yaml")


def _optional_import(name: str):  # pragma: no cover - optional deps
    try:
        return importlib.import_module(name)
    except Exception:
        return None


lgb = _optional_import("lightgbm")
xgb = _optional_import("xgboost")
mlflow = _optional_import("mlflow")
shap = _optional_import("shap")

from sklearn.metrics import accuracy_score, mean_squared_error
from sklearn.preprocessing import StandardScaler
from sklearn.pipeline import Pipeline as SKPipeline
from sklearn.ensemble import GradientBoostingClassifier, GradientBoostingRegressor

from .backtester import EventDrivenBacktester, VectorizedBacktester
from .feature_engineering import FeaturePipeline, FeaturePipelineConfig, make_features


@dataclass
class WalkForwardConfig:
    train_months: int = 6
    test_months: int = 1
    min_splits: int = 12


@dataclass
class MLConfig:
    target_column: str = "label"
    regression_target: str = "future_return"
    feature_columns: Optional[List[str]] = None
    walk_forward: WalkForwardConfig = field(default_factory=WalkForwardConfig)
    pipeline: FeaturePipelineConfig = field(default_factory=FeaturePipelineConfig)


class MLExperiment:
    """Train classification/regression models with walk-forward validation."""

    def __init__(
        self,
        config: Optional[MLConfig] = None,
        *,
        tracking_uri: Optional[str] = None,
    ) -> None:
        self.config = config or MLConfig()
        self.pipeline = FeaturePipeline(self.config.pipeline)
        if tracking_uri and mlflow:
            mlflow.set_tracking_uri(tracking_uri)

    # ------------------------------------------------------------------
    def load_market_csv(self, path: Path) -> pd.DataFrame:
        df = pd.read_csv(path)
        if "timestamp" in df.columns:
            df["timestamp"] = pd.to_datetime(df["timestamp"], utc=True, errors="coerce")
        return df

    def prepare_features(self, df: pd.DataFrame) -> pd.DataFrame:
        return make_features(df, pipeline=self.pipeline)

    def _select_features(self, features: pd.DataFrame) -> Tuple[pd.DataFrame, pd.Series, pd.Series]:
        cols = self.config.feature_columns
        X = features[cols] if cols else features.select_dtypes(include=[np.number]).drop(columns=[self.config.target_column, self.config.regression_target], errors="ignore")
        y_cls = features[self.config.target_column]
        y_reg = features[self.config.regression_target]
        return X.fillna(0.0), y_cls, y_reg

    def walk_forward_splits(self, df: pd.DataFrame) -> Iterator[Tuple[np.ndarray, np.ndarray]]:
        timestamps = pd.to_datetime(df["timestamp"], utc=True)
        train_window = pd.DateOffset(months=self.config.walk_forward.train_months)
        test_window = pd.DateOffset(months=self.config.walk_forward.test_months)

        start = timestamps.min()
        splits = 0

        while True:
            train_end = start + train_window
            test_end = train_end + test_window

            train_mask = (timestamps >= start) & (timestamps < train_end)
            test_mask = (timestamps >= train_end) & (timestamps < test_end)

            if test_mask.sum() == 0 or train_mask.sum() == 0:
                break

            splits += 1
            yield train_mask.values, test_mask.values

            start = start + test_window

        if splits < self.config.walk_forward.min_splits:
            raise ValueError(f"Insufficient walk-forward splits produced ({splits}) < required {self.config.walk_forward.min_splits}")

    # ------------------------------------------------------------------
    def train(self, features: pd.DataFrame) -> Dict[str, Dict[str, float]]:
        X, y_cls, y_reg = self._select_features(features)
        results: Dict[str, Dict[str, float]] = {}

        metrics: List[Dict[str, float]] = []
        reg_metrics: List[Dict[str, float]] = []

        for split_idx, (train_mask, test_mask) in enumerate(self.walk_forward_splits(features)):
            X_train, X_test = X[train_mask], X[test_mask]
            y_train_cls, y_test_cls = y_cls[train_mask], y_cls[test_mask]
            y_train_reg, y_test_reg = y_reg[train_mask], y_reg[test_mask]

            classifier = self._build_classifier()
            regression = self._build_regressor()

            classifier.fit(X_train, y_train_cls)
            regression.fit(X_train, y_train_reg)

            preds = classifier.predict(X_test)
            acc = accuracy_score(y_test_cls, preds)
            mse = mean_squared_error(y_test_reg, regression.predict(X_test))
            metrics.append({"fold": split_idx, "accuracy": acc})
            reg_metrics.append({"fold": split_idx, "mse": mse})

            if mlflow:
                with mlflow.start_run(run_name=f"fold_{split_idx}"):
                    mlflow.log_metric("accuracy", acc)
                    mlflow.log_metric("mse", mse)

        results["classification"] = {
            "accuracy_mean": float(np.mean([m["accuracy"] for m in metrics])),
            "accuracy_std": float(np.std([m["accuracy"] for m in metrics], ddof=0)),
        }
        results["regression"] = {
            "mse_mean": float(np.mean([m["mse"] for m in reg_metrics])),
            "mse_std": float(np.std([m["mse"] for m in reg_metrics], ddof=0)),
        }

        self._compute_shap(classifier, X)

        return results

    def _build_classifier(self):
        if lgb is not None:
            return lgb.LGBMClassifier(objective="multiclass", num_class=3, n_estimators=200, learning_rate=0.05)
        if xgb is not None:
            return xgb.XGBClassifier(objective="multi:softprob", num_class=3, n_estimators=200, learning_rate=0.05)
        return SKPipeline([
            ("scaler", StandardScaler()),
            ("clf", GradientBoostingClassifier()),
        ])

    def _build_regressor(self):
        if lgb is not None:
            return lgb.LGBMRegressor(objective="regression", n_estimators=200, learning_rate=0.05)
        if xgb is not None:
            return xgb.XGBRegressor(objective="reg:squarederror", n_estimators=200, learning_rate=0.05)
        return SKPipeline([
            ("scaler", StandardScaler()),
            ("reg", GradientBoostingRegressor()),
        ])

    def _compute_shap(self, model, X: pd.DataFrame) -> None:
        if shap is None:
            return
        try:
            explainer = shap.Explainer(model)
            shap_values = explainer(X[:200])
            summary = np.nanmean(np.abs(shap_values.values), axis=0)
            ranked = sorted(zip(X.columns, summary), key=lambda kv: kv[1], reverse=True)
            top = {feature: float(score) for feature, score in ranked[:10]}
        except Exception:
            top = {}

        if mlflow:
            mlflow.log_dict(top, "shap_feature_importance.json")
        else:
            print("Top SHAP features:", json.dumps(top, indent=2))

    # ------------------------------------------------------------------
    def backtest(self, market_data: pd.DataFrame, instrument: str) -> Dict[str, object]:
        event_report = EventDrivenBacktester(pipeline=self.pipeline).run(market_data, instrument)
        vector_report = VectorizedBacktester(pipeline=self.pipeline).run(market_data)
        return {
            "event_driven": event_report.metrics.__dict__,
            "vectorized": vector_report.metrics.__dict__,
        }

    # ------------------------------------------------------------------
    @classmethod
    def from_yaml(cls, path: Path) -> "MLExperiment":
        with open(path, "r", encoding="utf-8") as handle:
            raw = yaml.safe_load(handle)
        config = MLConfig(**raw)
        return cls(config)