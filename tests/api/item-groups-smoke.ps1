# =============================================================================
#  AgriERP - item group smoke test
#
#  Drives the group layer over real HTTP: the form definition each group
#  serves, the per-group code series, and the group-specific field values that
#  live in ItemMasterDetails rather than in a column.
#
#  Everything it creates is prefixed ZZ and purged by purge-zz-data.sql.
#
#  Run:  pwsh tests/api/item-groups-smoke.ps1     (API must be listening on 5215)
# =============================================================================
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5215/api'
$json = 'application/json'
$pass = 0; $fail = 0

function Check($name, $cond, $detail = '') {
  if ($cond) { Write-Output "PASS  $name"; $script:pass++ }
  else { Write-Output "FAIL  $name  $detail"; $script:fail++ }
}
function Body($e) {
  $s = $e.Exception.Response.GetResponseStream(); (New-Object IO.StreamReader($s)).ReadToEnd()
}

$purgeFile = Join-Path $PSScriptRoot 'purge-zz-data.sql'
function Purge {
  $out = & sqlcmd -S 'DESKTOP-L96U5S2\MSSQLSERVER03' -U Indus -P $env:AGRIERP_SQL_PASSWORD -C -I -b -d AgriERP -i $purgeFile 2>&1
  if ($LASTEXITCODE -ne 0) { throw "Purge failed:`n$out" }
}
Purge

$login = Invoke-RestMethod -Uri "$base/auth/login" -Method Post -ContentType $json `
  -Body (@{ userName = 'admin'; password = 'Admin@123' } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.data.accessToken)" }

# =============================================================================
#  GROUPS
# =============================================================================
$groups = (Invoke-RestMethod -Uri "$base/item-groups" -Headers $h).data
Check "1. Four item groups returned" (@($groups).Count -eq 4) "got $(@($groups).Count)"

$expected = @{ PRDGRP = 'P'; FRTGRP = 'F'; SEDGRP = 'S'; OTHGRP = 'R' }
$prefixesOk = $true
foreach ($code in $expected.Keys) {
  $g = $groups | Where-Object { $_.itemGroupCode -eq $code }
  if (-not $g -or $g.itemCodePrefix -ne $expected[$code]) { $prefixesOk = $false }
}
Check "2. Each group carries its own single-letter prefix (P/F/S/R)" $prefixesOk

$seed = $groups | Where-Object { $_.itemGroupCode -eq 'SEDGRP' }
$prod = $groups | Where-Object { $_.itemGroupCode -eq 'PRDGRP' }

# =============================================================================
#  FORM DEFINITION
# =============================================================================
$seedForm = (Invoke-RestMethod -Uri "$base/item-groups/$($seed.itemGroupId)/form" -Headers $h).data
$prodForm = (Invoke-RestMethod -Uri "$base/item-groups/$($prod.itemGroupId)/form" -Headers $h).data

Check "3. Seed form has a Seed details section" ($seedForm.sections -contains 'Seed details')
Check "4. Product form has a Safety section instead" `
  (($prodForm.sections -contains 'Safety') -and -not ($prodForm.sections -contains 'Seed details'))

$germination = $seedForm.fields | Where-Object { $_.fieldName -eq 'GerminationPct' }
Check "5. Germination is defined, required, and NOT an ItemMaster column" `
  ($null -ne $germination -and $germination.isRequired -and -not $germination.isStoredOnItem)
Check "6. Germination carries its 0-100 bounds and unit" `
  ($germination.minValue -eq 0 -and $germination.maxValue -eq 100 -and $germination.unitLabel -eq '%')

$sellingRate = $seedForm.fields | Where-Object { $_.fieldName -eq 'SellingRate' }
Check "7. Selling rate IS an ItemMaster column, so billing keeps its typed value" `
  ($null -ne $sellingRate -and $sellingRate.isStoredOnItem)

# A lookup name must be one the API recognises; raw SQL is never accepted.
$lookupFields = $seedForm.fields | Where-Object { $_.fieldType -eq 'select' }
$allowed = @('subgroups','companies','units','gstslabs','hsncodes','locations','fertilizerform','season')
$lookupsOk = $true
foreach ($f in $lookupFields) { if ($allowed -notcontains $f.lookupSource) { $lookupsOk = $false } }
Check "8. Every select names a whitelisted lookup, never SQL" $lookupsOk

try {
  Invoke-RestMethod -Uri "$base/item-groups/9999/form" -Headers $h | Out-Null
  Check '9. Unknown group returns 404' $false
} catch {
  Check '9. Unknown group returns 404' ([int]$_.Exception.Response.StatusCode -eq 404)
}

# =============================================================================
#  CREATE - per-group code series and the extra field values
# =============================================================================
$lk = (Invoke-RestMethod -Uri "$base/lookups/item-form" -Headers $h).data
$seedSubGroup = ($lk.itemSubGroups | Where-Object { $_.code -eq 'SEEDVEG' }).id
$unitPkt      = ($lk.units | Where-Object { $_.code -eq 'PKT' }).id
$gst0         = ($lk.gstSlabs | Where-Object { $_.totalRate -eq 0 }).id

function FieldId($form, $name) { ($form.fields | Where-Object { $_.fieldName -eq $name }).itemGroupFieldId }

$extra = @{}
$extra["$(FieldId $seedForm 'SeedVariety')"]  = 'ZZ Hybrid Abhinav'
$extra["$(FieldId $seedForm 'GerminationPct')"] = '85.5'
$extra["$(FieldId $seedForm 'SeedLotNumber')"] = 'ZZLOT-2026-11'

$created = Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType $json -Body (@{
  itemName = 'ZZ Tomato Seed Test'; itemSubGroupId = $seedSubGroup; unitId = $unitPkt
  gstSlabId = $gst0; sellingRate = 210; mrp = 250; isBatchTracked = $true
  isExpiryTracked = $true; isActive = $true
  extraFields = $extra
} | ConvertTo-Json -Depth 5)

$itemId = $created.data.itemId
Check "10. Seed item took a code from the SEED series ($($created.data.itemCode))" `
  ($created.data.itemCode -match '^S-\d{6}$') "got $($created.data.itemCode)"
Check "11. Group was derived from the sub group" ($created.data.itemGroupId -eq $seed.itemGroupId)

$fetched = (Invoke-RestMethod -Uri "$base/items/$itemId" -Headers $h).data
Check "12. Germination round-tripped through ItemMasterDetails" `
  ($fetched.extraFields."$(FieldId $seedForm 'GerminationPct')" -eq '85.5') `
  "got $($fetched.extraFields."$(FieldId $seedForm 'GerminationPct')")"
Check "13. Lot number round-tripped" `
  ($fetched.extraFields."$(FieldId $seedForm 'SeedLotNumber')" -eq 'ZZLOT-2026-11')

# A pesticide must take the OTHER series, not continue the seed numbering.
$prodSubGroup = ($lk.itemSubGroups | Where-Object { $_.code -eq 'INSEC' }).id
$gst18 = ($lk.gstSlabs | Where-Object { $_.totalRate -eq 18 }).id
$pesticide = Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType $json -Body (@{
  itemName = 'ZZ Confidor Test'; itemSubGroupId = $prodSubGroup; unitId = $unitPkt
  gstSlabId = $gst18; sellingRate = 500; mrp = 600; technicalName = 'Imidacloprid 17.8% SL'
  licenceNumber = 'ZZ-CIB-001'; isActive = $true
} | ConvertTo-Json -Depth 5)
Check "14. Pesticide took a code from the PRODUCT series ($($pesticide.data.itemCode))" `
  ($pesticide.data.itemCode -match '^P-\d{6}$') "got $($pesticide.data.itemCode)"

# =============================================================================
#  GUARDS
# =============================================================================
# A required group field left blank must be refused, exactly like a NOT NULL
# column would be - otherwise "required" is decoration.
try {
  Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType $json -Body (@{
    itemName = 'ZZ Seed No Germination'; itemSubGroupId = $seedSubGroup; unitId = $unitPkt
    gstSlabId = $gst0; sellingRate = 100; isActive = $true
    extraFields = @{ "$(FieldId $seedForm 'SeedVariety')" = 'ZZ V'; "$(FieldId $seedForm 'SeedLotNumber')" = 'ZZL' }
  } | ConvertTo-Json -Depth 5) | Out-Null
  Check '15. Missing required group field refused' $false
} catch {
  $b = Body $_
  Check '15. Missing required group field refused (400)' `
    ([int]$_.Exception.Response.StatusCode -eq 400 -and $b -match 'Germination') "$b"
}

# A field belonging to another group must not be writable onto this item.
try {
  Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType $json -Body (@{
    itemName = 'ZZ Seed Wrong Field'; itemSubGroupId = $seedSubGroup; unitId = $unitPkt
    gstSlabId = $gst0; sellingRate = 100; isActive = $true
    extraFields = @{
      "$(FieldId $seedForm 'SeedVariety')"    = 'ZZ V'
      "$(FieldId $seedForm 'GerminationPct')" = '80'
      "$(FieldId $seedForm 'SeedLotNumber')"  = 'ZZL'
      "$(FieldId $prodForm 'AntidoteInfo')"   = 'not a seed field'
    }
  } | ConvertTo-Json -Depth 5) | Out-Null
  Check "16. A field from another group is refused" $false
} catch {
  Check "16. A field from another group is refused (400)" `
    ([int]$_.Exception.Response.StatusCode -eq 400)
}

# Editing must update the value in place, not add a second answer.
$extra["$(FieldId $seedForm 'GerminationPct')"] = '92'
Invoke-RestMethod -Uri "$base/items/$itemId" -Method Put -Headers $h -ContentType $json -Body (@{
  itemName = 'ZZ Tomato Seed Test'; itemSubGroupId = $seedSubGroup; unitId = $unitPkt
  gstSlabId = $gst0; sellingRate = 210; mrp = 250; isBatchTracked = $true
  isExpiryTracked = $true; isActive = $true; extraFields = $extra
} | ConvertTo-Json -Depth 5) | Out-Null

$afterEdit = (Invoke-RestMethod -Uri "$base/items/$itemId" -Headers $h).data
Check "17. Edited value replaced the old one" `
  ($afterEdit.extraFields."$(FieldId $seedForm 'GerminationPct')" -eq '92')

$rowCount = & sqlcmd -S 'DESKTOP-L96U5S2\MSSQLSERVER03' -U Indus -P $env:AGRIERP_SQL_PASSWORD -C -I -h -1 -W -d AgriERP -Q `
  "SET NOCOUNT ON; SELECT COUNT(*) FROM ItemMasterDetails WHERE ItemId = $itemId AND ItemGroupFieldId = $(FieldId $seedForm 'GerminationPct');"
Check "18. Editing did not leave a second row for the same field" ($rowCount.Trim() -eq '1') "rows: $rowCount"

Purge

Write-Output ""
Write-Output "Cleanup: ZZ item group fixtures purged."
Write-Output "-----------------------------------------------------"
Write-Output "RESULT: $pass passed, $fail failed"
