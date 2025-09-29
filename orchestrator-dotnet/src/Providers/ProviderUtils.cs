using System;
using System.Text.RegularExpressions;

namespace Orchestrator.Providers;

internal static partial class ProviderUtils
{
    private static readonly Regex PlaceholderRegex = PlaceholderPattern();

    public static string ExpandPlaceholders(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return PlaceholderRegex.Replace(value, match =>
        {
            var key = match.Groups["key"].Value;
            return Environment.GetEnvironmentVariable(key) ?? string.Empty;
        });
    }

    public static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim();
    }

    [GeneratedRegex(@"\$\{(?<key>[A-Za-z0-9_]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
