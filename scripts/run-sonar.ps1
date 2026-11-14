param(
    [string]$SonarHostUrl = $env:SONAR_HOST_URL,
    [string]$SonarToken   = $env:SONAR_TOKEN,
    [string]$ProjectKey   = "cohort-10-dotnet-20292-mudassar"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SonarHostUrl)) {
    $SonarHostUrl = "http://localhost:9000"
}

if ([string]::IsNullOrWhiteSpace($SonarToken)) {
    Write-Error "SONAR_TOKEN is required. Set `$env:SONAR_TOKEN or pass -SonarToken <token>."
}

Write-Host "==> Ensuring dotnet-sonarscanner tool is available..."
if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
    dotnet tool install --global dotnet-sonarscanner
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

Write-Host "==> Building .NET Solution..."
dotnet build Backend/TaskManager.sln -c Release --no-incremental

Write-Host "==> Running xUnit Tests with Code Coverage..."
dotnet test Backend/TaskManager.sln -c Release --no-build `
    --collect:"XPlat Code Coverage" `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

Write-Host "==> Completing SonarQube Analysis..."
dotnet sonarscanner end /d:sonar.token="$SonarToken"

