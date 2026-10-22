# GoldERP reference: Module Path and Page In Role

The reference source was read from D:/GoldERP/GoldERP/PageRightsManagement/ModulePath.aspx, PageInRole.aspx, PageInRoleEdit.aspx and their code-behind. The GoldERP source is unchanged. Only its page-management concepts were adapted; no GoldERP connection strings, credentials, user IDs, DevExpress binaries, SQL data or WebForms dependencies were copied.

## Pages

- /ModulePath is a compatible singular route for /ModulePaths.
- Module Path has Add/List navigation, parent page/group selection, a computed full page path, page name, leaf-node flag, Label ID, Sequence ID, MVC controller/path, Active and all eight action flags. Lists provide search, pagination, edit/delete and restore. Groups have no action rights. Group type and controller identity stay fixed after creation to protect assigned rights. Pages can be moved between groups. Overview may remain a root page.
- /PageInRole lists the LibraryManagement application. Choose Manage page rights.
- /PageInRole/Edit offers User wise or Role wise, a Staff/Members user category filter, user search or role selection, and a hierarchical permission table. Expand/collapse and search the tree, tick operations and save the selected matrix. Read is selected when another operation is checked; clearing Read clears the other operations. Server validation remains authoritative.

The categories and application selector reflect LibraryManagement. GoldERP-only Sales Executive categories and .aspx paths do not belong to this application's model. The UI uses Bootstrap and Razor instead of DevExpress WebForms controls. Approval 1 and Approval 2 retain the additional flags requested for this project; they are not new approval workflows.

## Role and user behavior

Priority is: active account and existing role authorization -> active page and ancestors -> configured module capabilities -> user-wise custom matrix, otherwise role-wise defaults, otherwise built-in role defaults. Operations other than Read require Read as well.

Role-wise changes apply on the next request to existing and future users who have no user-wise custom matrix. Custom users retain their assigned matrix. This avoids silently replacing individual exceptions. It is a deliberate adaptation of GoldERP's role-wide bulk-update behavior. Administrators remain able to manage access and cannot be restricted through these forms. Members cannot be elevated into staff or administration routes.

A parent menu group is shown only when it has a permitted descendant. Disabled or deleted parents deny descendant page URLs too. Empty or unauthorized groups do not appear in the sidebar. Access-management modules and their parent groups cannot be disabled or deleted. Deleting a group with active children is rejected; move or delete its children first.

The old /UserAccess routes remain compatible. The dynamic sidebar exposes Page in role as the main permissions page. Existing custom assignments survive the migration. It adds hierarchy and role defaults without replacing book/member/circulation data.

## Storage and validation

Apply Database/015_GoldErpPageRights.sql after 014, using sqlcmd -b -I. The migration is transactional and repeatable. On the first upgrade it adds Collection, Library operations and Administration parent groups and the PageInRole module. A rerun preserves administrator changes to the hierarchy.

ModulePaths gains ParentId (self FK), IsLeafNode and LabelId; SortOrder is presented as Sequence ID. MVC paths and full parent paths are computed rather than stored twice. A filtered unique index preserves one registration per controller while allowing multiple groups without controllers. SQL checks constrain leaf/group shape and Label ID. The existing eight operation columns remain.

RoleAccessProfiles and RoleModuleAccess store role-default matrices with PKs, FKs, indexes and all six audit columns. Role/user saves serialize with the same application lock, compare catalog/profile revisions, enforce active administrator and role/capability ceilings, and commit audit events with the changes. Cycles, leaf parents, stale matrices and privileged-role escalation are rejected. Permission values are scoped to one request so revocations do not wait for login claims to expire.

Unit and isolated SQL/MVC tests cover hierarchy ordering, invalid parents/cycles, inactive ancestors, user override priority, PageInRole authorization, form saves, all eight flags, target mismatch, antiforgery, stale updates and transactional audit failure. Use a database ending in _Test for all fixtures.
