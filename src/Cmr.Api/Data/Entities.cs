using System.Net;

namespace Cmr.Api.Data;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public interface IAudited : ITenantOwned
{
    Guid Id { get; set; }
    DateTimeOffset CreatedAt { get; set; }
    Guid CreatedBy { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    int RowVersion { get; set; }
}

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string? Gstin { get; set; }
    public string? MpscscRegNo { get; set; }
    public string Status { get; set; } = "active";
    public string Plan { get; set; } = "pilot";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class User : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string? Username { get; set; }
    public string? PhoneE164 { get; set; }
    public string Role { get; set; } = "staff";
    public string PinHash { get; set; } = "";
    public int PinFailCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class Device : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Label { get; set; } = "";
    public string RefreshTokenHash { get; set; } = "";
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class Season : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Label { get; set; } = "";
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public decimal DefaultOutturnPct { get; set; } = 67m;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class Lot : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SeasonId { get; set; }
    public string LotNo { get; set; } = "";
    public string CentreName { get; set; } = "";
    public string? CentreCode { get; set; }
    public string? TruckNo { get; set; }
    public decimal? PaddyQtyQtl { get; set; }
    public decimal OutturnPct { get; set; } = 67m;
    public string Stage { get; set; } = "paddy_received";
    public string? OwnerNote { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public string EntryStatus { get; set; } = EntryRulesLocal.Submitted;
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public bool MismatchesAcked { get; set; }
    public string? MismatchAckNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public static class EntryRulesLocal
{
    public const string Submitted = "submitted";
    public const string SentBack = "sent_back";
    public const string Approved = "approved";
}

public sealed class PaddyRo : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LotId { get; set; }
    public string RoNo { get; set; } = "";
    public DateOnly RoDate { get; set; }
    public string CentreName { get; set; } = "";
    public string? DistrictName { get; set; }
    public string TruckNo { get; set; } = "";
    public decimal PaddyQtyQtl { get; set; }
    public decimal? GradeAQtyQtl { get; set; }
    public decimal? OrderQtyQtl { get; set; }
    public int BagsNew { get; set; }
    public int BagsOld { get; set; }
    public string? PaddyDoNo { get; set; }
    public DateOnly? PaddyDoDate { get; set; }
    public DateOnly? PaddyDoValidTill { get; set; }
    public string? AgreementNo { get; set; }
    public string? GodownName { get; set; }
    public string? PrintedLotNo { get; set; }
    public string EntryStatus { get; set; } = "submitted";
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class CmrDo : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LotId { get; set; }
    public string DoNo { get; set; } = "";
    public DateOnly DoDate { get; set; }
    public string Destination { get; set; } = "";
    public decimal RiceQtyQtl { get; set; }
    public string? TruckNo { get; set; }
    public DateOnly? ValidTill { get; set; }
    public string? AgreementNo { get; set; }
    public string? CmrType { get; set; }
    public string? GodownName { get; set; }
    public string? DistrictName { get; set; }
    public string? IssueCentre { get; set; }
    public string? PaddyDoNo { get; set; }
    public string? PrintedLotNo { get; set; }
    public string? FciLotNo { get; set; }
    public string EntryStatus { get; set; } = "submitted";
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class Acceptance : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LotId { get; set; }
    public Guid DoId { get; set; }
    public string CmraNo { get; set; } = "";
    public DateOnly AcceptedOn { get; set; }
    public decimal AcceptedQtyQtl { get; set; }
    public int BagsNew { get; set; }
    public int BagsOld { get; set; }
    public string TruckNo { get; set; } = "";
    public string? ToulParchiNo { get; set; }
    public string? Grade { get; set; }
    public string? GodownName { get; set; }
    public string? IssueCentre { get; set; }
    public string? CmrType { get; set; }
    public string? PrintedLotNo { get; set; }
    public string? DepositDistrict { get; set; }
    public string EntryStatus { get; set; } = "submitted";
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class Weighment : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LotId { get; set; }
    public string Kind { get; set; } = "rice_out";
    public string TruckNo { get; set; } = "";
    public string? RstNo { get; set; }
    public bool RstMissing { get; set; }
    public int? GrossKg { get; set; }
    public int? TareKg { get; set; }
    public int? NetKg { get; set; }
    public DateTimeOffset WeighedAt { get; set; }
    public string? PartyName { get; set; }
    public string? Material { get; set; }
    public string? WeighbridgeName { get; set; }
    public string EntryStatus { get; set; } = "submitted";
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class Document : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string OwnerType { get; set; } = "";
    public Guid OwnerId { get; set; }
    public string DocKind { get; set; } = "photo";
    public string StorageKey { get; set; } = "";
    public string Mime { get; set; } = "";
    public int SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public int Pages { get; set; } = 1;
    public DateTimeOffset? CapturedAt { get; set; }
    public string UploadStatus { get; set; } = "uploaded";
    public string OcrStatus { get; set; } = "none";
    public string? OcrJson { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class BagMovement : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid? LotId { get; set; }
    public DateOnly MovedOn { get; set; }
    public string BagType { get; set; } = "new";
    public string Direction { get; set; } = "in";
    public int Qty { get; set; }
    public string Source { get; set; } = "manual";
    public Guid? RefId { get; set; }
    public string? Note { get; set; }
    public bool Voided { get; set; }
    public string EntryStatus { get; set; } = "approved";
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class ByProductSale : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SeasonId { get; set; }
    public DateOnly SoldOn { get; set; }
    public string Product { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "qtl";
    public string? BuyerName { get; set; }
    public string? VehicleNo { get; set; }
    public string? Note { get; set; }
    public string EntryStatus { get; set; } = "submitted";
    public Guid? ClientOpId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>Money for a by-product sale. Never joined into a staff query.</summary>
public sealed class ByProductPrice : ITenantOwned
{
    public Guid SaleId { get; set; }
    public Guid TenantId { get; set; }
    public decimal? Rate { get; set; }
    public decimal? Amount { get; set; }
    public decimal? AmountReceived { get; set; }
    public string PaymentStatus { get; set; } = "due";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}

public sealed class Expense : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? SeasonId { get; set; }
    public Guid? LotId { get; set; }
    public DateOnly SpentOn { get; set; }
    public string Category { get; set; } = "misc";
    public decimal Amount { get; set; }
    public string Mode { get; set; } = "cash";
    public string? Payee { get; set; }
    public string? Note { get; set; }
    public Guid? ClientOpId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class Receipt : IAudited
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? SeasonId { get; set; }
    public Guid? LotId { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public string Source { get; set; } = "other";
    public Guid? SaleId { get; set; }
    public decimal Amount { get; set; }
    public string Mode { get; set; } = "cash";
    public string? RefNo { get; set; }
    public string? Note { get; set; }
    public Guid? ClientOpId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

public sealed class EntryReview : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string Action { get; set; } = "";
    public Guid ByUserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientOp : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ClientOpId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public DateTimeOffset Ts { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? DeviceId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public IPAddress? Ip { get; set; }
    public string? RequestId { get; set; }
}
