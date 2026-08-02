# AgriERP — Database

SQL Server schema for the Agriculture Shop ERP. Step 1 (design) and step 2 (scripts) of the build plan.

## Running the scripts

Run in numeric order. Every script is idempotent — re-running against an existing database is safe and makes no changes.

```powershell
$srv = 'DESKTOP-L96U5S2\MSSQLSERVER03'
$dir = 'd:\Agriculture shop\database\scripts'

Get-ChildItem $dir -Filter '*.sql' | Sort-Object Name | ForEach-Object {
    sqlcmd -S $srv -U Indus -P '<password>' -C -b -i $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "Failed on $($_.Name)" }
}
```

Then verify:

```powershell
sqlcmd -S $srv -U Indus -P '<password>' -C -d AgriERP -i 'd:\Agriculture shop\database\tests\smoke_test.sql'
```

Expected last line: `RESULT: all 10 checks passed.`

## Script order

| # | Script | Creates |
|---|--------|---------|
| 00 | `00_CreateDatabase.sql` | Database, RCSI, recovery model |
| 01 | `01_Schemas.sql` | `sec` `mst` `inv` `pur` `sal` `fin` `app` |
| 02 | `02_Security.sql` | Roles, permissions, users, refresh tokens |
| 03 | `03_Masters.sql` | States, units, GST slabs, HSN, categories, companies, suppliers, customers, locations |
| 04 | `04_Products.sql` | Products, batches, images, price history |
| 05 | `05_Inventory.sql` | Stock journal, adjustments, transfers |
| 06 | `06_Purchase.sql` | Purchase orders, purchases, purchase returns |
| 07 | `07_Sales.sql` | Invoices, invoice payment splits, sales returns |
| 08 | `08_Finance.sql` | Payment modes, payments, allocations, expenses |
| 09 | `09_System.sql` | Shop profile, financial years, numbering, settings, audit |
| 10 | `10_Views.sql` | 15 reporting views |
| 11 | `11_StoredProcedures.sql` | 6 procedures |
| 12 | `12_SeedData.sql` | Reference data |

## What got built

| Object | Count |
|--------|------:|
| Tables | 46 |
| Views | 15 |
| Stored procedures | 6 |
| Foreign keys | 84 |
| Check constraints | 66 |
| Persisted computed columns | 39 |
| Non-clustered indexes | 193 |

## Two rules the schema enforces

**Stock only moves through `inv.usp_PostStockTransaction`.** It validates, updates the batch, and appends the journal row in one transaction. Nothing else should write to `inv.StockTransactions` or touch `InwardQty` / `OutwardQty`. `inv.usp_RebuildBatchQuantities @ReportOnly = 1` rebuilds the totals from the journal and reports any disagreement — if it ever finds one, something bypassed the procedure.

**Money arithmetic belongs to the database.** Line totals, invoice totals, balances and profit are persisted computed columns. The API writes quantities, rates, discounts and tax amounts; it never writes a total. A total that disagrees with its own lines is not expressible.

## Things to do before go-live

- **Switch to FULL recovery** and schedule log backups. `00_CreateDatabase.sql` sets SIMPLE, which is fine for development but means a crash loses everything since the last full backup.
- **Set the real admin password.** `sec.Users.admin` is seeded with the sentinel hash `!SEED-PENDING!`, which no BCrypt check can match. The API seeder (step 4) writes the real hash.
- **Fill `app.CompanyProfile`** with the shop's GST number, address and dealer licence numbers. These print on every invoice.
- **Confirm HSN codes with your CA.** `12_SeedData.sql` seeds the common agri-input classification, but HSN classification is the shop's legal responsibility.
- **Move the connection string out of source control.** It currently contains a plaintext password. Use .NET user secrets in development and environment variables in production:
  ```
  dotnet user-secrets set "ConnectionStrings:AgriERP" "Server=...;User ID=...;Password=..."
  ```

## Seeded reference data

States (37 with GST codes) · Units (14) · GST slabs (0/5/12/18/28%) · HSN codes (12 agri-input codes) · Categories (12 parents + 4 seed sub-categories) · Manufacturers (19 names; statutory fields left blank deliberately) · Storage locations (Main Shop, Godown) · Payment modes (6) · Expense categories (10) · Roles (4) · Permissions (70) · Financial years (previous/current/next, derived from today) · Number series (13 document types) · App settings (20)

No suppliers, customers, products or opening stock are seeded — that is shop data, and it arrives through the bulk-import screen in step 5.
