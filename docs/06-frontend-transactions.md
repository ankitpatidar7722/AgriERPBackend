# AgriERP — Frontend Transactions (Step 5b)

Billing, purchase entry, stock, payments and reports. Completes step 5.

---

## 1. What runs now

**21 routes, 75 source files.** New in 5b:

| Route | What it does |
|---|---|
| `/sales` | Invoice register with date range, status and unpaid filters |
| `/sales/new` | **The billing screen** — type-ahead, barcode, live GST, tender split |
| `/sales/[id]` | Invoice detail, post, cancel, margin (permission-gated) |
| `/sales/[id]/print` | Printable tax invoice with HSN summary and amount in words |
| `/purchases` | Purchase register |
| `/purchases/new` | Line entry with batch, expiry and **live landed-cost preview** |
| `/purchases/[id]` | Detail with landed cost per line, post, cancel |
| `/stock` | Ledger · Batches · Adjustments · Transfers, in four tabs |
| `/payments` | Receipts and payments with open-bill allocation |
| `/reports` | Stock · Expiry · Sales · Purchase · Profit · GST |

The sidebar now carries **Trading** (Sales, Purchases, Payments), **Inventory** (Stock, Reports) and **Masters**, ordered the way the working day runs.

---

## 2. Decisions worth knowing

### 2.1 The client previews the money; the server owns it

The billing and purchase screens recompute totals as you type — line amount, discount, GST split, round-off — so the operator sees the real figure before committing. Those numbers are a **preview**. On save the server recalculates everything from scratch and its result is what gets stored, because the GST split depends on comparing the shop's state to the customer's, which the browser has no business deciding.

### 2.2 One requested line can become several invoice lines

Billing sends `batchId: null` and lets the server pick by earliest expiry. Ask for 60 units and you may get two invoice lines, each naming its own batch and expiry — required both for the printed bill and for honest costing. The detail screen says so explicitly rather than leaving it as a surprise.

### 2.3 Landed cost is shown before posting, not after

Purchase entry previews the same calculation the server performs: freight spread across lines by taxable value, divided by quantity **including free goods**. That figure becomes the batch cost and, later, the cost on every sale — so it is on screen while the operator can still correct a rate, not discovered afterwards.

### 2.4 Payments allocate oldest-first

"Apply oldest first" walks the open bills in age order. The oldest debt is the one most at risk, and it is what a collection conversation is actually about. Anything left over stays as an on-account advance rather than being spread automatically — an unallocated balance is real information, not an error to hide.

### 2.5 Cancelling a receipt is the bounced-cheque path

The confirm dialog says what will happen: every bill the payment settled reopens for its allocated amount. Both cancel dialogs require a typed reason, because a cancelled document stays in the record forever and "why" is the part someone will need later.

### 2.6 Stock is read-only

The stock screen has no edit controls anywhere. Stock changes by posting a document — purchase, sale, adjustment, transfer — never by typing over a number. Adjustments and transfers are created as drafts and post separately, because a physical count is entered over hours and must not move stock until someone signs it off.

### 2.7 Printing is HTML, not a generated PDF

The invoice is ordinary markup printed through the browser. The shop picks its own paper, the layout is the one on screen, and there is no second rendering path to keep in step. App chrome carries `no-print` — **verified under emulated print media**, not assumed.

---

## 3. Verification

Five suites, all green:

```
Transactions API HTTP   49 passed, 0 failed
Masters API HTTP        32 passed, 0 failed
Frontend integration    30 passed, 0 failed
Persistence (EF model)   8 passed, 0 failed
SQL smoke               10 passed, 0 failed
```

Plus two browser checks that a build cannot make:

**`scripts/billing-flow-check.mjs`** — signs in, searches a product through the type-ahead, puts it on a bill, checks the total moved off zero, clicks *Exact cash*, posts the invoice, and opens the printable version. Asserts the posted bill reads as Posted, has items, and that the printed page carries "TAX INVOICE", a grand total and an amount in words. Screenshots every transaction screen in light and dark, desktop and mobile.

**`scripts/print-media-check.mjs`** — switches the page to `print` media and asserts the sidebar, navbar and Print button are gone while the document survives. Without it, "we added `no-print`" is a claim, not a fact.

```powershell
cd Frontend
npm install --no-save playwright && npx playwright install chromium --only-shell
node scripts/billing-flow-check.mjs ./shots "ZZ Confidor"
node scripts/print-media-check.mjs ./shots http://localhost:3000/sales/1
```

Both need `sec.Users.MustChangePassword` cleared for the admin first, and restored after.

Final run: **no problems**. A bill was raised, posted and printed end-to-end through the UI; GST split verified as CGST ₹135 + SGST ₹135 on ₹1,500 taxable at 18%.

---

## 4. Problems this step surfaced

**The API had no payment-modes endpoint.** Billing needs one for the tender split and I had stubbed a hook pointing at the wrong URL with `enabled: false` — dead code that would have silently rendered an empty payment panel. Added `GET /lookups/payment-modes` properly, returning the `RequiresReference` flag so a cheque can demand a reference number and cash cannot.

**The test purge left an orphan invoice.** It matched sales on the party, but a bill raised through the UI as an unnamed walk-in has neither a ZZ customer nor a ZZ walk-in name. Worse, the fix was subtle: sale detail rows are deleted before the headers, so a subquery looking for them at header-delete time finds nothing. Sale ids are now captured into a temp table **before** anything is deleted.

**Unused imports failed the production build.** `next build` runs ESLint and treats `no-unused-vars` as an error — `tsc --noEmit` passes them happily. Worth knowing: a clean typecheck does not mean a clean build.

Also noticed during seeding: the API correctly refused three credit sales that would have pushed a customer past a ₹50,000 limit. That was the guard working, not a failure.

---

## 5. Not built

- **Sales and purchase returns screens.** The API supports both and the transaction suite exercises them; the UI does not yet. Returns are rarer than billing and were the right thing to defer.
- **Purchase orders screen.** Same — endpoints exist, screen does not.
- **Opening stock screen.** The endpoint is live; loading a shop's starting position is a one-time job better done through the bulk-import path in a later step.
- **Server-side export of a whole dataset.** Exports remain client-side and page-scoped.

---

## 6. Next: Step 6 — Testing

The suites built so far are integration and browser checks. Step 6 adds the unit tests that do not need a database or a server: GST splitting and rounding, landed-cost apportionment, FEFO allocation, amount-in-words, and the money model's edge cases — plus a CI script that runs everything in one command.
