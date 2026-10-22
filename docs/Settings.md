# Module 10 — Settings

1. Open the existing MVC solution in Visual Studio. Apply Database/012_Settings.sql after scripts 001–011 to the selected library database. Setup-Local.ps1 now includes it for new installations.
2. Sign in as an Admin and open **Library settings** in the Administration sidebar, or /Settings.
3. Enter the library name (required), optional address, contact email and phone. The saved name appears in the sidebar, including sign-in pages. Razor encodes the displayed text.
4. Set maximum active books per member (1–100), suggested issue duration (1–90 days), daily overdue fine (₹0–₹1,000, at most two decimal places), and membership duration (1–120 calendar months). Save settings; no application restart is needed.

## Policy behavior

- Checkout assessment and the final SQL transaction both use the persisted borrowing limit. SQL overrides caller-supplied defaults, preventing a stale page or direct repository call from bypassing the limit. Lowering the limit retains active loans and blocks further issues until the member falls below the new limit.
- The issue form suggests today plus the configured days, capped at membership expiry. Staff may choose another valid date within the existing 90-day maximum. Existing due dates remain recorded as issued.
- New checkouts snapshot the configured daily fine. Previously issued loans keep their rate, even when returned later. Zero disables fines for new checkouts.
- Public registration reads the configured membership duration inside the same SQL transaction that creates the member and Identity account. It uses calendar months, preserving month-end semantics. Posted expiry/role values cannot override registration policy.
- The staff Add Member form defaults expiry to today plus the configured months. Staff can edit the expiry for an individual membership. Existing memberships and their renewal dates remain explicit.

## Onion boundaries

MVC SettingsController depends on ILibrarySettingsService. Application validates/normalizes the DTO and calls ILibrarySettingsRepository. Infrastructure uses parameterized Dapper stored procedures Settings_Get and Settings_Save. Domain/Application do not reference ASP.NET Core, Dapper, or SQL Server packages.

The existing singleton library.FinePolicy now holds all settings. Its legacy name is retained to preserve existing data and the earlier fine-settings route. There is one daily-rate value and one revision shared by both forms. A successful save atomically updates settings, revision, modifier metadata and an audit event. Stale forms show a reload message instead of overwriting another Admin's save. Database constraints enforce policy bounds. The existing fine-only route remains compatible and updates the same rate/revision.

Until an Admin saves settings, nullable SQL rate/limit values preserve Lending:DailyFine and Lending:MaxActiveLoans from existing server configuration. Other initial defaults are Library Desk, 14 issue days and 12 membership months. Migration does not modify existing member/loan rows.

Both controller authorization and SQL writes require an active Admin. Unsafe MVC requests require antiforgery tokens; numeric, length, contact and precision validation runs server-side. Settings are not posted as JSON to a separate API. The branding view component uses the application interface and falls back to Library Desk if the database is unavailable so an error page can still render.

Tests use only a dedicated database whose name ends in _Test, restoring singleton settings after policy-change scenarios.
