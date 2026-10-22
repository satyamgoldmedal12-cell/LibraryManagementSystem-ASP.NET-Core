-- Apply after 001-003. Existing authentication cookies must sign in again after this upgrade.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
BEGIN TRAN;
IF OBJECT_ID(N'library.UserSessions',N'U') IS NULL
CREATE TABLE library.UserSessions(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY,
    UserId nvarchar(450) NOT NULL REFERENCES library.Users(Id),
    Device nvarchar(160) NOT NULL,
    IssuedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    LastSeenAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt datetime2 NOT NULL,
    RevokedAt datetime2 NULL
);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.UserSessions') AND name=N'IX_UserSessions_User')
CREATE INDEX IX_UserSessions_User ON library.UserSessions(UserId,ExpiresAt) INCLUDE(LastSeenAt,RevokedAt);
COMMIT;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Create @Id uniqueidentifier,@UserId nvarchar(450),@Device nvarchar(160),@AbsoluteHours int AS
BEGIN
 SET NOCOUNT ON;
 IF @AbsoluteHours NOT BETWEEN 1 AND 24 THROW 50001,'Invalid session policy.',1;
 INSERT library.UserSessions(Id,UserId,Device,ExpiresAt) VALUES(@Id,@UserId,@Device,DATEADD(hour,@AbsoluteHours,SYSUTCDATETIME()));
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Validate @Id uniqueidentifier,@UserId nvarchar(450),@IdleMinutes int AS
BEGIN
 SET NOCOUNT ON;
 IF @IdleMinutes NOT BETWEEN 1 AND 120 THROW 50001,'Invalid session policy.',1;
 UPDATE library.UserSessions SET LastSeenAt=SYSUTCDATETIME()
 WHERE Id=@Id AND UserId=@UserId AND RevokedAt IS NULL AND ExpiresAt>SYSUTCDATETIME()
 AND LastSeenAt>DATEADD(minute,-@IdleMinutes,SYSUTCDATETIME());
 SELECT @@ROWCOUNT;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Revoke @Id uniqueidentifier,@UserId nvarchar(450) AS
BEGIN SET NOCOUNT ON; UPDATE library.UserSessions SET RevokedAt=SYSUTCDATETIME() WHERE Id=@Id AND UserId=@UserId AND RevokedAt IS NULL; END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_RevokeAll @UserId nvarchar(450),@ExceptId uniqueidentifier=NULL AS
BEGIN SET NOCOUNT ON; UPDATE library.UserSessions SET RevokedAt=SYSUTCDATETIME() WHERE UserId=@UserId AND RevokedAt IS NULL AND (@ExceptId IS NULL OR Id<>@ExceptId); END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_List @UserId nvarchar(450),@IdleMinutes int AS
BEGIN
 SET NOCOUNT ON;
 SELECT Id,Device,IssuedAt,LastSeenAt,ExpiresAt FROM library.UserSessions
 WHERE UserId=@UserId AND RevokedAt IS NULL AND ExpiresAt>SYSUTCDATETIME()
 AND LastSeenAt>DATEADD(minute,-@IdleMinutes,SYSUTCDATETIME()) ORDER BY IssuedAt DESC;
END;
GO
