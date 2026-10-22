# Module 5 — Book Issue Management

Open **Circulation → Issue a book** after signing in as Admin or Librarian. Book details and active member profiles also provide an Issue shortcut.

1. Search for a book by title, author, publisher, category or ISBN, then choose a result. Results show available quantity; zero-stock titles cannot be selected through the lookup.
2. Search for an active member by name, member ID, email, mobile or address and choose a result.
3. Select the due date. Live checks show stock, the member's active issue count, membership expiry and any blocking reason. Use **Check availability** to refresh.
4. Confirm checkout. A successful POST redirects to the permanent issue receipt. SQL Server records the actual UTC issue timestamp; it cannot be supplied or backdated by the browser.
5. Open **Issue history** to search and filter active, overdue or returned loans. History is paged and retained after return. A member can view only their own receipts through My borrowing.

## Rules and transactions

The due date must be tomorrow through 90 days from today and cannot exceed membership expiry. Inactive, deleted or expired members, archived books, unavailable stock, duplicate active member/book pairs, overdue loans and the configured borrowing limit block issuance. The fine rate comes from persisted Admin fine settings, falling back to server configuration until settings are first saved, and is snapshotted on the loan. Returned unpaid fines currently do not block checkout.

Availability is advisory. The final stored procedure checks all rules again in one transaction, locks the member then the book, inserts the loan and audit event, and commits. Stock is derived from total copies minus active loans, avoiding a second counter that could drift. A filtered unique index provides an additional duplicate-active-issue guard. Repeated or concurrent requests cannot issue the same title twice to a member or oversell the last copy. A returned title may be issued again; both loans remain in history.

## Onion Architecture implementation

- MVC: LoansController, IssueBookViewModel, Issue/Receipt/History Razor pages and book-issue.js.
- Application: IBookIssueService, BookIssueService and framework-independent assessment/receipt contracts. The existing LibraryService validates issue commands.
- Infrastructure: Dapper repository methods call parameterized Loans_IssueContext, Loans_Receipt and transactional Loans_Issue procedures.
- Domain: existing Loan, lending rules, roles and business exceptions.

No separate Web API project is introduced. AJAX uses protected MVC actions. Server authorization, global antiforgery validation, DTO binding and Razor/jQuery text encoding apply to the entire flow.

## Upgrade and verification

Apply Database/008_BookIssueManagement.sql after scripts 001–007 to an existing database. Setup-Local.ps1 includes it for new or upgraded installations. It adds two read procedures and refreshes transactional issuance; existing books, members and loans are retained.

Core tests cover eligibility and date boundaries. Isolated SQL/MVC tests cover the form, CSRF, receipt privacy, spoofed dates/policy, duplicate and stale-stock rejection, concurrent duplicates and persistent history. Existing SQL tests also exercise concurrent last-copy issuance. Integration fixtures are restricted to a database ending in _Test.
