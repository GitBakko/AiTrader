using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orchestrator.Application.Configuration;

public interface IFreeModeConfigProvider
{
    FreeModeConfig Current { get; }
    Task<FreeModeConfig> ReloadAsync(CancellationToken cancellationToken = default);
}

public sealed class FreeModeConfigProvider : IFreeModeConfigProvider
{
    private readonly ILogger<FreeModeConfigProvider> _logger;
    private readonly IOptionsMonitor<ProviderOptions> _options;
    private readonly IDeserializer _deserializer;
    private readonly object _sync = new();
    private FreeModeConfig? _current;

    public FreeModeConfigProvider(ILogger<FreeModeConfigProvider> logger, IOptionsMonitor<ProviderOptions> options)
    {
        _logger = logger;
        _options = options;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public FreeModeConfig Current
    {
        get
        {
            if (_current is null)
            {
                lock (_sync)
                {
                    _current ??= LoadConfig();
                }
            }

            return _current;
        }
    }

    public Task<FreeModeConfig> ReloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _current = LoadConfig();
        }

        return Task.FromResult(_current);
    }

    private FreeModeConfig LoadConfig()
    {
        var path = ResolvePath(_options.CurrentValue.FreeModeConfigPath);
        try
        {
            _logger.LogInformation("Loading FREE mode configuration from {ConfigPath}", path);
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream);
            var yamlText = reader.ReadToEnd();
            var config = _deserializer.Deserialize<FreeModeConfig>(yamlText);
            return config ?? new FreeModeConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load FREE mode configuration from {ConfigPath}; falling back to defaults", path);
            return new FreeModeConfig();
        }
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var basePath = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(basePath, configuredPath));
    }
}
