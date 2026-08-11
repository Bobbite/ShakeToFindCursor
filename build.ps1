# PowerShell Build Script for Shake to Find Cursor (Windows 11)
$ErrorActionPreference = "Stop"

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    Write-Error "C# compiler csc.exe not found at $cscPath"
    exit 1
}

$outputExe = Join-Path $PSScriptRoot "ShakeToFindCursor.exe"
$sourceFile = Join-Path $PSScriptRoot "ShakeToFindCursor.cs"

Write-Host "Compiling ShakeToFindCursor.cs..." -ForegroundColor Cyan

$references = "System.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Core.dll"
$refArgs = $references | ForEach-Object { "/r:$_" }

$icoPath = Join-Path $PSScriptRoot "app.ico"

$args = @(
    "/target:winexe",
    "/optimize",
    "/win32icon:$icoPath",
    "/out:$outputExe"
) + $refArgs + @("$sourceFile")

$process = Start-Process -FilePath $cscPath -ArgumentList $args -NoNewWindow -Wait -PassThru

if ($process.ExitCode -eq 0) {
    Write-Host "Build Succeeded!" -ForegroundColor Green
    Write-Host "Executable generated at: $outputExe" -ForegroundColor Yellow
} else {
    Write-Host "Build Failed with exit code $($process.ExitCode)" -ForegroundColor Red
    exit $process.ExitCode
}
