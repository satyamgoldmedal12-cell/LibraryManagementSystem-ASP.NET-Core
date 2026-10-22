-- 014: MVC module registry and per-user permissions. Run with sqlcmd -b -I after 013.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRAN;
GO
IF OBJECT_ID('library.ModulePaths','U') IS NULL
CREATE TABLE library.ModulePaths(Id int IDENTITY CONSTRAINT PK_ModulePaths PRIMARY KEY,DisplayName nvarchar(80) NOT NULL,ControllerName nvarchar(80) NOT NULL CONSTRAINT UQ_ModulePaths_Controller UNIQUE,SortOrder int NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,Revision int NOT NULL DEFAULT 1,IsRead bit NOT NULL DEFAULT 0,IsAdd bit NOT NULL DEFAULT 0,IsEdit bit NOT NULL DEFAULT 0,IsDelete bit NOT NULL DEFAULT 0,IsExport bit NOT NULL DEFAULT 0,IsImport bit NOT NULL DEFAULT 0,IsApproval1 bit NOT NULL DEFAULT 0,IsApproval2 bit NOT NULL DEFAULT 0,CreatedDate datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id),CONSTRAINT CK_ModulePaths_Read CHECK(IsRead=1 OR (IsAdd=0 AND IsEdit=0 AND IsDelete=0 AND IsExport=0 AND IsImport=0 AND IsApproval1=0 AND IsApproval2=0)),CONSTRAINT CK_ModulePaths_Order CHECK(SortOrder BETWEEN 0 AND 9999),CONSTRAINT CK_ModulePaths_ActiveRead CHECK(IsActive=0 OR IsRead=1));
IF OBJECT_ID('library.UserAccessProfiles','U') IS NULL
CREATE TABLE library.UserAccessProfiles(UserId nvarchar(450) CONSTRAINT PK_UserAccessProfiles PRIMARY KEY REFERENCES library.Users(Id),Revision int NOT NULL DEFAULT 1,CreatedDate datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id));
IF OBJECT_ID('library.UserModuleAccess','U') IS NULL
CREATE TABLE library.UserModuleAccess(UserId nvarchar(450) NOT NULL REFERENCES library.UserAccessProfiles(UserId),ModuleId int NOT NULL REFERENCES library.ModulePaths(Id),IsRead bit NOT NULL DEFAULT 0,IsAdd bit NOT NULL DEFAULT 0,IsEdit bit NOT NULL DEFAULT 0,IsDelete bit NOT NULL DEFAULT 0,IsExport bit NOT NULL DEFAULT 0,IsImport bit NOT NULL DEFAULT 0,IsApproval1 bit NOT NULL DEFAULT 0,IsApproval2 bit NOT NULL DEFAULT 0,CreatedDate datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id),CONSTRAINT PK_UserModuleAccess PRIMARY KEY NONCLUSTERED(UserId,ModuleId),CONSTRAINT CK_UserModuleAccess_Read CHECK(IsRead=1 OR (IsAdd=0 AND IsEdit=0 AND IsDelete=0 AND IsExport=0 AND IsImport=0 AND IsApproval1=0 AND IsApproval2=0)));
GO
IF OBJECT_ID('library.CK_ModulePaths_ActiveRead','C') IS NULL ALTER TABLE library.ModulePaths WITH CHECK ADD CONSTRAINT CK_ModulePaths_ActiveRead CHECK(IsActive=0 OR IsRead=1);
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.UserModuleAccess') AND name='PK_UserModuleAccess' AND type=1)
BEGIN ALTER TABLE library.UserModuleAccess DROP CONSTRAINT PK_UserModuleAccess; ALTER TABLE library.UserModuleAccess ADD CONSTRAINT PK_UserModuleAccess PRIMARY KEY NONCLUSTERED(UserId,ModuleId); END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.ModulePaths') AND name='IX_ModulePaths_Menu') CREATE INDEX IX_ModulePaths_Menu ON library.ModulePaths(IsActive,SortOrder) INCLUDE(DisplayName,ControllerName,DeletedDate);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.UserModuleAccess') AND name='IX_UserModuleAccess_Module') CREATE INDEX IX_UserModuleAccess_Module ON library.UserModuleAccess(ModuleId) INCLUDE(UserId,IsRead);
GO
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Home') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Overview','Home',0,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Books') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Book catalog','Books',10,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Members') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Members','Members',20,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Loans') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Circulation','Loans',30,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Returns') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Returns','Returns',40,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Fines') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Fines & collections','Fines',50,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Reports') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Reports','Reports',60,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Categories') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Categories','Categories',70,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Users') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'User accounts','Users',80,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='Settings') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Library settings','Settings',90,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='ModulePaths') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'Module paths','ModulePaths',100,1,1,1,1,1,1,1,1);
IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName='UserAccess') INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2) VALUES(N'User access','UserAccess',110,1,1,1,1,1,1,1,1);
GO
CREATE OR ALTER PROCEDURE library.Modules_List AS
BEGIN SET NOCOUNT ON;SELECT * FROM library.ModulePaths ORDER BY SortOrder,DisplayName,Id;END;
GO
CREATE OR ALTER PROCEDURE library.Modules_Save @Id int,@DisplayName nvarchar(80),@ControllerName nvarchar(80),@SortOrder int,@IsActive bit,@Revision int,@IsRead bit,@IsAdd bit,@IsEdit bit,@IsDelete bit,@IsExport bit,@IsImport bit,@IsApproval1 bit,@IsApproval2 bit,@ActorId nvarchar(450) AS
BEGIN SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRY BEGIN TRAN; DECLARE @Lock int; EXEC @Lock=sys.sp_getapplock @Resource='LibraryManagement.ModuleAccess',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @Lock<0 THROW 50001,'Another permission update is running. Try again.',1; IF NOT EXISTS(SELECT 1 FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ActorId AND Role='Admin' AND IsActive=1 AND DeletedDate IS NULL) THROW 50001,'An active administrator is required.',1;
 IF LEN(LTRIM(RTRIM(@DisplayName)))=0 OR @SortOrder NOT BETWEEN 0 AND 9999 OR @ControllerName NOT IN('Home','Books','Members','Loans','Returns','Fines','Reports','Categories','Users','Settings','ModulePaths','UserAccess') THROW 50001,'Invalid module details.',1;
 IF @ControllerName IN('ModulePaths','UserAccess') AND (@IsActive=0 OR @IsRead=0) THROW 50001,'Access management must remain available to administrators.',1;
 IF @Id=0 BEGIN
  IF EXISTS(SELECT 1 FROM library.ModulePaths WHERE ControllerName=@ControllerName) THROW 50001,'Controller already registered. Use Edit or restore its existing entry.',1;
  INSERT library.ModulePaths(DisplayName,ControllerName,SortOrder,IsActive,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2,CreatedBy) VALUES(@DisplayName,@ControllerName,@SortOrder,@IsActive,@IsRead,@IsAdd,@IsEdit,@IsDelete,@IsExport,@IsImport,@IsApproval1,@IsApproval2,@ActorId); SET @Id=SCOPE_IDENTITY();
 END ELSE BEGIN
  IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@Id AND Revision=@Revision) THROW 50001,'Module changed. Reload and try again.',1;
  IF NOT EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@Id AND ControllerName=@ControllerName) THROW 50001,'A registered controller cannot be renamed. Create a separate module instead.',1;
  UPDATE library.ModulePaths SET DisplayName=@DisplayName,SortOrder=@SortOrder,IsActive=@IsActive,IsRead=@IsRead,IsAdd=@IsAdd,IsEdit=@IsEdit,IsDelete=@IsDelete,IsExport=@IsExport,IsImport=@IsImport,IsApproval1=@IsApproval1,IsApproval2=@IsApproval2,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=NULL,DeletedBy=NULL WHERE Id=@Id;
 END;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Module saved',CONVERT(nvarchar(20),@Id));
 COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH; SELECT @Id;
END;
GO
CREATE OR ALTER PROCEDURE library.Modules_Delete @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRY BEGIN TRAN; DECLARE @Lock int; EXEC @Lock=sys.sp_getapplock @Resource='LibraryManagement.ModuleAccess',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @Lock<0 THROW 50001,'Another permission update is running. Try again.',1; IF NOT EXISTS(SELECT 1 FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ActorId AND Role='Admin' AND IsActive=1 AND DeletedDate IS NULL) THROW 50001,'An active administrator is required.',1;
 IF EXISTS(SELECT 1 FROM library.ModulePaths WHERE Id=@Id AND ControllerName IN('ModulePaths','UserAccess')) THROW 50001,'Access management modules cannot be deleted.',1;
 UPDATE library.ModulePaths SET IsActive=0,DeletedDate=SYSUTCDATETIME(),DeletedBy=@ActorId,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,Revision=Revision+1 WHERE Id=@Id AND Revision=@Revision AND DeletedDate IS NULL;
 IF @@ROWCOUNT<>1 THROW 50001,'Module changed. Reload and try again.',1;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Module deleted',CONVERT(nvarchar(20),@Id));
 COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.ModuleAccess_Get @UserId nvarchar(450) AS
BEGIN SET NOCOUNT ON;
 SELECT u.Id UserId,u.FullName,u.Email,u.Role,u.IsActive,u.Revision UserRevision,COALESCE(p.Revision,0) Revision FROM library.Users u LEFT JOIN library.UserAccessProfiles p ON p.UserId=u.Id WHERE u.Id=@UserId AND u.DeletedDate IS NULL;
 SELECT a.ModuleId,m.Revision ModuleRevision,a.IsRead,a.IsAdd,a.IsEdit,a.IsDelete,a.IsExport,a.IsImport,a.IsApproval1,a.IsApproval2 FROM library.UserModuleAccess a JOIN library.ModulePaths m ON m.Id=a.ModuleId WHERE a.UserId=@UserId AND a.DeletedDate IS NULL;
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
 OR (m.ControllerName IN('Users','Settings','ModulePaths','UserAccess') AND (r.IsRead=1 OR r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1))
 OR (@Role='Member' AND ((m.ControllerName NOT IN('Books','Members','Loans') AND r.IsRead=1) OR (r.IsAdd=1 OR r.IsEdit=1 OR r.IsDelete=1 OR r.IsExport=1 OR r.IsImport=1 OR r.IsApproval1=1 OR r.IsApproval2=1)))) THROW 50001,'Rights exceed module capabilities or the user role.',1;
 IF @Revision=0 INSERT library.UserAccessProfiles(UserId,CreatedBy) VALUES(@UserId,@ActorId);
 ELSE UPDATE library.UserAccessProfiles SET Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId WHERE UserId=@UserId;
 UPDATE a SET IsRead=r.IsRead,IsAdd=r.IsAdd,IsEdit=r.IsEdit,IsDelete=r.IsDelete,IsExport=r.IsExport,IsImport=r.IsImport,IsApproval1=r.IsApproval1,IsApproval2=r.IsApproval2,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=NULL,DeletedBy=NULL FROM library.UserModuleAccess a JOIN @Rows r ON r.ModuleId=a.ModuleId WHERE a.UserId=@UserId;
 INSERT library.UserModuleAccess(UserId,ModuleId,IsRead,IsAdd,IsEdit,IsDelete,IsExport,IsImport,IsApproval1,IsApproval2,CreatedBy) SELECT @UserId,r.ModuleId,r.IsRead,r.IsAdd,r.IsEdit,r.IsDelete,r.IsExport,r.IsImport,r.IsApproval1,r.IsApproval2,@ActorId FROM @Rows r WHERE NOT EXISTS(SELECT 1 FROM library.UserModuleAccess a WHERE a.UserId=@UserId AND a.ModuleId=r.ModuleId);
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Module rights assigned',@UserId);
 COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'LibraryManagement.SchemaVersion') EXEC sys.sp_updateextendedproperty @name=N'LibraryManagement.SchemaVersion',@value=14;
ELSE EXEC sys.sp_addextendedproperty @name=N'LibraryManagement.SchemaVersion',@value=14;
GO
COMMIT;
