-- 016: Stronger PageInRole model + module path guards (inspired by GoldERP, safer and idempotent).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRAN;
GO

-- Ensure critical access modules always exist and stay available.
IF NOT EXISTS (SELECT 1 FROM library.ModulePaths WHERE ControllerName = 'ModulePaths')
BEGIN
	INSERT library.ModulePaths
	(
		DisplayName,
		ControllerName,
		SortOrder,
		IsActive,
		IsRead,
		IsAdd,
		IsEdit,
		IsDelete,
		IsExport,
		IsImport,
		IsApproval1,
		IsApproval2
	)
	VALUES
	(
		N'Module paths',
		N'ModulePaths',
		100,
		1,
		1,
		1,
		1,
		1,
		1,
		1,
		1,
		1
	);
END;

IF NOT EXISTS (SELECT 1 FROM library.ModulePaths WHERE ControllerName = 'UserAccess')
BEGIN
	INSERT library.ModulePaths
	(
		DisplayName,
		ControllerName,
		SortOrder,
		IsActive,
		IsRead,
		IsAdd,
		IsEdit,
		IsDelete,
		IsExport,
		IsImport,
		IsApproval1,
		IsApproval2
	)
	VALUES
	(
		N'User access',
		N'UserAccess',
		110,
		1,
		1,
		1,
		1,
		1,
		1,
		1,
		1,
		1
	);
END;

IF NOT EXISTS (SELECT 1 FROM library.ModulePaths WHERE ControllerName = 'PageInRole')
BEGIN
	INSERT library.ModulePaths
	(
		DisplayName,
		ControllerName,
		SortOrder,
		IsActive,
		IsRead,
		IsAdd,
		IsEdit,
		IsDelete,
		IsExport,
		IsImport,
		IsApproval1,
		IsApproval2
	)
	VALUES
	(
		N'Page in role',
		N'PageInRole',
		115,
		1,
		1,
		1,
		1,
		1,
		1,
		1,
		1,
		1
	);
END;

UPDATE library.ModulePaths
SET
	IsActive = 1,
	IsRead = 1,
	DeletedDate = NULL,
	DeletedBy = NULL,
	ModifiedDate = SYSUTCDATETIME(),
	ModifiedBy = NULL,
	Revision = Revision + 1
WHERE ControllerName IN ('ModulePaths', 'UserAccess', 'PageInRole')
  AND (IsActive = 0 OR IsRead = 0 OR DeletedDate IS NOT NULL);

GO

IF OBJECT_ID('library.PageInRoleProfiles', 'U') IS NULL
BEGIN
	CREATE TABLE library.PageInRoleProfiles
	(
		Role nvarchar(20) NOT NULL CONSTRAINT PK_PageInRoleProfiles PRIMARY KEY,
		Revision int NOT NULL CONSTRAINT DF_PageInRoleProfiles_Revision DEFAULT 1,
		IsActive bit NOT NULL CONSTRAINT DF_PageInRoleProfiles_IsActive DEFAULT 1,
		CreatedDate datetime2 NOT NULL CONSTRAINT DF_PageInRoleProfiles_CreatedDate DEFAULT SYSUTCDATETIME(),
		CreatedBy nvarchar(450) NULL REFERENCES library.Users (Id),
		ModifiedDate datetime2 NULL,
		ModifiedBy nvarchar(450) NULL REFERENCES library.Users (Id),
		DeletedDate datetime2 NULL,
		DeletedBy nvarchar(450) NULL REFERENCES library.Users (Id),
		CONSTRAINT CK_PageInRoleProfiles_Role CHECK (Role IN ('Admin', 'Librarian', 'Member'))
	);
END;

IF OBJECT_ID('library.PageInRoleAccess', 'U') IS NULL
BEGIN
	CREATE TABLE library.PageInRoleAccess
	(
		Role nvarchar(20) NOT NULL REFERENCES library.PageInRoleProfiles (Role),
		ModuleId int NOT NULL REFERENCES library.ModulePaths (Id),
		IsRead bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsRead DEFAULT 0,
		IsAdd bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsAdd DEFAULT 0,
		IsEdit bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsEdit DEFAULT 0,
		IsDelete bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsDelete DEFAULT 0,
		IsExport bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsExport DEFAULT 0,
		IsImport bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsImport DEFAULT 0,
		IsApproval1 bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsApproval1 DEFAULT 0,
		IsApproval2 bit NOT NULL CONSTRAINT DF_PageInRoleAccess_IsApproval2 DEFAULT 0,
		CreatedDate datetime2 NOT NULL CONSTRAINT DF_PageInRoleAccess_CreatedDate DEFAULT SYSUTCDATETIME(),
		CreatedBy nvarchar(450) NULL REFERENCES library.Users (Id),
		ModifiedDate datetime2 NULL,
		ModifiedBy nvarchar(450) NULL REFERENCES library.Users (Id),
		DeletedDate datetime2 NULL,
		DeletedBy nvarchar(450) NULL REFERENCES library.Users (Id),
		CONSTRAINT PK_PageInRoleAccess PRIMARY KEY (Role, ModuleId),
		CONSTRAINT CK_PageInRoleAccess_Read CHECK
		(
			IsRead = 1
			OR (IsAdd = 0 AND IsEdit = 0 AND IsDelete = 0 AND IsExport = 0 AND IsImport = 0 AND IsApproval1 = 0 AND IsApproval2 = 0)
		)
	);
END;

IF NOT EXISTS
(
	SELECT 1
	FROM sys.indexes
	WHERE object_id = OBJECT_ID('library.PageInRoleAccess')
	  AND name = 'IX_PageInRoleAccess_Module'
)
BEGIN
	CREATE INDEX IX_PageInRoleAccess_Module
		ON library.PageInRoleAccess (ModuleId)
		INCLUDE (Role, IsRead);
END;

GO

-- One-time migration from old role access tables when present.
IF OBJECT_ID('library.RoleAccessProfiles', 'U') IS NOT NULL
AND OBJECT_ID('library.RoleModuleAccess', 'U') IS NOT NULL
BEGIN
	INSERT library.PageInRoleProfiles (Role, Revision, IsActive, CreatedDate, CreatedBy, ModifiedDate, ModifiedBy, DeletedDate, DeletedBy)
	SELECT r.Role, r.Revision, 1, r.CreatedDate, r.CreatedBy, r.ModifiedDate, r.ModifiedBy, r.DeletedDate, r.DeletedBy
	FROM library.RoleAccessProfiles r
	WHERE r.Role IN ('Admin', 'Librarian', 'Member')
	  AND NOT EXISTS (SELECT 1 FROM library.PageInRoleProfiles p WHERE p.Role = r.Role);

	INSERT library.PageInRoleAccess
	(
		Role,
		ModuleId,
		IsRead,
		IsAdd,
		IsEdit,
		IsDelete,
		IsExport,
		IsImport,
		IsApproval1,
		IsApproval2,
		CreatedDate,
		CreatedBy,
		ModifiedDate,
		ModifiedBy,
		DeletedDate,
		DeletedBy
	)
	SELECT
		a.Role,
		a.ModuleId,
		a.IsRead,
		a.IsAdd,
		a.IsEdit,
		a.IsDelete,
		a.IsExport,
		a.IsImport,
		a.IsApproval1,
		a.IsApproval2,
		a.CreatedDate,
		a.CreatedBy,
		a.ModifiedDate,
		a.ModifiedBy,
		a.DeletedDate,
		a.DeletedBy
	FROM library.RoleModuleAccess a
	WHERE a.Role IN ('Admin', 'Librarian', 'Member')
	  AND NOT EXISTS
	  (
		  SELECT 1
		  FROM library.PageInRoleAccess p
		  WHERE p.Role = a.Role
			AND p.ModuleId = a.ModuleId
	  );
END;

GO

CREATE OR ALTER PROCEDURE library.PageInRole_Get
	@Role nvarchar(20)
AS
BEGIN
	SET NOCOUNT ON;

	IF @Role NOT IN ('Admin', 'Librarian', 'Member')
	BEGIN
		THROW 50001, 'Choose a valid role.', 1;
	END;

	SELECT
		Role,
		Revision
	FROM library.PageInRoleProfiles
	WHERE Role = @Role
	  AND DeletedDate IS NULL;

	SELECT
		a.ModuleId,
		m.Revision AS ModuleRevision,
		a.IsRead,
		a.IsAdd,
		a.IsEdit,
		a.IsDelete,
		a.IsExport,
		a.IsImport,
		a.IsApproval1,
		a.IsApproval2
	FROM library.PageInRoleAccess a
	INNER JOIN library.ModulePaths m ON m.Id = a.ModuleId
	WHERE a.Role = @Role
	  AND a.DeletedDate IS NULL
	  AND m.DeletedDate IS NULL
	ORDER BY m.SortOrder, m.DisplayName, m.Id;
END;
GO

CREATE OR ALTER PROCEDURE library.PageInRole_Save
	@Role nvarchar(20),
	@Revision int,
	@Grants nvarchar(max),
	@ActorId nvarchar(450)
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	BEGIN TRY
		BEGIN TRAN;

		DECLARE @Lock int;
		EXEC @Lock = sys.sp_getapplock
			@Resource = 'LibraryManagement.PageInRole',
			@LockMode = 'Exclusive',
			@LockOwner = 'Transaction',
			@LockTimeout = 10000;

		IF @Lock < 0
			THROW 50001, 'Another role-permission update is running. Try again.', 1;

		IF NOT EXISTS
		(
			SELECT 1
			FROM library.Users WITH (UPDLOCK, HOLDLOCK)
			WHERE Id = @ActorId
			  AND Role = 'Admin'
			  AND IsActive = 1
			  AND DeletedDate IS NULL
		)
			THROW 50001, 'An active administrator is required.', 1;

		IF @Role NOT IN ('Admin', 'Librarian', 'Member')
			THROW 50001, 'Choose a valid role.', 1;

		IF ISJSON(@Grants) <> 1
			THROW 50001, 'Invalid role permission payload.', 1;

		DECLARE @CurrentRevision int =
		(
			SELECT Revision
			FROM library.PageInRoleProfiles WITH (UPDLOCK, HOLDLOCK)
			WHERE Role = @Role
			  AND DeletedDate IS NULL
		);

		IF @CurrentRevision IS NULL
		BEGIN
			IF @Revision <> 0
				THROW 50001, 'Role permissions changed. Reload and try again.', 1;

			INSERT library.PageInRoleProfiles (Role, Revision, IsActive, CreatedBy)
			VALUES (@Role, 1, 1, @ActorId);

			SET @CurrentRevision = 1;
		END;
		ELSE IF @CurrentRevision <> @Revision
		BEGIN
			THROW 50001, 'Role permissions changed. Reload and try again.', 1;
		END;

		DECLARE @Input TABLE
		(
			ModuleId int NOT NULL,
			ModuleRevision int NOT NULL,
			IsRead bit NOT NULL,
			IsAdd bit NOT NULL,
			IsEdit bit NOT NULL,
			IsDelete bit NOT NULL,
			IsExport bit NOT NULL,
			IsImport bit NOT NULL,
			IsApproval1 bit NOT NULL,
			IsApproval2 bit NOT NULL,
			PRIMARY KEY (ModuleId)
		);

		INSERT @Input (ModuleId, ModuleRevision, IsRead, IsAdd, IsEdit, IsDelete, IsExport, IsImport, IsApproval1, IsApproval2)
		SELECT
			j.ModuleId,
			j.ModuleRevision,
			j.IsRead,
			j.IsAdd,
			j.IsEdit,
			j.IsDelete,
			j.IsExport,
			j.IsImport,
			j.IsApproval1,
			j.IsApproval2
		FROM OPENJSON(@Grants)
		WITH
		(
			ModuleId int '$.ModuleId',
			ModuleRevision int '$.ModuleRevision',
			IsRead bit '$.IsRead',
			IsAdd bit '$.IsAdd',
			IsEdit bit '$.IsEdit',
			IsDelete bit '$.IsDelete',
			IsExport bit '$.IsExport',
			IsImport bit '$.IsImport',
			IsApproval1 bit '$.IsApproval1',
			IsApproval2 bit '$.IsApproval2'
		) j;

		IF EXISTS
		(
			SELECT 1
			FROM @Input i
			WHERE i.IsRead = 0
			  AND (i.IsAdd = 1 OR i.IsEdit = 1 OR i.IsDelete = 1 OR i.IsExport = 1 OR i.IsImport = 1 OR i.IsApproval1 = 1 OR i.IsApproval2 = 1)
		)
			THROW 50001, 'Operation rights require read rights.', 1;

		IF EXISTS
		(
			SELECT 1
			FROM @Input i
			LEFT JOIN library.ModulePaths m ON m.Id = i.ModuleId
			WHERE m.Id IS NULL
			   OR m.Revision <> i.ModuleRevision
			   OR m.DeletedDate IS NOT NULL
			   OR m.IsLeafNode = 0
		)
			THROW 50001, 'Module list changed. Reload and submit again.', 1;

		-- Ensure access administration pages stay readable for Admin role.
		IF @Role = 'Admin'
		AND EXISTS
		(
			SELECT 1
			FROM library.ModulePaths m
			LEFT JOIN @Input i ON i.ModuleId = m.Id
			WHERE m.ControllerName IN ('ModulePaths', 'UserAccess', 'PageInRole')
			  AND (i.ModuleId IS NULL OR i.IsRead = 0)
		)
			THROW 50001, 'Admin role must retain read access to access-management pages.', 1;

		MERGE library.PageInRoleAccess AS t
		USING
		(
			SELECT
				@Role AS Role,
				i.ModuleId,
				i.IsRead,
				i.IsAdd,
				i.IsEdit,
				i.IsDelete,
				i.IsExport,
				i.IsImport,
				i.IsApproval1,
				i.IsApproval2
			FROM @Input i
		) AS s
		ON t.Role = s.Role
	   AND t.ModuleId = s.ModuleId
		WHEN MATCHED THEN
			UPDATE SET
				IsRead = s.IsRead,
				IsAdd = s.IsAdd,
				IsEdit = s.IsEdit,
				IsDelete = s.IsDelete,
				IsExport = s.IsExport,
				IsImport = s.IsImport,
				IsApproval1 = s.IsApproval1,
				IsApproval2 = s.IsApproval2,
				DeletedDate = NULL,
				DeletedBy = NULL,
				ModifiedDate = SYSUTCDATETIME(),
				ModifiedBy = @ActorId
		WHEN NOT MATCHED THEN
			INSERT
			(
				Role,
				ModuleId,
				IsRead,
				IsAdd,
				IsEdit,
				IsDelete,
				IsExport,
				IsImport,
				IsApproval1,
				IsApproval2,
				CreatedBy
			)
			VALUES
			(
				s.Role,
				s.ModuleId,
				s.IsRead,
				s.IsAdd,
				s.IsEdit,
				s.IsDelete,
				s.IsExport,
				s.IsImport,
				s.IsApproval1,
				s.IsApproval2,
				@ActorId
			)
		WHEN NOT MATCHED BY SOURCE AND t.Role = @Role THEN
			UPDATE SET
				IsRead = 0,
				IsAdd = 0,
				IsEdit = 0,
				IsDelete = 0,
				IsExport = 0,
				IsImport = 0,
				IsApproval1 = 0,
				IsApproval2 = 0,
				DeletedDate = SYSUTCDATETIME(),
				DeletedBy = @ActorId,
				ModifiedDate = SYSUTCDATETIME(),
				ModifiedBy = @ActorId;

		UPDATE library.PageInRoleProfiles
		SET
			Revision = Revision + 1,
			IsActive = 1,
			DeletedDate = NULL,
			DeletedBy = NULL,
			ModifiedDate = SYSUTCDATETIME(),
			ModifiedBy = @ActorId
		WHERE Role = @Role;

		INSERT library.AuditEvents (ActorId, Action, EntityId)
		VALUES (@ActorId, N'Page in role permissions saved', @Role);

		COMMIT;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0
			ROLLBACK;
		THROW;
	END CATCH;
END;
GO

-- Backward-compatible names used by repository/service today.
CREATE OR ALTER PROCEDURE library.RoleAccess_Get
	@Role nvarchar(20)
AS
BEGIN
	SET NOCOUNT ON;
	EXEC library.PageInRole_Get @Role = @Role;
END;
GO

CREATE OR ALTER PROCEDURE library.RoleAccess_Save
	@Role nvarchar(20),
	@Revision int,
	@Grants nvarchar(max),
	@ActorId nvarchar(450)
AS
BEGIN
	SET NOCOUNT ON;
	EXEC library.PageInRole_Save @Role = @Role, @Revision = @Revision, @Grants = @Grants, @ActorId = @ActorId;
END;
GO

COMMIT;
GO
