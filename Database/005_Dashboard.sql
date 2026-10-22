-- Apply after 001-004. Replaces the dashboard read procedure and adds reporting indexes.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.Loans') AND name=N'IX_Loans_IssuedAt')
CREATE INDEX IX_Loans_IssuedAt ON library.Loans(IssuedAt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'library.Loans') AND name=N'IX_Loans_ReturnedAt')
CREATE INDEX IX_Loans_ReturnedAt ON library.Loans(ReturnedAt);
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
        FROM library.Members WITH(HOLDLOCK);
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
