# Security implementation and verification

The existing MVC application was audited against all ten requested controls. Authentication, role checks, form validation, parameterized repositories and safe image reconstruction were already implemented. This follow-up hardens auxiliary cookies, makes exception handling independent of database/Razor availability, preserves security headers on failures, and tests that submitted secrets stay out of handled-error logs.

| Requirement | Implementation | Verification |
| --- | --- | --- |
| Authentication | ASP.NET Core Identity, salted PBKDF2 hashes with 210,000 iterations, lockout, generic sign-in failures, SQL-backed idle/absolute sessions | Authentication/registration suites; equal passwords produce distinct hashes |
| Role-based authorization | Authenticated fallback policy; Admin-only users/settings/audit; staff-only mutations/reports; members scoped to their own records | Role portals, privilege injection, staff-route denial, profile/receipt ownership and session tests |
| Anti-forgery tokens | Global AutoValidateAntiforgeryToken filter; Razor form tokens; X-CSRF-TOKEN for AJAX | Missing/foreign-cookie tokens rejected; valid header token accepted; logout is POST-only |
| Input validation | DataAnnotations and Application validators; bounded fields, allowed roles/statuses, date/range/revision checks; SQL constraints repeat business invariants | Core/service tests, invalid login/signup/member/book/settings/report inputs and no-write checks |
| SQL injection prevention | Dapper parameters with fixed procedure names; no request-driven SQL text; caller identity comes from signed claims | Literal injection strings remain book/search data; SQL statements in search do not alter schema/row counts |
| XSS prevention | Razor encoding, safe text DOM construction, same-origin CSP, nosniff; no raw user HTML | Script/image payloads render encoded in catalog/details/editor and persisted values remain intact |
| Secure cookies | Authentication: Secure, HttpOnly, SameSite=Lax; antiforgery: Secure, HttpOnly, SameSite=Strict; encrypted TempData: Secure, HttpOnly, SameSite=Lax | Real Set-Cookie assertions; cookie/token binding; logout/revocation/replay/expiry checks |
| Global exception handling | Registered IExceptionHandler, safe standalone HTML or AJAX ProblemDetails, status/reference, no dependency on authentication/database/Razor during error rendering | Development/Production fault injection, malformed Accept, safe 413, no sensitive response text, CSP/no-store on errors |
| Logging | Serilog rolling files/request summaries; SQL activity audits; handled failures record reference/type/endpoint/application method without exception messages or request bodies/query strings/cookies | Captured Serilog sink verifies submitted password/reset-token markers are absent; branding failures also omit raw exception text |
| Secure file uploads | Staff authorization/CSRF; 3 MB request envelope and 2 MB file limit; JPEG/PNG extension and decoded format agreement; bounded dimensions/pixels; full decode and JPEG reconstruction; SQL storage | Unsupported/spoofed/truncated/oversized images rejected; trailing content discarded; upload/removal atomicity and cover privacy tests |

## Cookie and session behavior

The authentication cookie name and Data Protection application name are unchanged. The new explicit antiforgery and TempData cookie names ensure freshly emitted auxiliary cookies use the secure settings. Refresh any old form after an application restart before submitting it. Tokens are paired to the cookie and authenticated identity; a token copied from another browser session is invalid. HTTPS is required. Production enables HSTS; hosting must provide the correct HTTPS scheme to the application.

Private MVC responses were already marked no-store through LibraryController. SecurityHeadersMiddleware now applies headers immediately before sending the response, so an exception handler clearing an earlier response does not remove them. It also covers early authenticated failures and error statuses; public static assets keep their normal caching behavior.

## Error handling and diagnostic privacy

GlobalExceptionHandler is in Web/Middleware. Ordinary browser errors receive a standalone, encoded HTML response with a support reference. AJAX/JSON requests receive application/problem+json. The handler never prints exception messages, SQL, submitted passwords, reset tokens, cookies or stack traces to the user. Known oversized/malformed HTTP failures retain safe 413/400 responses.

Request logging wraps the exception handler, so handled failures produce an HTTP status summary without a raw exception attached. The handler emits structural diagnostics at Error/Warning level: reference, exception type, endpoint and first application method. The .NET 10 handled-exception diagnostic behavior avoids a second raw exception log for a handled failure. Branding's database fallback also logs only the exception type. Startup failures and failures after a response has started follow framework hosting behavior; this is not a general redaction layer for arbitrary future logger calls. Continue to keep secrets out of every log statement.

Home/Error remains available as a generic page; automatic failures are handled directly without re-executing an authentication/database-dependent MVC pipeline.

## Upload behavior

Only reconstructed JPEG bytes are stored/served. Original filenames are used only for an extension check; paths and filenames never select a server filesystem destination. EXIF/GPS metadata and trailing payloads are not preserved. A filename path is therefore not a path traversal write. The uploaded Content-Type is not trusted as proof of image type: the decoder verifies actual content. Other document formats and arbitrary downloadable attachments are not accepted by this cover upload flow.

## Run verification

1. Build LibraryManagement.slnx and run Core.Tests for independent validation/business-rule checks.
2. Initialize a separate SQL Server database with a name ending in _Test using Database scripts 001–013.
3. Set LIBRARY_TEST_CONNECTION to that test database, then run dotnet test LibraryManagement.slnx. SecurityPipelineTests need no database; SecurityDatabaseTests and existing HTTP/SQL suites use only the isolated database.
4. Publish src/LibraryManagement.Web/LibraryManagement.Web.csproj and verify private App_Data, logs, source, tests and database scripts are excluded from output.

No SQL migration or new NuGet dependency is needed for these security changes. Configuration-specific production email delivery, hosting/proxy/TLS setup and independent penetration/load testing remain deployment verification tasks.

References: [Microsoft antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [Microsoft error-handling guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).
