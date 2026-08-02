# AgriERP — Backend APIs (Step 4a)

Foundation, authentication and all master modules. Transactions (stock, purchase, sales, reports, dashboard) follow in 4b.

---

## 1. What runs today

```powershell
dotnet run --project Backend/AgriERP.API --urls http://localhost:5215
# Swagger: http://localhost:5215/swagger
# Health:  http://localhost:5215/health
```

First sign-in: **`admin` / `Admin@123`**, and the API forces a password change before anything else is usable.

**36 endpoints across 54 operations**, in 8 controllers:

| Controller | Covers |
|-----------|--------|
| `Auth` | login, refresh, logout, me, change/forgot/reset password |
| `Categories` | CRUD + lookup, one-level hierarchy |
| `Companies` | CRUD + lookup (manufacturers) |
| `Suppliers` | CRUD + lookup + outstanding |
| `Customers` | CRUD + lookup + by-mobile + villages + outstanding |
| `Units` | CRUD + lookup |
| `Products` | CRUD + billing search + barcode scan + batches |
| `Lookups` | states, GST slabs, HSN codes, locations, product-form bundle |

Every list endpoint supports **paging, free-text search, sorting and filters**.

---

## 2. Layering

```
API            controllers, middleware, filters, authorization, seeder
  ↓
Persistence    DbContext, repositories, unit of work, interceptor, numbering
  ↓
Infrastructure BCrypt, JWT, clock
  ↓
Application    DTOs, services, validators, AutoMapper profiles, interfaces
  ↓
Domain         entities, enums, read models  — zero package references
```

Services throw `NotFoundException` / `ValidationException` / `ConflictException` / `BusinessRuleException`; the API's middleware maps each to a status code. No service references `ActionResult` or `HttpContext`, so the Application layer is testable without a web host.

---

## 3. Decisions worth knowing

### 3.1 Permission-based authorization, not role checks

`[HasPermission(Permissions.Product.Create)]`. Roles change as a shop grows ("Manager can now cancel bills") and role checks scattered across controllers have to be hunted down every time. A permission code is a stable contract; which roles hold it is a row in `sec.RolePermissions`.

Permissions ride in the JWT so authorization costs no database round trip. The trade-off is explicit: **a permission revoked mid-session stays effective until the access token expires** — which is why the access token is short-lived (60 min) and a password or role change rotates the security stamp.

A `PermissionPolicyProvider` builds policies on demand, so adding an endpoint never means registering another policy in `Program.cs`.

### 3.2 Refresh-token rotation with reuse detection

Every refresh issues a new token and revokes the old one. Presenting an **already-rotated** token means it was captured — the legitimate holder and the attacker both have it, and there is no way to tell which is calling. The whole chain is revoked and both must sign in again.

Tokens are stored as SHA-256 hashes. A leaked database backup must not hand an attacker live sessions.

### 3.3 Login does not leak which usernames exist

Wrong password and unknown username both return the same message, and an unknown username still burns a BCrypt verification against a dummy hash so the two paths take the same time. `forgot-password` always reports success for the same reason — a village shop's usernames are guessable.

### 3.4 Deleting a master in use is blocked, not cascaded

Soft-deleting a category silently strips the classification from every product under it, and category-wise stock reports quietly stop adding up. So the services refuse and say what is in the way:

> `'Insecticide' is used by 42 product(s). Reassign or remove them first, or set the category inactive instead.`

Same for companies with products, suppliers with bills, customers with invoices (which also reports the outstanding amount), units used as either selling or packing unit, and products still holding stock.

### 3.5 Duplicate checks in services **and** unique indexes in the database

The service check exists to give a field-level message instead of a raw index violation. The **index remains the real guarantee** — the service check cannot close the race between two simultaneous saves.

### 3.6 `ProjectTo`, not `Map`, for lists

AutoMapper's `ProjectTo` rewrites the projection into the SQL SELECT list, so only the DTO's columns leave the database. Mapping after materialisation would pull every column of every entity — including the 1000-character product description — to render a grid showing six fields.

The **product list is hand-projected** instead: it joins `mst.vw_ProductStock` for the rolled-up stock figures, and a join projection is clearer written out than bent through AutoMapper.

### 3.7 `pageSize` is clamped, not trusted

Ceiling of 200. Without it, `?pageSize=100000` against the product list is an accidental denial-of-service on a shop running SQL Server Express on the counter PC.

### 3.8 Security config has one home

Token lifetimes, lockout thresholds and the signing key all live in `appsettings.json`. The `Auth.*` rows were **removed** from `app.AppSettings` in `12_SeedData.sql` — two places to set a lockout threshold means one of them is eventually wrong and nobody knows which is in force. `app.AppSettings` keeps business tunables (max discount, expiry warning days, negative stock).

---

## 4. Verification

Three suites, all green:

```
API HTTP smoke   32 passed, 0 failed   tests/api/api-smoke.ps1
Persistence       8 passed, 0 failed   dotnet test tests/AgriERP.Persistence.Tests
SQL smoke        10 passed, 0 failed   database/tests/smoke_test.sql
```

`tests/api/api-smoke.ps1` drives the running API over real HTTP — it logs in, exercises paging/search/sort/clamping, creates and updates records, asserts every guard rail fires, rotates refresh tokens, and purges everything it created. Selected checks:

```
PASS  1. Wrong password rejected (401, generic message)
PASS  4. Admin carries all 70 seeded permissions (70)
PASS  9. pageSize clamped to 200 (got 200)
PASS 12. FluentValidation reports every failing field at once
PASS 14. Credit limit without mobile rejected
PASS 20. Product auto-numbered (PRD-000001)
PASS 21. Rate precision preserved (380.4550)
PASS 23. Selling rate above MRP rejected
PASS 25. Barcode scan returns billing payload (rate + GST + stock)
PASS 29. Deleting an in-use category blocked (409)
PASS 31. Reused refresh token detected and session killed
```

---

## 5. Problems this step surfaced

**AutoMapper 13.0.1 carried CVE-2026-32933** — uncontrolled recursion in nested-object mapping that exhausts the stack and kills the whole process, not just the request. Upgraded to 15.1.3 (first patched line is 15.1.1). The 15.x DI signature changed, so `AddAutoMapper` now takes a configuration action.

**Document numbers read `PRD/000001`** where the design documented `PRD-000001`. The seed gave every series the `/` separator, but master codes carry no year segment, so `/` made it look like a year was missing. Master series (product, customer, supplier) now use `-`; documents keep `INV/2026-27/00042`.

**`RuleForEach` over an array expression threw at runtime.** FluentValidation cannot infer a property name from `new[] { x.PurchaseRate, x.SellingRate, ... }`, so every product save returned 500. Replaced with one rule per rate — which also puts each error on the right input in the form.

**`Include` after a projection is invalid in EF Core.** Product detail applied `.Include(x => x.p.PackingUnit)` after `select new { p, s }`. Now the three related values are projected as scalars, which is cheaper anyway than loading three whole entities to read one column from each.

**Two of my own tests were wrong, not the code**: a category count that did not exclude soft-deleted rows, and a PowerShell assertion that broke when a single-item array was unwrapped. Both fixed.

**`sqlcmd -Q` needs `-I`.** Ad-hoc DML against these tables fails with Msg 1934 unless `QUOTED_IDENTIFIER` is ON — the filtered indexes require it. EF Core sets it automatically, so only manual sqlcmd is affected. Worth remembering for hand-run cleanup queries.

Also corrected from step 2: the seed creates **70** permissions, not 72 as previously reported.

---

## 6. Next: Step 4b

- **Stock** — opening stock, adjustments, transfers, ledger, batch/expiry reports; wraps `inv.usp_PostStockTransaction`
- **Purchase** — orders, entry with batch creation, returns, landed-cost calculation, supplier dues
- **Sales** — billing with FEFO batch picking, GST split, multi-mode payment, returns, invoice print data
- **Reports + Dashboard** — `app.usp_DashboardSummary`, GST returns, profit, Excel/PDF export

Then step 5 (Next.js frontend), 6 (testing), 7 (deployment).
