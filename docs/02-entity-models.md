# AgriERP — Entity Models (Step 3)

Domain entities, EF Core configurations and DbContext, mapped to the schema built in step 2.

---

## 1. Solution layout

```
AgriERP.sln                     .NET 8 (pinned via global.json)
├── src/
│   ├── AgriERP.Domain          entities, enums, read models   — references nothing
│   ├── AgriERP.Shared          cross-cutting primitives
│   ├── AgriERP.Application     → Domain, Shared
│   ├── AgriERP.Persistence     → Application   (EF Core, DbContext, configurations)
│   ├── AgriERP.Infrastructure  → Application   (files, email, external services)
│   └── AgriERP.API             → Persistence, Infrastructure
└── tests/
    └── AgriERP.Persistence.Tests
```

Dependencies point inward. `AgriERP.Domain` has **no package references at all** — the entities are plain POCOs with no EF attributes, so the domain model is not hostage to the ORM.

---

## 2. The schema is the source of truth — not migrations

The EF model is configured to *match* `database/scripts/`, not to generate it. Schema changes go through a new numbered SQL script, never `dotnet ef migrations add`.

The schema carries things EF migrations model poorly or not at all:

- 39 persisted computed columns holding the money arithmetic
- 66 CHECK constraints (`CK_Sales_CreditNeedsCustomer`, `CK_Purchases_TaxMode`, …)
- filtered unique indexes
- 6 stored procedures

Running migrations alongside hand-written scripts means two tools each believing they own the schema, and the first `Update-Database` against a live shop would try to "fix" everything it did not author.

The safety net is `tests/AgriERP.Persistence.Tests`, which queries every mapped table and view against real SQL Server. Drift fails a test run rather than surfacing at the billing counter.

---

## 3. Four mapping decisions worth knowing

### 3.1 No global query filters on soft delete

The obvious move is `HasQueryFilter(x => !x.IsDeleted)` on every master. It is the wrong move here.

A filter on `Product.IsDeleted` makes `Include(x => x.Product)` return **null** for a sale line whose product was later deleted — silently breaking the reprint of a two-year-old invoice. Historical documents must always resolve their masters.

So lists and lookups filter explicitly in the repositories, and document reads see everything.

### 3.2 Enums are stored as strings

`SaleType`, `DocumentStatus`, `PartyType` and the rest map with `HasConversion<string>()`:

- The database stays readable. `WHERE Status = 'Posted'` works from SSMS without a lookup table in your head — which matters when an accountant queries the data directly.
- The CHECK constraints already spell out the legal values. Storing integers would put the enum's meaning in C# only, and the database could no longer defend itself.

Consequence: **renaming an enum member is a breaking change.** Update the matching CHECK constraint in the same commit.

### 3.3 Computed columns have private setters

Every persisted computed column (`GrandTotal`, `BalanceAmount`, `LineProfit`, `CurrentQty`, …) is `{ get; private set; }` in C# and `HasComputedColumnSql(..., stored: true)` in the configuration.

The application writes quantities, rates, discounts and tax amounts. It never writes a total. A total that disagrees with its own lines is not expressible — not in SQL, and now not in C# either.

`Computed_columns_are_never_written_by_EF` asserts all 39 are still store-generated.

### 3.4 Decimal precision is explicit everywhere

EF's default for `decimal` is `DECIMAL(18,2)`. That silently rounds a purchase rate of 380.4550 to 380.46 on every consignment, and the error compounds through valuation into profit.

Four shapes, matching the DDL:

| Shape | Type | Used for |
|-------|------|----------|
| `AsQuantity()` | `DECIMAL(18,3)` | quantities — seeds sell in fractions of a kilo |
| `AsRate()` | `DECIMAL(18,4)` | unit rates |
| `AsAmount()` | `DECIMAL(18,2)` | money totals |
| `AsPercent()` | `DECIMAL(6,3)` | GST and discount percentages |

`Decimal_precision_matches_the_schema` fails if any decimal property slips below these.

---

## 4. Read models

Eight reporting views are mapped `HasNoKey().ToView(...)` as read models in `Domain/ReadModels/`:

`ProductStockView` · `BatchStockView` · `StockLedgerView` · `CustomerOutstandingView` · `SupplierOutstandingView` · `CategoryWiseStockView` · `DailySalesSummaryView` · `DailyPurchaseSummaryView`

They live in Domain rather than Persistence because the Application layer builds DTOs from them, and Application cannot reference Persistence without inverting the dependency direction.

Keyless types are not change-tracked, so they are both cheaper and structurally impossible to write through.

---

## 5. Verification

```powershell
dotnet test tests/AgriERP.Persistence.Tests
```

| Test | Asserts |
|------|---------|
| `Every_mapped_type_can_be_queried_against_the_real_database` | all 54 mapped types (46 tables + 8 views) query cleanly |
| `Computed_columns_are_never_written_by_EF` | all 39 computed columns are store-generated |
| `RowVersion_columns_are_concurrency_tokens` | optimistic concurrency is wired everywhere |
| `Decimal_precision_matches_the_schema` | no decimal fell back to the EF default |
| `Enums_are_stored_as_strings` | enum conversions match the CHECK constraints |
| `Reference_data_is_seeded` | 37 states, 5 GST slabs, 16 categories, 4 roles, active financial year |
| `Product_and_batch_round_trip_through_EF` | write + read + view roll-up, with cleanup |
| `Reporting_views_are_keyless_and_read_only` | views cannot be written through |

These are **integration tests against the real database**, deliberately. An in-memory or SQLite provider would happily accept a misnamed column, a wrong precision, or a computed column EF thinks it can write. Only real SQL Server can refute those.

---

## 6. Two bugs this step surfaced

**`vw_CategoryWiseStock` and `vw_CompanyWiseStock` returned NULL** for categories and manufacturers with no products — `SUM()` over a `LEFT JOIN` with no matching rows. A stock report showing NULL instead of 0.00 for an empty category is wrong on its own terms; it also failed to materialise into the non-nullable decimals on the read model. Both views now wrap those aggregates in `ISNULL(..., 0)`.

**`TransactionType.TransactionTypeId` was a raw `byte`** while the foreign key on `StockTransaction` was the `StockTransactionTypeId` enum. EF requires a foreign key and the principal key it targets to share a CLR type. The lookup's key is now the enum — which reads better anyway, since the lookup *is* the enum.

---

## 7. Entity inventory

| Namespace | Entities |
|-----------|---------|
| `Entities.Security` | Role, Permission, RolePermission, User, UserRefreshToken, UserPasswordReset |
| `Entities.Masters` | State, Unit, GstSlab, HsnCode, StorageLocation, Category, Company, Supplier, Customer |
| `Entities.Products` | Product, ProductBatch, ProductImage, ProductPriceHistory |
| `Entities.Inventory` | TransactionType, StockTransaction, StockAdjustment(+Detail), StockTransfer(+Detail) |
| `Entities.Purchases` | PurchaseOrder(+Detail), Purchase(+Detail), PurchaseReturn(+Detail) |
| `Entities.Sales` | Sale, SalesDetail, SalePayment, SalesReturn(+Detail) |
| `Entities.Finance` | PaymentMode, Payment, PaymentAllocation, ExpenseCategory, Expense |
| `Entities.System` | CompanyProfile, FinancialYear, NumberSeries, AppSetting, AuditLog |

Base types in `Domain/Common/`:

- `AuditableEntity` — CreatedAt/By, UpdatedAt/By
- `MasterEntity` — auditable + `IsActive` + `IsDeleted` + `RowVersion`
- `DocumentEntity` — auditable + `RowVersion` + `FinancialYearId`, **not** soft-deletable: a posted document is cancelled by status and reversed in the stock journal, never deleted

---

## 8. Next: Step 4 — Backend APIs

Not yet built, in dependency order:

1. `AgriERP.Application` — DTOs, AutoMapper profiles, FluentValidation validators, repository and service interfaces, paged-result primitives
2. `AgriERP.Persistence` — generic repository + unit of work, `usp_GetNextDocumentNumber` and `usp_PostStockTransaction` wrappers, audit-log `SaveChanges` interceptor
3. `AgriERP.Infrastructure` — BCrypt password hashing, JWT issuing/refresh, file storage for product images
4. `AgriERP.API` — controllers, exception middleware, Serilog, Swagger, permission-based authorisation, pagination/filtering/sorting/search

The admin password hash (`!SEED-PENDING!` in the database today) gets set by the API seeder in this step: default `Admin@123`, with `MustChangePassword` forcing a change at first login.
