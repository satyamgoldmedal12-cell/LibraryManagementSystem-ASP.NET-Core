-- GoldERP PageRightsManagement flow adapted to LibraryManagement MVC.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRAN;
GO
IF COL_LENGTH('library.ModulePaths','ParentId') IS NULL ALTER TABLE library.ModulePaths ADD ParentId int NULL REFERENCES library.ModulePaths(Id);
IF COL_LENGTH('library.ModulePaths','IsLeafNode') IS NULL ALTER TABLE library.ModulePaths ADD IsLeafNode bit NOT NULL DEFAULT 1;
IF COL_LENGTH('library.ModulePaths','LabelId') IS NULL ALTER TABLE library.ModulePaths ADD LabelId int NOT NULL DEFAULT 1;
GO
IF OBJECT_ID('library.UQ_ModulePaths_Controller','UQ') IS NOT NULL ALTER TABLE library.ModulePaths DROP CONSTRAINT UQ_ModulePaths_Controller;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('library.ModulePaths') AND name='ControllerName' AND is_nullable=0) ALTER TABLE library.ModulePaths ALTER COLUMN ControllerName nvarchar(80) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.ModulePaths') AND name='UX_ModulePaths_Controller') CREATE UNIQUE INDEX UX_ModulePaths_Controller ON library.ModulePaths(ControllerName) WHERE ControllerName IS NOT NULL;
IF OBJECT_ID('library.CK_ModulePaths_ActiveRead','C') IS NOT NULL ALTER TABLE library.ModulePaths DROP CONSTRAINT CK_ModulePaths_ActiveRead;
ALTER TABLE library.ModulePaths WITH CHECK ADD CONSTRAINT CK_ModulePaths_ActiveRead CHECK(IsLeafNode=0 OR IsActive=0 OR IsRead=1);
IF OBJECT_ID('library.CK_ModulePaths_Shape','C') IS NULL ALTER TABLE library.ModulePaths WITH CHECK ADD CONSTRAINT CK_ModulePaths_Shape CHECK((IsLeafNode=1 AND ControllerName IS NOT NULL) OR (IsLeafNode=0 AND ControllerName IS NULL AND IsRead=0 AND IsAdd=0 AND IsEdit=0 AND IsDelete=0 AND IsExport=0 AND IsImport=0 AND IsApproval1=0 AND IsApproval2=0));
IF OBJECT_ID('library.CK_ModulePaths_Parent','C') IS NULL ALTER TABLE library.ModulePaths WITH CHECK ADD CONSTRAINT CK_ModulePaths_Parent CHECK(ParentId IS NULL OR ParentId<>Id);
IF OBJECT_ID('library.CK_ModulePaths_Label','C') IS NULL ALTER TABLE library.ModulePaths WITH CHECK ADD CONSTRAINT CK_ModulePaths_Label CHECK(LabelId BETWEEN 1 AND 50);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.ModulePaths') AND name='IX_ModulePaths_Parent') CREATE INDEX IX_ModulePaths_Parent ON library.ModulePaths(ParentId,SortOrder,LabelId);
GO
IF OBJECT_ID('library.RoleAccessProfiles','U') IS NULL CREATE TABLE library.RoleAccessProfiles(Role nvarchar(20) CONSTRAINT PK_RoleAccessProfiles PRIMARY KEY,Revision int NOT NULL DEFAULT 1,CreatedDate datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id),CONSTRAINT CK_RoleAccessProfiles_Role CHECK(Role IN('Librarian','Member')));
IF OBJECT_ID('library.RoleModuleAccess','U') IS NULL CREATE TABLE library.RoleModuleAccess(Role nvarchar(20) NOT NULL REFERENCES library.RoleAccessProfiles(Role),ModuleId int NOT NULL REFERENCES library.ModulePaths(Id),IsRead bit NOT NULL DEFAULT 0,IsAdd bit NOT NULL DEFAULT 0,IsEdit bit NOT NULL DEFAULT 0,IsDelete bit NOT NULL DEFAULT 0,IsExport bit NOT NULL DEFAULT 0,IsImport bit NOT NULL DEFAULT 0,IsApproval1 bit NOT NULL DEFAULT 0,IsApproval2 bit NOT NULL DEFAULT 0,CreatedDate datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id),CONSTRAINT PK_RoleModuleAccess PRIMARY KEY(Role,ModuleId),CONSTRAINT CK_RoleModuleAccess_Read CHECK(IsRead=1 OR (IsAdd=0 AND IsEdit=0 AND IsDelete=0 AND IsExport=0 AND IsImport=0 AND IsApproval1=0 AND IsApproval2=0)));
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.RoleModuleAccess') AND name='IX_RoleModuleAccess_Module') CREATE INDEX IX_RoleModuleAccess_Module ON library.RoleModuleAccess(ModuleId);
GO
-- Seed hierarchy only on first upgrade; repeat runs never rearrange administrator changes.
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'LibraryManagement.SchemaVersion' AND CONVERT(int,value)>=15)
BEGIN
 DECLARE @Collection int,@Operations int,@Administration int;
 INSERT library.ModulePaths(DisplayName,ControllerName,IsLeafNode,SortOrder) VALUES(N'Collection',NULL,0,10);SET @Collection=SCOPE_IDENTITY();
 INSERT library.ModulePaths(DisplayName,ControllerName,IsLeafNode,SortOrder) VALUES(N'Library operations',NULL,0,20);SET @Operations=SCOPE_IDENTITY();
 INSERT library.ModulePaths(DisplayName,ControllerName,IsLeafNode,SortOrder) VALUES(N'Administration',NULL,0,30);SET @Administration=SCOPE_IDENTITY();
 UPDATE library.ModulePaths SET ParentId=CASE WHEN ControllerName IN('Books','Categories') THEN @Collection WHEN ControllerName IN('Members','Loans','Returns','Fines','Reports') THEN @Operations ELSE @Administration END,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=NULL WHERE IsLeafNode=1 AND ControllerName<>'Home';
 INSERT library.ModulePaths(DisplayName,ControllerName,ParentId,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Page in role','PageInRole',@Administration,105,1,1,1,1,1,1,1,1);
END;
GO
CREATE OR ALTER PROCEDURE library.Modules_Save @Id int,@DisplayName nvarchar(80),@ControllerName nvarchar(80),@SortOrder int,@IsActive bit,@Revision int,@IsRead bit,@IsAdd bit,@IsEdit bit,@IsDelete bit,@IsExport bit,@IsImport bit,@IsApproval1 bit,@IsApproval2 bit,@ActorId nvarchar(450),@ParentId int=NULL,@IsLeafNode bit=1,@LabelId int=1 AS
BEGIN SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRY BEGIN TRAN; DECLARE @Lock int; EXEC @Lock=sys.sp_getapplock @Resource='LibraryManagement.ModuleAccess',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @Lock<0 THROW 50001,'Another permission update is running. Try again.',1; IF NOT EXISTS(SELECT 1 FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ActorId AND Role='Admin' AND IsActive=1 AND DeletedDate IS NULL) THROW 50001,'An active administrator is required.',1;
 IF LEN(LTRIM(RTRIM(@DisplayName)))=0 OR @SortOrder NOT BETWEEN 0 AND 9999 OR (@IsLeafNode=1 AND @ControllerName NOT IN('Home','Books','Members','Loans','Returns','Fines','Reports','Categories','Users','Settings','ModulePaths','UserAccess','PageInRole')) OR @LabelId NOT BETWEEN 1 AND 50 THROW 50001,'Invalid module details.',1;
 IF @ControllerName IN('ModulePaths','UserAccess','PageInRole') AND (@IsActive=0 OR @IsRead=0) THROW 50001,'Access management must remain available to administrators.',1;
 IF @IsLeafNode=0 BEGIN SET @ControllerName=NULL; IF @IsRead=1 OR @IsAdd=1 OR @IsEdit=1 OR @IsDelete=1 OR @IsExport=1 OR @IsImport=1 OR @IsApproval1=1 OR @IsApproval2=1 THROW 50001,'Groups cannot have operation rights.',1; END;
 IF @ParentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@ParentId AND IsLeafNode=0 AND DeletedDate IS NULL) THROW 50001,'Choose an existing parent group.',1;
 DECLARE @Cursor int=@ParentId,@Depth int=0;
 WHILE @Cursor IS NOT NULL BEGIN
  IF @Cursor=@Id OR @Depth>=64 THROW 50001,'The parent would create an invalid hierarchy.',1;
  SET @Depth=@Depth+1;SELECT @Cursor=ParentId FROM library.ModulePaths WHERE Id=@Cursor;
 END;
 IF @Id=0 BEGIN
  IF EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName=@ControllerName) THROW 50001,'Controller already registered. Use Edit or restore its existing entry.',1;
  INSERT library.ModulePaths(DisplayName,ControllerName,ParentId,IsLeafNode,LabelId,SortOrder,IsActive,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2,CreatedBy) VALUES(@DisplayName,@ControllerName,@ParentId,@IsLeafNode,@LabelId,@SortOrder,@IsActive,@IsRead,@IsAdd,@IsEdit,@IsDelete,@IsExport,@IsImport,@IsApproval1,@IsApproval2,@ActorId); SET @Id=SCOPE_IDENTITY();
 END ELSE BEGIN
  IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@Id AND Revision=@Revision) THROW 50001,'Module changed. Reload and try again.',1;
  IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@Id AND IsLeafNode=@IsLeafNode AND (ControllerName=@ControllerName OR (ControllerName IS NULL AND @ControllerName IS NULL))) THROW 50001,'A registered controller cannot be renamed. Create a separate module instead.',1;
  UPDATE library.ModulePaths SET DisplayName=@DisplayName,ParentId=@ParentId,LabelId=@LabelId,SortOrder=@SortOrder,IsActive=@IsActive,IsRead=@IsRead,IsAdd=@IsAdd,IsEdit=@IsEdit,IsDelete=@IsDelete,IsExport=@IsExport,IsImport=@IsImport,IsApproval1=@IsApproval1,IsApproval2=@IsApproval2,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=NULL,DeletedBy=NULL WHERE Id=@Id;
 END;
 DECLARE @Blocked int;
 ;WITH Parents AS (SELECT Id,ParentId,IsActive,DeletedDate FROM library.ModulePaths WHERE ControllerName IN('ModulePaths','UserAccess','PageInRole') UNION ALL SELECT p.Id,p.ParentId,p.IsActive,p.DeletedDate FROM library.ModulePaths p JOIN Parents c ON p.Id=c.ParentId)
 SELECT @Blocked=COUNT(*) FROM Parents WHERE IsActive=0 OR DeletedDate IS NOT NULL OPTION(MAXRECURSION 64);
 IF @Blocked>0 THROW 50001,'Access administration and its parent groups must remain active.',1;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Module saved',CONVERT(nvarchar(20),@Id));
 COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH; SELECT @Id;
END;
GO
CREATE OR ALTER PROCEDURE library.Modules_Delete @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRY BEGIN TRAN; DECLARE @Lock int; EXEC @Lock=sys.sp_getapplock @Resource='LibraryManagement.ModuleAccess',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @Lock<0 THROW 50001,'Another permission update is running. Try again.',1; IF NOT EXISTS(SELECT 1 FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ActorId AND Role='Admin' AND IsActive=1 AND DeletedDate IS NULL) THROW 50001,'An active administrator is required.',1;
 IF EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@Id AND ControllerName IN('ModulePaths','UserAccess','PageInRole')) THROW 50001,'Access management modules cannot be deleted.',1;
 IF EXISTS(SELECT 1 FROM library.ModulePaths WHERE ParentId=@Id AND DeletedDate IS NULL) THROW 50001,'Move or delete child entries before deleting a group.',1;
 UPDATE library.ModulePaths SET IsActive=0,DeletedDate=SYSUTCDATETIME(),DeletedBy=@ActorId,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,Revision=Revision+1 WHERE Id=@Id AND Revision=@Revision AND DeletedDate IS NULL;
 IF @@ROWCOUNT<>1 THROW 50001,'Module changed. Reload and try again.',1;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Module deleted',CONVERT(nvarchar(20),@Id));
 COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.ModuleAccess_Save @UserId nvarchar(450),@UserRevision int,@Revision int,@Grants nvarchar(max),@ActorId nvarchar(450) AS
BEGIN SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRY BEGIN TRAN; DECLARE @Lock int; EXEC @Lock=sys.sp_getapplock @Resource='LibraryManagement.ModuleAccess',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @Lock<0 THROW 50001,'Another permission update is running. Try again.',1; IF NOT EXISTS(SELECT 1 FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ActorId AND Role='Admin' AND IsActive=1 AND DeletedDate IS NULL) THROW 50001,'An active administrator is required.',1;
 DECLARE @Role nvarchar(20),@CurrentUserRevision int;
 SELECT @Role=Role,@CurrentUserRevision=Revision FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@UserId AND DeletedDate IS NULL;
 IF @Role IS NULL OR @Role='Admin' THROW 50001,'Select an existing non-administrator account.',1;
 IF @CurrentUserRevision<>@UserRevision THROW 50001,'User account changed. Reload permissions.',1;
 IF COALESCE((SELECT Revision FROM library.UserAccessProfiles WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@UserId),0)<>@Revision THROW 50001,'Permissions changed. Reload and try again.',1;
 IF ISJSON(@Grants)<>1 THROW 50001,'Invalid permission data.',1;
 DECLARE @Rows TABLE(ModuleId int PRIMARY KEY,ModuleRevision int,IsRead bit NOT NULL,IsAdd bit NOT NULL,IsEdit bit NOT NULL,IsDelete bit NOT NULL,IsExport bit NOT NULL,IsImport bit NOT NULL,IsApproval1 bit NOT NULL,IsApproval2 bit NOT NULL);
 INSERT @Rows SELECT ModuleId,ModuleRevision,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2 FROM OPENJSON(@Grants) WITH(ModuleId int,ModuleRevision int,IsRead bit,IsAdd bit,IsEdit bit,IsDelete bit,IsExport bit,IsImport bit,IsApproval1 bit,IsApproval2 bit);
 IF (SELECT COUNT(*) FROM @Rows)<>(SELECT COUNT(*) FROM library.ModulePaths) OR EXISTS(SELECT 1 FROM @Rows r LEFT JOIN library.ModulePaths m ON m.Id=r.ModuleId WHERE m.Id IS NULL OR r.ModuleRevision<>m.Revision) THROW 50001,'Module list changed. Reload and try again.',1;
 IF EXISTS(SELECT 1 FROM @Rows r JOIN library.ModulePaths m ON m.Id=r.ModuleId WHERE
 ((r.IsRead=1 AND (m.IsRead=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsAdd=1 AND (m.IsAdd=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsEdit=1 AND (m.IsEdit=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsDelete=1 AND (m.IsDelete=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsExport=1 AND (m.IsExport=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsImport=1 AND (m.IsImport=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsApproval1=1 AND (m.IsApproval1=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)) OR (r.IsApproval2=1 AND (m.IsApproval2=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL)))
 OR (r.IsRead=0 AND (r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1))
 OR (m.ControllerName IN('Users','Settings','ModulePaths','UserAccess','PageInRole') AND (r.IsRead=1 OR r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1))
 OR (@Role='Member' AND ((m.ControllerName NOT IN('Books','Members','Loans') AND r.IsRead=1) OR (r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1)))) THROW 50001,'Rights exceed module capabilities or the user role.',1;
 IF @Revision=0 INSERT library.UserAccessProfiles(UserId,CreatedBy) VALUES(@UserId,@ActorId);
 ELSE UPDATE library.UserAccessProfiles SET Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId WHERE UserId=@UserId;
 UPDATE a SET IsRead=r.IsRead,IsAdd=r.IsAdd,IsEdit=r.IsEdit,IsDelete=r.IsDelete,IsExport=r.IsExport,IsImport=r.IsImport,IsApproval1=r.IsApproval1,IsApproval2=r.IsApproval2,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=NULL,DeletedBy=NULL FROM library.UserModuleAccess a JOIN @Rows r ON r.ModuleId=a.ModuleId WHERE a.UserId=@UserId;
 INSERT library.UserModuleAccess(UserId,ModuleId,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2,CreatedBy) SELECT @UserId,r.ModuleId,r.IsRead,r.IsAdd,r.IsEdit,r.IsDelete,r.IsExport,r.IsImport,r.IsApproval1,r.IsApproval2,@ActorId FROM @Rows r WHERE NOT EXISTS(SELECT 1 FROM library.UserModuleAccess a WHERE a.UserId=@UserId AND a.ModuleId=r.ModuleId);
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Module rights assigned',@UserId);
 COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.RoleAccess_Get @Role nvarchar(20) AS
BEGIN SET NOCOUNT ON;SELECT Role,Revision FROM library.RoleAccessProfiles WHERE Role=@Role AND DeletedDate IS NULL;
SELECT a.ModuleId,m.Revision ModuleRevision,a.IsRead,a.IsAdd,a.IsEdit,a.IsDelete,a.IsExport,a.IsImport,a.IsApproval1,a.IsApproval2 FROM library.RoleModuleAccess a JOIN library.ModulePaths m ON m.Id=a.ModuleId WHERE a.Role=@Role AND a.DeletedDate IS NULL;END;
GO
CREATE OR ALTER PROCEDURE library.RoleAccess_Save @Role nvarchar(20),@Revision int,@Grants nvarchar(max),@ActorId nvarchar(450) AS
BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRY BEGIN TRAN;DECLARE @Lock int;EXEC @Lock=sys.sp_getapplock @Resource='LibraryManagement.ModuleAccess',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;IF @Lock<0 THROW 50001,'Another permission update is running.',1;
 IF NOT EXISTS(SELECT 1 FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ActorId AND Role='Admin' AND IsActive=1 AND DeletedDate IS NULL) THROW 50001,'An active administrator is required.',1;
 IF @Role NOT IN('Librarian','Member') THROW 50001,'Choose a non-administrator role.',1;
 IF COALESCE((SELECT Revision FROM library.RoleAccessProfiles WITH(UPDLOCK,HOLDLOCK) WHERE Role=@Role),0)<>@Revision THROW 50001,'Role rights changed. Reload and try again.',1;
 IF ISJSON(@Grants)<>1 THROW 50001,'Invalid permission data.',1;
 DECLARE @Rows TABLE(ModuleId int PRIMARY KEY,ModuleRevision int,IsRead bit NOT NULL,IsAdd bit NOT NULL,IsEdit bit NOT NULL,IsDelete bit NOT NULL,IsExport bit NOT NULL,IsImport bit NOT NULL,IsApproval1 bit NOT NULL,IsApproval2 bit NOT NULL);
 INSERT @Rows SELECT ModuleId,ModuleRevision,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2 FROM OPENJSON(@Grants) WITH(ModuleId int,ModuleRevision int,IsRead bit,IsAdd bit,IsEdit bit,IsDelete bit,IsExport bit,IsImport bit,IsApproval1 bit,IsApproval2 bit);
 IF (SELECT COUNT(*) FROM @Rows)<>(SELECT COUNT(*) FROM library.ModulePaths) OR EXISTS(SELECT 1 FROM @Rows r LEFT JOIN library.ModulePaths m ON m.Id=r.ModuleId WHERE m.Id IS NULL OR r.ModuleRevision<>m.Revision) THROW 50001,'Module list changed. Reload and try again.',1;
 IF EXISTS(SELECT 1 FROM @Rows r JOIN library.ModulePaths m ON m.Id=r.ModuleId WHERE
 ((r.IsRead=1 AND (m.IsRead=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsAdd=1 AND (m.IsAdd=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsEdit=1 AND (m.IsEdit=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsDelete=1 AND (m.IsDelete=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsExport=1 AND (m.IsExport=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsImport=1 AND (m.IsImport=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsApproval1=1 AND (m.IsApproval1=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)) OR (r.IsApproval2=1 AND (m.IsApproval2=0 OR m.IsActive=0 OR m.DeletedDate IS NOT NULL OR m.IsLeafNode=0)))
 OR (r.IsRead=0 AND (r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1))
 OR (m.ControllerName IN('Users','Settings','ModulePaths','UserAccess','PageInRole') AND (r.IsRead=1 OR r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1))
 OR (@Role='Member' AND ((m.ControllerName NOT IN('Books','Members','Loans') AND r.IsRead=1) OR (r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1)))) THROW 50001,'Rights exceed module capabilities or role.',1;
 IF @Revision=0 INSERT library.RoleAccessProfiles(Role,CreatedBy) VALUES(@Role,@ActorId);
 ELSE UPDATE library.RoleAccessProfiles SET Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId WHERE Role=@Role;
 UPDATE a SET IsRead=r.IsRead,IsAdd=r.IsAdd,IsEdit=r.IsEdit,IsDelete=r.IsDelete,IsExport=r.IsExport,IsImport=r.IsImport,IsApproval1=r.IsApproval1,IsApproval2=r.IsApproval2,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId FROM library.RoleModuleAccess a JOIN @Rows r ON r.ModuleId=a.ModuleId WHERE a.Role=@Role;
 INSERT library.RoleModuleAccess(Role,ModuleId,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2,CreatedBy) SELECT @Role,r.ModuleId,r.IsRead,r.IsAdd,r.IsEdit,r.IsDelete,r.IsExport,r.IsImport,r.IsApproval1,r.IsApproval2,@ActorId FROM @Rows r WHERE NOT EXISTS(SELECT 1 FROM library.RoleModuleAccess a WHERE a.Role=@Role AND a.ModuleId=r.ModuleId);
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Role rights assigned',@Role);
 COMMIT;END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH;
END;
GO
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'LibraryManagement.SchemaVersion') EXEC sys.sp_updateextendedproperty @name=N'LibraryManagement.SchemaVersion',@value=15;
ELSE EXEC sys.sp_addextendedproperty @name=N'LibraryManagement.SchemaVersion',@value=15;
COMMIT;
GO
