using Dapper;
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
public sealed class MemberManagementIntegrationTests
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
    private static Member NewMember(string? prefix = null) => new() { MemberNumber = "M-" + Guid.NewGuid().ToString("N")[..24], FullName = (prefix ?? "Reader ") + Guid.NewGuid(), Email = Guid.NewGuid() + "@member.example", Phone = "9876543210", Address = "First line\nSecond line", JoinedOn = DateTime.UtcNow.Date.AddMonths(-1), ExpiresOn = DateTime.UtcNow.Date.AddYears(1) };
    private static Dictionary<string, string> Fields(Member m, string token) => new() { ["Id"] = m.Id.ToString(), ["Revision"] = m.Revision.ToString(), ["MemberNumber"] = m.MemberNumber, ["FullName"] = m.FullName, ["Email"] = m.Email, ["Phone"] = m.Phone ?? "", ["Address"] = m.Address ?? "", ["JoinedOn"] = m.JoinedOn?.ToString("yyyy-MM-dd") ?? "", ["ExpiresOn"] = m.ExpiresOn.ToString("yyyy-MM-dd"), ["IsActive"] = m.IsActive.ToString(), ["__RequestVerificationToken"] = token };
    [SqlFact]
    public async Task Librarian_forms_save_member_ID_mobile_address_dates_status_and_encode_the_profile()
    {
        await using var host = Host(); var (staff, password) = await Account(host, Roles.Librarian); using var client = Client(host); Assert.Equal(HttpStatusCode.Redirect, (await Login(client, staff, password)).StatusCode);
        using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>(); var member = NewMember(); member.Address = "<script>unsafe</script>\nStreet 1";
        var form = await client.GetStringAsync("/Members/Edit"); var fields = Fields(member, Token(form)); fields["IsArchived"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Members/Edit/0", new FormUrlEncodedContent(fields))).StatusCode);
        member = Assert.Single((await repo.MembersAsync(member.Email, 1, default)).Items); Assert.False(member.IsArchived); Assert.NotNull(member.JoinedOn); Assert.Equal("9876543210", member.Phone);
        var profile = await client.GetStringAsync("/Members/Profile/" + member.Id); Assert.Contains("&lt;script&gt;unsafe&lt;/script&gt;", profile); Assert.DoesNotContain("<script>unsafe</script>", profile); Assert.Contains("Membership date", profile);
        form = await client.GetStringAsync("/Members/Edit/" + member.Id); member.Address = "Updated address"; member.Phone = "9876543211"; member.IsActive = false;
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Members/Edit/" + member.Id, new FormUrlEncodedContent(Fields(member, Token(form))))).StatusCode);
        var saved = (await repo.MemberAsync(member.Id, default))!; Assert.Equal("Updated address", saved.Address); Assert.Equal("Inactive", saved.Status);
    }
    [SqlFact]
    public async Task Members_can_read_only_their_own_profile_and_cannot_manage_other_members()
    {
        await using var host = Host(); var (one, password) = await Account(host, Roles.Member); var (two, _) = await Account(host, Roles.Member); using var client = Client(host); await Login(client, one, password);
        Assert.Contains(one.Email!, await client.GetStringAsync("/Members/Profile?memberId=" + two.MemberId));
        Assert.Contains("/Account/AccessDenied", (await client.GetAsync("/Members/Profile/" + two.MemberId)).Headers.Location!.OriginalString);
        foreach (var route in new[] { "/Members", "/Members/Edit", "/Members/Delete/" + one.MemberId, "/Members/Lookup?search=reader" }) Assert.Contains("/Account/AccessDenied", (await client.GetAsync(route)).Headers.Location!.OriginalString);
        var token = Token(await client.GetStringAsync("/Members/Profile"));
        foreach (var route in new[] { "/Members/Edit/" + one.MemberId, "/Members/Delete/" + one.MemberId, "/Members/Restore/" + one.MemberId }) Assert.Contains("/Account/AccessDenied", (await client.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["revision"] = "1" }))).Headers.Location!.OriginalString);
        using var anonymous = Client(host); Assert.Contains("/Account/Login", (await anonymous.GetAsync("/Members/Profile/" + one.MemberId)).Headers.Location!.OriginalString);
    }
    [SqlFact]
    public async Task Delete_revokes_sessions_blocks_login_and_restore_requires_a_fresh_sign_in()
    {
        await using var host = Host(); var (staff, password) = await Account(host, Roles.Admin); var (reader, readerPassword) = await Account(host, Roles.Member);
        using var admin = Client(host); using var client = Client(host); await Login(admin, staff, password); await Login(client, reader, readerPassword);
        using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>(); var member = (await repo.MemberAsync(reader.MemberId!.Value, default))!;
        Assert.Equal(DateTime.UtcNow.Date, member.JoinedOn);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/Members/Delete/" + member.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = member.Revision.ToString() }))).StatusCode);
        var page = await admin.GetStringAsync("/Members/Delete/" + member.Id);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Members/Delete/" + member.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = member.Revision.ToString(), ["__RequestVerificationToken"] = Token(page) }))).StatusCode);
        Assert.Contains("/Account/Login", (await client.GetAsync("/Members/Profile")).Headers.Location!.OriginalString);
        using var fresh = Client(host); Assert.Equal(HttpStatusCode.OK, (await Login(fresh, reader, readerPassword)).StatusCode);
        member = (await repo.MemberAsync(member.Id, default))!; Assert.Equal("Deleted", member.Status);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IUserSessionRepository>().ListAsync(reader.Id, 30, default));
        page = await admin.GetStringAsync("/Members/Profile/" + member.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/Members/Restore/" + member.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = member.Revision.ToString() }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Members/Restore/" + member.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = member.Revision.ToString(), ["__RequestVerificationToken"] = Token(page) }))).StatusCode);
        member = (await repo.MemberAsync(member.Id, default))!; Assert.Equal("Inactive", member.Status); Assert.Equal(HttpStatusCode.Redirect, (await Login(fresh, reader, readerPassword)).StatusCode);
    }
    [SqlFact]
    public async Task Delete_blocks_outstanding_books_and_preserves_returned_loans_and_fines()
    {
        await using var host = Host(); var (staff, _) = await Account(host, Roles.Admin); using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var member = NewMember(); member.Id = await repo.SaveMemberAsync(member, staff.Id, default); member = (await repo.MemberAsync(member.Id, default))!;
        var book = await repo.SaveBookAsync(new Book { ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(), Title = "Member History " + Guid.NewGuid(), Author = "Test", CategoryId = 1 }, staff.Id, default);
        var loan = await repo.IssueAsync(new IssueLoan { BookId = book, MemberId = member.Id, DueDate = DateTime.UtcNow.AddDays(7) }, new(), staff.Id, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.DeleteMemberAsync(member.Id, member.Revision, staff.Id, default));
        await repo.ReturnAsync(loan, staff.Id, default); await repo.DeleteMemberAsync(member.Id, member.Revision, staff.Id, default);
        Assert.Equal(1, (await repo.MemberStatisticsAsync(member.Id, default)).ReturnedLoans); Assert.Single((await repo.LoansAsync(null, "all", member.Id, 1, default)).Items);
        var deleted = (await repo.MemberAsync(member.Id, default))!; await Assert.ThrowsAsync<BusinessException>(() => repo.SaveMemberAsync(deleted, staff.Id, default));
    }
    [SqlFact]
    public async Task Duplicate_ID_email_stale_mutations_and_linked_account_email_changes_are_rejected()
    {
        await using var host = Host(); var (staff, _) = await Account(host, Roles.Admin); var (reader, _) = await Account(host, Roles.Member); using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var member = NewMember(); member.Id = await repo.SaveMemberAsync(member, staff.Id, default); member = (await repo.MemberAsync(member.Id, default))!; var stale = member.Revision;
        await repo.SaveMemberAsync(member, staff.Id, default); await Assert.ThrowsAsync<BusinessException>(() => repo.DeleteMemberAsync(member.Id, stale, staff.Id, default));
        await Assert.ThrowsAsync<BusinessException>(() => repo.SaveMemberAsync(member, staff.Id, default));
        var duplicate = NewMember(); duplicate.MemberNumber = member.MemberNumber; await Assert.ThrowsAsync<BusinessException>(() => repo.SaveMemberAsync(duplicate, staff.Id, default));
        duplicate.MemberNumber = "NEW-" + Guid.NewGuid().ToString("N")[..20]; duplicate.Email = member.Email; await Assert.ThrowsAsync<BusinessException>(() => repo.SaveMemberAsync(duplicate, staff.Id, default));
        var linked = (await repo.MemberAsync(reader.MemberId!.Value, default))!; linked.Email = Guid.NewGuid() + "@changed.example"; await Assert.ThrowsAsync<BusinessException>(() => repo.SaveMemberAsync(linked, staff.Id, default));
        Assert.Equal(reader.Email, (await repo.MemberAsync(linked.Id, default))!.Email);
        member = (await repo.MemberAsync(member.Id, default))!; await repo.DeleteMemberAsync(member.Id, member.Revision, staff.Id, default);
        await Assert.ThrowsAsync<BusinessException>(() => repo.RestoreMemberAsync(member.Id, member.Revision, staff.Id, default));
    }
    [SqlFact]
    public async Task Status_filters_search_and_deleted_visibility_match_the_member_state()
    {
        await using var host = Host(); var (staff, _) = await Account(host, Roles.Admin); using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>(); var prefix = "Directory-" + Guid.NewGuid();
        var items = new List<Member>(); for (var n = 0; n < 4; n++) { var m = NewMember(prefix); if (n == 0) { m.Phone = Random.Shared.NextInt64(1000000000,9999999999).ToString(); m.Address = "Search Street " + Guid.NewGuid(); } if (n == 1) m.IsActive = false; if (n == 2) { m.JoinedOn = DateTime.UtcNow.AddYears(-2); m.ExpiresOn = DateTime.UtcNow.AddDays(-1); } m.Id = await repo.SaveMemberAsync(m, staff.Id, default); items.Add((await repo.MemberAsync(m.Id, default))!); }
        await repo.DeleteMemberAsync(items[3].Id, items[3].Revision, staff.Id, default);
        Assert.Equal(3, (await repo.MembersPageAsync(prefix, 1, false, "all", default)).Total); Assert.Equal(4, (await repo.MembersPageAsync(prefix, 1, true, "all", default)).Total);
        foreach (var status in new[] { "active", "inactive", "expired" }) Assert.Single((await repo.MembersPageAsync(prefix, 1, false, status, default)).Items);
        Assert.Contains((await repo.MembersPageAsync(items[0].Phone, 1, false, "all", default)).Items, m => m.Id == items[0].Id);
        Assert.Contains((await repo.MembersPageAsync(items[0].Address, 1, false, "all", default)).Items, m => m.Id == items[0].Id);
    }
    [SqlFact]
    public async Task Legacy_dates_remain_unknown_and_blank_edits_preserve_recorded_dates()
    {
        await using var host = Host(); var (staff, password) = await Account(host, Roles.Admin); using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var member = NewMember(); member.Id = await repo.SaveMemberAsync(member, staff.Id, default); var original = member.JoinedOn;
        member = (await repo.MemberAsync(member.Id, default))!; member.JoinedOn = null; await scope.ServiceProvider.GetRequiredService<IMemberManagementService>().SaveAsync(member, staff.Id, default);
        Assert.Equal(original, (await repo.MemberAsync(member.Id, default))!.JoinedOn);
        await using var connection = await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().OpenAsync(default);
        await connection.ExecuteAsync("UPDATE library.Members SET JoinedOn=NULL WHERE Id=@Id", new { member.Id });
        member = (await repo.MemberAsync(member.Id, default))!; Assert.Null(member.JoinedOn); member.Address = "Updated legacy address";
        await scope.ServiceProvider.GetRequiredService<IMemberManagementService>().SaveAsync(member, staff.Id, default);
        Assert.Null((await repo.MemberAsync(member.Id, default))!.JoinedOn);
        using var client = Client(host); await Login(client, staff, password); Assert.Contains("Not recorded (legacy membership)", await client.GetStringAsync("/Members/Profile/" + member.Id));
    }
    [SqlFact]
    public async Task Profile_history_is_paged_and_fines_distinguish_unpaid_from_recorded_payments()
    {
        await using var host = Host(); var (staff, _) = await Account(host, Roles.Admin); using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var member = NewMember(); member.Id = await repo.SaveMemberAsync(member, staff.Id, default);
        var book = await repo.SaveBookAsync(new Book { ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(), Title = "Profile History " + Guid.NewGuid(), Author = "Test", CategoryId = 1 }, staff.Id, default);
        await using var connection = await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().OpenAsync(default);
        for (var i = 0; i < 13; i++)
        {
            var loan = await repo.IssueAsync(new IssueLoan { BookId = book, MemberId = member.Id, DueDate = DateTime.UtcNow.AddDays(7) }, new LendingOptions { DailyFine = 7.5m }, staff.Id, default);
            if (i < 2) await connection.ExecuteAsync("UPDATE library.Loans SET DueDate=DATEADD(day,@Offset,CONVERT(date,SYSUTCDATETIME())) WHERE Id=@Id", new { Id = loan, Offset = i == 0 ? -3 : -1 });
            await repo.ReturnAsync(loan, staff.Id, default); if (i == 0) await repo.PayFineAsync(loan, staff.Id, default);
        }
        var profile = (await scope.ServiceProvider.GetRequiredService<IMemberManagementService>().ProfileAsync(member.Id, 2, default))!;
        Assert.Single(profile.History.Items); Assert.Equal(13, profile.History.Total); Assert.Equal(13, profile.Statistics.ReturnedLoans);
        Assert.Equal(22.5m, profile.Statistics.PaidFines); Assert.Equal(7.5m, profile.Statistics.UnpaidFines);
    }
    [SqlFact]
    public async Task Archived_members_cannot_create_sessions_and_restore_does_not_enable_a_disabled_login()
    {
        await using var host = Host(); var (staff, _) = await Account(host, Roles.Admin); var (reader, password) = await Account(host, Roles.Member); using var scope = host.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>(); var identity = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        Assert.True((await identity.SetAccessAsync(reader.Id, false, staff.Id, default)).Succeeded);
        var member = (await repo.MemberAsync(reader.MemberId!.Value, default))!; await repo.DeleteMemberAsync(member.Id, member.Revision, staff.Id, default);
        await Assert.ThrowsAsync<SqlException>(() => scope.ServiceProvider.GetRequiredService<IUserSessionRepository>().CreateAsync(Guid.NewGuid(), reader.Id, "test", 8, default));
        member = (await repo.MemberAsync(member.Id, default))!; await repo.RestoreMemberAsync(member.Id, member.Revision, staff.Id, default);
        using var client = Client(host); Assert.Equal(HttpStatusCode.OK, (await Login(client, reader, password)).StatusCode);
        Assert.Equal(DateTimeOffset.MaxValue, (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(reader.Id))!.LockoutEnd);
    }
    [SqlFact]
    public async Task Concurrent_member_deletion_and_checkout_allow_only_one_operation()
    {
        await using var host = Host(); var (staff, _) = await Account(host, Roles.Admin); using var scope = host.Services.CreateScope(); var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var member = NewMember(); member.Id = await repo.SaveMemberAsync(member, staff.Id, default); member = (await repo.MemberAsync(member.Id, default))!;
        var book = await repo.SaveBookAsync(new Book { ISBN = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString(), Title = "Member Race " + Guid.NewGuid(), Author = "Test", CategoryId = 1 }, staff.Id, default);
        async Task<bool> Delete() { try { await repo.DeleteMemberAsync(member.Id, member.Revision, staff.Id, default); return true; } catch (BusinessException) { return false; } }
        async Task<bool> Issue() { try { await repo.IssueAsync(new IssueLoan { BookId = book, MemberId = member.Id, DueDate = DateTime.UtcNow.AddDays(7) }, new(), staff.Id, default); return true; } catch (BusinessException) { return false; } }
        var outcomes = await Task.WhenAll(Delete(), Issue()); Assert.Single(outcomes, x => x);
        var saved = (await repo.MemberAsync(member.Id, default))!; var stats = await repo.MemberStatisticsAsync(member.Id, default);
        Assert.Equal(outcomes[0], saved.IsArchived); Assert.Equal(outcomes[1] ? 1 : 0, stats.ActiveLoans);
    }

}
