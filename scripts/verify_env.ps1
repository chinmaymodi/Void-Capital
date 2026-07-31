Write-Host "=== Void Capital - Environment Verification ===" -ForegroundColor Cyan
Write-Host ""

# Run from the project root regardless of invocation directory
Set-Location (Split-Path -Parent $PSScriptRoot)

# Docker
$dockerVer = docker --version 2>$null
if ($dockerVer) {
    Write-Host "[PASS] Docker: $dockerVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Docker: not found" -ForegroundColor Red
}

# Docker Compose
$composeVer = docker compose version 2>$null
if ($composeVer) {
    Write-Host "[PASS] Docker Compose: $composeVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Docker Compose: not found" -ForegroundColor Red
}

# Docker images
$pgImg = docker images -q postgres:16-alpine 2>$null
$redisImg = docker images -q redis:7-alpine 2>$null
if ($pgImg) { Write-Host "[PASS] postgres:16-alpine image present" -ForegroundColor Green }
else { Write-Host "[FAIL] postgres:16-alpine image missing" -ForegroundColor Red }
if ($redisImg) { Write-Host "[PASS] redis:7-alpine image present" -ForegroundColor Green }
else { Write-Host "[FAIL] redis:7-alpine image missing" -ForegroundColor Red }

# .NET SDK
$dotnetVer = dotnet --version 2>$null
if ($dotnetVer) {
    Write-Host "[PASS] .NET SDK: $dotnetVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] .NET SDK: not found" -ForegroundColor Red
}

# Node
$nodeVer = node --version 2>$null
if ($nodeVer) {
    Write-Host "[PASS] Node.js: $nodeVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Node.js: not found" -ForegroundColor Red
}

# npm (call via cmd to bypass PowerShell execution policy on npm.ps1)
$npmVer = cmd /c "npm --version" 2>$null
if ($npmVer) {
    Write-Host "[PASS] npm: $npmVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] npm: not found" -ForegroundColor Red
}

# Git
$gitVer = git --version 2>$null
if ($gitVer) {
    Write-Host "[PASS] Git: $gitVer" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Git: not found" -ForegroundColor Red
}

# Directory structure
$dirs = @("src/VoidCapital.Api", "src/VoidCapital.Api.Tests", "frontend", "scripts")
foreach ($d in $dirs) {
    if (Test-Path $d) { Write-Host "[PASS] Directory: $d" -ForegroundColor Green }
    else { Write-Host "[FAIL] Directory: $d not found" -ForegroundColor Red }
}

Write-Host ""
Write-Host "=== Verification Complete ===" -ForegroundColor Cyan
