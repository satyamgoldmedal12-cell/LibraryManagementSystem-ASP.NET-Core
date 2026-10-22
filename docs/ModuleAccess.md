# Module Path and Page In Role

See [GoldErpPageRightsReference.md](GoldErpPageRightsReference.md) for the reference source, architecture, database and implementation details.

## Administrator workflow

1. Open /ModulePath (or /ModulePaths). Use Add/List to maintain page names, parent groups, leaf pages, Label ID, Sequence ID, controller, Active and the eight available operation flags.
2. Open /PageInRole and choose Manage page rights for LibraryManagement.
3. Select User wise or Role wise. Search/select a user, or select Librarian/Member, and load page rights.
4. Search or expand the tree, tick the required operations and save. Read is required before any other operation. Disabled cells are outside the page capabilities or selected role.
5. Changes affect the user's menu and direct action access on the next request. Account/password/session/logout self-service stays available even with no business modules.

Unconfigured users inherit configured role defaults, or built-in defaults when no role matrix exists. Saving a user-wise matrix creates an explicit override. Role updates preserve custom user matrices. Admin access management stays available to prevent lockout. Existing role authorization remains the upper bound, so Members cannot gain staff/Admin routes through flags.

## Required operations

| Action | Right |
| --- | --- |
| Lists/details/profile/cover/receipt/history/lookup | Read |
| Books/Members Edit with ID 0 | Read + Add |
| Books/Members Edit with existing ID | Read + Edit |
| Delete / confirmation | Read + Delete |
| Members Restore | Read + Edit |
| Loans Issue / Availability | Loans Read + Add |
| Returns Confirm / legacy Loans Return | Returns Read + Edit |
| Fines Pay / legacy Loans PayFine | Fines Read + Add |
| Reports Excel/PDF download | Reports Read + Export |
| Settings Index GET / POST | Read / Read + Edit |
| Users Create | Read + Add |
| Users edit/reset password/activate/deactivate | Read + Edit |
| PageInRole SaveUser / SaveRole | Read + Edit, Admin only |

Lookup selectors need Books Read and Members Read when staff use their book/member filters. Import, Approval1 and Approval2 are permission slots for implemented endpoints; enabling them does not create an import or approval workflow.

SQL migrations 014 and 015 include revision checks, parameterized stored procedures, PKs/FKs/indexes, all six audit columns and atomic audit writes. Module groups with children cannot be deleted. Invalid hierarchy/cycles, stale forms and role escalation are rejected. The old /UserAccess pages remain compatible.
