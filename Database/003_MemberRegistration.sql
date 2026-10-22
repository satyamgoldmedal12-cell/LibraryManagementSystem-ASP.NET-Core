-- Apply to the existing LibraryManagement database after 001 and 002. No table changes are required.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER PROCEDURE library.Users_RegisterMember
    @Id nvarchar(450), @UserName nvarchar(256), @NormalizedUserName nvarchar(256),
    @Email nvarchar(256), @NormalizedEmail nvarchar(256), @PasswordHash nvarchar(max),
    @SecurityStamp nvarchar(100), @ConcurrencyStamp nvarchar(100), @FullName nvarchar(120)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @MemberNumber varchar(30) = 'LIB-' + LEFT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),20);
        INSERT library.Members(MemberNumber,FullName,Email,ExpiresOn,IsActive)
        VALUES (@MemberNumber,@FullName,@Email,DATEADD(year,1,CONVERT(date,SYSUTCDATETIME())),1);
        DECLARE @MemberId int = SCOPE_IDENTITY();

        -- Public callers cannot supply Role, MemberId, expiry, confirmation, or lockout settings.
        INSERT library.Users(Id,UserName,NormalizedUserName,Email,NormalizedEmail,EmailConfirmed,
            PasswordHash,SecurityStamp,ConcurrencyStamp,LockoutEnabled,AccessFailedCount,FullName,Role,MemberId)
        VALUES (@Id,@UserName,@NormalizedUserName,@Email,@NormalizedEmail,0,
            @PasswordHash,@SecurityStamp,@ConcurrencyStamp,1,0,@FullName,'Member',@MemberId);
        INSERT library.AuditEvents(ActorId,Action,EntityId)
        VALUES (@Id,'Member registered',CONVERT(nvarchar(20),@MemberId));
        COMMIT;
        SELECT @MemberId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
