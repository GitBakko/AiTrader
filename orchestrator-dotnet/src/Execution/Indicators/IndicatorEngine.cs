using System;
using System.Collections.Generic;
using System.Linq;
using Orchestrator.Application.Contracts;
using Orchestrator.Market;

namespace Orchestrator.Execution.Indicators;

internal sealed class IndicatorEngine
{
    private const int EmaPeriod = 200;
    private const int SmaPeriod = 20;
    private const int AtrPeriod = 14;
    private const int RsiFastPeriod = 7;
    private const int RsiSlowPeriod = 14;
    private const int SpreadWindowSize = 120; // ~120 book updates
    private const int SigmaWindowSize = 60;   // ~60 minutes residuals

    private decimal? _ema200;
    private decimal? _atr14;
    private decimal? _rsi7;
    private decimal? _rsi14;
    private decimal _cumPv;
    private decimal _cumVolume;
    private decimal _lastClose;
    private decimal _lastTypicalPrice;
    private decimal _lastVolume;
    private decimal _lastSpread;
    private DateTime _lastTimestampUtc;

    private readonly LimitedQueue _sma20 = new(SmaPeriod);
    private readonly SpreadWindow _spreadWindow = new(SpreadWindowSize);
    private readonly ResidualWindow _residualWindow = new(SigmaWindowSize);
    private readonly WilderRsi _rsiFast = new(RsiFastPeriod);
    private readonly WilderRsi _rsiSlow = new(RsiSlowPeriod);

    public void UpdateQuote(Quote quote)
    {
        var spread = Math.Max(quote.Ask - quote.Bid, 0m);
        _lastSpread = spread;
        _lastTimestampUtc = quote.TimestampUtc;
        _spreadWindow.Add(spread);
    }

    public void UpdateTrade(TradeEvent trade)
    {
        _lastTimestampUtc = trade.TimestampUtc;
        _lastClose = trade.Price;
        _lastVolume = trade.Quantity;
    }

    public void ApplyKline(KlineEvent kline)
    {
        _lastTimestampUtc = kline.CloseTimeUtc;
        _lastClose = kline.Close;
        _lastVolume = kline.Volume;

        var typicalPrice = (kline.High + kline.Low + kline.Close) / 3m;
        _lastTypicalPrice = typicalPrice;

        UpdateVwap(typicalPrice, kline.Volume);
        UpdateSma(kline.Close);
        UpdateEma(kline.Close);
        UpdateAtr(kline.High, kline.Low, kline.Close);
        UpdateRsi(kline.Close);
        UpdateResiduals(kline.Close);
    }

    private void UpdateVwap(decimal typicalPrice, decimal volume)
    {
        if (volume <= 0m)
        {
            volume = 1m;
        }

        _cumPv += typicalPrice * volume;
        _cumVolume += volume;
    }

    private void UpdateSma(decimal close)
    {
        _sma20.Add(close);
    }

    private void UpdateEma(decimal close)
    {
        if (!_ema200.HasValue)
        {
            if (_sma20.Count >= EmaPeriod)
            {
                _ema200 = _sma20.Average;
            }
            else
            {
                _ema200 = close;
            }
        }
        else
        {
            var multiplier = 2m / (EmaPeriod + 1);
            _ema200 = close * multiplier + _ema200.Value * (1 - multiplier);
        }
    }

    private void UpdateAtr(decimal high, decimal low, decimal close)
    {
        var prevClose = _lastClose == 0m ? close : _lastClose;
        var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));

        if (!_atr14.HasValue)
        {
            _atr14 = tr;
        }
        else
        {
            _atr14 = (_atr14.Value * (AtrPeriod - 1) + tr) / AtrPeriod;
        }
    }

    private void UpdateRsi(decimal close)
    {
        if (_lastClose == 0m)
        {
            _lastClose = close;
        }

        var change = close - _lastClose;
        _rsi7 = _rsiFast.Next(change);
        _rsi14 = _rsiSlow.Next(change);
    }

    private void UpdateResiduals(decimal close)
    {
        if (_cumVolume <= 0m)
        {
            return;
        }

        var vwap = _cumPv / _cumVolume;
        var residual = close - vwap;
        _residualWindow.Add(residual);
    }

    public IndicatorSnapshot BuildSnapshot(string symbol)
    {
        var vwap = _cumVolume > 0 ? _cumPv / _cumVolume : _lastClose;
        var ema = _ema200 ?? _lastClose;
        var sma = _sma20.Count > 0 ? _sma20.Average : _lastClose;
        var atr = _atr14 ?? 0m;
        var rsiFast = _rsi7 ?? 50m;
        var rsiSlow = _rsi14 ?? 50m;
        var sigma = _residualWindow.StandardDeviation;
        var spreadMedian = _spreadWindow.Median;
        var trendOk = ema != 0m && _lastClose != 0m && Math.Abs((_lastClose - ema) / _lastClose) >= 0.0005m;
        var volatilityOk = atr > 0m;
        var spreadOk = spreadMedian <= 0m || _lastSpread <= spreadMedian * 1.5m;

        var features = new Dictionary<string, decimal>
        {
            ["price"] = _lastClose,
            ["vwap"] = vwap,
            ["ema200"] = ema,
            ["sma20"] = sma,
            ["atr14"] = atr,
            ["rsi7"] = rsiFast,
            ["rsi14"] = rsiSlow,
            ["sigma"] = sigma,
            ["spread"] = _lastSpread,
            ["volume"] = _lastVolume
        };

        return new IndicatorSnapshot(
            _lastTimestampUtc == default ? DateTime.UtcNow : _lastTimestampUtc,
            vwap,
            ema,
            sma,
            atr,
            rsiFast,
            rsiSlow,
            sigma,
            spreadMedian,
            trendOk,
            volatilityOk,
            spreadOk,
            features);
    }

    private sealed class LimitedQueue
    {
        private readonly int _capacity;
        private readonly Queue<decimal> _values;
        private decimal _sum;

        public LimitedQueue(int capacity)
        {
            _capacity = capacity;
            _values = new Queue<decimal>(capacity);
        }

        public int Count => _values.Count;

        public void Add(decimal value)
        {
            if (_values.Count == _capacity)
            {
                var removed = _values.Dequeue();
                _sum -= removed;
            }

            _values.Enqueue(value);
            _sum += value;
        }

        public decimal Average => _values.Count == 0 ? 0m : _sum / _values.Count;
    }

    private sealed class SpreadWindow
    {
        private readonly int _capacity;
        private readonly Queue<decimal> _values;

        public SpreadWindow(int capacity)
        {
            _capacity = capacity;
            _values = new Queue<decimal>(capacity);
        }

        public void Add(decimal value)
        {
            if (_values.Count == _capacity)
            {
                _values.Dequeue();
            }

            _values.Enqueue(value);
        }

        public decimal Median
        {
            get
            {
                if (_values.Count == 0)
                {
                    return 0m;
                }

                var ordered = _values.OrderBy(x => x).ToArray();
                var midpoint = ordered.Length / 2;
                if (ordered.Length % 2 == 0)
                {
                    return (ordered[midpoint - 1] + ordered[midpoint]) / 2m;
                }

                return ordered[midpoint];
            }
        }
    }

    private sealed class ResidualWindow
    {
        private readonly int _capacity;
        private readonly Queue<decimal> _values;
        private decimal _sum;
        private decimal _sumSquares;

        public ResidualWindow(int capacity)
        {
            _capacity = capacity;
            _values = new Queue<decimal>(capacity);
        }

        public void Add(decimal value)
        {
            if (_values.Count == _capacity)
            {
                var removed = _values.Dequeue();
                _sum -= removed;
                _sumSquares -= removed * removed;
            }

            _values.Enqueue(value);
            _sum += value;
            _sumSquares += value * value;
        }

        public decimal StandardDeviation
        {
            get
            {
                if (_values.Count < 2)
                {
                    return 0m;
                }

                var count = (decimal)_values.Count;
                var mean = _sum / count;
                var variance = (_sumSquares / count) - (mean * mean);
                return variance <= 0m ? 0m : (decimal)Math.Sqrt((double)variance);
            }
        }
    }

    private sealed class WilderRsi
    {
        private readonly int _period;
        private decimal? _avgGain;
        private decimal? _avgLoss;
        private decimal _lastValue;
        private bool _initialized;

        public WilderRsi(int period)
        {
            _period = period;
        }

        public decimal Next(decimal change)
        {
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? Math.Abs(change) : 0m;

            if (!_initialized)
            {
                _avgGain = gain;
                _avgLoss = loss;
                _initialized = true;
            }
            else
            {
                _avgGain = ((_avgGain ?? 0m) * (_period - 1) + gain) / _period;
                _avgLoss = ((_avgLoss ?? 0m) * (_period - 1) + loss) / _period;
            }

            if ((_avgLoss ?? 0m) == 0m)
            {
                _lastValue = 100m;
                return _lastValue;
            }

            var rs = (_avgGain ?? 0m) / (_avgLoss ?? 1m);
            _lastValue = 100m - (100m / (1 + rs));
            return _lastValue;
        }
    }
}