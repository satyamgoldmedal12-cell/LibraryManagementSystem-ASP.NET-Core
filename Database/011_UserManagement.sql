SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
BEGIN TRAN;
IF COL_LENGTH('library.Users','IsActive') IS NULL
BEGIN
 ALTER TABLE library.Users ADD IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1;
 EXEC('UPDATE library.Users SET IsActive=0 WHERE LockoutEnd>''9999-01-01''');
END;
IF COL_LENGTH('library.Users','Revision') IS NULL ALTER TABLE library.Users ADD Revision int NOT NULL CONSTRAINT DF_Users_Revision DEFAULT 1;
IF COL_LENGTH('library.Users','CreatedBy') IS NULL ALTER TABLE library.Users ADD CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id);
IF COL_LENGTH('library.Users','ModifiedAt') IS NULL ALTER TABLE library.Users ADD ModifiedAt datetime2 NULL;
IF COL_LENGTH('library.Users','ModifiedBy') IS NULL ALTER TABLE library.Users ADD ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.AuditEvents') AND name='IX_AuditEvents_ActorTime')
 CREATE INDEX IX_AuditEvents_ActorTime ON library.AuditEvents(ActorId,OccurredAt DESC,Id DESC) INCLUDE(Action,EntityId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.AuditEvents') AND name='IX_AuditEvents_EntityTime')
 CREATE INDEX IX_AuditEvents_EntityTime ON library.AuditEvents(EntityId,OccurredAt DESC) INCLUDE(ActorId,Action);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Users') AND name='IX_Users_RoleActiveName')
 CREATE INDEX IX_Users_RoleActiveName ON library.Users(Role,IsActive,FullName) INCLUDE(Email,MemberId,LockoutEnd);
COMMIT;
GO
CREATE OR ALTER PROCEDURE library.Users_Create @Id nvarchar(450),@UserName nvarchar(256),@NormalizedUserName nvarchar(256),@Email nvarchar(256),@NormalizedEmail nvarchar(256),@EmailConfirmed bit,@PasswordHash nvarchar(max),@SecurityStamp nvarchar(100),@ConcurrencyStamp nvarchar(100),@LockoutEnd datetimeoffset=NULL,@LockoutEnabled bit,@AccessFailedCount int,@FullName nvarchar(120),@Role nvarchar(20),@MemberId int=NULL,@IsActive bit=1,@ActorId nvarchar(450)=NULL,@AuditAction nvarchar(80)=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF @ActorId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role='Admin' AND IsActive=1) THROW 50001,'An active administrator is required.',1;
 IF @Role='Member' AND NOT EXISTS(SELECT 1 FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId AND IsArchived=0 AND Email=@Email) THROW 50001,'Select a non-deleted member with matching email.',1;
 INSERT library.Users(Id,UserName,NormalizedUserName,Email,NormalizedEmail,EmailConfirmed,PasswordHash,SecurityStamp,ConcurrencyStamp,LockoutEnd,LockoutEnabled,AccessFailedCount,FullName,Role,MemberId,IsActive,CreatedBy)
 VALUES(@Id,@UserName,@NormalizedUserName,@Email,@NormalizedEmail,@EmailConfirmed,@PasswordHash,@SecurityStamp,@ConcurrencyStamp,@LockoutEnd,@LockoutEnabled,@AccessFailedCount,@FullName,@Role,@MemberId,@IsActive,@ActorId);
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(COALESCE(@ActorId,@Id),'User created',@Id);
 COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Users_Update @Id nvarchar(450),@UserName nvarchar(256),@NormalizedUserName nvarchar(256),@Email nvarchar(256),@NormalizedEmail nvarchar(256),@EmailConfirmed bit,@PasswordHash nvarchar(max),@SecurityStamp nvarchar(100),@ConcurrencyStamp nvarchar(100),@LockoutEnd datetimeoffset=NULL,@LockoutEnabled bit,@AccessFailedCount int,@FullName nvarchar(120),@Role nvarchar(20),@MemberId int=NULL,@PreviousStamp nvarchar(100),@IsActive bit=1,@ActorId nvarchar(450)=NULL,@AuditAction nvarchar(80)=NULL,@ExpectedRevision int=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @LockResult int,@CurrentRole nvarchar(20),@CurrentActive bit,@CurrentStamp nvarchar(100),@CurrentRevision int;
 SELECT @CurrentRole=Role FROM library.Users WHERE Id=@Id;
 -- Serialize administrative access changes so concurrent demotions cannot remove the last active Admin.
 IF @CurrentRole='Admin' OR @Role='Admin'
 BEGIN
  EXEC @LockResult=sys.sp_getapplock @Resource='LibraryManagement.AdminAccounts',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
  IF @LockResult<0 THROW 50001,'Another administrator is updating accounts. Try again.',1;
 END;
 IF @Role='Member' AND NOT EXISTS(SELECT 1 FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId AND IsArchived=0 AND Email=@Email) THROW 50001,'Select a non-deleted member with matching email.',1;
 SELECT @CurrentRole=Role,@CurrentActive=IsActive,@CurrentStamp=ConcurrencyStamp,@CurrentRevision=Revision FROM library.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentStamp IS NULL OR @CurrentStamp<>@PreviousStamp OR (@ExpectedRevision IS NOT NULL AND @ExpectedRevision<>@CurrentRevision)
 BEGIN COMMIT;SELECT 0;RETURN;END;
 IF @AuditAction IN('User edited','User enabled','User disabled','Password reset by admin') AND (@ActorId IS NULL OR NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role='Admin' AND IsActive=1)) THROW 50001,'An active administrator is required.',1;
 IF @ActorId=@Id AND (@Role<>@CurrentRole OR @IsActive=0) THROW 50001,'You cannot change your own role or deactivate your own account.',1;
 IF @CurrentRole='Admin' AND @CurrentActive=1 AND (@Role<>'Admin' OR @IsActive=0) AND NOT EXISTS(SELECT 1 FROM library.Users WHERE Role='Admin' AND IsActive=1 AND Id<>@Id) THROW 50001,'At least one active administrator must remain.',1;
 UPDATE library.Users SET UserName=@UserName,NormalizedUserName=@NormalizedUserName,Email=@Email,NormalizedEmail=@NormalizedEmail,EmailConfirmed=@EmailConfirmed,PasswordHash=@PasswordHash,SecurityStamp=@SecurityStamp,ConcurrencyStamp=@ConcurrencyStamp,LockoutEnd=@LockoutEnd,LockoutEnabled=@LockoutEnabled,AccessFailedCount=@AccessFailedCount,FullName=@FullName,Role=@Role,MemberId=@MemberId,IsActive=@IsActive,Revision=Revision+1,
 ModifiedAt=CASE WHEN @AuditAction IS NOT NULL THEN SYSUTCDATETIME() ELSE ModifiedAt END,ModifiedBy=CASE WHEN @AuditAction IS NOT NULL THEN COALESCE(@ActorId,@Id) ELSE ModifiedBy END WHERE Id=@Id;
 IF @AuditAction IS NOT NULL
 BEGIN
  INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(COALESCE(@ActorId,@Id),@AuditAction,@Id);
  UPDATE library.UserSessions SET RevokedAt=SYSUTCDATETIME() WHERE UserId=@Id AND RevokedAt IS NULL;
 END;
 COMMIT;SELECT 1;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Users_List AS
BEGIN SET NOCOUNT ON;SELECT TOP(200) Id,FullName,Email,Role,MemberId,LockoutEnd,IsActive,Revision,CreatedAt,ModifiedAt FROM library.Users ORDER BY FullName,Id;END;
GO
CREATE OR ALTER PROCEDURE library.Users_Summary @Id nvarchar(450) AS
BEGIN SET NOCOUNT ON;SELECT Id,FullName,Email,Role,MemberId,LockoutEnd,IsActive,Revision,CreatedAt,ModifiedAt FROM library.Users WHERE Id=@Id;END;
GO
CREATE OR ALTER PROCEDURE library.Users_Page @Search nvarchar(200)=NULL,@Role varchar(20)='all',@Status varchar(20)='all',@Page int=1 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page NOT BETWEEN 1 AND 100000 OR @Role NOT IN('all','Admin','Librarian','Member') OR @Status NOT IN('all','active','inactive','locked') THROW 50001,'Invalid user filter.',1;
 SELECT Id,FullName,Email,Role,MemberId,LockoutEnd,IsActive,Revision,CreatedAt,ModifiedAt INTO #Users FROM library.Users
 WHERE (@Search IS NULL OR FullName LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%') AND (@Role='all' OR Role=@Role)
 AND (@Status='all' OR (@Status='active' AND IsActive=1) OR (@Status='inactive' AND IsActive=0) OR (@Status='locked' AND IsActive=1 AND LockoutEnd>SYSUTCDATETIME()));
 SELECT * FROM #Users ORDER BY FullName,Id OFFSET (@Page-1)*20 ROWS FETCH NEXT 20 ROWS ONLY;SELECT COUNT(*) FROM #Users;
END;
GO
CREATE OR ALTER PROCEDURE library.UserActivity_Add @ActorId nvarchar(450),@Action nvarchar(80) AS
BEGIN
 SET NOCOUNT ON;
 IF @Action NOT IN('Signed in','Signed out') THROW 50001,'Invalid authentication event.',1;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,@Action,@ActorId);
END;
GO
CREATE OR ALTER PROCEDURE library.UserActivity_Page @UserId nvarchar(450)=NULL,@Action nvarchar(80)=NULL,@FromDate date=NULL,@ToDate date=NULL,@Page int=1 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page NOT BETWEEN 1 AND 100000 OR @FromDate>@ToDate THROW 50001,'Invalid activity filters.',1;
 SELECT e.Id,e.ActorId,a.FullName ActorName,e.Action,e.EntityId,s.FullName SubjectName,e.OccurredAt INTO #Activity
 FROM library.AuditEvents e JOIN library.Users a ON a.Id=e.ActorId
 LEFT JOIN library.Users s ON s.Id=e.EntityId AND e.Action IN('User created','User edited','User enabled','User disabled','Password reset by admin','Password changed','Password reset','Signed in','Signed out')
 WHERE (@UserId IS NULL OR e.ActorId=@UserId OR s.Id=@UserId) AND (@Action IS NULL OR e.Action=@Action)
 AND (@FromDate IS NULL OR e.OccurredAt>=@FromDate) AND (@ToDate IS NULL OR e.OccurredAt<DATEADD(day,1,CONVERT(datetime2,@ToDate)));
 SELECT * FROM #Activity ORDER BY OccurredAt DESC,Id DESC OFFSET (@Page-1)*20 ROWS FETCH NEXT 20 ROWS ONLY;SELECT COUNT(*) FROM #Activity;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Create @Id uniqueidentifier,@UserId nvarchar(450),@Device nvarchar(160),@AbsoluteHours int AS
BEGIN
 SET NOCOUNT ON;
 IF @AbsoluteHours NOT BETWEEN 1 AND 24 THROW 50001,'Invalid session policy.',1;
 INSERT library.UserSessions(Id,UserId,Device,ExpiresAt)
 SELECT @Id,u.Id,@Device,DATEADD(hour,@AbsoluteHours,SYSUTCDATETIME()) FROM library.Users u
 LEFT JOIN library.Members m WITH(HOLDLOCK) ON m.Id=u.MemberId
 WHERE u.Id=@UserId AND u.IsActive=1 AND (u.LockoutEnd IS NULL OR u.LockoutEnd<=SYSUTCDATETIME()) AND (u.Role<>'Member' OR (m.Id IS NOT NULL AND m.IsArchived=0));
 IF @@ROWCOUNT=0 THROW 50001,'Account unavailable.',1;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Validate @Id uniqueidentifier,@UserId nvarchar(450),@IdleMinutes int AS
BEGIN
 SET NOCOUNT ON;
 IF @IdleMinutes NOT BETWEEN 1 AND 120 THROW 50001,'Invalid session policy.',1;
 UPDATE s SET LastSeenAt=SYSUTCDATETIME() FROM library.UserSessions s JOIN library.Users u ON u.Id=s.UserId LEFT JOIN library.Members m ON m.Id=u.MemberId
 WHERE s.Id=@Id AND s.UserId=@UserId AND s.RevokedAt IS NULL AND s.ExpiresAt>SYSUTCDATETIME() AND s.LastSeenAt>DATEADD(minute,-@IdleMinutes,SYSUTCDATETIME()) AND u.IsActive=1 AND (u.Role<>'Member' OR (m.Id IS NOT NULL AND m.IsArchived=0));
 SELECT @@ROWCOUNT;
END;
GO
