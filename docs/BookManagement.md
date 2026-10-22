# Module 3 — Book Management

Implemented in the existing .NET 10 MVC application and Onion Architecture.

| Feature | Behavior |
| --- | --- |
| Add / Edit | Admin and Librarian can manage title, author, optional publisher, category, ISBN-13 and quantity. |
| View / Search | Signed-in users can browse paged cards and details; search covers title, author, publisher, category and ISBN. Wildcards are treated literally. |
| Delete | Staff confirm a POST action that archives the book. It disappears from the active catalog; loan/fine/audit/cover history is preserved. Outstanding loans prevent deletion. |
| Restore | Staff enable Include archived, choose Manage / restore and change Book status to Active catalog. |
| ISBN | Canonical 13 digits with a valid checksum; edit forms accept printed spaces/hyphens. ISBN remains unique, including archived titles. |
| Quantity | Total copies: 1–10,000. It cannot drop below outstanding loans. Available copies are calculated, never editable or trusted from a submitted form. |
| Cover | Optional JPEG or PNG, up to 2 MB, 4096 pixels per side and 12 MP. Upload/preview, replacement, removal and fallback initials are supported. |
| Status | Available when at least one active-catalog copy is free; All copies issued when none are free; Archived for deleted/archived titles. Staff choose catalog Active/Archived separately from calculated availability. |

## Cover pipeline

Only MVC accepts IFormFile. Application uses BookCoverUpload, IBookManagementService and IBookCoverProcessor contracts, without an ASP.NET Core or image-library dependency. The Infrastructure processor uses pinned SkiaSharp packages, checks extension against actual decoded format, checks dimensions before allocating pixels, requires a successful full decode, applies image orientation, limits the longest output side to 1024 pixels, and reconstructs a JPEG at quality 85. Filenames, EXIF/GPS metadata and appended content are not stored. SVG, GIF, WebP, HTML and executable uploads are rejected. Browser MIME claims are not trusted.

Reconstructed bytes are stored in library.BookCovers, outside catalog query payloads and static web folders. Covers are returned by the authenticated /Books/Cover/{id} action with a fixed image/jpeg content type and nosniff. Members cannot fetch archived details or covers even by guessing an ID. New cover bytes, cover removal, book changes and audit writes share the same SQL transaction. A failed/stale edit cannot replace the previous cover. Edit requests are capped at 3 MB and limited to ten per minute per client IP.

The per-file limit and decoder limits bound work; larger deployments should load-test their expected upload concurrency. Covers are part of SQL backups. No original upload is retained. Native Skia runtime assets are packaged for supported desktop/server runtimes, including Linux; test the chosen publishing RID and host libraries for deployment.

## Database setup

Apply Database/006_BookManagement.sql after 001–005. Setup-Local.ps1 includes it. It creates one BookCovers table, adds Books_Delete and BookCovers_Get, and updates Books_List, Books_Get and Books_Save. No existing library rows are deleted. The resulting schema has nine tables and 27 procedures. Rerunning the migration preserves covers and existing data.

```powershell
sqlcmd -S '(localdb)\MSSQLLocalDB' -d LibraryManagement -E -I -b -i Database\006_BookManagement.sql
```

The old Books_Save call remains valid: omitting cover parameters leaves the cover unchanged. SQL retains book locks, stock checks, revision protection and parameterized procedure access.

## Verification

Core tests cover validation and atomic persistence orchestration. Decoder tests cover JPEG/PNG reconstruction, disguised files, extension mismatch, corrupt images, dimension limits, resize and trailing payload removal. SQL/MVC tests cover cover upload/keep/replace/remove, publisher/category/ISBN search, quantities, archive/restore, history preservation, stale saves/deletes, CSRF, member POST denial and private cover access. Tests use only a database whose name ends in _Test.

References: [Microsoft file upload guidance](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0), [SkiaSharp package and license](https://www.nuget.org/packages/SkiaSharp/4.153.1).
