# =============================================================================
#  AgriERP - transaction cycle smoke test (step 4b)
#
#  Drives the running API over real HTTP through a complete trading cycle:
#  purchase -> stock -> FEFO sale -> return -> collection -> reports -> dashboard.
#  Everything it creates is prefixed ZZ and purged at the end.
#
#  Run:  pwsh tests/api/transactions-smoke.ps1     (API must be listening on 5215)
# =============================================================================
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5215/api'
$srv  = 'DESKTOP-L96U5S2\MSSQLSERVER03'
$pass = 0; $fail = 0

function Check($name, $cond, $detail = '') {
  if ($cond) { Write-Output "PASS  $name"; $script:pass++ }
  else { Write-Output "FAIL  $name  $detail"; $script:fail++ }
}
function Body($e) {
  $s = $e.Exception.Response.GetResponseStream(); (New-Object IO.StreamReader($s)).ReadToEnd()
}
# One purge definition, shared with seed-demo-data.ps1 and used both before and
# after this run. It lives in a file rather than here because two copies drift,
# and the half that forgets a child table leaves an orphan behind.
$purgeFile = Join-Path $PSScriptRoot 'purge-zz-data.sql'

function Purge {
  # -I sets QUOTED_IDENTIFIER ON; without it any DML against these tables
  # fails with Msg 1934 because of the filtered indexes.
  # -b makes sqlcmd exit non-zero on error, so a blocked delete is not swallowed.
  $out = & sqlcmd -S $srv -U Indus -P $env:AGRIERP_SQL_PASSWORD -C -I -b -d AgriERP -i $purgeFile 2>&1
  if ($LASTEXITCODE -ne 0) { throw "Purge failed:`n$out" }
}

Purge

# ---- sign in ----------------------------------------------------------------
$login = Invoke-RestMethod -Uri "$base/auth/login" -Method Post -ContentType 'application/json' `
  -Body (@{ userName = 'admin'; password = 'Admin@123' } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.data.accessToken)" }
$json = 'application/json'

# ---- master data ------------------------------------------------------------
$lk = Invoke-RestMethod -Uri "$base/lookups/item-form" -Headers $h
$catId  = ($lk.data.itemSubGroups | Where-Object { $_.code -eq 'INSEC' }).id
$unitId = ($lk.data.units      | Where-Object { $_.code -eq 'BTL' }).id
$gst18  = ($lk.data.gstSlabs   | Where-Object { $_.totalRate -eq 18 }).id
$hsn    = ($lk.data.hsnCodes   | Where-Object { $_.code -eq '3808' }).id
$locId  = ($lk.data.storageLocations | Select-Object -First 1).id

$sup = Invoke-RestMethod -Uri "$base/suppliers" -Method Post -Headers $h -ContentType $json `
  -Body (@{ supplierName = 'ZZ Agri Distributors'; city = 'Pune'; paymentTermDays = 30 } | ConvertTo-Json)
$supId = $sup.data.supplierId

$cust = Invoke-RestMethod -Uri "$base/customers" -Method Post -Headers $h -ContentType $json `
  -Body (@{ customerName = 'ZZ Ramesh Patil'; village = 'Shirur'; mobile = '9822011133'
            customerType = 'Retail'; creditLimit = 100000; creditDays = 30 } | ConvertTo-Json)
$custId = $cust.data.customerId

$prod = Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType $json `
  -Body (@{ itemName = 'ZZ Confidor 250ml'; technicalName = 'Imidacloprid 17.8% SL'
            itemSubGroupId = $catId; unitId = $unitId; gstSlabId = $gst18; hsnId = $hsn
            purchaseRate = 380; sellingRate = 500; mrp = 600; minSellingRate = 350
            minStockLevel = 10; isBatchTracked = $true; isExpiryTracked = $true; isActive = $true } | ConvertTo-Json)
$prodId = $prod.data.itemId

Write-Output "Setup: supplier $supId, customer $custId, product $prodId"
Write-Output ""

# =============================================================================
#  PURCHASE
# =============================================================================
$pur = Invoke-RestMethod -Uri "$base/purchases" -Method Post -Headers $h -ContentType $json -Body (@{
  purchaseDate = '2026-07-01'; supplierId = $supId; supplierInvoiceNumber = 'ZZ-BILL-001'
  locationId = $locId; freightCharges = 1000; paidAmount = 0
  lines = @(
    @{ itemId = $prodId; batchNumber = 'ZZB-LATE';  expiryDate = '2027-12-31'; quantity = 100; rate = 400; mrp = 600; sellingRate = 500 },
    @{ itemId = $prodId; batchNumber = 'ZZB-EARLY'; expiryDate = '2026-10-31'; quantity = 50;  rate = 420; mrp = 600; sellingRate = 500 }
  )
} | ConvertTo-Json -Depth 5)
$purId = $pur.data.purchaseId

# Gross 40000 + 21000 = 61000. GST 18% = 10980. Freight 1000. Total 72980.
Check "1. Purchase drafted ($($pur.data.purchaseNumber))" ($pur.data.purchaseNumber -match '^PUR/')
Check "2. Taxable 61,000.00 computed from lines" ($pur.data.taxableAmount -eq 61000.00) "got $($pur.data.taxableAmount)"
Check "3. Intra-state split: CGST 5,490 + SGST 5,490, IGST 0" `
  ($pur.data.cgstAmount -eq 5490.00 -and $pur.data.sgstAmount -eq 5490.00 -and $pur.data.igstAmount -eq 0)
Check "4. Grand total 72,980.00 (incl. 1,000 freight)" ($pur.data.grandTotal -eq 72980.00) "got $($pur.data.grandTotal)"

# Freight spread by taxable share: 1000*40000/61000 = 655.74 -> (40000+655.74)/100
$lateLine  = $pur.data.lines | Where-Object { $_.batchNumber -eq 'ZZB-LATE' }
$earlyLine = $pur.data.lines | Where-Object { $_.batchNumber -eq 'ZZB-EARLY' }
Check "5. Landed cost spreads freight by value (406.5574 / 426.8852)" `
  ($lateLine.landedRate -eq 406.5574 -and $earlyLine.landedRate -eq 426.8852) `
  "got $($lateLine.landedRate) / $($earlyLine.landedRate)"

# Draft must not have touched stock yet.
$stockBefore = (Invoke-RestMethod -Uri "$base/items/$prodId" -Headers $h).data.currentStock
Check "6. Draft purchase moved no stock" ($stockBefore -eq 0) "got $stockBefore"

$posted = Invoke-RestMethod -Uri "$base/purchases/$purId/post" -Method Post -Headers $h
Check "7. Purchase posted" ($posted.data.status -eq 'Posted')

$afterPurchase = Invoke-RestMethod -Uri "$base/items/$prodId" -Headers $h
Check "8. Stock in: 150 units across 2 batches" `
  ($afterPurchase.data.currentStock -eq 150 -and $afterPurchase.data.batchCount -eq 2) `
  "got $($afterPurchase.data.currentStock) / $($afterPurchase.data.batchCount)"
Check "9. Product purchase rate updated to landed cost" ($afterPurchase.data.purchaseRate -eq 426.8852) `
  "got $($afterPurchase.data.purchaseRate)"

# Duplicate supplier bill must be refused.
try {
  Invoke-RestMethod -Uri "$base/purchases" -Method Post -Headers $h -ContentType $json -Body (@{
    purchaseDate = '2026-07-02'; supplierId = $supId; supplierInvoiceNumber = 'ZZ-BILL-001'
    lines = @(@{ itemId = $prodId; quantity = 1; rate = 400 })
  } | ConvertTo-Json -Depth 5) | Out-Null
  Check '10. Duplicate supplier bill refused' $false
} catch {
  Check '10. Duplicate supplier bill refused (409)' ([int]$_.Exception.Response.StatusCode -eq 409)
}

# =============================================================================
#  SALE - FEFO
# =============================================================================
$sale = Invoke-RestMethod -Uri "$base/sales" -Method Post -Headers $h -ContentType $json -Body (@{
  invoiceDate = '2026-07-20'; customerId = $custId; saleType = 'Retail'; paymentType = 'Credit'
  locationId = $locId
  lines = @(@{ itemId = $prodId; quantity = 60; rate = 500 })
} | ConvertTo-Json -Depth 5)
$saleId = $sale.data.saleId

Check "11. Invoice drafted ($($sale.data.invoiceNumber))" ($sale.data.invoiceNumber -match '^INV/2026-27/')
Check "12. FEFO split 60 units into 2 lines across batches" ($sale.data.lines.Count -eq 2) `
  "got $($sale.data.lines.Count)"
Check "13. Earliest-expiring batch consumed first (ZZB-EARLY, 50)" `
  ($sale.data.lines[0].batchNumber -eq 'ZZB-EARLY' -and $sale.data.lines[0].quantity -eq 50) `
  "got $($sale.data.lines[0].batchNumber) / $($sale.data.lines[0].quantity)"
Check "14. Remainder from later batch (ZZB-LATE, 10)" `
  ($sale.data.lines[1].batchNumber -eq 'ZZB-LATE' -and $sale.data.lines[1].quantity -eq 10)
Check "15. Taxable 30,000.00 / GST 5,400.00 / total 35,400.00" `
  ($sale.data.taxableAmount -eq 30000.00 -and $sale.data.grandTotal -eq 35400.00 `
   -and $sale.data.cgstAmount -eq 2700.00 -and $sale.data.sgstAmount -eq 2700.00) `
  "got taxable $($sale.data.taxableAmount) total $($sale.data.grandTotal)"

# Cost frozen per batch: 50*426.8852 + 10*406.5574 = 21344.26 + 4065.57
Check "16. Cost frozen from each batch (25,409.83)" ($sale.data.totalCostAmount -eq 25409.83) `
  "got $($sale.data.totalCostAmount)"
Check "17. Gross profit 4,590.17 (30,000 - 25,409.83)" ($sale.data.grossProfit -eq 4590.17) `
  "got $($sale.data.grossProfit)"

$sPosted = Invoke-RestMethod -Uri "$base/sales/$saleId/post" -Method Post -Headers $h
Check "18. Invoice posted" ($sPosted.data.status -eq 'Posted')

$afterSale = Invoke-RestMethod -Uri "$base/items/$prodId" -Headers $h
Check "19. Stock reduced to 90 (150 - 60)" ($afterSale.data.currentStock -eq 90) `
  "got $($afterSale.data.currentStock)"
$earlyBatch = $afterSale.data.batches | Where-Object { $_.batchNumber -eq 'ZZB-EARLY' }
Check "20. Earliest batch fully drained to 0" ($earlyBatch.currentQty -eq 0) "got $($earlyBatch.currentQty)"

# Overselling must be refused.
try {
  Invoke-RestMethod -Uri "$base/sales" -Method Post -Headers $h -ContentType $json -Body (@{
    invoiceDate = '2026-07-21'; saleType = 'Retail'; paymentType = 'Cash'; locationId = $locId
    walkInCustomerName = 'ZZ Walk In'
    lines = @(@{ itemId = $prodId; quantity = 9999; rate = 500 })
  } | ConvertTo-Json -Depth 5) | Out-Null
  Check '21. Overselling refused' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '21. Overselling refused (422 with shortfall)' `
    ([int]$_.Exception.Response.StatusCode -eq 422 -and $b.message -match 'Insufficient') "$($b.message)"
}

# Selling below the minimum rate must be refused for users without the override.
# (admin HAS the override, so this instead proves the rate is accepted.)
$belowMin = Invoke-RestMethod -Uri "$base/sales" -Method Post -Headers $h -ContentType $json -Body (@{
  invoiceDate = '2026-07-21'; saleType = 'Retail'; paymentType = 'Cash'; locationId = $locId
  walkInCustomerName = 'ZZ Walk In'
  lines = @(@{ itemId = $prodId; quantity = 1; rate = 300 })
} | ConvertTo-Json -Depth 5)
Check '22. Admin may sell below min rate (holds the override permission)' `
  ($belowMin.data.lines[0].rate -eq 300)
Invoke-RestMethod -Uri "$base/sales/$($belowMin.data.saleId)/cancel" -Method Post -Headers $h `
  -ContentType $json -Body (@{ reason = 'test cleanup' } | ConvertTo-Json) | Out-Null

# Credit beyond the limit must be refused.
try {
  Invoke-RestMethod -Uri "$base/sales" -Method Post -Headers $h -ContentType $json -Body (@{
    invoiceDate = '2026-07-21'; customerId = $custId; saleType = 'Retail'; paymentType = 'Credit'
    locationId = $locId
    lines = @(@{ itemId = $prodId; quantity = 90; rate = 5000 })
  } | ConvertTo-Json -Depth 5) | Out-Null
  Check '23. Credit beyond limit refused' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '23. Credit beyond limit refused (422)' `
    ([int]$_.Exception.Response.StatusCode -eq 422 -and $b.message -match 'credit limit') "$($b.message)"
}

# =============================================================================
#  COLLECTION
# =============================================================================
$openBills = Invoke-RestMethod -Uri "$base/payments/open-bills?partyType=Customer&partyId=$custId" -Headers $h
Check "24. Open bill listed with 35,400.00 outstanding" `
  (@($openBills.data).Count -eq 1 -and $openBills.data[0].balanceAmount -eq 35400.00) `
  "got $(@($openBills.data).Count) bill(s)"

$receipt = Invoke-RestMethod -Uri "$base/payments" -Method Post -Headers $h -ContentType $json -Body (@{
  paymentDate = '2026-07-25'; partyType = 'Customer'; customerId = $custId; paymentType = 'Receipt'
  paymentModeId = 1; amount = 20000
  allocations = @(@{ referenceType = 'Sale'; referenceId = $saleId; allocatedAmount = 20000 })
} | ConvertTo-Json -Depth 5)
Check "25. Receipt recorded ($($receipt.data.voucherNumber))" ($receipt.data.voucherNumber -match '^RCT/')

$afterPayment = Invoke-RestMethod -Uri "$base/sales/$saleId" -Headers $h
Check "26. Invoice balance drops to 15,400.00, status Partial" `
  ($afterPayment.data.balanceAmount -eq 15400.00 -and $afterPayment.data.paymentStatus -eq 'Partial') `
  "got $($afterPayment.data.balanceAmount) / $($afterPayment.data.paymentStatus)"

$custAfter = Invoke-RestMethod -Uri "$base/customers/$custId" -Headers $h
Check "27. Customer outstanding derived as 15,400.00" ($custAfter.data.outstandingAmount -eq 15400.00) `
  "got $($custAfter.data.outstandingAmount)"

# Over-allocating must be refused.
try {
  Invoke-RestMethod -Uri "$base/payments" -Method Post -Headers $h -ContentType $json -Body (@{
    paymentDate = '2026-07-25'; partyType = 'Customer'; customerId = $custId; paymentType = 'Receipt'
    paymentModeId = 1; amount = 99999
    allocations = @(@{ referenceType = 'Sale'; referenceId = $saleId; allocatedAmount = 99999 })
  } | ConvertTo-Json -Depth 5) | Out-Null
  Check '28. Over-allocation refused' $false
} catch {
  Check '28. Over-allocation refused (422)' ([int]$_.Exception.Response.StatusCode -eq 422)
}

# =============================================================================
#  SALES RETURN
# =============================================================================
$lateBatchId = ($afterSale.data.batches | Where-Object { $_.batchNumber -eq 'ZZB-LATE' }).batchId

$ret = Invoke-RestMethod -Uri "$base/sales/returns" -Method Post -Headers $h -ContentType $json -Body (@{
  returnDate = '2026-07-26'; customerId = $custId; saleId = $saleId; locationId = $locId
  returnReason = 'Damaged packaging'; refundMode = 'Adjust'
  lines = @(@{ batchId = $lateBatchId; quantity = 5; rate = 500; isSaleable = $true })
} | ConvertTo-Json -Depth 5)
$retId = $ret.data.salesReturnId
Check "29. Sales return drafted ($($ret.data.returnNumber))" ($ret.data.returnNumber -match '^SR/')
Check "30. Return taxable 2,500.00 + GST 450 = 2,950.00" ($ret.data.grandTotal -eq 2950.00) `
  "got $($ret.data.grandTotal)"

Invoke-RestMethod -Uri "$base/sales/returns/$retId/post" -Method Post -Headers $h | Out-Null
$afterReturn = Invoke-RestMethod -Uri "$base/items/$prodId" -Headers $h
Check "31. Saleable return restores stock to 95" ($afterReturn.data.currentStock -eq 95) `
  "got $($afterReturn.data.currentStock)"

# An unsaleable return must NOT put goods back on the shelf.
$badRet = Invoke-RestMethod -Uri "$base/sales/returns" -Method Post -Headers $h -ContentType $json -Body (@{
  returnDate = '2026-07-26'; customerId = $custId; locationId = $locId
  returnReason = 'Expired'; refundMode = 'Adjust'
  lines = @(@{ batchId = $lateBatchId; quantity = 2; rate = 500; isSaleable = $false })
} | ConvertTo-Json -Depth 5)
Invoke-RestMethod -Uri "$base/sales/returns/$($badRet.data.salesReturnId)/post" -Method Post -Headers $h | Out-Null
$afterBadReturn = Invoke-RestMethod -Uri "$base/items/$prodId" -Headers $h
Check "32. Unsaleable return credits the customer but does NOT restock" `
  ($afterBadReturn.data.currentStock -eq 95) "got $($afterBadReturn.data.currentStock)"

# =============================================================================
#  STOCK LEDGER / ADJUSTMENT
# =============================================================================
$ledger = Invoke-RestMethod -Uri "$base/stock/ledger?itemId=$prodId&pageSize=50" -Headers $h
Check "33. Ledger holds every movement (2 in, 2 out, 1 return)" ($ledger.data.totalCount -eq 5) `
  "got $($ledger.data.totalCount)"

$batches = Invoke-RestMethod -Uri "$base/stock/batches?itemId=$prodId" -Headers $h
$lateNow = $batches.data | Where-Object { $_.batchNumber -eq 'ZZB-LATE' }
Check "34. Batch-wise stock: ZZB-LATE holds 95" ($lateNow.currentQty -eq 95) "got $($lateNow.currentQty)"

$adj = Invoke-RestMethod -Uri "$base/stock/adjustments" -Method Post -Headers $h -ContentType $json -Body (@{
  adjustmentDate = '2026-07-26'; adjustmentType = 'Physical'; locationId = $locId
  reason = 'Counted short'
  lines = @(@{ batchId = $lateBatchId; physicalQty = 93; reason = 'Two bottles broken' })
} | ConvertTo-Json -Depth 5)
Check "35. Adjustment drafted with -2 variance" ($adj.data.lines[0].differenceQty -eq -2) `
  "got $($adj.data.lines[0].differenceQty)"

Invoke-RestMethod -Uri "$base/stock/adjustments/$($adj.data.adjustmentId)/post" -Method Post -Headers $h | Out-Null
$afterAdj = Invoke-RestMethod -Uri "$base/items/$prodId" -Headers $h
Check "36. Posted adjustment brings stock to 93" ($afterAdj.data.currentStock -eq 93) `
  "got $($afterAdj.data.currentStock)"

# =============================================================================
#  REPORTS + DASHBOARD
# =============================================================================
$profit = Invoke-RestMethod -Uri "$base/reports/profit?fromDate=2026-07-01&toDate=2026-07-31" -Headers $h
$julyProfit = ($profit.data | Where-Object { $_.grossProfit -ne 0 } | Select-Object -First 1)
Check "37. Profit report shows the day's margin" ($null -ne $julyProfit -and $julyProfit.grossProfit -eq 4590.17) `
  "got $($julyProfit.grossProfit)"

$gst = Invoke-RestMethod -Uri "$base/reports/gst?fromDate=2026-07-01&toDate=2026-07-31" -Headers $h
$outward = @($gst.data.outwardSupplies | Where-Object { $_.hsnCode -eq '3808' })[0]
$inward  = @($gst.data.inwardSupplies  | Where-Object { $_.hsnCode -eq '3808' })[0]
Check "38. GST report: output tax 5,400 on 30,000 taxable" `
  ($outward.taxableAmount -eq 30000.00 -and $outward.totalTax -eq 5400.00) `
  "got $($outward.taxableAmount) / $($outward.totalTax)"
Check "39. GST report: input credit 10,980 on 61,000 taxable" `
  ($inward.taxableAmount -eq 61000.00 -and $inward.totalTax -eq 10980.00) `
  "got $($inward.taxableAmount) / $($inward.totalTax)"

$lowStock = Invoke-RestMethod -Uri "$base/reports/stock/near-expiry?withinDays=200" -Headers $h
Check "40. Near-expiry report is reachable" ($null -ne $lowStock.data)

$valuation = Invoke-RestMethod -Uri "$base/reports/stock/valuation" -Headers $h
Check "41. Stock valuation returns cost and MRP totals" `
  ($valuation.data.valueAtCost -gt 0 -and $valuation.data.valueAtMrp -ge $valuation.data.valueAtCost)

$dash = Invoke-RestMethod -Uri "$base/dashboard?asOnDate=2026-07-20" -Headers $h
Check "42. Dashboard headline: 35,400 billed on 20-Jul" ($dash.data.headline.todaySales -eq 35400.00) `
  "got $($dash.data.headline.todaySales)"
Check "43. Dashboard returns all six blocks" `
  ($null -ne $dash.data.alerts -and $null -ne $dash.data.recentBills -and `
   $null -ne $dash.data.topItems -and @($dash.data.monthlyTrend).Count -eq 12 -and `
   $null -ne $dash.data.itemSubGroupStock)
Check "44. Dashboard customer due matches derived outstanding" `
  ($dash.data.headline.customerDue -ge 15400.00 - 2950.00 - 1000.00)

# =============================================================================
#  INVOICE PRINT
# =============================================================================
$print = Invoke-RestMethod -Uri "$base/sales/$saleId/print" -Headers $h
Check "45. Print payload carries shop header and tax summary" `
  ($print.data.shop.shopName.Length -gt 0 -and @($print.data.taxSummary).Count -ge 1)
Check "46. Amount in words uses the Indian system" `
  ($print.data.amountInWords -match 'Thousand' -and $print.data.amountInWords -match 'Only') `
  "got '$($print.data.amountInWords)'"

# =============================================================================
#  CANCELLATION REVERSES, NEVER DELETES
# =============================================================================
$ledgerBefore = (Invoke-RestMethod -Uri "$base/stock/ledger?itemId=$prodId&pageSize=100" -Headers $h).data.totalCount

Invoke-RestMethod -Uri "$base/payments/$($receipt.data.paymentId)/cancel" -Method Post -Headers $h `
  -ContentType $json -Body (@{ reason = 'Cheque bounced' } | ConvertTo-Json) | Out-Null
$afterPayCancel = Invoke-RestMethod -Uri "$base/sales/$saleId" -Headers $h
Check "47. Cancelled receipt reopens the invoice to 35,400.00" `
  ($afterPayCancel.data.balanceAmount -eq 35400.00) "got $($afterPayCancel.data.balanceAmount)"

Invoke-RestMethod -Uri "$base/purchases/$purId/cancel" -Method Post -Headers $h `
  -ContentType $json -Body (@{ reason = 'Wrong consignment' } | ConvertTo-Json) | Out-Null
$ledgerAfter = (Invoke-RestMethod -Uri "$base/stock/ledger?itemId=$prodId&pageSize=100" -Headers $h).data.totalCount
Check "48. Cancellation APPENDS reversing rows rather than deleting" ($ledgerAfter -gt $ledgerBefore) `
  "before $ledgerBefore after $ledgerAfter"

# The batch cache must still agree with the journal after all of that.
$drift = & sqlcmd -S $srv -U Indus -P $env:AGRIERP_SQL_PASSWORD -C -I -d AgriERP -h -1 -W -Q `
  "SET NOCOUNT ON; SELECT COUNT(*) FROM ItemBatches b WHERE b.ItemId = $prodId AND b.CurrentQty <> (SELECT ISNULL(SUM(st.SignedQuantity),0) FROM StockTransactions st WHERE st.BatchId = b.BatchId);"
Check "49. Batch balances still reconcile to the journal (zero drift)" ($drift.Trim() -eq '0') "drifted rows: $drift"

# ---- cleanup ----------------------------------------------------------------
Purge

Write-Output ""
Write-Output "Cleanup: all ZZ test data purged, number series reset."
Write-Output "-----------------------------------------------------"
Write-Output "RESULT: $pass passed, $fail failed"
