namespace Cmr.Contracts;

public static class AppVersion
{
    public const string Current = "1.1.0";
}

public record LoginRequest(string Identifier, string Pin);

public record MeDto(
    Guid Id, string Name, string Role, string? Username, string? Phone,
    Guid TenantId, string TenantName, string TenantShortName);

public record SeasonDto(Guid Id, string Label, DateOnly StartsOn, DateOnly? EndsOn, decimal DefaultOutturnPct);
public record SeasonWrite(string Label, DateOnly StartsOn, DateOnly? EndsOn, decimal? DefaultOutturnPct);

public record BalanceDto(decimal? PaddyQtl, decimal? RiceDueQtl, decimal? RiceAcceptedQtl, decimal? RicePendingQtl, decimal OutturnPct);

public record CheckDto(IReadOnlyList<string> Missing, IReadOnlyList<string> Mismatches, bool IsComplete, bool MismatchesAcked);

public record LotSummaryDto(
    Guid Id, string LotNo, string CentreName, string? TruckNo, string Stage,
    string EntryStatus, string? ReviewNote, bool IsComplete,
    IReadOnlyList<string> Missing, IReadOnlyList<string> Mismatches,
    decimal? PaddyQtl, decimal? RiceDueQtl, decimal? RiceAcceptedQtl, decimal? RicePendingQtl,
    decimal OutturnPct, DateTimeOffset UpdatedAt);

public record LotWrite(
    string LotNo, string CentreName, string? CentreCode, string? TruckNo,
    decimal? PaddyQtyQtl, decimal? OutturnPct, string? Stage, DateOnly? OpenedOn,
    Guid? SeasonId, Guid? ClientOpId, string? OwnerNote);

public record PaddyDto(
    Guid Id, string RoNo, DateOnly RoDate, string CentreName, string? DistrictName, string TruckNo,
    decimal PaddyQtyQtl, decimal? GradeAQtyQtl, decimal? OrderQtyQtl, int BagsNew, int BagsOld,
    string? PaddyDoNo, DateOnly? PaddyDoDate, DateOnly? PaddyDoValidTill, string? AgreementNo,
    string? GodownName, string? PrintedLotNo, string EntryStatus, string? ReviewNote);

public record PaddyWrite(
    string RoNo, DateOnly RoDate, string CentreName, string TruckNo, decimal PaddyQtyQtl,
    int BagsNew, int BagsOld, decimal? GradeAQtyQtl, decimal? OrderQtyQtl, string? DistrictName,
    string? PaddyDoNo, DateOnly? PaddyDoDate, DateOnly? PaddyDoValidTill, string? AgreementNo,
    string? GodownName, string? PrintedLotNo, Guid? ClientOpId);

public record DoDto(
    Guid Id, string DoNo, DateOnly DoDate, string Destination, decimal RiceQtyQtl, string? TruckNo,
    DateOnly? ValidTill, string? AgreementNo, string? CmrType, string? GodownName, string? DistrictName,
    string? IssueCentre, string? PaddyDoNo, string? PrintedLotNo, string? FciLotNo,
    string EntryStatus, string? ReviewNote);

public record DoWrite(
    string DoNo, DateOnly DoDate, string Destination, decimal RiceQtyQtl, string? TruckNo,
    DateOnly? ValidTill, string? AgreementNo, string? CmrType, string? GodownName, string? DistrictName,
    string? IssueCentre, string? PaddyDoNo, string? PrintedLotNo, string? FciLotNo, Guid? ClientOpId);

public record AcceptanceDto(
    Guid Id, Guid DoId, string CmraNo, DateOnly AcceptedOn, decimal AcceptedQtyQtl, int BagsNew, int BagsOld,
    string TruckNo, string? ToulParchiNo, string? Grade, string? GodownName, string? IssueCentre,
    string? CmrType, string? PrintedLotNo, string? DepositDistrict, string EntryStatus, string? ReviewNote);

public record AcceptanceWrite(
    string CmraNo, DateOnly AcceptedOn, decimal AcceptedQtyQtl, int BagsNew, int BagsOld, string TruckNo,
    string? ToulParchiNo, string? Grade, string? GodownName, string? IssueCentre, string? CmrType,
    string? PrintedLotNo, string? DepositDistrict, Guid? ClientOpId);

public record WeighmentDto(
    Guid Id, string Kind, string TruckNo, string? RstNo, bool RstMissing, int? GrossKg, int? TareKg, int? NetKg,
    DateTimeOffset WeighedAt, string? PartyName, string? Material, string? WeighbridgeName,
    string EntryStatus, string? ReviewNote);

public record WeighmentWrite(
    string Kind, string TruckNo, string? RstNo, bool RstMissing, int? GrossKg, int? TareKg,
    DateTimeOffset WeighedAt, string? PartyName, string? Material, string? WeighbridgeName, Guid? ClientOpId);

public record DocumentDto(Guid Id, string OwnerType, Guid OwnerId, string DocKind, string Mime, int SizeBytes, DateTimeOffset CreatedAt);

public record LotDetailDto(
    Guid Id, Guid SeasonId, string LotNo, string CentreName, string? CentreCode, string? TruckNo,
    decimal? PaddyQtyQtl, decimal OutturnPct, string Stage, string EntryStatus, string? ReviewNote,
    string? OwnerNote, bool MismatchesAcked, string? MismatchAckNote, DateOnly? OpenedOn,
    BalanceDto Balance, CheckDto Check, PaddyDto? Ro, DoDto? Do, AcceptanceDto? Acceptance,
    IReadOnlyList<WeighmentDto> Weighments, IReadOnlyList<DocumentDto> Documents, int RowVersion);

public record WriteResultDto(Guid Id, string EntryStatus, IReadOnlyList<string> Warnings);

public record SendBackRequest(string EntityType, Guid EntityId, string Note);
public record ApproveRequest(string EntityType, Guid EntityId);
public record AckRequest(string Note);

public record ByProductDto(
    Guid Id, DateOnly SoldOn, string Product, decimal Qty, string Unit, string? BuyerName,
    string? VehicleNo, string? Note, string EntryStatus, string? ReviewNote);

public record ByProductOwnerDto(
    Guid Id, DateOnly SoldOn, string Product, decimal Qty, string Unit, string? BuyerName,
    string? VehicleNo, string? Note, string EntryStatus, string? ReviewNote,
    decimal? Rate, decimal? Amount, decimal? AmountReceived, string? PaymentStatus);

public record ByProductWrite(
    DateOnly SoldOn, string Product, decimal Qty, string Unit, string? BuyerName,
    string? VehicleNo, string? Note, Guid? SeasonId, Guid? ClientOpId);

public record PriceWrite(decimal? Rate, decimal? Amount, decimal? AmountReceived, string PaymentStatus);

public record ExpenseDto(
    Guid Id, DateOnly SpentOn, string Category, decimal Amount, string Mode, string? Payee,
    string? Note, Guid? LotId);

public record ExpenseWrite(
    DateOnly SpentOn, string Category, decimal Amount, string Mode, string? Payee,
    string? Note, Guid? LotId, Guid? SeasonId, Guid? ClientOpId);

public record ReceiptDto(
    Guid Id, DateOnly ReceivedOn, string Source, decimal Amount, string Mode, string? RefNo,
    string? Note, Guid? LotId, Guid? SaleId);

public record ReceiptWrite(
    DateOnly ReceivedOn, string Source, decimal Amount, string Mode, string? RefNo,
    string? Note, Guid? LotId, Guid? SaleId, Guid? SeasonId, Guid? ClientOpId);

public record BagBalanceDto(int In, int Out, int Balance);
public record BagMovementDto(
    Guid Id, DateOnly MovedOn, string BagType, string Direction, int Qty, string Source,
    Guid? LotId, string? LotNo, string? Note, bool Voided);
public record BagLedgerDto(BagBalanceDto NewBags, BagBalanceDto OldBags, IReadOnlyList<BagMovementDto> Rows);
public record BagWrite(DateOnly MovedOn, string BagType, string Direction, int Qty, Guid? LotId, Guid? SeasonId, string? Note, Guid? ClientOpId);

public record DashboardDto(
    DateOnly Today, decimal PaddyInTodayQtl, decimal RiceAcceptedTodayQtl, int LotsTouchedToday,
    int IncompleteLots, int PendingAcceptances, int MissingPapers, IReadOnlyList<LotSummaryDto> RecentLots);

public record OwnerDashboardDto(
    DateOnly Today, decimal PaddyInTodayQtl, decimal RiceAcceptedTodayQtl, int LotsTouchedToday,
    int IncompleteLots, int PendingAcceptances, int MissingPapers, IReadOnlyList<LotSummaryDto> RecentLots,
    decimal ExpensesToday, decimal ReceiptsToday, decimal ExpensesMonth, decimal ReceiptsMonth);

public record VaultHitDto(
    Guid DocumentId, string OwnerType, Guid? LotId, string? LotNo, string? RefNo,
    string? TruckNo, DateOnly? DocDate, string Mime, string DocKind);

public record UserDto(Guid Id, string Name, string? Username, string? Phone, string Role, string Status, DateTimeOffset? LastLoginAt);
public record UserWrite(string Name, string? Username, string? Phone, string Pin);
public record PinReset(string Pin);
public record DeviceDto(Guid Id, Guid UserId, string UserName, string Label, DateTimeOffset LastSeenAt, DateTimeOffset? RevokedAt, DateTimeOffset ExpiresAt);
public record AuditDto(long Id, DateTimeOffset Ts, string? ActorName, string Action, string EntityType, Guid? EntityId, string? Ip);
