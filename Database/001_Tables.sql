-- Select LibraryManagement in SSMS before running this script.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
IF SCHEMA_ID(N'library') IS NULL EXEC(N'CREATE SCHEMA library AUTHORIZATION dbo');
GO
BEGIN TRAN;
IF OBJECT_ID(N'library.Users', N'U') IS NULL
CREATE TABLE library.Users (
    Id nvarchar(450) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    UserName nvarchar(256) NOT NULL,
    NormalizedUserName nvarchar(256) NOT NULL,
    Email nvarchar(256) NOT NULL,
    NormalizedEmail nvarchar(256) NOT NULL,
    EmailConfirmed bit NOT NULL DEFAULT 0,
    PasswordHash nvarchar(max) NULL,
    SecurityStamp nvarchar(100) NULL,
    ConcurrencyStamp nvarchar(100) NOT NULL,
    LockoutEnd datetimeoffset NULL,
    LockoutEnabled bit NOT NULL DEFAULT 1,
    AccessFailedCount int NOT NULL DEFAULT 0,
    FullName nvarchar(120) NOT NULL,
    Role nvarchar(20) NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN ('Admin','Librarian','Member')),
    MemberId int NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Users_UserName UNIQUE (NormalizedUserName),
    CONSTRAINT UQ_Users_Email UNIQUE (NormalizedEmail),
    CONSTRAINT CK_Users_Membership CHECK ((Role = 'Member' AND MemberId IS NOT NULL) OR (Role <> 'Member' AND MemberId IS NULL))
);
IF OBJECT_ID(N'library.Categories', N'U') IS NULL
CREATE TABLE library.Categories (
    Id int IDENTITY CONSTRAINT PK_Categories PRIMARY KEY,
    Name nvarchar(80) NOT NULL CONSTRAINT UQ_Categories_Name UNIQUE
);
IF OBJECT_ID(N'library.Books', N'U') IS NULL
CREATE TABLE library.Books (
    Id int IDENTITY CONSTRAINT PK_Books PRIMARY KEY,
    ISBN varchar(13) NOT NULL CONSTRAINT UQ_Books_ISBN UNIQUE,
    Title nvarchar(200) NOT NULL,
    Author nvarchar(150) NOT NULL,
    Publisher nvarchar(150) NULL,
    CategoryId int NOT NULL REFERENCES library.Categories(Id),
    TotalCopies int NOT NULL CONSTRAINT CK_Books_Copies CHECK (TotalCopies BETWEEN 1 AND 10000),
    IsArchived bit NOT NULL DEFAULT 0,
    Revision int NOT NULL DEFAULT 1,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID(N'library.Members', N'U') IS NULL
CREATE TABLE library.Members (
    Id int IDENTITY CONSTRAINT PK_Members PRIMARY KEY,
    MemberNumber varchar(30) NOT NULL CONSTRAINT UQ_Members_Number UNIQUE,
    FullName nvarchar(120) NOT NULL,
    Email nvarchar(256) NOT NULL CONSTRAINT UQ_Members_Email UNIQUE,
    Phone nvarchar(25) NULL,
    ExpiresOn date NOT NULL,
    IsActive bit NOT NULL DEFAULT 1,
    Revision int NOT NULL DEFAULT 1,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID(N'library.FK_Users_Members',N'F') IS NULL
ALTER TABLE library.Users ADD CONSTRAINT FK_Users_Members FOREIGN KEY (MemberId) REFERENCES library.Members(Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.Users') AND name=N'UX_Users_Member')
CREATE UNIQUE INDEX UX_Users_Member ON library.Users(MemberId) WHERE MemberId IS NOT NULL;
IF OBJECT_ID(N'library.Loans', N'U') IS NULL
CREATE TABLE library.Loans (
    Id int IDENTITY CONSTRAINT PK_Loans PRIMARY KEY,
    BookId int NOT NULL REFERENCES library.Books(Id),
    MemberId int NOT NULL REFERENCES library.Members(Id),
    IssuedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    DueDate date NOT NULL,
    ReturnedAt datetime2 NULL,
    DailyFine decimal(10,2) NOT NULL CHECK (DailyFine BETWEEN 0 AND 1000),
    FineAmount decimal(10,2) NOT NULL DEFAULT 0 CHECK (FineAmount >= 0),
    IssuedBy nvarchar(450) NOT NULL REFERENCES library.Users(Id),
    ReturnedBy nvarchar(450) NULL REFERENCES library.Users(Id)
);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.Loans') AND name=N'UX_Loans_ActiveMemberBook')
CREATE UNIQUE INDEX UX_Loans_ActiveMemberBook ON library.Loans(MemberId, BookId) WHERE ReturnedAt IS NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.Loans') AND name=N'IX_Loans_ActiveBook')
CREATE INDEX IX_Loans_ActiveBook ON library.Loans(BookId) INCLUDE(MemberId, DueDate) WHERE ReturnedAt IS NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.Loans') AND name=N'IX_Loans_DueDate')
CREATE INDEX IX_Loans_DueDate ON library.Loans(DueDate) INCLUDE(BookId,MemberId) WHERE ReturnedAt IS NULL;
IF OBJECT_ID(N'library.Payments', N'U') IS NULL
CREATE TABLE library.Payments (
    Id int IDENTITY CONSTRAINT PK_Payments PRIMARY KEY,
    LoanId int NOT NULL CONSTRAINT UQ_Payments_Loan UNIQUE REFERENCES library.Loans(Id),
    Amount decimal(10,2) NOT NULL CHECK (Amount > 0),
    PaidAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ReceivedBy nvarchar(450) NOT NULL REFERENCES library.Users(Id)
);
IF OBJECT_ID(N'library.AuditEvents', N'U') IS NULL
CREATE TABLE library.AuditEvents (
    Id bigint IDENTITY CONSTRAINT PK_AuditEvents PRIMARY KEY,
    ActorId nvarchar(450) NOT NULL REFERENCES library.Users(Id),
    Action nvarchar(80) NOT NULL,
    EntityId nvarchar(450) NOT NULL,
    OccurredAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.AuditEvents') AND name=N'IX_AuditEvents_OccurredAt')
CREATE INDEX IX_AuditEvents_OccurredAt ON library.AuditEvents(OccurredAt DESC);
INSERT library.Categories(Name) SELECT Name FROM (VALUES (N'Technology'),(N'Literature'),(N'Science'),(N'History'),(N'Business')) seed(Name)
WHERE NOT EXISTS(SELECT 1 FROM library.Categories c WHERE c.Name=seed.Name);
COMMIT;
GO
-- Runtime credentials need procedure execution only. Do not grant db_owner.
IF DATABASE_PRINCIPAL_ID(N'library_app') IS NULL CREATE ROLE library_app;
GRANT EXECUTE ON SCHEMA::library TO library_app;
GO
