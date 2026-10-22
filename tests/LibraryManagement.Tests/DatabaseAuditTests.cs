using System.Data;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryManagement.Tests;

[Collection("MVC host")]
public sealed class DatabaseAuditTests
{
    private static string ConnectionString()
    {
        var value=Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if(!new SqlConnectionStringBuilder(value).InitialCatalog.EndsWith("_Test",StringComparison.Ordinal))throw new InvalidOperationException("Dedicated _Test database required.");
        return value;
    }
    private static WebApplicationFactory<Program> Host()=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
        .ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:LibraryDatabase"]=ConnectionString()})));
    private static async Task<ApplicationUser> Staff(WebApplicationFactory<Program> host)
    {
        using var scope=host.Services.CreateScope();var email=Guid.NewGuid()+"@audit.example";
        var user=new ApplicationUser{FullName="Audit Librarian",UserName=email,Email=email,Role=Roles.Librarian};
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user,"Test!"+Guid.NewGuid().ToString("N")+"7aA")).Succeeded);return user;
    }
    private static async Task<int> Book(ILibraryRepository repo,string actor,string? author=null,string? publisher=null)
    {
        var suffix=Guid.NewGuid().ToString("N");return await repo.SaveBookAsync(new(){Title="Audit book "+suffix,Author=author??"Author "+suffix,Publisher=publisher,
            ISBN=Random.Shared.NextInt64(1000000000000,9999999999999).ToString(),CategoryId=(await repo.CategoriesAsync(default))[0].Id,TotalCopies=2},actor,default);
    }
    private static Task<int> Member(ILibraryRepository repo,string actor)=>repo.SaveMemberAsync(new(){MemberNumber=Guid.NewGuid().ToString("N")[..25],FullName="Audit Reader",
        Email=Guid.NewGuid()+"@audit-reader.example",ExpiresOn=DateTime.UtcNow.AddYears(1)},actor,default);

    [SqlFact]
    public async Task All_tables_have_primary_keys_audit_columns_trusted_foreign_keys_and_catalog_constraints()
    {
        await using var sql=new SqlConnection(ConnectionString());
        var names=(await sql.QueryAsync<string>("SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID('library')")).ToArray();Assert.Equal(17,names.Length);
        foreach(var name in names)
        {
            Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(@table) AND type='PK'",new{table="library."+name}));
            var columns=(await sql.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@table)",new{table="library."+name})).ToArray();
            foreach(var field in new[]{"CreatedDate","CreatedBy","ModifiedDate","ModifiedBy","DeletedDate","DeletedBy"})Assert.Contains(field,columns);
            foreach(var field in new[]{"CreatedBy","ModifiedBy","DeletedBy"})Assert.Equal(1,await sql.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM sys.foreign_key_columns f
                JOIN sys.columns c ON c.object_id=f.parent_object_id AND c.column_id=f.parent_column_id
                WHERE f.parent_object_id=OBJECT_ID(@table) AND c.name=@field AND f.referenced_object_id=OBJECT_ID('library.Users')",new{table="library."+name,field}));
        }
        Assert.Equal(0,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE schema_id=SCHEMA_ID('library') AND (is_disabled=1 OR is_not_trusted=1)"));
        Assert.Equal(0,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('library.Books') AND name IN('Author','Publisher')"));
        Assert.Equal(2,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('library.Books') AND referenced_object_id IN(OBJECT_ID('library.Authors'),OBJECT_ID('library.Publishers'))"));
        Assert.Equal(2,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.key_constraints WHERE name IN('UQ_Authors_Name','UQ_Publishers_Name')"));
        Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('library.Loans') AND name='UX_Loans_ActiveMemberBook' AND is_unique=1 AND has_filter=1"));
    }

    [SqlFact]
    public async Task Shared_catalog_labels_use_foreign_keys_and_failed_edits_do_not_leave_orphan_metadata()
    {
        await using var host=Host();var actor=await Staff(host);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var suffix=Guid.NewGuid().ToString("N");var author="Shared Author "+suffix;var publisher="Shared Publisher "+suffix;
        var first=await Book(repo,actor.Id,author,publisher);var second=await Book(repo,actor.Id,author,publisher);await using var sql=new SqlConnection(ConnectionString());
        Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT AuthorId) FROM library.Books WHERE Id IN(@first,@second)",new{first,second}));
        Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT PublisherId) FROM library.Books WHERE Id IN(@first,@second)",new{first,second}));
        Assert.Equal(author,(await repo.BookAsync(first,default))!.Author);Assert.Equal(publisher,(await repo.BookAsync(second,default))!.Publisher);
        var stale=(await repo.BookAsync(first,default))!;var fresh=(await repo.BookAsync(first,default))!;fresh.Title+=" edited";await repo.SaveBookAsync(fresh,actor.Id,default);
        var uncommitted="Rolled back author "+suffix;stale.Author=uncommitted;
        await Assert.ThrowsAsync<BusinessException>(()=>repo.SaveBookAsync(stale,actor.Id,default));
        Assert.Equal(0,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Authors WHERE Name=@uncommitted",new{uncommitted}));
        var invalid=await Assert.ThrowsAsync<SqlException>(()=>sql.ExecuteAsync("UPDATE library.Books SET AuthorId=-1 WHERE Id=@first",new{first}));Assert.Equal(547,invalid.Number);
        Assert.Equal(author,(await repo.BookAsync(first,default))!.Author);
    }

    [SqlFact]
    public async Task Book_and_cover_create_edit_delete_restore_record_server_owned_audit_metadata()
    {
        await using var host=Host();var creator=await Staff(host);var editor=await Staff(host);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var before=DateTime.UtcNow.AddSeconds(-2);var id=await Book(repo,creator.Id);var book=(await repo.BookAsync(id,default))!;
        Assert.Equal(creator.Id,book.CreatedBy);Assert.InRange(book.CreatedDate!.Value,before,DateTime.UtcNow);Assert.Null(book.ModifiedDate);
        book.CreatedBy=editor.Id;book.CreatedDate=new DateTime(2000,1,1);book.DeletedBy=editor.Id;book.DeletedDate=book.CreatedDate;book.Title+=" revised";
        var cover=new LibraryManagement.Infrastructure.Images.BookCoverProcessor().Process(new(BookCoverTests.Image(),"cover.png"));
        await repo.SaveBookWithCoverAsync(book,cover,false,editor.Id,default);book=(await repo.BookAsync(id,default))!;
        Assert.Equal(creator.Id,book.CreatedBy);Assert.InRange(book.CreatedDate!.Value,before,DateTime.UtcNow);Assert.Equal(editor.Id,book.ModifiedBy);Assert.NotNull(book.ModifiedDate);Assert.Null(book.DeletedDate);
        await repo.SaveBookWithCoverAsync(book,null,true,editor.Id,default);Assert.Null(await repo.BookCoverAsync(id,default));
        await using var sql=new SqlConnection(ConnectionString());var removed=await sql.QuerySingleAsync<AuditRow>("SELECT CreatedBy,CreatedDate,ModifiedBy,ModifiedDate,DeletedBy,DeletedDate FROM library.BookCovers WHERE BookId=@id",new{id});
        Assert.Equal(editor.Id,removed.CreatedBy);Assert.Equal(editor.Id,removed.DeletedBy);Assert.NotNull(removed.DeletedDate);
        book=(await repo.BookAsync(id,default))!;await repo.SaveBookWithCoverAsync(book,cover,false,editor.Id,default);Assert.NotNull(await repo.BookCoverAsync(id,default));
        Assert.Null(await sql.ExecuteScalarAsync<DateTime?>("SELECT DeletedDate FROM library.BookCovers WHERE BookId=@id",new{id}));
        book=(await repo.BookAsync(id,default))!;await repo.DeleteBookAsync(id,book.Revision,editor.Id,default);book=(await repo.BookAsync(id,default))!;
        Assert.Equal(editor.Id,book.DeletedBy);Assert.NotNull(book.DeletedDate);Assert.True(book.IsArchived);
        book.IsArchived=false;await repo.SaveBookAsync(book,creator.Id,default);book=(await repo.BookAsync(id,default))!;
        Assert.Null(book.DeletedBy);Assert.Null(book.DeletedDate);Assert.Equal(creator.Id,book.ModifiedBy);
        Assert.Equal(1,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.AuditEvents WHERE ActorId=@actor AND Action='Book deleted (archived)' AND EntityId=@entity",new{actor=editor.Id,entity=id.ToString()}));
    }

    [SqlFact]
    public async Task Member_delete_restore_and_public_registration_keep_correct_creator_and_modifier()
    {
        await using var host=Host();var creator=await Staff(host);var editor=await Staff(host);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var id=await Member(repo,creator.Id);var member=(await repo.MemberAsync(id,default))!;Assert.Equal(creator.Id,member.CreatedBy);
        member.Address="Changed address";await repo.SaveMemberAsync(member,editor.Id,default);member=(await repo.MemberAsync(id,default))!;Assert.Equal(editor.Id,member.ModifiedBy);
        await repo.DeleteMemberAsync(id,member.Revision,editor.Id,default);member=(await repo.MemberAsync(id,default))!;Assert.Equal(editor.Id,member.DeletedBy);Assert.NotNull(member.DeletedDate);
        await repo.RestoreMemberAsync(id,member.Revision,creator.Id,default);member=(await repo.MemberAsync(id,default))!;Assert.Null(member.DeletedDate);Assert.Null(member.DeletedBy);Assert.False(member.IsActive);
        var email=Guid.NewGuid()+"@self-audit.example";var password="Test!"+Guid.NewGuid().ToString("N")+"7aA";
        Assert.True((await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(new(){FullName="Self Reader",Email=email,Password=password,ConfirmPassword=password})).Succeeded);
        var account=(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
        Assert.Equal(account.Id,(await repo.MemberAsync(account.MemberId!.Value,default))!.CreatedBy);
        await using var sql=new SqlConnection(ConnectionString());Assert.Equal(account.Id,await sql.ExecuteScalarAsync<string>("SELECT CreatedBy FROM library.Users WHERE Id=@Id",new{account.Id}));
    }

    [SqlFact]
    public async Task Issue_return_and_payment_roll_back_business_and_audit_changes_when_final_audit_write_fails()
    {
        await using var host=Host();var actor=await Staff(host);using var scope=host.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var book=await Book(repo,actor.Id);var member=await Member(repo,actor.Id);await using var sql=new SqlConnection(ConnectionString());
        async Task Fail(string action,Func<Task> command)
        {
            Assert.Contains(action,new[]{"Book issued","Book returned","Fine paid"});
            await sql.ExecuteAsync("CREATE OR ALTER TRIGGER library.Test_FailAudit ON library.AuditEvents AFTER INSERT AS BEGIN SET NOCOUNT ON;IF EXISTS(SELECT 1 FROM inserted WHERE Action=N'"+action+"') THROW 50001,'Simulated downstream audit failure.',1;END;");
            try{await Assert.ThrowsAsync<BusinessException>(command);}finally{await sql.ExecuteAsync("DROP TRIGGER IF EXISTS library.Test_FailAudit");}
        }
        var input=new IssueLoan{BookId=book,MemberId=member,DueDate=DateTime.UtcNow.Date.AddDays(7)};
        await Fail("Book issued",async()=>{await repo.IssueAsync(input,new(),actor.Id,default);});
        Assert.Equal(2,(await repo.BookAsync(book,default))!.AvailableCopies);Assert.Equal(0,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Loans WHERE BookId=@book",new{book}));
        var loan=await repo.IssueAsync(input,new(),actor.Id,default);
        Assert.Equal(actor.Id,await sql.ExecuteScalarAsync<string>("SELECT CreatedBy FROM library.Loans WHERE Id=@loan",new{loan}));
        await sql.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,-30,SYSUTCDATETIME()),DueDate=DATEADD(day,-2,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@loan",new{loan});
        await Fail("Book returned",()=>repo.ReturnAsync(loan,actor.Id,default));Assert.Equal(1,(await repo.BookAsync(book,default))!.AvailableCopies);
        var unchanged=await sql.QuerySingleAsync<AuditRow>("SELECT ReturnedAt,ModifiedDate,ModifiedBy FROM library.Loans WHERE Id=@loan",new{loan});Assert.Null(unchanged.ReturnedAt);Assert.Null(unchanged.ModifiedDate);
        await repo.ReturnAsync(loan,actor.Id,default);Assert.Equal(2,(await repo.BookAsync(book,default))!.AvailableCopies);
        await Fail("Fine paid",()=>repo.PayFineAsync(loan,actor.Id,default));Assert.Equal(0,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Payments WHERE LoanId=@loan",new{loan}));
        await repo.PayFineAsync(loan,actor.Id,default);
        Assert.Equal(actor.Id,await sql.ExecuteScalarAsync<string>("SELECT CreatedBy FROM library.Payments WHERE LoanId=@loan",new{loan}));
        Assert.Equal(3,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.AuditEvents WHERE ActorId=@Id AND EntityId=@entity AND Action IN('Book issued','Book returned','Fine paid') AND CreatedBy=@Id",new{actor.Id,entity=loan.ToString()}));
    }

    private sealed class AuditRow
    {
        public string? CreatedBy{get;set;}public DateTime? CreatedDate{get;set;}public string? ModifiedBy{get;set;}public DateTime? ModifiedDate{get;set;}
        public string? DeletedBy{get;set;}public DateTime? DeletedDate{get;set;}public DateTime? ReturnedAt{get;set;}
    }
}
