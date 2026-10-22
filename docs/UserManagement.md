# Module 9 — User Management

Only Admin can manage accounts or read User activity logs. Open User accounts from the sidebar. The directory supports name/email search, role and access filters, and pagination beyond the previous 200-account limit.

## Account operations

- Create User: provide name, email, initial password and Admin/Librarian/Member role. Identity validates and hashes passwords. Member accounts require a non-deleted membership with the same email and no other linked account.
- Edit / Assign Role: change name, email, role or membership link. A safe numeric revision rejects stale edits. Email changes update the sign-in name and clear email confirmation. A Member-to-staff change removes the account link while preserving the membership and borrowing history.
- Activate / Deactivate: review the target account and confirm access. Inactive accounts cannot sign in; activation also clears temporary lockout. The directory distinguishes inactive accounts from temporary failed-login lockout.
- Reset Password: Admin supplies a new password on the existing Identity-backed reset form. Resetting a password does not activate a deactivated account.
- Activity: search/select an acting or affected user and filter by action/date. Includes successful sign-in/out, user creation/edit/access/password operations and existing audited library writes. Unknown or unsuccessful sign-in attempts are not stored as user audit rows. Passwords, hashes, tokens and connection details are excluded from the activity page.

Every write requires an antiforgery token and Admin authorization. Account editing, access changes and password resets invalidate sessions. Name/email/role edits also rotate the security stamp so old role claims cannot survive. Password operations remain owned by ASP.NET Core Identity; custom SQL never hashes a password.

You cannot change your own role or access. SQL additionally serializes administrative access changes and prevents removal of the last active Admin. These guards apply even when persistence is called outside the MVC action. Temporary lockout is separate from administrative activation.

## Audit and consistency

User creation and managed account updates write their audit event in the same transaction as persistence. Managed updates also revoke SQL sessions in that transaction. Additional explicit session revocation is retained in existing Identity flows. Sign-in/out events are recorded through the Identity adapter. A user filter includes actions they performed and account changes affecting them. Existing legacy audit rows remain readable; missing historical events are not fabricated.

UserSummary and edit DTOs contain only safe display/input fields. Password hashes, Identity tokens, security stamps and concurrency stamps never enter Razor view models. SQL queries are parameterized. Account revision, created/modified metadata and indexes support paging, audit filtering and concurrent editing.

## Upgrade and verification

Apply Database/011_UserManagement.sql after 010. It adds activation/revision/audit columns and indexes, updates user/session procedures, and adds safe account summary, paging and activity procedures. Existing permanent lockouts are mapped to inactive accounts, preserving their blocked access. Existing account passwords, memberships, library loans and payments are retained. Setup-Local.ps1 includes the migration.

Core and isolated SQL/MVC tests cover account creation, editing, role/membership validation, stale forms, session invalidation, password reset while inactive, activation, last-Admin protection with rollback, directory pagination, audit fields and Admin-only access.
