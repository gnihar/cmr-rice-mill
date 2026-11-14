using System.Security.Claims;
using Cmr.Api.Auth;
using Cmr.Api.Data;
using Cmr.Api.Services;
using Cmr.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Cmr.Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapCmrApi(this WebApplication app)
    {
        app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
        {
            try
            {
                var ok = await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                return Results.Ok(new { ok = true, version = AppVersion.Current });
            }
            catch
            {
                return Results.Json(new { ok = false, version = AppVersion.Current }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        app.MapGet("/api/auth/demo", (IConfiguration config) =>
        {
            if (!config.GetValue("Seed:Demo", false)) return Results.NotFound();
            return Results.Ok(new
            {
                version = AppVersion.Current,
                accounts = new[]
                {
                    new { role = "owner", identifier = DemoLogins.OwnerUser, phone = "9800000001", pin = DemoLogins.OwnerPin },
                    new { role = "staff", identifier = DemoLogins.StaffUser, phone = "9800000002", pin = DemoLogins.StaffPin }
                }
            });
        });

        var auth = app.MapGroup("/api/auth");
        auth.MapPost("/login", Login).RequireRateLimiting("login");
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/me", Me);

        var api = app.MapGroup("/api");
        api.MapGet("/seasons", Seasons);
        api.MapPost("/seasons", CreateSeason);
        api.MapGet("/lots", Lots);
        api.MapPost("/lots", CreateLot);
        api.MapGet("/lots/{id:guid}", Lot);
        api.MapPut("/lots/{id:guid}", UpdateLot);
        api.MapPost("/lots/{id:guid}/acknowledge", Ack);
        api.MapPut("/lots/{id:guid}/ro", SaveRo);
        api.MapPut("/lots/{id:guid}/do", SaveDo);
        api.MapPut("/lots/{id:guid}/acceptance", SaveAcceptance);
        api.MapPost("/lots/{id:guid}/weighments", AddWeighment);
        api.MapPut("/lots/{id:guid}/weighments/{wid:guid}", UpdateWeighment);
        api.MapPost("/entries/send-back", SendBack);
        api.MapPost("/entries/approve", Approve);
        api.MapPost("/documents", SaveDocument).DisableAntiforgery();
        api.MapGet("/documents/{id:guid}/file", DocumentFile);
        api.MapGet("/by-products", Sales);
        api.MapPost("/by-products", CreateSale);
        api.MapPut("/by-products/{id:guid}", UpdateSale);
        api.MapPut("/by-products/{id:guid}/price", SavePrice);
        api.MapGet("/expenses", Expenses);
        api.MapPost("/expenses", CreateExpense);
        api.MapGet("/receipts", Receipts);
        api.MapPost("/receipts", CreateReceipt);
        api.MapGet("/bags", Bags);
        api.MapPost("/bags", AddBag);
        api.MapGet("/dashboard", Dashboard);
        api.MapGet("/vault", Vault);
        api.MapGet("/reports/lots.csv", LotsCsv);
        api.MapGet("/reports/lots.pdf", LotsPdf);
        api.MapGet("/users", Users);
        api.MapPost("/users", CreateUser);
        api.MapPost("/users/{id:guid}/pin", ResetPin);
        api.MapPost("/users/{id:guid}/disable", (Guid id, OfficeService office, ClaimsPrincipal user, CancellationToken ct) => Status(id, "disabled", office, user, ct));
        api.MapPost("/users/{id:guid}/enable", (Guid id, OfficeService office, ClaimsPrincipal user, CancellationToken ct) => Status(id, "active", office, user, ct));
        api.MapGet("/devices", Devices);
        api.MapPost("/devices/{id:guid}/revoke", Revoke);
        api.MapGet("/audit", AuditLog);
    }

    private static async Task<IResult> Login(LoginRequest body, IValidator<LoginRequest> validator, ILoginService login, HttpContext http, CancellationToken ct)
    {
        var error = await Invalid(validator, body, ct);
        if (error is not null) return error;
        var tokens = await login.LoginWithPinAsync(body.Identifier, body.Pin, http.Request.Headers.UserAgent.ToString(), ct);
        AuthCookies.Write(http, tokens);
        return Results.Ok(new { tokens.Role, tokens.Name });
    }

    private static async Task<IResult> Refresh(ILoginService login, HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(AuthCookies.Refresh, out var refresh))
            return Results.Json(new { error = "invalid_login", message = Messages.InvalidLogin }, statusCode: 401);
        var tokens = await login.RefreshAsync(refresh, http.Request.Headers.UserAgent.ToString(), ct);
        AuthCookies.Write(http, tokens);
        return Results.Ok(new { tokens.Role, tokens.Name });
    }

    private static async Task<IResult> Logout(ILoginService login, HttpContext http, CancellationToken ct)
    {
        http.Request.Cookies.TryGetValue(AuthCookies.Refresh, out var refresh);
        await login.LogoutAsync(refresh, ct);
        AuthCookies.Clear(http);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Me(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var gate = Need(user);
        if (gate is not null) return gate;
        var id = Guid.Parse(user.FindFirstValue("sub")!);
        var row = await db.Users.AsNoTracking().FirstAsync(u => u.Id == id, ct);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == row.TenantId, ct);
        return Results.Ok(new MeDto(row.Id, row.Name, row.Role, row.Username, row.PhoneE164, tenant.Id, tenant.Name, tenant.ShortName));
    }

    private static async Task<IResult> Seasons(ClaimsPrincipal user, MillService mill, CancellationToken ct) =>
        Need(user) ?? Results.Ok(await mill.SeasonsAsync(ct));

    private static async Task<IResult> CreateSeason(SeasonWrite body, IValidator<SeasonWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.CreateSeasonAsync(body, ct));
    }

    private static async Task<IResult> Lots(string? q, ClaimsPrincipal user, MillService mill, CancellationToken ct) =>
        Need(user) ?? Results.Ok(await mill.ListAsync(q, ct));

    private static async Task<IResult> CreateLot(LotWrite body, IValidator<LotWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.CreateLotAsync(body, ct));
    }

    private static async Task<IResult> Lot(Guid id, ClaimsPrincipal user, MillService mill, CancellationToken ct) =>
        Need(user) ?? Results.Ok(await mill.GetWithDocsAsync(id, ct));

    private static async Task<IResult> UpdateLot(Guid id, LotWrite body, IValidator<LotWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.UpdateLotAsync(id, body, ct));
    }

    private static async Task<IResult> Ack(Guid id, AckRequest body, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        if (string.IsNullOrWhiteSpace(body.Note)) return Results.BadRequest(new { error = "validation", message = "नोट लिखें। Write a note." });
        await mill.AcknowledgeAsync(id, body.Note, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SaveRo(Guid id, PaddyWrite body, IValidator<PaddyWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.UpsertRoAsync(id, body, ct));
    }

    private static async Task<IResult> SaveDo(Guid id, DoWrite body, IValidator<DoWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.UpsertDoAsync(id, body, ct));
    }

    private static async Task<IResult> SaveAcceptance(Guid id, AcceptanceWrite body, IValidator<AcceptanceWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.UpsertAcceptanceAsync(id, body, ct));
    }

    private static async Task<IResult> AddWeighment(Guid id, WeighmentWrite body, IValidator<WeighmentWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.AddWeighmentAsync(id, body, ct));
    }

    private static async Task<IResult> UpdateWeighment(Guid id, Guid wid, WeighmentWrite body, IValidator<WeighmentWrite> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await mill.UpdateWeighmentAsync(id, wid, body, ct));
    }

    private static async Task<IResult> SendBack(SendBackRequest body, IValidator<SendBackRequest> validator, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        if (gate is not null) return gate;
        await mill.SendBackAsync(body.EntityType, body.EntityId, body.Note, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Approve(ApproveRequest body, ClaimsPrincipal user, MillService mill, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        await mill.ApproveAsync(body.EntityType, body.EntityId, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SaveDocument(HttpRequest request, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Need(user);
        if (gate is not null) return gate;
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "file", message = "फ़ाइल चुनें। Choose a file." });
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest(new { error = "file", message = "फ़ाइल चुनें। Choose a file." });
        var ownerType = form["ownerType"].ToString();
        if (!Guid.TryParse(form["ownerId"], out var ownerId))
            return Results.BadRequest(new { error = "file", message = "कागज़ किस प्रविष्टि का है, यह चुनें।" });
        await using var stream = file.OpenReadStream();
        return Results.Ok(await office.SaveDocumentAsync(stream, ownerType, ownerId, form["docKind"], ct));
    }

    private static async Task<IResult> DocumentFile(Guid id, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Need(user);
        if (gate is not null) return gate;
        var (stream, mime, name) = await office.OpenAsync(id, ct);
        return Results.File(stream, mime, name);
    }

    private static async Task<IResult> Sales(ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Need(user) ?? Results.Ok(await office.SalesAsync(ct));

    private static async Task<IResult> CreateSale(ByProductWrite body, IValidator<ByProductWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await office.CreateSaleAsync(body, ct));
    }

    private static async Task<IResult> UpdateSale(Guid id, ByProductWrite body, IValidator<ByProductWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Need(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await office.UpdateSaleAsync(id, body, ct));
    }

    private static async Task<IResult> SavePrice(Guid id, PriceWrite body, IValidator<PriceWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        if (gate is not null) return gate;
        await office.SavePriceAsync(id, body, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Expenses(ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Owner(user) ?? Results.Ok(await office.ExpensesAsync(ct));

    private static async Task<IResult> CreateExpense(ExpenseWrite body, IValidator<ExpenseWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await office.CreateExpenseAsync(body, ct));
    }

    private static async Task<IResult> Receipts(ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Owner(user) ?? Results.Ok(await office.ReceiptsAsync(ct));

    private static async Task<IResult> CreateReceipt(ReceiptWrite body, IValidator<ReceiptWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await office.CreateReceiptAsync(body, ct));
    }

    private static async Task<IResult> Bags(ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Need(user) ?? Results.Ok(await office.BagsAsync(ct));

    private static async Task<IResult> AddBag(BagWrite body, IValidator<BagWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await office.AddBagAsync(body, ct));
    }

    private static async Task<IResult> Dashboard(ClaimsPrincipal user, MillService mill, OfficeService office, CancellationToken ct)
    {
        var gate = Need(user);
        if (gate is not null) return gate;
        var dash = await mill.DashboardAsync(ct);
        if (!user.IsInRole("owner")) return Results.Ok(dash);
        var money = await office.MoneyTotalsAsync(ct);
        return Results.Ok(new OwnerDashboardDto(
            dash.Today, dash.PaddyInTodayQtl, dash.RiceAcceptedTodayQtl, dash.LotsTouchedToday,
            dash.IncompleteLots, dash.PendingAcceptances, dash.MissingPapers, dash.RecentLots,
            money.ExpensesToday, money.ReceiptsToday, money.ExpensesMonth, money.ReceiptsMonth));
    }

    private static async Task<IResult> Vault(string? q, DateOnly? from, DateOnly? to, ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Need(user) ?? Results.Ok(await office.VaultAsync(q, from, to, ct));

    private static async Task<IResult> LotsCsv(ClaimsPrincipal user, MillService mill, AuditWriter audit, AppDbContext db, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        var rows = await mill.ListAsync(null, ct);
        audit.Add("export", "lot", null, null, new { format = "csv", count = rows.Count });
        await db.SaveChangesAsync(ct);
        return Results.File(LotReport.Csv(rows), "text/csv", "lots.csv");
    }

    private static async Task<IResult> LotsPdf(ClaimsPrincipal user, MillService mill, AppDbContext db, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        var rows = await mill.ListAsync(null, ct);
        var tenantId = Guid.Parse(user.FindFirstValue("tenant_id")!);
        var millName = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstAsync(ct);
        return Results.File(LotReport.Pdf(rows, millName), "application/pdf", "lots.pdf");
    }

    private static async Task<IResult> Users(ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Owner(user) ?? Results.Ok(await office.UsersAsync(ct));

    private static async Task<IResult> CreateUser(UserWrite body, IValidator<UserWrite> validator, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user) ?? await Invalid(validator, body, ct);
        return gate ?? Results.Ok(await office.CreateUserAsync(body, ct));
    }

    private static async Task<IResult> ResetPin(Guid id, PinReset body, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        await office.ResetPinAsync(id, body.Pin, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Status(Guid id, string status, OfficeService office, ClaimsPrincipal user, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        await office.SetStatusAsync(id, status, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Devices(ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Owner(user) ?? Results.Ok(await office.DevicesAsync(ct));

    private static async Task<IResult> Revoke(Guid id, ClaimsPrincipal user, OfficeService office, CancellationToken ct)
    {
        var gate = Owner(user);
        if (gate is not null) return gate;
        await office.RevokeDeviceAsync(id, ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> AuditLog(int? take, ClaimsPrincipal user, OfficeService office, CancellationToken ct) =>
        Owner(user) ?? Results.Ok(await office.AuditAsync(take ?? 100, ct));

    private static IResult? Need(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? null : Results.Json(new { error = "unauthorized", message = "Login required." }, statusCode: 401);

    private static IResult? Owner(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "unauthorized", message = "Login required." }, statusCode: 401);
        return user.IsInRole("owner") ? null : Results.NotFound();
    }

    private static async Task<IResult?> Invalid<T>(IValidator<T> validator, T body, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(body, ct);
        if (result.IsValid) return null;
        return Results.BadRequest(new { error = "validation", message = string.Join(" ", result.Errors.Select(e => e.ErrorMessage)) });
    }
}
