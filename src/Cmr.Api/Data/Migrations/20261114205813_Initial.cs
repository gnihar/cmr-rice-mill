using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cmr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    request_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bag_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    moved_on = table.Column<DateOnly>(type: "date", nullable: false),
                    bag_type = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    voided = table.Column<bool>(type: "boolean", nullable: false),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bag_movements", x => x.id);
                    table.CheckConstraint("ck_bags_qty", "qty > 0");
                    table.CheckConstraint("ck_bags_type", "bag_type IN ('new','old')");
                });

            migrationBuilder.CreateTable(
                name: "by_product_prices",
                columns: table => new
                {
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    amount_received = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    payment_status = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_by_product_prices", x => x.sale_id);
                });

            migrationBuilder.CreateTable(
                name: "by_product_sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sold_on = table.Column<DateOnly>(type: "date", nullable: false),
                    product = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    buyer_name = table.Column<string>(type: "text", nullable: true),
                    vehicle_no = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_by_product_sales", x => x.id);
                    table.CheckConstraint("ck_sale_qty", "qty > 0");
                });

            migrationBuilder.CreateTable(
                name: "client_ops",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_ops", x => new { x.tenant_id, x.client_op_id });
                });

            migrationBuilder.CreateTable(
                name: "cmr_dos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    do_no = table.Column<string>(type: "text", nullable: false),
                    do_date = table.Column<DateOnly>(type: "date", nullable: false),
                    destination = table.Column<string>(type: "text", nullable: false),
                    rice_qty_qtl = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    truck_no = table.Column<string>(type: "text", nullable: true),
                    valid_till = table.Column<DateOnly>(type: "date", nullable: true),
                    agreement_no = table.Column<string>(type: "text", nullable: true),
                    cmr_type = table.Column<string>(type: "text", nullable: true),
                    godown_name = table.Column<string>(type: "text", nullable: true),
                    district_name = table.Column<string>(type: "text", nullable: true),
                    issue_centre = table.Column<string>(type: "text", nullable: true),
                    paddy_do_no = table.Column<string>(type: "text", nullable: true),
                    printed_lot_no = table.Column<string>(type: "text", nullable: true),
                    fci_lot_no = table.Column<string>(type: "text", nullable: true),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cmr_dos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "text", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_kind = table.Column<string>(type: "text", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    mime = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    pages = table.Column<int>(type: "integer", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    upload_status = table.Column<string>(type: "text", nullable: false),
                    ocr_status = table.Column<string>(type: "text", nullable: false),
                    ocr_json = table.Column<string>(type: "jsonb", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                    table.CheckConstraint("ck_documents_size", "size_bytes > 0 AND size_bytes <= 10485760");
                });

            migrationBuilder.CreateTable(
                name: "entry_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    spent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    payee = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.id);
                    table.CheckConstraint("ck_expense_amt", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_no = table.Column<string>(type: "text", nullable: false),
                    centre_name = table.Column<string>(type: "text", nullable: false),
                    centre_code = table.Column<string>(type: "text", nullable: true),
                    truck_no = table.Column<string>(type: "text", nullable: true),
                    paddy_qty_qtl = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    outturn_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    owner_note = table.Column<string>(type: "text", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: true),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    mismatches_acked = table.Column<bool>(type: "boolean", nullable: false),
                    mismatch_ack_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lots", x => x.id);
                    table.CheckConstraint("ck_lots_outturn", "outturn_pct > 0 AND outturn_pct <= 100");
                });

            migrationBuilder.CreateTable(
                name: "paddy_ros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ro_no = table.Column<string>(type: "text", nullable: false),
                    ro_date = table.Column<DateOnly>(type: "date", nullable: false),
                    centre_name = table.Column<string>(type: "text", nullable: false),
                    district_name = table.Column<string>(type: "text", nullable: true),
                    truck_no = table.Column<string>(type: "text", nullable: false),
                    paddy_qty_qtl = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    grade_a_qty_qtl = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    order_qty_qtl = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    bags_new = table.Column<int>(type: "integer", nullable: false),
                    bags_old = table.Column<int>(type: "integer", nullable: false),
                    paddy_do_no = table.Column<string>(type: "text", nullable: true),
                    paddy_do_date = table.Column<DateOnly>(type: "date", nullable: true),
                    paddy_do_valid_till = table.Column<DateOnly>(type: "date", nullable: true),
                    agreement_no = table.Column<string>(type: "text", nullable: true),
                    godown_name = table.Column<string>(type: "text", nullable: true),
                    printed_lot_no = table.Column<string>(type: "text", nullable: true),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paddy_ros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    ref_no = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.id);
                    table.CheckConstraint("ck_receipt_amt", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    default_outturn_pct = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    short_name = table.Column<string>(type: "text", nullable: false),
                    gstin = table.Column<string>(type: "text", nullable: true),
                    mpscsc_reg_no = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    plan = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    phone_e164 = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    pin_hash = table.Column<string>(type: "text", nullable: false),
                    pin_fail_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_identity", "phone_e164 IS NOT NULL OR username IS NOT NULL");
                    table.CheckConstraint("ck_users_role", "role IN ('owner','staff')");
                });

            migrationBuilder.CreateTable(
                name: "weighments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    truck_no = table.Column<string>(type: "text", nullable: false),
                    rst_no = table.Column<string>(type: "text", nullable: true),
                    rst_missing = table.Column<bool>(type: "boolean", nullable: false),
                    gross_kg = table.Column<int>(type: "integer", nullable: true),
                    tare_kg = table.Column<int>(type: "integer", nullable: true),
                    net_kg = table.Column<int>(type: "integer", nullable: true, computedColumnSql: "CASE WHEN gross_kg IS NULL OR tare_kg IS NULL THEN NULL ELSE gross_kg - tare_kg END", stored: true),
                    weighed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    party_name = table.Column<string>(type: "text", nullable: true),
                    material = table.Column<string>(type: "text", nullable: true),
                    weighbridge_name = table.Column<string>(type: "text", nullable: true),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weighments", x => x.id);
                    table.CheckConstraint("ck_weighment_rst", "(rst_missing = true) OR (rst_no IS NOT NULL AND gross_kg IS NOT NULL AND tare_kg IS NOT NULL AND gross_kg >= tare_kg)");
                });

            migrationBuilder.CreateTable(
                name: "acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    do_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cmra_no = table.Column<string>(type: "text", nullable: false),
                    accepted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    accepted_qty_qtl = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    bags_new = table.Column<int>(type: "integer", nullable: false),
                    bags_old = table.Column<int>(type: "integer", nullable: false),
                    truck_no = table.Column<string>(type: "text", nullable: false),
                    toul_parchi_no = table.Column<string>(type: "text", nullable: true),
                    grade = table.Column<string>(type: "text", nullable: true),
                    godown_name = table.Column<string>(type: "text", nullable: true),
                    issue_centre = table.Column<string>(type: "text", nullable: true),
                    cmr_type = table.Column<string>(type: "text", nullable: true),
                    printed_lot_no = table.Column<string>(type: "text", nullable: true),
                    deposit_district = table.Column<string>(type: "text", nullable: true),
                    entry_status = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "FK_acceptances_cmr_dos_do_id",
                        column: x => x.do_id,
                        principalTable: "cmr_dos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_acceptances_do_id",
                table: "acceptances",
                column: "do_id");

            migrationBuilder.CreateIndex(
                name: "IX_acceptances_lot_id",
                table: "acceptances",
                column: "lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_acceptances_tenant_id_cmra_no",
                table: "acceptances",
                columns: new[] { "tenant_id", "cmra_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cmr_dos_lot_id",
                table: "cmr_dos",
                column: "lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cmr_dos_tenant_id_do_no",
                table: "cmr_dos",
                columns: new[] { "tenant_id", "do_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_refresh_token_hash",
                table: "devices",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_user_id",
                table: "devices",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_tenant_id_owner_type_owner_id_sha256",
                table: "documents",
                columns: new[] { "tenant_id", "owner_type", "owner_id", "sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lots_tenant_id_season_id_lot_no",
                table: "lots",
                columns: new[] { "tenant_id", "season_id", "lot_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_paddy_ros_client_op_id",
                table: "paddy_ros",
                column: "client_op_id");

            migrationBuilder.CreateIndex(
                name: "IX_paddy_ros_lot_id",
                table: "paddy_ros",
                column: "lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_paddy_ros_tenant_id_ro_no",
                table: "paddy_ros",
                columns: new[] { "tenant_id", "ro_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seasons_tenant_id_label",
                table: "seasons",
                columns: new[] { "tenant_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_phone_e164",
                table: "users",
                column: "phone_e164",
                unique: true,
                filter: "phone_e164 IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true,
                filter: "username IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_weighments_tenant_id_kind_rst_no",
                table: "weighments",
                columns: new[] { "tenant_id", "kind", "rst_no" },
                unique: true,
                filter: "rst_no IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acceptances");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "bag_movements");

            migrationBuilder.DropTable(
                name: "by_product_prices");

            migrationBuilder.DropTable(
                name: "by_product_sales");

            migrationBuilder.DropTable(
                name: "client_ops");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "entry_reviews");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "lots");

            migrationBuilder.DropTable(
                name: "paddy_ros");

            migrationBuilder.DropTable(
                name: "receipts");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "weighments");

            migrationBuilder.DropTable(
                name: "cmr_dos");
        }
    }
}
