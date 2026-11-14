using System.Net;
using System.Net.Http.Json;
using Xunit;
using System.Text.Json;
using Cmr.Api.Auth;
using Cmr.Api.Data;
using Cmr.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Cmr.Tests;

[Collection("api")]
public class ApiTests(ApiFixture fx)
{
    [Fact]
    public async Task Pin_login_accepts_username_and_phone()
    {
        var mill = await fx.MakeMillAsync();
        var byName = await fx.LoginAsync(mill.OwnerUser, mill.Pin);
        Assert.Equal("owner", (await byName.GetFromJsonAsync<MeDto>("/api/auth/me"))!.Role);
        var byPhone = await fx.LoginAsync(mill.OwnerPhone[3..], mill.Pin);
        Assert.Equal("owner", (await byPhone.GetFromJsonAsync<MeDto>("/api/auth/me"))!.Role);
    }

    [Fact]
    public async Task Five_bad_pins_lock_the_account()
    {
        var mill = await fx.MakeMillAsync();
        for (var i = 0; i < 4; i++)
        {
            var bad = await fx.PostLogin(mill.StaffUser, "0000");
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }
        var locked = await fx.PostLogin(mill.StaffUser, "0000");
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        var still = await fx.PostLogin(mill.StaffUser, mill.StaffPin);
        Assert.Equal((HttpStatusCode)423, still.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_read_money_through_the_api()
    {
        var mill = await fx.MakeMillAsync();
        var owner = await fx.LoginAsync(mill.OwnerUser, mill.Pin);
        var expense = await owner.PostAsJsonAsync("/api/expenses", new ExpenseWrite(new DateOnly(2026, 11, 14), "labour", 10, "cash", "x", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, expense.StatusCode);
        var sale = await owner.PostAsJsonAsync("/api/by-products", new ByProductWrite(new DateOnly(2026, 11, 14), "husk", 2, "qtl", "Buyer", "MP09SK1001", null, null, Guid.NewGuid()));
        var saleBody = await sale.Content.ReadFromJsonAsync<WriteResultDto>();
        Assert.Equal(HttpStatusCode.OK, await owner.PutAsJsonAsync($"/api/by-products/{saleBody!.Id}/price", new PriceWrite(10, 20, 0, "due")).ContinueWith(t => t.Result.StatusCode));

        var staff = await fx.LoginAsync(mill.StaffUser, mill.StaffPin);
        foreach (var path in new[] { "/api/expenses", "/api/receipts", "/api/reports/lots.csv", "/api/reports/lots.pdf", "/api/users", "/api/audit" })
        {
            var got = await staff.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, got.StatusCode);
        }
        var posted = await staff.PostAsJsonAsync("/api/expenses", new ExpenseWrite(new DateOnly(2026, 11, 14), "fuel", 5, "cash", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        var priced = await staff.PutAsJsonAsync($"/api/by-products/{saleBody.Id}/price", new PriceWrite(1, 1, 1, "paid"));
        Assert.Equal(HttpStatusCode.NotFound, priced.StatusCode);

        foreach (var path in new[] { "/api/by-products", "/api/dashboard", "/api/lots" })
        {
            var json = await staff.GetStringAsync(path);
            AssertNoMoney(json);
        }
        Assert.Contains("amount", await owner.GetStringAsync("/api/expenses"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Other_mill_cannot_read_this_mills_lot_and_rls_hides_it()
    {
        var a = await fx.MakeMillAsync();
        var b = await fx.MakeMillAsync();
        var ownerA = await fx.LoginAsync(a.OwnerUser, a.Pin);
        var created = await ownerA.PostAsJsonAsync("/api/lots", new LotWrite("ISO-1", "Centre A", null, "MP09AA0001", 10, 67, null, null, null, null, null));
        var lot = await created.Content.ReadFromJsonAsync<WriteResultDto>();
        var ownerB = await fx.LoginAsync(b.OwnerUser, b.Pin);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/lots/{lot!.Id}")).StatusCode);

        await using var conn = new NpgsqlConnection(fx.AppCs);
        await conn.OpenAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @id, false)", conn))
        {
            set.Parameters.AddWithValue("id", b.TenantId.ToString());
            await set.ExecuteNonQueryAsync();
        }
        await using var count = new NpgsqlCommand("SELECT count(*) FROM lots WHERE id = @id", conn);
        count.Parameters.AddWithValue("id", lot.Id);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);

        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @id, false)", conn))
        {
            set.Parameters.AddWithValue("id", a.TenantId.ToString());
            await set.ExecuteNonQueryAsync();
        }
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Audit_log_rejects_updates()
    {
        var mill = await fx.MakeMillAsync();
        await fx.LoginAsync(mill.OwnerUser, mill.Pin);
        await using var conn = new NpgsqlConnection(fx.AppCs);
        await conn.OpenAsync();
        await using var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @id, false)", conn);
        set.Parameters.AddWithValue("id", mill.TenantId.ToString());
        await set.ExecuteNonQueryAsync();
        await using var update = new NpgsqlCommand("UPDATE audit_log SET action = 'tamper'", conn);
        await Assert.ThrowsAnyAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Papers_follow_the_mill_rules()
    {
        var mill = await fx.MakeMillAsync();
        var owner = await fx.LoginAsync(mill.OwnerUser, mill.Pin);
        var created = await owner.PostAsJsonAsync("/api/lots", new LotWrite("RULE-1", "Demo Kendra", null, "MP09SK1001", 100, 67, null, null, null, Guid.NewGuid(), null));
        var lot = await created.Content.ReadFromJsonAsync<WriteResultDto>();
        var tooSoon = await owner.PutAsJsonAsync($"/api/lots/{lot!.Id}/acceptance", Acceptance("CMRA26000000000999"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooSoon.StatusCode);

        var odd = await owner.PutAsJsonAsync($"/api/lots/{lot.Id}/ro", Ro("NOT-A-PDY"));
        Assert.Equal(HttpStatusCode.OK, odd.StatusCode);
        Assert.Contains("ro_format", (await odd.Content.ReadFromJsonAsync<WriteResultDto>())!.Warnings);
        var duplicate = await owner.PutAsJsonAsync($"/api/lots/{Guid.NewGuid()}/ro", Ro("NOT-A-PDY"));
        Assert.True(duplicate.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict);

        var second = await owner.PostAsJsonAsync("/api/lots", new LotWrite("RULE-2", "Demo Kendra", null, "MP09SK1001", 10, 67, null, null, null, null, null));
        var lot2 = await second.Content.ReadFromJsonAsync<WriteResultDto>();
        var clash = await owner.PutAsJsonAsync($"/api/lots/{lot2!.Id}/ro", Ro("NOT-A-PDY"));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        var dob = await owner.PutAsJsonAsync($"/api/lots/{lot.Id}/do", new DoWrite("CDO26000000000991", new DateOnly(2026, 11, 12), "Demo Godown", 67, "MP09SK1001", null, null, null, null, null, "Demo Kendra", null, "RULE-1", null, null));
        var doBody = await dob.Content.ReadFromJsonAsync<WriteResultDto>();
        var acc = await owner.PutAsJsonAsync($"/api/lots/{lot.Id}/acceptance", Acceptance("CMRA26000000000991"));
        var accBody = await acc.Content.ReadFromJsonAsync<WriteResultDto>();
        var kaanta = await owner.PostAsJsonAsync($"/api/lots/{lot.Id}/weighments", new WeighmentWrite("rice_out", "MP09SK1001", "880011", false, 8000, 1300, new DateTimeOffset(2026, 11, 14, 6, 0, 0, TimeSpan.Zero), null, null, null, null));
        var kaantaBody = await kaanta.Content.ReadFromJsonAsync<WriteResultDto>();
        var roBody = await (await owner.PutAsJsonAsync($"/api/lots/{lot.Id}/ro", Ro("PDY26000000000991"))).Content.ReadFromJsonAsync<WriteResultDto>();
        await Upload(owner, "paddy_ro", roBody!.Id);
        await Upload(owner, "cmr_do", doBody!.Id);
        await Upload(owner, "acceptance", accBody!.Id);
        await Upload(owner, "weighment", kaantaBody!.Id);

        var detail = await owner.GetFromJsonAsync<LotDetailDto>($"/api/lots/{lot.Id}");
        Assert.True(detail!.Check.IsComplete);
        Assert.Equal(67.00m, detail.Balance.RiceDueQtl);

        var staff = await fx.LoginAsync(mill.StaffUser, mill.StaffPin);
        var staffLot = await staff.PostAsJsonAsync("/api/lots", new LotWrite("STAFF-1", "Demo Kendra", null, "MP09SK2002", 5, 67, null, null, null, null, null));
        var staffBody = await staffLot.Content.ReadFromJsonAsync<WriteResultDto>();
        Assert.Equal("submitted", staffBody!.EntryStatus);
        var locked = await staff.PutAsJsonAsync($"/api/lots/{staffBody.Id}", new LotWrite("STAFF-1", "Other", null, "MP09SK2002", 5, 67, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        var sent = await owner.PostAsJsonAsync("/api/entries/send-back", new SendBackRequest("lot", staffBody.Id, "ट्रक जाँचो"));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var edited = await staff.PutAsJsonAsync($"/api/lots/{staffBody.Id}", new LotWrite("STAFF-1", "Demo Kendra", null, "MP09SK2002", 8, 67, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var op = Guid.NewGuid();
        var once = await owner.PostAsJsonAsync("/api/lots", new LotWrite("ONCE-1", "Demo Kendra", null, null, null, 67, null, null, null, op, null));
        var twice = await owner.PostAsJsonAsync("/api/lots", new LotWrite("ONCE-1", "Demo Kendra", null, null, null, 67, null, null, null, op, null));
        Assert.Equal((await once.Content.ReadFromJsonAsync<WriteResultDto>())!.Id, (await twice.Content.ReadFromJsonAsync<WriteResultDto>())!.Id);
    }

    private static void AssertNoMoney(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement);
        static void Walk(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    Assert.DoesNotContain(prop.Name, new[] { "amount", "rate", "amountReceived", "paymentStatus", "payee", "expensesToday", "receiptsToday", "pinHash" });
                    Walk(prop.Value);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray()) Walk(item);
            }
        }
    }

    private static PaddyWrite Ro(string no) => new(no, new DateOnly(2026, 11, 10), "Demo Kendra", "MP09SK1001", 100, 10, 4, null, null, null, null, null, null, null, null, "RULE-1", null);
    private static AcceptanceWrite Acceptance(string no) => new(no, new DateOnly(2026, 11, 14), 67, 20, 0, "MP09SK1001", "880011", null, null, "Demo Kendra", null, "RULE-1", null, null);

    private static async Task Upload(HttpClient client, string ownerType, Guid ownerId)
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(png), "file", "slip.png");
        content.Add(new StringContent(ownerType), "ownerType");
        content.Add(new StringContent(ownerId.ToString()), "ownerId");
        var response = await client.PostAsync("/api/documents", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class ApiFixture : IAsyncLifetime
{
    private static int Seq;
    public string AppCs { get; private set; } = "";
    private string OwnerCs { get; set; } = "";
    private WebApplicationFactory<Program> _factory = default!;
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "cmr-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        var admin = Environment.GetEnvironmentVariable("CMR_ADMIN_CS") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await Exec(conn, """
            DO $$ BEGIN
              BEGIN CREATE ROLE cmr_migrator NOLOGIN; EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL; END;
              BEGIN CREATE ROLE cmr_owner LOGIN PASSWORD 'cmr_owner_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB; EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL; END;
              BEGIN CREATE ROLE cmr_app LOGIN PASSWORD 'cmr_app_dev' NOSUPERUSER NOBYPASSRLS NOCREATEDB; EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL; END;
            END $$;
            GRANT cmr_migrator TO cmr_owner WITH ADMIN OPTION;
            """);
        await Exec(conn, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'cmr_itest' AND pid <> pg_backend_pid()");
        await Exec(conn, "DROP DATABASE IF EXISTS cmr_itest");
        await Exec(conn, "CREATE DATABASE cmr_itest OWNER cmr_owner");
        await Exec(conn, "GRANT CONNECT ON DATABASE cmr_itest TO cmr_app, cmr_owner");
        OwnerCs = "Host=localhost;Port=5432;Database=cmr_itest;Username=cmr_owner;Password=cmr_owner_dev";
        AppCs = "Host=localhost;Port=5432;Database=cmr_itest;Username=cmr_app;Password=cmr_app_dev";
        Directory.CreateDirectory(_storage);
        Environment.SetEnvironmentVariable("ConnectionStrings__Owner", OwnerCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__App", AppCs);
        Environment.SetEnvironmentVariable("Jwt__Key", "test-signing-key-0123456789abcdef-testing");
        Environment.SetEnvironmentVariable("Seed__Demo", "false");
        Environment.SetEnvironmentVariable("SEED_DEMO", "false");
        Environment.SetEnvironmentVariable("Storage__Root", _storage);
        Environment.SetEnvironmentVariable("Login__RateLimitDisabled", "true");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public async Task<MillLogin> MakeMillAsync()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var staff = Guid.NewGuid();
        var suffix = Interlocked.Increment(ref Seq).ToString("D8");
        var phoneOwner = "+9198" + suffix;
        var phoneStaff = "+9197" + suffix;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(OwnerCs).Options;
        await using var db = new AppDbContext(options, new CurrentUser());
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Mill " + suffix, ShortName = suffix, Status = "active", Plan = "pilot", CreatedAt = DateTimeOffset.UtcNow });
        db.Users.Add(new User { Id = owner, TenantId = tenant, Name = "Owner", Username = "own" + suffix, PhoneE164 = phoneOwner, Role = "owner", PinHash = PinHasher.Hash("246810"), Status = "active", CreatedBy = owner });
        db.Users.Add(new User { Id = staff, TenantId = tenant, Name = "Staff", Username = "stf" + suffix, PhoneE164 = phoneStaff, Role = "staff", PinHash = PinHasher.Hash("1357"), Status = "active", CreatedBy = owner });
        db.Seasons.Add(new Season { Id = Guid.NewGuid(), TenantId = tenant, Label = "KMS " + suffix, StartsOn = new DateOnly(2026, 10, 1), DefaultOutturnPct = 67, CreatedBy = owner });
        await db.SaveChangesAsync();
        return new MillLogin(tenant, "own" + suffix, phoneOwner, "stf" + suffix, "246810", "1357");
    }

    public async Task<HttpClient> LoginAsync(string identifier, string pin)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(identifier, pin));
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Login {(int)response.StatusCode} for {identifier}: {body}");
        }
        return client;
    }

    public Task<HttpResponseMessage> PostLogin(string identifier, string pin)
    {
        var client = _factory.CreateClient();
        return client.PostAsJsonAsync("/api/auth/login", new LoginRequest(identifier, pin));
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed record MillLogin(Guid TenantId, string OwnerUser, string OwnerPhone, string StaffUser, string Pin, string StaffPin);

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
