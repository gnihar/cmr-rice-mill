using Cmr.Api.Auth;
using Cmr.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Cmr.Api.Data;

public static class DemoLogins
{
    public const string OwnerUser = "owner";
    public const string OwnerPhone = "+919800000001";
    public const string OwnerPin = "246810";
    public const string StaffUser = "staff";
    public const string StaffPhone = "+919800000002";
    public const string StaffPin = "1357";
    public static readonly Guid TenantId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    public static readonly Guid OwnerId = Guid.Parse("10000000-0000-4000-8000-000000000002");
    public static readonly Guid StaffId = Guid.Parse("10000000-0000-4000-8000-000000000003");
}

/// <summary>Synthetic mill. None of these numbers come from a real challan, deposit order or kaanta slip.</summary>
public static class DemoSeeder
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static async Task SeedAsync(AppDbContext db, string storageRoot, CancellationToken ct)
    {
        if (await db.Tenants.AnyAsync(ct)) return;
        var today = Services.Clock.Today();
        var seasonId = Guid.Parse("10000000-0000-4000-8000-000000000004");
        var lot12 = Guid.Parse("10000000-0000-4000-8000-000000000012");
        var lot13 = Guid.Parse("10000000-0000-4000-8000-000000000013");
        var lot14 = Guid.Parse("10000000-0000-4000-8000-000000000014");
        var lot15 = Guid.Parse("10000000-0000-4000-8000-000000000015");
        var ro12 = Guid.Parse("10000000-0000-4000-8000-000000000112");
        var do12 = Guid.Parse("10000000-0000-4000-8000-000000000212");
        var ac12 = Guid.Parse("10000000-0000-4000-8000-000000000312");
        var ka12 = Guid.Parse("10000000-0000-4000-8000-000000000412");
        var ro13 = Guid.Parse("10000000-0000-4000-8000-000000000113");
        var do13 = Guid.Parse("10000000-0000-4000-8000-000000000213");
        var ka13 = Guid.Parse("10000000-0000-4000-8000-000000000413");
        var ro14 = Guid.Parse("10000000-0000-4000-8000-000000000114");
        var do14 = Guid.Parse("10000000-0000-4000-8000-000000000214");
        var salePriced = Guid.Parse("10000000-0000-4000-8000-000000000501");
        var saleOpen = Guid.Parse("10000000-0000-4000-8000-000000000502");

        db.Tenants.Add(new Tenant
        {
            Id = DemoLogins.TenantId,
            Name = "Shri Krishna Rice & Poha Industries",
            ShortName = "श्री कृष्ण",
            Status = "active",
            Plan = "pilot",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.Users.Add(User(DemoLogins.OwnerId, DemoLogins.OwnerUser, DemoLogins.OwnerPhone, "मालिक", "owner", DemoLogins.OwnerPin));
        db.Users.Add(User(DemoLogins.StaffId, DemoLogins.StaffUser, DemoLogins.StaffPhone, "मुनशी", "staff", DemoLogins.StaffPin));
        db.Seasons.Add(new Season
        {
            Id = seasonId,
            TenantId = DemoLogins.TenantId,
            Label = "KMS 2026-27",
            StartsOn = today.AddDays(-40),
            EndsOn = today.AddMonths(8),
            DefaultOutturnPct = 67m,
            CreatedBy = DemoLogins.OwnerId
        });

        db.Lots.Add(Lot(lot12, seasonId, "SK-12", "Demo Kendra", "MP09SK1001", 100m, "accepted", today.AddDays(-6), DemoLogins.OwnerId));
        db.Lots.Add(Lot(lot13, seasonId, "SK-13", "Demo Kendra", "MP09SK1301", 50m, "do_received", today.AddDays(-1), DemoLogins.OwnerId));
        db.Lots.Add(Lot(lot14, seasonId, "SK-14", "Demo Kendra", "MP09SK1401", 40m, "do_received", today.AddDays(-3), DemoLogins.OwnerId));
        db.Lots.Add(Lot(lot15, seasonId, "SK-15", "Demo Kendra", "MP09SK1501", 80m, "milling", today, DemoLogins.OwnerId));

        db.PaddyRos.Add(new PaddyRo
        {
            Id = ro12, TenantId = DemoLogins.TenantId, LotId = lot12, RoNo = "PDY26000000000012", RoDate = today.AddDays(-6),
            CentreName = "Demo Kendra", TruckNo = "MP09SK1001", PaddyQtyQtl = 100m, BagsNew = 40, BagsOld = 20,
            PrintedLotNo = "SK-12", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.CmrDos.Add(new CmrDo
        {
            Id = do12, TenantId = DemoLogins.TenantId, LotId = lot12, DoNo = "CDO2600000000012", DoDate = today.AddDays(-2),
            Destination = "Demo Godown 2", RiceQtyQtl = 67m, TruckNo = "MP09SK1001", ValidTill = today.AddDays(5),
            IssueCentre = "Demo Kendra", PrintedLotNo = "SK-12", CmrType = "raw", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.Acceptances.Add(new Acceptance
        {
            Id = ac12, TenantId = DemoLogins.TenantId, LotId = lot12, DoId = do12, CmraNo = "CMRA26000000000012",
            AcceptedOn = today, AcceptedQtyQtl = 67m, BagsNew = 80, BagsOld = 0, TruckNo = "MP09SK1001",
            ToulParchiNo = "100012", IssueCentre = "Demo Kendra", PrintedLotNo = "SK-12", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.Weighments.Add(new Weighment
        {
            Id = ka12, TenantId = DemoLogins.TenantId, LotId = lot12, Kind = "rice_out", TruckNo = "MP09SK1001",
            RstNo = "100012", GrossKg = 8000, TareKg = 1300, WeighedAt = At(today, 11, 30),
            WeighbridgeName = "Demo Kanta", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });

        db.PaddyRos.Add(new PaddyRo
        {
            Id = ro13, TenantId = DemoLogins.TenantId, LotId = lot13, RoNo = "PDY26000000000013", RoDate = today,
            CentreName = "Demo Kendra", TruckNo = "MP09SK1301", PaddyQtyQtl = 50m, BagsNew = 10, BagsOld = 30,
            PrintedLotNo = "SK-13", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.CmrDos.Add(new CmrDo
        {
            Id = do13, TenantId = DemoLogins.TenantId, LotId = lot13, DoNo = "CDO2600000000013", DoDate = today,
            Destination = "Demo Godown 2", RiceQtyQtl = 33.5m, TruckNo = "MP09SK1301", IssueCentre = "Demo Kendra",
            PrintedLotNo = "SK-13", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.Weighments.Add(new Weighment
        {
            Id = ka13, TenantId = DemoLogins.TenantId, LotId = lot13, Kind = "paddy_in", TruckNo = "MP09SK1301",
            RstNo = "100013", GrossKg = 6200, TareKg = 1200, WeighedAt = At(today, 9, 15),
            EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });

        db.PaddyRos.Add(new PaddyRo
        {
            Id = ro14, TenantId = DemoLogins.TenantId, LotId = lot14, RoNo = "PDY26000000000014", RoDate = today.AddDays(-3),
            CentreName = "Demo Kendra", TruckNo = "MP09SK1401", PaddyQtyQtl = 40m, BagsNew = 8, BagsOld = 12,
            PrintedLotNo = "SK-14", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.CmrDos.Add(new CmrDo
        {
            Id = do14, TenantId = DemoLogins.TenantId, LotId = lot14, DoNo = "CDO2600000000014", DoDate = today.AddDays(-1),
            Destination = "Demo Godown 4", RiceQtyQtl = 26.8m, TruckNo = "MP09SK9999", IssueCentre = "Demo Kendra",
            PrintedLotNo = "SK-14", EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });

        db.BagMovements.Add(Bag(seasonId, lot12, today.AddDays(-6), "new", "in", 40, "paddy_ro", ro12));
        db.BagMovements.Add(Bag(seasonId, lot12, today.AddDays(-6), "old", "in", 20, "paddy_ro", ro12));
        db.BagMovements.Add(Bag(seasonId, lot12, today, "new", "out", 80, "acceptance", ac12));
        db.BagMovements.Add(Bag(seasonId, lot13, today, "new", "in", 10, "paddy_ro", ro13));
        db.BagMovements.Add(Bag(seasonId, lot13, today, "old", "in", 30, "paddy_ro", ro13));

        db.ByProductSales.Add(new ByProductSale
        {
            Id = salePriced, TenantId = DemoLogins.TenantId, SeasonId = seasonId, SoldOn = today.AddDays(-1),
            Product = "husk", Qty = 5m, Unit = "qtl", BuyerName = "Demo Buyer", VehicleNo = "MP09SK7788",
            EntryStatus = "approved", CreatedBy = DemoLogins.OwnerId
        });
        db.ByProductPrices.Add(new ByProductPrice
        {
            SaleId = salePriced, TenantId = DemoLogins.TenantId, Rate = 400m, Amount = 2000m, AmountReceived = 800m,
            PaymentStatus = "part", UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = DemoLogins.OwnerId
        });
        db.ByProductSales.Add(new ByProductSale
        {
            Id = saleOpen, TenantId = DemoLogins.TenantId, SeasonId = seasonId, SoldOn = today,
            Product = "bran", Qty = 2m, Unit = "qtl", BuyerName = "Demo Cattle Feed", VehicleNo = "MP09SK7788",
            EntryStatus = "submitted", CreatedBy = DemoLogins.StaffId
        });
        db.Expenses.Add(new Expense
        {
            Id = Guid.Parse("10000000-0000-4000-8000-000000000601"),
            TenantId = DemoLogins.TenantId, SeasonId = seasonId, SpentOn = today, Category = "labour",
            Amount = 1500m, Mode = "cash", Payee = "Demo labour", CreatedBy = DemoLogins.OwnerId
        });
        db.Receipts.Add(new Receipt
        {
            Id = Guid.Parse("10000000-0000-4000-8000-000000000701"),
            TenantId = DemoLogins.TenantId, SeasonId = seasonId, ReceivedOn = today, Source = "by_product_sale",
            SaleId = salePriced, Amount = 800m, Mode = "upi", RefNo = "DEMO-UPI-1", CreatedBy = DemoLogins.OwnerId
        });

        foreach (var (ownerType, ownerId) in new (string, Guid)[]
        {
            ("paddy_ro", ro12), ("cmr_do", do12), ("acceptance", ac12), ("weighment", ka12),
            ("paddy_ro", ro13), ("cmr_do", do13), ("weighment", ka13)
        })
        {
            AddDoc(db, storageRoot, ownerType, ownerId);
        }

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = DemoLogins.TenantId,
            Ts = DateTimeOffset.UtcNow,
            ActorUserId = DemoLogins.OwnerId,
            Action = "seed",
            EntityType = "tenant",
            EntityId = DemoLogins.TenantId
        });
        await db.SaveChangesAsync(ct);
    }

    private static User User(Guid id, string username, string phone, string name, string role, string pin) => new()
    {
        Id = id,
        TenantId = DemoLogins.TenantId,
        Name = name,
        Username = username,
        PhoneE164 = phone,
        Role = role,
        PinHash = PinHasher.Hash(pin),
        Status = "active",
        CreatedBy = DemoLogins.OwnerId
    };

    private static Lot Lot(Guid id, Guid seasonId, string no, string centre, string truck, decimal paddy, string stage, DateOnly opened, Guid by) => new()
    {
        Id = id,
        TenantId = DemoLogins.TenantId,
        SeasonId = seasonId,
        LotNo = no,
        CentreName = centre,
        TruckNo = truck,
        PaddyQtyQtl = paddy,
        OutturnPct = 67m,
        Stage = stage,
        OpenedOn = opened,
        EntryStatus = "approved",
        CreatedBy = by
    };

    private static BagMovement Bag(Guid seasonId, Guid lotId, DateOnly day, string type, string direction, int qty, string source, Guid refId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = DemoLogins.TenantId,
        SeasonId = seasonId,
        LotId = lotId,
        MovedOn = day,
        BagType = type,
        Direction = direction,
        Qty = qty,
        Source = source,
        RefId = refId,
        EntryStatus = "approved",
        CreatedBy = DemoLogins.OwnerId
    };

    private static void AddDoc(AppDbContext db, string root, string ownerType, Guid ownerId)
    {
        var id = Guid.NewGuid();
        var key = $"tenants/{DemoLogins.TenantId}/{id}.png";
        var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Png);
        db.Documents.Add(new Document
        {
            Id = id,
            TenantId = DemoLogins.TenantId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            DocKind = "photo",
            StorageKey = key,
            Mime = "image/png",
            SizeBytes = Png.Length,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Png)).ToLowerInvariant() + ownerId.ToString("N")[..6],
            UploadStatus = "uploaded",
            OcrStatus = "none",
            CreatedBy = DemoLogins.OwnerId
        });
    }

    private static DateTimeOffset At(DateOnly day, int hour, int minute)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute));
        return TimeZoneInfo.ConvertTimeToUtc(local, Services.Clock.Ist);
    }
}
