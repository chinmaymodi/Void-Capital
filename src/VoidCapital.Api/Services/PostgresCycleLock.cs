using Npgsql;

namespace VoidCapital.Api.Services;

/// <summary>Lease on the daily-cycle advisory lock; releases it on dispose.</summary>
public interface ICycleLease : IAsyncDisposable { }

/// <summary>
/// Cross-instance execution lock for the daily cycle (DS1). Two API hosts
/// would otherwise both fire the cycle and both catch up on startup; the lock
/// guarantees exactly one runner at a time.
/// </summary>
public interface ICycleLock
{
    /// <summary>
    /// Tries to acquire the lock without blocking. Returns null when another
    /// instance holds it (the caller should skip the run).
    /// </summary>
    Task<ICycleLease?> TryAcquireAsync(CancellationToken ct = default);
}

/// <summary>
/// Postgres session-level advisory lock on a dedicated connection. The lease
/// unlocks BEFORE the connection is disposed, so the lock never leaks onto a
/// pooled connection; if the process dies mid-run, the OS closes the socket
/// and Postgres releases the lock automatically.
/// </summary>
public class PostgresCycleLock : ICycleLock
{
    // Fixed key shared by every instance. 0x56434943 = "VCIC".
    private const long LockKey = 0x56434943;

    private readonly string _connectionString;

    public PostgresCycleLock(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ICycleLease?> TryAcquireAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            cmd.Parameters.AddWithValue("key", LockKey);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct))!;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }
            return new Lease(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease : ICycleLease
    {
        private NpgsqlConnection? _connection;

        public Lease(NpgsqlConnection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            if (_connection is null) return;
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
                cmd.Parameters.AddWithValue("key", LockKey);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
    }
}