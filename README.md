# CMR register — v1.1.0

A small web app for custom milling of rice (CMR) in Madhya Pradesh. The pilot mill is **Shri Krishna Rice & Poha Industries, Ujjain**. It keeps the paddy challan, the CMR deposit order, the acceptance note, and the kaanta (weighbridge) slip on one lot, with bag counts and an owner-only money book.

OTP login is **not** in this version. Login is a mobile number or username plus a PIN.

## Run it locally

Docker Compose (Postgres and the app):

```bash
docker compose up --build
```

Open http://localhost:8080

Without Docker, on a machine that already has .NET 10 and PostgreSQL 16:

```bash
sudo -u postgres psql -f scripts/prepare-db.sql
dotnet run --project src/Cmr.Api
```

The second command uses the Development settings, listens on http://localhost:5000 (or the port dotnet prints), and loads the demo mill because `Seed:Demo` is true in `appsettings.Development.json`.

### Demo logins

These exist only after demo seed. They are not real people.

| Who | Username | Mobile | PIN |
|---|---|---|---|
| Owner | `owner` | `9800000001` | `246810` |
| Staff (munshi) | `staff` | `9800000002` | `1357` |

Change both PINs in Settings before any real paddy is entered. Set `SEED_DEMO=false` after the first boot so startup does not try to seed again (it will not duplicate a mill that is already there).

## What was built

- Home, PIN login, a staff home with large Hindi buttons, and an owner dashboard.
- Lot list and lot detail: paddy in, rice due at the lot's out-turn (default 67%, 68% is one tap), rice accepted, and what is still pending.
- Paddy challan (`PDY…`), CMR deposit order (`CDO…`), acceptance (`CMRA…`), and kaanta parchi (RST, truck, gross, tare, net). The challan is optional until you want the lot to turn green. An acceptance can be saved only after the deposit order. The kaanta can be marked missing; a missing slip keeps the lot incomplete. The acceptance's toul parchi number is the kaanta RST.
- New and old jute bag counts on the challan and the acceptance, plus a bag ledger.
- By-product sales (husk, bran, kanni, cattle-feed broken rice, rejection, misc). Staff enter the item, quantity, buyer, and truck. Only the owner sees and enters rate, amount, and payment received.
- Owner-only expenses and incoming money.
- Document vault with search, CSV and PDF export of the lot register, user settings, and an audit log.
- Hindi first, with an English toggle. Layout works on a phone and on a wide screen.
- If a save fails because the phone is offline, the JSON entry is kept in a small queue in the browser and sent again on the next open. Photos still need a connection.

A lot is **green** only when the challan, deposit order, acceptance, and a kaanta with an RST and a file are all stored, and no cross-check is still open. Lot number, centre, truck, the acceptance date against the rice-out kaanta (and an acceptance dated before its deposit order), and the RST link are compared. A mismatch is flagged for the owner, who can mark it reviewed with a note. Prefix and length of `PDY` / `CDO` / `CMRA` only warn; a duplicate number is rejected.

## Architecture

```
Browser (Blazor WebAssembly PWA)
        │  HttpOnly cookies, no secrets in the client
        ▼
ASP.NET Core API  ── tenant id comes only from the login token
        │
        ▼
PostgreSQL 16
  EF Core global filters
  + row level security for role cmr_app
```

- **API** `src/Cmr.Api` — endpoints, PIN login, files on disk, audit.
- **Web** `src/Cmr.Web` — Blazor WebAssembly, served by the API.
- **Rules** `src/Cmr.Domain` — out-turn, completeness, bag maths, PIN/phone parsing. No database.
- **Contracts** `src/Cmr.Contracts` — shared request and response types, and `AppVersion`.

Two database roles:

- `cmr_owner` (or the Neon owner) runs migrations and demo seed. It is a member of `cmr_migrator`, which the row-level policy allows through.
- `cmr_app` is the role the API uses. It is not a superuser and cannot bypass row level security. It cannot update or delete the audit log.

Staff money is a separate set of tables (`by_product_prices`, `expenses`, `receipts`). Staff routes never query them. Those routes return **404** for staff, not an empty money object.

### Deviations from the research brief

- **PIN only.** Nihar deferred OTP. `IOtpSender` and `ILoginFactor` are registered so a later SMS provider can sit in front of `ISessionIssuer` without a new session design. There is no OTP screen.
- **Files on local disk**, not Cloudflare R2. Keys still look like `tenants/{tenantId}/…`.
- **Offline** is a browser queue for JSON saves, plus the PWA shell cache in a published build. Photo upload and a full IndexedDB outbox are next.
- **PDF export** is a small Latin-text PDF. QuestPDF was skipped because current builds pull native libraries with high-severity advisories.
- **Photos** have EXIF, GPS, and text chunks removed. They are not re-encoded pixel by pixel: ImageSharp 3.2 requires a paid license for release builds, and older ImageSharp releases have high-severity advisories.
- A few optional fields that appear on the real MP printouts (agreement number, printed lot number, paddy DO number, godown, grade-A quantity, CMR type) are stored so the form can follow the paper. They are not required.
- The buyer name sits on the sale the staff member enters, which matches the founder's rule rather than the earlier draft that hid the buyer.

## Tests

```bash
dotnet test tests/Cmr.Tests/Cmr.Tests.csproj
dotnet test tests/Cmr.E2E/Cmr.E2E.csproj
```

The first project covers the domain rules and the API: role and tenant isolation, PIN lockout, duplicate numbers, acceptance-without-DO, send-back, and a direct SQL check that row-level security hides the other mill. The second is Playwright: owner dashboard, lot detail, staff home, paddy entry, and a staff call to `/api/expenses` that must be 404.

GitHub Actions (`.github/workflows/ci.yml`) runs both, and fails the build if `dotnet list package --vulnerable` reports a High or Critical advisory.

## Deploy on Render + Neon

No account is created by this repo. After merge, Nihar creates the database and the service.

1. In Neon, create a free Postgres database. Copy the **direct** connection string (the host must **not** contain `-pooler`). Row-level security sets a session variable, which Neon's transaction pooler drops between statements.
2. In Render, create a Blueprint from `render.yaml` (or a Docker web service pointing at this repo). Plan: free. Region: Singapore is the closest Render region to Madhya Pradesh; pick a Neon region in the same part of the world if Neon offers one.
3. Set these environment variables. Do not commit the values.

| Variable | Required | What to put |
|---|---|---|
| `DATABASE_URL` | yes | Neon direct connection string |
| `CMR_APP_PASSWORD` | yes | A long random password. Startup creates the restricted `cmr_app` role with it. |
| `JWT_KEY` | yes | A random secret, at least 32 characters. Signs the login cookie. |
| `SEED_DEMO` | first boot | `true` once, so the demo mill and the two demo users are created. Then set it back to `false`. |
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` (the Blueprint sets this) |
| `STORAGE_ROOT` | no | Blueprint uses `/tmp/cmr-files`. The free disk is wiped on restart; treat uploads as temporary until object storage is added. |

`PORT` is set by Render. The app listens on it. The health check is `GET /api/health`.

Migrations run on startup under a Postgres advisory lock, so a restart will not apply them twice. Demo seed runs only when `SEED_DEMO=true` and the database has no mill yet.

There is no public demo URL from this build. Render's free URL appears after you create the service.

## Known gaps and next steps

- Real OTP SMS after DLT registration (MSG91 or similar), in front of the existing session issuer.
- OCR to suggest PDY / CDO / CMRA from a photo, always with a confirm screen.
- Import of previous seasons from a spreadsheet.
- Android packaging (install the PWA first; a store wrapper later).
- Object storage (R2 or similar) and encrypted backups. Render's free disk does not keep files.
- Full offline photo queue.
- Penalty, security-deposit, and claim figures, still owner-only, once this season's policy is confirmed.
- Hosting in India if a paid region is worth it later. Nothing here is deployed to paid hosting.
