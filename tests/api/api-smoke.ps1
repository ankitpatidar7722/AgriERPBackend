$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5215/api'

# Clear residue from an interrupted previous run, so one failure does not
# poison every later run with duplicate-key errors.
#
# This used to be a third hand-written copy of the purge, and it carried an
# unconditional `SET CurrentNumber = 0` that rewound the product counter past
# records the shop had entered by hand - the next save then re-issued a code
# that already existed. The shared file resets a series only when nothing of
# that kind survives.
$purgeFile = Join-Path $PSScriptRoot 'purge-zz-data.sql'

function Purge {
  # -I sets QUOTED_IDENTIFIER ON. Without it sqlcmd refuses any DML against
  # tables carrying filtered indexes (Msg 1934).
  # -b makes sqlcmd exit non-zero on error, so a blocked delete is not swallowed.
  $out = & sqlcmd -S 'DESKTOP-L96U5S2\MSSQLSERVER03' -U Indus -P $env:AGRIERP_SQL_PASSWORD `
    -C -I -b -d AgriERP -i $purgeFile 2>&1
  if ($LASTEXITCODE -ne 0) { throw "Purge failed:`n$out" }
}

Purge

$pass = 0; $fail = 0
function Check($name, $cond, $detail='') {
  if ($cond) { Write-Output "PASS  $name"; $script:pass++ }
  else { Write-Output "FAIL  $name  $detail"; $script:fail++ }
}
function Body($e) {
  $s = $e.Exception.Response.GetResponseStream(); $r = New-Object IO.StreamReader($s); $r.ReadToEnd()
}

# 1. login with wrong password -> 401, generic message
try {
  Invoke-RestMethod -Uri "$base/auth/login" -Method Post -ContentType 'application/json' `
    -Body (@{userName='admin';password='wrong-password'} | ConvertTo-Json) | Out-Null
  Check '1. Wrong password rejected' $false 'no error raised'
} catch {
  $code = [int]$_.Exception.Response.StatusCode
  $b = Body $_ | ConvertFrom-Json
  Check '1. Wrong password rejected (401, generic message)' `
    ($code -eq 401 -and $b.message -eq 'Invalid username or password.') "got $code / $($b.message)"
}

# 2. login with seeded default
$login = Invoke-RestMethod -Uri "$base/auth/login" -Method Post -ContentType 'application/json' `
  -Body (@{userName='admin';password='Admin@123'} | ConvertTo-Json)
$token = $login.data.accessToken
$refresh = $login.data.refreshToken
Check '2. Login succeeds with seeded default password' ($login.success -and $token.Length -gt 100)
Check '3. MustChangePassword flag forces password change' ($login.data.user.mustChangePassword -eq $true)
Check "4. Admin carries all 70 seeded permissions ($($login.data.user.permissions.Count))" ($login.data.user.permissions.Count -eq 70)

$h = @{ Authorization = "Bearer $token" }

# 5. unauthenticated request is refused
try {
  Invoke-RestMethod -Uri "$base/item-subgroups" | Out-Null
  Check '5. Unauthenticated request refused' $false
} catch {
  Check '5. Unauthenticated request refused (401)' ([int]$_.Exception.Response.StatusCode -eq 401)
}

# 6. paged categories
$cats = Invoke-RestMethod -Uri "$base/item-subgroups?page=1&pageSize=5" -Headers $h
Check "6. Categories paged (total $($cats.data.totalCount), page size $($cats.data.items.Count))" `
  ($cats.data.totalCount -eq 16 -and $cats.data.items.Count -eq 5 -and $cats.data.totalPages -eq 4)

# 7. search
$search = Invoke-RestMethod -Uri "$base/item-subgroups?search=seed" -Headers $h
Check "7. Search 'seed' returns 5 (Seeds + 4 sub-types)" ($search.data.totalCount -eq 5) "got $($search.data.totalCount)"

# 8. sorting
$sorted = Invoke-RestMethod -Uri "$base/item-subgroups?sortBy=name&sortDescending=true&pageSize=100" -Headers $h
$names = $sorted.data.items | ForEach-Object { $_.itemSubGroupName }
$expected = $names | Sort-Object -Descending
Check '8. Sort by name descending' (($names -join '|') -eq ($expected -join '|'))

# 9. pageSize clamped to 200
$clamped = Invoke-RestMethod -Uri "$base/item-subgroups?pageSize=99999" -Headers $h
Check "9. pageSize clamped to 200 (got $($clamped.data.pageSize))" ($clamped.data.pageSize -eq 200)

# 10. create sub group - it must name the item group it sits under
$firstGroup = (Invoke-RestMethod -Uri "$base/item-groups" -Headers $h).data[0].itemGroupId
$new = Invoke-RestMethod -Uri "$base/item-subgroups" -Method Post -Headers $h -ContentType 'application/json' `
  -Body (@{itemSubGroupCode='ZZTEST';itemSubGroupName='ZZ Test Category';itemGroupId=$firstGroup;displayOrder=99;isActive=$true} | ConvertTo-Json)
$catId = $new.data.itemSubGroupId
Check "10. Category created (id $catId)" ($new.success -and $catId -gt 0)

# 11. duplicate code -> 400 with field error
try {
  Invoke-RestMethod -Uri "$base/item-subgroups" -Method Post -Headers $h -ContentType 'application/json' `
    -Body (@{itemSubGroupCode='ZZTEST';itemSubGroupName='Another Name';itemGroupId=$firstGroup;isActive=$true} | ConvertTo-Json) | Out-Null
  Check '11. Duplicate code rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '11. Duplicate code rejected (400, field-level error)' `
    ([int]$_.Exception.Response.StatusCode -eq 400 -and $null -ne $b.errors.ItemSubGroupCode) "$($b.message)"
}

# 12. FluentValidation shape failure
try {
  Invoke-RestMethod -Uri "$base/item-subgroups" -Method Post -Headers $h -ContentType 'application/json' `
    -Body (@{itemSubGroupCode='';itemSubGroupName='';isActive=$true} | ConvertTo-Json) | Out-Null
  Check '12. Empty required fields rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '12. FluentValidation reports every failing field at once' `
    ([int]$_.Exception.Response.StatusCode -eq 400 -and $null -ne $b.errors.ItemSubGroupCode -and $null -ne $b.errors.ItemSubGroupName)
}

# 13. update
$upd = Invoke-RestMethod -Uri "$base/item-subgroups/$catId" -Method Put -Headers $h -ContentType 'application/json' `
  -Body (@{itemSubGroupCode='ZZTEST';itemSubGroupName='ZZ Test Renamed';itemGroupId=$firstGroup;displayOrder=98;isActive=$true} | ConvertTo-Json)
Check '13. Category updated' ($upd.data.itemSubGroupName -eq 'ZZ Test Renamed')

# 14. customer with credit limit but no mobile -> validation
try {
  Invoke-RestMethod -Uri "$base/customers" -Method Post -Headers $h -ContentType 'application/json' `
    -Body (@{customerName='ZZ No Mobile';creditLimit=5000;customerType='Retail'} | ConvertTo-Json) | Out-Null
  Check '14. Credit limit without mobile rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '14. Credit limit without mobile rejected' ($null -ne $b.errors.Mobile) "$($b.message)"
}

# 15. customer auto-code from number series
$cust = Invoke-RestMethod -Uri "$base/customers" -Method Post -Headers $h -ContentType 'application/json' `
  -Body (@{customerName='ZZ Ramesh Patil';village='Shirur';mobile='9822011122';customerType='Retail'} | ConvertTo-Json)
$custId = $cust.data.customerId
Check "15. Customer auto-numbered ($($cust.data.customerCode))" ($cust.data.customerCode -match '^CUS-\d{5}$')

# 16. duplicate mobile
try {
  Invoke-RestMethod -Uri "$base/customers" -Method Post -Headers $h -ContentType 'application/json' `
    -Body (@{customerName='ZZ Someone Else';mobile='9822011122';customerType='Retail'} | ConvertTo-Json) | Out-Null
  Check '16. Duplicate mobile rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '16. Duplicate mobile rejected' ($null -ne $b.errors.Mobile)
}

# 17. bad mobile format
try {
  Invoke-RestMethod -Uri "$base/customers" -Method Post -Headers $h -ContentType 'application/json' `
    -Body (@{customerName='ZZ Bad Mobile';mobile='1234567890';customerType='Retail'} | ConvertTo-Json) | Out-Null
  Check '17. Mobile starting with 1 rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '17. Mobile starting with 1 rejected' ($null -ne $b.errors.Mobile)
}

# 18. find by mobile
$found = Invoke-RestMethod -Uri "$base/customers/by-mobile/9822011122" -Headers $h
Check '18. Lookup by mobile returns customer + outstanding' `
  ($found.data.customerName -eq 'ZZ Ramesh Patil' -and $found.data.outstandingAmount -eq 0)

# 19. lookups in one call
$lk = Invoke-RestMethod -Uri "$base/lookups/item-form" -Headers $h
Check "19. Product-form lookups in one call (cat $($lk.data.itemSubGroups.Count), gst $($lk.data.gstSlabs.Count), units $($lk.data.units.Count))" `
  ($lk.data.itemSubGroups.Count -ge 16 -and $lk.data.gstSlabs.Count -eq 5 -and $lk.data.units.Count -eq 14 -and $lk.data.hsnCodes.Count -eq 12)

# 20. create product
$catInsec = ($lk.data.itemSubGroups | Where-Object { $_.code -eq 'INSEC' }).id
$unitBtl  = ($lk.data.units | Where-Object { $_.code -eq 'BTL' }).id
$unitMl   = ($lk.data.units | Where-Object { $_.code -eq 'ML' }).id
$gst18    = ($lk.data.gstSlabs | Where-Object { $_.totalRate -eq 18 }).id
$hsn3808  = ($lk.data.hsnCodes | Where-Object { $_.code -eq '3808' }).id

$prodBody = @{
  itemName='ZZ Confidor 17.8% SL 250ml'; shortName='ZZ Confidor'; technicalName='Imidacloprid 17.8% SL'
  itemSubGroupId=$catInsec; unitId=$unitBtl; packingSize=250; packingUnitId=$unitMl
  hsnId=$hsn3808; gstSlabId=$gst18
  purchaseRate=380.455; sellingRate=450; mrp=495; wholesaleRate=420; dealerRate=410; minSellingRate=400
  minStockLevel=10; maxStockLevel=100; barcode='ZZ8901234567'; isBatchTracked=$true; isExpiryTracked=$true
  isActive=$true
} | ConvertTo-Json
$prod = Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType 'application/json' -Body $prodBody
$prodId = $prod.data.itemId
Check "20. Item auto-numbered from its group series ($($prod.data.itemCode))" ($prod.data.itemCode -match '^P-\d{6}$')
Check "21. Rate precision preserved ($($prod.data.purchaseRate))" ($prod.data.purchaseRate -eq 380.455)
Check "22. New item reads OutOfStock" ($prod.data.stockStatus -eq 'OutOfStock' -and $prod.data.currentStock -eq 0)

# 23. selling above MRP rejected
try {
  $bad = ($prodBody | ConvertFrom-Json); $bad.itemName='ZZ Above MRP'; $bad.barcode=$null; $bad.sellingRate=600
  Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType 'application/json' `
    -Body ($bad | ConvertTo-Json) | Out-Null
  Check '23. Selling rate above MRP rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '23. Selling rate above MRP rejected' ($null -ne $b.errors.SellingRate) "$($b.message)"
}

# 24. duplicate name+company+pack
try {
  $dup = ($prodBody | ConvertFrom-Json); $dup.barcode=$null
  Invoke-RestMethod -Uri "$base/items" -Method Post -Headers $h -ContentType 'application/json' `
    -Body ($dup | ConvertTo-Json) | Out-Null
  Check '24. Duplicate name+company+pack rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '24. Duplicate name+company+pack rejected' ($null -ne $b.errors.ItemName)
}

# 25. barcode lookup
$bc = Invoke-RestMethod -Uri "$base/items/by-barcode/ZZ8901234567" -Headers $h
Check '25. Barcode scan returns billing payload (rate + GST + stock)' `
  ($bc.data.id -eq $prodId -and $bc.data.gstPercent -eq 18 -and $bc.data.sellingRate -eq 450)

# 26. billing type-ahead by technical name
$ta = Invoke-RestMethod -Uri "$base/items/search?search=Imidacloprid" -Headers $h
Check '26. Type-ahead finds product by technical name' (@($ta.data | Where-Object { $_.id -eq $prodId }).Count -eq 1)

# 27. stock status filter
$oos = Invoke-RestMethod -Uri "$base/items?stockStatus=OutOfStock&search=ZZ%20Confidor" -Headers $h
Check '27. StockStatus=OutOfStock filter works' ($oos.data.totalCount -eq 1)
$ins = Invoke-RestMethod -Uri "$base/items?stockStatus=InStock&search=ZZ%20Confidor" -Headers $h
Check '28. StockStatus=InStock excludes it' ($ins.data.totalCount -eq 0)

# 29. delete guard - category in use
try {
  Invoke-RestMethod -Uri "$base/item-subgroups/$catInsec" -Method Delete -Headers $h | Out-Null
  Check '29. Deleting an in-use category blocked' $false
} catch {
  Check '29. Deleting an in-use category blocked (409)' ([int]$_.Exception.Response.StatusCode -eq 409)
}

# 30. refresh token rotation
$rt = Invoke-RestMethod -Uri "$base/auth/refresh" -Method Post -ContentType 'application/json' `
  -Body (@{refreshToken=$refresh} | ConvertTo-Json)
Check '30. Refresh returns a new token pair' ($rt.data.refreshToken -ne $refresh -and $rt.data.accessToken.Length -gt 100)

# 31. reuse of rotated token -> revoked chain
try {
  Invoke-RestMethod -Uri "$base/auth/refresh" -Method Post -ContentType 'application/json' `
    -Body (@{refreshToken=$refresh} | ConvertTo-Json) | Out-Null
  Check '31. Reused refresh token rejected' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '31. Reused refresh token detected and session killed' `
    ([int]$_.Exception.Response.StatusCode -eq 401 -and $b.message -match 'no longer valid')
}

# 32. 404 shape
try {
  Invoke-RestMethod -Uri "$base/item-subgroups/999999" -Headers $h | Out-Null
  Check '32. Unknown id returns 404' $false
} catch {
  $b = Body $_ | ConvertFrom-Json
  Check '32. Unknown id returns 404 in ApiResponse envelope' `
    ([int]$_.Exception.Response.StatusCode -eq 404 -and $b.success -eq $false -and $b.traceId)
}

# ---- cleanup ----
Invoke-RestMethod -Uri "$base/items/$prodId" -Method Delete -Headers $h | Out-Null
Invoke-RestMethod -Uri "$base/customers/$custId" -Method Delete -Headers $h | Out-Null
Invoke-RestMethod -Uri "$base/item-subgroups/$catId" -Method Delete -Headers $h | Out-Null
# The API deletes are SOFT deletes - correct for masters, but they would
# accumulate across runs, so the rows are purged for real here.
Purge

Write-Output ""
Write-Output "Cleanup: test item, customer and sub group purged."
Write-Output "-------------------------------------------------"
Write-Output "RESULT: $pass passed, $fail failed"
