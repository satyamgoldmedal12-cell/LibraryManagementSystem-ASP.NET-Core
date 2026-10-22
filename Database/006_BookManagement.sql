-- Apply after 001 through 005. No existing books, loans, or payments are removed.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'library.BookCovers',N'U') IS NULL
CREATE TABLE library.BookCovers (
 BookId int NOT NULL CONSTRAINT PK_BookCovers PRIMARY KEY REFERENCES library.Books(Id),
 Content varbinary(max) NOT NULL,
 ContentType varchar(20) NOT NULL CONSTRAINT CK_BookCovers_Type CHECK(ContentType='image/jpeg'),
 UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT CK_BookCovers_Size CHECK(DATALENGTH(Content) BETWEEN 1 AND 2097152)
);
GO
CREATE OR ALTER PROCEDURE library.Books_List @Search nvarchar(200)=NULL,@Page int=1,@PageSize int=12,@IncludeArchived bit=0 AS
BEGIN
 SET NOCOUNT ON;
 IF @Page<1 OR @Page>100000 OR @PageSize NOT BETWEEN 1 AND 100 THROW 50001,'Invalid pagination.',1;
 DECLARE @Pattern nvarchar(810)=N'%'+REPLACE(REPLACE(REPLACE(@Search,N'[',N'[[]'),N'%',N'[%]'),N'_',N'[_]')+N'%';
 DECLARE @IsbnSearch nvarchar(200)=REPLACE(REPLACE(@Search,N'-',N''),N' ',N'');
 SELECT b.*,c.Name CategoryName,
 b.TotalCopies-(SELECT COUNT(*) FROM library.Loans l WHERE l.BookId=b.Id AND l.ReturnedAt IS NULL) AvailableCopies,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.BookCovers bc WHERE bc.BookId=b.Id) THEN 1 ELSE 0 END AS bit) HasCover
 FROM library.Books b JOIN library.Categories c ON c.Id=b.CategoryId
 WHERE (@IncludeArchived=1 OR b.IsArchived=0) AND (@Search IS NULL OR b.Title LIKE @Pattern OR b.Author LIKE @Pattern
  OR b.Publisher LIKE @Pattern OR c.Name LIKE @Pattern OR b.ISBN LIKE @Pattern OR b.ISBN=@IsbnSearch)
 ORDER BY b.Title,b.Id OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
 SELECT COUNT(*) FROM library.Books b JOIN library.Categories c ON c.Id=b.CategoryId
 WHERE (@IncludeArchived=1 OR b.IsArchived=0) AND (@Search IS NULL OR b.Title LIKE @Pattern OR b.Author LIKE @Pattern
  OR b.Publisher LIKE @Pattern OR c.Name LIKE @Pattern OR b.ISBN LIKE @Pattern OR b.ISBN=@IsbnSearch);
END;
GO
CREATE OR ALTER PROCEDURE library.Books_Get @Id int AS
BEGIN
 SET NOCOUNT ON;
 SELECT b.*,c.Name CategoryName,
 b.TotalCopies-(SELECT COUNT(*) FROM library.Loans l WHERE l.BookId=b.Id AND l.ReturnedAt IS NULL) AvailableCopies,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM library.BookCovers bc WHERE bc.BookId=b.Id) THEN 1 ELSE 0 END AS bit) HasCover
 FROM library.Books b JOIN library.Categories c ON c.Id=b.CategoryId WHERE b.Id=@Id;
END;
GO
CREATE OR ALTER PROCEDURE library.BookCovers_Get @Id int AS
BEGIN SET NOCOUNT ON; SELECT Content,ContentType FROM library.BookCovers WHERE BookId=@Id; END;
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
 DECLARE @WasArchived bit=0;
 IF @Id=0
 BEGIN
  INSERT library.Books(ISBN,Title,Author,Publisher,CategoryId,TotalCopies,IsArchived)
   VALUES(@ISBN,@Title,@Author,@Publisher,@CategoryId,@TotalCopies,@IsArchived);
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
  UPDATE library.Books SET ISBN=@ISBN,Title=@Title,Author=@Author,Publisher=@Publisher,CategoryId=@CategoryId,
   TotalCopies=@TotalCopies,IsArchived=@IsArchived,Revision=Revision+1 WHERE Id=@Id;
 END;
 IF @RemoveCover=1 DELETE library.BookCovers WHERE BookId=@Id;
 ELSE IF @CoverImage IS NOT NULL
 BEGIN
  UPDATE library.BookCovers SET Content=@CoverImage,ContentType=@CoverContentType,UpdatedAt=SYSUTCDATETIME() WHERE BookId=@Id;
  IF @@ROWCOUNT=0 INSERT library.BookCovers(BookId,Content,ContentType) VALUES(@Id,@CoverImage,@CoverContentType);
 END;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,
  CASE WHEN @WasArchived=1 AND @IsArchived=0 THEN 'Book restored' ELSE 'Book saved' END,CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
-- Deletion is reversible archiving; loan, fine, audit and cover history remain intact.
CREATE OR ALTER PROCEDURE library.Books_Delete @Id int,@Revision int,@ActorId nvarchar(450) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @CurrentRevision int,@Archived bit;
 SELECT @CurrentRevision=Revision,@Archived=IsArchived FROM library.Books WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
 IF @CurrentRevision IS NULL OR @CurrentRevision<>@Revision THROW 50001,'This book changed. Reload and try again.',1;
 IF @Archived=1 THROW 50001,'This book is already archived.',1;
 IF EXISTS(SELECT 1 FROM library.Loans WHERE BookId=@Id AND ReturnedAt IS NULL)
  THROW 50001,'Return outstanding loans before deleting this book.',1;
 UPDATE library.Books SET IsArchived=1,Revision=Revision+1 WHERE Id=@Id;
 INSERT library.AuditEvents(ActorId,Action,EntityId) VALUES(@ActorId,'Book deleted (archived)',CONVERT(nvarchar(20),@Id));
 COMMIT; SELECT @Id;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
