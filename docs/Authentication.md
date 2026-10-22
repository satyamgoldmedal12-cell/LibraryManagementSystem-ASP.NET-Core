# Module 1 — Authentication & Authorization

This module uses ASP.NET Core Identity inside the MVC application, with a custom Dapper user store, Razor forms, and SQL Server stored procedures. It does not introduce a separate API.

## Entry points and permissions

| Route | Purpose | Access |
| --- | --- | --- |
| /Account/Login | Shared sign-in for any of the three roles | Anonymous |
| /Account/AdminLogin | Admin sign-in portal | Anonymous; actual Admin role required |
| /Account/LibrarianLogin | Librarian sign-in portal | Anonymous; actual Librarian role required |
| /Account/MemberLogin | Member sign-in portal | Anonymous; actual Member role required |
| /Account/Logout | Sign out and revoke the current server session | Authenticated POST + antiforgery |
| /Account/ForgotPassword | Request an emailed reset link | Anonymous; throttled POST |
| /Account/ResetPassword | Choose a password using an emailed token | Anonymous; throttled POST |
| /Account/ChangePassword | Change a password with the current password | Authenticated POST + antiforgery |
| /Account/Sessions | List your currently active sessions | Authenticated |
| /Account/RevokeSession | Revoke one of your sessions | Authenticated POST + antiforgery |
| /Account/RevokeOtherSessions | Keep the current session, revoke the rest | Authenticated POST + antiforgery |

The login portals share one implementation. A selected portal cannot grant a role: the submitted password is checked by Identity and the actual role stored in SQL must match. The shared login accepts any actual role. After login, Home sends Member users to their own borrowing screen; Admin and Librarian see the staff dashboard. Admin-only and staff-only MVC actions remain protected by server-side Authorize attributes, independently of navigation links.

Public registration can create only Member accounts. Administrators provision staff accounts and reset/disable accounts. A missing signed member link denies access to personal loan data. No default administrator/password is shipped.

## Install or upgrade

Apply Database/004_AuthenticationSessions.sql to your existing LibraryManagement database after scripts 001–003. The setup script now includes 004. It adds library.UserSessions and five Sessions_* stored procedures, bringing the schema to eight tables and 25 procedures.

The upgrade intentionally requires existing users to sign in again: old cookies have no server session record. It preserves existing users, password hashes, memberships, and business data.

Build/run from the project folder:

    dotnet restore LibraryManagement.slnx
    dotnet build LibraryManagement.slnx
    dotnet run --launch-profile https

## Password recovery

An eligible user receives a protected Identity reset token through email. The link is valid for 30 minutes; after a successful reset, the changed security stamp makes it unusable again. Invalid, expired, malformed, reused, or disabled-account links cannot reset a password. Successful recovery clears temporary password lockout and signs out old sessions. Permanently disabled accounts cannot recover access through this flow.

Forgot Password always redirects to the same generic confirmation page for existing, missing, or disabled accounts and for delivery failures. Reset URLs are never returned by that page or written to application logs. The sender's configured public HTTPS origin is used for links; the incoming Host header is not trusted. Account responses use no-store, no-referrer and noindex headers. Reset POSTs and Forgot Password POSTs require antiforgery tokens.

Identity still does not require email confirmation at registration. Recovery proves access to the destination inbox, but the module does not add a separate email-confirmation workflow.

### Local development

Development uses RecoveryEmail:DeliveryMethod = Pickup. Emails are written as MIME .eml files under App_Data/MailPickup in the running application's content root. Open the file locally in a mail client to follow the reset link. This folder is outside wwwroot, has no MVC download endpoint, is ignored by Git, and is excluded from compilation/publishing.

PasswordRecovery:PublicOrigin defaults to https://localhost:7115 in appsettings.Development.json. If testing on another HTTPS port, override PasswordRecovery__PublicOrigin to match the running instance. Existing accounts must be present in that instance's database before requesting recovery. The application never displays a development reset token publicly.

### Production SMTP

Production rejects pickup delivery and validates required configuration at startup. Set the public origin and SMTP settings with environment variables or your secret manager, not source files:

    PasswordRecovery__PublicOrigin=https://library.your-domain.example
    RecoveryEmail__DeliveryMethod=Smtp
    RecoveryEmail__FromAddress=library@your-domain.example
    RecoveryEmail__FromName=Library Desk
    RecoveryEmail__Host=smtp.your-provider.example
    RecoveryEmail__Port=587
    RecoveryEmail__SslOnConnect=false
    RecoveryEmail__UserName=your-smtp-user
    RecoveryEmail__Password=your-secret

Port 587 uses mandatory STARTTLS. For implicit TLS, use the provider's port (commonly 465) and SslOnConnect=true. Certificate validation is retained; there is no plaintext fallback or certificate bypass. An approved authenticated relay can omit both username and password. If one credential is configured, both are required. Set AllowedHosts to the real deployment domain as well.

No SMTP credentials were supplied during implementation, so external email delivery has not been verified. Tests use a capture sender, and separately verify real development MIME pickup and the production pickup restriction. Delivery failures produce a generic browser response and a server log containing the error type, without the recipient, token, SMTP password, or reset URL. Monitor and resolve these events operationally.

## Password and session protection

Identity's salted PBKDF2 hasher is configured for 210,000 iterations and its versioned IdentityV3 format. Existing valid hashes are accepted and upgraded by Identity when verification requires rehashing. Passwords require at least 12 characters with upper/lowercase, a digit and a symbol; public forms cap passwords at 128 characters. Passwords are never stored in plaintext.

Five failed password checks lock the account for 15 minutes. Login is limited to ten POSTs per IP per minute, registration to five per ten minutes, recovery to five per fifteen minutes, and token reset to ten per fifteen minutes. These in-process limiters are per application instance; production deployments with multiple instances should also enforce consistent limits at a trusted gateway. Forwarded client IPs require explicitly trusted proxy configuration.

Authentication uses encrypted, HttpOnly, Secure, SameSite=Lax Identity cookies. No password or bearer token is stored in browser local storage. Remember me persists the cookie when the browser closes; the same idle and absolute limits still apply. Defaults are:

    AuthenticationSessions:IdleMinutes = 30
    AuthenticationSessions:AbsoluteHours = 8

Each cookie carries an encrypted session ID. SQL validates ownership, revocation, idle activity and absolute expiry on authenticated requests. Sliding cookie renewal cannot extend the SQL absolute expiry. The browser session row records only the reported user agent and timestamps; device text is encoded by Razor.

Logout revokes the server record, so replaying the previous cookie fails. Users can revoke only sessions belonging to their signed-in user ID. Change Password rotates the current session and revokes the others. User password recovery, administrator password reset, and account disabling revoke prior sessions. Identity security stamps are checked on every authenticated request for prompt invalidation; this deliberately adds database reads in exchange for immediate security-state checks.

Data Protection uses the application identity LibraryManagement and the framework's key management. Keep its protected key ring persistent across restarts and shared securely across replicas; changing/losing keys invalidates cookies and outstanding reset links. Configure persistent encrypted key storage and access restrictions for the actual hosting identity before production deployment. SQL session data alone does not replace the key ring.

Expired/revoked session metadata is retained for operational review. Apply your own retention job to old session rows and logs. This implementation has not been load-tested or deployed behind a production proxy.

## Code map and verification

- Controllers/AccountController.cs — portal login, recovery, password changes, logout and session screens.
- src/LibraryManagement.Infrastructure/Identity/PasswordRecoveryService.cs — Identity token generation/validation and reset orchestration.
- src/LibraryManagement.Infrastructure/Email/PasswordResetEmailSender.cs — SMTP and Development-only MIME pickup.
- src/LibraryManagement.Infrastructure/Identity/LibraryCookieEvents.cs — session creation, validation and revocation, retaining Identity stamp checks.
- src/LibraryManagement.Infrastructure/Repositories/UserSessionRepository.cs — Dapper calls to Sessions_* procedures.
- Database/004_AuthenticationSessions.sql — session table and procedures.
- tests/LibraryManagement.Tests/AuthenticationIntegrationTests.cs — SQL-backed HTTP security flows.
- tests/LibraryManagement.Tests/RecoveryEmailTests.cs — actual MIME pickup and production restriction.

Use the dedicated LibraryManagement_Test database with scripts 001–005 applied:

    $env:LIBRARY_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=LibraryManagement_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
    dotnet test LibraryManagement.slnx

References: [Microsoft password recovery](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-10.0), [Microsoft cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [MailKit SMTP](https://mimekit.net/docs/html/T_MailKit_Net_Smtp_SmtpClient.htm).
