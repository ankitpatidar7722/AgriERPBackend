<#
  Builds the AgriERP PostgreSQL database from scratch: drops the public schema,
  runs every numbered script in dependency order, then (unless -NoSeed) the seed.

  Examples:
    ./build-postgres.ps1                         # schema + functions + seed into 'agrierp'
    ./build-postgres.ps1 -NoSeed                 # schema only (for the integration tests)
    ./build-postgres.ps1 -Database agrierp_dev -Password mypw
#>
param(
    [string]$DbHost   = 'localhost',
    [int]   $Port     = 5432,
    [string]$User     = 'postgres',
    [string]$Password = 'postgres',
    [string]$Database = 'agrierp',
    [switch]$NoSeed,
    [string]$Psql     = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
)

# psql writes NOTICEs to stderr; do not let PowerShell treat those as terminating.
$ErrorActionPreference = 'Continue'
$env:PGPASSWORD = $Password
$dir = $PSScriptRoot

# Create the database if it does not exist (connect via the default 'postgres' db).
$exists = & $Psql -h $DbHost -p $Port -U $User -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Database'" 2>$null
if ($exists -ne '1') {
    & $Psql -h $DbHost -p $Port -U $User -d postgres -c "CREATE DATABASE ""$Database""" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create database '$Database'." }
    Write-Host "Created database '$Database'."
}

# Reset the schema so the build is deterministic (quiet: suppress the cascade NOTICE).
& $Psql -h $DbHost -p $Port -U $User -d $Database -c "SET client_min_messages=warning; DROP SCHEMA public CASCADE; CREATE SCHEMA public;" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not reset schema on '$Database'." }

$scripts = @(
    '02_security.sql','03_masters.sql','04_items.sql','05_inventory.sql',
    '06_purchase.sql','07_sales.sql','08_finance.sql','09_system.sql',
    '80_foreign_keys.sql','85_views.sql','11_functions.sql'
)
if (-not $NoSeed) { $scripts += '12_seed_data.sql'; $scripts += '13_seed_modules.sql' }

foreach ($s in $scripts) {
    Write-Host "-> $s"
    & $Psql -h $DbHost -p $Port -U $User -d $Database -v ON_ERROR_STOP=1 -f (Join-Path $dir $s)
    if ($LASTEXITCODE -ne 0) { throw "Failed on $s" }
}

Write-Host ("Done - '{0}' built ({1})." -f $Database, $(if ($NoSeed) { 'schema only' } else { 'schema + seed' }))
