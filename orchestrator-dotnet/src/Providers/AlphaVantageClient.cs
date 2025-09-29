using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Orchestrator.Providers
{
    public class AlphaVantageClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly AlphaVantageRateLimiter _limiter;

        public AlphaVantageClient(HttpClient http, string baseUrl, string apiKey, AlphaVantageRateLimiter limiter)
        {
            _http = http; _baseUrl = baseUrl.TrimEnd('/'); _apiKey = apiKey; _limiter = limiter;
        }

        public async Task<JsonDocument?> GetAsync(string query)
        {
            if (!_limiter.TryConsume(out var remaining)) return null;
            var url = $"{_baseUrl}?{query}&apikey={_apiKey}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var stream = await resp.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
    }
}
