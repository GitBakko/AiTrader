using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Options;

namespace Orchestrator.Persistence;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly IOptionsMonitor<DatabaseOptions> _options;
    private readonly ILogger<SqlConnectionFactory> _logger;

    public SqlConnectionFactory(IOptionsMonitor<DatabaseOptions> options, ILogger<SqlConnectionFactory> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = ResolveConnectionString();
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            SqlConnection? connection = null;
            try
            {
                connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Opened SQL connection {DataSource} on attempt {Attempt}", connection.DataSource, attempt);
                return connection;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                connection?.Dispose();
                _logger.LogWarning(ex, "Transient failure opening SQL connection (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}.", attempt, maxAttempts, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
            }
            catch
            {
                connection?.Dispose();
                throw;
            }
        }

        // Should not reach here because final attempt either returns or throws.
        throw new InvalidOperationException("Failed to open SQL connection after retries.");
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is SqlException sqlException)
        {
            foreach (SqlError error in sqlException.Errors)
            {
                switch (error.Number)
                {
                    case -2:   // Timeout expired
                    case 53:   // Network-related or instance-specific error
                    case 4060: // Cannot open database requested by the login
                    case 18456: // Login failed for user
                    case 10054: // Transport-level error
                        return true;
                }
            }

            return sqlException.IsTransient;
        }

        if (exception is InvalidOperationException invalidOperation &&
            invalidOperation.InnerException is SqlException innerSql)
        {
            return IsTransient(innerSql);
        }

        // Account for localized error messages that include "instance" failures.
        if (exception is InvalidOperationException invalid &&
            invalid.Message.Contains("errore dell'istanza", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private string ResolveConnectionString()
    {
        var configured = _options.CurrentValue.ConnectionString;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var env = Environment.GetEnvironmentVariable("ORCHESTRATOR_SQL_CONNECTION") ??
                  Environment.GetEnvironmentVariable("SQLSERVER_CONNECTIONSTRING") ??
                  Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");

        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        throw new InvalidOperationException("Database connection string is not configured. Provide Database:ConnectionString in settings or ORCHESTRATOR_SQL_CONNECTION environment variable.");
    }
}
