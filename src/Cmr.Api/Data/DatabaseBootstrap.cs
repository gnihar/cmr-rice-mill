using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cmr.Api.Data;

public static class DatabaseBootstrap
{
    private const long LockKey = 4815162342;

    private static readonly string[] TenantTables =
    [
        "users", "devices", "seasons", "lots", "paddy_ros", "cmr_dos", "acceptances", "weighments",
        "documents", "bag_movements", "by_product_sales", "by_product_prices", "expenses", "receipts",
        "entry_reviews", "client_ops"
    ];

    public static async Task ApplyAsync(IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var ownerCs = Required(config, "ConnectionStrings:Owner");
        var appCs = Required(config, "ConnectionStrings:App");
        var fromUrl = string.Equals(config["Database:FromUrl"], "true", StringComparison.OrdinalIgnoreCase);
        var appPassword = First(config["CMR_APP_PASSWORD"], Environment.GetEnvironmentVariable("CMR_APP_PASSWORD"));

        await using var conn = new NpgsqlConnection(ownerCs);
        await conn.OpenAsync(ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", conn))
        {
            lockCmd.Parameters.AddWithValue("k", LockKey);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await EnsureRolesAsync(conn, fromUrl ? appPassword : null, logger, ct);

            var ownerOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ownerCs).Options;
            await using (var db = new AppDbContext(ownerOptions, new CurrentUser()))
                await db.Database.MigrateAsync(ct);

            await using (var cmd = new NpgsqlCommand(SecuritySql(), conn))
                await cmd.ExecuteNonQueryAsync(ct);

            await DatabaseBootstrap.AssertAppRole(appCs, ct);

            if (config.GetValue("Seed:Demo", false))
            {
                logger.LogWarning("SEED_DEMO is on. Loading the synthetic demo mill. Turn SEED_DEMO off after the first boot if this database will hold real records.");
                await using var db = new AppDbContext(ownerOptions, new CurrentUser());
                await DemoSeeder.SeedAsync(db, config["Storage:Root"] ?? "data/files", ct);
            }
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", conn);
            unlock.Parameters.AddWithValue("k", LockKey);
            await unlock.ExecuteNonQueryAsync(ct);
        }
    }

    public static async Task AssertAppRole(string appCs, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(appCs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT current_user, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Could not read the application database role.");
        var name = reader.GetString(0);
        if (name != "cmr_app" || reader.GetBoolean(1) || reader.GetBoolean(2))
            throw new InvalidOperationException(
                $"Refusing to start as database role '{name}'. The app must use cmr_app, which is not a superuser and cannot bypass row level security.");
    }

    /// <summary>
    /// Creates cmr_migrator and cmr_app when missing. Never alters an existing cmr_app:
    /// Neon’s neondb_owner is not a superuser and ALTER ROLE returns 42501, which took down the second boot.
    /// </summary>
    internal static async Task EnsureRolesAsync(NpgsqlConnection conn, string? appPassword, ILogger logger, CancellationToken ct)
    {
        await ExecuteIgnoringPermission(conn, logger, """
            DO $$ BEGIN
              BEGIN
                CREATE ROLE cmr_migrator NOLOGIN;
              EXCEPTION WHEN duplicate_object OR unique_violation THEN
                NULL;
              END;
            END $$;
            """, "create cmr_migrator", ct);
        if (!await Scalar<bool>(conn, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_migrator')", ct))
            throw new InvalidOperationException("Role cmr_migrator does not exist, and this database user cannot create it.");

        var exists = await Scalar<bool>(conn, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_app')", ct);
        if (!exists)
        {
            if (string.IsNullOrWhiteSpace(appPassword))
                throw new InvalidOperationException("Role cmr_app does not exist. Set CMR_APP_PASSWORD so startup can create it, or create the role before boot.");

            var quoted = await Scalar<string>(conn, "SELECT format('%L', @pwd)", ct, ("pwd", appPassword));
            try
            {
                await Execute(conn, "CREATE ROLE cmr_app LOGIN PASSWORD " + quoted + " NOSUPERUSER NOBYPASSRLS NOCREATEDB", ct);
            }
            catch (PostgresException ex) when (ex.SqlState is "42501" or "42710" or "23505")
            {
                if (ex.SqlState is "42710" or "23505")
                {
                    logger.LogWarning("Role cmr_app was created concurrently ({SqlState}). Continuing.", ex.SqlState);
                }
                else
                {
                    logger.LogWarning(ex, "CREATE ROLE cmr_app with role attributes was denied. Retrying with LOGIN and PASSWORD only.");
                    try
                    {
                        await Execute(conn, "CREATE ROLE cmr_app LOGIN PASSWORD " + quoted, ct);
                    }
                    catch (PostgresException retry) when (retry.SqlState is "42710" or "23505")
                    {
                        logger.LogWarning("Role cmr_app was created concurrently ({SqlState}). Continuing.", retry.SqlState);
                    }
                }
            }
        }
        else
        {
            logger.LogWarning(
                "Role cmr_app already exists. Startup will not ALTER ROLE. Neon denies that for neondb_owner (SQLSTATE 42501). The password set when the role was created stays in effect.");
        }

        try
        {
            await Execute(conn, "GRANT cmr_migrator TO CURRENT_USER", ct);
        }
        catch (PostgresException ex) when (ex.SqlState is "42501" or "0LP01" or "42710")
        {
            logger.LogWarning("Could not grant cmr_migrator to the current user ({SqlState}). Continuing.", ex.SqlState);
        }
    }

    private static async Task ExecuteIgnoringPermission(NpgsqlConnection conn, ILogger logger, string sql, string action, CancellationToken ct)
    {
        try
        {
            await Execute(conn, sql, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is "42501" or "42710")
        {
            logger.LogWarning("Skipped {Action} ({SqlState}). Continuing.", action, ex.SqlState);
        }
    }

    private static string SecuritySql()
    {
        var sql = new System.Text.StringBuilder();
        sql.AppendLine("""
            DO $$ BEGIN
              EXECUTE format('GRANT CONNECT ON DATABASE %I TO cmr_app', current_database());
            END $$;
            GRANT USAGE ON SCHEMA public TO cmr_app;
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO cmr_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO cmr_app;
            REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA public FROM cmr_app;
            REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM cmr_app;

            CREATE OR REPLACE FUNCTION audit_log_block_change()
            RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
              RAISE EXCEPTION 'audit_log is append-only';
            END $fn$;

            DROP TRIGGER IF EXISTS audit_log_no_update ON audit_log;
            CREATE TRIGGER audit_log_no_update
              BEFORE UPDATE OR DELETE ON audit_log
              FOR EACH ROW EXECUTE FUNCTION audit_log_block_change();

            CREATE OR REPLACE FUNCTION app_lookup_login(p_phone text, p_username text)
            RETURNS TABLE (
              id uuid, tenant_id uuid, name text, username text, phone_e164 text, role text,
              pin_hash text, pin_fail_count integer, locked_until timestamptz, status text
            )
            LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public AS $fn$
              SELECT u.id, u.tenant_id, u.name, u.username, u.phone_e164, u.role, u.pin_hash,
                     u.pin_fail_count, u.locked_until, u.status
              FROM users u
              WHERE (p_phone IS NOT NULL AND u.phone_e164 = p_phone)
                 OR (p_username IS NOT NULL AND u.username = p_username)
              ORDER BY u.created_at
              LIMIT 1;
            $fn$;

            CREATE OR REPLACE FUNCTION app_lookup_device(p_hash text)
            RETURNS TABLE (
              id uuid, tenant_id uuid, user_id uuid, expires_at timestamptz, revoked_at timestamptz
            )
            LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public AS $fn$
              SELECT d.id, d.tenant_id, d.user_id, d.expires_at, d.revoked_at
              FROM devices d
              WHERE d.refresh_token_hash = p_hash
              LIMIT 1;
            $fn$;

            REVOKE ALL ON FUNCTION app_lookup_login(text, text) FROM PUBLIC;
            REVOKE ALL ON FUNCTION app_lookup_device(text) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION app_lookup_login(text, text) TO cmr_app;
            GRANT EXECUTE ON FUNCTION app_lookup_device(text) TO cmr_app;
            """);

        foreach (var table in TenantTables)
        {
            sql.AppendLine($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
            sql.AppendLine($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
            sql.AppendLine($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
            sql.AppendLine($"""
                CREATE POLICY tenant_isolation ON {table}
                  USING (pg_has_role(current_user, 'cmr_migrator', 'member') OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                  WITH CHECK (pg_has_role(current_user, 'cmr_migrator', 'member') OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        sql.AppendLine("""
            ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
            ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS audit_select ON audit_log;
            DROP POLICY IF EXISTS audit_insert ON audit_log;
            CREATE POLICY audit_select ON audit_log FOR SELECT
              USING (pg_has_role(current_user, 'cmr_migrator', 'member') OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY audit_insert ON audit_log FOR INSERT
              WITH CHECK (
                pg_has_role(current_user, 'cmr_migrator', 'member')
                OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR (tenant_id IS NULL AND action = 'login_failed'));
            """);
        return sql.ToString();
    }

    private static async Task Execute(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (T)result!;
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] ?? throw new InvalidOperationException($"Missing {key}.");

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
