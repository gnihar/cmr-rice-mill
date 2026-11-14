using System.Diagnostics;
using System.Net;
using Microsoft.Playwright;
using Npgsql;
using Xunit;

namespace Cmr.E2E;

public class Flows : IAsyncLifetime
{
    private Process? _proc;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private const string Base = "http://127.0.0.1:5088";

    public async Task InitializeAsync()
    {
        var admin = Environment.GetEnvironmentVariable("CMR_ADMIN_CS") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await Exec(conn, """
                DO $$ BEGIN
                  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_migrator') THEN CREATE ROLE cmr_migrator NOLOGIN; END IF;
                  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_owner') THEN CREATE ROLE cmr_owner LOGIN PASSWORD 'cmr_owner_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB; END IF;
                  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cmr_app') THEN CREATE ROLE cmr_app LOGIN PASSWORD 'cmr_app_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB; END IF;
                END $$;
                GRANT cmr_migrator TO cmr_owner WITH ADMIN OPTION;
                """);
            await Exec(conn, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'cmr_e2e' AND pid <> pg_backend_pid()");
            await Exec(conn, "DROP DATABASE IF EXISTS cmr_e2e");
            await Exec(conn, "CREATE DATABASE cmr_e2e OWNER cmr_owner");
            await Exec(conn, "GRANT CONNECT ON DATABASE cmr_e2e TO cmr_app, cmr_owner");
        }

        var storage = Path.Combine(Path.GetTempPath(), "cmr-e2e-files");
        Directory.CreateDirectory(storage);
        var config = Environment.GetEnvironmentVariable("CMR_E2E_CONFIG") ?? "Debug";
        var psi = new ProcessStartInfo("dotnet", $"run --no-build -c {config} --project src/Cmr.Api/Cmr.Api.csproj --urls http://127.0.0.1:5088")
        {
            WorkingDirectory = FindRepo(),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = Base;
        psi.Environment["SEED_DEMO"] = "true";
        psi.Environment["ConnectionStrings__Owner"] = "Host=localhost;Port=5432;Database=cmr_e2e;Username=cmr_owner;Password=cmr_owner_dev";
        psi.Environment["ConnectionStrings__App"] = "Host=localhost;Port=5432;Database=cmr_e2e;Username=cmr_app;Password=cmr_app_dev";
        psi.Environment["Jwt__Key"] = "e2e-signing-key-0123456789abcdef-not-for-deploy";
        psi.Environment["Storage__Root"] = storage;
        psi.Environment["Login__RateLimitDisabled"] = "true";
        _proc = Process.Start(psi)!;
        await WaitHealth();
        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_proc is { HasExited: false })
        {
            _proc.Kill(entireProcessTree: true);
            await _proc.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task Owner_and_staff_can_work_a_lot_and_staff_cannot_open_money()
    {
        var shots = Directory.Exists("/opt/cursor/artifacts") ? "/opt/cursor/artifacts" : Path.GetTempPath();
        await using var phone = await _browser!.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await phone.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.GotoAsync(Base);
        await page.GetByTestId("home-login").WaitForAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "home.png"), FullPage = true });
        await page.GetByTestId("home-login").ClickAsync();
        await page.GetByTestId("login-form").WaitForAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "login.png"), FullPage = true });

        await page.GetByTestId("login-identifier").FillAsync("staff");
        await page.GetByTestId("login-pin").FillAsync("1357");
        await page.GetByTestId("login-submit").ClickAsync();
        await page.GetByTestId("staff-home").WaitForAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "staff-home.png"), FullPage = true });
        var money = await page.Context.APIRequest.GetAsync(Base + "/api/expenses");
        Assert.Equal(404, money.Status);

        await page.GetByTestId("nav-new-lot").ClickAsync();
        var lotNo = "E2E-" + DateTime.UtcNow.ToString("HHmmss");
        await page.GetByTestId("lot-no").FillAsync(lotNo);
        await page.GetByTestId("lot-centre").FillAsync("Demo Kendra");
        await page.GetByTestId("lot-truck").FillAsync("MP09E20001");
        await page.GetByTestId("lot-paddy").FillAsync("25");
        await page.GetByTestId("save-lot").ClickAsync();
        await page.GetByTestId("add-paddy").WaitForAsync();
        await page.GetByTestId("add-paddy").ClickAsync();
        await page.GetByTestId("paddy-form").WaitForAsync();
        await Assertions.Expect(page.GetByTestId("truck")).ToHaveValueAsync("MP09E20001");
        await page.GetByTestId("ro-no").FillAsync("PDY26000000007701");
        await page.GetByTestId("qty").FillAsync("25");
        await page.GetByTestId("bags-new").FillAsync("6");
        await page.GetByTestId("bags-old").FillAsync("4");
        await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "paddy-entry.png"), FullPage = true });
        await page.GetByTestId("save-paddy").ClickAsync();
        await page.GetByTestId("lot-title").WaitForAsync();
        await page.GetByText("लॉक").WaitForAsync();

        await page.GetByTestId("logout").ClickAsync();
        await page.GetByTestId("home-login").WaitForAsync();

        await using var desk = await _browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var owner = await desk.NewPageAsync();
        owner.SetDefaultTimeout(60_000);
        await owner.GotoAsync(Base + "/login");
        await owner.GetByTestId("login-identifier").FillAsync("owner");
        await owner.GetByTestId("login-pin").FillAsync("246810");
        await owner.GetByTestId("login-submit").ClickAsync();
        await owner.GetByTestId("owner-dashboard").WaitForAsync();
        await owner.GetByTestId("money-today").WaitForAsync();
        await owner.ScreenshotAsync(new() { Path = Path.Combine(shots, "owner-dashboard.png"), FullPage = true });
        await owner.GetByRole(AriaRole.Link, new() { Name = "SK-12" }).ClickAsync();
        await owner.GetByTestId("lot-title").WaitForAsync();
        await owner.ScreenshotAsync(new() { Path = Path.Combine(shots, "lot-detail.png"), FullPage = true });
        Assert.Equal("complete", await owner.GetByTestId("lot-completeness").GetAttributeAsync("data-state"));
    }

    private async Task WaitHealth()
    {
        using var http = new HttpClient();
        var until = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < until)
        {
            if (_proc is { HasExited: true })
            {
                var err = await _proc.StandardError.ReadToEndAsync();
                throw new InvalidOperationException("App exited early. " + err);
            }
            try
            {
                var response = await http.GetAsync(Base + "/api/health");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch { }
            await Task.Delay(500);
        }
        throw new TimeoutException("App did not become healthy.");
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cmr.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repo root.");
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
