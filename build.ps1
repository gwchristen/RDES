# PowerShell Build Script to generate standalone zero-install RDES Server and Client editions
param (
    [string]$OutputDir = "dist"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building RDES Server & Client Editions... " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$root = $PSScriptRoot
$projectPath = Join-Path $root "src\RDES.App\RDES.App.csproj"
$serverPubXml = Join-Path $root "src\RDES.App\Properties\PublishProfiles\Server-Portable.pubxml"
$clientPubXml = Join-Path $root "src\RDES.App\Properties\PublishProfiles\Client-Portable.pubxml"

Write-Host "1. Publishing RDES-Server (Host / Master DB Edition)..." -ForegroundColor Yellow
dotnet publish $projectPath /p:PublishProfile=$serverPubXml

Write-Host "`n2. Publishing RDES-Client (Workstation / Network Link Edition)..." -ForegroundColor Yellow
dotnet publish $projectPath /p:PublishProfile=$clientPubXml

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n==========================================" -ForegroundColor Green
    Write-Host " Both Editions Built Successfully!" -ForegroundColor Green
    Write-Host " 1. Server Edition (Builds/Hosts DB): $(Join-Path $root 'dist\Server\RDES-Server.exe')" -ForegroundColor Green
    Write-Host " 2. Client Edition (Links to Shared DB): $(Join-Path $root 'dist\Client\RDES-Client.exe')" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
