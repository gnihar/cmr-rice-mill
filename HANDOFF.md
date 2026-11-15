# Handoff — CMR register v1.1.2

This file is for the next person (or coding agent) who continues the app. Read it with `README.md` and `CHANGELOG.md`. The product decisions below came from Nihar and override the two research briefs that started the work.

## Product rules that are easy to get wrong

- One lot has at most one paddy challan (RO, `PDY…`), one CMR deposit order (DO, `CDO…`), and one acceptance (`CMRA…`). They are cross-checked, not forced to be entered in that order.
- The challan is optional until the lot should turn green. The deposit order and the challan are not foreign keys of each other.
- An acceptance requires a deposit order on the same lot. Enforced in `MillService.UpsertAcceptanceAsync` with HTTP 422 `acceptance_requires_do`.
- A duplicate RO, DO, CMRA, or RST number is rejected (409). A weird prefix or length only adds a warning code (`ro_format`, `do_format`, `cmra_format`). Patterns live in `Cmr.Domain.DocumentIds` and came from one sample each, so do not tighten them without more real numbers.
- Kaanta (weighment) is required for a green lot but can be stored with `rst_missing = true`. Missing keeps the lot incomplete. The acceptance field `toul_parchi_no` must match a non-missing kaanta `rst_no`.
- Green (`Check.IsComplete`) also needs a file on each paper and no unacknowledged mismatch. The owner acknowledges with `POST /api/lots/{id}/acknowledge`. Any later edit of the papers clears the acknowledgement.
- Compared fields: printed lot number, centre (the DO uses issue centre, not the godown destination), truck (punctuation ignored), acceptance date vs rice-out kaanta date, acceptance before the DO date, and the RST link. See `Cmr.Domain.LotCheck`.
- Out-turn defaults to 67. Rice due is `paddy × outturn / 100`, rounded to 2 decimals, half away from zero. The challan quantity wins over the quantity typed on the lot. Do not hard-code 433 qtl or 290 qtl.
- Bags: `new` and `old`, directions `in`, `out`, `used_packing`, `returned`, `lost`. Challan creates `in` movements; acceptance creates `out`. Updates void a row instead of deleting it (`bag_movements.voided`). The app role cannot `DELETE`.
- Staff entries are created as `submitted` and then locked. Staff may update only when `entry_status = sent_back` and `created_by` is themselves (`Cmr.Domain.EntryRules`). The owner sends back with a note or approves. Owner-created rows start as `approved`.
- Staff must not see money. Tables: `by_product_prices`, `expenses`, `receipts`. Staff DTOs do not have those properties. Endpoints return **404** (not 403) for staff. Tests walk the JSON and reject property names such as `amount`, `rate`, `amountReceived`, `paymentStatus`.
- By-product staff fields are item, quantity, unit, buyer, truck. Owner adds rate, amount, amount received, payment status (`due`, `part`, `paid`).
- Login is PIN only: username **or** Indian mobile, bcrypt hash, 5 failures then 15 minutes lock. OTP is deferred. Do not build an OTP screen until DLT registration exists. Extension points: `ILoginFactor`, `IOtpSender` (throws if called), `ISessionIssuer`.
- `tenant_id` on business rows comes from the JWT, never from the body. Postgres RLS is forced. Policy allows the row when `app.tenant_id` matches, or when the current role is a member of `cmr_migrator` (migrations, seed, and the security-definer login lookup). The API connects as `cmr_app` and refuses to start otherwise.
- Demo seed runs only when `SEED_DEMO=true` (or Development `Seed:Demo`) **and** there is no tenant yet. Data in `DemoSeeder` is synthetic. Do not paste numbers from the sample PDFs.

## Where to change things

| Task | Where |
|---|---|
| Completeness, out-turn, bags, ID warnings | `src/Cmr.Domain/Rules.cs` and `tests/Cmr.Tests/DomainTests.cs` |
| Lot, papers, send-back | `src/Cmr.Api/Services/MillService.cs` |
| Money, files, users, vault | `src/Cmr.Api/Services/OfficeService.cs` |
| Routes | `src/Cmr.Api/Endpoints/ApiEndpoints.cs` |
| PIN, cookies, refresh | `src/Cmr.Api/Auth/AuthServices.cs` |
| RLS, roles, migrations on boot | `src/Cmr.Api/Data/DatabaseBootstrap.cs` |
| Schema | `src/Cmr.Api/Data/Entities.cs`, `AppDbContext.cs`, then `dotnet ef migrations add` |
| Screens | `src/Cmr.Web/Pages` |
| Version | `Cmr.Contracts.AppVersion` and `CHANGELOG.md` |

New EF migration:

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet tool restore
dotnet ef migrations add <Name> --project src/Cmr.Api --output-dir Data/Migrations
```

Startup runs `Database.Migrate()` and then the RLS script. Keep that script idempotent (`DROP POLICY IF EXISTS`, `CREATE OR REPLACE FUNCTION`).

## Run and test

```bash
docker compose up --build
# or
sudo -u postgres psql -f scripts/prepare-db.sql
dotnet run --project src/Cmr.Api
dotnet test tests/Cmr.Tests/Cmr.Tests.csproj
dotnet test tests/Cmr.E2E/Cmr.E2E.csproj
```

API tests use database `cmr_itest`. Playwright uses `cmr_e2e` on port 5088. Both create their roles with the admin connection `CMR_ADMIN_CS` (default `postgres` / `postgres` on localhost).

Demo owner username `owner`, PIN `246810`. Staff username `staff`, PIN `1357`. These are also returned by `GET /api/auth/demo` only while `SEED_DEMO=true`. Do not put the PINs in the README.

On Neon, `cmr_app` is created once. Later boots must not `ALTER ROLE` it: `neondb_owner` gets SQLSTATE 42501. Keep the original `CMR_APP_PASSWORD`. That guard is `DatabaseBootstrap.EnsureRolesAsync`.

## Deploy

`render.yaml` is a Render free Docker web service. Required secrets, all `sync: false` except the ones with plain values in the Blueprint:

- `DATABASE_URL` — Neon **direct** URL, no `-pooler`
- `CMR_APP_PASSWORD` — password for role `cmr_app`, created on boot
- `JWT_KEY` — 32+ random characters
- `SEED_DEMO` — `true` only for the first boot
- `ASPNETCORE_ENVIRONMENT=Production`

Health: `GET /api/health`. The app reads `PORT`.

## Do not

- Put rates, amounts, or PINs in a staff response, even as null.
- Trust the client for `tenant_id` or role.
- Block a document number only because the prefix looks short or long.
- Copy real challan, CDO, CMRA, truck, or RST values from mill photos into seed data or tests.
- Put `#[.{fingerprint}]` back into `wwwroot/index.html`. The host publish copies that file as-is, so phones request a script that does not exist and stay on the boot screen. The script tags must be `_framework/blazor.webassembly.js` and `_framework/dotnet.js`. .NET 10 does not emit `blazor.boot.json`.
- Add an OTP flow, a paid host, a domain, or an SMS account until Nihar asks. DLT is still pending.

## Suggested next change

One of: MSG91 behind `IOtpSender` for new devices only; or a confirm screen that reads PDY/CDO/CMRA out of an uploaded photo. Keep the human confirm step. Add tests before changing `LotCheck`.
