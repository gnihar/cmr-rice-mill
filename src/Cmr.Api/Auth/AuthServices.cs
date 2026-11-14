using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cmr.Api.Data;
using Cmr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Cmr.Api.Auth;

/// <summary>A step that proves identity. MVP-1 registers only <see cref="PinLogin"/>.</summary>
public interface ILoginFactor
{
    string Name { get; }
}

public sealed class PinLogin : ILoginFactor
{
    public string Name => "pin";
}

/// <summary>
/// SMS one-time codes. Nothing in MVP-1 calls this.
/// When DLT registration is done, register an MSG91 implementation and add a challenge step in front of <see cref="ISessionIssuer"/>.
/// </summary>
public interface IOtpSender
{
    Task SendAsync(string phoneE164, string code, CancellationToken cancellationToken);
}

public sealed class UnconfiguredOtpSender : IOtpSender
{
    public Task SendAsync(string phoneE164, string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "OTP SMS is not enabled. Add a provider after DLT registration; do not call this in MVP-1.");
}

public sealed record SessionTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpires, DateTimeOffset RefreshExpires, string Role, string Name);

/// <summary>Issues the cookie session after a factor succeeds. OTP can call this later without a second session design.</summary>
public interface ISessionIssuer
{
    Task<SessionTokens> IssueAsync(Guid userId, string? userAgent, CancellationToken ct);
}

public interface ILoginService
{
    Task<SessionTokens> LoginWithPinAsync(string identifier, string pin, string? userAgent, CancellationToken ct);
    Task<SessionTokens> RefreshAsync(string refreshToken, string? userAgent, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
}

public sealed class AppException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class Messages
{
    public const string InvalidLogin = "मोबाइल, यूज़रनेम या पिन गलत है। Wrong mobile, username, or PIN.";
    public const string Locked = "पिन कई बार गलत हुआ। 15 मिनट बाद कोशिश करें। Too many wrong PINs. Try again in 15 minutes.";
    public const string Duplicate = "यह नंबर पहले से दर्ज है। This number is already used.";
    public const string LockedEntry = "यह प्रविष्टि लॉक है। मालिक वापस भेजें, फिर सुधार करें। This entry is locked until the owner sends it back.";
    public const string NeedDo = "स्वीकृति से पहले जमा आदेश चाहिए। Add the deposit order before the acceptance.";
    public const string NotFound = "रिकॉर्ड नहीं मिला। Not found.";
    public const string BadPin = "पिन अंकों में होना चाहिए। मालिक का पिन 6 अंक, कर्मचारी का 4 से 6 अंक। PIN must be digits: 6 for the owner, 4 to 6 for staff.";
}

public sealed class SessionIssuer(AppDbContext db, IConfiguration config) : ISessionIssuer
{
    public async Task<SessionTokens> IssueAsync(Guid userId, string? userAgent, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var device = new Device
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            Label = string.IsNullOrWhiteSpace(userAgent) ? "browser" : userAgent.Trim()[..Math.Min(userAgent.Trim().Length, 160)],
            RefreshTokenHash = Hash(refresh),
            LastSeenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        db.Devices.Add(device);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.PinFailCount = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(ct);

        var accessMinutes = config.GetValue("Jwt:AccessMinutes", 15);
        var access = WriteAccessToken(user, device.Id, DateTime.UtcNow.AddMinutes(accessMinutes));
        return new SessionTokens(access, refresh, DateTimeOffset.UtcNow.AddMinutes(accessMinutes), device.ExpiresAt, user.Role, user.Name);
    }

    public string WriteAccessToken(User user, Guid deviceId, DateTime expires)
    {
        var key = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not set.");
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            config["Jwt:Issuer"] ?? "cmr",
            config["Jwt:Audience"] ?? "cmr",
            [
                new Claim("sub", user.Id.ToString()),
                new Claim("tenant_id", user.TenantId.ToString()),
                new Claim("role", user.Role),
                new Claim("name", user.Name),
                new Claim("device_id", deviceId.ToString())
            ],
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string Hash(string refresh) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh)));
}

public sealed class LoginService(
    AppDbContext db,
    CurrentUser current,
    ISessionIssuer sessions,
    IConfiguration config,
    ILogger<LoginService> logger) : ILoginService
{
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-pin", workFactor: 10);

    public async Task<SessionTokens> LoginWithPinAsync(string identifier, string pin, string? userAgent, CancellationToken ct)
    {
        if (!LoginNames.TryParse(identifier, out var phone, out var username))
        {
            BCrypt.Net.BCrypt.Verify(pin ?? "", DummyHash);
            throw new AppException(401, "invalid_login", Messages.InvalidLogin);
        }

        var found = await LookupAsync(phone, username, ct);
        if (found is null)
        {
            BCrypt.Net.BCrypt.Verify(pin ?? "", DummyHash);
            db.AuditLogs.Add(FailAudit(null, null));
            await db.SaveChangesAsync(ct);
            throw new AppException(401, "invalid_login", Messages.InvalidLogin);
        }

        current.TenantId = found.TenantId;
        current.UserId = found.Id;
        await RlsConnectionInterceptor.ApplyAsync(db.Database.GetDbConnection(), found.TenantId, ct);

        if (found.Status != "active")
        {
            BCrypt.Net.BCrypt.Verify(pin ?? "", DummyHash);
            db.AuditLogs.Add(FailAudit(found.TenantId, found.Id));
            await db.SaveChangesAsync(ct);
            throw new AppException(401, "invalid_login", Messages.InvalidLogin);
        }

        var user = await db.Users.FirstAsync(u => u.Id == found.Id, ct);
        if (user.LockedUntil is DateTimeOffset until && until > DateTimeOffset.UtcNow)
            throw new AppException(423, "locked", Messages.Locked);

        if (!BCrypt.Net.BCrypt.Verify(pin ?? "", user.PinHash))
        {
            var max = config.GetValue("Login:MaxFailures", 5);
            var minutes = config.GetValue("Login:LockoutMinutes", 15);
            user.PinFailCount++;
            if (user.PinFailCount >= max)
            {
                user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(minutes);
                user.PinFailCount = 0;
            }
            db.AuditLogs.Add(FailAudit(user.TenantId, user.Id));
            await db.SaveChangesAsync(ct);
            if (user.LockedUntil is not null)
                throw new AppException(423, "locked", Messages.Locked);
            throw new AppException(401, "invalid_login", Messages.InvalidLogin);
        }

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            Ts = DateTimeOffset.UtcNow,
            ActorUserId = user.Id,
            Action = "login",
            EntityType = "user",
            EntityId = user.Id
        });
        logger.LogInformation("PIN login succeeded for user {UserId}", user.Id);
        return await sessions.IssueAsync(user.Id, userAgent, ct);
    }

    public async Task<SessionTokens> RefreshAsync(string refreshToken, string? userAgent, CancellationToken ct)
    {
        var hash = SessionIssuer.Hash(refreshToken);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, tenant_id, user_id, expires_at, revoked_at FROM app_lookup_device(@hash)";
        var p = cmd.CreateParameter();
        p.ParameterName = "hash";
        p.Value = hash;
        cmd.Parameters.Add(p);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new AppException(401, "invalid_login", Messages.InvalidLogin);
        var deviceId = reader.GetGuid(0);
        var tenantId = reader.GetGuid(1);
        var userId = reader.GetGuid(2);
        var expires = reader.GetFieldValue<DateTimeOffset>(3);
        var revoked = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4);
        await reader.DisposeAsync();

        if (revoked is not null || expires <= DateTimeOffset.UtcNow)
            throw new AppException(401, "invalid_login", Messages.InvalidLogin);

        current.TenantId = tenantId;
        current.UserId = userId;
        await RlsConnectionInterceptor.ApplyAsync(conn, tenantId, ct);

        var device = await db.Devices.FirstAsync(d => d.Id == deviceId, ct);
        device.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await sessions.IssueAsync(userId, userAgent, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        try
        {
            var hash = SessionIssuer.Hash(refreshToken);
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, tenant_id FROM app_lookup_device(@hash)";
            var p = cmd.CreateParameter();
            p.ParameterName = "hash";
            p.Value = hash;
            cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return;
            var deviceId = reader.GetGuid(0);
            var tenantId = reader.GetGuid(1);
            await reader.DisposeAsync();
            current.TenantId = tenantId;
            await RlsConnectionInterceptor.ApplyAsync(conn, tenantId, ct);
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is null || device.RevokedAt is not null) return;
            device.RevokedAt = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                Ts = DateTimeOffset.UtcNow,
                ActorUserId = device.UserId,
                DeviceId = device.Id,
                Action = "logout",
                EntityType = "device",
                EntityId = device.Id
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Logout could not revoke the refresh token.");
        }
    }

    private async Task<LookedUp?> LookupAsync(string? phone, string? username, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, tenant_id, name, username, phone_e164, role, pin_hash, pin_fail_count, locked_until, status
            FROM app_lookup_login(@phone, @username)
            """;
        var phoneParam = cmd.CreateParameter();
        phoneParam.ParameterName = "phone";
        phoneParam.Value = (object?)phone ?? DBNull.Value;
        cmd.Parameters.Add(phoneParam);
        var userParam = cmd.CreateParameter();
        userParam.ParameterName = "username";
        userParam.Value = (object?)username ?? DBNull.Value;
        cmd.Parameters.Add(userParam);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new LookedUp(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetString(9));
    }

    private static AuditLog FailAudit(Guid? tenant, Guid? user) => new()
    {
        TenantId = tenant,
        Ts = DateTimeOffset.UtcNow,
        ActorUserId = user,
        Action = "login_failed",
        EntityType = "user",
        EntityId = user
    };

    private sealed record LookedUp(
        Guid Id, Guid TenantId, string Name, string? Username, string? Phone, string Role,
        string PinHash, int PinFailCount, DateTimeOffset? LockedUntil, string Status);
}

public static class PinHasher
{
    public static string Hash(string pin) => BCrypt.Net.BCrypt.HashPassword(pin, workFactor: 10);

    public static void EnsurePolicy(string pin, string role)
    {
        if (pin.Length == 0 || pin.Any(c => !char.IsDigit(c)))
            throw new AppException(400, "bad_pin", Messages.BadPin);
        if (role == "owner" && pin.Length != 6)
            throw new AppException(400, "bad_pin", Messages.BadPin);
        if (role == "staff" && pin.Length is < 4 or > 6)
            throw new AppException(400, "bad_pin", Messages.BadPin);
    }
}

public static class AuthCookies
{
    public const string Access = "cmr_access";
    public const string Refresh = "cmr_refresh";

    public static void Write(HttpContext http, SessionTokens tokens)
    {
        http.Response.Cookies.Append(Access, tokens.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = tokens.AccessExpires - DateTimeOffset.UtcNow
        });
        http.Response.Cookies.Append(Refresh, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            MaxAge = tokens.RefreshExpires - DateTimeOffset.UtcNow
        });
    }

    public static void Clear(HttpContext http)
    {
        http.Response.Cookies.Delete(Access, new CookieOptions { Path = "/" });
        http.Response.Cookies.Delete(Refresh, new CookieOptions { Path = "/api/auth" });
    }
}
