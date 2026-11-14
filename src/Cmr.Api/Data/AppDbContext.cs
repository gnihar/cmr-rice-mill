using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cmr.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser current) : DbContext(options)
{
    public Guid CurrentTenantId => current.TenantId ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<PaddyRo> PaddyRos => Set<PaddyRo>();
    public DbSet<CmrDo> CmrDos => Set<CmrDo>();
    public DbSet<Acceptance> Acceptances => Set<Acceptance>();
    public DbSet<Weighment> Weighments => Set<Weighment>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<BagMovement> BagMovements => Set<BagMovement>();
    public DbSet<ByProductSale> ByProductSales => Set<ByProductSale>();
    public DbSet<ByProductPrice> ByProductPrices => Set<ByProductPrice>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<EntryReview> EntryReviews => Set<EntryReview>();
    public DbSet<ClientOp> ClientOps => Set<ClientOp>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            entity.SetTableName(Snake(entity.GetTableName()!));
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(Snake(prop.Name));
        }

        b.Entity<Tenant>().HasKey(x => x.Id);
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.PhoneE164).IsUnique().HasFilter("phone_e164 IS NOT NULL");
            e.HasIndex(x => x.Username).IsUnique().HasFilter("username IS NOT NULL");
            e.Property(x => x.Name).HasMaxLength(120);
            e.ToTable(t => t.HasCheckConstraint("ck_users_role", "role IN ('owner','staff')"));
            e.ToTable(t => t.HasCheckConstraint("ck_users_identity", "phone_e164 IS NOT NULL OR username IS NOT NULL"));
        });
        b.Entity<Device>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
        });
        b.Entity<Season>().HasIndex(x => new { x.TenantId, x.Label }).IsUnique();
        b.Entity<Lot>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.SeasonId, x.LotNo }).IsUnique();
            e.Property(x => x.OutturnPct).HasPrecision(5, 2);
            e.Property(x => x.PaddyQtyQtl).HasPrecision(12, 2);
            e.ToTable(t => t.HasCheckConstraint("ck_lots_outturn", "outturn_pct > 0 AND outturn_pct <= 100"));
        });
        b.Entity<PaddyRo>(e =>
        {
            e.HasIndex(x => x.LotId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RoNo }).IsUnique();
            e.Property(x => x.PaddyQtyQtl).HasPrecision(12, 2);
            e.Property(x => x.GradeAQtyQtl).HasPrecision(12, 2);
            e.Property(x => x.OrderQtyQtl).HasPrecision(12, 2);
            e.HasIndex(x => x.ClientOpId);
        });
        b.Entity<CmrDo>(e =>
        {
            e.HasIndex(x => x.LotId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.DoNo }).IsUnique();
            e.Property(x => x.RiceQtyQtl).HasPrecision(12, 2);
        });
        b.Entity<Acceptance>(e =>
        {
            e.HasIndex(x => x.LotId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CmraNo }).IsUnique();
            e.Property(x => x.AcceptedQtyQtl).HasPrecision(12, 2);
            e.HasOne<CmrDo>().WithMany().HasForeignKey(x => x.DoId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Weighment>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Kind, x.RstNo }).IsUnique().HasFilter("rst_no IS NOT NULL");
            e.Property(x => x.NetKg).HasComputedColumnSql(
                "CASE WHEN gross_kg IS NULL OR tare_kg IS NULL THEN NULL ELSE gross_kg - tare_kg END", stored: true);
            e.Property(x => x.NetKg).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            e.Property(x => x.NetKg).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_weighment_rst",
                "(rst_missing = true) OR (rst_no IS NOT NULL AND gross_kg IS NOT NULL AND tare_kg IS NOT NULL AND gross_kg >= tare_kg)"));
        });
        b.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId, x.Sha256 }).IsUnique();
            e.Property(x => x.OcrJson).HasColumnType("jsonb");
            e.ToTable(t => t.HasCheckConstraint("ck_documents_size", "size_bytes > 0 AND size_bytes <= 10485760"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId && x.DeletedAt == null);
        });
        b.Entity<BagMovement>(e =>
        {
            e.Property(x => x.Qty);
            e.ToTable(t => t.HasCheckConstraint("ck_bags_qty", "qty > 0"));
            e.ToTable(t => t.HasCheckConstraint("ck_bags_type", "bag_type IN ('new','old')"));
        });
        b.Entity<ByProductSale>(e =>
        {
            e.Property(x => x.Qty).HasPrecision(12, 2);
            e.ToTable(t => t.HasCheckConstraint("ck_sale_qty", "qty > 0"));
        });
        b.Entity<ByProductPrice>(e =>
        {
            e.HasKey(x => x.SaleId);
            e.Property(x => x.Rate).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.AmountReceived).HasPrecision(14, 2);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        b.Entity<Expense>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.ToTable(t => t.HasCheckConstraint("ck_expense_amt", "amount > 0"));
        });
        b.Entity<Receipt>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.ToTable(t => t.HasCheckConstraint("ck_receipt_amt", "amount > 0"));
        });
        b.Entity<ClientOp>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.ClientOpId });
            e.Property(x => x.ResultJson).HasColumnType("jsonb");
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.BeforeJson).HasColumnName("before").HasColumnType("jsonb");
            e.Property(x => x.AfterJson).HasColumnName("after").HasColumnType("jsonb");
            e.Property(x => x.Ip).HasColumnType("inet");
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        TenantFilter<User>(b);
        TenantFilter<Device>(b);
        TenantFilter<Season>(b);
        TenantFilter<Lot>(b);
        TenantFilter<PaddyRo>(b);
        TenantFilter<CmrDo>(b);
        TenantFilter<Acceptance>(b);
        TenantFilter<Weighment>(b);
        TenantFilter<BagMovement>(b);
        TenantFilter<ByProductSale>(b);
        TenantFilter<Expense>(b);
        TenantFilter<Receipt>(b);
        TenantFilter<EntryReview>(b);

        foreach (var entity in b.Model.GetEntityTypes())
        {
            var prop = entity.GetProperties().FirstOrDefault(p => p.Name == "RowVersion");
            if (prop is not null) prop.IsConcurrencyToken = true;
        }

        b.Entity<Tenant>().ToTable("tenants");
        b.Entity<User>().ToTable("users");
        b.Entity<Device>().ToTable("devices");
        b.Entity<Season>().ToTable("seasons");
        b.Entity<Lot>().ToTable("lots");
        b.Entity<PaddyRo>().ToTable("paddy_ros");
        b.Entity<CmrDo>().ToTable("cmr_dos");
        b.Entity<Acceptance>().ToTable("acceptances");
        b.Entity<Weighment>().ToTable("weighments");
        b.Entity<Document>().ToTable("documents");
        b.Entity<BagMovement>().ToTable("bag_movements");
        b.Entity<ByProductSale>().ToTable("by_product_sales");
        b.Entity<ByProductPrice>().ToTable("by_product_prices");
        b.Entity<Expense>().ToTable("expenses");
        b.Entity<Receipt>().ToTable("receipts");
        b.Entity<EntryReview>().ToTable("entry_reviews");
        b.Entity<ClientOp>().ToTable("client_ops");
        b.Entity<AuditLog>().ToTable("audit_log");
    }

    private void TenantFilter<T>(ModelBuilder b) where T : class, ITenantOwned =>
        b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAudited>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty) entry.Entity.Id = Guid.NewGuid();
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                if (entry.Entity.RowVersion == 0) entry.Entity.RowVersion = 1;
                if (entry.Entity.TenantId == Guid.Empty && current.TenantId is Guid tenant) entry.Entity.TenantId = tenant;
                if (entry.Entity.CreatedBy == Guid.Empty && current.UserId is Guid user) entry.Entity.CreatedBy = user;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.RowVersion++;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    public static string Snake(string name)
    {
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CMR_OWNER_CS")
                 ?? "Host=localhost;Port=5432;Database=cmr;Username=cmr_owner;Password=cmr_owner_dev";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
        return new AppDbContext(options, new CurrentUser());
    }
}
