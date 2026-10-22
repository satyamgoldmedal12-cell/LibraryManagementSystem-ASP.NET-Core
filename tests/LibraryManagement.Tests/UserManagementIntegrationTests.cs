using Dapper;
using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LibraryManagement.Tests;
[Collection("MVC host")]
public sealed class UserManagementIntegrationTests
{
    private static WebApplicationFactory<Program> Host()
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal)) throw new InvalidOperationException("Dedicated _Test database required.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = connection })));
    }
    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task<(ApplicationUser User, string Password)> Account(WebApplicationFactory<Program> host, string role)
    {
        using var scope = host.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid() + "@member-test.example"; var password = "Test!" + Guid.NewGuid().ToString("N") + "7aA";
        if (role == Roles.Member) { Assert.True((await scope.ServiceProvider.GetRequiredService<IMemberRegistrationService>().RegisterAsync(new RegisterModel { FullName = "Member Reader", Email = email, Password = password, ConfirmPassword = password })).Succeeded); return ((await users.FindByEmailAsync(email))!, password); }
        var user = new ApplicationUser { FullName = "Member Staff", Email = email, UserName = email, Role = role }; Assert.True((await users.CreateAsync(user, password)).Succeeded); return (user, password);
    }
    private static async Task<HttpResponseMessage> Login(HttpClient client, ApplicationUser user, string password)
    {
        var page = await client.GetStringAsync("/Account/Login"); return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = user.Email!, ["Password"] = password, ["__RequestVerificationToken"] = Token(page) }));
    }

    private static async Task<int> Book(ILibraryRepository repo,string actor,int copies=1) => await repo.SaveBookAsync(new() { Title="Issue test "+Guid.NewGuid(),Author="Issue Author",ISBN=Random.Shared.NextInt64(1000000000000,9999999999999).ToString(),CategoryId=(await repo.CategoriesAsync(default))[0].Id,TotalCopies=copies },actor,default);

    private static SqlConnection Connection()
    {
        var value=Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if(!new SqlConnectionStringBuilder(value).InitialCatalog.EndsWith("_Test",StringComparison.Ordinal)) throw new InvalidOperationException("Dedicated test database required.");
        return new(value);
    }

    private static async Task AgeLoan(int loan,int days)
    {
        await using var sql=Connection();
        await sql.ExecuteAsync("UPDATE library.Loans SET IssuedAt=DATEADD(day,-30,SYSUTCDATETIME()),DueDate=DATEADD(day,-@days,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@loan",new{loan,days});
    }



    private static Dictionary<string,string> Fields(UserSummary user,string token) => new(){["Id"]=user.Id,["FullName"]=user.FullName,["Email"]=user.Email!,["Role"]=user.Role,["MemberId"]=user.MemberId?.ToString()??"",["Revision"]=user.Revision.ToString(),["__RequestVerificationToken"]=token};
    [SqlFact] public async Task Admin_create_edit_reset_deactivate_activate_and_activity_work_with_session_revocation()
    {
        await using var host=Host();var(admin,password)=await Account(host,Roles.Admin);using var scope=host.Services.CreateScope();var accounts=scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();using var client=Client(host);await Login(client,admin,password);
        var accessToken=Token(await client.GetStringAsync("/Users"));
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Users/SetAccess",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=accessToken}))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Users/SetAccess",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=admin.Id,["__RequestVerificationToken"]=accessToken}))).StatusCode);
        var email=Guid.NewGuid()+"@managed.test";var initial="Test!"+Guid.NewGuid().ToString("N")+"Aa7";var form=await client.GetStringAsync("/Users/Create");var fields=new Dictionary<string,string>{["FullName"]="Managed Reader",["Email"]=email,["Password"]=initial,["Role"]=Roles.Librarian,["__RequestVerificationToken"]=Token(form),["PasswordHash"]="forged",["IsActive"]="false"};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Users/Create",new FormUrlEncodedContent(fields))).StatusCode);var target=(await users.FindByEmailAsync(email))!;Assert.True(target.IsActive);Assert.True(await users.CheckPasswordAsync(target,initial));
        using var session=Client(host);await Login(session,target,initial);Assert.Equal(HttpStatusCode.OK,(await session.GetAsync("/Books")).StatusCode);
        var summary=(await accounts.SummaryAsync(target.Id,default))!;form=await client.GetStringAsync("/Users/Edit/"+target.Id);fields=Fields(summary,Token(form));fields["FullName"]="<script>unsafe</script> Managed";fields["Role"]=Roles.Admin;
        var noToken=new Dictionary<string,string>(fields);noToken.Remove("__RequestVerificationToken");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Users/Edit/"+target.Id,new FormUrlEncodedContent(noToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Users/Edit/"+target.Id,new FormUrlEncodedContent(fields))).StatusCode);Assert.Contains("/Account/Login",(await session.GetAsync("/Books")).Headers.Location!.OriginalString);
        var listing=await client.GetStringAsync("/Users?Search="+Uri.EscapeDataString(email));Assert.Contains("&lt;script&gt;unsafe&lt;/script&gt;",listing);Assert.DoesNotContain("<script>unsafe</script>",listing);
        var stale=await client.PostAsync("/Users/Edit/"+target.Id,new FormUrlEncodedContent(fields));Assert.Equal(HttpStatusCode.OK,stale.StatusCode);Assert.Contains("account changed",await stale.Content.ReadAsStringAsync());
        form=await client.GetStringAsync("/Users/Access/"+target.Id+"?enabled=false");Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Users/SetAccess",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=target.Id,["enabled"]="false",["__RequestVerificationToken"]=Token(form)}))).StatusCode);Assert.False((await accounts.SummaryAsync(target.Id,default))!.IsActive);
        form=await client.GetStringAsync("/Users/ResetPassword/"+target.Id);var replacement="Reset!"+Guid.NewGuid().ToString("N")+"Aa7";
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Users/ResetPassword",new FormUrlEncodedContent(new Dictionary<string,string>{["Id"]=target.Id,["NewPassword"]=replacement,["__RequestVerificationToken"]=Token(form)}))).StatusCode);target=(await users.FindByIdAsync(target.Id))!;Assert.False(target.IsActive);
        using var fresh=Client(host);Assert.Equal(HttpStatusCode.OK,(await Login(fresh,target,replacement)).StatusCode);
        form=await client.GetStringAsync("/Users/Access/"+target.Id+"?enabled=true");await client.PostAsync("/Users/SetAccess",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=target.Id,["enabled"]="true",["__RequestVerificationToken"]=Token(form)}));target=(await users.FindByIdAsync(target.Id))!;Assert.True(target.IsActive);Assert.Equal(HttpStatusCode.Redirect,(await Login(fresh,target,replacement)).StatusCode);
        var activity=await accounts.ActivityAsync(new(){UserId=target.Id},default);foreach(var action in new[]{"User created","User edited","User disabled","User enabled","Password reset by admin","Signed in"})Assert.Contains(activity.Items,x=>x.Action==action);
        var page=await client.GetStringAsync("/Users/Activity?UserId="+target.Id);Assert.DoesNotContain(replacement,page);Assert.DoesNotContain(target.PasswordHash!,page);Assert.Contains("Password reset by admin",page);
        var logout=await fresh.GetStringAsync("/Books");await fresh.PostAsync("/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=Token(logout)}));Assert.Contains((await accounts.ActivityAsync(new(){UserId=target.Id,Action="Signed out"},default)).Items,x=>x.Action=="Signed out");
    }
    [SqlFact] public async Task Self_role_and_access_changes_are_blocked_and_membership_links_must_match()
    {
        await using var host=Host();var(admin,_)=await Account(host,Roles.Admin);var(target,_)=await Account(host,Roles.Librarian);var(member,_)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var accounts=scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var self=(await accounts.SummaryAsync(admin.Id,default))!;Assert.False((await accounts.EditAsync(new(){Id=admin.Id,FullName=self.FullName,Email=self.Email!,Role=Roles.Member,Revision=self.Revision},admin.Id,default)).Succeeded);Assert.False((await accounts.SetAccessAsync(admin.Id,false,admin.Id,default)).Succeeded);
        var user=(await accounts.SummaryAsync(target.Id,default))!;var edit=new EditUserModel{Id=user.Id,FullName=user.FullName,Email=user.Email!,Role=Roles.Member,MemberId=member.MemberId,Revision=user.Revision};Assert.False((await accounts.EditAsync(edit,admin.Id,default)).Succeeded);
        edit.Email=member.Email!;Assert.False((await accounts.EditAsync(edit,admin.Id,default)).Succeeded); // membership already has an account
        var repo=scope.ServiceProvider.GetRequiredService<ILibraryRepository>();var suffix=Guid.NewGuid().ToString("N");var link=await repo.SaveMemberAsync(new(){MemberNumber=suffix[..25],FullName="Unlinked Reader",Email=suffix+"@unlinked.test",ExpiresOn=DateTime.UtcNow.AddYears(1)},admin.Id,default);
        edit.MemberId=link;edit.Email=suffix+"@unlinked.test";Assert.True((await accounts.EditAsync(edit,admin.Id,default)).Succeeded);var saved=(await accounts.SummaryAsync(target.Id,default))!;Assert.Equal(Roles.Member,saved.Role);Assert.Equal(link,saved.MemberId);
        edit.Role=Roles.Librarian;edit.MemberId=null;edit.Revision=saved.Revision;Assert.True((await accounts.EditAsync(edit,admin.Id,default)).Succeeded);Assert.NotNull(await repo.MemberAsync(link,default));
    }
    [SqlFact] public async Task Database_prevents_removing_the_last_active_administrator()
    {
        await using var host=Host();var(admin,_)=await Account(host,Roles.Admin);using var scope=host.Services.CreateScope();var user=(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(admin.Id))!;
        await using var sql=Connection();await sql.OpenAsync();var original=await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Users WHERE Role='Admin' AND IsActive=1");await using var transaction=(SqlTransaction)await sql.BeginTransactionAsync();
        try
        {
            await sql.ExecuteAsync("UPDATE library.Users SET IsActive=0 WHERE Role='Admin' AND Id<>@id",new{id=user.Id},transaction);
            var error=await Assert.ThrowsAsync<SqlException>(()=>sql.ExecuteScalarAsync<int>("library.Users_Update",new{user.Id,user.UserName,user.NormalizedUserName,user.Email,user.NormalizedEmail,user.EmailConfirmed,user.PasswordHash,user.SecurityStamp,ConcurrencyStamp=Guid.NewGuid().ToString(),user.LockoutEnd,user.LockoutEnabled,user.AccessFailedCount,user.FullName,user.Role,user.MemberId,PreviousStamp=user.ConcurrencyStamp,IsActive=false},transaction,commandType:CommandType.StoredProcedure));Assert.Equal(50001,error.Number);Assert.Contains("active administrator",error.Message);
        }
        finally {if(transaction.Connection is not null)await transaction.RollbackAsync();}
        Assert.Equal(original,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Users WHERE Role='Admin' AND IsActive=1"));
    }
    [SqlFact] public async Task User_directory_is_paged_and_staff_members_cannot_manage_accounts_or_read_audit()
    {
        await using var host=Host();var(admin,password)=await Account(host,Roles.Admin);var(staff,staffPass)=await Account(host,Roles.Librarian);var(member,memberPass)=await Account(host,Roles.Member);using var scope=host.Services.CreateScope();var accounts=scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();var suffix="Directory "+Guid.NewGuid().ToString("N");var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        for(var i=0;i<21;i++){var email=Guid.NewGuid()+"@directory.test";Assert.True((await users.CreateAsync(new(){FullName=suffix+" "+i.ToString("D2"),Email=email,UserName=email,Role=Roles.Librarian})).Succeeded);}
        var first=await accounts.PageAsync(new(){Search=suffix,Role=Roles.Librarian,Status="active"},default);var second=await accounts.PageAsync(new(){Search=suffix,Role=Roles.Librarian,Status="active",Page=2},default);Assert.Equal(21,first.Total);Assert.Equal(20,first.Items.Count);Assert.Single(second.Items);Assert.DoesNotContain(first.Items,x=>x.Id==second.Items[0].Id);
        foreach(var account in new[]{(staff,staffPass),(member,memberPass)})
        {
            using var client=Client(host);await Login(client,account.Item1,account.Item2);foreach(var route in new[]{"/Users","/Users/Create","/Users/Edit/"+admin.Id,"/Users/Activity","/Users/Lookup?search=Admin","/Users/Access/"+admin.Id+"?enabled=false","/Users/ResetPassword/"+admin.Id})Assert.Contains("/Account/AccessDenied",(await client.GetAsync(route)).Headers.Location!.OriginalString);
            var token=Token(await client.GetStringAsync("/Books"));Assert.Contains("/Account/AccessDenied",(await client.PostAsync("/Users/SetAccess",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=admin.Id,["enabled"]="false",["__RequestVerificationToken"]=token}))).Headers.Location!.OriginalString);
        }
        using var owner=Client(host);await Login(owner,admin,password);Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync("/Users?Search="+Uri.EscapeDataString(suffix)+"&Page=2")).StatusCode);
    }
}
