using System;
using System.IO;
using System.Text.Json;

namespace Orchestrator.Providers
{
    public class AlphaVantageRateLimiter
    {
        private readonly string _stateFile;
        private readonly int _dailyQuota;
        private readonly object _lock = new();

        private class State { public DateTime DateUtc { get; set; } public int Used { get; set; } }

        public AlphaVantageRateLimiter(string stateFile, int dailyQuota)
        {
            _stateFile = stateFile;
            _dailyQuota = dailyQuota;
            EnsureToday();
        }

        private State Load()
        {
            if (!File.Exists(_stateFile)) return new State{ DateUtc = DateTime.UtcNow.Date, Used = 0 };
            return JsonSerializer.Deserialize<State>(File.ReadAllText(_stateFile)) ?? new State{ DateUtc = DateTime.UtcNow.Date, Used = 0 };
        }
        private void Save(State s) => File.WriteAllText(_stateFile, JsonSerializer.Serialize(s));

        private void EnsureToday()
        {
            lock(_lock)
            {
                var s = Load();
                if (s.DateUtc != DateTime.UtcNow.Date) { s.DateUtc = DateTime.UtcNow.Date; s.Used = 0; Save(s); }
            }
        }

        public bool TryConsume(out int remaining)
        {
            lock(_lock)
            {
                EnsureToday();
                var s = Load();
                if (s.Used >= _dailyQuota) { remaining = 0; return false; }
                s.Used += 1;
                Save(s);
                remaining = _dailyQuota - s.Used;
                return true;
            }
        }
    }
}
