-- Apply with sqlcmd -b -I after 001-012. All batches share one transaction.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRAN;
GO
IF OBJECT_ID('library.Authors','U') IS NULL
CREATE TABLE library.Authors(Id int IDENTITY CONSTRAINT PK_Authors PRIMARY KEY,Name nvarchar(150) NOT NULL CONSTRAINT UQ_Authors_Name UNIQUE,
 CreatedDate datetime2 NULL CONSTRAINT DF_Authors_CreatedDate DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),
 ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id),CONSTRAINT CK_Authors_Name CHECK(LEN(LTRIM(RTRIM(Name)))>0));
IF OBJECT_ID('library.Publishers','U') IS NULL
CREATE TABLE library.Publishers(Id int IDENTITY CONSTRAINT PK_Publishers PRIMARY KEY,Name nvarchar(150) NOT NULL CONSTRAINT UQ_Publishers_Name UNIQUE,
 CreatedDate datetime2 NULL CONSTRAINT DF_Publishers_CreatedDate DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(450) NULL REFERENCES library.Users(Id),
 ModifiedDate datetime2 NULL,ModifiedBy nvarchar(450) NULL REFERENCES library.Users(Id),DeletedDate datetime2 NULL,DeletedBy nvarchar(450) NULL REFERENCES library.Users(Id),CONSTRAINT CK_Publishers_Name CHECK(LEN(LTRIM(RTRIM(Name)))>0));
GO
IF COL_LENGTH('library.Books','AuthorId') IS NULL ALTER TABLE library.Books ADD AuthorId int NULL;
IF COL_LENGTH('library.Books','PublisherId') IS NULL ALTER TABLE library.Books ADD PublisherId int NULL;
GO
IF COL_LENGTH('library.Books','Author') IS NOT NULL
BEGIN
 EXEC(N'IF EXISTS(SELECT 1 FROM library.Books WHERE LEN(LTRIM(RTRIM(Author)))=0) THROW 50001,''Record valid author names before migration.'',1;
 INSERT library.Authors(Name,CreatedDate) SELECT DISTINCT LTRIM(RTRIM(Author)),NULL FROM library.Books b WHERE NOT EXISTS(SELECT 1 FROM library.Authors a WHERE a.Name=LTRIM(RTRIM(b.Author)));
 INSERT library.Publishers(Name,CreatedDate) SELECT DISTINCT LTRIM(RTRIM(Publisher)),NULL FROM library.Books b WHERE NULLIF(LTRIM(RTRIM(Publisher)),N'''') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM library.Publishers p WHERE p.Name=LTRIM(RTRIM(b.Publisher)));
 UPDATE b SET AuthorId=a.Id,PublisherId=p.Id FROM library.Books b JOIN library.Authors a ON a.Name=LTRIM(RTRIM(b.Author)) LEFT JOIN library.Publishers p ON p.Name=NULLIF(LTRIM(RTRIM(b.Publisher)),N'''');
 IF EXISTS(SELECT 1 FROM library.Books WHERE AuthorId IS NULL) THROW 50001,''Author mapping failed.'',1;');
END;
ALTER TABLE library.Books ALTER COLUMN AuthorId int NOT NULL;
IF OBJECT_ID('library.FK_Books_Authors','F') IS NULL ALTER TABLE library.Books WITH CHECK ADD CONSTRAINT FK_Books_Authors FOREIGN KEY(AuthorId) REFERENCES library.Authors(Id);
IF OBJECT_ID('library.FK_Books_Publishers','F') IS NULL ALTER TABLE library.Books WITH CHECK ADD CONSTRAINT FK_Books_Publishers FOREIGN KEY(PublisherId) REFERENCES library.Publishers(Id);
GO
IF COL_LENGTH('library.Users','CreatedDate') IS NULL ALTER TABLE library.Users ADD CreatedDate AS CreatedAt PERSISTED;
IF COL_LENGTH('library.Users','ModifiedDate') IS NULL ALTER TABLE library.Users ADD ModifiedDate AS ModifiedAt PERSISTED;
IF COL_LENGTH('library.Users','DeletedDate') IS NULL ALTER TABLE library.Users ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.Users','DeletedBy') IS NULL ALTER TABLE library.Users ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_Users_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Categories','CreatedDate') IS NULL ALTER TABLE library.Categories ADD CreatedDate datetime2 NULL CONSTRAINT DF_Categories_CreatedDate DEFAULT SYSUTCDATETIME();
IF COL_LENGTH('library.Categories','CreatedBy') IS NULL ALTER TABLE library.Categories ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_Categories_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Categories','ModifiedDate') IS NULL ALTER TABLE library.Categories ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.Categories','ModifiedBy') IS NULL ALTER TABLE library.Categories ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_Categories_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Categories','DeletedDate') IS NULL ALTER TABLE library.Categories ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.Categories','DeletedBy') IS NULL ALTER TABLE library.Categories ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_Categories_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Books','CreatedDate') IS NULL ALTER TABLE library.Books ADD CreatedDate AS CreatedAt PERSISTED;
IF COL_LENGTH('library.Books','CreatedBy') IS NULL ALTER TABLE library.Books ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_Books_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Books','ModifiedDate') IS NULL ALTER TABLE library.Books ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.Books','ModifiedBy') IS NULL ALTER TABLE library.Books ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_Books_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Books','DeletedDate') IS NULL ALTER TABLE library.Books ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.Books','DeletedBy') IS NULL ALTER TABLE library.Books ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_Books_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Members','CreatedDate') IS NULL ALTER TABLE library.Members ADD CreatedDate AS CreatedAt PERSISTED;
IF COL_LENGTH('library.Members','CreatedBy') IS NULL ALTER TABLE library.Members ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_Members_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Members','ModifiedDate') IS NULL ALTER TABLE library.Members ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.Members','ModifiedBy') IS NULL ALTER TABLE library.Members ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_Members_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Members','DeletedDate') IS NULL ALTER TABLE library.Members ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.Members','DeletedBy') IS NULL ALTER TABLE library.Members ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_Members_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Loans','CreatedDate') IS NULL ALTER TABLE library.Loans ADD CreatedDate AS IssuedAt PERSISTED;
IF COL_LENGTH('library.Loans','CreatedBy') IS NULL ALTER TABLE library.Loans ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_Loans_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Loans','ModifiedDate') IS NULL ALTER TABLE library.Loans ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.Loans','ModifiedBy') IS NULL ALTER TABLE library.Loans ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_Loans_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Loans','DeletedDate') IS NULL ALTER TABLE library.Loans ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.Loans','DeletedBy') IS NULL ALTER TABLE library.Loans ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_Loans_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Payments','CreatedDate') IS NULL ALTER TABLE library.Payments ADD CreatedDate AS PaidAt PERSISTED;
IF COL_LENGTH('library.Payments','CreatedBy') IS NULL ALTER TABLE library.Payments ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_Payments_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Payments','ModifiedDate') IS NULL ALTER TABLE library.Payments ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.Payments','ModifiedBy') IS NULL ALTER TABLE library.Payments ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_Payments_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.Payments','DeletedDate') IS NULL ALTER TABLE library.Payments ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.Payments','DeletedBy') IS NULL ALTER TABLE library.Payments ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_Payments_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.AuditEvents','CreatedDate') IS NULL ALTER TABLE library.AuditEvents ADD CreatedDate AS OccurredAt PERSISTED;
IF COL_LENGTH('library.AuditEvents','CreatedBy') IS NULL ALTER TABLE library.AuditEvents ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_AuditEvents_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.AuditEvents','ModifiedDate') IS NULL ALTER TABLE library.AuditEvents ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.AuditEvents','ModifiedBy') IS NULL ALTER TABLE library.AuditEvents ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_AuditEvents_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.AuditEvents','DeletedDate') IS NULL ALTER TABLE library.AuditEvents ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.AuditEvents','DeletedBy') IS NULL ALTER TABLE library.AuditEvents ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_AuditEvents_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.BookCovers','CreatedDate') IS NULL ALTER TABLE library.BookCovers ADD CreatedDate datetime2 NULL CONSTRAINT DF_BookCovers_CreatedDate DEFAULT SYSUTCDATETIME();
IF COL_LENGTH('library.BookCovers','CreatedBy') IS NULL ALTER TABLE library.BookCovers ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_BookCovers_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.BookCovers','ModifiedDate') IS NULL ALTER TABLE library.BookCovers ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.BookCovers','ModifiedBy') IS NULL ALTER TABLE library.BookCovers ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_BookCovers_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.BookCovers','DeletedDate') IS NULL ALTER TABLE library.BookCovers ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.BookCovers','DeletedBy') IS NULL ALTER TABLE library.BookCovers ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_BookCovers_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.UserSessions','CreatedDate') IS NULL ALTER TABLE library.UserSessions ADD CreatedDate AS IssuedAt PERSISTED;
IF COL_LENGTH('library.UserSessions','CreatedBy') IS NULL ALTER TABLE library.UserSessions ADD CreatedBy nvarchar(450) NULL CONSTRAINT FK_UserSessions_CreatedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.UserSessions','ModifiedDate') IS NULL ALTER TABLE library.UserSessions ADD ModifiedDate datetime2 NULL;
IF COL_LENGTH('library.UserSessions','ModifiedBy') IS NULL ALTER TABLE library.UserSessions ADD ModifiedBy nvarchar(450) NULL CONSTRAINT FK_UserSessions_ModifiedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.UserSessions','DeletedDate') IS NULL ALTER TABLE library.UserSessions ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.UserSessions','DeletedBy') IS NULL ALTER TABLE library.UserSessions ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_UserSessions_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
IF COL_LENGTH('library.FinePolicy','CreatedDate') IS NULL ALTER TABLE library.FinePolicy ADD CreatedDate AS CreatedAt PERSISTED;
IF COL_LENGTH('library.FinePolicy','ModifiedDate') IS NULL ALTER TABLE library.FinePolicy ADD ModifiedDate AS ModifiedAt PERSISTED;
IF COL_LENGTH('library.FinePolicy','DeletedDate') IS NULL ALTER TABLE library.FinePolicy ADD DeletedDate datetime2 NULL;
IF COL_LENGTH('library.FinePolicy','DeletedBy') IS NULL ALTER TABLE library.FinePolicy ADD DeletedBy nvarchar(450) NULL CONSTRAINT FK_FinePolicy_DeletedBy FOREIGN KEY REFERENCES library.Users(Id);
GO
-- Backfill only facts that were already recorded; unknown historical actors/dates remain NULL.
UPDATE library.Loans SET CreatedBy=IssuedBy WHERE CreatedBy IS NULL;
UPDATE library.Payments SET CreatedBy=ReceivedBy WHERE CreatedBy IS NULL;
UPDATE library.AuditEvents SET CreatedBy=ActorId WHERE CreatedBy IS NULL;
UPDATE library.UserSessions SET CreatedBy=UserId WHERE CreatedBy IS NULL;
GO
CREATE OR ALTER VIEW library.BookCatalog AS
 SELECT b.Id,b.ISBN,b.Title,a.Name Author,p.Name Publisher,b.CategoryId,b.TotalCopies,b.IsArchived,b.Revision,b.CreatedAt,
 b.AuthorId,b.PublisherId,b.CreatedDate,b.CreatedBy,b.ModifiedDate,b.ModifiedBy,b.DeletedDate,b.DeletedBy
 FROM library.Books b JOIN library.Authors a ON a.Id=b.AuthorId LEFT JOIN library.Publishers p ON p.Id=b.PublisherId;
GO
CREATE OR ALTER PROCEDURE library.Books_List @Search nvarchar(200)=NULL,@Page int=1,@PageSize int=12,@IncludeArchived bit=0 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page<1 OR @Page>100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 DECLARE @Pattern nvarchar(810)=N'%'+REPLACE(REPLACE(REPLACE(@Search,N'[',N'[[]'),N'%',N'[%]'),N'_',N'[_]')+N'%';
 DECLARE @IsbnSearch nvarchar(200)=REPLACE(REPLACE(@Search,N'-',N''),N' ',N'');
 SELECT b.*,c.Name CategoryName,
 b.TotalCopies-(SELECT COUNT(*) FROM library.Loans l WHERE l.BookId=b.Id AND l.ReturnedAt IS NULL) AvailableCopies,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.BookCovers bc WHERE bc.BookId=b.Id AND bc.DeletedDate IS NULL) THEN 1 ELSE 0 END AS bit) HasCover
 FROM library.BookCatalog b JOIN library.Categories c ON c.Id=b.CategoryId
 WHERE (@IncludeArchived=1 OR b.IsArchived=0) AND (@Search IS NULL OR b.Title LIKE @Pattern OR b.Author LIKE @Pattern
  OR b.Publisher LIKE @Pattern OR c.Name LIKE @Pattern OR b.ISBN LIKE @Pattern OR b.ISBN=@IsbnSearch)
 ORDER BY b.Title,b.Id OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM library.BookCatalog b JOIN library.Categories c ON c.Id=b.CategoryId
 WHERE (@IncludeArchived=1 OR b.IsArchived=0) AND (@Search IS NULL OR b.Title LIKE @Pattern OR b.Author LIKE @Pattern
  OR b.Publisher LIKE @Pattern OR c.Name LIKE @Pattern OR b.ISBN LIKE @Pattern OR b.ISBN=@IsbnSearch);
END;
GO
CREATE OR ALTER PROCEDURE library.Books_Get @Id int AS
BEGIN
 SET NOCOUNT ON;
 SELECT b.*,c.Name CategoryName,
 b.TotalCopies-(SELECT COUNT(*) FROM library.Loans l WHERE l.BookId=b.Id AND l.ReturnedAt IS NULL) AvailableCopies,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.BookCovers bc WHERE bc.BookId=b.Id AND bc.DeletedDate IS NULL) THEN 1 ELSE 0 END AS bit) HasCover
 FROM library.BookCatalog b JOIN library.Categories c ON c.Id=b.CategoryId WHERE b.Id=@Id;
END;
GO
CREATE OR ALTER PROCEDURE library.BookCovers_Get @Id int AS
BEGIN SET NOCOUNT ON; SELECT Content,ContentType FROM library.BookCovers WHERE BookId=@Id AND DeletedDate IS NULL; END;
GO
CREATE OR ALTER PROCEDURE library.Books_Save @Id int=0,@ISBN varchar(13),@Title nvarchar(200),@Author nvarchar(150),
 @Publisher nvarchar(150)=NULL,@CategoryId int,@TotalCopies int,@Revision int=0,@IsArchived bit=0,@ActorId nvarchar(450),
 @CoverImage varbinary(max)=NULL,@CoverContentType varchar(20)=NULL,@RemoveCover bit=0 AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @CoverImage IS NOT NULL AND (@CoverContentType IS NULL OR @CoverContentType<>'image/jpeg' OR DATALENGTH(@CoverImage) NOT BETWEEN 1 AND 2097152)
  THROW 50001,'Invalid processed cover image.',1;
 IF @CoverImage IS NULL AND @CoverContentType IS NOT NULL THROW 50001,'Missing cover image.',1;
 IF @CoverImage IS NOT NULL AND @RemoveCover=1 THROW 50001,'Choose a new cover or remove the current cover, not both.',1;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only active staff can save books.',1;
 SET @Author=LTRIM(RTRIM(@Author)); SET @Publisher=NULLIF(LTRIM(RTRIM(@Publisher)),N'');
 IF @Author IS NULL OR LEN(@Author)=0 THROW 50001,'Author is required.',1;
 DECLARE @AuthorId int,@PublisherId int;
 SELECT @AuthorId=Id FROM library.Authors WITH(UPDLOCK,HOLDLOCK) WHERE Name=@Author;
 IF @AuthorId IS NULL BEGIN INSERT library.Authors(Name,CreatedBy) VALUES(@Author,@ActorId);SET @AuthorId=SCOPE_IDENTITY();END;
 IF @Publisher IS NOT NULL
 BEGIN
  SELECT @PublisherId=Id FROM library.Publishers WITH(UPDLOCK,HOLDLOCK) WHERE Name=@Publisher;
  IF @PublisherId IS NULL BEGIN INSERT library.Publishers(Name,CreatedBy) VALUES(@Publisher,@ActorId);SET @PublisherId=SCOPE_IDENTITY();END;
 END;
 DECLARE @Now datetime2=SYSUTCDATETIME(),@WasArchived bit=0;
 IF @Id=0
 BEGIN
  INSERT library.Books(ISBN,Title,AuthorId,PublisherId,CategoryId,TotalCopies,IsArchived,CreatedBy,DeletedDate,DeletedBy)
   VALUES(@ISBN,@Title,@AuthorId,@PublisherId,@CategoryId,@TotalCopies,@IsArchived,@ActorId,CASE WHEN @IsArchived=1 THEN @Now END,CASE WHEN @IsArchived=1 THEN @ActorId END);
  SET @Id=SCOPE_IDENTITY();
 END
 ELSE
 BEGIN
  DECLARE @CurrentRevision int;
  SELECT @CurrentRevision=Revision,@WasArchived=IsArchived FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
  IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This book changed. Reload and try again.',1;
  DECLARE @ActiveLoans int=(SELECT COUNT(*) FROM library.Loans WHERE BookId=@Id AND ReturnedAt IS NULL);
  IF @TotalCopies<@ActiveLoans THROW 50001,'Copies cannot be fewer than outstanding loans.',1;
  IF @IsArchived=1 AND @ActiveLoans>0 THROW 50001,'Return outstanding loans before archiving this book.',1;
  UPDATE library.Books SET ISBN=@ISBN,Title=@Title,AuthorId=@AuthorId,PublisherId=@PublisherId,CategoryId=@CategoryId,
   TotalCopies=@TotalCopies,IsArchived=@IsArchived,Revision=Revision+1,ModifiedDate=@Now,ModifiedBy=@ActorId,
   DeletedDate=CASE WHEN @IsArchived=0 THEN NULL WHEN @WasArchived=0 THEN @Now ELSE DeletedDate END,
   DeletedBy=CASE WHEN @IsArchived=0 THEN NULL WHEN @WasArchived=0 THEN @ActorId ELSE DeletedBy END WHERE Id=@Id;
 END;
 IF @RemoveCover=1 UPDATE library.BookCovers SET DeletedDate=@Now,DeletedBy=@ActorId,ModifiedDate=@Now,ModifiedBy=@ActorId WHERE BookId=@Id AND DeletedDate IS NULL;
 ELSE IF @CoverImage IS NOT NULL
 BEGIN
  UPDATE library.BookCovers SET Content=@CoverImage,ContentType=@CoverContentType,UpdatedAt=@Now,ModifiedDate=@Now,ModifiedBy=@ActorId,DeletedDate=NULL,DeletedBy=NULL WHERE BookId=@Id;
  IF @@ROWCOUNT=0 INSERT library.BookCovers(BookId,Content,ContentType,CreatedBy) VALUES(@Id,@CoverImage,@CoverContentType,@ActorId);
 END;
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,
  CASE WHEN @WasArchived=1 AND @IsArchived=0 THEN 'Book restored' ELSE 'Book saved' END,CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Books_Delete @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only active staff can delete books.',1;
 DECLARE @CurrentRevision int,@Archived bit;
 SELECT @CurrentRevision=Revision,@Archived=IsArchived FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This book changed. Reload and try again.',1;
 IF @Archived=1 THROW 50001,'This book is already archived.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE BookId=@Id AND ReturnedAt IS NULL)
  THROW 50001,'Return outstanding loans before deleting this book.',1;
 UPDATE library.Books SET IsArchived=1,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=SYSUTCDATETIME(),DeletedBy=@ActorId WHERE Id=@Id;
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Book deleted (archived)',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
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
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only active staff can change members.',1;
 IF @Id=0
 BEGIN
  INSERT library.Members(MemberNumber,FullName,Email,Phone,Address,JoinedOn,ExpiresOn,IsActive,CreatedBy)
  VALUES(@MemberNumber,@FullName,@Email,@Phone,@Address,@JoinedOn,@ExpiresOn,@IsActive,@ActorId);
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
   JoinedOn=@JoinedOn,ExpiresOn=@ExpiresOn,IsActive=@IsActive,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId WHERE Id=@Id;
 END;
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Member saved',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Delete @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only active staff can change members.',1;
 DECLARE @CurrentRevision int,@Archived bit;
 SELECT @CurrentRevision=Revision,@Archived=IsArchived FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This member changed. Reload and try again.',1;
 IF @Archived=1 THROW 50001,'This member is already deleted.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE MemberId=@Id AND ReturnedAt IS NULL)
  THROW 50001,'Return outstanding books before deleting this member.',1;
 UPDATE library.Members SET IsArchived=1,IsActive=0,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=SYSUTCDATETIME(),DeletedBy=@ActorId WHERE Id=@Id;
 UPDATE library.Users SET SecurityStamp=CONVERT(nvarchar(100),NEWID()),ConcurrencyStamp=CONVERT(nvarchar(100),NEWID()) WHERE MemberId=@Id;
 UPDATE s SET RevokedAt=SYSUTCDATETIME(),ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId FROM library.UserSessions s JOIN library.Users u ON u.Id=s.UserId WHERE u.MemberId=@Id AND s.RevokedAt IS NULL;
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Member deleted (archived)',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Members_Restore @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only active staff can change members.',1;
 DECLARE @CurrentRevision int,@Archived bit;
 SELECT @CurrentRevision=Revision,@Archived=IsArchived FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This member changed. Reload and try again.',1;
 IF @Archived=0 THROW 50001,'This member is not deleted.',1;
 UPDATE library.Members SET IsArchived=0,IsActive=0,Revision=Revision+1,ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@ActorId,DeletedDate=NULL,DeletedBy=NULL WHERE Id=@Id;
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Member restored',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Categories_Save @Name nvarchar(80), @ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only active staff can create categories.',1;
 INSERT library.Categories(Name,CreatedBy) VALUES (@Name,@ActorId);
 DECLARE @Id int = SCOPE_IDENTITY();
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Category created',CONVERT(nvarchar(20),@Id));
 COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Categories_List AS
BEGIN SET NOCOUNT ON; SELECT Id,Name,CreatedDate,CreatedBy,ModifiedDate,ModifiedBy,DeletedDate,DeletedBy FROM library.Categories ORDER BY Name; END;
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
 INSERT library.Loans(BookId,MemberId,IssuedAt,DueDate,DailyFine,IssuedBy,CreatedBy) VALUES (@BookId,@MemberId,SYSUTCDATETIME(),@DueDate,@DailyFine,@ActorId,@ActorId);
 DECLARE @Id int=SCOPE_IDENTITY();
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Book issued',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_Return @Id int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only staff can return books.',1;
  DECLARE @MemberId int,@BookId int,@LockId int;
  SELECT @MemberId=MemberId,@BookId=BookId FROM library.Loans WHERE Id=@Id;
  IF @MemberId IS NULL THROW 50001,'Loan not found.',1;
  SELECT @LockId=Id FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId;
  SELECT @LockId=Id FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@BookId;
  DECLARE @Now datetime2=SYSUTCDATETIME();
  UPDATE library.Loans SET ReturnedAt=@Now,ReturnedBy=@ActorId,ModifiedDate=@Now,ModifiedBy=@ActorId,
   FineAmount=CASE WHEN DueDate<CONVERT(date,@Now) THEN DATEDIFF(day,DueDate,CONVERT(date,@Now))*DailyFine ELSE 0 END
  WHERE Id=@Id AND ReturnedAt IS NULL;
  IF @@ROWCOUNT=0 THROW 50001,'This loan has already been returned.',1;
  INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Book returned',CONVERT(nvarchar(20),@Id));
  COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_PayFine @Id int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRAN;
  IF NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role IN('Admin','Librarian') AND IsActive=1) THROW 50001,'Only staff can record fine payments.',1;
  DECLARE @Fine decimal(10,2),@Returned datetime2;
  SELECT @Fine=FineAmount,@Returned=ReturnedAt FROM library.Loans WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
  IF @Returned IS NULL OR @Fine IS NULL OR @Fine<=0 THROW 50001,'A returned loan with a fine is required.',1;
  IF EXISTS(SELECT 1 FROM library.Payments WHERE LoanId=@Id) THROW 50001,'Fine is already paid.',1;
  INSERT library.Payments(LoanId,Amount,PaidAt,ReceivedBy,CreatedBy) VALUES(@Id,@Fine,SYSUTCDATETIME(),@ActorId,@ActorId);
  INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Fine paid',CONVERT(nvarchar(20),@Id));
  COMMIT;
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
            PasswordHash,SecurityStamp,ConcurrencyStamp,LockoutEnabled,AccessFailedCount,FullName,Role,MemberId,CreatedBy)
        VALUES (@Id,@UserName,@NormalizedUserName,@Email,@NormalizedEmail,0,
            @PasswordHash,@SecurityStamp,@ConcurrencyStamp,1,0,@FullName,'Member',@MemberId,@Id);
        UPDATE library.Members SET CreatedBy=@Id WHERE Id=@MemberId;
        INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@Id,@Id,'Member registered',CONVERT(nvarchar(20),@MemberId));
        COMMIT;
        SELECT @MemberId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.Users_Create @Id nvarchar(450),@UserName nvarchar(256),@NormalizedUserName nvarchar(256),@Email nvarchar(256),@NormalizedEmail nvarchar(256),@EmailConfirmed bit,@PasswordHash nvarchar(max),@SecurityStamp nvarchar(100),@ConcurrencyStamp nvarchar(100),@LockoutEnd datetimeoffset=NULL,@LockoutEnabled bit,@AccessFailedCount int,@FullName nvarchar(120),@Role nvarchar(20),@MemberId int=NULL,@IsActive bit=1,@ActorId nvarchar(450)=NULL,@AuditAction nvarchar(80)=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 IF @ActorId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM library.Users WHERE Id=@ActorId AND Role='Admin' AND IsActive=1) THROW 50001,'An active administrator is required.',1;
 IF @Role='Member' AND NOT EXISTS(SELECT 1 FROM library.Members WITH(UPDLOCK,HOLDLOCK) WHERE Id=@MemberId AND IsArchived=0 AND Email=@Email) THROW 50001,'Select a non-deleted member with matching email.',1;
 INSERT library.Users(Id,UserName,NormalizedUserName,Email,NormalizedEmail,EmailConfirmed,PasswordHash,SecurityStamp,ConcurrencyStamp,LockoutEnd,LockoutEnabled,AccessFailedCount,FullName,Role,MemberId,IsActive,CreatedBy)
 VALUES(@Id,@UserName,@NormalizedUserName,@Email,@NormalizedEmail,@EmailConfirmed,@PasswordHash,@SecurityStamp,@ConcurrencyStamp,@LockoutEnd,@LockoutEnabled,@AccessFailedCount,@FullName,@Role,@MemberId,@IsActive,COALESCE(@ActorId,@Id));
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(COALESCE(@ActorId,@Id),COALESCE(@ActorId,@Id),'User created',@Id);
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
 ModifiedAt=SYSUTCDATETIME(),ModifiedBy=CASE WHEN @AuditAction IS NOT NULL THEN COALESCE(@ActorId,@Id) ELSE @ActorId END WHERE Id=@Id;
 IF @AuditAction IS NOT NULL
 BEGIN
  INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(COALESCE(@ActorId,@Id),COALESCE(@ActorId,@Id),@AuditAction,@Id);
  UPDATE library.UserSessions SET RevokedAt=SYSUTCDATETIME(),ModifiedDate=SYSUTCDATETIME(),ModifiedBy=COALESCE(@ActorId,@Id) WHERE UserId=@Id AND RevokedAt IS NULL;
 END;
 COMMIT;SELECT 1;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
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
  INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Library settings updated','1');
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
  INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,'Fine policy updated','1');
  COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE library.UserActivity_Add @ActorId nvarchar(450),@Action nvarchar(80) AS
BEGIN
 SET NOCOUNT ON;
 IF @Action NOT IN('Signed in','Signed out') THROW 50001,'Invalid authentication event.',1;
 INSERT library.AuditEvents(CreatedBy,ActorId,Action,EntityId) VALUES(@ActorId,@ActorId,@Action,@ActorId);
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Create @Id uniqueidentifier,@UserId nvarchar(450),@Device nvarchar(160),@AbsoluteHours int AS
BEGIN
 SET NOCOUNT ON;
 IF @AbsoluteHours NOT BETWEEN 1 AND 24 THROW 50001,'Invalid session policy.',1;
 INSERT library.UserSessions(Id,UserId,Device,ExpiresAt,CreatedBy)
 SELECT @Id,u.Id,@Device,DATEADD(hour,@AbsoluteHours,SYSUTCDATETIME()),@UserId FROM library.Users u
 LEFT JOIN library.Members m WITH(HOLDLOCK) ON m.Id=u.MemberId
 WHERE u.Id=@UserId AND u.IsActive=1 AND (u.LockoutEnd IS NULL OR u.LockoutEnd<=SYSUTCDATETIME()) AND (u.Role<>'Member' OR (m.Id IS NOT NULL AND m.IsArchived=0));
 IF @@ROWCOUNT=0 THROW 50001,'Account unavailable.',1;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Validate @Id uniqueidentifier,@UserId nvarchar(450),@IdleMinutes int AS
BEGIN
 SET NOCOUNT ON;
 IF @IdleMinutes NOT BETWEEN 1 AND 120 THROW 50001,'Invalid session policy.',1;
 UPDATE s SET LastSeenAt=SYSUTCDATETIME(),ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@UserId FROM library.UserSessions s JOIN library.Users u ON u.Id=s.UserId LEFT JOIN library.Members m ON m.Id=u.MemberId
 WHERE s.Id=@Id AND s.UserId=@UserId AND s.RevokedAt IS NULL AND s.ExpiresAt>SYSUTCDATETIME() AND s.LastSeenAt>DATEADD(minute,-@IdleMinutes,SYSUTCDATETIME()) AND u.IsActive=1 AND (u.Role<>'Member' OR (m.Id IS NOT NULL AND m.IsArchived=0));
 SELECT @@ROWCOUNT;
END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_Revoke @Id uniqueidentifier,@UserId nvarchar(450) AS
BEGIN SET NOCOUNT ON; UPDATE library.UserSessions SET RevokedAt=SYSUTCDATETIME(),ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@UserId WHERE Id=@Id AND UserId=@UserId AND RevokedAt IS NULL; END;
GO
CREATE OR ALTER PROCEDURE library.Sessions_RevokeAll @UserId nvarchar(450),@ExceptId uniqueidentifier=NULL AS
BEGIN SET NOCOUNT ON; UPDATE library.UserSessions SET RevokedAt=SYSUTCDATETIME(),ModifiedDate=SYSUTCDATETIME(),ModifiedBy=@UserId WHERE UserId=@UserId AND RevokedAt IS NULL AND (@ExceptId IS NULL OR Id<>@ExceptId); END;
GO
CREATE OR ALTER PROCEDURE library.Reports_Query
 @Report varchar(20),@FromDate date=NULL,@ToDate date=NULL,@MemberId int=NULL,@BookId int=NULL,@Status varchar(20)='all',@Page int=1,@PageSize int=20 AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @Now datetime2=SYSUTCDATETIME(),@Today date=CONVERT(date,SYSUTCDATETIME());
 IF @Report NOT IN('stock','issues','returns','overdue','members','collections','monthly') THROW 50001,'Invalid report.',1;
 IF @Page NOT BETWEEN 1 AND 100000 OR @PageSize NOT BETWEEN 1 AND 5001 THROW 50001,'Invalid report page.',1;
 IF @FromDate>@ToDate THROW 50001,'From date cannot be after To date.',1;
 IF @Status<>'all' AND NOT((@Report='stock' AND @Status IN('active','available','unavailable','archived')) OR (@Report IN('issues','members') AND @Status IN('active','returned','overdue')) OR (@Report='returns' AND @Status IN('pending','paid','no-fine')) OR (@Report='overdue' AND @Status='overdue') OR (@Report='collections' AND @Status='paid') OR (@Report='monthly' AND @Status IN('issued','returned','paid'))) THROW 50001,'Invalid report status.',1;
 CREATE TABLE #Rows(RecordId int NOT NULL,Title nvarchar(200) NULL,ISBN varchar(13) NULL,Author nvarchar(150) NULL,Publisher nvarchar(150) NULL,Category nvarchar(80) NULL,
 MemberName nvarchar(120) NULL,MemberNumber varchar(30) NULL,StaffName nvarchar(120) NULL,Status nvarchar(30) NULL,EventDate datetime2 NULL,DueDate date NULL,ReturnedAt datetime2 NULL,
 TotalCopies int NOT NULL DEFAULT 0,AvailableCopies int NOT NULL DEFAULT 0,IssuedCount int NOT NULL DEFAULT 0,ReturnedCount int NOT NULL DEFAULT 0,ActiveCount int NOT NULL DEFAULT 0,OverdueCount int NOT NULL DEFAULT 0,LateDays int NOT NULL DEFAULT 0,PaymentCount int NOT NULL DEFAULT 0,
 DailyFine decimal(10,2) NOT NULL DEFAULT 0,FineAmount decimal(19,2) NOT NULL DEFAULT 0,CollectedAmount decimal(19,2) NOT NULL DEFAULT 0,PendingFine decimal(19,2) NOT NULL DEFAULT 0);
 IF @Report='stock'
 BEGIN
  SELECT b.*,c.Name CategoryName,b.TotalCopies-(SELECT COUNT(*) FROM library.Loans l WHERE l.BookId=b.Id AND l.ReturnedAt IS NULL) StockAvailable
  INTO #Stock FROM library.BookCatalog b JOIN library.Categories c ON c.Id=b.CategoryId
  WHERE (@BookId IS NULL OR b.Id=@BookId) AND (@FromDate IS NULL OR b.CreatedAt>=@FromDate) AND (@ToDate IS NULL OR b.CreatedAt<DATEADD(day,1,CONVERT(datetime2,@ToDate)))
  AND (@MemberId IS NULL OR EXISTS(SELECT 1 FROM library.Loans l WHERE l.BookId=b.Id AND l.MemberId=@MemberId));
  INSERT #Rows(RecordId,Title,ISBN,Author,Publisher,Category,EventDate,TotalCopies,AvailableCopies,ActiveCount,Status)
  SELECT Id,Title,ISBN,Author,Publisher,CategoryName,CreatedAt,TotalCopies,CASE WHEN IsArchived=1 THEN 0 ELSE StockAvailable END,TotalCopies-StockAvailable,
   CASE WHEN IsArchived=1 THEN 'Archived' WHEN StockAvailable>0 THEN 'Available' ELSE 'Unavailable' END FROM #Stock
  WHERE @Status='all' OR (@Status='active' AND IsArchived=0) OR (@Status='archived' AND IsArchived=1) OR (@Status='available' AND IsArchived=0 AND StockAvailable>0) OR (@Status='unavailable' AND IsArchived=0 AND StockAvailable=0);
 END
 ELSE
 BEGIN
  SELECT r.Id,r.BookId,r.MemberId,r.Title,r.ISBN,r.MemberName,r.MemberNumber,r.IssuedAt,r.DueDate,r.ReturnedAt,r.ReturnedByName,r.LateDays,r.DailyFine,r.FineAmount,r.PaymentId,r.PaidAt,r.PaidAmount,r.ReceivedByName
  INTO #Loans FROM library.CirculationRecords r WHERE (@MemberId IS NULL OR r.MemberId=@MemberId) AND (@BookId IS NULL OR r.BookId=@BookId)
  AND (@Report='monthly' OR ((@FromDate IS NULL OR CASE @Report WHEN 'returns' THEN r.ReturnedAt WHEN 'collections' THEN r.PaidAt WHEN 'overdue' THEN CONVERT(datetime2,r.DueDate) ELSE r.IssuedAt END>=@FromDate)
   AND (@ToDate IS NULL OR CASE @Report WHEN 'returns' THEN r.ReturnedAt WHEN 'collections' THEN r.PaidAt WHEN 'overdue' THEN CONVERT(datetime2,r.DueDate) ELSE r.IssuedAt END<DATEADD(day,1,CONVERT(datetime2,@ToDate)))));
  IF @Report IN('issues','overdue','returns')
  INSERT #Rows(RecordId,Title,ISBN,MemberName,MemberNumber,StaffName,Status,EventDate,DueDate,ReturnedAt,IssuedCount,ReturnedCount,ActiveCount,OverdueCount,LateDays,DailyFine,FineAmount,CollectedAmount,PendingFine)
  SELECT Id,Title,ISBN,MemberName,MemberNumber,CASE WHEN @Report='returns' THEN ReturnedByName ELSE NULL END,
   CASE WHEN @Report='returns' THEN CASE WHEN PaymentId IS NOT NULL THEN 'Paid' WHEN FineAmount>0 THEN 'Pending' ELSE 'No fine' END ELSE CASE WHEN ReturnedAt IS NOT NULL THEN 'Returned' WHEN DueDate<@Today THEN 'Overdue' ELSE 'Active' END END,
   CASE WHEN @Report='returns' THEN ReturnedAt ELSE IssuedAt END,DueDate,ReturnedAt,1,CASE WHEN ReturnedAt IS NOT NULL THEN 1 ELSE 0 END,CASE WHEN ReturnedAt IS NULL THEN 1 ELSE 0 END,
   CASE WHEN ReturnedAt IS NULL AND DueDate<@Today THEN 1 ELSE 0 END,LateDays,DailyFine,FineAmount,PaidAmount,CASE WHEN ReturnedAt IS NOT NULL AND PaymentId IS NULL THEN FineAmount ELSE 0 END
  FROM #Loans
  WHERE (@Report<>'returns' OR ReturnedAt IS NOT NULL) AND (@Report<>'overdue' OR (ReturnedAt IS NULL AND DueDate<@Today))
  AND (@FromDate IS NULL OR CASE @Report WHEN 'returns' THEN ReturnedAt WHEN 'overdue' THEN CONVERT(datetime2,DueDate) ELSE IssuedAt END>=@FromDate)
  AND (@ToDate IS NULL OR CASE @Report WHEN 'returns' THEN ReturnedAt WHEN 'overdue' THEN CONVERT(datetime2,DueDate) ELSE IssuedAt END<DATEADD(day,1,CONVERT(datetime2,@ToDate)))
  AND (@Status='all' OR (@Report='returns' AND ((@Status='paid' AND PaymentId IS NOT NULL) OR (@Status='pending' AND PaymentId IS NULL AND FineAmount>0) OR (@Status='no-fine' AND FineAmount=0)))
   OR (@Report IN('issues','overdue') AND ((@Status='active' AND ReturnedAt IS NULL) OR (@Status='returned' AND ReturnedAt IS NOT NULL) OR (@Status='overdue' AND ReturnedAt IS NULL AND DueDate<@Today))));
  IF @Report='collections'
  INSERT #Rows(RecordId,Title,ISBN,MemberName,MemberNumber,StaffName,Status,EventDate,PaymentCount,CollectedAmount)
  SELECT PaymentId,Title,ISBN,MemberName,MemberNumber,ReceivedByName,'Paid',PaidAt,1,PaidAmount FROM #Loans
  WHERE PaymentId IS NOT NULL AND (@FromDate IS NULL OR PaidAt>=@FromDate) AND (@ToDate IS NULL OR PaidAt<DATEADD(day,1,CONVERT(datetime2,@ToDate)));
  IF @Report='members'
  INSERT #Rows(RecordId,MemberName,MemberNumber,IssuedCount,ReturnedCount,ActiveCount,OverdueCount,FineAmount,CollectedAmount,PendingFine)
  SELECT MemberId,MemberName,MemberNumber,COUNT(*),COUNT(CASE WHEN ReturnedAt IS NOT NULL THEN 1 END),COUNT(CASE WHEN ReturnedAt IS NULL THEN 1 END),COUNT(CASE WHEN ReturnedAt IS NULL AND DueDate<@Today THEN 1 END),SUM(FineAmount),SUM(PaidAmount),SUM(CASE WHEN ReturnedAt IS NOT NULL AND PaymentId IS NULL THEN FineAmount ELSE 0 END)
  FROM #Loans WHERE (@FromDate IS NULL OR IssuedAt>=@FromDate) AND (@ToDate IS NULL OR IssuedAt<DATEADD(day,1,CONVERT(datetime2,@ToDate)))
  AND (@Status='all' OR (@Status='active' AND ReturnedAt IS NULL) OR (@Status='returned' AND ReturnedAt IS NOT NULL) OR (@Status='overdue' AND ReturnedAt IS NULL AND DueDate<@Today))
  GROUP BY MemberId,MemberName,MemberNumber;
  IF @Report='monthly'
  BEGIN
   IF @FromDate IS NULL OR @ToDate IS NULL OR DATEDIFF(month,@FromDate,@ToDate)>119 THROW 50001,'Monthly activity requires a range up to 120 months.',1;
   ;WITH Months AS(SELECT DATEFROMPARTS(YEAR(@FromDate),MONTH(@FromDate),1) MonthStart UNION ALL SELECT DATEADD(month,1,MonthStart) FROM Months WHERE DATEADD(month,1,MonthStart)<=DATEFROMPARTS(YEAR(@ToDate),MONTH(@ToDate),1)),
   Events AS(
    SELECT IssuedAt EventAt,1 Issued,0 Returned,0 Paid,CONVERT(decimal(19,2),0) Collected FROM #Loans WHERE @Status IN('all','issued')
    UNION ALL SELECT ReturnedAt,0,1,0,0 FROM #Loans WHERE ReturnedAt IS NOT NULL AND @Status IN('all','returned')
    UNION ALL SELECT PaidAt,0,0,1,PaidAmount FROM #Loans WHERE PaidAt IS NOT NULL AND @Status IN('all','paid')
   ), Activity AS(SELECT DATEFROMPARTS(YEAR(EventAt),MONTH(EventAt),1) MonthStart,SUM(Issued) Issued,SUM(Returned) Returned,SUM(Paid) Paid,SUM(Collected) Collected FROM Events WHERE EventAt>=@FromDate AND EventAt<DATEADD(day,1,CONVERT(datetime2,@ToDate)) GROUP BY YEAR(EventAt),MONTH(EventAt))
   INSERT #Rows(RecordId,EventDate,IssuedCount,ReturnedCount,PaymentCount,CollectedAmount)
   SELECT YEAR(m.MonthStart)*100+MONTH(m.MonthStart),m.MonthStart,COALESCE(a.Issued,0),COALESCE(a.Returned,0),COALESCE(a.Paid,0),COALESCE(a.Collected,0) FROM Months m LEFT JOIN Activity a ON a.MonthStart=m.MonthStart OPTION(MAXRECURSION 120);
  END;
 END;
 SELECT * FROM #Rows ORDER BY CASE WHEN @Report='stock' THEN Title WHEN @Report='members' THEN MemberName END,
 CASE WHEN @Report='monthly' THEN EventDate END,CASE WHEN @Report<>'monthly' THEN EventDate END DESC,RecordId DESC
 OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) TotalRecords,COALESCE(SUM(CONVERT(bigint,TotalCopies)),0) TotalCopies,COALESCE(SUM(CONVERT(bigint,AvailableCopies)),0) AvailableCopies,
 COALESCE(SUM(CONVERT(bigint,IssuedCount)),0) IssuedCount,COALESCE(SUM(CONVERT(bigint,ReturnedCount)),0) ReturnedCount,COALESCE(SUM(CONVERT(bigint,OverdueCount)),0) OverdueCount,COALESCE(SUM(FineAmount),0) FineAmount,COALESCE(SUM(CollectedAmount),0) CollectedAmount,@Now GeneratedAt FROM #Rows;
END;
GO
CREATE OR ALTER PROCEDURE library.Loans_List @Search nvarchar(200)=NULL,@Status varchar(15)='active',@MemberId int=NULL,@Page int=1,@PageSize int=12 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page<1 OR @Page>100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 SELECT l.Id,b.Title,m.FullName MemberName,m.MemberNumber,l.MemberId,l.BookId,l.IssuedAt,l.DueDate,l.ReturnedAt,l.CreatedDate,l.CreatedBy,l.ModifiedDate,l.ModifiedBy,l.DeletedDate,l.DeletedBy,
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
-- Redundant labels are removed only after all read/write projections use their foreign keys.
IF COL_LENGTH('library.Books','Author') IS NOT NULL ALTER TABLE library.Books DROP COLUMN Author;
IF COL_LENGTH('library.Books','Publisher') IS NOT NULL ALTER TABLE library.Books DROP COLUMN Publisher;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Books') AND name='IX_Books_AuthorId') CREATE INDEX IX_Books_AuthorId ON library.Books(AuthorId) INCLUDE(Title,ISBN);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Books') AND name='IX_Books_PublisherId') CREATE INDEX IX_Books_PublisherId ON library.Books(PublisherId) INCLUDE(Title,ISBN);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Books') AND name='IX_Books_CategoryId') CREATE INDEX IX_Books_CategoryId ON library.Books(CategoryId) INCLUDE(Title,ISBN);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Loans') AND name='IX_Loans_MemberHistory') CREATE INDEX IX_Loans_MemberHistory ON library.Loans(MemberId,IssuedAt DESC,Id DESC) INCLUDE(BookId,DueDate,ReturnedAt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Loans') AND name='IX_Loans_BookHistory') CREATE INDEX IX_Loans_BookHistory ON library.Loans(BookId,IssuedAt DESC,Id DESC) INCLUDE(MemberId,DueDate,ReturnedAt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Payments') AND name='IX_Payments_PaidAt') CREATE INDEX IX_Payments_PaidAt ON library.Payments(PaidAt,Id) INCLUDE(LoanId,Amount,ReceivedBy);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Books') AND name='IX_Books_DeletedDate') CREATE INDEX IX_Books_DeletedDate ON library.Books(DeletedDate) INCLUDE(DeletedBy) WHERE DeletedDate IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Members') AND name='IX_Members_DeletedDate') CREATE INDEX IX_Members_DeletedDate ON library.Members(DeletedDate) INCLUDE(DeletedBy) WHERE DeletedDate IS NOT NULL;
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'LibraryManagement.SchemaVersion') EXEC sys.sp_updateextendedproperty @name=N'LibraryManagement.SchemaVersion',@value=13;
ELSE EXEC sys.sp_addextendedproperty @name=N'LibraryManagement.SchemaVersion',@value=13;
COMMIT;
GO
