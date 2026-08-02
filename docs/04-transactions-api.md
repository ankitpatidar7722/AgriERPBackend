# AgriERP — Transactions, Reports and Dashboard (Step 4b)

Stock, Purchase, Sales, Payments, Reports and Dashboard. Completes step 4.

---

## 1. What runs now

**80 endpoints / 106 operations** across 14 controllers. Step 4b added:

| Controller | Covers |
|-----------|--------|
| `Stock` | ledger, batch stock, opening stock, adjustments (draft→post), transfers (draft→post) |
| `Purchases` | bills (draft→post→cancel), returns, purchase orders |
| `Sales` | invoices (draft→post→cancel), FEFO billing, returns, print payload |
| `Payments` | receipts and payments, open bills, invoice-wise allocation, cancellation |
| `Reports` | current/low/out-of-stock, near-expiry, expired, category & company stock, valuation, sales, purchase, profit, product profit, GST |
| `Dashboard` | all six blocks in one call |

---

## 2. The rule everything obeys

**Stock only moves through `inv.usp_PostStockTransaction`.** Every service goes via `IStockPostingService`, which validates the movement, updates the batch balance and appends the journal row in one transaction.

Every posting path runs inside `IUnitOfWork.ExecuteInTransactionAsync`, so an invoice writes its header, its lines and all its stock movements as a unit — or none of it. That is the difference between a shop whose stock matches its bills and one that quietly stops adding up.

Verified by check 49: after a full cycle of purchase, sale, two returns, an adjustment and two cancellations, every batch balance still equals the sum of its journal rows. Zero drift.

---

## 3. Decisions worth knowing

### 3.1 FEFO splits one requested line into several invoice lines

Ask for 60 units and the server picks batches by **earliest expiry first**, producing one invoice line per batch. That is deliberate: an invoice line must name exactly one batch and one expiry, both for the printed bill and for honest costing.

Plain FIFO would quietly age dated stock into a write-off. Expired batches are never picked automatically — selling an expired pesticide is a licensing problem, not a bookkeeping one.

### 3.2 Landed cost, not invoice rate

Purchase posting spreads freight and other charges across lines **in proportion to taxable value**, then divides by the total quantity **including free goods**:

```
LandedRate = (LineTaxable + FreightShare) ÷ (Quantity + FreeQuantity)
```

Both halves matter. Ignoring freight understates cost on every bulky consignment; dividing by paid quantity alone ignores that a "10 + 1 free" scheme is exactly what makes the deal profitable. GST is excluded — it is input credit, not cost.

Verified: 100 @ 400 and 50 @ 420 with ₹1,000 freight gives 406.5574 and 426.8852.

### 3.3 Cost is frozen onto the sale line

`SalesDetail.CostRate` is the landed rate of the batch that left the shelf, captured at the moment of sale. Deriving profit later from the product's *current* purchase rate would silently restate last year's profit every time a new consignment arrives at a different price.

Verified: selling 50 units from one batch and 10 from another produces a cost of ₹25,409.83 and a profit of ₹4,590.17, computed per batch.

### 3.4 CGST and SGST are not rounded independently

Total tax is rounded once, CGST takes half of that rounded figure, and SGST takes the remainder. Rounding both halves separately can leave CGST + SGST one paisa away from the total — and that single paisa is what makes a GSTR-1 filing fail reconciliation.

Rounding is `MidpointRounding.AwayFromZero` throughout, not .NET's banker's default, because 0.125 → 0.12 is not what a shop or a tax officer expects when checking by hand.

### 3.5 Cancellation reverses; it never deletes

Cancelling a posted document appends reversing journal rows linked by `ReversesTransactionId`. The goods may already have been sold on, and the journal has to keep showing they arrived before it shows them going back. Calling cancel twice is a no-op — already-reversed rows are skipped.

Cancelling a receipt reopens every bill it settled. That is the bounced-cheque path, and without it invoices would look paid while the money never arrived.

### 3.6 Unsaleable returns credit the customer without restocking

A sales return line carries `IsSaleable`. When false — expired or damaged goods — the credit note is raised and the customer refunded, but nothing goes back into stock. Damaged product must not become sellable again by being handed over the counter.

### 3.7 Draft versus posted

Purchases, invoices, adjustments and transfers are all created as **drafts** that touch no stock. A physical count is entered over hours and must not move stock until someone signs it off; a half-keyed consignment can be left and finished later. Opening stock is the exception — it posts immediately, since there is nothing to review it against.

### 3.8 Profit is hidden from users without permission

`SaleDto.TotalCostAmount`, `GrossProfit` and every line's `CostRate` are stripped for anyone lacking `Report.Profit`. The dashboard does the same with `Dashboard.ViewProfit`. A salesman can raise, collect on and reprint a bill without seeing the shop's margin.

---

## 4. Verification

Four suites, all green:

```
Masters API HTTP        32 passed, 0 failed   tests/api/api-smoke.ps1
Transactions API HTTP   49 passed, 0 failed   tests/api/transactions-smoke.ps1
Persistence (EF model)   8 passed, 0 failed   dotnet test tests/AgriERP.Persistence.Tests
SQL smoke               10 passed, 0 failed   database/tests/smoke_test.sql
```

`transactions-smoke.ps1` drives a complete trading cycle over real HTTP and purges everything afterwards. Selected checks:

```
PASS  5. Landed cost spreads freight by value (406.5574 / 426.8852)
PASS  6. Draft purchase moved no stock
PASS 10. Duplicate supplier bill refused (409)
PASS 13. Earliest-expiring batch consumed first (ZZB-EARLY, 50)
PASS 16. Cost frozen from each batch (25,409.83)
PASS 17. Gross profit 4,590.17 (30,000 - 25,409.83)
PASS 21. Overselling refused (422 with shortfall)
PASS 23. Credit beyond limit refused (422)
PASS 26. Invoice balance drops to 15,400.00, status Partial
PASS 32. Unsaleable return credits the customer but does NOT restock
PASS 39. GST report: input credit 10,980 on 61,000 taxable
PASS 47. Cancelled receipt reopens the invoice to 35,400.00
PASS 48. Cancellation APPENDS reversing rows rather than deleting
PASS 49. Batch balances still reconcile to the journal (zero drift)
```

---

## 5. Problems this step surfaced

**A static method inside a LINQ `Select()` is client-evaluated.** `MapPayment(p)` and two `MapReturn(r)` helpers looked like ordinary projections but EF could not translate them, so it materialised the entity — with every navigation null, because nothing `Include`d them — and ran the method on the client. Result: `NullReferenceException` on `p.PaymentMode`, at runtime, on three different endpoints. All three are now `static readonly Expression<Func<T, TDto>>` fields, which EF folds into the SQL SELECT.

**`EF.Functions.DateDiffDay` lives in the SQL Server provider**, which the Application layer deliberately does not reference. Ageing is now computed after materialisation, keeping the layer provider-agnostic.

**The test's own cleanup missed `ProductPriceHistory`**, whose foreign key silently blocked the product delete — and the script was swallowing sqlcmd's exit code, so the failure was invisible until the next run died on a duplicate. Both purges are now one shared ordered script that fails loudly.

---

## 6. Deliberately not built

- **Excel and PDF export.** The report endpoints return JSON. Rendering is a frontend concern for step 5, and server-side generation would mean a reporting library choice better made once the screens exist.
- **Allocating payments against credit and debit notes.** `fin.PaymentAllocations` supports all four reference types; the service handles Sale and Purchase. Returns currently settle by adjusting the party ledger, which is how a village shop actually works.
- **Weighted-average costing.** `app.AppSettings` carries `Purchase.CostingMethod`; batch costing is what is implemented, and it is the more accurate of the two.

---

## 7. Next: Step 5 — Frontend

Next.js 15, TypeScript, Tailwind, ShadCN UI, React Hook Form + Zod, TanStack Query, Axios. Sidebar and navbar, dark mode, data tables with the server-side paging/search/sort already in place, dashboard charts, and the CRUD and billing screens over these 80 endpoints.
