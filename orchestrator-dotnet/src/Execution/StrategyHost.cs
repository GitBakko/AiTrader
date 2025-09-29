using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Configuration;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Options;
using Orchestrator.Execution.Indicators;
using Orchestrator.Execution.Orders;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Execution;

public sealed class StrategyHost : IStrategyHost, IDisposable
{
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(10);

    private readonly IEventBus _eventBus;
    private readonly IRiskManager _riskManager;
    private readonly IBrokerAdapter _broker;
    private readonly ILogger<StrategyHost> _logger;
    private readonly IMarketDataCache _marketDataCache;
    private readonly IFreeModeConfigProvider _configProvider;
    private readonly IOptionsMonitor<TradingOptions> _tradingOptions;
    private readonly IPortfolioService _portfolio;
    private readonly IIndicatorSnapshotStore _indicatorSnapshots;
    private readonly IOrderStore _orderStore;
    private readonly ConcurrentDictionary<string, SymbolState> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _subscriptions = new();

    private FreeModeConfig? _config;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private StrategySettings _settings = StrategySettings.Default();
    private bool _initialized;

    public StrategyHost(
        IEventBus eventBus,
        IRiskManager riskManager,
        IBrokerAdapter broker,
        ILogger<StrategyHost> logger,
        IMarketDataCache marketDataCache,
        IFreeModeConfigProvider configProvider,
        IOptionsMonitor<TradingOptions> tradingOptions,
        IPortfolioService portfolio,
        IIndicatorSnapshotStore indicatorSnapshots,
        IOrderStore orderStore)
    {
        _eventBus = eventBus;
        _riskManager = riskManager;
        _broker = broker;
        _logger = logger;
        _marketDataCache = marketDataCache;
        _configProvider = configProvider;
        _tradingOptions = tradingOptions;
        _portfolio = portfolio;
        _indicatorSnapshots = indicatorSnapshots;
        _orderStore = orderStore;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _config = _configProvider.Current;
        _timeZone = ResolveTimeZone(_config.Timezone);
        _settings = StrategySettings.FromConfig(_config, _tradingOptions.CurrentValue);

        _subscriptions.Add(_eventBus.Subscribe<BookTickerEvent>(OnBookTickerAsync));
        _subscriptions.Add(_eventBus.Subscribe<TradeEvent>(OnTradeAsync));
        _subscriptions.Add(_eventBus.Subscribe<KlineEvent>(OnKlineAsync));

        _initialized = true;
        _logger.LogInformation("Strategy host initialized with timezone {Timezone}", _timeZone.Id);
        return Task.CompletedTask;
    }

    public async Task HandleEventAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        switch (@event)
        {
            case BookTickerEvent bookTicker:
                await OnBookTickerAsync(bookTicker, cancellationToken).ConfigureAwait(false);
                break;
            case TradeEvent tradeEvent:
                await OnTradeAsync(tradeEvent, cancellationToken).ConfigureAwait(false);
                break;
            case KlineEvent klineEvent:
                await OnKlineAsync(klineEvent, cancellationToken).ConfigureAwait(false);
                break;
            default:
                _logger.LogDebug("Unhandled event type {EventType}", @event.GetType().Name);
                break;
        }
    }

    private ValueTask OnBookTickerAsync(BookTickerEvent evt, CancellationToken cancellationToken)
    {
        var quote = new Quote(evt.Symbol, evt.Bid, evt.Ask, evt.TimestampUtc);
        _marketDataCache.UpdateQuote(quote);
        var state = GetSymbolState(evt.Symbol);
        state.UpdateQuote(quote);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnTradeAsync(TradeEvent evt, CancellationToken cancellationToken)
    {
        var state = GetSymbolState(evt.Symbol);
        state.UpdateTrade(evt);
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnKlineAsync(KlineEvent evt, CancellationToken cancellationToken)
    {
        var state = GetSymbolState(evt.Symbol);
        state.ApplyKline(evt);
        state.UpdateOpeningRange(evt, _timeZone, _settings.Orb.Minutes);

        if (!evt.IsFinal)
        {
            return;
        }

        await EvaluateStrategiesAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private SymbolState GetSymbolState(string symbol)
    {
        return _symbols.GetOrAdd(symbol, s => new SymbolState(s, _settings.MaxAttemptsPerSide));
    }

    private async Task EvaluateStrategiesAsync(SymbolState state, CancellationToken ct)
    {
        try
        {
            var snapshot = state.BuildSnapshot();
            _indicatorSnapshots.Update(state.Symbol, snapshot);
            var midFeature = Feature(snapshot.Features, "price");
            var quote = state.LastQuote ?? new Quote(state.Symbol, midFeature, midFeature, snapshot.TimestampUtc);
            var now = snapshot.TimestampUtc;

            if (snapshot.Atr14 <= 0m || Feature(snapshot.Features, "price") <= 0m)
            {
                return;
            }

            if (_settings.Tpb.Enabled)
            {
                var signal = TryGenerateTpbSignal(state.Symbol, quote, snapshot, now, _settings.Tpb);
                if (signal is not null && state.CanEmit(signal.Strategy, signal.Side, now, _settings.MaxAttemptsPerSide, _settings.Tpb.Cooldown ?? DefaultCooldown))
                {
                    state.RegisterSignal(signal.Strategy, signal.Side, now);
                    await ProcessSignalAsync(signal, snapshot, quote, ct).ConfigureAwait(false);
                }
            }

            if (_settings.Orb.Enabled)
            {
                var signal = TryGenerateOrbSignal(state, quote, snapshot, now, _settings.Orb);
                if (signal is not null && state.CanEmit(signal.Strategy, signal.Side, now, _settings.MaxAttemptsPerSide, _settings.Orb.Cooldown ?? DefaultCooldown))
                {
                    state.RegisterSignal(signal.Strategy, signal.Side, now);
                    await ProcessSignalAsync(signal, snapshot, quote, ct).ConfigureAwait(false);
                }
            }

            if (_settings.Vrb.Enabled)
            {
                var signal = TryGenerateVrbSignal(state.Symbol, quote, snapshot, now, _settings.Vrb);
                if (signal is not null && state.CanEmit(signal.Strategy, signal.Side, now, _settings.MaxAttemptsPerSide, _settings.Vrb.Cooldown ?? DefaultCooldown))
                {
                    state.RegisterSignal(signal.Strategy, signal.Side, now);
                    await ProcessSignalAsync(signal, snapshot, quote, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Strategy evaluation failed for {Symbol}", state.Symbol);
        }
    }

    private async Task ProcessSignalAsync(StrategySignal signal, IndicatorSnapshot snapshot, Quote quote, CancellationToken ct)
    {
        await _eventBus.PublishAsync(new StrategySignalEvent(signal.TimestampUtc, signal), ct).ConfigureAwait(false);

        var account = _portfolio.GetSnapshot();
        var market = new MarketSnapshot(snapshot.Atr14, Feature(snapshot.Features, "spread"), snapshot.TrendOk, true);

        var entry = signal.EntryPrice;
        var stop = signal.StopPrice;
        var riskFraction = signal.RiskFraction <= 0m ? _settings.DefaultRiskFraction : signal.RiskFraction;
        var stopDistance = Math.Abs(entry - stop);
        if (stopDistance <= 0m)
        {
            _logger.LogWarning("Invalid stop distance for signal {Strategy} {Instrument}", signal.Strategy, signal.Instrument);
            return;
        }

        var desiredQuantity = Math.Max(1m, Math.Floor((account.Equity * riskFraction) / stopDistance));

        var intent = new TradeIntent(
            signal.Instrument,
            signal.Side,
            desiredQuantity,
            entry,
            stop,
            signal.Strategy,
            signal.TargetPrice,
            OrderType.Market,
            Guid.NewGuid().ToString());

        var riskDecision = await _riskManager.PreTradeCheckAsync(intent, account, market, ct).ConfigureAwait(false);

        if (!riskDecision.Allowed || riskDecision.AllowedQuantity <= 0m)
        {
            _logger.LogInformation("Risk rejected {Strategy} on {Instrument}: {Reason}", signal.Strategy, signal.Instrument, riskDecision.Reason);
            await _eventBus.PublishAsync(new AlertEvent(DateTime.UtcNow, "BROKER_REJECT", "WARN", riskDecision.Reason, new Dictionary<string, object>
            {
                ["instrument"] = signal.Instrument,
                ["strategy"] = signal.Strategy,
                ["side"] = signal.Side.ToString().ToUpperInvariant()
            }), ct).ConfigureAwait(false);
            return;
        }

        intent = intent with { IntendedQuantity = riskDecision.AllowedQuantity };

        var execution = await _broker.PlaceOrderAsync(intent, ct).ConfigureAwait(false);
        var metadata = execution.Metadata is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(execution.Metadata);
        metadata["riskFraction"] = riskDecision.RiskFractionUsed;
        metadata["signalScore"] = signal.Score;
        execution = execution with { Metadata = metadata };
        await _riskManager.PostTradeUpdateAsync(execution.Fill, market, ct).ConfigureAwait(false);
        _portfolio.ApplyFill(execution.Fill);
        _orderStore.RecordExecution("auto", intent, execution, signal.Strategy);

        await _eventBus.PublishAsync(new ExecutionFillEvent(execution.Fill.TimestampUtc, execution.Fill, execution.OrderId, signal.Strategy), ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Executed {Strategy} on {Instrument}: side {Side} qty {Qty} entry {Entry} stop {Stop}",
            signal.Strategy,
            signal.Instrument,
            signal.Side,
            execution.Fill.Quantity,
            execution.Fill.Price,
            signal.StopPrice);
    }

    private static StrategySignal? TryGenerateTpbSignal(string symbol, Quote quote, IndicatorSnapshot snapshot, DateTime timestampUtc, TpbSettings settings)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        var price = GetMidPrice(quote, snapshot);
        if (price <= 0m)
        {
            return null;
        }

        var ema = Feature(snapshot.Features, "ema200");
        var sma20 = Feature(snapshot.Features, "sma20");
        var vwap = Feature(snapshot.Features, "vwap");
        var rsi7 = Feature(snapshot.Features, "rsi7", 50m);
        var atr = Feature(snapshot.Features, "atr14");

        if (atr <= 0m)
        {
            return null;
        }

        var trendUp = price >= ema;
        var trendDown = price <= ema;

        if (!trendUp && !trendDown)
        {
            return null;
        }

        var distanceSma = RelativeDistance(price, sma20);
        var distanceVwap = RelativeDistance(price, vwap);

        if (distanceSma > settings.MaxDistance || distanceVwap > settings.MaxDistance)
        {
            return null;
        }

        if (trendUp && rsi7 <= 35m)
        {
            return null;
        }

        if (trendDown && rsi7 >= 65m)
        {
            return null;
        }

        var side = trendUp ? TradeSide.Buy : TradeSide.Sell;
        var stopDistance = Math.Max(settings.MinStopDistance, Math.Min(atr * 0.8m, Math.Max(1e-6m, Math.Abs(price - sma20))));
        var stop = side == TradeSide.Buy ? price - stopDistance : price + stopDistance;
        var target = side == TradeSide.Buy
            ? price + stopDistance * settings.TakeProfitMultiplier
            : price - stopDistance * settings.TakeProfitMultiplier;

        if (stopDistance <= 0m)
        {
            return null;
        }

        var score = 1m - Math.Min(distanceSma, distanceVwap) / settings.MaxDistance;
        score = Math.Clamp(score, 0m, 1m);

        var features = MergeFeatures(snapshot.Features, new Dictionary<string, decimal>
        {
            ["distance_sma"] = distanceSma,
            ["distance_vwap"] = distanceVwap,
            ["stop_distance"] = stopDistance
        });

        return new StrategySignal(symbol, "TPB_VWAP", side, price, stop, target, score, settings.RiskFraction, features, timestampUtc);
    }

    private static StrategySignal? TryGenerateOrbSignal(SymbolState state, Quote quote, IndicatorSnapshot snapshot, DateTime timestampUtc, OrbSettings settings)
    {
        if (!settings.Enabled || !state.OpeningRange.Completed)
        {
            return null;
        }

        var rangeHigh = state.OpeningRange.High;
        var rangeLow = state.OpeningRange.Low;
        var range = rangeHigh - rangeLow;

        if (range <= 0m)
        {
            return null;
        }

        var price = GetMidPrice(quote, snapshot);
        var breakoutLevel = rangeHigh + settings.BufferTicks;
        var breakdownLevel = rangeLow - settings.BufferTicks;
        var atr = Feature(snapshot.Features, "atr14");

        TradeSide? side = null;
        decimal entry = 0m;
        decimal stop = 0m;
        decimal target = 0m;

        if (price > breakoutLevel)
        {
            side = TradeSide.Buy;
            entry = breakoutLevel;
            var stopDistance = Math.Max(settings.MinStopDistance, Math.Min(range / 2m, atr));
            stop = entry - stopDistance;
            target = entry + range * settings.RangeProjection;
        }
        else if (price < breakdownLevel)
        {
            side = TradeSide.Sell;
            entry = breakdownLevel;
            var stopDistance = Math.Max(settings.MinStopDistance, Math.Min(range / 2m, atr));
            stop = entry + stopDistance;
            target = entry - range * settings.RangeProjection;
        }

        if (side is null)
        {
            return null;
        }

        var stopDistanceCheck = Math.Abs(entry - stop);
        if (stopDistanceCheck <= 0m)
        {
            return null;
        }

        var score = Math.Clamp(range / (atr + 1e-6m), 0m, 3m);
        var features = MergeFeatures(snapshot.Features, new Dictionary<string, decimal>
        {
            ["or_high"] = rangeHigh,
            ["or_low"] = rangeLow,
            ["or_range"] = range
        });

        return new StrategySignal(state.Symbol, "ORB_15", side.Value, entry, stop, target, score, settings.RiskFraction, features, timestampUtc);
    }

    private static StrategySignal? TryGenerateVrbSignal(string symbol, Quote quote, IndicatorSnapshot snapshot, DateTime timestampUtc, VrbSettings settings)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        var sigma = Feature(snapshot.Features, "sigma");
        if (sigma <= 0m)
        {
            return null;
        }

        var price = GetMidPrice(quote, snapshot);
        var vwap = Feature(snapshot.Features, "vwap");
        var upperBand = vwap + settings.BandK * sigma;
        var lowerBand = vwap - settings.BandK * sigma;
        var atr = Feature(snapshot.Features, "atr14");

        TradeSide? side = null;
        decimal entry = price;
        decimal stop;
        decimal target = vwap;

        if (price > upperBand)
        {
            side = TradeSide.Sell;
            stop = price + sigma * settings.StopMultiplier;
        }
        else if (price < lowerBand)
        {
            side = TradeSide.Buy;
            stop = price - sigma * settings.StopMultiplier;
        }
        else
        {
            return null;
        }

        var stopDistance = Math.Abs(entry - stop);
        if (stopDistance <= 0m)
        {
            return null;
        }

        var score = Math.Clamp(Math.Abs(price - vwap) / (settings.BandK * sigma + 1e-6m), 0m, 2m);
        var features = MergeFeatures(snapshot.Features, new Dictionary<string, decimal>
        {
            ["vwap"] = vwap,
            ["sigma"] = sigma,
            ["distance_vwap"] = Math.Abs(price - vwap)
        });

        return new StrategySignal(symbol, "VRB", side.Value, entry, stop, target, score, settings.RiskFraction, features, timestampUtc);
    }

    private static IReadOnlyDictionary<string, decimal> MergeFeatures(IReadOnlyDictionary<string, decimal> baseFeatures, IDictionary<string, decimal> extra)
    {
        var map = new Dictionary<string, decimal>(baseFeatures);
        foreach (var kvp in extra)
        {
            map[kvp.Key] = kvp.Value;
        }

        return map;
    }

    private static decimal Feature(IReadOnlyDictionary<string, decimal> features, string key, decimal fallback = 0m)
    {
        return features.TryGetValue(key, out var value) ? value : fallback;
    }

    private static decimal GetMidPrice(Quote quote, IndicatorSnapshot snapshot)
    {
        var mid = (quote.Bid + quote.Ask) / 2m;
        if (mid <= 0m)
        {
            return Feature(snapshot.Features, "price");
        }

        return mid;
    }

    private static decimal RelativeDistance(decimal price, decimal reference)
    {
        if (reference == 0m)
        {
            return decimal.MaxValue;
        }

        return Math.Abs(price - reference) / reference;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private sealed class SymbolState
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, AttemptTracker> _attempts = new(StringComparer.OrdinalIgnoreCase);
        private readonly IndicatorEngine _indicators = new();
        private readonly int _maxAttemptsPerSide;

        public SymbolState(string symbol, int maxAttemptsPerSide)
        {
            Symbol = symbol;
            _maxAttemptsPerSide = maxAttemptsPerSide;
        }

        public string Symbol { get; }
        public Quote? LastQuote { get; private set; }
        public OpeningRangeState OpeningRange { get; } = new();

        public void UpdateQuote(Quote quote)
        {
            lock (_sync)
            {
                LastQuote = quote;
                _indicators.UpdateQuote(quote);
            }
        }

        public void UpdateTrade(TradeEvent trade)
        {
            lock (_sync)
            {
                _indicators.UpdateTrade(trade);
            }
        }

        public void ApplyKline(KlineEvent kline)
        {
            lock (_sync)
            {
                _indicators.ApplyKline(kline);
            }
        }

        public void UpdateOpeningRange(KlineEvent kline, TimeZoneInfo timeZone, int windowMinutes)
        {
            lock (_sync)
            {
                OpeningRange.Update(kline, timeZone, windowMinutes);
            }
        }

        public IndicatorSnapshot BuildSnapshot()
        {
            lock (_sync)
            {
                return _indicators.BuildSnapshot(Symbol);
            }
        }

        public bool CanEmit(string strategy, TradeSide side, DateTime timestampUtc, int maxAttemptsPerSide, TimeSpan cooldown)
        {
            lock (_sync)
            {
                var session = timestampUtc.Date;
                var key = AttemptTracker.Key(strategy, side);

                if (!_attempts.TryGetValue(key, out var tracker) || tracker.SessionDate != session)
                {
                    tracker = new AttemptTracker(session);
                    _attempts[key] = tracker;
                }

                if (tracker.Attempts >= Math.Min(maxAttemptsPerSide, _maxAttemptsPerSide))
                {
                    return false;
                }

                if (timestampUtc - tracker.LastSignalUtc < cooldown)
                {
                    return false;
                }

                return true;
            }
        }

        public void RegisterSignal(string strategy, TradeSide side, DateTime timestampUtc)
        {
            lock (_sync)
            {
                var key = AttemptTracker.Key(strategy, side);
                if (!_attempts.TryGetValue(key, out var tracker) || tracker.SessionDate != timestampUtc.Date)
                {
                    tracker = new AttemptTracker(timestampUtc.Date);
                    _attempts[key] = tracker;
                }

                tracker.Attempts++;
                tracker.LastSignalUtc = timestampUtc;
            }
        }
    }

    private sealed class OpeningRangeState
    {
        public DateTime SessionDate { get; private set; }
        public decimal High { get; private set; } = decimal.MinValue;
        public decimal Low { get; private set; } = decimal.MaxValue;
        public bool Completed { get; private set; }

        public void Update(KlineEvent kline, TimeZoneInfo timeZone, int windowMinutes)
        {
            var localClose = TimeZoneInfo.ConvertTimeFromUtc(kline.CloseTimeUtc, timeZone);
            var sessionDate = localClose.Date;

            if (sessionDate != SessionDate)
            {
                SessionDate = sessionDate;
                High = decimal.MinValue;
                Low = decimal.MaxValue;
                Completed = false;
            }

            if (Completed)
            {
                return;
            }

            High = Math.Max(High, kline.High);
            Low = Math.Min(Low, kline.Low);

            var rangeEnd = sessionDate.AddMinutes(windowMinutes);
            if (localClose >= rangeEnd)
            {
                Completed = true;
            }
        }
    }

    private sealed class AttemptTracker
    {
        public AttemptTracker(DateTime sessionDate)
        {
            SessionDate = sessionDate;
        }

        public DateTime SessionDate { get; }
        public int Attempts { get; set; }
        public DateTime LastSignalUtc { get; set; }

        public static string Key(string strategy, TradeSide side) => $"{strategy}:{side}";
    }

    private sealed record TpbSettings(bool Enabled, decimal RiskFraction, decimal TakeProfitMultiplier, TimeSpan? Cooldown, decimal MaxDistance, decimal MinStopDistance)
    {
        public static TpbSettings FromConfig(FreeModeConfig.StrategySection? section, decimal riskFraction)
        {
            if (section is null)
            {
                return new TpbSettings(true, riskFraction, 1.8m, TimeSpan.FromMinutes(10), 0.005m, 0.1m);
            }

            var tpMultiplier = 1.8m;
            if (TryGetParameter(section.Parameters, "take_profit_R", out var tpObject))
            {
                var candidates = new List<decimal>();
                if (tpObject is IEnumerable<object> sequence)
                {
                    foreach (var item in sequence)
                    {
                        if (TryConvertToDecimal(item, out var dec))
                        {
                            candidates.Add(dec);
                        }
                    }
                }
                else if (TryConvertToDecimal(tpObject, out var single))
                {
                    candidates.Add(single);
                }

                if (candidates.Count > 0)
                {
                    tpMultiplier = candidates.Max();
                }
            }

            return new TpbSettings(section.Enabled, riskFraction, tpMultiplier, TimeSpan.FromMinutes(10), 0.005m, 0.1m);
        }
    }

    private sealed record OrbSettings(bool Enabled, int Minutes, decimal BufferTicks, decimal RangeProjection, TimeSpan? Cooldown, decimal RiskFraction)
    {
        public decimal MinStopDistance => 0.1m;

        public static OrbSettings FromConfig(FreeModeConfig.StrategySection? section, decimal riskFraction)
        {
            if (section is null)
            {
                return new OrbSettings(true, 15, 0.5m, 1.2m, TimeSpan.FromMinutes(10), riskFraction);
            }

            var minutes = ConvertToInt(section.Parameters, "or_minutes", 15);
            var buffer = ConvertToDecimal(section.Parameters, "buffer_ticks", 0.5m);
            return new OrbSettings(section.Enabled, minutes, buffer, 1.2m, TimeSpan.FromMinutes(10), riskFraction);
        }
    }

    private sealed record VrbSettings(bool Enabled, decimal BandK, decimal StopMultiplier, TimeSpan? Cooldown, decimal RiskFraction)
    {
        public static VrbSettings FromConfig(FreeModeConfig.StrategySection? section, decimal riskFraction)
        {
            if (section is null)
            {
                return new VrbSettings(true, 2.0m, 1.2m, TimeSpan.FromMinutes(10), riskFraction);
            }

            var bandK = ConvertToDecimal(section.Parameters, "vwap_band_k", 2.0m);
            return new VrbSettings(section.Enabled, bandK, 1.2m, TimeSpan.FromMinutes(10), riskFraction);
        }
    }

    private sealed record StrategySettings(
        TpbSettings Tpb,
        OrbSettings Orb,
        VrbSettings Vrb,
        int MaxAttemptsPerSide,
        decimal DefaultRiskFraction)
    {
        public static StrategySettings Default() => new(
            TpbSettings.FromConfig(null, 0.0035m),
            OrbSettings.FromConfig(null, 0.0035m),
            VrbSettings.FromConfig(null, 0.0035m),
            2,
            0.0035m);

        public static StrategySettings FromConfig(FreeModeConfig config, TradingOptions tradingOptions)
        {
            var riskFraction = tradingOptions.Risk?.PerTradePct ?? 0.0035m;

            return new StrategySettings(
                TpbSettings.FromConfig(config.Strategies.GetValueOrDefault("TPB_VWAP"), riskFraction),
                OrbSettings.FromConfig(config.Strategies.GetValueOrDefault("ORB_15"), riskFraction),
                VrbSettings.FromConfig(config.Strategies.GetValueOrDefault("VRB"), riskFraction),
                2,
                riskFraction);
        }
    }

    private static int ConvertToInt(Dictionary<string, object> parameters, string key, int fallback)
    {
        object? value = null;
        if (parameters.TryGetValue(key, out var direct))
        {
            value = direct;
        }
        else if (!TryGetParameter(parameters, key, out value))
        {
            return fallback;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return (int)l;
        }

        if (value is double d)
        {
            return (int)Math.Round(d);
        }

        if (value is float f)
        {
            return (int)Math.Round(f);
        }

        if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static decimal ConvertToDecimal(Dictionary<string, object> parameters, string key, decimal fallback)
    {
        object? value = null;
        if (parameters.TryGetValue(key, out var direct))
        {
            value = direct;
        }
        else if (TryGetParameter(parameters, key, out var fallbackValue))
        {
            value = fallbackValue;
        }

        if (value is not null && TryConvertToDecimal(value, out var converted))
        {
            return converted;
        }

        return fallback;
    }

    private static bool TryGetParameter(Dictionary<string, object> parameters, string key, out object? value)
    {
        if (parameters.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var kvp in parameters)
        {
            if (kvp.Key is string str && string.Equals(str, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    private static bool TryConvertToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal dec:
                result = dec;
                return true;
            case double dbl:
                result = (decimal)dbl;
                return true;
            case float fl:
                result = (decimal)fl;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case null:
                result = 0m;
                return false;
            default:
                if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
                {
                    result = parsed;
                    return true;
                }

                result = 0m;
                return false;
        }
    }
}
