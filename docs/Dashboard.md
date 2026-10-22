# Library overview dashboard

The staff dashboard remains an ASP.NET Core MVC Razor view at Home/Index. It uses Bootstrap cards, Dapper, SQL Server, and locally served Chart.js 4.5.1. Admin and Librarian can open it; Member users are redirected to their own borrowing page. No public dashboard-data API was added.

## Metric definitions

| Card | Definition | Scope |
| --- | --- | --- |
| Total Books | Sum of TotalCopies for non-archived titles | Current physical inventory, not distinct titles |
| Available Books | Total Books minus outstanding loans for those titles | Current copies ready to borrow |
| Issued Books | Loans whose ReturnedAt is null, for active titles | Current outstanding loans; includes overdue loans |
| Returned Books | Count of loans whose ReturnedAt is not null | All-time completed return transactions, including archived titles; repeated borrowing of one copy counts again |
| Overdue Books | Outstanding loans with DueDate before today in UTC | Subset of Issued Books |
| Total Members | All library.Members records | Includes inactive and expired memberships; staff login accounts alone are not memberships |
| Total Fine Collected | Sum of library.Payments.Amount | All-time INR payments actually recorded; excludes unpaid and accruing fines |

The title count is supporting context below Total Books. A book title with five copies contributes five to Total Books. Archived copies do not contribute to current stock, while their completed borrowing history stays in the all-time return total and monthly history.

The stock chart uses mutually exclusive categories: Available, On loan (not overdue), and Overdue. These sum to Total Books. It does not stack Overdue on top of all Issued Books.

The monthly grouped bar chart shows six UTC calendar months, including the current month to date. Issues use IssuedAt and returns use actual ReturnedAt. It includes archived titles, preserves zero-activity months, and does not group returns by the original checkout month. Cards intentionally mix current stock with explicitly labeled all-time completed activity; they should not be added together.

## Database and source lineage

Apply Database/005_Dashboard.sql after scripts 001–004. The setup script includes it. It updates library.Dashboard_Get and adds indexes on Loans.IssuedAt and Loans.ReturnedAt; it does not alter books, memberships, loans or payments. The database still has eight tables and 25 procedures.

Dapper reads the first result set into Dashboard and the second into six MonthlyCirculation rows. Cards and charts use the same server-rendered response. The procedure performs aggregate reads with short shared range locks inside one transaction, in member/book/loan order consistent with circulation operations, to avoid inconsistent stock totals between its result sets. Measure lock duration and reporting load before deployment at larger scale; consider a reporting replica or reviewed snapshot-isolation strategy if needed.

The page shows the SQL read timestamp in UTC and has a Refresh overview link. There is no hidden auto-refresh scheduler or fabricated trend data. The expandable chart-data section contains exact reviewed monthly values, scope notes and definitions. The browser payload contains aggregates only, not member identities, contact details, passwords or tokens.

## Charts, accessibility and empty data

The doughnut shows copy composition; the grouped bars compare issue/return transaction counts from a common zero baseline. Tooltips use the full month/year and integer counts. Colours match the application's palette and are reinforced with text labels, counts and an accessible table. Reduced-motion preferences disable chart animation.

When stock is empty, the page shows an explicit no-inventory message instead of a fake ring. A zero-activity monthly chart retains all six calendar months. Server-rendered cards and the expandable data table remain available if JavaScript fails. Chart loading failure reveals a visible notice directing readers to those values.

All scripts are local and work with the existing self-only Content Security Policy. There is no runtime CDN dependency.

## Vendor provenance

Chart.js 4.5.1 was downloaded from the official npm package with lifecycle scripts disabled:

    npm pack chart.js@4.5.1 --ignore-scripts

npm integrity:

    sha512-GIjfiT9dbmHRiYi6Nl2yFCq7kkwdkp1W/lp2J99rX0yo9tgJGn3lKQATztIjb5tVtevcBtIdICNWqlq5+E8/Pw==

The unmodified UMD bundle and MIT licence are under wwwroot/lib/chartjs. Keep the licence when redistributing it. [Chart.js integration documentation](https://www.chartjs.org/docs/latest/getting-started/integration) describes local script-tag usage.

## Verification

Dashboard integration tests exercise:

- Multiple copies per title, current loans, overdue loans, and archived inventory.
- Repeated completed returns and retained history for archived titles.
- Inactive memberships included in Total Members.
- A paid 22.50 fine contributing to collected payments while an unpaid 7.50 fine does not.
- Six ordered calendar-month buckets and reconciliation to raw issue/return event dates.
- All seven Bootstrap cards, local chart scripts, and staff access.
- Member users receiving a borrowing-page redirect without staff dashboard data.

Fixtures are confined to the dedicated SQL test database. No sample inventory or payments are inserted into the real library database. See VERIFICATION.md for build and test results.

Member Management update: deleted/archived members are excluded from Total Members and Active Members. Inactive and expired nondeleted memberships remain included in Total Members. Apply 007 after 006.
