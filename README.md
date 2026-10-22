# Library Desk — ASP.NET Core MVC Library Management

An MVC application targeting .NET 10 with Razor views, SQL Server stored procedures, Dapper, Bootstrap 5, jQuery/AJAX, ASP.NET Core Identity, dependency injection, repositories, a service layer, and Serilog. There is one web project; AJAX lookups are actions on its MVC controllers.

Module 1 now includes dedicated role login portals, forgot/reset password, password changes, logout and server-side session management. See [Authentication module setup and security details](docs/Authentication.md), including SMTP configuration and local email pickup.

The staff overview now includes all seven requested inventory/member/payment cards plus stock composition and six-month issue/return charts. See [Dashboard definitions and setup](docs/Dashboard.md).

## Run locally

Prerequisites: .NET 10 SDK, SQL Server or LocalDB, `sqlcmd`, and an HTTPS development certificate. Open `LibraryManagement.slnx` in Visual Studio, or use PowerShell in this folder.

```powershell
sqllocaldb start MSSQLLocalDB
dotnet dev-certs https --trust
.\scripts\Setup-Local.ps1
dotnet run --project src/LibraryManagement.Web/LibraryManagement.Web.csproj --launch-profile https
```

Browse https://localhost:7115. The setup script applies the database scripts and prompts privately for your first administrator password. No default account or password is shipped. After schema 13, the setup script skips previously applied migrations; leave the administrator email empty if already provisioned. `TrustServerCertificate=True` is confined to local development. The development connection uses Windows authentication, so there are no SQL credentials in source.

For a named SQL Server, run `Setup-Local.ps1 -Server '.\SQLEXPRESS'` and set your connection with user-secrets:

```powershell
dotnet user-secrets set 'ConnectionStrings:LibraryDatabase' 'Server=.\SQLEXPRESS;Database=LibraryManagement;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
```

The normal web process never creates schema or administrator accounts at startup. Configuration may also come from `ConnectionStrings__LibraryDatabase`. Production `appsettings.json` intentionally has an empty connection string: configure the deployment secret rather than committing a password.

## Database ownership

The application includes the SQL design, not just examples:

| Script | Purpose |
| --- | --- |
| `Database/000_CreateDatabase.sql` | Create `LibraryManagement` if missing; run against master |
| `Database/001_Tables.sql` | Create seven base tables, constraints, indexes, five starter categories, and `library_app` database role |
| `Database/002_Procedures.sql` | Create or alter 19 base stored procedures used by Dapper |
| `Database/003_MemberRegistration.sql` | Add public registration with atomic membership/account creation |
| `Database/004_AuthenticationSessions.sql` | Add the authentication session table and five session procedures |
| `Database/005_Dashboard.sql` | Update the dashboard metrics/history query and add reporting indexes |
| `Database/006_BookManagement.sql` | Add atomic cover storage, richer book queries and reversible delete |
| `Database/007_MemberManagement.sql` | Add member dates/address, profiles, archive/restore and linked session guards |
| `Database/008_BookIssueManagement.sql` | Add live issue context and receipts; strengthen transactional checkout |
| `Database/009_ReturnsAndFines.sql` | Persist fine settings; return/payment receipts, history and collection reporting |
| `Database/010_Reports.sql` | Add seven report queries and catalog-date index |
| `Database/011_UserManagement.sql` | Add account activation/revision/audit metadata, safe editing/paging and user activity |
| `Database/012_Settings.sql` | Add central library information, lending/default durations and shared fine configuration |
| `Database/013_NormalizationAndAudit.sql` | Normalize author/publisher references; add consistent creation/modification/deletion audit fields and indexes |

In SSMS run `000` first, select the `LibraryManagement` database, then run `001` through `013`. For an older installation, apply its missing numbered migrations through `013` once. After schema 13, use the version-aware setup script and never replay older procedures over the upgraded schema. With sqlcmd always use `-b -I` to stop on errors and enable quoted identifiers. Initial table creation is transactional and rerunnable, but these scripts are not a general upgrade/migration engine. Future schema changes should use reviewed numbered migration scripts.

Tables: `Users`, `Categories`, `Authors`, `Publishers`, `Books`, `BookCovers`, `Members`, `Loans`, `Payments`, `AuditEvents`, `UserSessions`, `FinePolicy`, all under the `library` schema. Indexes support ISBN uniqueness, member uniqueness, stock counts, active loans and due dates. All runtime database operations, including Identity persistence, use parameterized stored procedures through Dapper.

## Included workflows

- Dashboard: Total Books, Available Books, Issued Books, Returned Books, Overdue Books, Total Members and Total Fine Collected, with Bootstrap cards and locally served charts.
- Books: paged search by title/author/publisher/category/ISBN, details, create/edit, JPEG/PNG cover upload/preview/removal, quantities, computed availability status, and reversible delete/restore. See [Book management module](docs/BookManagement.md).
- Categories: list and create. Existing categories remain stable to preserve classification history.
- Members: searchable/status-filtered directory, add/edit, mobile/address/dates, profiles and private My profile, paged history and fine summaries, safe delete/restore, expiry and deactivation. See [Member Management](docs/MemberManagement.md).
- Book issue: live stock/member eligibility, server-recorded issue dates, due-date checks, transactional duplicate/stock guards, issue receipts and paged issue history. See [Book Issue Management](docs/BookIssueManagement.md).
- Returns and fines: return confirmation/receipts, late days, final fine, immediate stock availability and return history; Admin-configured daily fine, pending/accruing/paid ledger, full payment receipts and filtered fine collection report. See [Returns and Fines](docs/ReturnsAndFines.md).
- Reports: seven report types with date/member/book/status filters and matching Excel/PDF exports. See [Reports](docs/Reports.md).
- User management: paged/searchable account directory, create/edit, assign role, activation/deactivation, password reset and user activity logs. See [User Management](docs/UserManagement.md).
- Accounts: public member registration, login/logout, password change, account lockout and login rate limiting.
- Member access: catalog and the signed-in member's own loans only. Staff routes are protected on the server.
- Activity: transactional database audit history for catalog/circulation writes. Account security events go to Serilog.

Admin can manage accounts and view audit history. Admin and Librarian manage collection, members, circulation and payments. Member is linked to one membership, with matching email at creation. These three fixed roles are issued as signed claims by `LibraryClaimsFactory`; the application does not need dynamic role-management tables. Identity's managers, password hashing, cookies, security stamps, token providers and lockout are retained, with a custom Dapper user store instead of EF Core.

Public registration is available at `/Account/Register`, linked from the sign-in screen. It accepts full name, email, password and confirmation. It creates an active one-year membership with an automatically generated member number and an Identity account in a single SQL transaction. Both the service and SQL procedure fix the role to Member; role, membership ID, expiry and confirmation cannot be chosen by the public form. Existing memberships cannot be claimed by typing their email: the person must contact an administrator to link an account. Admin and Librarian accounts are created only by administrators.

Registration follows the same Identity password rules, validates antiforgery tokens, checks duplicate emails and is limited to five POST requests per IP every ten minutes. Successful signup redirects to sign-in. Email ownership is not verified: EmailConfirmed remains false and email confirmation is not required. Password recovery uses configurable SMTP in production and private MIME pickup in Development. MFA and external sign-in providers are not configured. Administrators can also reset passwords.

## Lending rules

`Lending:MaxActiveLoans` defaults to 5 and `Lending:DailyFine` to INR 5.00. Admin can persist the borrowing limit and daily rate in Library settings; until the first save, the server values remain the fallback. A loan snapshots its rate at checkout, so later configuration changes do not alter existing loans. Date-only business rules use **UTC calendar days** consistently in C#, SQL and the views. A due date must be after today, no more than 90 days away, and no later than membership expiry. Overdue active loans block further borrowing. Unpaid returned fines are shown but do not block borrowing.

Stock is calculated from total copies minus active loans. SQL locks the membership and book in a consistent order during issue/return. This serializes decisions for the same member or book and prevents two concurrent requests from taking the last copy. A filtered unique index also prevents duplicate active loans for the same member/book. Returns and payments reject repeated submissions. Fine payments settle the entire finalized fine; partial payments, waivers, refunds and payment gateways are outside this version.

Book/member edits use optimistic revision checks. Books with active loans cannot be archived or have their total copies reduced below outstanding loans. Records are retained for borrowing history. Inventory is maintained as counts per title; individual barcode/copy tracking, reservations, branches, renewal scheduling, reminder email/SMS are future extensions.

## Architecture and WebForms transition

The solution uses Onion Architecture with four production assemblies:

| Project | Responsibility | Project dependencies |
| --- | --- | --- |
| `src/LibraryManagement.Domain` | Entities, roles, domain errors and lending rules | None |
| `src/LibraryManagement.Application` | Use cases, queries, DTOs and persistence/identity/session interfaces | Domain |
| `src/LibraryManagement.Infrastructure` | Dapper/SQL Server, Identity, cookies, email and adapter registration | Application |
| `src/LibraryManagement.Web` | MVC controllers, Razor, static assets, HTTP authorization and startup | Application; Infrastructure only in `Program.cs` |

Controllers call application interfaces. Application services depend on ports they own; Infrastructure implements those ports. Domain and Application reference no ASP.NET Core framework or NuGet packages. Identity types, SQL connections and mail transport never cross application contracts. Account pages receive safe `UserSummary` projections.

`Program.cs` is the composition root: its Infrastructure reference registers adapters and invokes explicit administrator bootstrap. Middleware, filters, routes and user-secrets belong to LibraryManagement.Web. Database scripts and setup tooling remain at solution level. This is one MVC application with three class libraries.

See [Architecture guide](docs/Architecture.md) for the dependency diagram, feature placement and extension workflow.

Coming from WebForms: `Page_Load` becomes a GET action; a button event becomes a POST action; `ModelState` replaces ad-hoc form validation; Razor replaces server controls; an injected service replaces code-behind business logic. There is no ViewState. Successful writes redirect to a GET, preventing accidental browser refresh resubmission. Razor encodes data by default. SQL stays out of controllers and views.

## Tests

```powershell
# Core tests need neither SQL Server nor the MVC host.
dotnet test tests/LibraryManagement.Core.Tests/LibraryManagement.Core.Tests.csproj
# Complete suite (configure the isolated SQL database as described below).
dotnet test LibraryManagement.slnx
```

Independent core tests reference only Domain and Application. They cover ISBN checksums, due-date limits, lending policy, expired new memberships, registration and account/session boundaries. MVC tests also cover member-loan scoping. Architecture tests enforce inward assembly dependencies, framework-free application contracts and controllers without repository or Infrastructure dependencies. SQL integration tests skip unless `LIBRARY_TEST_CONNECTION` points to an initialized dedicated database ending in `_Test`. Apply scripts `001` through `012` to that database using a deployment account, then run:

```powershell
$env:LIBRARY_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=LibraryManagement_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test LibraryManagement.slnx
Remove-Item Env:LIBRARY_TEST_CONNECTION
```

Registration tests additionally cover form validation, hashed passwords and login, privilege injection, duplicate signup, existing membership protection, transaction rollback, CSRF rejection and signup throttling.

Integration tests exercise real issue/return, concurrent last-copy issuance, revision conflicts, archive rules, late fine calculation, duplicate payment rejection and Identity concurrency. They create uniquely named test records and never connect to a database without the `_Test` suffix. Use a disposable test database; test data is intentionally retained for diagnosis.

## Security and deployment

Global authorization requires login. Mutation actions are POST-only and globally validate antiforgery tokens; AJAX lookup actions are authenticated GETs. Member identifiers for loan visibility are taken from signed claims, with missing links denied. Login accepts only local return URLs. Secure HttpOnly cookies require HTTPS even in development. Passwords require 12 characters with upper/lowercase, digit and symbol; lockout is five failed attempts for 15 minutes, with per-IP login throttling. Account reset/disable changes security stamps, invalidating existing sessions on their next authenticated request. Authentication cookies are backed by SQL session records with a 30-minute idle limit and eight-hour maximum. Users can review and revoke their own sessions.

Serilog writes console output and rolling `logs/library-YYYYMMDD.log`, retained for 14 files. Forms, passwords, connection strings and query strings are not deliberately logged. Unexpected failures go to logs and a generic error page with a reference ID. Restrict log access and configure retention for your organization's policy.

For deployment, use a trusted SQL certificate (`TrustServerCertificate=False`), a dedicated runtime SQL/Windows account belonging only to the `library_app` role, and a separate schema deployment account. For example, after creating a database user: `ALTER ROLE library_app ADD MEMBER [your_runtime_database_user];`. Do not grant runtime users `db_owner` or direct table write permissions. Restrict deployment credentials and review execute permissions if adding new administrative procedures later.

Configure the real domain through `AllowedHosts`, HTTPS at IIS/reverse proxy, persistent protected ASP.NET Data Protection keys shared across replicas, and persistent log storage. Configure forwarded headers for explicitly trusted proxies before HTTPS redirection/rate limiting if hosted behind a proxy; do not trust arbitrary forwarded IPs. The current defaults target direct local IIS/Kestrel use. Use connection pooling and paging as supplied, then measure with production-like workloads before claiming capacity. Run NuGet vulnerability auditing and the tests in a network-enabled CI environment before deployment.

References: [Microsoft: custom Identity storage providers](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-custom-storage-providers?view=aspnetcore-10.0), [MVC antiforgery protection](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

Module 10: Admins can open **Library settings** from the sidebar to save library information, borrowing limits, the suggested issue duration, the daily fine and membership duration. Changes persist in SQL Server and take effect without restarting. See [Settings setup and behavior](docs/Settings.md).

Security follow-up: secure authentication/antiforgery/notification cookies, global CSRF, role/ownership checks, parameterized SQL, encoded Razor, bounded cover decoding and database-independent sanitized exceptions are implemented and tested. See [security controls and verification](docs/Security.md).

Database design: [normalized SQL Server tables, audit metadata and atomic transactions](docs/Database.md). After schema 13, do not manually replay older SQL scripts over the upgraded schema.
