# AgriERP — Testing (Step 6)

How the system is proved correct, and one command that proves it.

---

## 1. One command

```powershell
pwsh tests/run-all-tests.ps1
```

Seven layers, ordered fastest-and-most-isolated first, so a broken build or a bad
calculation fails in seconds rather than after a browser has started:

| # | Suite | Needs | Cases | What it proves |
|---|---|---|---|---|
| 1 | Build | — | — | Backend and frontend compile, with ESLint as an error |
| 2 | Unit (.NET) | — | 66 | GST, FEFO, landed cost, amount-in-words |
| 3 | Unit (web) | — | 48 | Billing preview, Indian formatting, greeting, financial year |
| 4 | SQL | SQL Server | 10 | Schema behaviour against the real database |
| 5 | Persistence | SQL Server | 8 | The EF model matches the hand-written schema |
| 6 | API | SQL Server | 131 | Masters, transactions and item groups over real HTTP |
| 7 | Browser | Playwright | 6 | The app driven in headless Chromium |

**≈270 automated checks, reported as 19 pass/fail suites in about five minutes.**
Useful variants:

```powershell
pwsh tests/run-all-tests.ps1 -UnitOnly      # suites 1-3, nothing external, ~40s
pwsh tests/run-all-tests.ps1 -SkipBuild     # reuse build output on a repeat run
pwsh tests/run-all-tests.ps1 -SkipBrowser   # everything but Playwright
```

Suite 7 skips itself with a message when Playwright is not installed — both
halves are checked, the npm package and the browser binary, because they install
separately and a missing package otherwise reports as a confusing
`ERR_MODULE_NOT_FOUND` instead of a clean skip. The skip is deliberate: a machine
without Chromium still gets a complete verdict from the other six rather than a
red run it cannot fix.

---

## 2. The shape of the pyramid

Each layer tests something the layer below it cannot.

```
   Browser        6 flows      does a human get a bill out of this?
   API          131 checks     do the endpoints agree with their contract?
   Persistence    8 tests      does the EF model match the real schema?
   SQL           10 checks     does the database enforce its own rules?
   Unit         114 tests      is the arithmetic right?
```

Each layer also costs more than the one below it. The 114 unit tests finish in
under a second; the browser layer needs a build, two servers, a fixture and a
headless Chromium, and takes most of the five minutes.

The arithmetic layer is the widest on purpose. Money rules are where a defect is
both most likely and most expensive — a wrong GST split is a compliance problem,
a wrong cost is a business decision made on a false number — and they are the
only layer that can be tested exhaustively in milliseconds.

---

## 3. Making the critical logic testable

Two pieces of logic mattered most and were the hardest to reach, because both
lived inside methods that also talked to a database or a React form. Both were
extracted before being tested; the extraction is the interesting part.

### 3.1 `StockAllocationRules.cs` — FEFO and landed cost

FEFO batch picking was embedded in the sale-posting service, entangled with an EF
query. `FefoAllocation.Allocate` now takes an ordered list of candidates and a
required quantity and returns the split, knowing nothing about the database. The
service still owns *which* batches are candidates and in what order; the pure
function owns *how much* comes from each.

That separation is what makes the awkward cases testable at all:

- asking for 60 when the earliest batch holds 20 spans three batches;
- asking for more than the total returns **empty**, not a partial allocation — a
  half-filled sale silently posting is worse than a refusal;
- with `allowShortfall`, the excess lands on the **last** batch, so an adjustment
  that legitimately drives stock negative still records where it went.

`LandedCost.Apportion` came out of the same file for the same reason. Freight is
spread across lines by taxable value, then divided by quantity **including free
goods** — the rule that turns a supplier's 10+1 scheme into a genuinely lower unit
cost. The test pins the worked example from the design doc (406.5574 and
426.8852 for a 100@400 / 50@420 purchase carrying 1000 freight) so the number a
purchase screen shows can be traced to a document.

### 3.2 `billing-math.ts` — the client's money preview

The billing screen recomputes totals as the operator types. That code was inside
a `useMemo` in a 700-line page component, which meant it could only be tested by
rendering the page. It now lives in `src/features/transactions/billing-math.ts`
as four pure functions, and the page imports them.

The purchase screen imports the same functions. It previously carried its own
copy of the totals loop, which is exactly how two screens drift apart; freight
simply joins the other charges, since both sit outside the taxable base and
before the round-off. **One implementation, one set of tests, two screens.**

---

## 4. What the unit tests actually pin

### 4.1 GST — 21 cases

The rule that earns its own tests: **CGST is rounded, SGST takes the remainder.**

```csharp
var cgst = Money(totalTax / 2m);
var sgst = totalTax - cgst;   // remainder, so the halves always sum exactly
```

Round both halves independently and a tax of ₹135.01 becomes 67.51 + 67.51 =
135.02, and the invoice no longer adds up. The test uses odd amounts precisely
because even ones cannot catch it.

Also covered: intra-state splits into CGST+SGST while inter-state produces a
single IGST; seeds at 0% produce zero tax rather than a zero-width rounding
artefact; cess adds on top of GST without entering the CGST/SGST halves; rounding
is `MidpointRounding.AwayFromZero`, not the .NET default of banker's rounding —
₹0.125 must become ₹0.13, the way a shop counts; and credit notes carry negative
amounts through the same path without sign errors.

### 4.2 Amount in words — 24 cases

Printed on every invoice, and the one place where a Western number-word library
would be quietly wrong: Indian invoices read **lakh and crore**, not million.
`1,23,456` is "One Lakh Twenty Three Thousand Four Hundred Fifty Six", and a
library that says "one hundred twenty-three thousand" produces a legally odd
document.

Paise **round**, they do not truncate: `99.999` prints "One Hundred Rupees Only",
not "Ninety Nine Rupees and Ninety Nine Paise".

### 4.3 FEFO — 11 cases

First **Expiry**, first out — not first in. A pesticide bought later but expiring
sooner must leave the shelf first, or it expires in stock and is written off. The
tests confirm the function honours the order it is given (the caller sorts by
expiry, so the function must not re-sort), respects per-batch availability rather
than treating stock as one pool, and handles fractional quantities because seeds
sell in half kilos.

### 4.4 Landed cost — 10 cases

Covered above. The zero-quantity guard is worth naming separately: a line with no
quantity returns a cost of 0 instead of dividing by zero and poisoning a batch
record with `Infinity`.

### 4.5 Formatting and the bill preview — 48 web cases

Lakh grouping (`1,23,456.78`, never `123,456.78`); quantities that drop trailing
zeros but keep three decimals; `null` rendering as `0.00` rather than `₹NaN` on a
bill.

`toIsoDate` gets the most attention for its size. `Date#toISOString()` converts
to UTC first, so in IST (+5:30) a bill entered at 00:30 files under the
*previous* day. The tests cover both ends of the day — which one breaks depends
on whether the machine sits ahead of or behind UTC, so one of the two catches it
either way — plus a property-style loop asserting the output always reconstructs
the input's own local year, month and day.

The greeting bands (Good Morning / Afternoon / Evening / Night) and the Indian
financial-year label are pinned the same way — every boundary minute checked,
because an off-by-one there greets the morning chai with "Good Evening" or files
a January bill under the wrong year.

---

## 5. SQL: `database/tests/smoke_test.sql`

Ten checks against the real database, each one a rule the application layer must
never be trusted to enforce alone. The script creates its own `ZZ`-prefixed
fixtures and purges them at the end, so it is safe to run repeatedly against a
database with real data.

```
PASS  1. Inward posting updated batch quantities (50 + 20).
PASS  2. vw_ProductStock rolled up 70 units valued 26,440.00 at batch cost.
PASS  3. FEFO offered ZZB-EARLY (expires 2026-11-30) before ZZB-LATE.
PASS  4. Outward posting reduced ZZB-EARLY from 20 to 12.
PASS  5. vw_StockLedger running balance closed at 62.
PASS  6. Overselling refused (error 50024) and stock left untouched.
PASS  7. Cancellation restored stock to 20 by appending a reversal, not deleting.
PASS  8. usp_RebuildBatchQuantities found zero drift between cache and journal.
PASS  9. Numbering produced INV/2026-27/00001 then INV/2026-27/00002.
PASS 10. Money model arithmetic: taxable 4,275.00 / total 5,044.50 / profit 475.00.
```

Checks 6, 7 and 8 are the ones that justify the design:

- **6** proves overselling raises error 50024 *and leaves stock untouched* — a
  rollback that half-applied would be worse than no check at all.
- **7** proves cancellation **appends a reversal** rather than deleting rows. The
  stock journal is append-only, so last month's closing stock still reconstructs
  correctly after this month's cancellation.
- **8** runs `usp_RebuildBatchQuantities` and asserts zero drift between the
  cached `Batches.Quantity` and the sum of the journal. The cache exists for
  speed; this check proves it is never the source of truth.

---

## 6. Persistence: does the model match the schema?

The SQL scripts own the schema and EF migrations are never used — 39 persisted
computed columns, 66 CHECK constraints, filtered unique indexes and 6 stored
procedures are all things migrations model poorly. The consequence is a real
risk: the C# model and the database can drift with nothing to notice.

`tests/AgriERP.Persistence.Tests` closes that gap by opening the actual database
and asserting the model against it — every entity maps to a table that exists,
every mapped property to a column that exists with a compatible type, computed
columns are marked as computed (so EF never tries to write them), decimal
precision matches, and every declared relationship has a real foreign key behind
it.

These 8 tests are the reason a schema change made in SQL cannot silently break
the API at runtime.

---

## 7. API: 131 checks over real HTTP

Four PowerShell suites drive a running API with `Invoke-RestMethod` — no mocks,
no in-memory provider, a real Kestrel and a real SQL Server:

| Script | Checks | Covers |
|---|---|---|
| `tests/api/api-smoke.ps1` | 32 | Auth, the 6 master modules, validation, permissions |
| `tests/api/transactions-smoke.ps1` | 49 | Purchase, sale, stock, payments, reports |
| `tests/api/item-groups-smoke.ps1` | 18 | Per-group form definition, code series, extra-field round-trip |
| `tests/api/frontend-smoke.ps1` | 32 | The exact endpoint shapes the web app consumes |

They test the things only an integration test can reach: that a 401 comes back
without a token and a 403 with the wrong permission; that refresh-token rotation
issues a new pair and that **reusing** an old refresh token revokes the family;
that posting a sale moves stock and cancelling it moves it back; that a document
number is never issued twice; that validation returns 400 with a field-level
message rather than a 500.

`frontend-smoke.ps1` exists because a contract mismatch — an endpoint returning
`items` where the client expects `data` — passes both a backend test and a
frontend test while breaking the running app.

`item-groups-smoke.ps1` proves the part the restructure is for: that Seed Master
serves a "Seed details" section while Product Master serves "Safety" instead;
that a seed takes an `S-` code and a pesticide a `P-`; that germination round-
trips through `ItemMasterDetails` keyed by field id; and that a required group
field left blank, or a field belonging to another group, is refused with a 400.

Every suite creates `ZZ`-prefixed fixtures and purges them afterwards. There is
exactly one purge definition — `tests/api/purge-zz-data.sql`, shared by the
transaction suite, the demo seeder and the runner's `finally` block — because two
copies drift, and the half that forgets a child table leaves an orphan that fails
the *next* run with a duplicate-key error pointing nowhere near the cause.

It captures sale ids into a temp table before deleting anything, because detail
rows go first and a subquery looking for them afterwards finds nothing. That is
the bug that once left an orphan invoice behind: a walk-in bill raised through
the UI has no `ZZ` party name, so it can only be matched by its products — and
its products are gone by the time the header delete runs.

---

## 8. Browser: the part no assertion catches

Six Playwright scripts in `Frontend/scripts/`. Each takes its web URL from
the `WEB_URL` env var the runner exports, so the whole suite moves off port 3000
— commonly owned by another project's dev server — with a single setting:

- **`visual-check.mjs`** logs in, walks the dashboard and every master screen in
  both light and dark, screenshots each, and fails on any console error, page
  error, empty render, or horizontal overflow at a 390px viewport.
- **`billing-flow-check.mjs`** walks every transaction screen, then types a
  product into the billing screen, takes the type-ahead suggestion, sets a
  quantity, tenders cash, posts the bill, and opens the printable invoice — the
  whole counter workflow, in a browser.
- **`print-media-check.mjs`** opens the sales register, prints the first invoice
  under emulated `print` media, and asserts the app chrome disappears while the
  document survives. A print stylesheet is invisible on screen; this is the only
  way to see what the printer sees without spending paper.
- **`item-create-check.mjs`** proves the item form is one panel with nothing
  hidden, that an incomplete submit says why instead of doing nothing, and that a
  complete one saves — the guard on a bug where a required field on an unopened
  tab silently blocked Create.
- **`item-form-check.mjs`** proves the form follows the group: it switches
  between Seed, Fertilizer and Product and asserts the questions change with it.
- **`login-shot.mjs`** drives the login screen and the remember-me round trip,
  and checks the time-of-day greeting against the browser's own clock.

`scripts/find-overflow.mjs` and `scripts/ui-shot.mjs` are diagnostics, not tests:
the first lists every element crossing the viewport edge, deepest first, turning
"the page scrolls sideways" into a specific element and class list.

This layer has earned its place twice.

**Dark mode did not work at all** and nothing else noticed:
`defaultTheme="light"` with `enableSystem` pins next-themes to light and the OS
preference is never consulted. Every unit test passed, every API check passed,
and the screenshots were the only thing that showed a light screen where a dark
one belonged.

**The dashboard scrolled sideways on a phone.** At a 390px viewport the body was
409px wide. The cause is a CSS default that is easy to forget: a grid item gets
`min-width: auto`, so it refuses to shrink below the intrinsic width of its
content. The recent-bills and top-products tables were pushing their cards past
the screen, and the `overflow-x-auto` wrapper already inside each card could do
nothing about it, because the wrapper's own ancestors were free to grow. Adding
`min-w-0` to the grid children lets the card be narrow and hands the scrolling
back to the wrapper where it belongs.

That bug had been present since the dashboard was built and was only reachable
with data on the screen — which is what the next section is about.

---

## 9. The fixture problem: an assertion about nothing

Every API suite creates `ZZ`-prefixed records and purges them when it finishes.
That is correct — a test must not leave debris — but it means that by the time
the browser suites start, the database holds no products and no trade at all.

On an empty database the dashboard renders "No sales or purchases in the last 12
months yet" and the billing type-ahead finds nothing. **Both are the right
behaviour.** But it makes "the charts drew geometry" and "a bill can be built"
assertions about nothing, which is worse than having no assertion at all: they
look like coverage.

`tests/api/seed-demo-data.ps1` lays down a small trading history before the
browser suites and the runner clears it afterwards in a `finally` block:

- six products across six categories — the smallest set that exercises the
  donut's five-slices-plus-Other fold;
- three customers on retail, wholesale and dealer terms;
- two purchases two months apart, so every product has an early and a late batch
  and FEFO has a real choice to make;
- seven invoices spread over four months, split between cash and credit;
- one part payment, so the collection screens are not empty.

It drives the **API**, not `INSERT` statements. Posting a purchase allocates a
document number, apportions freight into landed cost and appends to the stock
journal; posting a sale picks batches by expiry and freezes the cost onto the
line. Hand-written inserts would have to reproduce all of that, and the moment
they got it slightly wrong the fixture would look right while disagreeing with
the rules the application enforces.

It also verifies its own work before returning, failing loudly if fewer than
three months carry sales or fewer than three categories hold stock. A fixture
that half-worked would surface later as a browser failure that looks like a UI
bug.

---

## 10. Proving the tests can fail

A green suite proves nothing unless a broken implementation turns it red. Five
deliberate mutations were introduced, run, and reverted:

| Mutation | Failures | What would have shipped |
|---|---|---|
| Banker's rounding instead of `AwayFromZero` | 2 | ₹0.125 → ₹0.12; every bill off by a paisa |
| FEFO ignoring per-batch availability | 5 | Batches oversold; stock cache drifts negative |
| Landed cost ignoring free goods | 2 | A 10+1 scheme shows no cost benefit; margins understated |
| `toIsoDate` using `toISOString()` | 3 | Early-morning bills filed under yesterday |
| Bill total dropping the other-charges term | 2 | Freight vanishes from the purchase total |

Each mutation was caught, and each was reverted after the run. The counts matter
as much as the pass/fail: a mutation caught by a *single* test is a warning that
the case is pinned in one place only.

The last row is what that warning looks like in practice. When the purchase
screen was moved onto the shared `computeBillTotals`, dropping the other-charges
term failed exactly **one** test — and that term now carries a supplier's
freight, so a single assertion was too thin a thread for it. Two tests were added
from the purchase angle: one asserting the grand total rises by exactly the
charges while the taxable base and tax do not move, one asserting the total still
lands on a whole rupee once an awkward charge is added. The same mutation now
fails two.

---

## 11. What is not tested, and why

Stated plainly, because an undeclared gap reads as coverage:

- **No load or concurrency testing.** Document numbering is serialised in a
  stored procedure with `UPDLOCK`, which is correct by construction, but nothing
  here proves it under 50 simultaneous invoices. For a single-counter shop that
  is an acceptable gap; it stops being one the day a second till is added.
- **No test for the reports' SQL under large data.** They are correct against
  the demo fixture — seven invoices over four months — and have not been run
  against a year of real trade. Query plans that are fine at that size are the
  usual place an ERP slows down first.
- **The demo fixture is small by design, which bounds what the browser suites
  can see.** Six products cannot surface a paging bug in a product list of two
  thousand, and four months of trade cannot surface a chart that mislabels a
  year boundary.
- **The deferred screens have no browser coverage** — sales/purchase returns,
  purchase orders and opening stock have working, API-tested endpoints but no UI,
  so there is nothing to drive.
- **Printer output is verified as emulated print media, not on paper.** Margins
  on a specific thermal printer remain a physical check.

---

## 12. Running the pieces individually

```powershell
# .NET unit tests
dotnet test tests/AgriERP.Application.Tests

# Web unit tests (watch mode available with npm run test:watch)
cd Frontend ; npm test

# SQL behaviour  (-I is required: QUOTED_IDENTIFIER must be ON for the
# filtered indexes and persisted computed columns, or Msg 1934 stops it)
sqlcmd -S DESKTOP-L96U5S2\MSSQLSERVER03 -U Indus -P <password> -C -I -d AgriERP `
       -i database/tests/smoke_test.sql

# API suites (the API must already be running)
pwsh tests/api/api-smoke.ps1
pwsh tests/api/transactions-smoke.ps1
pwsh tests/api/frontend-smoke.ps1

# Demo fixture - lay it down, or take it away again
pwsh tests/api/seed-demo-data.ps1
pwsh tests/api/seed-demo-data.ps1 -PurgeOnly

# Browser (needs the API and web both running, and the demo fixture in place)
cd Frontend
npm install --no-save playwright ; npx playwright install chromium --only-shell
node scripts/visual-check.mjs ./shots
node scripts/billing-flow-check.mjs ./shots "ZZ Confidor"
node scripts/print-media-check.mjs ./shots
node scripts/find-overflow.mjs          # diagnostic, not a test
```

The browser suites need the admin account's `MustChangePassword` flag cleared —
the guard correctly blocks every screen until the seeded password is changed.
Patching `localStorage` to get past it does not work and should not be tried:
`AuthProvider` re-fetches `/auth/me` on mount and the server's answer wins. The
runner lifts the flag in the database for the run and restores it in a `finally`
block, so an aborted run still leaves the account protected.

---

## 13. Three PowerShell traps worth recording

Both cost a debugging session and both are invisible until they bite.

**`2>&1` on a native command while `$ErrorActionPreference = 'Stop'`.** Every
stderr *line* becomes a terminating error — so a tool that merely prints progress
to stderr (npm, dotnet and npx all do) aborts the whole run despite exiting 0.
`Invoke-Native` in the runner drops the preference to `Continue` for the duration
of the call, so only the real exit code decides pass or fail.

**`Set-Content -Encoding UTF8` writes a BOM.** In Windows PowerShell 5.1 that is
not optional. Writing `package.json` that way produced a leading `EF BB BF` that
`JSON.parse` rejects, and `next build` failed with an error that pointed nowhere
near the cause. Files that other tools parse are written byte-level with
`[System.IO.File]::WriteAllBytes` and a `UTF8Encoding` constructed with
`$false` — and with an **absolute** path, because `System.IO` resolves against
the process working directory, not PowerShell's current location.

**`Stop-Process` kills a process, not its tree.** `npm.cmd run start` spawns
`cmd.exe`, which spawns the real `next start`. Killing the recorded id left that
grandchild running — and because it had inherited the script's stdout handle,
the pipe never closed and anything reading the run's output hung indefinitely,
**thirty-four minutes after the run itself had finished and printed its verdict**.
The symptom looks exactly like a hung test suite and is nothing of the kind.
`Stop-Servers` uses `taskkill /PID <id> /T /F`.

A related habit worth keeping: `Start-Process` does **not** inherit PowerShell's
current location, so `Push-Location` before it does nothing. Pass
`-WorkingDirectory` explicitly. And a path containing a space needs embedded
quotes inside `-ArgumentList`, or `D:\Agriculture shop\...` arrives as two
arguments and `dotnet` reports `'D:\Agriculture' is not a valid project file.`

---

## 14. Status

Last full run: **19 of 19 suites passed in 283 seconds**, and again on an
immediate second run — the purge, the demo seed and the password restore all
leave the database as they found it.

| Suite | Result |
|---|---|
| Backend build | PASS |
| Frontend build | PASS |
| Application unit tests | 66 / 66 |
| Web unit tests | 48 / 48 |
| Database behaviour | 10 / 10 |
| EF model matches schema | 8 / 8 |
| API started | PASS |
| Web started | PASS |
| Masters API | 32 / 32 |
| Transactions API | 49 / 49 |
| Item groups API | 18 / 18 |
| Frontend integration | 32 / 32 |
| Demo data seeded | 4 months of trade across 6 sub groups |
| Visual check (masters + dashboard) | PASS |
| Billing flow (bill posted in a browser) | PASS |
| Print layout (emulated print media) | PASS |
| Item form reports hidden validation errors | PASS |
| Item form follows the item group | PASS |
| Login page and remember-me | PASS |

The run leaves nothing behind: `ZZ` fixtures purged, `MustChangePassword`
restored to 1, both servers stopped, the user's own dev server on port 3000
untouched, and screenshots written to the log folder.

The web server runs on **port 3001** by default now — 3000 is commonly owned by
another project, and the runner refuses to start rather than testing whatever
else answers on the port. Override with `-WebPort` / `-ApiPort`.

Item-group restructure complete (see `docs/08-item-groups.md`). Next: **deployment.**
