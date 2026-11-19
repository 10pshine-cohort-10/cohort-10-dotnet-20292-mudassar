param(
    [string]$SonarHostUrl = $env:SONAR_HOST_URL,
    [string]$SonarToken   = $env:SONAR_TOKEN,
    [string]$ProjectKey   = "cohort-10-dotnet-20292-mudassar"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SonarHostUrl)) {
    $SonarHostUrl = "http://localhost:9000"
}

[Uri]$parsedHost = $null
if (-not [Uri]::TryCreate($SonarHostUrl, [UriKind]::Absolute, [ref]$parsedHost)) {
    throw "Invalid SonarHostUrl '$SonarHostUrl'. Provide a valid absolute URL."
}
if ($parsedHost.Scheme -eq "http" -and -not $parsedHost.IsLoopback) {
    throw "Refusing to transmit SONAR_TOKEN over non-HTTPS remote URL '$SonarHostUrl'. Use HTTPS or a loopback address."
}
if ($parsedHost.Scheme -notin @("http", "https")) {
    throw "Unsupported scheme '$($parsedHost.Scheme)' in SonarHostUrl '$SonarHostUrl'. Use HTTPS or loopback HTTP."
}

if ([string]::IsNullOrWhiteSpace($SonarToken)) {
    throw "SONAR_TOKEN is required. Set `$env:SONAR_TOKEN or pass -SonarToken <token>."
}

Write-Host "==> Ensuring dotnet-sonarscanner tool is available..."
if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
    dotnet tool install --global dotnet-sonarscanner
    if ($LASTEXITCODE -ne 0) { throw "Failed to install dotnet-sonarscanner (exit code $LASTEXITCODE)." }
}

Write-Host "==> Starting SonarQube Scanner for .NET (C# Backend + Tests)..."
dotnet sonarscanner begin `
    /k:"$ProjectKey" `
    /n:"TaskFlow (.NET + React)" `
    /d:sonar.host.url="$SonarHostUrl" `
    /d:sonar.token="$SonarToken" `
    /d:sonar.exclusions="**/node_modules/**,**/dist/**,**/bin/**,**/obj/**,**/Data/Migrations/**" `
    /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml" `
    /d:sonar.javascript.file.suffixes=".js,.jsx"
if ($LASTEXITCODE -ne 0) { throw "dotnet sonarscanner begin failed (exit code $LASTEXITCODE)." }

Write-Host "==> Building .NET Solution..."
dotnet build Backend/TaskManager.sln -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit code $LASTEXITCODE)." }

Write-Host "==> Running xUnit Tests with Code Coverage..."
dotnet test Backend/TaskManager.sln -c Release --no-build `
    --collect:"XPlat Code Coverage" `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed (exit code $LASTEXITCODE)." }

Write-Host "==> Completing SonarQube Analysis..."
dotnet sonarscanner end /d:sonar.token="$SonarToken"
if ($LASTEXITCODE -ne 0) { throw "dotnet sonarscanner end failed (exit code $LASTEXITCODE)." }

