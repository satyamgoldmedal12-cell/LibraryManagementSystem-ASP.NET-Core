SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('library.Books') AND name='IX_Books_CreatedAt')
 CREATE INDEX IX_Books_CreatedAt ON library.Books(CreatedAt,Id) INCLUDE(IsArchived,CategoryId,TotalCopies);
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
  INTO #Stock FROM library.Books b JOIN library.Categories c ON c.Id=b.CategoryId
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
