# Modules 6 and 7 — Returns and Fines

## Return a book

1. Open Circulation, find the active loan and select Return. The issue receipt also links to this action.
2. Review the member, book, issue date, due date, estimated late days and fine.
3. Confirm receipt of the physical book. SQL Server records the return timestamp and final fine in one transaction.
4. The return receipt shows the final amount, receiving staff member and current available quantity. Return history is searchable, date-filtered and paged.

The return date is the actual server-recorded UTC timestamp. Browser-supplied return dates, late-day values and amounts are ignored. A loan can be returned once. The book becomes available immediately because availability is total copies minus loans without a return timestamp; there is no separate counter to drift. The transaction locks member, book and loan in a consistent order, and writes an audit event. Return history remains after book/member archival.

## Fine calculation

Late days = max(0, UTC return date − due date), using calendar dates. Fine = late days × the daily rate recorded at checkout. Returning on the due date or earlier costs zero. Outstanding overdue books display an accruing estimate using today's UTC date; return freezes the final fine. Example: due 20 October, returned 23 October, rate ₹7.25 → 3 days × ₹7.25 = ₹21.75.

## Configure fine per day

An Admin opens Fines & collections → Configure fine per day. Save a rate from ₹0 to ₹1,000 with at most two decimal places. Settings are persisted in SQL with revision checks and audit metadata. A concurrent settings change requires reloading the current settings before saving again. Librarians and Members cannot configure rates.

Until an Admin first saves settings, the application preserves the existing Lending:DailyFine server configuration (default ₹5). A saved database rate applies to subsequent checkouts across application processes without restart. The issue preview reads this rate, and transactional issuance reads it again at commit. Existing loans retain their checkout rate, including when returning them later. A zero rate produces no fines for future checkouts.

## Fine ledger and payment

Admin and Librarian can filter the fine ledger by All, Pending, Accruing or Paid, dates, member, book and search text. Date filters in this ledger apply to the due date. The cards show totals for all matching rows across pages and the selected status.

- Accruing: an overdue book still on loan; estimate changes until return.
- Pending: a returned loan with a positive final fine and no payment.
- Paid: the full finalized fine has one recorded payment.

Select Record payment for a pending fine, review the amount, then confirm only after receiving payment. SQL records the exact full fine, UTC payment date and receiving staff member. Client-supplied amount and payment date are ignored. Payment is transactional, audited and protected by a unique loan/payment constraint. Repeated or concurrent payment attempts cannot collect twice. An active loan or zero fine cannot be paid.

The payment receipt links to the return receipt. This is staff recording of a received payment; partial payments, waivers, refunds and online payment gateways are not provided.

## Fine collection report

Fines & collections → Fine collection report displays actual payment receipts and their total. From/To filters use payment dates in UTC, include both endpoints, and can be combined with member/book filters and text search. Totals include every matching page. Pending fines and accruing estimates are excluded from collections. Archived titles and memberships remain available in historical filters.

## Architecture and database upgrade

Controllers use ICirculationService. Application owns the use case, DTOs, validation and ICirculationRepository port. Infrastructure implements it through parameterized Dapper procedures. Existing LibraryService/LibraryRepository commands are retained for issue/return/payment compatibility. Domain FineRules expresses calendar-day calculations and rate constraints. SQL remains authoritative for the atomic return/payment calculation.

Apply Database/009_ReturnsAndFines.sql after 001–008. Setup-Local.ps1 includes this script. It adds the FinePolicy table, historical return index, read view, four procedures and updates the issue/return/payment procedures. It retains existing circulation and payment records and preserves the configured fallback rate.

All write forms use global antiforgery protection and role authorization. Inputs are bound to narrow contracts; amounts and timestamps are server controlled. Razor encodes record text, and search/lookup results use text rendering. Core tests cover calendar-day calculations, rate bounds and validation; isolated SQL/MVC tests verify real transactions, duplicate/concurrent return/payment guards, stock, finalization, receipt privacy, settings permissions and filtered report totals across pagination.
