# Layered / Onion Architecture

There is one deployed ASP.NET Core MVC application on .NET 10, with Razor views and three supporting class libraries. The solution now has these physical projects and folders:

```text
LibraryManagement.slnx
src/
  LibraryManagement.Web/
    Controllers/
    Views/
    ViewModels/
    ViewComponents/
    wwwroot/
    Middleware/
    Filters/
    Properties/
    Program.cs
    appsettings.json
    appsettings.Development.json
  LibraryManagement.Application/
    Interfaces/
    Services/
    DTOs/
    Validators/
  LibraryManagement.Domain/
    Entities/
    Enums/
    Rules/
    Exceptions/
  LibraryManagement.Infrastructure/
    Data/
    Repositories/
    Identity/
    Logging/
    Email/
    Images/
    Reporting/
    LibraryRegistration.cs
Database/
scripts/
tests/
```

## Dependency rules

```mermaid
flowchart LR
    Web[LibraryManagement.Web] --> App[LibraryManagement.Application]
    App --> Domain[LibraryManagement.Domain]
    Infra[LibraryManagement.Infrastructure] --> App
    Web -. Program.cs composition .-> Infra
```

| Project | Responsibilities | Direct project references |
| --- | --- | --- |
| Domain | Book, Member, Loan, Category; LibraryRole and role constants; lending/fine rules and business errors | None |
| Application | Use cases, query/input/output DTOs, service/repository/Identity interfaces, input validators | Domain |
| Infrastructure | SQL Server connection factory, parameterized Dapper repositories, Identity/email/image/report adapters, Serilog configuration, DI registration | Application |
| Web | MVC binding/actions, Razor views, view models, static assets, HTTP middleware, global MVC filters and startup | Application; Infrastructure for composition only |

Domain and Application have no ASP.NET Core FrameworkReference or NuGet PackageReference. Infrastructure implements interfaces owned by Application; Application never references implementations. Web uses Domain types through Application's transitive reference. No controller or view component accepts Infrastructure classes or repository interfaces. Only Program.cs uses Infrastructure to register adapters, configure logging and run explicit administrator bootstrap.

Runtime example: BooksController → IBookManagementService → BookManagementService → ILibraryRepository → LibraryRepository → parameterized SQL procedure. The repository interface is in Application.Interfaces and its implementation in Infrastructure.Repositories. The dependency points inward even though the runtime call proceeds to SQL Server.

## Real responsibilities in the requested folders

- Web/Middleware/SecurityHeadersMiddleware.cs owns the existing security and account privacy headers. GlobalExceptionHandler returns a safe standalone HTML/AJAX response without re-executing authentication or database-dependent rendering.
- Web/Filters/LibraryMvcFilters.cs registers framework antiforgery validation for all unsafe MVC requests, including AJAX form submissions. Controllers retain authorization, binding, ModelState and local redirects.
- Application/Validators contains the existing book/member normalization and validation shared by services. DTOs carry framework-independent DataAnnotations; MVC maps validation to form feedback.
- Domain/Enums contains LibraryRole. Roles exposes constant names derived from it so Identity/authorization attributes retain the existing Admin, Librarian and Member string values.
- Infrastructure/Data contains SqlConnectionFactory. Repositories contain all Dapper persistence adapters, including settings and sessions.
- Infrastructure/Logging owns Serilog application/request configuration. Logging package references are in Infrastructure; startup orchestration stays in Program.cs.
- Identity adapters convert framework results to Application-owned results. Password hashes, security stamps and SqlConnection never cross application contracts or reach Razor models.

SQL transactions continue to enforce availability, duplicate issues, member borrowing limits, fine snapshots and revision checks. Moving files requires no database migration or changes to existing accounts/library records.

## Open and run in Visual Studio

1. Open LibraryManagement.slnx from the solution root. The src solution folder contains all four production projects.
2. Right-click LibraryManagement.Web and select **Set as Startup Project**. The https launch profile and existing ports are preserved.
3. Build the solution and start the Web project. Controllers and Razor stay together in this MVC host.
4. Existing user-secrets are preserved by retaining the Web project's UserSecretsId. Set connection/recovery environment values for your environment as before.
5. For a fresh database, run scripts/Setup-Local.ps1 from the solution root. It applies Database scripts 000–013 and launches the new Web project for optional administrator bootstrap.

```powershell
dotnet restore LibraryManagement.slnx
dotnet build LibraryManagement.slnx
dotnet run --project src/LibraryManagement.Web/LibraryManagement.Web.csproj --launch-profile https
# Core tests use no SQL Server or MVC host.
dotnet test tests/LibraryManagement.Core.Tests/LibraryManagement.Core.Tests.csproj
# Full tests require an initialized, isolated database ending in _Test.
$env:LIBRARY_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=LibraryManagement_Test;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test LibraryManagement.slnx
dotnet publish src/LibraryManagement.Web/LibraryManagement.Web.csproj -c Release
```

The root LibraryManagement.csproj and its old MVC source locations are retired; use the Web project path above. Database scripts, docs, tests and artifacts remain outside the Web content root. Web excludes private App_Data and logs from publishing. New development MIME files are stored under src/LibraryManagement.Web/App_Data/MailPickup; old root-level pickup files/logs are retained in place. The same user-secrets ID and Data Protection application name preserve configuration/token compatibility; session validity remains governed by the existing security policy.

## Add a feature

1. Put entities and framework-independent business rules in Domain.
2. Define DTOs/interfaces in Application, then implement services and reusable validators there.
3. Implement persistence or external-system interfaces in Infrastructure and register them in LibraryRegistration.
4. Inject an Application service interface into a Web controller and create its Razor views/view models.
5. Test use cases with fake ports in Core.Tests, then cover SQL, HTTP, authorization, CSRF and dependency boundaries in LibraryManagement.Tests.

Architecture tests enforce inward core dependencies, framework-free contracts, actual layer namespaces/assemblies, presentation constructors without persistence dependencies, Identity-free view models and the global antiforgery filter.
