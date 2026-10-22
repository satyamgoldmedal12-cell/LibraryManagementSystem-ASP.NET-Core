SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER PROCEDURE library.Categories_List AS
BEGIN SET NOCOUNT ON; SELECT Id, Name FROM library.Categories ORDER BY Name; END;
GO
CREATE OR ALTER PROCEDURE library.Categories_Save @Name nvarchar(80), @ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRAN;
 INSERT library.Categories(Name) VALUES (@Name);
 DECLARE @Id int = SCOPE_IDENTITY();
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Category created',CONVERT(nvarchar(20),@Id));
 COMMIT;
END;
GO
CREATE OR ALTER PROCEDURE library.Books_List @Search nvarchar(200)=NULL, @Page int=1, @PageSize int=12, @IncludeArchived bit=0 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page < 1 OR @Page > 100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 SELECT b.*, c.Name CategoryName, b.TotalCopies - (SELECT COUNT(*) FROM library.Loans l WHERE l.BookId=b.Id AND l.ReturnedAt IS NULL) AvailableCopies
 FROM library.Books b JOIN library.Categories c ON c.Id=b.CategoryId
 WHERE (@IncludeArchived=1 OR b.IsArchived=0) AND (@Search IS NULL OR b.Title LIKE '%'+@Search+'%' OR b.Author LIKE '%'+@Search+'%' OR b.ISBN LIKE '%'+@Search+'%')
 ORDER BY b.Title,b.Id OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM library.Books b WHERE (@IncludeArchived=1 OR b.IsArchived=0) AND (@Search IS NULL OR b.Title LIKE '%'+@Search+'%' OR b.Author LIKE '%'+@Search+'%' OR b.ISBN LIKE '%'+@Search+'%');
END;
GO
CREATE OR ALTER PROCEDURE library.Books_Get @Id int AS
BEGIN SET NOCOUNT ON; SELECT * FROM library.Books WHERE Id=@Id; END;
GO
CREATE OR ALTER PROCEDURE library.Books_Save @Id int=0, @ISBN varchar(13), @Title nvarchar(200), @Author nvarchar(150), @Publisher nvarchar(150)=NULL, @CategoryId int, @TotalCopies int, @Revision int=0, @IsArchived bit=0, @ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF @Id=0
 BEGIN
  INSERT library.Books(ISBN,Title,Author,Publisher,CategoryId,TotalCopies) VALUES (@ISBN,@Title,@Author,@Publisher,@CategoryId,@TotalCopies);
  SET @Id=SCOPE_IDENTITY();
 END
 ELSE
 BEGIN
  DECLARE @CurrentRevision int;
  SELECT @CurrentRevision=Revision FROM library.Books WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id;
  IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This book changed. Reload and try again.',1;
  DECLARE @ActiveLoans int=(SELECT COUNT(*) FROM library.Loans WHERE BookId=@Id AND ReturnedAt IS NULL);
  IF @TotalCopies<@ActiveLoans THROW 50001,'Copies cannot be fewer than outstanding loans.',1;
  IF @IsArchived=1 AND @ActiveLoans>0 THROW 50001,'Return outstanding loans before archiving this book.',1;
  UPDATE library.Books SET ISBN=@ISBN,Title=@Title,Author=@Author,Publisher=@Publisher,CategoryId=@CategoryId,TotalCopies=@TotalCopies,IsArchived=@IsArchived,Revision=Revision+1 WHERE Id=@Id;
 END;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Book saved',CONVERT(nvarchar(20),@Id));
 COMMIT;
 SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_List @Search nvarchar(200)=NULL,@Page int=1,@PageSize int=12 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page<1 OR @Page>100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 SELECT * FROM library.Members WHERE @Search IS NULL OR FullName LIKE '%'+@Search+'%' OR MemberNumber LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%'
 ORDER BY FullName,Id OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM library.Members WHERE @Search IS NULL OR FullName LIKE '%'+@Search+'%' OR MemberNumber LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%';
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Get @Id int AS
BEGIN SET NOCOUNT ON; SELECT * FROM library.Members WHERE Id=@Id; END;
GO
CREATE OR ALTER PROCEDURE library.Members_Save @Id int=0,@MemberNumber varchar(30),@FullName nvarchar(120),@Email nvarchar(256),@Phone nvarchar(25)=NULL,@ExpiresOn date,@IsActive bit=1,@Revision int=0,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF @Id=0 BEGIN
  INSERT library.Members(MemberNumber,FullName,Email,Phone,ExpiresOn,IsActive) VALUES (@MemberNumber,@FullName,@Email,@Phone,@ExpiresOn,@IsActive);
  SET @Id=SCOPE_IDENTITY();
 END
 ELSE BEGIN
  UPDATE library.Members SET MemberNumber=@MemberNumber,FullName=@FullName,Email=@Email,Phone=@Phone,ExpiresOn=@ExpiresOn,IsActive=@IsActive,Revision=Revision+1 WHERE Id=@Id AND Revision=@Revision;
  IF @@ROWCOUNT=0 THROW 50001,'This member changed. Reload and try again.',1;
 END;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Member saved',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_Issue @BookId int,@MemberId int,@DueDate date,@DailyFine decimal(10,2),@MaxLoans int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Today date=CONVERT(date,SYSUTCDATETIME()),@Expiry date,@Active bit,@Copies int,@Archived bit;
 IF @DueDate<=@Today OR @DueDate>DATEADD(day,90,@Today) THROW 50001,'Due date must be within the next 90 days.',1;
 IF @MaxLoans NOT BETWEEN 1 AND 100 OR @DailyFine NOT BETWEEN 0 AND 1000 THROW 50001,'Invalid lending policy.',1;
 -- Every circulation transaction locks member, then book, then loan rows.
 SELECT @Expiry=ExpiresOn,@Active=IsActive FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId;
 IF @Active IS NULL OR @Active=0 OR @Expiry<@Today THROW 50001,'Member is inactive, expired, or missing.',1;
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
CREATE OR ALTER PROCEDURE library.Loans_Return @Id int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @MemberId int,@BookId int,@LockId int;
 SELECT @MemberId=MemberId,@BookId=BookId FROM library.Loans WHERE Id=@Id;
 IF @MemberId IS NULL THROW 50001,'Loan not found.',1;
 SELECT @LockId=Id FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId;
 SELECT @LockId=Id FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@BookId;
 UPDATE library.Loans SET ReturnedAt=SYSUTCDATETIME(),ReturnedBy=@ActorId,
 FineAmount=CASE WHEN DueDate<CONVERT(date,SYSUTCDATETIME()) THEN DATEDIFF(day,DueDate,CONVERT(date,SYSUTCDATETIME()))*DailyFine ELSE 0 END
 WHERE Id=@Id AND ReturnedAt IS NULL;
 IF @@ROWCOUNT=0 THROW 50001,'This loan has already been returned.',1;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Book returned',CONVERT(nvarchar(20),@Id));
 COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_PayFine @Id int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Fine decimal(10,2),@Returned datetime2;
 SELECT @Fine=FineAmount,@Returned=ReturnedAt FROM library.Loans WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @Returned IS NULL OR @Fine IS NULL OR @Fine<=0 THROW 50001,'A returned loan with a fine is required.',1;
 IF EXISTS(SELECT 1 FROM library.Payments WHERE LoanId=@Id) THROW 50001,'Fine is already paid.',1;
 INSERT library.Payments(LoanId,Amount,ReceivedBy) VALUES (@Id,@Fine,@ActorId);
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES (@ActorId,'Fine paid',CONVERT(nvarchar(20),@Id));
 COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_List @Search nvarchar(200)=NULL,@Status varchar(15)='active',@MemberId int=NULL,@Page int=1,@PageSize int=12 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page<1 OR @Page>100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 SELECT l.Id,b.Title,m.FullName MemberName,m.MemberNumber,l.MemberId,l.BookId,l.IssuedAt,l.DueDate,l.ReturnedAt,
 CASE WHEN l.ReturnedAt IS NULL AND l.DueDate<CONVERT(date,SYSUTCDATETIME()) THEN DATEDIFF(day,l.DueDate,CONVERT(date,SYSUTCDATETIME()))*l.DailyFine ELSE l.FineAmount END FineAmount,
 CONVERT(bit,CASE WHEN p.Id IS NOT NULL THEN 1 ELSE 0 END) IsPaid
 INTO #Results FROM library.Loans l JOIN library.Books b ON b.Id=l.BookId JOIN library.Members m ON m.Id=l.MemberId LEFT JOIN library.Payments p ON p.LoanId=l.Id
 WHERE (@MemberId IS NULL OR l.MemberId=@MemberId)
 AND (@Search IS NULL OR b.Title LIKE '%'+@Search+'%' OR m.FullName LIKE '%'+@Search+'%' OR m.MemberNumber LIKE '%'+@Search+'%')
 AND (@Status='all' OR (@Status='active' AND l.ReturnedAt IS NULL) OR (@Status='returned' AND l.ReturnedAt IS NOT NULL) OR (@Status='overdue' AND l.ReturnedAt IS NULL AND l.DueDate<CONVERT(date,SYSUTCDATETIME())) OR (@Status='unpaid' AND l.ReturnedAt IS NOT NULL AND l.FineAmount>0 AND p.Id IS NULL));
 SELECT * FROM #Results ORDER BY IssuedAt DESC,Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM #Results;
END;
GO
CREATE OR ALTER PROCEDURE library.Dashboard_Get AS
BEGIN
 SET NOCOUNT ON;
 SELECT (SELECT COUNT(*) FROM library.Books WHERE IsArchived=0) BookTitles,
 (SELECT COALESCE(SUM(TotalCopies),0) FROM library.Books WHERE IsArchived=0) TotalCopies,
 (SELECT COUNT(*) FROM library.Members WHERE IsActive=1 AND ExpiresOn>=CONVERT(date,SYSUTCDATETIME())) ActiveMembers,
 (SELECT COUNT(*) FROM library.Loans WHERE ReturnedAt IS NULL) ActiveLoans,
 (SELECT COUNT(*) FROM library.Loans WHERE ReturnedAt IS NULL AND DueDate<CONVERT(date,SYSUTCDATETIME())) OverdueLoans,
 (SELECT COALESCE(SUM(l.FineAmount),0) FROM library.Loans l WHERE ReturnedAt IS NOT NULL AND NOT EXISTS(SELECT 1 FROM library.Payments p WHERE p.LoanId=l.Id)) UnpaidFines,
 (SELECT COALESCE(SUM(Amount),0) FROM library.Payments) CollectedFines;
END;
GO
CREATE OR ALTER PROCEDURE library.Audit_List AS
BEGIN SET NOCOUNT ON; SELECT TOP(100) a.*,u.FullName ActorName FROM library.AuditEvents a JOIN library.Users u ON u.Id=a.ActorId ORDER BY a.Id DESC; END;
GO
CREATE OR ALTER PROCEDURE library.Users_Find @Id nvarchar(450)=NULL,@NormalizedUserName nvarchar(256)=NULL,@NormalizedEmail nvarchar(256)=NULL AS
BEGIN
 SET NOCOUNT ON;
 SELECT * FROM library.Users WHERE (@Id IS NOT NULL AND Id=@Id) OR (@NormalizedUserName IS NOT NULL AND NormalizedUserName=@NormalizedUserName) OR (@NormalizedEmail IS NOT NULL AND NormalizedEmail=@NormalizedEmail);
END;
GO
CREATE OR ALTER PROCEDURE library.Users_Create @Id nvarchar(450),@UserName nvarchar(256),@NormalizedUserName nvarchar(256),@Email nvarchar(256),@NormalizedEmail nvarchar(256),@EmailConfirmed bit,@PasswordHash nvarchar(max),@SecurityStamp nvarchar(100),@ConcurrencyStamp nvarchar(100),@LockoutEnd datetimeoffset=NULL,@LockoutEnabled bit,@AccessFailedCount int,@FullName nvarchar(120),@Role nvarchar(20),@MemberId int=NULL AS
BEGIN
 SET NOCOUNT ON;
 INSERT library.Users(Id,UserName,NormalizedUserName,Email,NormalizedEmail,EmailConfirmed,PasswordHash,SecurityStamp,ConcurrencyStamp,LockoutEnd,LockoutEnabled,AccessFailedCount,FullName,Role,MemberId)
 VALUES (@Id,@UserName,@NormalizedUserName,@Email,@NormalizedEmail,@EmailConfirmed,@PasswordHash,@SecurityStamp,@ConcurrencyStamp,@LockoutEnd,@LockoutEnabled,@AccessFailedCount,@FullName,@Role,@MemberId);
END;
GO
CREATE OR ALTER PROCEDURE library.Users_Update @Id nvarchar(450),@UserName nvarchar(256),@NormalizedUserName nvarchar(256),@Email nvarchar(256),@NormalizedEmail nvarchar(256),@EmailConfirmed bit,@PasswordHash nvarchar(max),@SecurityStamp nvarchar(100),@ConcurrencyStamp nvarchar(100),@LockoutEnd datetimeoffset=NULL,@LockoutEnabled bit,@AccessFailedCount int,@FullName nvarchar(120),@Role nvarchar(20),@MemberId int=NULL,@PreviousStamp nvarchar(100) AS
BEGIN
 SET NOCOUNT ON;
 UPDATE library.Users SET UserName=@UserName,NormalizedUserName=@NormalizedUserName,Email=@Email,NormalizedEmail=@NormalizedEmail,EmailConfirmed=@EmailConfirmed,PasswordHash=@PasswordHash,SecurityStamp=@SecurityStamp,ConcurrencyStamp=@ConcurrencyStamp,LockoutEnd=@LockoutEnd,LockoutEnabled=@LockoutEnabled,AccessFailedCount=@AccessFailedCount,FullName=@FullName,Role=@Role,MemberId=@MemberId WHERE Id=@Id AND ConcurrencyStamp=@PreviousStamp;
 SELECT @@ROWCOUNT;
END;
GO
CREATE OR ALTER PROCEDURE library.Users_Delete @Id nvarchar(450),@ConcurrencyStamp nvarchar(100) AS
BEGIN SET NOCOUNT ON; DELETE library.Users WHERE Id=@Id AND ConcurrencyStamp=@ConcurrencyStamp; SELECT @@ROWCOUNT; END;
GO
CREATE OR ALTER PROCEDURE library.Users_List AS
BEGIN SET NOCOUNT ON; SELECT TOP(200) Id,FullName,Email,Role,MemberId,LockoutEnd FROM library.Users ORDER BY FullName; END;
GO
