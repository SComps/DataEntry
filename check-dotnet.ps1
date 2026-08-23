# =============================================================================
# check-dotnet.ps1 -- Verify .NET SDK requirements for DataEntry Compile/Build
# =============================================================================
# Checks:
#   1. dotnet is on the PATH
#   2. SDK version is net10.0 or higher
#   3. VB.NET (Roslyn) compiler is functional
#   4. Self-contained publish runtime pack is available for this OS/arch
#   5. DataEntry project builds successfully
#
# Usage:
#   .\check-dotnet.ps1
#
# Exit codes:
#   0 = all checks passed
#   1 = one or more checks failed
# =============================================================================

$ErrorActionPreference = "SilentlyContinue"
$SEP = "-" * 62
$overall = 0

function Write-Pass($msg)  { Write-Host "  [PASS]  $msg" -ForegroundColor Green }
function Write-Fail($msg)  { Write-Host "  [FAIL]  $msg" -ForegroundColor Red;  $script:overall = 1 }
function Write-Warn($msg)  { Write-Host "  [WARN]  $msg" -ForegroundColor Yellow }
function Write-Note($msg)  { Write-Host "          $msg" -ForegroundColor DarkGray }
function Write-Sep         { Write-Host $SEP -ForegroundColor DarkCyan }
function Write-Step($n,$t) { Write-Host ""; Write-Host "[ $n ] $t" -ForegroundColor Cyan }

# -- Header -------------------------------------------------------------------
Write-Host ""
Write-Sep
Write-Host "  DataEntry -- .NET SDK Environment Check" -ForegroundColor White
Write-Sep
Write-Host ""

# -- 1. dotnet on PATH --------------------------------------------------------
Write-Step "1/5" "Checking: dotnet is on PATH"
$dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    $dotnetPath = $dotnetCmd.Source
    Write-Pass "dotnet found at: $dotnetPath"
} else {
    Write-Fail "dotnet not found on PATH"
    Write-Note "Install from: https://dotnet.microsoft.com/download/dotnet/10.0"
    Write-Host ""
    Write-Sep
    Write-Host "  [FAIL]  Cannot continue without dotnet. Exiting." -ForegroundColor Red
    Write-Sep
    exit 1
}

# -- 2. SDK version >= 10.0 ---------------------------------------------------
Write-Step "2/5" "Checking: .NET SDK version (need 10.0+)"
$sdkList = dotnet --list-sdks 2>$null
$sdk10 = $sdkList | Where-Object { $_ -match "^10\." }

if ($sdk10) {
    $versions = ($sdk10 | ForEach-Object { ($_ -split '\s+')[0] }) -join ", "
    Write-Pass "SDK 10.x found: $versions"
} else {
    Write-Fail "No .NET 10.x SDK found"
    if ($sdkList) {
        Write-Note "Installed SDKs:"
        $sdkList | ForEach-Object { Write-Note "  $_" }
    } else {
        Write-Note "No SDKs detected at all."
    }
    Write-Note "Install from: https://dotnet.microsoft.com/download/dotnet/10.0"
}

# -- 3. VB.NET compiler -------------------------------------------------------
Write-Step "3/5" "Checking: VB.NET compiler (Roslyn) is functional"
$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

try {
    $vbSrc = @"
Module Hello
    Sub Main()
        Console.WriteLine("VB OK")
    End Sub
End Module
"@
    $vbSrc | Set-Content (Join-Path $tmpDir "Hello.vb") -Encoding UTF8

    $projXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"@
    $projXml | Set-Content (Join-Path $tmpDir "probe.vbproj") -Encoding UTF8

    $buildOut = dotnet build (Join-Path $tmpDir "probe.vbproj") --nologo -v quiet 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Pass "VB.NET compiler compiled a test project successfully"
    } else {
        Write-Fail "VB.NET compiler failed"
        $buildOut | Select-Object -First 15 | ForEach-Object { Write-Note $_ }
    }
} finally {
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}

# -- 4. Self-contained runtime pack -------------------------------------------
Write-Step "4/5" "Checking: self-contained publish runtime pack"

Add-Type -AssemblyName System.Runtime.InteropServices.RuntimeInformation -ErrorAction SilentlyContinue
$arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
$archPart = if ($arch.ToString() -eq "Arm64") { "arm64" } else { "x64" }
$rid = "win-$archPart"
Write-Note "Detected RID: $rid"

$tmpDir2 = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmpDir2 -Force | Out-Null

try {
    $vbSrc2 = @"
Module Hello
    Sub Main()
        Console.WriteLine("SC OK")
    End Sub
End Module
"@
    $vbSrc2 | Set-Content (Join-Path $tmpDir2 "Hello.vb") -Encoding UTF8

    $projXml2 = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"@
    $projXml2 | Set-Content (Join-Path $tmpDir2 "probe.vbproj") -Encoding UTF8

    $pubOut = dotnet publish (Join-Path $tmpDir2 "probe.vbproj") `
        --configuration Release `
        --runtime $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        --output (Join-Path $tmpDir2 "publish") `
        --nologo -v quiet 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Pass "Self-contained publish for '$rid' succeeded"
    } else {
        Write-Fail "Self-contained publish for '$rid' failed"
        $pubOut | Select-Object -First 20 | ForEach-Object { Write-Note $_ }
        Write-Note ""
        Write-Note "This usually means the runtime pack for '$rid' is not installed."
        Write-Note "Run:  dotnet workload restore"
    }
} finally {
    Remove-Item -Recurse -Force $tmpDir2 -ErrorAction SilentlyContinue
}

# -- 5. DataEntry project builds ----------------------------------------------
Write-Step "5/5" "Checking: DataEntry project builds"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $scriptDir "src\DataEntry\DataEntry.vbproj"

if (-not (Test-Path $proj)) {
    Write-Warn "DataEntry.vbproj not found at: $proj"
    Write-Note "Skipping project build check -- run this script from the repo root."
} else {
    $projOut = dotnet build $proj --nologo -v quiet 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Pass "DataEntry project built successfully"
    } else {
        Write-Fail "DataEntry project build failed"
        $projOut | Select-Object -First 30 | ForEach-Object { Write-Note $_ }
    }
}

# -- Summary ------------------------------------------------------------------
Write-Host ""
Write-Sep
if ($overall -eq 0) {
    Write-Host "  [PASS]  All checks passed -- DataEntry Compile/Build is ready." -ForegroundColor Green
} else {
    Write-Host "  [FAIL]  One or more checks FAILED -- see details above." -ForegroundColor Red
    Write-Note "Fix the issues listed, then re-run this script."
}
Write-Sep
Write-Host ""

exit $overall
