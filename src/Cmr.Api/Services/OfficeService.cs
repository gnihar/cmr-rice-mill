using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cmr.Api.Auth;
using Cmr.Api.Data;
using Cmr.Contracts;
using Cmr.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmr.Api.Services;

public sealed class OfficeService(AppDbContext db, CurrentUser current, AuditWriter audit, IConfiguration config)
{
    private static readonly string[] Products = ["husk", "bran", "kanni", "cattle_feed_broken", "rejection", "misc"];
    private static readonly string[] Units = ["qtl", "kg", "bag", "ton"];
    private static readonly string[] Categories = ["labour", "transport", "electricity", "fuel", "repair", "bags", "rent_interest", "misc"];
    private static readonly string[] Modes = ["cash", "bank", "upi"];
    private static readonly string[] Sources = ["corporation", "by_product_sale", "other"];
    private static readonly string[] DocOwners = ["paddy_ro", "cmr_do", "acceptance", "weighment", "by_product_sale", "expense", "receipt", "lot", "other"];

    public async Task<object> SalesAsync(CancellationToken ct)
    {
        var rows = await db.ByProductSales.AsNoTracking().OrderByDescending(s => s.SoldOn).ToListAsync(ct);
        if (!current.IsOwner)
            return rows.Select(MapStaff).ToList();

        var prices = await db.ByProductPrices.AsNoTracking().ToDictionaryAsync(p => p.SaleId, ct);
        return rows.Select(s =>
        {
            prices.TryGetValue(s.Id, out var price);
            return MapOwner(s, price);
        }).ToList();
    }

    public async Task<WriteResultDto> CreateSaleAsync(ByProductWrite body, CancellationToken ct)
    {
        var season = await Season(body.SeasonId, ct);
        var sale = new ByProductSale
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            SoldOn = body.SoldOn,
            Product = body.Product,
            Qty = body.Qty,
            Unit = body.Unit,
            BuyerName = Clean(body.BuyerName),
            VehicleNo = Truck(body.VehicleNo),
            Note = Clean(body.Note),
            EntryStatus = EntryRules.InitialStatus(current.IsOwner),
            ClientOpId = body.ClientOpId
        };
        db.ByProductSales.Add(sale);
        audit.Add("create", "by_product_sale", sale.Id, null, new { sale.Product, sale.Qty, sale.Unit, sale.BuyerName, sale.VehicleNo });
        await db.SaveChangesAsync(ct);
        return new WriteResultDto(sale.Id, sale.EntryStatus, []);
    }

    public async Task<WriteResultDto> UpdateSaleAsync(Guid id, ByProductWrite body, CancellationToken ct)
    {
        var sale = await db.ByProductSales.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        if (!current.IsOwner && !EntryRules.StaffMayEdit(sale.EntryStatus, sale.CreatedBy, current.UserId!.Value))
            throw new AppException(409, "locked", Messages.LockedEntry);
        sale.SoldOn = body.SoldOn;
        sale.Product = body.Product;
        sale.Qty = body.Qty;
        sale.Unit = body.Unit;
        sale.BuyerName = Clean(body.BuyerName);
        sale.VehicleNo = Truck(body.VehicleNo);
        sale.Note = Clean(body.Note);
        if (!current.IsOwner) sale.EntryStatus = EntryRules.Submitted;
        audit.Add("update", "by_product_sale", sale.Id, null, new { sale.Product, sale.Qty, sale.BuyerName, sale.VehicleNo });
        await db.SaveChangesAsync(ct);
        return new WriteResultDto(sale.Id, sale.EntryStatus, []);
    }

    public async Task SavePriceAsync(Guid saleId, PriceWrite body, CancellationToken ct)
    {
        Owner();
        var sale = await db.ByProductSales.FirstOrDefaultAsync(s => s.Id == saleId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var price = await db.ByProductPrices.FirstOrDefaultAsync(p => p.SaleId == saleId, ct);
        if (price is null)
        {
            price = new ByProductPrice { SaleId = sale.Id, TenantId = current.TenantId!.Value };
            db.ByProductPrices.Add(price);
        }
        price.Rate = body.Rate;
        price.Amount = body.Amount;
        price.AmountReceived = body.AmountReceived;
        price.PaymentStatus = body.PaymentStatus;
        price.UpdatedAt = DateTimeOffset.UtcNow;
        price.UpdatedBy = current.UserId!.Value;
        audit.Add("save_price", "by_product_price", saleId, null, new { body.Rate, body.Amount, body.AmountReceived, body.PaymentStatus });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseDto>> ExpensesAsync(CancellationToken ct)
    {
        Owner();
        return await db.Expenses.AsNoTracking().OrderByDescending(e => e.SpentOn)
            .Select(e => new ExpenseDto(e.Id, e.SpentOn, e.Category, e.Amount, e.Mode, e.Payee, e.Note, e.LotId))
            .ToListAsync(ct);
    }

    public async Task<ExpenseDto> CreateExpenseAsync(ExpenseWrite body, CancellationToken ct)
    {
        Owner();
        var row = new Expense
        {
            Id = Guid.NewGuid(),
            SeasonId = body.SeasonId,
            LotId = body.LotId,
            SpentOn = body.SpentOn,
            Category = body.Category,
            Amount = body.Amount,
            Mode = body.Mode,
            Payee = Clean(body.Payee),
            Note = Clean(body.Note),
            ClientOpId = body.ClientOpId
        };
        db.Expenses.Add(row);
        audit.Add("create", "expense", row.Id, null, new { row.Category, row.Amount, row.Mode });
        await db.SaveChangesAsync(ct);
        return new ExpenseDto(row.Id, row.SpentOn, row.Category, row.Amount, row.Mode, row.Payee, row.Note, row.LotId);
    }

    public async Task<IReadOnlyList<ReceiptDto>> ReceiptsAsync(CancellationToken ct)
    {
        Owner();
        return await db.Receipts.AsNoTracking().OrderByDescending(r => r.ReceivedOn)
            .Select(r => new ReceiptDto(r.Id, r.ReceivedOn, r.Source, r.Amount, r.Mode, r.RefNo, r.Note, r.LotId, r.SaleId))
            .ToListAsync(ct);
    }

    public async Task<ReceiptDto> CreateReceiptAsync(ReceiptWrite body, CancellationToken ct)
    {
        Owner();
        var row = new Receipt
        {
            Id = Guid.NewGuid(),
            SeasonId = body.SeasonId,
            LotId = body.LotId,
            ReceivedOn = body.ReceivedOn,
            Source = body.Source,
            SaleId = body.SaleId,
            Amount = body.Amount,
            Mode = body.Mode,
            RefNo = Clean(body.RefNo),
            Note = Clean(body.Note),
            ClientOpId = body.ClientOpId
        };
        db.Receipts.Add(row);
        audit.Add("create", "receipt", row.Id, null, new { row.Source, row.Amount, row.Mode });
        await db.SaveChangesAsync(ct);
        return new ReceiptDto(row.Id, row.ReceivedOn, row.Source, row.Amount, row.Mode, row.RefNo, row.Note, row.LotId, row.SaleId);
    }

    public async Task<(decimal ExpensesToday, decimal ReceiptsToday, decimal ExpensesMonth, decimal ReceiptsMonth)> MoneyTotalsAsync(CancellationToken ct)
    {
        Owner();
        var today = Clock.Today();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.SpentOn >= monthStart).ToListAsync(ct);
        var receipts = await db.Receipts.AsNoTracking().Where(r => r.ReceivedOn >= monthStart).ToListAsync(ct);
        return (
            expenses.Where(e => e.SpentOn == today).Sum(e => e.Amount),
            receipts.Where(r => r.ReceivedOn == today).Sum(r => r.Amount),
            expenses.Sum(e => e.Amount),
            receipts.Sum(r => r.Amount));
    }

    public async Task<BagLedgerDto> BagsAsync(CancellationToken ct)
    {
        var rows = await db.BagMovements.AsNoTracking().Where(b => !b.Voided).OrderByDescending(b => b.MovedOn).ToListAsync(ct);
        var lots = await db.Lots.AsNoTracking().Select(l => new { l.Id, l.LotNo }).ToDictionaryAsync(l => l.Id, l => l.LotNo, ct);
        BagBalanceDto Sum(string type)
        {
            var slice = rows.Where(r => r.BagType == type).Select(r => (r.Direction, r.Qty));
            var (inn, outn, balance) = BagMath.Sum(slice);
            return new BagBalanceDto(inn, outn, balance);
        }
        return new BagLedgerDto(Sum("new"), Sum("old"), rows.Select(r => new BagMovementDto(
            r.Id, r.MovedOn, r.BagType, r.Direction, r.Qty, r.Source, r.LotId,
            r.LotId is Guid id && lots.TryGetValue(id, out var no) ? no : null, r.Note, r.Voided)).ToList());
    }

    public async Task<BagMovementDto> AddBagAsync(BagWrite body, CancellationToken ct)
    {
        Owner();
        var season = await Season(body.SeasonId, ct);
        var row = new BagMovement
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            LotId = body.LotId,
            MovedOn = body.MovedOn,
            BagType = body.BagType,
            Direction = body.Direction,
            Qty = body.Qty,
            Source = "manual",
            Note = Clean(body.Note),
            EntryStatus = EntryRules.Approved,
            ClientOpId = body.ClientOpId
        };
        db.BagMovements.Add(row);
        audit.Add("create", "bag_movement", row.Id, null, new { row.BagType, row.Direction, row.Qty });
        await db.SaveChangesAsync(ct);
        return new BagMovementDto(row.Id, row.MovedOn, row.BagType, row.Direction, row.Qty, row.Source, row.LotId, null, row.Note, false);
    }

    public async Task<DocumentDto> SaveDocumentAsync(Stream content, string ownerType, Guid ownerId, string? docKind, CancellationToken ct)
    {
        if (!DocOwners.Contains(ownerType)) throw new AppException(400, "validation", "कागज़ का प्रकार सही नहीं है।");
        if (ownerType is "expense" or "receipt") Owner();
        await EnsureOwnerExists(ownerType, ownerId, ct);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0 || buffer.Length > 10 * 1024 * 1024)
            throw new AppException(400, "file", "फ़ाइल 1 बाइट से 10 MB के बीच होनी चाहिए। File must be between 1 byte and 10 MB.");
        var raw = buffer.ToArray();
        var (stored, mime) = Normalize(raw);
        var sha = Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant();
        var existing = await db.Documents.FirstOrDefaultAsync(d => d.OwnerType == ownerType && d.OwnerId == ownerId && d.Sha256 == sha, ct);
        if (existing is not null)
            return new DocumentDto(existing.Id, existing.OwnerType, existing.OwnerId, existing.DocKind, existing.Mime, existing.SizeBytes, existing.CreatedAt);

        var id = Guid.NewGuid();
        var ext = mime == "application/pdf" ? "pdf" : "jpg";
        var key = $"tenants/{current.TenantId}/{id}.{ext}";
        var path = Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, stored, ct);
        var doc = new Document
        {
            Id = id,
            OwnerType = ownerType,
            OwnerId = ownerId,
            DocKind = string.IsNullOrWhiteSpace(docKind) ? (mime == "application/pdf" ? "pdf" : "photo") : docKind.Trim(),
            StorageKey = key,
            Mime = mime,
            SizeBytes = stored.Length,
            Sha256 = sha,
            UploadStatus = "uploaded",
            OcrStatus = "none"
        };
        db.Documents.Add(doc);
        audit.Add("upload", "document", doc.Id, null, new { doc.OwnerType, doc.OwnerId, doc.Mime, doc.SizeBytes });
        await db.SaveChangesAsync(ct);
        return new DocumentDto(doc.Id, doc.OwnerType, doc.OwnerId, doc.DocKind, doc.Mime, doc.SizeBytes, doc.CreatedAt);
    }

    public async Task<(Stream Stream, string Mime, string Name)> OpenAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        if (doc.OwnerType is "expense" or "receipt" && !current.IsOwner)
            throw new AppException(404, "not_found", Messages.NotFound);
        var prefix = $"tenants/{current.TenantId}/";
        if (!doc.StorageKey.StartsWith(prefix, StringComparison.Ordinal) || doc.StorageKey.Contains("..", StringComparison.Ordinal))
            throw new AppException(404, "not_found", Messages.NotFound);
        var path = Path.Combine(Root, doc.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new AppException(404, "not_found", Messages.NotFound);
        return (File.OpenRead(path), doc.Mime, Path.GetFileName(path));
    }

    public async Task<IReadOnlyList<VaultHitDto>> VaultAsync(string? q, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var docs = await db.Documents.AsNoTracking().OrderByDescending(d => d.CreatedAt).Take(400).ToListAsync(ct);
        if (!current.IsOwner)
            docs = docs.Where(d => d.OwnerType is not ("expense" or "receipt")).ToList();
        var lots = await db.Lots.AsNoTracking().ToListAsync(ct);
        var ros = await db.PaddyRos.AsNoTracking().ToListAsync(ct);
        var dos = await db.CmrDos.AsNoTracking().ToListAsync(ct);
        var acc = await db.Acceptances.AsNoTracking().ToListAsync(ct);
        var weighments = await db.Weighments.AsNoTracking().ToListAsync(ct);
        var query = q?.Trim();
        var hits = new List<VaultHitDto>();
        foreach (var doc in docs)
        {
            var (lotId, lotNo, refNo, truck, date) = Describe(doc, lots, ros, dos, acc, weighments);
            if (from is DateOnly f && (date is null || date < f)) continue;
            if (to is DateOnly t && (date is null || date > t)) continue;
            if (!string.IsNullOrEmpty(query))
            {
                var blob = $"{lotNo} {refNo} {truck} {doc.OwnerType}";
                if (blob.Contains(query, StringComparison.OrdinalIgnoreCase) == false) continue;
            }
            hits.Add(new VaultHitDto(doc.Id, doc.OwnerType, lotId, lotNo, refNo, truck, date, doc.Mime, doc.DocKind));
        }
        return hits.Take(100).ToList();
    }

    public async Task<IReadOnlyList<UserDto>> UsersAsync(CancellationToken ct)
    {
        Owner();
        return await db.Users.AsNoTracking().OrderBy(u => u.Name)
            .Select(u => new UserDto(u.Id, u.Name, u.Username, u.PhoneE164, u.Role, u.Status, u.LastLoginAt))
            .ToListAsync(ct);
    }

    public async Task<UserDto> CreateUserAsync(UserWrite body, CancellationToken ct)
    {
        Owner();
        var (phone, username) = Identifiers(body.Phone, body.Username);
        PinHasher.EnsurePolicy(body.Pin, "staff");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Username = username,
            PhoneE164 = phone,
            Role = "staff",
            PinHash = PinHasher.Hash(body.Pin),
            Status = "active"
        };
        db.Users.Add(user);
        audit.Add("create", "user", user.Id, null, new { user.Name, user.Username, user.PhoneE164, user.Role });
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Name, user.Username, user.PhoneE164, user.Role, user.Status, null);
    }

    public async Task ResetPinAsync(Guid id, string pin, CancellationToken ct)
    {
        Owner();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        PinHasher.EnsurePolicy(pin, user.Role);
        user.PinHash = PinHasher.Hash(pin);
        user.PinFailCount = 0;
        user.LockedUntil = null;
        var devices = await db.Devices.Where(d => d.UserId == id && d.RevokedAt == null).ToListAsync(ct);
        foreach (var device in devices) device.RevokedAt = DateTimeOffset.UtcNow;
        audit.Add("pin_reset", "user", id, null, new { devices = devices.Count });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(Guid id, string status, CancellationToken ct)
    {
        Owner();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        if (status == "disabled" && user.Role == "owner")
        {
            var owners = await db.Users.CountAsync(u => u.Role == "owner" && u.Status == "active" && u.Id != id, ct);
            if (owners == 0) throw new AppException(400, "last_owner", "आखिरी मालिक को बंद नहीं कर सकते। The last owner cannot be disabled.");
        }
        user.Status = status;
        if (status == "disabled")
        {
            var devices = await db.Devices.Where(d => d.UserId == id && d.RevokedAt == null).ToListAsync(ct);
            foreach (var device in devices) device.RevokedAt = DateTimeOffset.UtcNow;
        }
        audit.Add("status", "user", id, null, new { status });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceDto>> DevicesAsync(CancellationToken ct)
    {
        Owner();
        var devices = await db.Devices.AsNoTracking().OrderByDescending(d => d.LastSeenAt).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return devices.Select(d => new DeviceDto(d.Id, d.UserId, names.GetValueOrDefault(d.UserId, ""), d.Label, d.LastSeenAt, d.RevokedAt, d.ExpiresAt)).ToList();
    }

    public async Task RevokeDeviceAsync(Guid id, CancellationToken ct)
    {
        Owner();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        device.RevokedAt = DateTimeOffset.UtcNow;
        audit.Add("revoke_device", "device", id, null, new { device.UserId });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditDto>> AuditAsync(int take, CancellationToken ct)
    {
        Owner();
        take = Math.Clamp(take, 1, 200);
        var rows = await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return rows.Select(a => new AuditDto(
            a.Id, a.Ts, a.ActorUserId is Guid id && names.TryGetValue(id, out var name) ? name : null,
            a.Action, a.EntityType, a.EntityId, a.Ip?.ToString())).ToList();
    }

    public static bool KnownProduct(string value) => Products.Contains(value);
    public static bool KnownUnit(string value) => Units.Contains(value);
    public static bool KnownCategory(string value) => Categories.Contains(value);
    public static bool KnownMode(string value) => Modes.Contains(value);
    public static bool KnownSource(string value) => Sources.Contains(value);
    public static bool KnownPayment(string value) => value is "due" or "part" or "paid";
    public static bool KnownBag(string type, string direction) =>
        type is "new" or "old" && direction is "in" or "out" or "used_packing" or "returned" or "lost";

    private async Task EnsureOwnerExists(string ownerType, Guid ownerId, CancellationToken ct)
    {
        var ok = ownerType switch
        {
            "paddy_ro" => await db.PaddyRos.AnyAsync(x => x.Id == ownerId, ct),
            "cmr_do" => await db.CmrDos.AnyAsync(x => x.Id == ownerId, ct),
            "acceptance" => await db.Acceptances.AnyAsync(x => x.Id == ownerId, ct),
            "weighment" => await db.Weighments.AnyAsync(x => x.Id == ownerId, ct),
            "by_product_sale" => await db.ByProductSales.AnyAsync(x => x.Id == ownerId, ct),
            "expense" => await db.Expenses.AnyAsync(x => x.Id == ownerId, ct),
            "receipt" => await db.Receipts.AnyAsync(x => x.Id == ownerId, ct),
            "lot" or "other" => await db.Lots.AnyAsync(x => x.Id == ownerId, ct),
            _ => false
        };
        if (!ok) throw new AppException(404, "not_found", Messages.NotFound);
    }

    private static (byte[] Bytes, string Mime) Normalize(byte[] raw)
    {
        if (raw.Length >= 4 && raw[0] == 0x25 && raw[1] == 0x50 && raw[2] == 0x44 && raw[3] == 0x46)
            return (raw, "application/pdf");
        if (ImageSanitizer.IsJpeg(raw)) return (ImageSanitizer.StripJpeg(raw), "image/jpeg");
        if (ImageSanitizer.IsPng(raw)) return (ImageSanitizer.StripPng(raw), "image/png");
        throw new AppException(400, "file", "सिर्फ JPEG, PNG या PDF। Only JPEG, PNG or PDF.");
    }

    private static (Guid? LotId, string? LotNo, string? RefNo, string? Truck, DateOnly? Date) Describe(
        Document doc, List<Lot> lots, List<PaddyRo> ros, List<CmrDo> dos, List<Acceptance> acc, List<Weighment> weighments)
    {
        Lot? lotOf(Guid? lotId) => lotId is Guid id ? lots.FirstOrDefault(l => l.Id == id) : null;
        switch (doc.OwnerType)
        {
            case "paddy_ro":
                var ro = ros.FirstOrDefault(r => r.Id == doc.OwnerId);
                return (ro?.LotId, lotOf(ro?.LotId)?.LotNo, ro?.RoNo, ro?.TruckNo, ro?.RoDate);
            case "cmr_do":
                var dob = dos.FirstOrDefault(r => r.Id == doc.OwnerId);
                return (dob?.LotId, lotOf(dob?.LotId)?.LotNo, dob?.DoNo, dob?.TruckNo, dob?.DoDate);
            case "acceptance":
                var row = acc.FirstOrDefault(r => r.Id == doc.OwnerId);
                return (row?.LotId, lotOf(row?.LotId)?.LotNo, row?.CmraNo, row?.TruckNo, row?.AcceptedOn);
            case "weighment":
                var w = weighments.FirstOrDefault(r => r.Id == doc.OwnerId);
                return (w?.LotId, lotOf(w?.LotId)?.LotNo, w?.RstNo, w?.TruckNo, w is null ? null : Clock.Day(w.WeighedAt));
            case "lot":
                var lot = lots.FirstOrDefault(l => l.Id == doc.OwnerId);
                return (lot?.Id, lot?.LotNo, lot?.LotNo, lot?.TruckNo, lot?.OpenedOn);
            default:
                return (null, null, null, null, null);
        }
    }

    private async Task<Season> Season(Guid? id, CancellationToken ct)
    {
        if (id is Guid seasonId)
            return await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var today = Clock.Today();
        var seasons = await db.Seasons.OrderByDescending(s => s.StartsOn).ToListAsync(ct);
        return seasons.FirstOrDefault(s => s.StartsOn <= today && (s.EndsOn is null || s.EndsOn >= today))
            ?? seasons.FirstOrDefault()
            ?? throw new AppException(400, "season", "पहले सीजन बनाएँ। Create a season first.");
    }

    private string Root => config["Storage:Root"] ?? "data/files";
    private void Owner()
    {
        if (!current.IsOwner) throw new AppException(404, "not_found", Messages.NotFound);
    }

    private static (string? Phone, string? Username) Identifiers(string? phone, string? username)
    {
        string? e164 = null;
        string? user = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            if (!Phones.TryNormalizeIndia(phone, out var parsed))
                throw new AppException(400, "validation", "मोबाइल नंबर 10 अंक का होना चाहिए। Enter a 10-digit mobile number.");
            e164 = parsed;
        }
        if (!string.IsNullOrWhiteSpace(username))
        {
            user = username.Trim().ToLowerInvariant();
            if (!LoginNames.IsUsername(user))
                throw new AppException(400, "validation", "यूज़रनेम 3–32 अक्षर, अंक या _ । Username must be 3–32 letters, digits or _.");
        }
        if (e164 is null && user is null)
            throw new AppException(400, "validation", "मोबाइल या यूज़रनेम में से एक चाहिए। Enter a mobile number or a username.");
        return (e164, user);
    }

    private static ByProductDto MapStaff(ByProductSale s) => new(s.Id, s.SoldOn, s.Product, s.Qty, s.Unit, s.BuyerName, s.VehicleNo, s.Note, s.EntryStatus, s.ReviewNote);
    private static ByProductOwnerDto MapOwner(ByProductSale s, ByProductPrice? p) => new(
        s.Id, s.SoldOn, s.Product, s.Qty, s.Unit, s.BuyerName, s.VehicleNo, s.Note, s.EntryStatus, s.ReviewNote,
        p?.Rate, p?.Amount, p?.AmountReceived, p?.PaymentStatus);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Truck(string? value)
    {
        var truck = TextMatch.CanonTruck(value);
        return truck.Length == 0 ? null : truck;
    }
}

public static class LotReport
{
    public static byte[] Csv(IReadOnlyList<LotSummaryDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("lot_no,centre,truck,stage,complete,paddy_qtl,rice_due_qtl,rice_accepted_qtl,rice_pending_qtl,outturn_pct,missing,mismatches");
        foreach (var row in rows)
        {
            sb.Append(Csv(row.LotNo)).Append(',')
                .Append(Csv(row.CentreName)).Append(',')
                .Append(Csv(row.TruckNo)).Append(',')
                .Append(Csv(row.Stage)).Append(',')
                .Append(row.IsComplete ? "yes" : "no").Append(',')
                .Append(N(row.PaddyQtl)).Append(',')
                .Append(N(row.RiceDueQtl)).Append(',')
                .Append(N(row.RiceAcceptedQtl)).Append(',')
                .Append(N(row.RicePendingQtl)).Append(',')
                .Append(N(row.OutturnPct)).Append(',')
                .Append(Csv(string.Join(' ', row.Missing))).Append(',')
                .Append(Csv(string.Join(' ', row.Mismatches)))
                .AppendLine();
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] Pdf(IReadOnlyList<LotSummaryDto> rows, string mill)
    {
        var lines = new List<string> { mill, "Lot register", "" };
        lines.Add("Lot            Centre          Truck        Paddy   Due    Accepted  Pending  Stage");
        foreach (var row in rows)
        {
            lines.Add(string.Join("  ", new[]
            {
                Fit(row.LotNo, 14), Fit(row.CentreName, 14), Fit(row.TruckNo, 12),
                Fit(N(row.PaddyQtl), 7), Fit(N(row.RiceDueQtl), 6), Fit(N(row.RiceAcceptedQtl), 8),
                Fit(N(row.RicePendingQtl), 7), Fit(row.IsComplete ? "complete" : row.Stage, 16)
            }));
        }
        return SimplePdf.FromLines(lines);
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static string N(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
    private static string Fit(string? value, int width)
    {
        var text = value ?? "";
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}

/// <summary>Latin-only PDF so the report has no native PDF library.</summary>
public static class SimplePdf
{
    public static byte[] FromLines(IReadOnlyList<string> lines)
    {
        var pages = new List<string>();
        for (var i = 0; i < Math.Max(lines.Count, 1); i += 48)
            pages.Add(string.Join('\n', lines.Skip(i).Take(48).Select(Escape)));
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "PAGES",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"
        };
        var pageIds = new List<int>();
        foreach (var page in pages)
        {
            var contentId = objects.Count + 1;
            var commands = new StringBuilder();
            commands.Append("BT /F1 9 Tf 40 800 Td 12 TL\n");
            foreach (var line in page.Split('\n'))
                commands.Append('(').Append(line).Append(") '\n");
            commands.Append("ET");
            var stream = commands.ToString();
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");
            var pageId = objects.Count + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");
            pageIds.Add(pageId);
        }
        objects[1] = $"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(' ', pageIds.Select(id => id + " 0 R"))}] >>";
        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer << /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n");
        builder.Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string Escape(string line)
    {
        var sb = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c is >= ' ' and <= '~') sb.Append(c);
            else sb.Append('?');
        }
        return sb.ToString();
    }
}
