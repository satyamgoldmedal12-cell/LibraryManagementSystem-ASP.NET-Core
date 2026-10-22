# Module 8 — Reports

Admin and Librarian can open Reports from the sidebar. Choose a report, apply From Date/To Date, Member, Book and Status, then run it. Member and book lookups include archived records. Screens are paged, while totals cover the complete filtered result.

| Report | Date filter | Meaning |
| --- | --- | --- |
| Book Stock | Catalog creation date | Current quantity, available copies and active issues per title. Archived stock is retained but unavailable. A Member filter selects titles ever borrowed by that member. |
| Book Issue | Issue timestamp | One row per matching issue with current status and fine. |
| Book Return | Return timestamp | Finalized late days, checkout rate, fine, payment status and receiving staff. |
| Overdue Books | Due date | Outstanding loans whose due date is before today's UTC date; fine remains an estimate. |
| Member Wise Issue | Issue timestamp | One row per member with issue/active/return/overdue counts and pending/collected fines for the selected issue cohort. |
| Fine Collection | Payment timestamp | Actual recorded payments, including the receiving staff; excludes pending or accruing fines. |
| Monthly Activity | Each event's own timestamp | Issues, returns and payments counted in their own month, including months with zero activity. Status selects event types. |

Dates use UTC, including both selected endpoints. Monthly activity defaults to the last 12 calendar months through today and supports up to 120 months. Other reports allow open date endpoints. Status choices depend on the report. Historical reports continue to include archived titles and memberships.

## Excel and PDF

Export buttons reuse the same report definition and filters. They include all matching rows across screen pages, up to 5,000 rows. Larger requests return a clear validation message and no partial file. Narrow the filters before retrying. Downloads are protected by staff authorization, no-store caching and a per-IP export request limit.

Excel files are genuine ZIP/Open XML .xlsx workbooks with text, numeric, money and date cells, frozen headers and an autofilter. User-entered values are inline strings, never formulas. Metadata records the report, filter IDs, date basis, generation time and summary. PDF exports include metadata, repeated column headers, wrapped content and page numbers. Neither format uses a renamed CSV or HTML file. Excel uses .NET ZIP/XML APIs; PDF uses the project's existing SkiaSharp dependency.

The Reports link from the existing Fine Collection screen carries its member/book/date filters into the export-enabled report.

## Architecture and setup

Application owns ReportCatalog, DTOs, validation, IReportService and the query/export ports. Infrastructure implements parameterized Reports_Query and the document renderer. The MVC controller handles authorization, binding and downloads. Domain/Application remain independent of MVC, SQL and document libraries.

Apply Database/010_Reports.sql after 001–009. It adds one query procedure and a catalog-date index; it does not change library records. Setup-Local.ps1 includes it.

SQL/MVC tests reconcile all seven reports, date bases, zero-filled activity months, archival, status filters, member/book scope, cross-page exports, safe Excel text and unauthorized access. Independent QA reads the exported workbooks and PDF text and renders the PDFs, including a long-text multi-page case.

PDF API reference: https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skdocument.createpdf
