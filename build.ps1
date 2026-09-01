# PowerShell Build Script to generate standalone zero-install RDES.exe
param (
    [string]$Configuration = "Release",
    [string]$OutputDir = "dist"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building RDES Standalone Portable App... " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$root = $PSScriptRoot
$projectPath = Join-Path $root "src\RDES.App\RDES.App.csproj"
$outPath = Join-Path $root $OutputDir

if (Test-Path $outPath) {
    Remove-Item -Recurse -Force $outPath
}

Write-Host "Publishing self-contained Win-x64 Single-File executable to: $outPath" -ForegroundColor Yellow

dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outPath

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n==========================================" -ForegroundColor Green
    Write-Host " Build Succeeded!" -ForegroundColor Green
    Write-Host " Standalone Executable: $(Join-Path $outPath 'RDES.exe')" -ForegroundColor Green
    Write-Host " You can place this file directly on a shared drive or copy to any Windows 11 PC." -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
