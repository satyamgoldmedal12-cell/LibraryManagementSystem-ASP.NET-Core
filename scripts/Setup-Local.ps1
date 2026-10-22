param([string]$Server = '(localdb)\MSSQLLocalDB')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'Install SQL Server command-line tools (sqlcmd) first.' }
    & sqlcmd -S $Server -E -I -b -i Database\000_CreateDatabase.sql
    if ($LASTEXITCODE -ne 0) { throw 'Database creation failed.' }
    # Once schema 13 is applied, replaying the older catalog procedures would regress the normalized schema.
    $versionText = & sqlcmd -S $Server -d LibraryManagement -E -I -b -h -1 -W -Q "SET NOCOUNT ON;SELECT COALESCE((SELECT CONVERT(int,value) FROM sys.extended_properties WHERE class=0 AND name=N'LibraryManagement.SchemaVersion'),0);"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read database schema version.' }
    $schemaVersion = [int](($versionText | Out-String).Trim())
    foreach ($scriptName in @('001_Tables.sql','002_Procedures.sql','003_MemberRegistration.sql','004_AuthenticationSessions.sql','005_Dashboard.sql','006_BookManagement.sql','007_MemberManagement.sql','008_BookIssueManagement.sql','009_ReturnsAndFines.sql','010_Reports.sql','011_UserManagement.sql','012_Settings.sql','013_NormalizationAndAudit.sql','014_ModuleAccess.sql','015_GoldErpPageRights.sql')) {
        $scriptVersion = [int]$scriptName.Substring(0,3)
        if ($schemaVersion -ge 13 -and $scriptVersion -le $schemaVersion) { continue }
        & sqlcmd -S $Server -d LibraryManagement -E -I -b -i (Join-Path 'Database' $scriptName)
        if ($LASTEXITCODE -ne 0) { throw "Failed to apply $scriptName" }
    }
    & dotnet restore LibraryManagement.slnx
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build LibraryManagement.slnx --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $adminEmail = Read-Host 'Email for the FIRST administrator (leave empty if already provisioned)'
    if ($adminEmail) {
        $securePassword = Read-Host 'Administrator password (12+ characters, mixed case, number, symbol)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        $previousEmail = $env:BootstrapAdmin__Email
        $previousPassword = $env:BootstrapAdmin__Password
        $previousConnection = $env:ConnectionStrings__LibraryDatabase
        try {
            $env:BootstrapAdmin__Email = $adminEmail
            $env:BootstrapAdmin__Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
            $env:ConnectionStrings__LibraryDatabase = "Server=$Server;Database=LibraryManagement;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
            & dotnet run --project src/LibraryManagement.Web/LibraryManagement.Web.csproj --no-build --no-launch-profile -- --bootstrap-admin
            if ($LASTEXITCODE -ne 0) { throw 'Administrator creation failed. An existing account is never overwritten.' }
        } finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
            $env:BootstrapAdmin__Email = $previousEmail
            $env:BootstrapAdmin__Password = $previousPassword
            $env:ConnectionStrings__LibraryDatabase = $previousConnection
        }
    }
    Write-Host 'Setup complete. Run: dotnet run --project src/LibraryManagement.Web/LibraryManagement.Web.csproj --launch-profile https'
} finally { Pop-Location }
