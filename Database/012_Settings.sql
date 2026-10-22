-- Apply after 001-011. Extends the existing singleton so old fine settings and new settings share one rate/revision.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
BEGIN TRAN;
IF COL_LENGTH('library.FinePolicy','LibraryName') IS NULL
BEGIN
 ALTER TABLE library.FinePolicy ADD
  LibraryName nvarchar(120) NOT NULL CONSTRAINT DF_FinePolicy_LibraryName DEFAULT N'Library Desk',
  Address nvarchar(500) NULL,
  Email nvarchar(256) NULL,
  Phone nvarchar(30) NULL,
  MaxActiveLoans int NULL CONSTRAINT CK_FinePolicy_MaxLoans CHECK(MaxActiveLoans BETWEEN 1 AND 100),
  DefaultIssueDays int NOT NULL CONSTRAINT DF_FinePolicy_IssueDays DEFAULT 14 CONSTRAINT CK_FinePolicy_IssueDays CHECK(DefaultIssueDays BETWEEN 1 AND 90),
  MembershipDurationMonths int NOT NULL CONSTRAINT DF_FinePolicy_MembershipMonths DEFAULT 12 CONSTRAINT CK_FinePolicy_MembershipMonths CHECK(MembershipDurationMonths BETWEEN 1 AND 120);
 EXEC(N'ALTER TABLE library.FinePolicy ADD CONSTRAINT CK_FinePolicy_LibraryName CHECK(LEN(LTRIM(RTRIM(LibraryName)))>0)');
END;
-- Null MaxActiveLoans and DailyFine preserve the existing server configuration until an Admin saves settings.
COMMIT;
GO
CREATE OR ALTER PROCEDURE library.Settings_Get @DefaultMaxLoans int,@DefaultFine decimal(10,2) AS
BEGIN
 SET NOCOUNT ON;
 SELECT p.LibraryName,p.Address,p.Email,p.Phone,COALESCE(p.MaxActiveLoans,@DefaultMaxLoans) MaxActiveLoans,
  p.DefaultIssueDays,COALESCE(p.DailyFine,@DefaultFine) DailyFine,p.MembershipDurationMonths,
  p.Revision,p.ModifiedAt,u.FullName ModifiedByName
 FROM library.FinePolicy p LEFT JOIN library.Users u ON u.Id=p.ModifiedBy WHERE p.Id=1;
END;
GO
CREATE OR ALTER PROCEDURE library.Settings_Save
 @LibraryName nvarchar(max),@Address nvarchar(max),@Email nvarchar(max),@Phone nvarchar(max),
 @MaxActiveLoans int,@DefaultIssueDays int,@DailyFine decimal(38,18),@MembershipDurationMonths int,
 @Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role='Admin' AND IsActive=1)
   THROW 50001,'Only an active administrator can change library settings.',1;
  SET @LibraryName=LTRIM(RTRIM(@LibraryName));
  SET @Address=NULLIF(LTRIM(RTRIM(@Address)),N'');
  SET @Email=NULLIF(LTRIM(RTRIM(@Email)),N'');
  SET @Phone=NULLIF(LTRIM(RTRIM(@Phone)),N'');
  IF @LibraryName IS NULL OR LEN(@LibraryName)=0 OR DATALENGTH(@LibraryName)>240 OR DATALENGTH(@Address)>1000 OR DATALENGTH(@Email)>512 OR DATALENGTH(@Phone)>60
   THROW 50001,'Check library information and field lengths.',1;
  IF @MaxActiveLoans IS NULL OR @MaxActiveLoans NOT BETWEEN 1 AND 100 OR @DefaultIssueDays IS NULL OR @DefaultIssueDays NOT BETWEEN 1 AND 90
   OR @MembershipDurationMonths IS NULL OR @MembershipDurationMonths NOT BETWEEN 1 AND 120
   THROW 50001,'Check the borrowing limit and issue/membership duration.',1;
  IF @DailyFine IS NULL OR @DailyFine<0 OR @DailyFine>1000 OR ROUND(@DailyFine,2)<>@DailyFine
   THROW 50001,'Fine per day must be between 0 and 1000 with at most two decimal places.',1;
  UPDATE library.FinePolicy SET LibraryName=@LibraryName,Address=@Address,Email=@Email,Phone=@Phone,
   MaxActiveLoans=@MaxActiveLoans,DefaultIssueDays=@DefaultIssueDays,DailyFine=@DailyFine,MembershipDurationMonths=@MembershipDurationMonths,
   Revision=Revision+1,CreatedBy=COALESCE(CreatedBy,@ActorId),ModifiedAt=SYSUTCDATETIME(),ModifiedBy=@ActorId
  WHERE Id=1 AND Revision=@Revision;
  IF @@ROWCOUNT=0 THROW 50001,'Library settings changed. Reload current settings before saving again.',1;
  INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Library settings updated','1');
  COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.FinePolicy_Save @DailyFine decimal(18,4),@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role='Admin' AND IsActive=1) THROW 50001,'Only an administrator can configure fines.',1;
  IF @DailyFine IS NULL OR @DailyFine<0 OR @DailyFine>1000 OR ROUND(@DailyFine,2)<>@DailyFine THROW 50001,'Fine per day must be between 0 and 1000 with at most two decimal places.',1;
  UPDATE library.FinePolicy SET DailyFine=@DailyFine,Revision=Revision+1,CreatedBy=COALESCE(CreatedBy,@ActorId),ModifiedAt=SYSUTCDATETIME(),ModifiedBy=@ActorId WHERE Id=1 AND Revision=@Revision;
  IF @@ROWCOUNT=0 THROW 50001,'Fine settings changed. Reload before saving again.',1;
  INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Fine policy updated','1');
  COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_IssueContext @BookId int,@MemberId int AS
BEGIN
 SET NOCOUNT ON;
 SELECT p.MaxActiveLoans ConfiguredMaxActiveLoans,p.DefaultIssueDays,p.DailyFine ConfiguredDailyFine,b.Id BookId,b.Title BookTitle,b.ISBN,COALESCE(b.TotalCopies,0) TotalCopies,
 COALESCE(b.TotalCopies,0)-(SELECT COUNT(*) FROM library.Loans WHERE BookId=@BookId AND ReturnedAt IS NULL) AvailableCopies,
 COALESCE(b.IsArchived,0) BookArchived,m.Id MemberId,m.FullName MemberName,m.MemberNumber,
 COALESCE(m.IsActive,0) MemberActive,COALESCE(m.IsArchived,0) MemberArchived,m.ExpiresOn MemberExpiresOn,
 (SELECT COUNT(*) FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL) ActiveLoans,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.Loans WHERE BookId=@BookId AND MemberId=@MemberId AND ReturnedAt IS NULL) THEN 1 ELSE 0 END AS bit) DuplicateActiveIssue,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL AND DueDate<CONVERT(date,SYSUTCDATETIME())) THEN 1 ELSE 0 END AS bit) HasOverdueLoans
 FROM library.FinePolicy p LEFT JOIN library.Books b ON b.Id=@BookId LEFT JOIN library.Members m ON m.Id=@MemberId WHERE p.Id=1;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_Issue @BookId int,@MemberId int,@DueDate date,@DailyFine decimal(10,2),@MaxLoans int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only staff can issue books.',1;
 SELECT @DailyFine=COALESCE(DailyFine,@DailyFine),@MaxLoans=COALESCE(MaxActiveLoans,@MaxLoans)
 FROM library.FinePolicy WITH(HOLDLOCK) WHERE Id=1;
 DECLARE @Today date=CONVERT(date,SYSUTCDATETIME()),@Expiry date,@Active bit,@MemberDeleted bit,@Copies int,@Archived bit;
 IF @DueDate<=@Today OR @DueDate>DATEADD(day,90,@Today) THROW 50001,'Due date must be within the next 90 days.',1;
 IF @MaxLoans IS NULL OR @DailyFine IS NULL OR @MaxLoans NOT BETWEEN 1 AND 100 OR @DailyFine NOT BETWEEN 0 AND 1000 THROW 50001,'Invalid lending policy.',1;
 -- Every circulation transaction locks member, then book, then loan rows.
 SELECT @Expiry=ExpiresOn,@Active=IsActive,@MemberDeleted=IsArchived FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId;
 IF @Active IS NULL OR @Active=0 OR @MemberDeleted=1 OR @Expiry<@Today THROW 50001,'Member is inactive, expired, or missing.',1;
 IF @DueDate>@Expiry THROW 50001,'Due date cannot exceed membership expiry.',1;
 SELECT @Copies=TotalCopies,@Archived=IsArchived FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@BookId;
 IF @Copies IS NULL OR @Archived=1 THROW 50001,'Book is unavailable.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND BookId=@BookId AND ReturnedAt IS NULL) THROW 50001,'Member already has this book.',1;
 IF (SELECT COUNT(*) FROM library.Loans WHERE BookId=@BookId AND ReturnedAt IS NULL)>=@Copies THROW 50001,'No copies are available.',1;
 IF (SELECT COUNT(*) FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL)>=@MaxLoans THROW 50001,'Member has reached the borrowing limit.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL AND DueDate<@Today) THROW 50001,'Return overdue books before borrowing again.',1;
 INSERT library.Loans(BookId,MemberId,IssuedAt,DueDate,DailyFine,IssuedBy) VALUES (@BookId,@MemberId,SYSUTCDATETIME(),@DueDate,@DailyFine,@ActorId);
 DECLARE @Id int=SCOPE_IDENTITY();
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Book issued',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
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
        DECLARE @MembershipMonths int;
        SELECT @MembershipMonths=MembershipDurationMonths FROM library.FinePolicy WITH(HOLDLOCK) WHERE Id=1;
        IF @MembershipMonths IS NULL THROW 50001,'Library settings are missing. Contact an administrator.',1;
        DECLARE @MemberNumber varchar(30) = 'LIB-' + LEFT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),20);
        INSERT library.Members(MemberNumber,FullName,Email,ExpiresOn,IsActive)
        VALUES (@MemberNumber,@FullName,@Email,DATEADD(month,@MembershipMonths,CONVERT(date,SYSUTCDATETIME())),1);
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
