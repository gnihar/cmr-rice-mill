using Cmr.Api.Services;
using Cmr.Domain;
using Xunit;

namespace Cmr.Tests;

public class DomainTests
{
    [Fact]
    public void Outturn_of_100_qtl_at_67_percent_is_67()
    {
        var (_, due, _, pending) = LotBalance.Compute(100m, null, 67m, null);
        Assert.Equal(67.00m, due);
        Assert.Equal(67.00m, pending);
    }

    [Fact]
    public void Release_order_wins_over_the_lot_quantity()
    {
        var (paddy, due, _, _) = LotBalance.Compute(10m, 50m, 68m, 10m);
        Assert.Equal(50m, paddy);
        Assert.Equal(34.00m, due);
    }

    [Fact]
    public void Unusual_document_numbers_warn_and_well_formed_ones_do_not()
    {
        Assert.Equal("ro_format", DocumentIds.Warning("ro", "challan-1"));
        Assert.Null(DocumentIds.Warning("ro", "PDY26000000000012"));
        Assert.Equal("do_format", DocumentIds.Warning("do", "CDO-12"));
        Assert.Null(DocumentIds.Warning("do", "CDO2600000000012"));
        Assert.Null(DocumentIds.Warning("cmra", "cmra26000000000012"));
        Assert.Equal("cmra_format", DocumentIds.Warning("cmra", "CMRA"));
    }

    [Fact]
    public void Phone_and_username_parse()
    {
        Assert.True(Phones.TryNormalizeIndia("98000 00001", out var phone));
        Assert.Equal("+919800000001", phone);
        Assert.True(LoginNames.TryParse("Owner", out var parsedPhone, out var user));
        Assert.Null(parsedPhone);
        Assert.Equal("owner", user);
        Assert.True(LoginNames.TryParse("+91 98000 00002", out parsedPhone, out user));
        Assert.Equal("+919800000002", parsedPhone);
    }

    [Fact]
    public void Truck_formatting_does_not_count_as_a_mismatch()
    {
        Assert.Equal("MP09SK1001", TextMatch.CanonTruck("mp-09 sk 1001"));
    }

    [Fact]
    public void Staff_can_edit_only_their_sent_back_entry()
    {
        var staff = Guid.NewGuid();
        Assert.False(EntryRules.StaffMayEdit("submitted", staff, staff));
        Assert.False(EntryRules.StaffMayEdit("approved", staff, staff));
        Assert.False(EntryRules.StaffMayEdit("sent_back", Guid.NewGuid(), staff));
        Assert.True(EntryRules.StaffMayEdit("sent_back", staff, staff));
    }

    [Fact]
    public void Bag_stock_is_in_minus_out()
    {
        var (inn, outn, balance) = BagMath.Sum([("in", 40), ("in", 10), ("out", 30), ("lost", 5)]);
        Assert.Equal(50, inn);
        Assert.Equal(35, outn);
        Assert.Equal(15, balance);
    }

    [Fact]
    public void Lot_is_green_only_when_every_paper_and_check_is_in()
    {
        var full = Sample(files: true, truck: "MP09SK1001", doTruck: "MP09SK1001", missingKaanta: false);
        Assert.Empty(full.Missing);
        Assert.Empty(full.Mismatches);
        Assert.True(full.IsComplete);

        var noFile = Sample(files: false, truck: "MP09SK1001", doTruck: "MP09SK1001", missingKaanta: false);
        Assert.Contains("ro_file", noFile.Missing);
        Assert.False(noFile.IsComplete);

        var missingSlip = Sample(files: true, truck: "MP09SK1001", doTruck: "MP09SK1001", missingKaanta: true);
        Assert.Contains("kaanta", missingSlip.Missing);
        Assert.False(missingSlip.IsComplete);

        var mismatch = Sample(files: true, truck: "MP09SK1001", doTruck: "MP09SK9999", missingKaanta: false);
        Assert.Contains("truck", mismatch.Mismatches);
        Assert.False(mismatch.IsComplete);
        var acked = LotCheck.Evaluate(Input(files: true, truck: "MP09SK1001", doTruck: "MP09SK9999", missingKaanta: false, acked: true));
        Assert.True(acked.IsComplete);
    }

    [Fact]
    public void Acceptance_date_before_the_deposit_order_is_a_mismatch_and_rst_must_link()
    {
        var input = Input(files: true, truck: "MP09SK1001", doTruck: "MP09SK1001", missingKaanta: false, acked: false);
        var early = input with { AcceptanceDate = new DateOnly(2026, 1, 1), DoDate = new DateOnly(2026, 2, 1) };
        Assert.Contains("date", LotCheck.Evaluate(early).Mismatches);
        var unlink = input with { ToulParchiNo = "999" };
        Assert.Contains("rst", LotCheck.Evaluate(unlink).Mismatches);
    }

    [Fact]
    public void Jpeg_exif_segment_is_removed()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x08, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0xFF, 0xD9 };
        var clean = ImageSanitizer.StripJpeg(jpeg);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, clean);
    }

    private static CheckOutput Sample(bool files, string truck, string doTruck, bool missingKaanta) =>
        LotCheck.Evaluate(Input(files, truck, doTruck, missingKaanta, false));

    private static CheckInput Input(bool files, string truck, string doTruck, bool missingKaanta, bool acked) => new(
        "SK-12", "Demo Kendra", truck,
        new PaperSnap("SK-12", "Demo Kendra", truck, files),
        new PaperSnap("SK-12", "Demo Kendra", doTruck, files),
        new DateOnly(2026, 11, 12),
        new PaperSnap("SK-12", "Demo Kendra", truck, files),
        new DateOnly(2026, 11, 14),
        "100012",
        [new KaantaSnap(missingKaanta ? null : "100012", missingKaanta, new DateOnly(2026, 11, 14), truck, files, true)],
        acked);
}
