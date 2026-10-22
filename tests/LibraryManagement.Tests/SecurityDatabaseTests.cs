using System.Net;
using System.Text.RegularExpressions;
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
public sealed class SecurityDatabaseTests
{
    private static WebApplicationFactory<Program> Host()
    {
        var connection = Environment.GetEnvironmentVariable("LIBRARY_TEST_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_Test", StringComparison.Ordinal))
            throw new InvalidOperationException("Dedicated _Test database required.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LibraryDatabase"] = connection })));
    }
    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    private static async Task Login(HttpClient client, string email, string password)
    {
        var token = Token(await client.GetStringAsync("/Account/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token }))).StatusCode);
    }
    private static string Isbn()
    {
        var digits = Random.Shared.NextInt64(100000000000, 999999999999).ToString();
        return digits + ((10 - digits.Select((c,i) => (c-'0')*(i%2==0?1:3)).Sum()%10)%10);
    }

    [SqlFact]
    public async Task Sql_injection_is_literal_data_and_stored_XSS_is_encoded_in_real_razor_pages()
    {
        await using var host = Host(); using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid()+"@security.example"; var password = "Test!"+Guid.NewGuid().ToString("N")+"7aA";
        var admin = new ApplicationUser { FullName="Security Admin", UserName=email, Email=email, Role=Roles.Admin };
        Assert.True((await users.CreateAsync(admin,password)).Succeeded);
        var repo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>(); var books = scope.ServiceProvider.GetRequiredService<IBookManagementService>();
        var suffix=Guid.NewGuid().ToString("N"); var payload="<script>alert(1)</script> ' OR 1=1-- "+suffix;
        var book = new Book { Title=payload, Author="<img src=x onerror=alert(1)> "+suffix, Publisher="<svg onload=alert(1)>",
            ISBN=Isbn(), CategoryId=(await repo.CategoriesAsync(default))[0].Id, TotalCopies=1 };
        var id=await books.SaveAsync(book,null,false,admin.Id,default);
        using var client=Client(host); await Login(client,email,password);
        foreach(var route in new[]{"/Books/Details/"+id,"/Books?search="+Uri.EscapeDataString(suffix),"/Books/Edit/"+id})
        {
            var response=await client.GetAsync(route); var html=await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains("&lt;script&gt;",html);
            Assert.DoesNotContain("<script>alert(1)</script>",html); Assert.DoesNotContain("<img src=x onerror=alert(1)>",html);
            Assert.Contains("no-store",response.Headers.CacheControl!.ToString());
        }
        Assert.Equal(payload,(await repo.BookAsync(id,default))!.Title);
        Assert.Equal(id,Assert.Single((await repo.BooksAsync("' OR 1=1-- "+suffix,1,false,default)).Items).Id);
        await using var sql=await scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().OpenAsync(default);
        var before=await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Books");
        Assert.Empty((await repo.BooksAsync("'; DROP TABLE library.Books;--"+suffix,1,false,default)).Items);
        Assert.Equal(before,await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM library.Books"));
        var lookup=await client.GetAsync("/Books/Lookup?search="+Uri.EscapeDataString(suffix));
        Assert.Equal(HttpStatusCode.OK,lookup.StatusCode); Assert.Contains("no-store",lookup.Headers.CacheControl!.ToString());
    }

    [SqlFact]
    public async Task Real_registration_emits_secure_notification_cookie_and_identical_passwords_have_distinct_hashes()
    {
        await using var host=Host(); using var client=Client(host); using var scope=host.Services.CreateScope();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var password="Test!"+Guid.NewGuid().ToString("N")+"7aA";
        var hashes=new List<string>();
        for(var index=0;index<2;index++)
        {
            var email=Guid.NewGuid()+"@cookie-security.example"; var token=Token(await client.GetStringAsync("/Account/Register"));
            var response=await client.PostAsync("/Account/Register",new FormUrlEncodedContent(new Dictionary<string,string>
            { ["FullName"]="Cookie Reader",["Email"]=email,["Password"]=password,["ConfirmPassword"]=password,["__RequestVerificationToken"]=token }));
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
            var cookie=Assert.Single(response.Headers.GetValues("Set-Cookie"),x=>x.StartsWith("LibraryManagement.TempData=",StringComparison.Ordinal));
            Assert.Contains("secure",cookie.ToLowerInvariant()); Assert.Contains("httponly",cookie.ToLowerInvariant()); Assert.Contains("samesite=lax",cookie.ToLowerInvariant());
            Assert.DoesNotContain(password,cookie);
            var account=(await users.FindByEmailAsync(email))!; Assert.True(await users.CheckPasswordAsync(account,password));
            Assert.NotEqual(password,account.PasswordHash); hashes.Add(account.PasswordHash!);
        }
        Assert.NotEqual(hashes[0],hashes[1]);
    }
}
