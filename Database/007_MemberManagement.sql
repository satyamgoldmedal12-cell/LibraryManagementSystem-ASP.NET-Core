-- Apply after 001 through 006. Legacy membership dates stay NULL until recorded.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
IF COL_LENGTH('library.Members','Address') IS NULL ALTER TABLE library.Members ADD Address nvarchar(500) NULL;
IF COL_LENGTH('library.Members','JoinedOn') IS NULL ALTER TABLE library.Members ADD JoinedOn date NULL CONSTRAINT DF_Members_JoinedOn DEFAULT(CONVERT(date,SYSUTCDATETIME()));
IF COL_LENGTH('library.Members','IsArchived') IS NULL ALTER TABLE library.Members ADD IsArchived bit NOT NULL CONSTRAINT DF_Members_IsArchived DEFAULT(0) WITH VALUES;
GO
IF OBJECT_ID('library.CK_Members_Dates','C') IS NULL ALTER TABLE library.Members ADD CONSTRAINT CK_Members_Dates CHECK(JoinedOn IS NULL OR JoinedOn<=ExpiresOn);
GO
CREATE OR ALTER PROCEDURE library.Members_List @Search nvarchar(200)=NULL,@Page int=1,@PageSize int=12,@IncludeArchived bit=0,@Status varchar(12)='all' AS
BEGIN
 SET NOCOUNT ON;
 IF @Page<1 OR @Page>100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 IF @Status NOT IN('all','active','inactive','expired') THROW 50001,'Invalid member status.',1;
 DECLARE @Today date=CONVERT(date,SYSUTCDATETIME());
 DECLARE @Pattern nvarchar(810)=N'%'+REPLACE(REPLACE(REPLACE(@Search,N'[',N'[[]'),N'%',N'[%]'),N'_',N'[_]')+N'%';
 SELECT Id INTO #Matches FROM library.Members
 WHERE (@IncludeArchived=1 OR IsArchived=0)
 AND (@Status='all' OR (@Status='active' AND IsArchived=0 AND IsActive=1 AND ExpiresOn>=@Today)
 OR (@Status='inactive' AND IsArchived=0 AND IsActive=0) OR (@Status='expired' AND IsArchived=0 AND IsActive=1 AND ExpiresOn<@Today))
 AND (@Search IS NULL OR FullName LIKE @Pattern OR MemberNumber LIKE @Pattern OR Email LIKE @Pattern OR Phone LIKE @Pattern OR Address LIKE @Pattern);
 SELECT m.* FROM library.Members m JOIN #Matches x ON x.Id=m.Id ORDER BY m.FullName,m.Id OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM #Matches;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Save @Id int=0,@MemberNumber varchar(30),@FullName nvarchar(120),@Email nvarchar(256),
 @Phone nvarchar(25)=NULL,@ExpiresOn date,@IsActive bit=1,@Revision int=0,@ActorId nvarchar(450),@Address nvarchar(500)=NULL,@JoinedOn date=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @Today date=CONVERT(date,SYSUTCDATETIME());
 IF @Id=0 AND @JoinedOn IS NULL SET @JoinedOn=@Today;
 IF @JoinedOn>@Today OR @JoinedOn>@ExpiresOn THROW 50001,'Check membership and expiry dates.',1;
 BEGIN TRY
 BEGIN TRAN;
 IF @Id=0
 BEGIN
  INSERT library.Members(MemberNumber,FullName,Email,Phone,Address,JoinedOn,ExpiresOn,IsActive)
  VALUES(@MemberNumber,@FullName,@Email,@Phone,@Address,@JoinedOn,@ExpiresOn,@IsActive);
  SET @Id=SCOPE_IDENTITY();
 END
 ELSE
 BEGIN
  DECLARE @CurrentRevision int,@Archived bit,@CurrentJoinedOn date;
  SELECT @CurrentRevision=Revision,@Archived=IsArchived,@CurrentJoinedOn=JoinedOn FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
  IF @JoinedOn IS NULL SET @JoinedOn=@CurrentJoinedOn;
  IF @JoinedOn>@ExpiresOn THROW 50001,'Membership expiry cannot be before the membership date.',1;
  IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This member changed. Reload and try again.',1;
  IF @Archived=1 THROW 50001,'Restore this deleted member before editing.',1;
  IF EXISTS(SELECT 1 FROM library.Users WHERE MemberId=@Id AND NormalizedEmail<>UPPER(LTRIM(RTRIM(@Email))))
   THROW 50001,'Keep the email registered to the linked login account. Account email changes require a separate verified workflow.',1;
  UPDATE library.Members SET MemberNumber=@MemberNumber,FullName=@FullName,Email=@Email,Phone=@Phone,Address=@Address,
   JoinedOn=@JoinedOn,ExpiresOn=@ExpiresOn,IsActive=@IsActive,Revision=Revision+1 WHERE Id=@Id;
 END;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Member saved',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Statistics @Id int AS
BEGIN
 SET NOCOUNT ON;
 SELECT COUNT(CASE WHEN l.ReturnedAt IS NULL THEN 1 END) ActiveLoans,
  COUNT(CASE WHEN l.ReturnedAt IS NOT NULL THEN 1 END) ReturnedLoans,
  COUNT(CASE WHEN l.ReturnedAt IS NULL AND l.DueDate<CONVERT(date,SYSUTCDATETIME()) THEN 1 END) OverdueLoans,
  COALESCE(SUM(CASE WHEN l.ReturnedAt IS NOT NULL AND p.Id IS NULL THEN l.FineAmount ELSE 0 END),0) UnpaidFines,
  COALESCE(SUM(p.Amount),0) PaidFines
 FROM library.Loans l LEFT JOIN library.Payments p ON p.LoanId=l.Id WHERE l.MemberId=@Id;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Delete @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @CurrentRevision int,@Archived bit;
 SELECT @CurrentRevision=Revision,@Archived=IsArchived FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This member changed. Reload and try again.',1;
 IF @Archived=1 THROW 50001,'This member is already deleted.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@Id AND ReturnedAt IS NULL)
  THROW 50001,'Return outstanding books before deleting this member.',1;
 UPDATE library.Members SET IsArchived=1,IsActive=0,Revision=Revision+1 WHERE Id=@Id;
 UPDATE library.Users SET SecurityStamp=CONVERT(nvarchar(100),NEWID()),ConcurrencyStamp=CONVERT(nvarchar(100),NEWID()) WHERE MemberId=@Id;
 UPDATE s SET RevokedAt=SYSUTCDATETIME() FROM library.UserSessions s JOIN library.Users u ON u.Id=s.UserId WHERE u.MemberId=@Id AND s.RevokedAt IS NULL;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Member deleted (archived)',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Restore @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @CurrentRevision int,@Archived bit;
 SELECT @CurrentRevision=Revision,@Archived=IsArchived FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This member changed. Reload and try again.',1;
 IF @Archived=0 THROW 50001,'This member is not deleted.',1;
 UPDATE library.Members SET IsArchived=0,IsActive=0,Revision=Revision+1 WHERE Id=@Id;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Member restored',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Create @Id uniqueidentifier,@UserId nvarchar(450),@Device nvarchar(160),@AbsoluteHours int AS
BEGIN
 SET NOCOUNT ON;
 IF @AbsoluteHours NOT BETWEEN 1 AND 24 THROW 50001,'Invalid session policy.',1;
 INSERT library.UserSessions(Id,UserId,Device,ExpiresAt)
 SELECT @Id,u.Id,@Device,DATEADD(hour,@AbsoluteHours,SYSUTCDATETIME()) FROM library.Users u
 LEFT JOIN library.Members m WITH(HOLDLOCK) ON m.Id=u.MemberId
 WHERE u.Id=@UserId AND (u.Role<>'Member' OR (m.Id IS NOT NULL AND m.IsArchived=0));
 IF @@ROWCOUNT=0 THROW 50001,'Account unavailable.',1;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Validate @Id uniqueidentifier,@UserId nvarchar(450),@IdleMinutes int AS
BEGIN
 SET NOCOUNT ON;
 IF @IdleMinutes NOT BETWEEN 1 AND 120 THROW 50001,'Invalid session policy.',1;
 UPDATE s SET LastSeenAt=SYSUTCDATETIME() FROM library.UserSessions s JOIN library.Users u ON u.Id=s.UserId
 LEFT JOIN library.Members m ON m.Id=u.MemberId
 WHERE s.Id=@Id AND s.UserId=@UserId AND s.RevokedAt IS NULL AND s.ExpiresAt>SYSUTCDATETIME()
 AND s.LastSeenAt>DATEADD(minute,-@IdleMinutes,SYSUTCDATETIME()) AND (u.Role<>'Member' OR (m.Id IS NOT NULL AND m.IsArchived=0));
 SELECT @@ROWCOUNT;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_Issue @BookId int,@MemberId int,@DueDate date,@DailyFine decimal(10,2),@MaxLoans int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Today date=CONVERT(date,SYSUTCDATETIME()),@Expiry date,@Active bit,@MemberDeleted bit,@Copies int,@Archived bit;
 IF @DueDate<=@Today OR @DueDate>DATEADD(day,90,@Today) THROW 50001,'Due date must be within the next 90 days.',1;
 IF @MaxLoans NOT BETWEEN 1 AND 100 OR @DailyFine NOT BETWEEN 0 AND 1000 THROW 50001,'Invalid lending policy.',1;
 -- Every circulation transaction locks member, then book, then loan rows.
 SELECT @Expiry=ExpiresOn,@Active=IsActive,@MemberDeleted=IsArchived FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId;
 IF @Active IS NULL OR @Active=0 OR @MemberDeleted=1 OR @Expiry<@Today THROW 50001,'Member is inactive, expired, or missing.',1;
 IF @DueDate>@Expiry THROW 50001,'Due date cannot exceed membership expiry.',1;
 SELECT @Copies=TotalCopies,@Archived=IsArchived FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@BookId;
 IF @Copies IS NULL OR @Archived=1 THROW 50001,'Book is unavailable.',1;
 IF (SELECT COUNT(*) FROM library.Loans WHERE BookId=@BookId AND ReturnedAt IS NULL)>=@Copies THROW 50001,'No copies are available.',1;
 IF (SELECT COUNT(*) FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL)>=@MaxLoans THROW 50001,'Member has reached the borrowing limit.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND BookId=@BookId AND ReturnedAt IS NULL) THROW 50001,'Member already has this book.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL AND DueDate<@Today) THROW 50001,'Return overdue books before borrowing again.',1;
 INSERT library.Loans(BookId,MemberId,DueDate,DailyFine,IssuedBy) VALUES (@BookId,@MemberId,@DueDate,@DailyFine,@ActorId);
 DECLARE @Id int=SCOPE_IDENTITY();
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Book issued',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Dashboard_Get AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Now datetime2=SYSUTCDATETIME(),@Today date=CONVERT(date,SYSUTCDATETIME());
    DECLARE @CurrentMonth date=DATEFROMPARTS(YEAR(@Now),MONTH(@Now),1);
    DECLARE @FirstMonth date=DATEADD(month,-5,@CurrentMonth);
    DECLARE @TotalMembers bigint,@ActiveMembers bigint,@BookTitles bigint,@TotalBooks bigint,
        @IssuedBooks bigint,@ReturnedBooks bigint,@OverdueBooks bigint,@Fine decimal(19,2);

    BEGIN TRY
        BEGIN TRAN;
        -- Short shared range locks provide consistent cards and chart rows in one request.
        -- Read in the same member -> book -> loan order used by circulation operations.
        SELECT @TotalMembers=COUNT_BIG(*),
            @ActiveMembers=COALESCE(SUM(CONVERT(bigint,CASE WHEN IsActive=1 AND ExpiresOn>=@Today THEN 1 ELSE 0 END)),0)
        FROM library.Members WITH(HOLDLOCK) WHERE IsArchived=0;
        SELECT @BookTitles=COUNT_BIG(*),@TotalBooks=COALESCE(SUM(CONVERT(bigint,TotalCopies)),0)
        FROM library.Books WITH(HOLDLOCK) WHERE IsArchived=0;
        SELECT @IssuedBooks=COUNT_BIG(*),
            @OverdueBooks=COALESCE(SUM(CONVERT(bigint,CASE WHEN l.DueDate<@Today THEN 1 ELSE 0 END)),0)
        FROM library.Loans l WITH(HOLDLOCK) JOIN library.Books b ON b.Id=l.BookId
        WHERE l.ReturnedAt IS NULL AND b.IsArchived=0;
        SELECT @ReturnedBooks=COUNT_BIG(*) FROM library.Loans WITH(HOLDLOCK) WHERE ReturnedAt IS NOT NULL;
        SELECT @Fine=COALESCE(SUM(CONVERT(decimal(19,2),Amount)),0) FROM library.Payments WITH(HOLDLOCK);

        ;WITH Months AS (
            SELECT DATEADD(month,n,@FirstMonth) MonthStart FROM (VALUES(0),(1),(2),(3),(4),(5)) MonthNumbers(n)
        ), Issues AS (
            SELECT DATEFROMPARTS(YEAR(IssuedAt),MONTH(IssuedAt),1) MonthStart,COUNT_BIG(*) Total
            FROM library.Loans WITH(HOLDLOCK) WHERE IssuedAt>=@FirstMonth AND IssuedAt<=@Now
            GROUP BY YEAR(IssuedAt),MONTH(IssuedAt)
        ), Returns AS (
            SELECT DATEFROMPARTS(YEAR(ReturnedAt),MONTH(ReturnedAt),1) MonthStart,COUNT_BIG(*) Total
            FROM library.Loans WITH(HOLDLOCK) WHERE ReturnedAt>=@FirstMonth AND ReturnedAt<=@Now
            GROUP BY YEAR(ReturnedAt),MONTH(ReturnedAt)
        )
        SELECT m.MonthStart Month,COALESCE(i.Total,0) IssuedBooks,COALESCE(r.Total,0) ReturnedBooks
        INTO #MonthlyActivity FROM Months m LEFT JOIN Issues i ON i.MonthStart=m.MonthStart
            LEFT JOIN Returns r ON r.MonthStart=m.MonthStart;

        SELECT @TotalBooks TotalBooks,@TotalBooks-@IssuedBooks AvailableBooks,@IssuedBooks IssuedBooks,
            @ReturnedBooks ReturnedBooks,@OverdueBooks OverdueBooks,@TotalMembers TotalMembers,
            @Fine TotalFineCollected,@BookTitles BookTitles,@ActiveMembers ActiveMembers,@Now RefreshedAt;
        SELECT Month,IssuedBooks,ReturnedBooks FROM #MonthlyActivity ORDER BY Month;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
