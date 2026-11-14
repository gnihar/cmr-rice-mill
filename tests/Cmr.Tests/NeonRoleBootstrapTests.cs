using Cmr.Api.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Cmr.Tests;

/// <summary>
/// Neon’s neondb_owner is not a superuser. ALTER ROLE cmr_app returns SQLSTATE 42501.
/// Startup must keep going when the role already exists.
/// </summary>
[Collection("api")]
public class NeonRoleBootstrapTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Existing_app_role_is_not_altered_by_a_non_superuser()
    {
        var adminCs = Environment.GetEnvironmentVariable("CMR_ADMIN_CS")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
        await using var admin = new NpgsqlConnection(adminCs);
        await admin.OpenAsync();
        await Exec(admin, """
            DO $$ BEGIN
              BEGIN
                CREATE ROLE cmr_app LOGIN PASSWORD 'cmr_app_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB;
              EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL;
              END;
              BEGIN
                CREATE ROLE limited_owner LOGIN PASSWORD 'limited_dev' NOSUPERUSER CREATEROLE NOBYPASSRLS;
              EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL;
              END;
            END $$;
            GRANT CONNECT ON DATABASE postgres TO limited_owner;
            """);

        var before = await Scalar<string>(admin, "SELECT rolpassword FROM pg_authid WHERE rolname = 'cmr_app'");
        Assert.False(string.IsNullOrEmpty(before));

        var builder = new NpgsqlConnectionStringBuilder(adminCs)
        {
            Username = "limited_owner",
            Password = "limited_dev",
            Database = "postgres"
        };
        await using var limited = new NpgsqlConnection(builder.ConnectionString);
        await limited.OpenAsync();

        var alter = await Assert.ThrowsAsync<PostgresException>(() =>
            Exec(limited, "ALTER ROLE cmr_app WITH LOGIN PASSWORD 'replaced-by-test' NOSUPERUSER NOBYPASSRLS NOCREATEDB"));
        Assert.Equal("42501", alter.SqlState);

        var logger = new WarningLogger(output);
        await DatabaseBootstrap.EnsureRolesAsync(limited, "a-different-password-not-applied", logger, CancellationToken.None);
        await DatabaseBootstrap.EnsureRolesAsync(limited, "a-different-password-not-applied", logger, CancellationToken.None);

        var after = await Scalar<string>(admin, "SELECT rolpassword FROM pg_authid WHERE rolname = 'cmr_app'");
        Assert.Equal(before, after);
        Assert.Contains(logger.Warnings, w => w.Contains("will not ALTER ROLE", StringComparison.Ordinal));

        await DropProbe(admin);
        await Exec(limited, "CREATE ROLE cmr_probe_boot LOGIN PASSWORD 'probe-pass' NOSUPERUSER NOBYPASSRLS NOCREATEDB");
        await Exec(admin, "GRANT CONNECT ON DATABASE postgres TO cmr_probe_boot");
        var probe = new NpgsqlConnectionStringBuilder(adminCs)
        {
            Username = "cmr_probe_boot",
            Password = "probe-pass",
            Database = "postgres"
        };
        await using (var probeConn = new NpgsqlConnection(probe.ConnectionString))
            await probeConn.OpenAsync();
        await DropProbe(admin);
    }

    private static Task DropProbe(NpgsqlConnection admin) => Exec(admin, """
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_probe_boot') THEN
            EXECUTE 'REVOKE ALL PRIVILEGES ON DATABASE postgres FROM cmr_probe_boot';
            DROP ROLE cmr_probe_boot;
          END IF;
        END $$;
        """);

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed class WarningLogger(ITestOutputHelper output) : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel >= LogLevel.Warning) Warnings.Add(message);
            output.WriteLine(message);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
