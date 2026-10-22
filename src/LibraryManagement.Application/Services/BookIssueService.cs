namespace LibraryManagement.Application.Services;

public sealed class BookIssueService(ILibraryRepository repository,ILibraryService commands,LendingOptions policy,TimeProvider clock) : IBookIssueService
{
    public async Task<IssueAssessment> AssessAsync(int bookId,int memberId,DateTime? dueDate,CancellationToken ct)
    {
        var context=await repository.IssueContextAsync(bookId,memberId,ct);
        var now = LibraryManagement.Domain.Rules.IndiaTime.FromUtc(clock.GetUtcNow()).DateTime;
        var today = now.Date;
        var max = today.AddDays(90);
        if(context.MemberExpiresOn is DateTime expiry&&expiry.Date<max)max=expiry.Date;
        var maximumBooks=context.ConfiguredMaxActiveLoans??policy.MaxActiveLoans;
        var suggested=today.AddDays(context.DefaultIssueDays??14);
        if(max>=today.AddDays(1)&&suggested>max)suggested=max;
        var due=dueDate?.Date??suggested;
        var problems=new List<string>();
        if(context.BookId is null)problems.Add(bookId>0?"The selected book no longer exists.":"Select a book.");
        else
        {
            if(context.BookArchived)problems.Add("This book is archived and cannot be issued.");
            if(context.AvailableCopies<1)problems.Add("No copies are available for this book.");
        }
        if(context.MemberId is null)problems.Add(memberId>0?"The selected member no longer exists.":"Select a member.");
        else
        {
            if(context.MemberArchived||!context.MemberActive)problems.Add("This member is inactive or deleted.");
            if(context.MemberExpiresOn is null||context.MemberExpiresOn.Value.Date<today)problems.Add("This membership has expired.");
            if(context.ActiveLoans>=maximumBooks)problems.Add("Member has reached the borrowing limit.");
            if(context.DuplicateActiveIssue)problems.Add("Member already has an active issue of this book.");
            if(context.HasOverdueLoans)problems.Add("Return overdue books before borrowing again.");
        }
        if(due<=today||due>today.AddDays(90))problems.Add("Due date must be within the next 90 days.");
        if(context.MemberExpiresOn is DateTime expires&&due>expires.Date)problems.Add("Due date cannot exceed membership expiry.");
        return new(context,now,due,today.AddDays(1),max,maximumBooks,context.ConfiguredDailyFine??policy.DailyFine,problems);
    }
    public Task<int> IssueAsync(IssueLoan loan,string actor,CancellationToken ct) => commands.IssueAsync(loan,actor,ct);
    public Task<IssueReceipt?> ReceiptAsync(int id,CancellationToken ct) => repository.IssueReceiptAsync(id,ct);
}
