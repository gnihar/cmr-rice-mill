using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Cmr.Api.Auth;
using Cmr.Api.Data;
using Cmr.Api.Endpoints;
using Cmr.Api.Services;
using Cmr.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var seedEnv = Environment.GetEnvironmentVariable("SEED_DEMO");
var seed = seedEnv is null
    ? builder.Configuration.GetValue("Seed:Demo", false)
    : seedEnv.Equals("true", StringComparison.OrdinalIgnoreCase);
var jwt = First(Environment.GetEnvironmentVariable("JWT_KEY"), builder.Configuration["Jwt:Key"]);
if (string.IsNullOrWhiteSpace(jwt) && builder.Environment.IsDevelopment())
    jwt = "development-only-signing-key-not-for-deploy-0001";
if (builder.Environment.IsProduction() && (string.IsNullOrWhiteSpace(jwt) || jwt.Contains("development-only", StringComparison.Ordinal) || jwt.Length < 32))
    throw new InvalidOperationException("Set JWT_KEY to a random secret of at least 32 characters.");

var connections = DbConnections.Resolve(builder.Configuration);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:Owner"] = connections.Owner,
    ["ConnectionStrings:App"] = connections.App,
    ["Jwt:Key"] = jwt,
    ["Seed:Demo"] = seed ? "true" : "false",
    ["Database:FromUrl"] = connections.FromDatabaseUrl ? "true" : "false",
    ["Storage:Root"] = First(Environment.GetEnvironmentVariable("STORAGE_ROOT"), builder.Configuration["Storage:Root"]) ?? "data/files"
});

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<RlsConnectionInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("App"));
    options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());
});
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<MillService>();
builder.Services.AddScoped<OfficeService>();
builder.Services.AddScoped<ILoginFactor, PinLogin>();
builder.Services.AddScoped<IOtpSender, UnconfiguredOtpSender>();
builder.Services.AddScoped<ISessionIssuer, SessionIssuer>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddValidatorsFromAssemblyContaining<Cmr.Api.Validation.LoginValidator>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt ?? throw new InvalidOperationException("JWT key is missing.")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "cmr",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "cmr",
        IssuerSigningKey = signingKey,
        RoleClaimType = "role",
        NameClaimType = "name",
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (string.IsNullOrEmpty(context.Token) && context.Request.Cookies.TryGetValue(AuthCookies.Access, out var cookie))
                context.Token = cookie;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permit = builder.Configuration.GetValue("Login:RateLimitDisabled", false) ? 100_000 : builder.Configuration.GetValue("Login:PermitPerIp", 30);
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("Login:WindowMinutes", 10)),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "rate_limit", message = "बहुत कोशिशें। थोड़ी देर बाद। Too many attempts." }, token);
    };
});

var app = builder.Build();

await DatabaseBootstrap.ApplyAsync(app.Configuration, app.Logger, CancellationToken.None);

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try
    {
        await next();
    }
    catch (AppException ex)
    {
        if (ctx.Response.HasStarted) throw;
        ctx.Response.StatusCode = ex.Status;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Code, message = ex.Message });
    }
    catch (Cmr.Domain.DomainException ex)
    {
        if (ctx.Response.HasStarted) throw;
        ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Code, message = ex.Message });
    }
});

app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    var current = ctx.RequestServices.GetRequiredService<CurrentUser>();
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        current.UserId = Guid.Parse(ctx.User.FindFirstValue("sub")!);
        current.TenantId = Guid.Parse(ctx.User.FindFirstValue("tenant_id")!);
        current.Role = ctx.User.FindFirstValue("role") ?? "";
        if (Guid.TryParse(ctx.User.FindFirstValue("device_id"), out var device)) current.DeviceId = device;
    }
    await next();
});
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapCmrApi();
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("CMR register {Version} listening", AppVersion.Current);
app.Run();

static string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

public partial class Program;
