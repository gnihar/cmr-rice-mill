using Npgsql;

namespace Cmr.Api.Data;

public static class DbConnections
{
    public sealed record Pair(string Owner, string App, bool FromDatabaseUrl);

    public static Pair Resolve(IConfiguration config)
    {
        var url = First(config["DATABASE_URL"], Environment.GetEnvironmentVariable("DATABASE_URL"));
        if (!string.IsNullOrWhiteSpace(url))
        {
            var owner = ToNpgsql(url);
            var password = First(config["CMR_APP_PASSWORD"], Environment.GetEnvironmentVariable("CMR_APP_PASSWORD"));
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("CMR_APP_PASSWORD is required when DATABASE_URL is set.");
            return new Pair(owner, WithUser(owner, "cmr_app", password), true);
        }

        var ownerCs = config.GetConnectionString("Owner");
        var appCs = config.GetConnectionString("App");
        if (string.IsNullOrWhiteSpace(ownerCs) || string.IsNullOrWhiteSpace(appCs))
            throw new InvalidOperationException("Set DATABASE_URL and CMR_APP_PASSWORD, or ConnectionStrings:Owner and ConnectionStrings:App.");
        return new Pair(ownerCs, appCs, false);
    }

    public static string ToNpgsql(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return text;

        var uri = new Uri(text);
        var info = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(info[0]),
            Password = info.Length > 1 ? Uri.UnescapeDataString(info[1]) : "",
            Database = uri.AbsolutePath.Trim('/'),
            SslMode = SslMode.Require,
            ChannelBinding = ChannelBinding.Disable,
            Timeout = 15,
            CommandTimeout = 30
        };
        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("sslmode", out var mode) && mode.Equals("disable", StringComparison.OrdinalIgnoreCase))
            builder.SslMode = SslMode.Disable;
        return builder.ConnectionString;
    }

    public static string WithUser(string connectionString, string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = username,
            Password = password
        };
        return builder.ConnectionString;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split('=', 2);
            result[Uri.UnescapeDataString(bits[0])] = bits.Length > 1 ? Uri.UnescapeDataString(bits[1]) : "";
        }
        return result;
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
