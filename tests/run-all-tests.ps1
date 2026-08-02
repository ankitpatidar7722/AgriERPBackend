<#
.SYNOPSIS
    Runs every AgriERP test suite and reports one verdict.

.DESCRIPTION
    Seven suites, fastest and most isolated first so a broken build or a bad
    calculation fails in seconds rather than after a browser has started:

      1. Build            backend + frontend compile
      2. Unit (.NET)      GST, FEFO, landed cost, amount-in-words - no database
      3. Unit (web)       billing preview + formatting - no browser
      4. SQL              schema behaviour against the real database
      5. Persistence      EF model matches the schema
      6. API              masters + transactions over real HTTP
      7. Browser          the app driven in headless Chromium

    Suites 4-7 need SQL Server. Suite 7 additionally needs Playwright; it is
    skipped automatically when Chromium is not installed rather than failing.

.PARAMETER SkipBuild
    Reuse the existing build output. Faster on repeat runs.

.PARAMETER SkipBrowser
    Skip the Playwright suite even when it is available.

.PARAMETER UnitOnly
    Run only the suites that need nothing external (1-3).

.EXAMPLE
    pwsh tests/run-all-tests.ps1
    pwsh tests/run-all-tests.ps1 -UnitOnly
    pwsh tests/run-all-tests.ps1 -SkipBuild -SkipBrowser
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [switch] $SkipBrowser,
    [switch] $UnitOnly,
    [string] $SqlServer = 'DESKTOP-L96U5S2\MSSQLSERVER03',
    [string] $SqlUser   = 'Indus',
    [string] $SqlPassword = $env:AGRIERP_SQL_PASSWORD,
    [int]    $ApiPort   = 5215,
    # 3001, not 3000: another project's dev server commonly owns 3000, and the
    # API's CORS list already allows both. The browser scripts read the port
    # from WEB_URL, which is exported below.
    [int]    $WebPort   = 3001
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# The Next.js app now lives in a sibling Frontend/ folder; Backend is $root.
$web  = Join-Path (Split-Path -Parent $root) 'Frontend'
$logs = Join-Path $env:TEMP 'agrierp-test-logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$results = [System.Collections.Generic.List[object]]::new()
$startedProcesses = @()

function Record($name, $ok, $detail = '') {
    $results.Add([pscustomobject]@{ Suite = $name; Passed = $ok; Detail = $detail })
    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("  {0}  {1} {2}" -f $status, $name, $detail)
}

function Section($title) {
    Write-Host ''
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

<#
    Runs a native executable and returns its combined output plus exit code.

    The isolation matters: in Windows PowerShell 5.1, capturing a native
    command's stderr with 2>&1 while $ErrorActionPreference is 'Stop' turns
    every stderr LINE into a terminating error - so a tool that merely prints a
    progress message to stderr (npm, dotnet, npx all do) aborts the whole run
    even though it exited 0. Dropping the preference to 'Continue' for the call
    means only the real exit code decides pass or fail.
#>
function Invoke-Native {
    param([string] $File, [string[]] $Arguments, [string] $WorkingDirectory)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($WorkingDirectory) { Push-Location $WorkingDirectory }
        $output = & $File @Arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
        if ($WorkingDirectory) { Pop-Location }
        return [pscustomobject]@{ Output = $output; ExitCode = $code; Ok = ($code -eq 0) }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Stop-Servers {
    # Only the processes this script started; a dev server the user is running
    # in another window must survive.
    #
    # /T matters: 'npm.cmd run start' spawns cmd.exe which spawns the real
    # 'next start'. Killing just the recorded id leaves that grandchild alive,
    # still holding the stdout handle it inherited - which keeps the pipe open
    # and hangs whatever is reading this script's output, long after the run
    # itself has finished.
    foreach ($id in $script:startedProcesses) {
        & taskkill.exe /PID $id /T /F 2>&1 | Out-Null
    }
    $script:startedProcesses = @()
}

function Wait-ForUrl($url, $timeoutSeconds = 45) {
    for ($i = 0; $i -lt $timeoutSeconds; $i++) {
        Start-Sleep -Seconds 1
        try {
            Invoke-WebRequest -Uri $url -TimeoutSec 3 -UseBasicParsing | Out-Null
            return $true
        } catch {}
    }
    return $false
}

# sqlcmd needs -I (QUOTED_IDENTIFIER ON) for any statement touching these
# tables; the filtered indexes reject it otherwise with Msg 1934.
function Invoke-Sql($query) {
    Invoke-Native 'sqlcmd' @('-S', $SqlServer, '-U', $SqlUser, '-P', $SqlPassword, '-C', '-I', '-b', '-d', 'AgriERP', '-Q', $query) | Out-Null
}

$overallStart = Get-Date

try {
    # ----------------------------------------------------------------- 1. build
    if (-not $SkipBuild) {
        Section '1. Build'
        $backend = Invoke-Native 'dotnet' @('build', (Join-Path $root 'AgriERP.sln'), '-v', 'q', '--nologo')
        Record 'Backend build' $backend.Ok
        if (-not $backend.Ok) { $backend.Output | Select-Object -Last 20 | ForEach-Object { Write-Host "      $_" } }

        $frontend = Invoke-Native 'npm.cmd' @('run', 'build') $web
        Record 'Frontend build' $frontend.Ok
        if (-not $frontend.Ok) { $frontend.Output | Select-Object -Last 25 | ForEach-Object { Write-Host "      $_" } }
    } else {
        Write-Host 'Skipping build (-SkipBuild).' -ForegroundColor DarkGray
    }

    # ------------------------------------------------------- 2. .NET unit tests
    Section '2. Unit tests (.NET) - no database'
    $unit = Invoke-Native 'dotnet' @('test', (Join-Path $root 'tests\AgriERP.Application.Tests\AgriERP.Application.Tests.csproj'), '--nologo', '-v', 'q')
    $unitLine = ($unit.Output | Select-String 'Passed!|Failed!' | Select-Object -Last 1)
    Record 'Application unit tests' $unit.Ok "$unitLine"
    if (-not $unit.Ok) { $unit.Output | Select-Object -Last 25 | ForEach-Object { Write-Host "      $_" } }

    # -------------------------------------------------- 3. frontend unit tests
    Section '3. Unit tests (web) - no browser'
    $vitest = Invoke-Native 'npx.cmd' @('vitest', 'run') $web
    $vitestLine = ($vitest.Output | Select-String 'Tests\s+\d+\s+passed|Tests\s+\d+\s+failed' | Select-Object -First 1)
    Record 'Web unit tests' $vitest.Ok "$("$vitestLine".Trim() -replace '\s+', ' ')"
    if (-not $vitest.Ok) { $vitest.Output | Select-Object -Last 25 | ForEach-Object { Write-Host "      $_" } }

    if ($UnitOnly) {
        Write-Host ''
        Write-Host 'Stopping after unit suites (-UnitOnly).' -ForegroundColor DarkGray
    }
    else {
        # ------------------------------------------------------------- 4. SQL
        Section '4. SQL smoke test'
        $sql = Invoke-Native 'sqlcmd' @('-S', $SqlServer, '-U', $SqlUser, '-P', $SqlPassword, '-C', '-d', 'AgriERP', '-i', (Join-Path $root 'database\tests\smoke_test.sql'))
        $sqlResult = ($sql.Output | Select-String 'RESULT:' | Select-Object -Last 1)
        Record 'Database behaviour' ("$sqlResult" -match 'all 10 checks passed') "$($sqlResult -replace '^\s+','')"

        # ----------------------------------------------------- 5. persistence
        Section '5. Persistence tests (EF model vs schema)'
        $persistence = Invoke-Native 'dotnet' @('test', (Join-Path $root 'tests\AgriERP.Persistence.Tests\AgriERP.Persistence.Tests.csproj'), '--nologo', '-v', 'q')
        $persistenceLine = ($persistence.Output | Select-String 'Passed!|Failed!' | Select-Object -Last 1)
        Record 'EF model matches schema' $persistence.Ok "$persistenceLine"
        if (-not $persistence.Ok) { $persistence.Output | Select-Object -Last 25 | ForEach-Object { Write-Host "      $_" } }

        # ------------------------------------------------- start the servers
        Section '6. API and frontend (real HTTP)'
        $env:ASPNETCORE_ENVIRONMENT = 'Development'

        # Every browser script reads this, so the port is decided once here.
        $env:WEB_URL = "http://localhost:$WebPort"

        # Refuse to run against something we did not start. A single-page app
        # already listening on this port answers 200 for every path, which once
        # produced a fully green frontend suite while AgriERP was not running
        # at all - a false pass is worse than a failure.
        foreach ($port in @($ApiPort, $WebPort)) {
            $inUse = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
            if ($inUse) {
                $owner = (Get-Process -Id ($inUse.OwningProcess | Select-Object -First 1) -ErrorAction SilentlyContinue).ProcessName
                Record "Port $port free" $false "- held by $owner"
                throw "Port $port is already in use (by $owner). Stop it, or pass -ApiPort / -WebPort."
            }
        }

        $api = Start-Process -PassThru -FilePath 'dotnet' -WindowStyle Hidden `
            -ArgumentList 'run', '--project', "`"$(Join-Path $root 'AgriERP.API\AgriERP.API.csproj')`"",
                          '--no-build', '--urls', "http://localhost:$ApiPort" `
            -RedirectStandardOutput (Join-Path $logs 'api.log') `
            -RedirectStandardError  (Join-Path $logs 'api.err.log')
        $startedProcesses += $api.Id

        if (-not (Wait-ForUrl "http://localhost:$ApiPort/health")) {
            Record 'API started' $false '- check api.err.log'
            Get-Content (Join-Path $logs 'api.err.log') -Tail 20 -ErrorAction SilentlyContinue |
                ForEach-Object { Write-Host "      $_" }
            throw 'API did not start.'
        }
        Record 'API started' $true

        # -WorkingDirectory explicitly: Start-Process does not inherit
        # PowerShell's current location, so Push-Location would not have
        # placed npm in the web project.
        $webProcess = Start-Process -PassThru -FilePath 'npm.cmd' -WindowStyle Hidden `
            -WorkingDirectory $web `
            -ArgumentList 'run', 'start', '--', '-p', $WebPort `
            -RedirectStandardOutput (Join-Path $logs 'web.log') `
            -RedirectStandardError  (Join-Path $logs 'web.err.log')
        $startedProcesses += $webProcess.Id

        if (-not (Wait-ForUrl "http://localhost:$WebPort/login")) {
            Record 'Web started' $false '- check web.err.log'
            throw 'Web server did not start.'
        }
        Record 'Web started' $true

        foreach ($suite in @(
            @{ Name = 'Masters API';       Script = 'tests\api\api-smoke.ps1' },
            @{ Name = 'Transactions API';  Script = 'tests\api\transactions-smoke.ps1' },
            @{ Name = 'Item groups API';   Script = 'tests\api\item-groups-smoke.ps1' },
            @{ Name = 'Frontend integration'; Script = 'tests\api\frontend-smoke.ps1' }
        )) {
            $output = Invoke-Native 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root $suite.Script))
            $line = ($output.Output | Select-String '^RESULT:' | Select-Object -Last 1)
            $ok = "$line" -match ', 0 failed'
            Record $suite.Name $ok "$line"
            if (-not $ok) {
                $output.Output | Select-String '^FAIL' | ForEach-Object { Write-Host "      $_" }
            }
        }

        # --------------------------------------------------------- 7. browser
        Section '7. Browser checks'
        # Both halves are needed and they install separately: the npm package
        # drives the browser, the ms-playwright folder holds the browser itself.
        # Checking only for the binaries reports a confusing MODULE_NOT_FOUND
        # instead of a clean skip.
        $playwrightReady = (Test-Path (Join-Path $env:LOCALAPPDATA 'ms-playwright')) -and
                           (Test-Path (Join-Path $web 'node_modules\playwright'))

        if ($SkipBrowser) {
            Write-Host '  SKIP  Browser suites (-SkipBrowser).' -ForegroundColor DarkGray
        }
        elseif (-not $playwrightReady) {
            # Not a failure: a machine without Playwright still gets a complete
            # verdict from the other six suites.
            Write-Host '  SKIP  Playwright not installed. Run:' -ForegroundColor DarkGray
            Write-Host '        cd ../Frontend && npm install --no-save playwright && npx playwright install chromium --only-shell' -ForegroundColor DarkGray
        }
        else {
            # The guard correctly blocks everything until the seeded default
            # password is changed, so it is lifted for the run and restored
            # afterwards in the finally block.
            Invoke-Sql "UPDATE Users SET MustChangePassword = 0 WHERE UserName = 'admin';" | Out-Null

            # The API suites purge their own fixtures, which leaves the database
            # with no products and no trade at all. The dashboard then correctly
            # renders its empty states and the billing type-ahead correctly
            # finds nothing - so "the charts drew geometry" and "a bill can be
            # built" become assertions about nothing. This lays down a small
            # trading history first, and removes it afterwards.
            $seed = Invoke-Native 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass',
                                                 '-File', (Join-Path $root 'tests\api\seed-demo-data.ps1'))
            $seedLine = ($seed.Output | Select-String '^RESULT:' | Select-Object -Last 1)
            Record 'Demo data seeded' $seed.Ok "$seedLine"
            if (-not $seed.Ok) {
                $seed.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" }
                throw 'Demo data could not be seeded; the browser suites would test an empty app.'
            }

            $shots = Join-Path $logs 'shots'

            $visual = Invoke-Native 'node' @('scripts/visual-check.mjs', $shots) $web
            $visualOk = ($visual.Output -join "`n") -match 'no problems found'
            Record 'Visual check (masters + dashboard)' $visualOk
            if (-not $visualOk) { $visual.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" } }

            $billing = Invoke-Native 'node' @('scripts/billing-flow-check.mjs', $shots, 'ZZ Confidor') $web
            $billingOk = ($billing.Output -join "`n") -match 'no problems found'
            Record 'Billing flow (bill posted in a browser)' $billingOk
            if (-not $billingOk) { $billing.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" } }

            $print = Invoke-Native 'node' @('scripts/print-media-check.mjs', $shots) $web
            $printOk = ($print.Output -join "`n") -match 'no problems found'
            Record 'Print layout (emulated print media)' $printOk
            if (-not $printOk) { $print.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" } }

            # Guards a bug that had no visible symptom: a required field on a
            # tab the operator was not looking at silently blocked Create, and
            # Radix had unmounted the panel holding the explanation.
            $itemForm = Invoke-Native 'node' @('scripts/item-create-check.mjs', 'CI') $web
            $itemFormOk = ($itemForm.Output -join "`n") -match 'no problems found'
            Record 'Item form reports hidden validation errors' $itemFormOk
            if (-not $itemFormOk) {
                $itemForm.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" }
            }

            # The point of the whole item-group restructure: choosing a
            # different group changes which questions the form asks, and those
            # questions come from the database rather than from the codebase.
            $groupForm = Invoke-Native 'node' @('scripts/item-form-check.mjs', $shots) $web
            $groupFormOk = ($groupForm.Output -join "`n") -match 'no problems found'
            Record 'Item form follows the item group' $groupFormOk
            if (-not $groupFormOk) {
                $groupForm.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" }
            }

            $login = Invoke-Native 'node' @('scripts/login-shot.mjs', $shots) $web
            $loginOk = ($login.Output -join "`n") -match 'no problems found'
            Record 'Login page and remember-me' $loginOk
            if (-not $loginOk) { $login.Output | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" } }

            Write-Host "  Screenshots: $shots" -ForegroundColor DarkGray
        }
    }
}
catch {
    Write-Host ''
    Write-Host "Run aborted: $($_.Exception.Message)" -ForegroundColor Red
    Record 'Run completed' $false "- $($_.Exception.Message)"
}
finally {
    Stop-Servers
    if (-not $UnitOnly) {
        # Always restore the forced password change and clear the fixture,
        # whatever happened above - an aborted run must not leave the account
        # unguarded or the database full of ZZ records.
        try {
            Invoke-Sql "UPDATE Users SET MustChangePassword = 1 WHERE UserName = 'admin';" | Out-Null
        } catch {}
        try {
            & sqlcmd -S $SqlServer -U $SqlUser -P $SqlPassword -C -I -b -d AgriERP `
                     -i (Join-Path $root 'tests\api\purge-zz-data.sql') 2>&1 | Out-Null
        } catch {}
    }
}

# ------------------------------------------------------------------- verdict
$elapsed = [int]((Get-Date) - $overallStart).TotalSeconds
$failed  = @($results | Where-Object { -not $_.Passed })

Write-Host ''
Write-Host ('=' * 62)
foreach ($result in $results) {
    $mark = if ($result.Passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0,-4}  {1}" -f $mark, $result.Suite)
}
Write-Host ('=' * 62)

if ($failed.Count -eq 0) {
    Write-Host "ALL SUITES PASSED  ($($results.Count) suites, ${elapsed}s)" -ForegroundColor Green
    Write-Host "Logs: $logs"
    exit 0
}

Write-Host "$($failed.Count) OF $($results.Count) SUITES FAILED  (${elapsed}s)" -ForegroundColor Red
foreach ($result in $failed) { Write-Host "  - $($result.Suite) $($result.Detail)" }
Write-Host "Logs: $logs"
exit 1
