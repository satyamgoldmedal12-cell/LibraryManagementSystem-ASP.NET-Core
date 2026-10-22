# Module 4 — Member Management

The MVC application now supports add/edit, reversible delete/restore, staff profiles, private self profiles, member IDs, email, mobile, address, membership date, expiry date and status.

## Membership and profile rules

- Admin and Librarian manage member records. New forms suggest a unique LIB identifier; database constraints enforce unique IDs and email addresses. The numeric record key is also visible on the profile.
- Member profiles contain contact/address details, membership dates, a borrowing summary, finalized unpaid fines, recorded fine payments and paged loan history. Members can view only their own profile, resolved from their signed member_id claim. Staff can view any profile, including archived members.
- Status is Deleted for archived records, Inactive when administratively inactive, Expired when active but past expiry, and Active otherwise. Calendar rules use UTC dates.
- Membership dates cannot be in the future; expiry cannot precede a recorded membership date. New memberships cannot already be expired. New staff forms and public registration default to the current date.
- Older records have no reliable membership creation date in the original schema. The migration leaves their JoinedOn NULL and the profile says Not recorded. Staff may record the date later; unknown dates remain editable, and leaving a known date blank does not erase it.
- Mobile and address are optional, with phone validation and length limits. Razor encodes all displayed details and addresses retain line breaks.
- A linked login email cannot be changed through membership editing. Such changes need a separate verified account-email workflow; membership editing does not silently change login/recovery credentials.

## Delete, restore and account access

Delete is a confirmed staff POST with antiforgery and revision checks. Outstanding books prevent deletion. The SQL transaction archives the member, marks membership inactive, changes linked Identity stamps and revokes linked sessions while retaining all records. New sign-in and SQL session creation/validation reject archived memberships. Checkout and member deletion use the same member lock so concurrent operations cannot create a loan for a deleted member.

Restore retains dates, contact data and history, restores the member as inactive, and requires a fresh sign-in. It never clears an administrator-imposed account lockout. Staff can then activate or renew the membership separately. Inactive and expired members can sign in to inspect their own records, while borrowing is blocked. Deletion preserves unpaid fines; it does not forgive debt or remove payment records.

The directory excludes deleted members by default. Include deleted enables recovery. Status filters distinguish active, inactive and expired records; search covers name, library ID, email, mobile and address. Lending lookup returns active, nonexpired members only. The dashboard member total includes inactive/expired records and excludes deleted records.

## Onion Architecture

Domain owns member fields and status. Application owns IMemberManagementService, validation, profile composition and persistence contracts. Infrastructure implements parameterized Dapper operations and Identity/session adapters. MVC owns binding, authorization, ModelState, redirects and Razor. No HTTP, Identity or SQL types were added to Domain or Application.

## Database setup

Apply Database/007_MemberManagement.sql after scripts 001–006; Setup-Local.ps1 includes it. It adds nullable Address and JoinedOn plus IsArchived, preserving legacy dates. It creates Members_Statistics, Members_Delete and Members_Restore, and updates listing/saving, checkout, sessions and dashboard procedures. The schema has nine tables and 30 procedures. Rerunning preserves records and does not invent missing dates.

```powershell
sqlcmd -S '(localdb)\MSSQLLocalDB' -d LibraryManagement -E -I -b -i Database\007_MemberManagement.sql
```

Tests use only the isolated database ending in _Test. They cover dates, normalization, XSS encoding, privacy, staff permissions, CSRF, duplicate identifiers/emails, revision conflicts, delete/restore, linked sessions, disabled accounts, profile pagination/payments, legacy-date preservation and concurrent checkout/deletion.
