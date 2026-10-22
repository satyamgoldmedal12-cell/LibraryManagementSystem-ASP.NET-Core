-- Apply after 001-008. No existing circulation records are removed.
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
IF OBJECT_ID('library.FinePolicy','U') IS NULL
CREATE TABLE library.FinePolicy(
 Id tinyint NOT NULL CONSTRAINT PK_FinePolicy PRIMARY KEY CONSTRAINT CK_FinePolicy_Id CHECK(Id=1),
 DailyFine decimal(10,2) NULL CONSTRAINT CK_FinePolicy_Rate CHECK(DailyFine BETWEEN 0 AND 1000),
 Revision int NOT NULL CONSTRAINT DF_FinePolicy_Revision DEFAULT 1,
 CreatedAt datetime2 NOT NULL CONSTRAINT DF_FinePolicy_CreatedAt DEFAULT SYSUTCDATETIME(),
 CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),
 ModifiedAt datetime2 NULL,
 ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id)
);
-- A null rate preserves existing Lending:DailyFine configuration until an Admin saves settings.
IF NOT EXISTS(SELECT 1 FROM library.FinePolicy WHERE Id=1) INSERT library.FinePolicy(Id) VALUES(1);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Loans') AND name='IX_Loans_ReturnedAt')
CREATE INDEX IX_Loans_ReturnedAt ON library.Loans(ReturnedAt DESC,Id DESC) INCLUDE(BookId,MemberId,DueDate,FineAmount) WHERE ReturnedAt IS NOT NULL;
COMMIT;
GO
CREATE OR ALTER PROCEDURE library.FinePolicy_Get @DefaultFine decimal(10,2) AS
BEGIN
 SET NOCOUNT ON;
 SELECT COALESCE(p.DailyFine,@DefaultFine) DailyFine,p.Revision,p.ModifiedAt,u.FullName ModifiedByName
 FROM library.FinePolicy p LEFT JOIN library.Users u ON u.Id=p.ModifiedBy WHERE p.Id=1;
END;
GO
CREATE OR ALTER PROCEDURE library.FinePolicy_Save @DailyFine decimal(18,4),@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role='Admin') THROW 50001,'Only an administrator can configure fines.',1;
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
 SELECT (SELECT DailyFine FROM library.FinePolicy WHERE Id=1) ConfiguredDailyFine,b.Id BookId,b.Title BookTitle,b.ISBN,COALESCE(b.TotalCopies,0) TotalCopies,
 COALESCE(b.TotalCopies,0)-(SELECT COUNT(*) FROM library.Loans WHERE BookId=@BookId AND ReturnedAt IS NULL) AvailableCopies,
 COALESCE(b.IsArchived,0) BookArchived,m.Id MemberId,m.FullName MemberName,m.MemberNumber,
 COALESCE(m.IsActive,0) MemberActive,COALESCE(m.IsArchived,0) MemberArchived,m.ExpiresOn MemberExpiresOn,
 (SELECT COUNT(*) FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL) ActiveLoans,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.Loans WHERE BookId=@BookId AND MemberId=@MemberId AND ReturnedAt IS NULL) THEN 1 ELSE 0 END AS bit) DuplicateActiveIssue,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL AND DueDate<CONVERT(date,SYSUTCDATETIME())) THEN 1 ELSE 0 END AS bit) HasOverdueLoans
 FROM (VALUES(1)) seed(n) LEFT JOIN library.Books b ON b.Id=@BookId LEFT JOIN library.Members m ON m.Id=@MemberId;
END;
GO

CREATE OR ALTER PROCEDURE library.Loans_Issue @BookId int,@MemberId int,@DueDate date,@DailyFine decimal(10,2),@MaxLoans int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian')) THROW 50001,'Only staff can issue books.',1;
 SELECT @DailyFine=COALESCE(DailyFine,@DailyFine) FROM library.FinePolicy WITH(HOLDLOCK) WHERE Id=1;
 DECLARE @Today date=CONVERT(date,SYSUTCDATETIME()),@Expiry date,@Active bit,@MemberDeleted bit,@Copies int,@Archived bit;
 IF @DueDate<=@Today OR @DueDate>DATEADD(day,90,@Today) THROW 50001,'Due date must be within the next 90 days.',1;
 IF @MaxLoans NOT BETWEEN 1 AND 100 OR @DailyFine NOT BETWEEN 0 AND 1000 THROW 50001,'Invalid lending policy.',1;
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

CREATE OR ALTER PROCEDURE library.Loans_Return @Id int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian')) THROW 50001,'Only staff can return books.',1;
  DECLARE @MemberId int,@BookId int,@LockId int;
  SELECT @MemberId=MemberId,@BookId=BookId FROM library.Loans WHERE Id=@Id;
  IF @MemberId IS NULL THROW 50001,'Loan not found.',1;
  SELECT @LockId=Id FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId;
  SELECT @LockId=Id FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@BookId;
  DECLARE @Now datetime2=SYSUTCDATETIME();
  UPDATE library.Loans SET ReturnedAt=@Now,ReturnedBy=@ActorId,
   FineAmount=CASE WHEN DueDate<CONVERT(date,@Now) THEN DATEDIFF(day,DueDate,CONVERT(date,@Now))*DailyFine ELSE 0 END
  WHERE Id=@Id AND ReturnedAt IS NULL;
  IF @@ROWCOUNT=0 THROW 50001,'This loan has already been returned.',1;
  INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Book returned',CONVERT(nvarchar(20),@Id));
  COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_PayFine @Id int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian')) THROW 50001,'Only staff can record fine payments.',1;
  DECLARE @Fine decimal(10,2),@Returned datetime2;
  SELECT @Fine=FineAmount,@Returned=ReturnedAt FROM library.Loans WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
  IF @Returned IS NULL OR @Fine IS NULL OR @Fine<=0 THROW 50001,'A returned loan with a fine is required.',1;
  IF EXISTS(SELECT 1 FROM library.Payments WHERE LoanId=@Id) THROW 50001,'Fine is already paid.',1;
  INSERT library.Payments(LoanId,Amount,PaidAt,ReceivedBy) VALUES(@Id,@Fine,SYSUTCDATETIME(),@ActorId);
  INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Fine paid',CONVERT(nvarchar(20),@Id));
  COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER VIEW library.CirculationRecords AS
 SELECT l.Id,l.BookId,l.MemberId,b.Title,b.ISBN,m.FullName MemberName,m.MemberNumber,l.IssuedAt,l.DueDate,l.ReturnedAt,ru.FullName ReturnedByName,
 CASE WHEN l.DueDate<CONVERT(date,COALESCE(l.ReturnedAt,SYSUTCDATETIME())) THEN DATEDIFF(day,l.DueDate,CONVERT(date,COALESCE(l.ReturnedAt,SYSUTCDATETIME()))) ELSE 0 END LateDays,
 l.DailyFine,
 CASE WHEN l.ReturnedAt IS NULL THEN CASE WHEN l.DueDate<CONVERT(date,SYSUTCDATETIME()) THEN DATEDIFF(day,l.DueDate,CONVERT(date,SYSUTCDATETIME()))*l.DailyFine ELSE 0 END ELSE l.FineAmount END FineAmount,
 b.TotalCopies-(SELECT COUNT(*) FROM library.Loans active WHERE active.BookId=b.Id AND active.ReturnedAt IS NULL) AvailableCopies,
 p.Id PaymentId,p.PaidAt,COALESCE(p.Amount,0) PaidAmount,pu.FullName ReceivedByName,SYSUTCDATETIME() AsOfUtc
 FROM library.Loans l JOIN library.Books b ON b.Id=l.BookId JOIN library.Members m ON m.Id=l.MemberId
 LEFT JOIN library.Users ru ON ru.Id=l.ReturnedBy LEFT JOIN library.Payments p ON p.LoanId=l.Id LEFT JOIN library.Users pu ON pu.Id=p.ReceivedBy;
GO
CREATE OR ALTER PROCEDURE library.Circulation_Get @Id int AS
BEGIN SET NOCOUNT ON; SELECT * FROM library.CirculationRecords WHERE Id=@Id; END;
GO
CREATE OR ALTER PROCEDURE library.Circulation_List
 @Kind varchar(15),@Search nvarchar(200)=NULL,@FromDate date=NULL,@ToDate date=NULL,@MemberId int=NULL,@BookId int=NULL,@Status varchar(15)='all',@Page int=1,@PageSize int=12 AS
BEGIN
 SET NOCOUNT ON;
 IF @Kind NOT IN('returns','fines','collections') OR @Status NOT IN('all','pending','paid','accruing') THROW 50001,'Invalid report filter.',1;
 IF @FromDate>@ToDate THROW 50001,'From date cannot be after To date.',1;
 IF @Page NOT BETWEEN 1 AND 100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 SELECT r.* INTO #Results FROM library.CirculationRecords r
 WHERE (@MemberId IS NULL OR r.MemberId=@MemberId) AND (@BookId IS NULL OR r.BookId=@BookId)
 AND (@Search IS NULL OR r.Title LIKE '%'+@Search+'%' OR r.ISBN LIKE '%'+@Search+'%' OR r.MemberName LIKE '%'+@Search+'%' OR r.MemberNumber LIKE '%'+@Search+'%')
 AND ((@Kind='returns' AND r.ReturnedAt IS NOT NULL) OR (@Kind='collections' AND r.PaymentId IS NOT NULL) OR (@Kind='fines' AND r.FineAmount>0))
 AND (@Kind<>'fines' OR @Status='all' OR (@Status='pending' AND r.ReturnedAt IS NOT NULL AND r.PaymentId IS NULL) OR (@Status='accruing' AND r.ReturnedAt IS NULL) OR (@Status='paid' AND r.PaymentId IS NOT NULL))
 AND (@FromDate IS NULL OR CONVERT(date,CASE @Kind WHEN 'returns' THEN r.ReturnedAt WHEN 'collections' THEN r.PaidAt ELSE r.DueDate END)>=@FromDate)
 AND (@ToDate IS NULL OR CONVERT(date,CASE @Kind WHEN 'returns' THEN r.ReturnedAt WHEN 'collections' THEN r.PaidAt ELSE r.DueDate END)<=@ToDate);
 SELECT * FROM #Results ORDER BY CASE @Kind WHEN 'returns' THEN ReturnedAt WHEN 'collections' THEN PaidAt ELSE DueDate END DESC,Id DESC
 OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) TotalRecords,COALESCE(SUM(CASE WHEN ReturnedAt IS NOT NULL AND PaymentId IS NULL THEN FineAmount ELSE 0 END),0) PendingFine,
 COALESCE(SUM(CASE WHEN ReturnedAt IS NULL THEN FineAmount ELSE 0 END),0) AccruingFine,COALESCE(SUM(PaidAmount),0) PaidFine FROM #Results;
END;
GO
