# Changelog

## 1.1.1 — 2026-11-14

- Second boot on Neon no longer runs `ALTER ROLE cmr_app`. Neon’s `neondb_owner` is not a superuser and that statement returns SQLSTATE 42501, which stopped the process. If the role already exists, startup logs a warning and continues. Creating a missing role still works for a non-superuser owner.

## 1.1.0 — 2026-11-14

First pilot build of the CMR register for Shri Krishna Rice & Poha Industries, Ujjain.

- PIN login with a mobile number or username, hashed PIN, and lockout after repeated failures. OTP is a later phase; the login and session code leaves a place for it.
- Lots, paddy challan (PDY), CMR deposit order (CDO), acceptance (CMRA), and kaanta parchi, including new and old jute bags.
- A lot is green only when every paper is present. Mismatches of lot, centre, date, truck, or RST are flagged for the owner.
- Owner and staff roles. Staff never receive money from the API. Staff entries lock on save; the owner can send one back with a note.
- By-product sales, an owner-only expense and incoming-money tracker, bag ledger, document vault, and CSV/PDF lot register.
- Multi-tenant schema with Postgres row-level security.
- Hindi-first screens with an English toggle, for a phone or the mill computer.
- Render Blueprint for the free tier with a Neon Postgres database. Demo data loads only when `SEED_DEMO=true`.
