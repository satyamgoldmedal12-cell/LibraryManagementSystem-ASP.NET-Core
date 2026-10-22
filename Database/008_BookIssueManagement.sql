SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
-- Keep the database-level duplicate guard even when clients bypass the preview.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Loans') AND name='UX_Loans_ActiveMemberBook')
CREATE UNIQUE INDEX UX_Loans_ActiveMemberBook ON library.Loans(MemberId,BookId) WHERE ReturnedAt IS NULL;
GO
-- Advisory context only. Loans_Issue rechecks every rule inside its write transaction.
CREATE OR ALTER PROCEDURE library.Loans_IssueContext @BookId int,@MemberId int AS
BEGIN
 SET NOCOUNT ON;
 SELECT b.Id BookId,b.Title BookTitle,b.ISBN,COALESCE(b.TotalCopies,0) TotalCopies,
 COALESCE(b.TotalCopies,0)-(SELECT COUNT(*) FROM library.Loans WHERE BookId=@BookId AND ReturnedAt IS NULL) AvailableCopies,
 COALESCE(b.IsArchived,0) BookArchived,m.Id MemberId,m.FullName MemberName,m.MemberNumber,
 COALESCE(m.IsActive,0) MemberActive,COALESCE(m.IsArchived,0) MemberArchived,m.ExpiresOn MemberExpiresOn,
 (SELECT COUNT(*) FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL) ActiveLoans,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.Loans WHERE BookId=@BookId AND MemberId=@MemberId AND ReturnedAt IS NULL) THEN 1 ELSE 0 END AS bit) DuplicateActiveIssue,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@MemberId AND ReturnedAt IS NULL AND DueDate<CONVERT(date,SYSUTCDATETIME())) THEN 1 ELSE 0 END AS bit) HasOverdueLoans
 FROM (VALUES(1)) seed(n) LEFT JOIN library.Books b ON b.Id=@BookId LEFT JOIN library.Members m ON m.Id=@MemberId;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_Receipt @Id int AS
BEGIN
 SET NOCOUNT ON;
 SELECT l.Id,l.BookId,l.MemberId,b.Title BookTitle,b.ISBN,m.FullName MemberName,m.MemberNumber,
 l.IssuedAt,l.DueDate,l.ReturnedAt,u.FullName IssuedByName,l.DailyFine
 FROM library.Loans l JOIN library.Books b ON b.Id=l.BookId JOIN library.Members m ON m.Id=l.MemberId
 JOIN library.Users u ON u.Id=l.IssuedBy WHERE l.Id=@Id;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_Issue @BookId int,@MemberId int,@DueDate date,@DailyFine decimal(10,2),@MaxLoans int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian')) THROW 50001,'Only staff can issue books.',1;
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
