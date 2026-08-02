# =============================================================================
#  AgriERP - demo data fixture
#
#  Builds a small but complete trading history through the API: six products
#  across six categories, three customers, one supplier, two purchases and six
#  invoices spread over four months of the current financial year.
#
#  Why through the API rather than INSERT statements: posting a purchase has to
#  allocate document numbers, apportion freight into landed cost and append to
#  the stock journal, and posting a sale has to pick batches by expiry and
#  freeze the cost onto the line. Hand-written INSERTs would have to reproduce
#  all of that, and the moment they got it slightly wrong the demo data would
#  look right while disagreeing with the rules the application enforces.
#
#  Everything is prefixed ZZ and removed by purge-zz-data.sql, so this is safe
#  to run against a database holding real records.
#
#  Used as the fixture for the browser suites: the dashboard charts need
#  something to draw and the billing type-ahead needs something to find. On an
#  empty database both are correctly blank, which makes those assertions
#  meaningless rather than reassuring.
#
#  Run:  pwsh tests/api/seed-demo-data.ps1            (API must be on :5215)
#        pwsh tests/api/seed-demo-data.ps1 -PurgeOnly
# =============================================================================
[CmdletBinding()]
param(
    [switch] $PurgeOnly,
    [string] $SqlServer = 'DESKTOP-L96U5S2\MSSQLSERVER03',
    [string] $SqlUser   = 'Indus',
    [string] $SqlPassword = $env:AGRIERP_SQL_PASSWORD,
    [int]    $ApiPort   = 5215
)

$ErrorActionPreference = 'Stop'
$base = "http://localhost:$ApiPort/api"
$json = 'application/json'
$purgeFile = Join-Path $PSScriptRoot 'purge-zz-data.sql'

function Purge {
    # -I sets QUOTED_IDENTIFIER ON (the filtered indexes reject DML without it);
    # -b makes sqlcmd exit non-zero so a blocked delete is not swallowed.
    $out = & sqlcmd -S $SqlServer -U $SqlUser -P $SqlPassword -C -I -b -d AgriERP -i $purgeFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Purge failed:`n$out" }
}

Purge
if ($PurgeOnly) {
    Write-Output 'RESULT: demo data purged.'
    exit 0
}

# ---- sign in ----------------------------------------------------------------
$login = Invoke-RestMethod -Uri "$base/auth/login" -Method Post -ContentType $json `
    -Body (@{ userName = 'admin'; password = 'Admin@123' } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.data.accessToken)" }

$lk = Invoke-RestMethod -Uri "$base/lookups/item-form" -Headers $h
function ItemSubGroupId($code) { ($lk.data.itemSubGroups | Where-Object { $_.code -eq $code }).id }
function UnitId($code)     { ($lk.data.units      | Where-Object { $_.code -eq $code }).id }
function GstId($rate)      { ($lk.data.gstSlabs   | Where-Object { $_.totalRate -eq $rate }).id }
function HsnId($code)      { ($lk.data.hsnCodes   | Where-Object { $_.code -eq $code }).id }
$locId = ($lk.data.storageLocations | Select-Object -First 1).id

# ---- group-specific required fields ------------------------------------------
# An item inherits its group from its sub-group, and each group can demand
# fields of its own - a seed will not save without a germination percentage.
# Those are read from the group's own definition rather than hardcoded, so
# adding a required field by migration does not silently break this fixture:
# it either gets a value from the table below or fails loudly by name.
$subGroups = (Invoke-RestMethod -Uri "$base/item-subgroups?pageSize=200" -Headers $h).data.items
$groupOfSubGroup = @{}
foreach ($sg in $subGroups) { $groupOfSubGroup[$sg.itemSubGroupCode] = $sg.itemGroupId }

$sampleValues = @{
    SeedVariety      = 'ZZ Hybrid Abhinav'
    GerminationPct   = '85'
    SeedLotNumber    = 'ZZLOT-2026-01'
    NutrientContent  = '46-0-0'
}

$requiredByGroup = @{}
foreach ($groupId in ($groupOfSubGroup.Values | Sort-Object -Unique)) {
    $form = (Invoke-RestMethod -Uri "$base/item-groups/$groupId/form" -Headers $h).data
    $requiredByGroup[$groupId] = @($form.fields | Where-Object { -not $_.isStoredOnItem -and $_.isRequired })
}

function ExtraFieldsFor($subGroupCode) {
    $groupId = $groupOfSubGroup[$subGroupCode]
    $values = @{}
    foreach ($field in $requiredByGroup[$groupId]) {
        if (-not $sampleValues.ContainsKey($field.fieldName)) {
            throw "No sample value for required field '$($field.fieldName)' " +
                  "($($field.fieldDisplayName)). Add one to `$sampleValues."
        }
        $values["$($field.itemGroupFieldId)"] = $sampleValues[$field.fieldName]
    }
    return $values
}

# ---- parties ----------------------------------------------------------------
$supplier = Invoke-RestMethod -Uri "$base/suppliers" -Method Post -Headers $h -ContentType $json `
    -Body (@{ supplierName = 'ZZ Agri Distributors'; city = 'Pune'; mobile = '9822004455'
              paymentTermDays = 30 } | ConvertTo-Json)
$supId = $supplier.data.supplierId

$customers = @()
foreach ($c in @(
    @{ Name = 'ZZ Ramesh Patil';   Village = 'Shirur';   Mobile = '9822011133'; Type = 'Retail';    Limit = 100000 },
    @{ Name = 'ZZ Sunil Jadhav';   Village = 'Baramati'; Mobile = '9822022244'; Type = 'Wholesale'; Limit = 300000 },
    @{ Name = 'ZZ Kisan Agro Ctr'; Village = 'Daund';    Mobile = '9822033355'; Type = 'Dealer';    Limit = 500000 }
)) {
    $created = Invoke-RestMethod -Uri "$base/customers" -Method Post -Headers $h -ContentType $json `
        -Body (@{ customerName = $c.Name; village = $c.Village; mobile = $c.Mobile
                  customerType = $c.Type; creditLimit = $c.Limit; creditDays = 30 } | ConvertTo-Json)
    $customers += $created.data.customerId
}

# ---- products ---------------------------------------------------------------
# Six categories on purpose: the donut caps at five named slices plus "Other",
# so six is the smallest set that exercises the fold.
$catalogue = @(
    @{ Name = 'ZZ Confidor 250ml';       Tech = 'Imidacloprid 17.8% SL'; Cat = 'INSEC'; Unit = 'BTL'; Gst = 18; Hsn = '3808'; Buy = 380; Sell = 500; Mrp = 600; Min = 350 },
    @{ Name = 'ZZ Roundup 1L';           Tech = 'Glyphosate 41% SL';     Cat = 'HERBI'; Unit = 'BTL'; Gst = 18; Hsn = '3808'; Buy = 420; Sell = 560; Mrp = 650; Min = 400 },
    @{ Name = 'ZZ Bavistin 500g';        Tech = 'Carbendazim 50% WP';    Cat = 'FUNGI'; Unit = 'PKT'; Gst = 18; Hsn = '3808'; Buy = 290; Sell = 390; Mrp = 450; Min = 270 },
    @{ Name = 'ZZ Urea 45kg';            Tech = 'Nitrogen 46%';          Cat = 'FERT';  Unit = 'BAG'; Gst = 5;  Hsn = '3102'; Buy = 245; Sell = 275; Mrp = 300; Min = 250 },
    @{ Name = 'ZZ Zinc Sulphate 5kg';    Tech = 'Zinc Sulphate 21%';     Cat = 'MICRO'; Unit = 'PKT'; Gst = 12; Hsn = '3824'; Buy = 310; Sell = 420; Mrp = 480; Min = 290 },
    @{ Name = 'ZZ Tomato Seeds 10g';     Tech = 'Hybrid Abhinav';        Cat = 'SEED';  Unit = 'PKT'; Gst = 0;  Hsn = '1209'; Buy = 140; Sell = 210; Mrp = 250; Min = 160 }
)

$products = @()
foreach ($p in $catalogue) {
    $created = Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType $json `
        -Body (@{ itemName = $p.Name; technicalName = $p.Tech
                  itemSubGroupId = (ItemSubGroupId $p.Cat); unitId = (UnitId $p.Unit)
                  gstSlabId = (GstId $p.Gst); hsnId = (HsnId $p.Hsn)
                  purchaseRate = $p.Buy; sellingRate = $p.Sell; mrp = $p.Mrp; minSellingRate = $p.Min
                  minStockLevel = 10; isBatchTracked = $true; isExpiryTracked = $true
                  isActive = $true
                  extraFields = (ExtraFieldsFor $p.Cat) } | ConvertTo-Json -Depth 5)
    $products += $created.data.itemId
}

Write-Output "Created supplier $supId, $($customers.Count) customers, $($products.Count) products."

# ---- purchases --------------------------------------------------------------
# Two consignments, both inside FY 2026-27. The second gives every product a
# later-expiring batch, so FEFO has a real choice to make when the sales below
# draw stock.
function New-Purchase($date, $invoiceNumber, $freight, $lines) {
    $draft = Invoke-RestMethod -Uri "$base/purchases" -Method Post -Headers $h -ContentType $json -Body (@{
        purchaseDate = $date; supplierId = $supId; supplierInvoiceNumber = $invoiceNumber
        locationId = $locId; freightCharges = $freight; paidAmount = 0; lines = $lines
    } | ConvertTo-Json -Depth 5)
    Invoke-RestMethod -Uri "$base/purchases/$($draft.data.purchaseId)/post" -Method Post -Headers $h | Out-Null
    return $draft.data.purchaseNumber
}

$firstLines = @()
$secondLines = @()
for ($i = 0; $i -lt $catalogue.Count; $i++) {
    $p = $catalogue[$i]
    $firstLines += @{ itemId = $products[$i]; batchNumber = "ZZB-A$($i + 1)"; expiryDate = '2027-03-31'
                      quantity = 120; rate = $p.Buy; mrp = $p.Mrp; sellingRate = $p.Sell }
    $secondLines += @{ itemId = $products[$i]; batchNumber = "ZZB-B$($i + 1)"; expiryDate = '2027-11-30'
                       quantity = 80; rate = [Math]::Round($p.Buy * 1.05, 2); mrp = $p.Mrp; sellingRate = $p.Sell }
}

$purchaseOne = New-Purchase '2026-04-05' 'ZZ-BILL-APR' 2400 $firstLines
$purchaseTwo = New-Purchase '2026-06-10' 'ZZ-BILL-JUN' 1800 $secondLines
Write-Output "Posted purchases $purchaseOne and $purchaseTwo."

# ---- sales ------------------------------------------------------------------
# Spread across four months so the twelve-month trend chart has a shape rather
# than a single spike, and split between cash and credit so the dashboard's
# outstanding tile and the payments screen both have something to show.
$billing = @(
    @{ Date = '2026-04-12'; Customer = 0; Type = 'Retail';    Pay = 'Cash';   Lines = @(@{ P = 0; Q = 6 }, @{ P = 3; Q = 10 }) },
    @{ Date = '2026-04-28'; Customer = 1; Type = 'Wholesale'; Pay = 'Credit'; Lines = @(@{ P = 1; Q = 12 }, @{ P = 4; Q = 8 }) },
    @{ Date = '2026-05-14'; Customer = 0; Type = 'Retail';    Pay = 'Cash';   Lines = @(@{ P = 2; Q = 5 }, @{ P = 5; Q = 15 }) },
    @{ Date = '2026-05-29'; Customer = 2; Type = 'Dealer';    Pay = 'Credit'; Lines = @(@{ P = 3; Q = 40 }, @{ P = 0; Q = 15 }) },
    @{ Date = '2026-06-18'; Customer = 1; Type = 'Wholesale'; Pay = 'Cash';   Lines = @(@{ P = 4; Q = 20 }, @{ P = 1; Q = 9 }) },
    @{ Date = '2026-07-08'; Customer = 0; Type = 'Retail';    Pay = 'Cash';   Lines = @(@{ P = 5; Q = 25 }, @{ P = 2; Q = 7 }) },
    @{ Date = '2026-07-22'; Customer = 2; Type = 'Dealer';    Pay = 'Credit'; Lines = @(@{ P = 0; Q = 30 }, @{ P = 3; Q = 35 }) }
)

$invoiceNumbers = @()
foreach ($bill in $billing) {
    $lines = @($bill.Lines | ForEach-Object {
        @{ itemId = $products[$_.P]; quantity = $_.Q; rate = $catalogue[$_.P].Sell }
    })
    $draft = Invoke-RestMethod -Uri "$base/sales" -Method Post -Headers $h -ContentType $json -Body (@{
        invoiceDate = $bill.Date; customerId = $customers[$bill.Customer]
        saleType = $bill.Type; paymentType = $bill.Pay; locationId = $locId; lines = $lines
    } | ConvertTo-Json -Depth 5)
    Invoke-RestMethod -Uri "$base/sales/$($draft.data.saleId)/post" -Method Post -Headers $h | Out-Null
    $invoiceNumbers += $draft.data.invoiceNumber
}
Write-Output "Posted $($invoiceNumbers.Count) invoices: $($invoiceNumbers -join ', ')."

# ---- a part payment, so the collection screens are not empty -----------------
$openBills = Invoke-RestMethod -Uri "$base/payments/open-bills?partyType=Customer&partyId=$($customers[2])" -Headers $h
if (@($openBills.data).Count -gt 0) {
    $bill = @($openBills.data)[0]
    Invoke-RestMethod -Uri "$base/payments" -Method Post -Headers $h -ContentType $json -Body (@{
        paymentDate = '2026-07-24'; partyType = 'Customer'; customerId = $customers[2]
        paymentType = 'Receipt'; paymentModeId = 1
        amount = [Math]::Round($bill.balanceAmount / 2, 2)
        allocations = @(@{ referenceType = 'Sale'; referenceId = $bill.referenceId
                           allocatedAmount = [Math]::Round($bill.balanceAmount / 2, 2) })
    } | ConvertTo-Json -Depth 5) | Out-Null
    Write-Output 'Recorded a part payment against the oldest dealer bill.'
}

# ---- confirm the fixture is actually usable ---------------------------------
# Seeding that half-worked is worse than none: the browser suite would fail
# somewhere unrelated and the cause would look like a UI bug.
$dashboard = Invoke-RestMethod -Uri "$base/dashboard?asOnDate=2026-07-22" -Headers $h
$monthsWithSales = @($dashboard.data.monthlyTrend | Where-Object { $_.salesAmount -gt 0 }).Count
$subGroupsWithStock = @($dashboard.data.itemSubGroupStock | Where-Object { $_.stockValueAtCost -gt 0 }).Count

if ($monthsWithSales -lt 3) { throw "Only $monthsWithSales month(s) carry sales - the trend chart would still look empty." }
if ($subGroupsWithStock -lt 3) { throw "Only $subGroupsWithStock sub group(s) hold stock - the donut would be near-empty." }

Write-Output ''
Write-Output "RESULT: demo data ready - $monthsWithSales months of trade across $subGroupsWithStock sub groups."
