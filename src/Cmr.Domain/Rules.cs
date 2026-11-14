using System.Text.RegularExpressions;

namespace Cmr.Domain;

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>ID shapes seen on MP printouts. One sample each, so a mismatch warns and never blocks.</summary>
public static partial class DocumentIds
{
    public static string Norm(string value) => value.Trim().ToUpperInvariant();

    public static string? Warning(string kind, string value)
    {
        var v = Norm(value);
        var ok = kind switch
        {
            "ro" => Ro().IsMatch(v),
            "do" => Do().IsMatch(v),
            "cmra" => Cmra().IsMatch(v),
            _ => true
        };
        return ok ? null : kind switch
        {
            "ro" => "ro_format",
            "do" => "do_format",
            "cmra" => "cmra_format",
            _ => null
        };
    }

    [GeneratedRegex("^PDY[0-9]{10,18}$")]
    private static partial Regex Ro();

    [GeneratedRegex("^CDO[0-9]{10,18}$")]
    private static partial Regex Do();

    [GeneratedRegex("^CMRA[0-9]{10,18}$")]
    private static partial Regex Cmra();
}

public static partial class Phones
{
    public static bool TryNormalizeIndia(string raw, out string e164)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
            digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith('0'))
            digits = digits[1..];

        if (digits.Length == 10 && digits[0] is >= '6' and <= '9')
        {
            e164 = "+91" + digits;
            return true;
        }

        e164 = "";
        return false;
    }
}

public static partial class LoginNames
{
    public static bool TryParse(string raw, out string? phoneE164, out string? username)
    {
        phoneE164 = null;
        username = null;
        var s = raw.Trim();
        if (s.Length == 0) return false;

        var letters = s.Count(char.IsLetter);
        var digits = s.Count(char.IsDigit);
        if (letters == 0 && digits >= 10)
            return Phones.TryNormalizeIndia(s, out phoneE164);

        username = s.ToLowerInvariant();
        return User().IsMatch(username);
    }

    public static bool IsUsername(string username) => User().IsMatch(username);

    [GeneratedRegex("^[a-z0-9_]{3,32}$")]
    private static partial Regex User();
}

public static partial class TextMatch
{
    public static string CanonTruck(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return NotTruck().Replace(raw.Trim().ToUpperInvariant(), "");
    }

    public static string CanonLot(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return Spaces().Replace(raw.Trim().ToUpperInvariant(), "");
    }

    public static string CanonCentre(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return Spaces().Replace(raw.Trim(), " ").ToUpperInvariant();
    }

    public static bool SameLot(string? a, string? b)
    {
        var x = CanonLot(a);
        var y = CanonLot(b);
        return x.Length > 0 && y.Length > 0 && x == y;
    }

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NotTruck();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();
}

public static class EntryRules
{
    public const string Submitted = "submitted";
    public const string SentBack = "sent_back";
    public const string Approved = "approved";

    public static string InitialStatus(bool actorIsOwner) =>
        actorIsOwner ? Approved : Submitted;

    public static bool StaffMayEdit(string status, Guid createdBy, Guid currentUser) =>
        status == SentBack && createdBy == currentUser;
}

public static class LotBalance
{
    public static (decimal? Paddy, decimal? Due, decimal? Accepted, decimal? Pending) Compute(
        decimal? lotPaddyQtl, decimal? roPaddyQtl, decimal outturnPct, decimal? acceptedQtl)
    {
        var paddy = roPaddyQtl ?? lotPaddyQtl;
        if (paddy is null) return (null, null, acceptedQtl, null);
        var due = decimal.Round(paddy.Value * outturnPct / 100m, 2, MidpointRounding.AwayFromZero);
        var accepted = acceptedQtl ?? 0m;
        return (paddy, due, acceptedQtl, due - accepted);
    }
}

public static class BagMath
{
    public static int Signed(string direction, int qty) => direction switch
    {
        "in" => qty,
        "out" or "used_packing" or "returned" or "lost" => -qty,
        _ => throw new DomainException("bag_direction", "Unknown bag direction.")
    };

    public static (int In, int Out, int Balance) Sum(IEnumerable<(string Direction, int Qty)> rows)
    {
        var inn = 0;
        var outn = 0;
        foreach (var (direction, qty) in rows)
        {
            var signed = Signed(direction, qty);
            if (signed >= 0) inn += signed;
            else outn += -signed;
        }
        return (inn, outn, inn - outn);
    }
}

public sealed record PaperSnap(string? PrintedLotNo, string? Centre, string? Truck, bool HasFile);

public sealed record KaantaSnap(string? RstNo, bool MarkedMissing, DateOnly? Date, string? Truck, bool HasFile, bool RiceOut);

public sealed record CheckInput(
    string LotNo,
    string Centre,
    string? Truck,
    PaperSnap? Ro,
    PaperSnap? Do,
    DateOnly? DoDate,
    PaperSnap? Acceptance,
    DateOnly? AcceptanceDate,
    string? ToulParchiNo,
    IReadOnlyList<KaantaSnap> Kaantas,
    bool MismatchesAcked);

public sealed record CheckOutput(IReadOnlyList<string> Missing, IReadOnlyList<string> Mismatches, bool IsComplete);

/// <summary>
/// A lot is green only when the challan, deposit order, acceptance and a real kaanta
/// (RST plus file, not marked missing) are all present and no cross-check is still open.
/// The challan may be added late; until it is, the lot stays incomplete.
/// </summary>
public static class LotCheck
{
    public static CheckOutput Evaluate(CheckInput input)
    {
        var missing = new List<string>();
        void Need(bool data, bool file, string dataCode, string fileCode)
        {
            if (!data) missing.Add(dataCode);
            else if (!file) missing.Add(fileCode);
        }

        Need(input.Ro is not null, input.Ro?.HasFile == true, "ro", "ro_file");
        Need(input.Do is not null, input.Do?.HasFile == true, "do", "do_file");
        Need(input.Acceptance is not null, input.Acceptance?.HasFile == true, "acceptance", "acceptance_file");

        var goodKaanta = input.Kaantas.Any(k => !k.MarkedMissing && !string.IsNullOrWhiteSpace(k.RstNo) && k.HasFile);
        if (!goodKaanta)
        {
            if (input.Kaantas.Count == 0 || input.Kaantas.All(k => k.MarkedMissing))
                missing.Add("kaanta");
            else if (input.Kaantas.Any(k => !k.MarkedMissing && !string.IsNullOrWhiteSpace(k.RstNo) && !k.HasFile))
                missing.Add("kaanta_file");
            else
                missing.Add("kaanta");
        }

        var mismatches = new List<string>();
        var lotNo = TextMatch.CanonLot(input.LotNo);
        foreach (var paper in new[] { input.Ro, input.Do, input.Acceptance })
        {
            if (paper is null || string.IsNullOrWhiteSpace(paper.PrintedLotNo)) continue;
            if (TextMatch.CanonLot(paper.PrintedLotNo) != lotNo) mismatches.Add("lot_no");
        }

        var lotCentre = TextMatch.CanonCentre(input.Centre);
        foreach (var paper in new[] { input.Ro, input.Do, input.Acceptance })
        {
            if (paper is null || string.IsNullOrWhiteSpace(paper.Centre)) continue;
            if (TextMatch.CanonCentre(paper.Centre) != lotCentre) mismatches.Add("centre");
        }

        var trucks = new HashSet<string>(StringComparer.Ordinal);
        void AddTruck(string? truck)
        {
            var t = TextMatch.CanonTruck(truck);
            if (t.Length > 0) trucks.Add(t);
        }
        AddTruck(input.Truck);
        AddTruck(input.Ro?.Truck);
        AddTruck(input.Do?.Truck);
        AddTruck(input.Acceptance?.Truck);
        foreach (var k in input.Kaantas) AddTruck(k.Truck);
        if (trucks.Count > 1) mismatches.Add("truck");

        if (input.AcceptanceDate is DateOnly accepted)
        {
            if (input.DoDate is DateOnly issued && accepted < issued)
                mismatches.Add("date");
            var riceDates = input.Kaantas.Where(k => k.RiceOut && k.Date is not null && !k.MarkedMissing).Select(k => k.Date!.Value).Distinct().ToList();
            if (riceDates.Count > 0 && riceDates.Any(d => d != accepted))
                mismatches.Add("date");
        }

        if (input.Acceptance is not null)
        {
            var toul = (input.ToulParchiNo ?? "").Trim();
            var rsts = input.Kaantas.Where(k => !k.MarkedMissing && !string.IsNullOrWhiteSpace(k.RstNo)).Select(k => k.RstNo!.Trim()).ToList();
            if (toul.Length == 0 && rsts.Count > 0) mismatches.Add("rst");
            else if (toul.Length > 0 && !rsts.Any(r => string.Equals(r, toul, StringComparison.OrdinalIgnoreCase))) mismatches.Add("rst");
        }

        var distinctMissing = missing.Distinct().ToList();
        var distinctMismatch = mismatches.Distinct().ToList();
        var complete = distinctMissing.Count == 0 && (distinctMismatch.Count == 0 || input.MismatchesAcked);
        return new CheckOutput(distinctMissing, distinctMismatch, complete);
    }
}

public static class Stages
{
    public static readonly string[] Order = ["paddy_received", "milling", "do_received", "dispatched", "accepted", "closed"];

    public static bool IsKnown(string stage) => Order.Contains(stage);

    public static string Advance(string current, string atLeast)
    {
        if (current == "closed") return current;
        var a = Array.IndexOf(Order, current);
        var b = Array.IndexOf(Order, atLeast);
        if (a < 0) return atLeast;
        if (b < 0) return current;
        return b > a ? atLeast : current;
    }
}
