# =============================================================================
#  AgriERP - frontend integration smoke test (step 5a)
#
#  Verifies the Next.js app is served AND that every call it makes to the API
#  succeeds from the browser's origin - including the CORS preflight, which is
#  the one failure a server-side test can never catch.
#
#  Run with both running:  API on :5215, web on :3000
# =============================================================================
$ErrorActionPreference = 'Stop'
# The runner decides the port and exports it; 3000 is only the standalone
# default, and it is often owned by another project's dev server.
$web  = if ($env:WEB_URL) { $env:WEB_URL } else { 'http://localhost:3000' }
$api  = 'http://localhost:5215/api'
$pass = 0; $fail = 0

function Check($name, $cond, $detail = '') {
  if ($cond) { Write-Output "PASS  $name"; $script:pass++ }
  else { Write-Output "FAIL  $name  $detail"; $script:fail++ }
}

# ---- pages are served --------------------------------------------------------
$routes = @('/login', '/dashboard', '/items', '/item-subgroups', '/companies',
            '/suppliers', '/customers', '/units', '/change-password')

# A 200 is NOT enough on its own. Any single-page app listening on :3000 -
# another project's Vite dev server, say - answers 200 for every path it does
# not recognise, so these checks once passed in full while AgriERP was not
# running at all. The body has to prove it is this app.
foreach ($route in $routes) {
  try {
    $response = Invoke-WebRequest -Uri "$web$route" -UseBasicParsing -TimeoutSec 10
    $isThisApp = $response.Content -match '/_next/' -or $response.Content -match 'Shree Ram'
    Check "Page $route served by AgriERP (HTTP $($response.StatusCode))" `
      ($response.StatusCode -eq 200 -and $isThisApp) `
      $(if (-not $isThisApp) { "200 came from a different app on $web" })
  } catch {
    Check "Page $route served by AgriERP" $false "$($_.Exception.Message)"
  }
}

# The root redirects into the app; the guard decides where the user lands.
try {
  $root = Invoke-WebRequest -Uri $web -UseBasicParsing -TimeoutSec 10 -MaximumRedirection 5
  Check "Root redirects into the app (HTTP $($root.StatusCode))" ($root.StatusCode -eq 200)
} catch {
  Check "Root redirects into the app" $false "$($_.Exception.Message)"
}

# ---- the login shell actually rendered ---------------------------------------
# The point of this check is that the form is in the PRE-RENDERED html rather
# than behind a Suspense boundary - useSearchParams once pushed the whole page
# into one, leaving a bare spinner in the static shell. So it looks for the
# inputs themselves, not for label copy that any redesign will move.
#
# The greeting is deliberately NOT asserted here: it depends on the viewer's
# clock, so it is filled in after mount and cannot appear in server output.
$loginHtml = (Invoke-WebRequest -Uri "$web/login" -UseBasicParsing).Content
Check "Login page renders the sign-in form" `
  ($loginHtml -match 'id="userName"' -and $loginHtml -match 'id="password"' -and $loginHtml -match 'Login to your account')
Check "Brand name is in the pre-rendered shell" ($loginHtml -match 'Shree Ram')
Check "Theme variables are in the served CSS" ($loginHtml -match '_next/static/css')

# ---- CORS: the check a server-side test cannot make --------------------------
# The browser sends a preflight before any request carrying an Authorization
# header. If the API does not answer it, every single call from the app fails
# with a message that names CORS but points at nothing useful.
try {
  $preflight = Invoke-WebRequest -Uri "$api/auth/me" -Method Options -UseBasicParsing -TimeoutSec 10 `
    -Headers @{
      'Origin'                         = $web
      'Access-Control-Request-Method'  = 'GET'
      'Access-Control-Request-Headers' = 'authorization,content-type'
    }
  $allowOrigin = $preflight.Headers['Access-Control-Allow-Origin']
  Check "CORS preflight allows $web (returned '$allowOrigin')" ($allowOrigin -eq $web)
} catch {
  Check "CORS preflight allows $web" $false "$($_.Exception.Message)"
}

# ---- every endpoint the frontend calls ---------------------------------------
$headers = @{ Origin = $web; 'Content-Type' = 'application/json' }

$login = Invoke-RestMethod -Uri "$api/auth/login" -Method Post -Headers $headers `
  -Body (@{ userName = 'admin'; password = 'Admin@123' } | ConvertTo-Json)
Check "Login from the browser origin succeeds" ($login.success -eq $true)

$auth = @{ Authorization = "Bearer $($login.data.accessToken)"; Origin = $web }

$endpoints = @(
  @{ name = 'Current user (/auth/me)';        url = "$api/auth/me" },
  @{ name = 'Dashboard (all six blocks)';     url = "$api/dashboard" },
  @{ name = 'Products list';                  url = "$api/items?page=1&pageSize=25" },
  @{ name = 'Categories list';                url = "$api/item-subgroups?page=1&pageSize=25" },
  @{ name = 'Companies list';                 url = "$api/companies?page=1&pageSize=25" },
  @{ name = 'Suppliers list';                 url = "$api/suppliers?page=1&pageSize=25" },
  @{ name = 'Customers list';                 url = "$api/customers?page=1&pageSize=25" },
  @{ name = 'Units list';                     url = "$api/units?page=1&pageSize=25" },
  @{ name = 'Product form lookups (1 call)';  url = "$api/lookups/item-form" },
  @{ name = 'States lookup';                  url = "$api/lookups/states" },
  @{ name = 'Customer villages';              url = "$api/customers/villages" }
)

foreach ($endpoint in $endpoints) {
  try {
    $result = Invoke-RestMethod -Uri $endpoint.url -Headers $auth -TimeoutSec 15
    Check "$($endpoint.name) responds" ($result.success -eq $true)
  } catch {
    Check "$($endpoint.name) responds" $false "$($_.Exception.Message)"
  }
}

# ---- the dashboard payload has the shape the charts expect -------------------
$dashboard = (Invoke-RestMethod -Uri "$api/dashboard" -Headers $auth).data
Check "Dashboard headline present" ($null -ne $dashboard.headline.asOnDate)
Check "Dashboard alerts present" ($null -ne $dashboard.alerts)
Check "Trend series has 12 months for the chart" (@($dashboard.monthlyTrend).Count -eq 12)
Check "Trend points carry all three series keys" (
  $null -ne $dashboard.monthlyTrend[0].salesAmount -and
  $null -ne $dashboard.monthlyTrend[0].purchaseAmount -and
  $null -ne $dashboard.monthlyTrend[0].profitAmount -and
  $null -ne $dashboard.monthlyTrend[0].monthLabel)
Check "Category stock block present for the donut" ($null -ne $dashboard.itemSubGroupStock)

# ---- enums arrive as strings, not integers -----------------------------------
# The API serialises enums as names. If that ever flipped to numbers the UI
# would render "0" where it should say "Retail".
$customers = (Invoke-RestMethod -Uri "$api/customers?page=1&pageSize=1" -Headers $auth).data
if (@($customers.items).Count -gt 0) {
  Check "Enums serialise as strings (customerType)" ($customers.items[0].customerType -is [string])
} else {
  Write-Output "SKIP  Enum check - no customers on file"
}

Write-Output ""
Write-Output "-----------------------------------------------------"
Write-Output "RESULT: $pass passed, $fail failed"
