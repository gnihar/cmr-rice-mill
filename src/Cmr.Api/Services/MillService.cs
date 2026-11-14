using System.Text.Json;
using Cmr.Api.Auth;
using Cmr.Api.Data;
using Cmr.Contracts;
using Cmr.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cmr.Api.Services;

public static class Clock
{
    public static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    public static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist).Date);
    public static DateOnly Day(DateTimeOffset ts) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ts, Ist).Date);
}

public sealed class AuditWriter(AppDbContext db, CurrentUser current, IHttpContextAccessor http)
{
    public void Add(string action, string entityType, Guid? entityId, object? before, object? after) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = current.TenantId,
            Ts = DateTimeOffset.UtcNow,
            ActorUserId = current.UserId,
            DeviceId = current.DeviceId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            Ip = http.HttpContext?.Connection.RemoteIpAddress,
            RequestId = http.HttpContext?.TraceIdentifier
        });
}

public sealed class MillService(AppDbContext db, CurrentUser current, AuditWriter audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LotSummaryDto>> ListAsync(string? q, CancellationToken ct)
    {
        var graph = await LoadAsync(ct);
        var query = q?.Trim();
        IEnumerable<Lot> lots = graph.Lots;
        if (!string.IsNullOrEmpty(query))
        {
            var hit = new HashSet<Guid>();
            foreach (var lot in graph.Lots)
            {
                if (Contains(lot.LotNo, query) || Contains(lot.CentreName, query) || Contains(lot.TruckNo, query))
                    hit.Add(lot.Id);
            }
            foreach (var ro in graph.Ros.Values)
                if (Contains(ro.RoNo, query) || Contains(ro.TruckNo, query) || Contains(ro.PaddyDoNo, query)) hit.Add(ro.LotId);
            foreach (var row in graph.Dos.Values)
                if (Contains(row.DoNo, query) || Contains(row.TruckNo, query) || Contains(row.Destination, query)) hit.Add(row.LotId);
            foreach (var row in graph.Acceptances.Values)
                if (Contains(row.CmraNo, query) || Contains(row.TruckNo, query) || Contains(row.ToulParchiNo, query)) hit.Add(row.LotId);
            foreach (var row in graph.Weighments.SelectMany(x => x.Value))
                if (Contains(row.RstNo, query) || Contains(row.TruckNo, query)) hit.Add(row.LotId);
            lots = graph.Lots.Where(l => hit.Contains(l.Id));
        }
        return lots.OrderByDescending(l => l.UpdatedAt).Select(l => ToSummary(l, graph)).ToList();
    }

    public async Task<LotDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var graph = await LoadAsync(ct);
        var lot = graph.Lots.FirstOrDefault(l => l.Id == id) ?? throw new AppException(404, "not_found", Messages.NotFound);
        return ToDetail(lot, graph);
    }

    public async Task<DashboardDto> DashboardAsync(CancellationToken ct)
    {
        var graph = await LoadAsync(ct);
        var today = Clock.Today();
        var summaries = graph.Lots.OrderByDescending(l => l.UpdatedAt).Select(l => ToSummary(l, graph)).ToList();
        decimal paddyToday = graph.Ros.Values.Where(r => r.RoDate == today).Sum(r => r.PaddyQtyQtl);
        decimal riceToday = graph.Acceptances.Values.Where(a => a.AcceptedOn == today).Sum(a => a.AcceptedQtyQtl);
        var start = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), Clock.Ist);
        var touched = graph.Lots.Count(l => l.UpdatedAt >= start);
        return new DashboardDto(
            today, paddyToday, riceToday, touched,
            summaries.Count(s => !s.IsComplete),
            graph.Lots.Count(l => graph.Dos.ContainsKey(l.Id) && !graph.Acceptances.ContainsKey(l.Id)),
            summaries.Count(s => s.Missing.Count > 0),
            summaries.Take(12).ToList());
    }

    public async Task<WriteResultDto> CreateLotAsync(LotWrite body, CancellationToken ct)
    {
        if (await Replay(body.ClientOpId, ct) is WriteResultDto replay) return replay;
        var season = await ResolveSeason(body.SeasonId, ct);
        var lot = new Lot
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            LotNo = body.LotNo.Trim(),
            CentreName = body.CentreName.Trim(),
            CentreCode = Clean(body.CentreCode),
            TruckNo = TruckOrNull(body.TruckNo),
            PaddyQtyQtl = body.PaddyQtyQtl,
            OutturnPct = body.OutturnPct ?? season.DefaultOutturnPct,
            Stage = string.IsNullOrWhiteSpace(body.Stage) ? "paddy_received" : body.Stage.Trim(),
            OpenedOn = body.OpenedOn ?? Clock.Today(),
            OwnerNote = current.IsOwner ? Clean(body.OwnerNote) : null,
            EntryStatus = EntryRules.InitialStatus(current.IsOwner),
            ClientOpId = body.ClientOpId
        };
        if (!Stages.IsKnown(lot.Stage) || (lot.Stage == "closed" && !current.IsOwner))
            throw new AppException(400, "validation", "यह चरण सही नहीं है। Unknown stage.");
        db.Lots.Add(lot);
        var result = new WriteResultDto(lot.Id, lot.EntryStatus, []);
        Remember(body.ClientOpId, "lot", result);
        audit.Add("create", "lot", lot.Id, null, new { lot.LotNo, lot.CentreName, lot.TruckNo, lot.OutturnPct });
        await SaveAsync(ct);
        return result with { Id = lot.Id };
    }

    public async Task<WriteResultDto> UpdateLotAsync(Guid id, LotWrite body, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        EnsureEdit(lot.EntryStatus, lot.CreatedBy);
        var before = new { lot.LotNo, lot.CentreName, lot.TruckNo, lot.Stage, lot.OutturnPct };
        lot.LotNo = body.LotNo.Trim();
        lot.CentreName = body.CentreName.Trim();
        lot.CentreCode = Clean(body.CentreCode);
        lot.TruckNo = TruckOrNull(body.TruckNo);
        lot.PaddyQtyQtl = body.PaddyQtyQtl;
        if (body.OutturnPct is decimal pct) lot.OutturnPct = pct;
        if (!string.IsNullOrWhiteSpace(body.Stage))
        {
            if (!Stages.IsKnown(body.Stage) || (body.Stage == "closed" && !current.IsOwner))
                throw new AppException(400, "validation", "यह चरण सही नहीं है। Unknown stage.");
            lot.Stage = body.Stage;
            lot.ClosedAt = lot.Stage == "closed" ? DateTimeOffset.UtcNow : null;
        }
        if (body.OpenedOn is DateOnly opened) lot.OpenedOn = opened;
        if (current.IsOwner) lot.OwnerNote = Clean(body.OwnerNote);
        if (!current.IsOwner) lot.EntryStatus = EntryRules.Submitted;
        lot.MismatchesAcked = false;
        audit.Add("update", "lot", lot.Id, before, new { lot.LotNo, lot.CentreName, lot.TruckNo, lot.Stage, lot.OutturnPct });
        await SaveAsync(ct);
        return new WriteResultDto(lot.Id, lot.EntryStatus, []);
    }

    public async Task<WriteResultDto> UpsertRoAsync(Guid lotId, PaddyWrite body, CancellationToken ct)
    {
        if (await Replay(body.ClientOpId, ct) is WriteResultDto replay) return replay;
        var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == lotId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var roNo = DocumentIds.Norm(body.RoNo);
        var warnings = WarningList(DocumentIds.Warning("ro", roNo));
        var existing = await db.PaddyRos.FirstOrDefaultAsync(x => x.LotId == lotId, ct);
        if (existing is null)
        {
            existing = new PaddyRo { Id = Guid.NewGuid(), LotId = lotId, EntryStatus = EntryRules.InitialStatus(current.IsOwner), ClientOpId = body.ClientOpId };
            db.PaddyRos.Add(existing);
        }
        else EnsureEdit(existing.EntryStatus, existing.CreatedBy);

        existing.RoNo = roNo;
        existing.RoDate = body.RoDate;
        existing.CentreName = body.CentreName.Trim();
        existing.TruckNo = TextMatch.CanonTruck(body.TruckNo);
        existing.PaddyQtyQtl = body.PaddyQtyQtl;
        existing.GradeAQtyQtl = body.GradeAQtyQtl;
        existing.OrderQtyQtl = body.OrderQtyQtl;
        existing.BagsNew = body.BagsNew;
        existing.BagsOld = body.BagsOld;
        existing.DistrictName = Clean(body.DistrictName);
        existing.PaddyDoNo = Clean(body.PaddyDoNo);
        existing.PaddyDoDate = body.PaddyDoDate;
        existing.PaddyDoValidTill = body.PaddyDoValidTill;
        existing.AgreementNo = Clean(body.AgreementNo);
        existing.GodownName = Clean(body.GodownName);
        existing.PrintedLotNo = Clean(body.PrintedLotNo);
        if (!current.IsOwner) existing.EntryStatus = EntryRules.Submitted;
        lot.MismatchesAcked = false;
        lot.Stage = Stages.Advance(lot.Stage, "paddy_received");
        SyncBags(lot, existing.Id, "paddy_ro", body.RoDate, body.BagsNew, body.BagsOld, "in");
        var result = new WriteResultDto(existing.Id, existing.EntryStatus, warnings);
        Remember(body.ClientOpId, "paddy_ro", result);
        audit.Add("save", "paddy_ro", existing.Id, null, new { existing.RoNo, existing.PaddyQtyQtl, existing.BagsNew, existing.BagsOld, existing.TruckNo });
        await SaveAsync(ct);
        return result with { Id = existing.Id };
    }

    public async Task<WriteResultDto> UpsertDoAsync(Guid lotId, DoWrite body, CancellationToken ct)
    {
        if (await Replay(body.ClientOpId, ct) is WriteResultDto replay) return replay;
        var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == lotId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var doNo = DocumentIds.Norm(body.DoNo);
        var warnings = WarningList(DocumentIds.Warning("do", doNo));
        var existing = await db.CmrDos.FirstOrDefaultAsync(x => x.LotId == lotId, ct);
        if (existing is null)
        {
            existing = new CmrDo { Id = Guid.NewGuid(), LotId = lotId, EntryStatus = EntryRules.InitialStatus(current.IsOwner), ClientOpId = body.ClientOpId };
            db.CmrDos.Add(existing);
        }
        else EnsureEdit(existing.EntryStatus, existing.CreatedBy);

        existing.DoNo = doNo;
        existing.DoDate = body.DoDate;
        existing.Destination = body.Destination.Trim();
        existing.RiceQtyQtl = body.RiceQtyQtl;
        existing.TruckNo = TruckOrNull(body.TruckNo);
        existing.ValidTill = body.ValidTill;
        existing.AgreementNo = Clean(body.AgreementNo);
        existing.CmrType = Clean(body.CmrType);
        existing.GodownName = Clean(body.GodownName);
        existing.DistrictName = Clean(body.DistrictName);
        existing.IssueCentre = Clean(body.IssueCentre);
        existing.PaddyDoNo = Clean(body.PaddyDoNo);
        existing.PrintedLotNo = Clean(body.PrintedLotNo);
        existing.FciLotNo = Clean(body.FciLotNo);
        if (!current.IsOwner) existing.EntryStatus = EntryRules.Submitted;
        lot.MismatchesAcked = false;
        lot.Stage = Stages.Advance(lot.Stage, "do_received");
        var result = new WriteResultDto(existing.Id, existing.EntryStatus, warnings);
        Remember(body.ClientOpId, "cmr_do", result);
        audit.Add("save", "cmr_do", existing.Id, null, new { existing.DoNo, existing.Destination, existing.RiceQtyQtl, existing.TruckNo });
        await SaveAsync(ct);
        return result with { Id = existing.Id };
    }

    public async Task<WriteResultDto> UpsertAcceptanceAsync(Guid lotId, AcceptanceWrite body, CancellationToken ct)
    {
        if (await Replay(body.ClientOpId, ct) is WriteResultDto replay) return replay;
        var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == lotId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var dob = await db.CmrDos.FirstOrDefaultAsync(x => x.LotId == lotId, ct);
        if (dob is null) throw new AppException(422, "acceptance_requires_do", Messages.NeedDo);
        var cmra = DocumentIds.Norm(body.CmraNo);
        var warnings = WarningList(DocumentIds.Warning("cmra", cmra));
        var existing = await db.Acceptances.FirstOrDefaultAsync(x => x.LotId == lotId, ct);
        if (existing is null)
        {
            existing = new Acceptance { Id = Guid.NewGuid(), LotId = lotId, DoId = dob.Id, EntryStatus = EntryRules.InitialStatus(current.IsOwner), ClientOpId = body.ClientOpId };
            db.Acceptances.Add(existing);
        }
        else
        {
            EnsureEdit(existing.EntryStatus, existing.CreatedBy);
            existing.DoId = dob.Id;
        }

        existing.CmraNo = cmra;
        existing.AcceptedOn = body.AcceptedOn;
        existing.AcceptedQtyQtl = body.AcceptedQtyQtl;
        existing.BagsNew = body.BagsNew;
        existing.BagsOld = body.BagsOld;
        existing.TruckNo = TextMatch.CanonTruck(body.TruckNo);
        existing.ToulParchiNo = Clean(body.ToulParchiNo);
        existing.Grade = Clean(body.Grade);
        existing.GodownName = Clean(body.GodownName);
        existing.IssueCentre = Clean(body.IssueCentre);
        existing.CmrType = Clean(body.CmrType);
        existing.PrintedLotNo = Clean(body.PrintedLotNo);
        existing.DepositDistrict = Clean(body.DepositDistrict);
        if (!current.IsOwner) existing.EntryStatus = EntryRules.Submitted;
        lot.MismatchesAcked = false;
        lot.Stage = Stages.Advance(lot.Stage, "accepted");
        SyncBags(lot, existing.Id, "acceptance", body.AcceptedOn, body.BagsNew, body.BagsOld, "out");
        var result = new WriteResultDto(existing.Id, existing.EntryStatus, warnings);
        Remember(body.ClientOpId, "acceptance", result);
        audit.Add("save", "acceptance", existing.Id, null, new { existing.CmraNo, existing.AcceptedQtyQtl, existing.ToulParchiNo, existing.TruckNo });
        await SaveAsync(ct);
        return result with { Id = existing.Id };
    }

    public async Task<WriteResultDto> AddWeighmentAsync(Guid lotId, WeighmentWrite body, CancellationToken ct)
    {
        if (await Replay(body.ClientOpId, ct) is WriteResultDto replay) return replay;
        var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == lotId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var row = new Weighment
        {
            Id = Guid.NewGuid(),
            LotId = lotId,
            EntryStatus = EntryRules.InitialStatus(current.IsOwner),
            ClientOpId = body.ClientOpId
        };
        ApplyWeighment(row, body);
        db.Weighments.Add(row);
        lot.MismatchesAcked = false;
        var result = new WriteResultDto(row.Id, row.EntryStatus, []);
        Remember(body.ClientOpId, "weighment", result);
        audit.Add("create", "weighment", row.Id, null, new { row.Kind, row.RstNo, row.RstMissing, row.TruckNo, row.GrossKg, row.TareKg });
        await SaveAsync(ct);
        return result with { Id = row.Id };
    }

    public async Task<WriteResultDto> UpdateWeighmentAsync(Guid lotId, Guid id, WeighmentWrite body, CancellationToken ct)
    {
        var row = await db.Weighments.FirstOrDefaultAsync(x => x.Id == id && x.LotId == lotId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        EnsureEdit(row.EntryStatus, row.CreatedBy);
        ApplyWeighment(row, body);
        if (!current.IsOwner) row.EntryStatus = EntryRules.Submitted;
        var lot = await db.Lots.FirstAsync(x => x.Id == lotId, ct);
        lot.MismatchesAcked = false;
        audit.Add("update", "weighment", row.Id, null, new { row.Kind, row.RstNo, row.RstMissing, row.TruckNo, row.GrossKg, row.TareKg });
        await SaveAsync(ct);
        return new WriteResultDto(row.Id, row.EntryStatus, []);
    }

    public async Task SendBackAsync(string entityType, Guid id, string note, CancellationToken ct)
    {
        if (!current.IsOwner) throw new AppException(404, "not_found", Messages.NotFound);
        var editable = await FindAsync(entityType, id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        editable.SetStatus(EntryRules.SentBack);
        editable.SetNote(note.Trim());
        db.EntryReviews.Add(new EntryReview
        {
            Id = Guid.NewGuid(),
            TenantId = current.TenantId!.Value,
            EntityType = entityType,
            EntityId = id,
            Action = "send_back",
            ByUserId = current.UserId!.Value,
            Note = note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        audit.Add("send_back", entityType, id, null, new { note });
        await SaveAsync(ct);
    }

    public async Task ApproveAsync(string entityType, Guid id, CancellationToken ct)
    {
        if (!current.IsOwner) throw new AppException(404, "not_found", Messages.NotFound);
        var editable = await FindAsync(entityType, id, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        editable.SetStatus(EntryRules.Approved);
        db.EntryReviews.Add(new EntryReview
        {
            Id = Guid.NewGuid(),
            TenantId = current.TenantId!.Value,
            EntityType = entityType,
            EntityId = id,
            Action = "approve",
            ByUserId = current.UserId!.Value,
            CreatedAt = DateTimeOffset.UtcNow
        });
        audit.Add("approve", entityType, id, null, new { status = EntryRules.Approved });
        await SaveAsync(ct);
    }

    public async Task AcknowledgeAsync(Guid lotId, string note, CancellationToken ct)
    {
        if (!current.IsOwner) throw new AppException(404, "not_found", Messages.NotFound);
        var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == lotId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        lot.MismatchesAcked = true;
        lot.MismatchAckNote = note.Trim();
        audit.Add("acknowledge_mismatch", "lot", lotId, null, new { note });
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<SeasonDto>> SeasonsAsync(CancellationToken ct) =>
        await db.Seasons.OrderByDescending(s => s.StartsOn)
            .Select(s => new SeasonDto(s.Id, s.Label, s.StartsOn, s.EndsOn, s.DefaultOutturnPct))
            .ToListAsync(ct);

    public async Task<SeasonDto> CreateSeasonAsync(SeasonWrite body, CancellationToken ct)
    {
        if (!current.IsOwner) throw new AppException(404, "not_found", Messages.NotFound);
        var season = new Season
        {
            Id = Guid.NewGuid(),
            Label = body.Label.Trim(),
            StartsOn = body.StartsOn,
            EndsOn = body.EndsOn,
            DefaultOutturnPct = body.DefaultOutturnPct ?? 67m
        };
        db.Seasons.Add(season);
        audit.Add("create", "season", season.Id, null, new { season.Label, season.DefaultOutturnPct });
        await SaveAsync(ct);
        return new SeasonDto(season.Id, season.Label, season.StartsOn, season.EndsOn, season.DefaultOutturnPct);
    }

    private void ApplyWeighment(Weighment row, WeighmentWrite body)
    {
        if (body.Kind is not ("paddy_in" or "rice_out"))
            throw new AppException(400, "validation", "कांटा प्रकार paddy_in या rice_out होना चाहिए।");
        if (!body.RstMissing && (string.IsNullOrWhiteSpace(body.RstNo) || body.GrossKg is null || body.TareKg is null))
            throw new AppException(400, "validation", "RST नंबर, सकल और खाली वजन भरें, या पर्ची गुम चिह्नित करें। Enter RST, gross and tare, or mark the slip missing.");
        if (!body.RstMissing && body.GrossKg < body.TareKg)
            throw new AppException(400, "validation", "सकल वजन खाली वजन से कम नहीं हो सकता। Gross cannot be less than tare.");
        row.Kind = body.Kind;
        row.TruckNo = TextMatch.CanonTruck(body.TruckNo);
        row.RstMissing = body.RstMissing;
        row.RstNo = body.RstMissing ? null : body.RstNo!.Trim();
        row.GrossKg = body.RstMissing ? body.GrossKg : body.GrossKg;
        row.TareKg = body.RstMissing ? body.TareKg : body.TareKg;
        row.WeighedAt = body.WeighedAt;
        row.PartyName = Clean(body.PartyName);
        row.Material = Clean(body.Material);
        row.WeighbridgeName = Clean(body.WeighbridgeName);
    }

    private void SyncBags(Lot lot, Guid refId, string source, DateOnly day, int bagsNew, int bagsOld, string direction)
    {
        var existing = db.BagMovements.Local.Where(b => b.RefId == refId && b.Source == source).ToList();
        existing.AddRange(db.BagMovements.Where(b => b.RefId == refId && b.Source == source).ToList().Where(b => existing.All(e => e.Id != b.Id)));
        void One(string type, int qty)
        {
            var row = existing.FirstOrDefault(x => x.BagType == type && x.Direction == direction);
            if (qty <= 0)
            {
                if (row is not null) row.Voided = true;
                return;
            }
            if (row is null)
            {
                db.BagMovements.Add(new BagMovement
                {
                    SeasonId = lot.SeasonId,
                    LotId = lot.Id,
                    MovedOn = day,
                    BagType = type,
                    Direction = direction,
                    Qty = qty,
                    Source = source,
                    RefId = refId,
                    EntryStatus = EntryRules.Approved,
                    Voided = false
                });
            }
            else
            {
                row.Qty = qty;
                row.MovedOn = day;
                row.Voided = false;
                row.LotId = lot.Id;
                row.SeasonId = lot.SeasonId;
            }
        }
        One("new", bagsNew);
        One("old", bagsOld);
    }

    private async Task<Season> ResolveSeason(Guid? id, CancellationToken ct)
    {
        if (id is Guid seasonId)
            return await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AppException(404, "not_found", Messages.NotFound);
        var today = Clock.Today();
        var seasons = await db.Seasons.OrderByDescending(s => s.StartsOn).ToListAsync(ct);
        return seasons.FirstOrDefault(s => s.StartsOn <= today && (s.EndsOn is null || s.EndsOn >= today))
            ?? seasons.FirstOrDefault()
            ?? throw new AppException(400, "season", "पहले सीजन बनाएँ। Create a season first.");
    }

    private void EnsureEdit(string status, Guid createdBy)
    {
        if (current.IsOwner) return;
        if (current.Role == "staff" && EntryRules.StaffMayEdit(status, createdBy, current.UserId!.Value)) return;
        throw new AppException(409, "locked", Messages.LockedEntry);
    }

    private async Task<WriteResultDto?> Replay(Guid? clientOpId, CancellationToken ct)
    {
        if (clientOpId is null) return null;
        var hit = await db.ClientOps.FirstOrDefaultAsync(x => x.ClientOpId == clientOpId, ct);
        return hit is null ? null : JsonSerializer.Deserialize<WriteResultDto>(hit.ResultJson, Json);
    }

    private void Remember(Guid? clientOpId, string entityType, WriteResultDto result)
    {
        if (clientOpId is null) return;
        db.ClientOps.Add(new ClientOp
        {
            TenantId = current.TenantId!.Value,
            ClientOpId = clientOpId.Value,
            EntityType = entityType,
            EntityId = result.Id,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            throw new AppException(409, "duplicate", Messages.Duplicate);
        }
    }

    private async Task<Editable?> FindAsync(string type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case "lot":
                var lot = await db.Lots.FirstOrDefaultAsync(x => x.Id == id, ct);
                return lot is null ? null : Editable.Of(() => lot.EntryStatus, v => lot.EntryStatus = v, () => lot.ReviewNote, v => lot.ReviewNote = v, lot.CreatedBy, lot.Id);
            case "paddy_ro":
                var ro = await db.PaddyRos.FirstOrDefaultAsync(x => x.Id == id, ct);
                return ro is null ? null : Editable.Of(() => ro.EntryStatus, v => ro.EntryStatus = v, () => ro.ReviewNote, v => ro.ReviewNote = v, ro.CreatedBy, ro.Id);
            case "cmr_do":
                var dob = await db.CmrDos.FirstOrDefaultAsync(x => x.Id == id, ct);
                return dob is null ? null : Editable.Of(() => dob.EntryStatus, v => dob.EntryStatus = v, () => dob.ReviewNote, v => dob.ReviewNote = v, dob.CreatedBy, dob.Id);
            case "acceptance":
                var acc = await db.Acceptances.FirstOrDefaultAsync(x => x.Id == id, ct);
                return acc is null ? null : Editable.Of(() => acc.EntryStatus, v => acc.EntryStatus = v, () => acc.ReviewNote, v => acc.ReviewNote = v, acc.CreatedBy, acc.Id);
            case "weighment":
                var w = await db.Weighments.FirstOrDefaultAsync(x => x.Id == id, ct);
                return w is null ? null : Editable.Of(() => w.EntryStatus, v => w.EntryStatus = v, () => w.ReviewNote, v => w.ReviewNote = v, w.CreatedBy, w.Id);
            case "by_product_sale":
                var sale = await db.ByProductSales.FirstOrDefaultAsync(x => x.Id == id, ct);
                return sale is null ? null : Editable.Of(() => sale.EntryStatus, v => sale.EntryStatus = v, () => sale.ReviewNote, v => sale.ReviewNote = v, sale.CreatedBy, sale.Id);
            default:
                return null;
        }
    }

    private sealed class Editable
    {
        public required Func<string> GetStatus { get; init; }
        public required Action<string> SetStatus { get; init; }
        public required Action<string?> SetNote { get; init; }
        public static Editable Of(Func<string> get, Action<string> set, Func<string?> getNote, Action<string?> setNote, Guid createdBy, Guid id) =>
            new() { GetStatus = get, SetStatus = set, SetNote = setNote };
    }

    private async Task<Graph> LoadAsync(CancellationToken ct)
    {
        var lots = await db.Lots.AsNoTracking().ToListAsync(ct);
        var ros = await db.PaddyRos.AsNoTracking().ToDictionaryAsync(x => x.LotId, ct);
        var dos = await db.CmrDos.AsNoTracking().ToDictionaryAsync(x => x.LotId, ct);
        var acc = await db.Acceptances.AsNoTracking().ToDictionaryAsync(x => x.LotId, ct);
        var weighments = await db.Weighments.AsNoTracking().ToListAsync(ct);
        var files = await db.Documents.AsNoTracking().Select(d => new { d.OwnerType, d.OwnerId }).ToListAsync(ct);
        return new Graph(
            lots, ros, dos, acc,
            weighments.GroupBy(w => w.LotId).ToDictionary(g => g.Key, g => g.ToList()),
            files.Select(f => (f.OwnerType, f.OwnerId)).ToHashSet());
    }

    private static LotSummaryDto ToSummary(Lot lot, Graph graph)
    {
        var check = Evaluate(lot, graph);
        var balance = BalanceOf(lot, graph);
        return new LotSummaryDto(
            lot.Id, lot.LotNo, lot.CentreName, lot.TruckNo, lot.Stage, lot.EntryStatus, lot.ReviewNote,
            check.IsComplete, check.Missing, check.Mismatches,
            balance.Paddy, balance.Due, balance.Accepted, balance.Pending, lot.OutturnPct, lot.UpdatedAt);
    }

    private static LotDetailDto ToDetail(Lot lot, Graph graph)
    {
        graph.Ros.TryGetValue(lot.Id, out var ro);
        graph.Dos.TryGetValue(lot.Id, out var dob);
        graph.Acceptances.TryGetValue(lot.Id, out var acc);
        graph.Weighments.TryGetValue(lot.Id, out var ws);
        ws ??= [];
        var check = Evaluate(lot, graph);
        var balance = BalanceOf(lot, graph);
        var ids = new HashSet<Guid> { lot.Id };
        if (ro is not null) ids.Add(ro.Id);
        if (dob is not null) ids.Add(dob.Id);
        if (acc is not null) ids.Add(acc.Id);
        foreach (var w in ws) ids.Add(w.Id);
        return new LotDetailDto(
            lot.Id, lot.SeasonId, lot.LotNo, lot.CentreName, lot.CentreCode, lot.TruckNo, lot.PaddyQtyQtl,
            lot.OutturnPct, lot.Stage, lot.EntryStatus, lot.ReviewNote, lot.OwnerNote, lot.MismatchesAcked,
            lot.MismatchAckNote, lot.OpenedOn,
            new BalanceDto(balance.Paddy, balance.Due, balance.Accepted, balance.Pending, lot.OutturnPct),
            new CheckDto(check.Missing, check.Mismatches, check.IsComplete, lot.MismatchesAcked),
            ro is null ? null : MapRo(ro), dob is null ? null : MapDo(dob), acc is null ? null : MapAcc(acc),
            ws.OrderBy(w => w.WeighedAt).Select(MapW).ToList(),
            [],
            lot.RowVersion);
    }

    public async Task<LotDetailDto> GetWithDocsAsync(Guid id, CancellationToken ct)
    {
        var detail = await GetAsync(id, ct);
        var ids = new List<Guid> { detail.Id };
        if (detail.Ro is not null) ids.Add(detail.Ro.Id);
        if (detail.Do is not null) ids.Add(detail.Do.Id);
        if (detail.Acceptance is not null) ids.Add(detail.Acceptance.Id);
        ids.AddRange(detail.Weighments.Select(w => w.Id));
        var docs = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.OwnerId))
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto(d.Id, d.OwnerType, d.OwnerId, d.DocKind, d.Mime, d.SizeBytes, d.CreatedAt))
            .ToListAsync(ct);
        return detail with { Documents = docs };
    }

    private static CheckOutput Evaluate(Lot lot, Graph graph)
    {
        graph.Ros.TryGetValue(lot.Id, out var ro);
        graph.Dos.TryGetValue(lot.Id, out var dob);
        graph.Acceptances.TryGetValue(lot.Id, out var acc);
        graph.Weighments.TryGetValue(lot.Id, out var ws);
        ws ??= [];
        bool Has(string type, Guid id) => graph.Files.Contains((type, id));
        return LotCheck.Evaluate(new CheckInput(
            lot.LotNo, lot.CentreName, lot.TruckNo,
            ro is null ? null : new PaperSnap(ro.PrintedLotNo, ro.CentreName, ro.TruckNo, Has("paddy_ro", ro.Id)),
            dob is null ? null : new PaperSnap(dob.PrintedLotNo, dob.IssueCentre, dob.TruckNo, Has("cmr_do", dob.Id)),
            dob?.DoDate,
            acc is null ? null : new PaperSnap(acc.PrintedLotNo, acc.IssueCentre, acc.TruckNo, Has("acceptance", acc.Id)),
            acc?.AcceptedOn,
            acc?.ToulParchiNo,
            ws.Select(w => new KaantaSnap(w.RstNo, w.RstMissing, Clock.Day(w.WeighedAt), w.TruckNo, Has("weighment", w.Id), w.Kind == "rice_out")).ToList(),
            lot.MismatchesAcked));
    }

    private static (decimal? Paddy, decimal? Due, decimal? Accepted, decimal? Pending) BalanceOf(Lot lot, Graph graph)
    {
        graph.Ros.TryGetValue(lot.Id, out var ro);
        graph.Acceptances.TryGetValue(lot.Id, out var acc);
        return LotBalance.Compute(lot.PaddyQtyQtl, ro?.PaddyQtyQtl, lot.OutturnPct, acc?.AcceptedQtyQtl);
    }

    private static PaddyDto MapRo(PaddyRo r) => new(
        r.Id, r.RoNo, r.RoDate, r.CentreName, r.DistrictName, r.TruckNo, r.PaddyQtyQtl, r.GradeAQtyQtl, r.OrderQtyQtl,
        r.BagsNew, r.BagsOld, r.PaddyDoNo, r.PaddyDoDate, r.PaddyDoValidTill, r.AgreementNo, r.GodownName, r.PrintedLotNo,
        r.EntryStatus, r.ReviewNote);

    private static DoDto MapDo(CmrDo r) => new(
        r.Id, r.DoNo, r.DoDate, r.Destination, r.RiceQtyQtl, r.TruckNo, r.ValidTill, r.AgreementNo, r.CmrType,
        r.GodownName, r.DistrictName, r.IssueCentre, r.PaddyDoNo, r.PrintedLotNo, r.FciLotNo, r.EntryStatus, r.ReviewNote);

    private static AcceptanceDto MapAcc(Acceptance r) => new(
        r.Id, r.DoId, r.CmraNo, r.AcceptedOn, r.AcceptedQtyQtl, r.BagsNew, r.BagsOld, r.TruckNo, r.ToulParchiNo,
        r.Grade, r.GodownName, r.IssueCentre, r.CmrType, r.PrintedLotNo, r.DepositDistrict, r.EntryStatus, r.ReviewNote);

    private static WeighmentDto MapW(Weighment r) => new(
        r.Id, r.Kind, r.TruckNo, r.RstNo, r.RstMissing, r.GrossKg, r.TareKg, r.NetKg, r.WeighedAt, r.PartyName,
        r.Material, r.WeighbridgeName, r.EntryStatus, r.ReviewNote);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? TruckOrNull(string? value)
    {
        var truck = TextMatch.CanonTruck(value);
        return truck.Length == 0 ? null : truck;
    }
    private static bool Contains(string? value, string q) =>
        !string.IsNullOrEmpty(value) && value.Contains(q, StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyList<string> WarningList(string? code) => code is null ? [] : [code];

    private sealed record Graph(
        List<Lot> Lots,
        Dictionary<Guid, PaddyRo> Ros,
        Dictionary<Guid, CmrDo> Dos,
        Dictionary<Guid, Acceptance> Acceptances,
        Dictionary<Guid, List<Weighment>> Weighments,
        HashSet<(string, Guid)> Files);
}
