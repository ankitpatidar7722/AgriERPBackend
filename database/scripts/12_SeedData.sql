/*==============================================================================
  AgriERP  |  12_SeedData.sql
  ------------------------------------------------------------------------------
  Reference data the application cannot start without, plus the manufacturer
  names you listed.

  Idempotent: every block is a MERGE or an IF NOT EXISTS, so re-running is safe.

  WHAT IS DELIBERATELY *NOT* SEEDED
  ---------------------------------
  Manufacturer GST numbers, addresses and phone numbers are left NULL. Inventing
  plausible-looking GSTINs would put fake statutory identifiers into a live
  billing system - they would reach printed invoices and GST returns. Names and
  codes only; fill the statutory fields from the real dealer paperwork.

  No suppliers, customers, products or opening stock are seeded either - that is
  your shop's data and it comes in through the bulk-import screen in step 5.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*==============================================================================
  States  - official GST state codes
==============================================================================*/
MERGE States AS tgt
USING (VALUES
    ( 1,'01',N'Jammu and Kashmir',N'JK',1), ( 2,'02',N'Himachal Pradesh',N'HP',0),
    ( 3,'03',N'Punjab',N'PB',0),            ( 4,'04',N'Chandigarh',N'CH',1),
    ( 5,'05',N'Uttarakhand',N'UK',0),       ( 6,'06',N'Haryana',N'HR',0),
    ( 7,'07',N'Delhi',N'DL',1),             ( 8,'08',N'Rajasthan',N'RJ',0),
    ( 9,'09',N'Uttar Pradesh',N'UP',0),     (10,'10',N'Bihar',N'BR',0),
    (11,'11',N'Sikkim',N'SK',0),            (12,'12',N'Arunachal Pradesh',N'AR',0),
    (13,'13',N'Nagaland',N'NL',0),          (14,'14',N'Manipur',N'MN',0),
    (15,'15',N'Mizoram',N'MZ',0),           (16,'16',N'Tripura',N'TR',0),
    (17,'17',N'Meghalaya',N'ML',0),         (18,'18',N'Assam',N'AS',0),
    (19,'19',N'West Bengal',N'WB',0),       (20,'20',N'Jharkhand',N'JH',0),
    (21,'21',N'Odisha',N'OD',0),            (22,'22',N'Chhattisgarh',N'CG',0),
    (23,'23',N'Madhya Pradesh',N'MP',0),    (24,'24',N'Gujarat',N'GJ',0),
    (26,'26',N'Dadra and Nagar Haveli and Daman and Diu',N'DD',1),
    (27,'27',N'Maharashtra',N'MH',0),       (29,'29',N'Karnataka',N'KA',0),
    (30,'30',N'Goa',N'GA',0),               (31,'31',N'Lakshadweep',N'LD',1),
    (32,'32',N'Kerala',N'KL',0),            (33,'33',N'Tamil Nadu',N'TN',0),
    (34,'34',N'Puducherry',N'PY',1),        (35,'35',N'Andaman and Nicobar Islands',N'AN',1),
    (36,'36',N'Telangana',N'TS',0),         (37,'37',N'Andhra Pradesh',N'AP',0),
    (38,'38',N'Ladakh',N'LA',1),            (97,'97',N'Other Territory',N'OT',1)
) AS src (StateId, StateCode, StateName, StateAbbr, IsUnionTerritory)
    ON tgt.StateId = src.StateId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (StateId, StateCode, StateName, StateAbbr, IsUnionTerritory)
    VALUES (src.StateId, src.StateCode, src.StateName, src.StateAbbr, src.IsUnionTerritory);
GO

/*==============================================================================
  Units
==============================================================================*/
MERGE Units AS tgt
USING (VALUES
    (N'NOS', N'Numbers',    1, 0),
    (N'PKT', N'Packet',     2, 0),
    (N'BTL', N'Bottle',     3, 0),
    (N'BAG', N'Bag',        4, 0),
    (N'BOX', N'Box',        5, 0),
    (N'KG',  N'Kilogram',   6, 1),
    (N'GM',  N'Gram',       7, 1),
    (N'LTR', N'Litre',      8, 1),
    (N'ML',  N'Millilitre', 9, 1),
    (N'QTL', N'Quintal',   10, 1),
    (N'TON', N'Tonne',     11, 1),
    (N'MTR', N'Metre',     12, 1),
    (N'SET', N'Set',       13, 0),
    (N'PAIR',N'Pair',      14, 0)
) AS src (UnitCode, UnitName, DisplayOrder, AllowDecimal)
    ON tgt.UnitCode = src.UnitCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (UnitCode, UnitName, DisplayOrder, AllowDecimal)
    VALUES (src.UnitCode, src.UnitName, src.DisplayOrder, src.AllowDecimal);
GO

/*==============================================================================
  GstSlabs
==============================================================================*/
MERGE GstSlabs AS tgt
USING (VALUES
    (N'GST 0%',   0.000, 0.000, 0.000,  0.000),   -- seeds for sowing
    (N'GST 5%',   5.000, 2.500, 2.500,  5.000),   -- fertilizers
    (N'GST 12%', 12.000, 6.000, 6.000, 12.000),   -- sprayers, hand tools
    (N'GST 18%', 18.000, 9.000, 9.000, 18.000),   -- pesticides, PGRs
    (N'GST 28%', 28.000,14.000,14.000, 28.000)
) AS src (SlabName, TotalRate, CgstRate, SgstRate, IgstRate)
    ON tgt.TotalRate = src.TotalRate
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SlabName, TotalRate, CgstRate, SgstRate, IgstRate)
    VALUES (src.SlabName, src.TotalRate, src.CgstRate, src.SgstRate, src.IgstRate);
GO

/*==============================================================================
  HsnCodes
  Rates below are the common classification for agri-inputs. Confirm against
  your CA's advice before the first GST return - HSN classification is the
  shop's legal responsibility, not the software's.
==============================================================================*/
MERGE HsnCodes AS tgt
USING (VALUES
    (N'1209', N'Seeds, fruit and spores of a kind used for sowing',              0.000),
    (N'3101', N'Animal or vegetable fertilisers; bio-fertilisers',                5.000),
    (N'3102', N'Mineral or chemical fertilisers, nitrogenous (Urea)',             5.000),
    (N'3103', N'Mineral or chemical fertilisers, phosphatic',                     5.000),
    (N'3104', N'Mineral or chemical fertilisers, potassic (MOP)',                 5.000),
    (N'3105', N'Fertilisers containing two or three nutrients (DAP, NPK)',        5.000),
    (N'3808', N'Insecticides, fungicides, herbicides, plant growth regulators',  18.000),
    (N'3824', N'Micronutrients and chemical preparations',                       18.000),
    (N'8201', N'Hand tools: spades, hoes, sickles, khurpi, pruners',             12.000),
    (N'8424', N'Mechanical appliances for spraying liquids or powders',          12.000),
    (N'3923', N'Plastic articles: mulching film, crates, tanks',                 18.000),
    (N'5608', N'Knotted netting: crop cover, shade net, bird net',               12.000)
) AS src (HsnCode, Description, GstRate)
    ON tgt.HsnCode = src.HsnCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (HsnCode, Description, DefaultGstSlabId)
    VALUES (src.HsnCode, src.Description,
            (SELECT GstSlabId FROM GstSlabs WHERE TotalRate = src.GstRate));
GO

/*==============================================================================
  ItemSubGroupMaster  - parents first, then the seed sub-categories
==============================================================================*/
MERGE ItemSubGroupMaster AS tgt
USING (VALUES
    (N'INSEC',  N'Insecticide',            N'Products controlling insect pests',            1),
    (N'PESTI',  N'Pesticide',              N'General pest control products',                2),
    (N'FUNGI',  N'Fungicide',              N'Products controlling fungal disease',          3),
    (N'HERBI',  N'Herbicide',              N'Weed control products',                        4),
    (N'FERT',   N'Fertilizer',             N'Chemical and mineral fertilizers',             5),
    (N'BIOFERT',N'Bio Fertilizer',         N'Microbial and organic fertilizers',            6),
    (N'MICRO',  N'Micronutrient',          N'Zinc, boron, iron and mixed micronutrients',   7),
    (N'PGR',    N'Plant Growth Regulator', N'Hormones and growth promoters',                8),
    (N'SEED',   N'Seeds',                  N'All sowing seed',                              9),
    (N'EQUIP',  N'Farming Equipment',      N'Sprayers, tools and implements',              10),
    (N'ACCES',  N'Agriculture Accessories',N'Nozzles, pipes, nets and sundries',           11),
    (N'OTHER',  N'Others',                 N'Uncategorised items',                         99)
) AS src (ItemSubGroupCode, ItemSubGroupName, Description, DisplayOrder)
    ON tgt.ItemSubGroupCode = src.ItemSubGroupCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ItemSubGroupCode, ItemSubGroupName, Description, DisplayOrder)
    VALUES (src.ItemSubGroupCode, src.ItemSubGroupName, src.Description, src.DisplayOrder);
GO

-- Seed sub-categories. Modelled as children of "Seeds" so the product screen
-- can offer one Seeds filter or four specific ones, without a second table.
DECLARE @SeedItemSubGroupId INT = (SELECT ItemSubGroupId FROM ItemSubGroupMaster WHERE ItemSubGroupCode = N'SEED');

MERGE ItemSubGroupMaster AS tgt
USING (VALUES
    (N'SEEDVEG',   N'Vegetable Seeds',  N'Tomato, chilli, onion, brinjal and similar',  1),
    (N'SEEDFIELD', N'Field Crop Seeds', N'Wheat, soybean, cotton, gram and similar',    2),
    (N'SEEDFLOW',  N'Flower Seeds',     N'Marigold, rose and ornamental seed',          3),
    (N'SEEDFRUIT', N'Fruit Seeds',      N'Papaya, watermelon and fruit seed',           4)
) AS src (ItemSubGroupCode, ItemSubGroupName, Description, DisplayOrder)
    ON tgt.ItemSubGroupCode = src.ItemSubGroupCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ItemSubGroupCode, ItemSubGroupName, Description, ParentItemSubGroupId, DisplayOrder)
    VALUES (src.ItemSubGroupCode, src.ItemSubGroupName, src.Description, @SeedItemSubGroupId, src.DisplayOrder);
GO

/*==============================================================================
  Companies  - manufacturer names only; statutory fields stay NULL
==============================================================================*/
MERGE Companies AS tgt
USING (VALUES
    (N'UPL',      N'UPL Limited'),
    (N'BAYER',    N'Bayer CropScience Limited'),
    (N'SYNGENTA', N'Syngenta India Limited'),
    (N'BASF',     N'BASF India Limited'),
    (N'CORO',     N'Coromandel International Limited'),
    (N'IFFCO',    N'Indian Farmers Fertiliser Cooperative Limited'),
    (N'RALLIS',   N'Rallis India Limited'),
    (N'DHANUKA',  N'Dhanuka Agritech Limited'),
    (N'PIIND',    N'PI Industries Limited'),
    (N'FMC',      N'FMC India Private Limited'),
    (N'ADAMA',    N'ADAMA India Private Limited'),
    (N'GSP',      N'GSP Crop Science Private Limited'),
    (N'INSECT',   N'Insecticides (India) Limited'),
    (N'KRIBHCO',  N'Krishak Bharati Cooperative Limited'),
    (N'NFL',      N'National Fertilizers Limited'),
    (N'MAHYCO',   N'Maharashtra Hybrid Seeds Company'),
    (N'NUZIVEEDU',N'Nuziveedu Seeds Limited'),
    (N'KALASH',   N'Kalash Seeds Private Limited'),
    (N'OTHER',    N'Other / Local')
) AS src (CompanyCode, CompanyName)
    ON tgt.CompanyCode = src.CompanyCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CompanyCode, CompanyName) VALUES (src.CompanyCode, src.CompanyName);
GO

/*==============================================================================
  StorageLocations
==============================================================================*/
IF NOT EXISTS (SELECT 1 FROM StorageLocations WHERE LocationCode = N'MAIN')
    INSERT INTO StorageLocations (LocationCode, LocationName, LocationType, IsDefault)
    VALUES (N'MAIN', N'Main Shop', N'Counter', 1);

IF NOT EXISTS (SELECT 1 FROM StorageLocations WHERE LocationCode = N'GODOWN')
    INSERT INTO StorageLocations (LocationCode, LocationName, LocationType, IsDefault)
    VALUES (N'GODOWN', N'Godown', N'Godown', 0);
GO

/*==============================================================================
  PaymentModes
==============================================================================*/
MERGE PaymentModes AS tgt
USING (VALUES
    (N'CASH',   N'Cash',          0, 0, 1),
    (N'UPI',    N'UPI',           1, 1, 2),
    (N'CARD',   N'Debit/Credit Card', 1, 1, 3),
    (N'CHEQUE', N'Cheque',        1, 1, 4),
    (N'NEFT',   N'NEFT / RTGS / IMPS', 1, 1, 5),
    (N'CREDIT', N'On Credit',     0, 0, 6)
) AS src (ModeCode, ModeName, RequiresReference, IsBankMode, DisplayOrder)
    ON tgt.ModeCode = src.ModeCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ModeCode, ModeName, RequiresReference, IsBankMode, DisplayOrder)
    VALUES (src.ModeCode, src.ModeName, src.RequiresReference, src.IsBankMode, src.DisplayOrder);
GO

/*==============================================================================
  ExpenseCategories
==============================================================================*/
MERGE ExpenseCategories AS tgt
USING (VALUES
    (N'RENT',      N'Shop Rent'),
    (N'SALARY',    N'Salary and Wages'),
    (N'ELECTRIC',  N'Electricity'),
    (N'TRANSPORT', N'Transport and Freight'),
    (N'LOADING',   N'Loading and Unloading'),
    (N'TELEPHONE', N'Telephone and Internet'),
    (N'STATIONERY',N'Printing and Stationery'),
    (N'REPAIR',    N'Repairs and Maintenance'),
    (N'LICENCE',   N'Licence and Statutory Fees'),
    (N'MISC',      N'Miscellaneous')
) AS src (CategoryCode, CategoryName)
    ON tgt.CategoryCode = src.CategoryCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CategoryCode, CategoryName) VALUES (src.CategoryCode, src.CategoryName);
GO

/*==============================================================================
  Roles
==============================================================================*/
MERGE Roles AS tgt
USING (VALUES
    (N'Administrator', N'Full access to every module and setting',              1),
    (N'Manager',       N'All operations and reports; no user administration',   1),
    (N'Salesman',      N'Billing, customers and sales reports',                 1),
    (N'StoreKeeper',   N'Stock, purchase and product maintenance',              1)
) AS src (RoleName, Description, IsSystemRole)
    ON tgt.RoleName = src.RoleName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (RoleName, Description, IsSystemRole)
    VALUES (src.RoleName, src.Description, src.IsSystemRole);
GO

/*==============================================================================
  Permissions
==============================================================================*/
MERGE Permissions AS tgt
USING (VALUES
    (N'Dashboard.View',      N'Dashboard', N'View dashboard',            1),
    (N'Dashboard.ViewProfit',N'Dashboard', N'View profit figures',       2),

    (N'Category.View',       N'Category',  N'View categories',          10),
    (N'Category.Create',     N'Category',  N'Create category',          11),
    (N'Category.Edit',       N'Category',  N'Edit category',            12),
    (N'Category.Delete',     N'Category',  N'Delete category',          13),

    (N'Company.View',        N'Company',   N'View companies',           20),
    (N'Company.Create',      N'Company',   N'Create company',           21),
    (N'Company.Edit',        N'Company',   N'Edit company',             22),
    (N'Company.Delete',      N'Company',   N'Delete company',           23),

    (N'Supplier.View',       N'Supplier',  N'View suppliers',           30),
    (N'Supplier.Create',     N'Supplier',  N'Create supplier',          31),
    (N'Supplier.Edit',       N'Supplier',  N'Edit supplier',            32),
    (N'Supplier.Delete',     N'Supplier',  N'Delete supplier',          33),

    (N'Customer.View',       N'Customer',  N'View customers',           40),
    (N'Customer.Create',     N'Customer',  N'Create customer',          41),
    (N'Customer.Edit',       N'Customer',  N'Edit customer',            42),
    (N'Customer.Delete',     N'Customer',  N'Delete customer',          43),

    (N'Product.View',        N'Product',   N'View products',            50),
    (N'Product.Create',      N'Product',   N'Create product',           51),
    (N'Product.Edit',        N'Product',   N'Edit product',             52),
    (N'Product.Delete',      N'Product',   N'Delete product',           53),
    (N'Product.Import',      N'Product',   N'Bulk import products',     54),
    (N'Product.Export',      N'Product',   N'Bulk export products',     55),
    (N'Product.EditRate',    N'Product',   N'Change product rates',     56),

    (N'Stock.View',          N'Stock',     N'View stock and ledger',    60),
    (N'Stock.Adjust',        N'Stock',     N'Create stock adjustment',  61),
    (N'Stock.Transfer',      N'Stock',     N'Create stock transfer',    62),
    (N'Stock.Post',          N'Stock',     N'Post stock documents',     63),
    (N'Stock.Opening',       N'Stock',     N'Enter opening stock',      64),

    (N'Purchase.View',       N'Purchase',  N'View purchases',           70),
    (N'Purchase.Create',     N'Purchase',  N'Create purchase entry',    71),
    (N'Purchase.Edit',       N'Purchase',  N'Edit draft purchase',      72),
    (N'Purchase.Post',       N'Purchase',  N'Post purchase to stock',   73),
    (N'Purchase.Cancel',     N'Purchase',  N'Cancel posted purchase',   74),
    (N'Purchase.Return',     N'Purchase',  N'Create purchase return',   75),
    (N'Purchase.Order',      N'Purchase',  N'Manage purchase orders',   76),

    (N'Sales.View',          N'Sales',     N'View invoices',            80),
    (N'Sales.Create',        N'Sales',     N'Create invoice',           81),
    (N'Sales.Edit',          N'Sales',     N'Edit draft invoice',       82),
    (N'Sales.Post',          N'Sales',     N'Post invoice',             83),
    (N'Sales.Cancel',        N'Sales',     N'Cancel posted invoice',    84),
    (N'Sales.Return',        N'Sales',     N'Create sales return',      85),
    (N'Sales.Print',         N'Sales',     N'Print invoice',            86),
    (N'Sales.Discount',      N'Sales',     N'Apply discount',           87),
    (N'Sales.OverrideMinRate',N'Sales',    N'Sell below minimum rate',  88),
    (N'Sales.CreditSale',    N'Sales',     N'Sell on credit',           89),

    (N'Payment.View',        N'Payment',   N'View payments',            90),
    (N'Payment.Create',      N'Payment',   N'Record payment or receipt',91),
    (N'Payment.Cancel',      N'Payment',   N'Cancel payment',           92),
    (N'Expense.View',        N'Payment',   N'View expenses',            93),
    (N'Expense.Create',      N'Payment',   N'Record expense',           94),

    (N'Report.Sales',        N'Report',    N'Sales reports',           100),
    (N'Report.Purchase',     N'Report',    N'Purchase reports',        101),
    (N'Report.Stock',        N'Report',    N'Stock reports',           102),
    (N'Report.Profit',       N'Report',    N'Profit reports',          103),
    (N'Report.Gst',          N'Report',    N'GST reports',             104),
    (N'Report.Party',        N'Report',    N'Customer and supplier reports', 105),
    (N'Report.Export',       N'Report',    N'Export reports',          106),

    (N'User.View',           N'User',      N'View users',              110),
    (N'User.Create',         N'User',      N'Create user',             111),
    (N'User.Edit',           N'User',      N'Edit user',               112),
    (N'User.Delete',         N'User',      N'Delete user',             113),
    (N'User.ResetPassword',  N'User',      N'Reset another user password', 114),
    (N'Role.View',           N'User',      N'View roles',              115),
    (N'Role.Manage',         N'User',      N'Create and edit roles',   116),

    (N'Settings.View',       N'Settings',  N'View settings',           120),
    (N'Settings.Edit',       N'Settings',  N'Change settings',         121),
    (N'Settings.Audit',      N'Settings',  N'View audit log',          122),
    (N'Settings.YearEnd',    N'Settings',  N'Close financial year',    123)
) AS src (PermissionCode, Module, DisplayName, DisplayOrder)
    ON tgt.PermissionCode = src.PermissionCode
WHEN MATCHED THEN UPDATE SET
    tgt.Module = src.Module, tgt.DisplayName = src.DisplayName, tgt.DisplayOrder = src.DisplayOrder
WHEN NOT MATCHED BY TARGET THEN
    INSERT (PermissionCode, Module, DisplayName, DisplayOrder)
    VALUES (src.PermissionCode, src.Module, src.DisplayName, src.DisplayOrder);
GO

/*==============================================================================
  RolePermissions
==============================================================================*/
DECLARE @Admin INT = (SELECT RoleId FROM Roles WHERE RoleName = N'Administrator');
DECLARE @Mgr   INT = (SELECT RoleId FROM Roles WHERE RoleName = N'Manager');
DECLARE @Sales INT = (SELECT RoleId FROM Roles WHERE RoleName = N'Salesman');
DECLARE @Store INT = (SELECT RoleId FROM Roles WHERE RoleName = N'StoreKeeper');

-- Administrator: everything.
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT @Admin, p.PermissionId
FROM Permissions AS p
WHERE NOT EXISTS (SELECT 1 FROM RolePermissions AS rp
                  WHERE rp.RoleId = @Admin AND rp.PermissionId = p.PermissionId);

-- Manager: everything operational. Excluded from user administration and from
-- year-end close, so one person cannot both create a login and hide its trail.
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT @Mgr, p.PermissionId
FROM Permissions AS p
WHERE p.Module <> N'User'
  AND p.PermissionCode NOT IN (N'Settings.YearEnd')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions AS rp
                  WHERE rp.RoleId = @Mgr AND rp.PermissionId = p.PermissionId);

-- Salesman: bill, collect, and look things up. Cannot cancel a posted invoice,
-- cannot sell below the minimum rate, cannot see profit figures.
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT @Sales, p.PermissionId
FROM Permissions AS p
WHERE p.PermissionCode IN (
        N'Dashboard.View',
        N'Product.View', N'Stock.View',
        N'Customer.View', N'Customer.Create', N'Customer.Edit',
        N'Sales.View', N'Sales.Create', N'Sales.Post', N'Sales.Print',
        N'Sales.Discount', N'Sales.Return',
        N'Payment.View', N'Payment.Create',
        N'Report.Sales', N'Report.Party')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions AS rp
                  WHERE rp.RoleId = @Sales AND rp.PermissionId = p.PermissionId);

-- StoreKeeper: goods in, goods counted, goods moved. No billing, no money.
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT @Store, p.PermissionId
FROM Permissions AS p
WHERE p.PermissionCode IN (
        N'Dashboard.View',
        N'Category.View', N'Company.View', N'Supplier.View',
        N'Product.View', N'Product.Create', N'Product.Edit',
        N'Product.Import', N'Product.Export',
        N'Stock.View', N'Stock.Adjust', N'Stock.Transfer', N'Stock.Post', N'Stock.Opening',
        N'Purchase.View', N'Purchase.Create', N'Purchase.Edit',
        N'Purchase.Post', N'Purchase.Return', N'Purchase.Order',
        N'Report.Stock', N'Report.Purchase')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions AS rp
                  WHERE rp.RoleId = @Store AND rp.PermissionId = p.PermissionId);
GO

/*==============================================================================
  Users  - the bootstrap administrator

  PasswordHash is a sentinel that no BCrypt verification can ever match, so
  this row cannot be logged into as it stands. The API's seeder (step 4) writes
  the real BCrypt hash on first run - default password Admin@123, with
  MustChangePassword forcing a change at first login.

  A plaintext or fabricated hash here would be a live credential sitting in a
  file that ends up in source control.
==============================================================================*/
IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = N'admin')
BEGIN
    INSERT INTO Users (UserName, Email, PasswordHash, FullName, RoleId, MustChangePassword, IsActive)
    SELECT N'admin', NULL, N'!SEED-PENDING!', N'System Administrator',
           (SELECT RoleId FROM Roles WHERE RoleName = N'Administrator'), 1, 1;
END
GO

/*==============================================================================
  FinancialYears  - derived from today, so the script is never stale.
  Indian financial year runs 1 April to 31 March.
==============================================================================*/
DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @FyStartYear INT = CASE WHEN MONTH(@Today) >= 4 THEN YEAR(@Today) ELSE YEAR(@Today) - 1 END;

DECLARE @Years TABLE (StartYear INT);
INSERT INTO @Years (StartYear) VALUES (@FyStartYear - 1), (@FyStartYear), (@FyStartYear + 1);

MERGE FinancialYears AS tgt
USING (
    SELECT CAST(StartYear AS NVARCHAR(4)) + N'-' + RIGHT(CAST(StartYear + 1 AS NVARCHAR(4)), 2) AS YearCode,
           DATEFROMPARTS(StartYear,     4, 1)  AS StartDate,
           DATEFROMPARTS(StartYear + 1, 3, 31) AS EndDate,
           CASE WHEN StartYear = @FyStartYear THEN 1 ELSE 0 END AS IsActive
    FROM @Years
) AS src
    ON tgt.YearCode = src.YearCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (YearCode, StartDate, EndDate, IsActive)
    VALUES (src.YearCode, src.StartDate, src.EndDate, src.IsActive);
GO

/*==============================================================================
  NumberSeries  - one counter per document type for the active year
==============================================================================*/
DECLARE @ActiveFy INT = (SELECT FinancialYearId FROM FinancialYears WHERE IsActive = 1);

MERGE NumberSeries AS tgt
USING (VALUES
    (N'Sale',            N'INV', 5),
    (N'SalesReturn',     N'SR',  5),
    (N'Purchase',        N'PUR', 5),
    (N'PurchaseReturn',  N'PR',  5),
    (N'PurchaseOrder',   N'PO',  5),
    (N'StockAdjustment', N'ADJ', 5),
    (N'StockTransfer',   N'TRF', 5),
    (N'Receipt',         N'RCT', 5),
    (N'Payment',         N'PMT', 5),
    (N'Expense',         N'EXP', 5),
    (N'Product',         N'PRD', 6),
    (N'Customer',        N'CUS', 5),
    (N'Supplier',        N'SUP', 5)
) AS src (DocumentType, Prefix, PaddingLength)
    ON tgt.DocumentType = src.DocumentType AND tgt.FinancialYearId = @ActiveFy
WHEN NOT MATCHED BY TARGET THEN
    INSERT (DocumentType, FinancialYearId, Prefix, PaddingLength, IncludeYearCode, Separator)
    -- Documents carry the financial year and read INV/2026-27/00042.
    -- Master codes do NOT: a product created last year keeps its code forever,
    -- so they read PRD-000001 with a hyphen. Using '/' for a code that has no
    -- year segment produces the misleading "PRD/000001", which looks like a
    -- year is missing.
    VALUES (src.DocumentType, @ActiveFy, src.Prefix, src.PaddingLength,
            CASE WHEN src.DocumentType IN (N'Product', N'Customer', N'Supplier') THEN 0 ELSE 1 END,
            CASE WHEN src.DocumentType IN (N'Product', N'Customer', N'Supplier') THEN N'-' ELSE N'/' END);
GO

/*==============================================================================
  CompanyProfile  - placeholder; fill from the real shop paperwork
==============================================================================*/
IF NOT EXISTS (SELECT 1 FROM CompanyProfile)
BEGIN
    INSERT INTO CompanyProfile (CompanyProfileId, ShopName, InvoiceTerms, InvoiceFooterNote)
    VALUES (1, N'My Agriculture Shop',
            N'1. Goods once sold will not be taken back.' + CHAR(13) + CHAR(10) +
            N'2. Seed germination and crop results depend on field conditions; no guarantee is implied.' + CHAR(13) + CHAR(10) +
            N'3. Use pesticides strictly as per the label and leaflet.' + CHAR(13) + CHAR(10) +
            N'4. Subject to local jurisdiction.',
            N'Thank you for your business.');
END
GO

/*==============================================================================
  AppSettings
==============================================================================*/
MERGE AppSettings AS tgt
USING (VALUES
    (N'Stock.AllowNegative',        N'false',  N'bool',    N'Stock',   N'Allow billing beyond available stock'),
    (N'Stock.ExpiryWarningDays',    N'90',     N'int',     N'Stock',   N'Days before expiry to raise a warning'),
    (N'Stock.BlockExpiredSale',     N'true',   N'bool',    N'Stock',   N'Prevent selling expired batches'),
    (N'Stock.DefaultPickingMethod', N'FEFO',   N'string',  N'Stock',   N'FEFO or FIFO batch suggestion'),
    (N'Sales.DefaultPriceType',     N'Retail', N'string',  N'Sales',   N'Default price list on a new invoice'),
    (N'Sales.RoundOffInvoice',      N'true',   N'bool',    N'Sales',   N'Round invoice total to the nearest rupee'),
    (N'Sales.MaxDiscountPercent',   N'20',     N'decimal', N'Sales',   N'Maximum discount without an override'),
    (N'Sales.EnforceCreditLimit',   N'true',   N'bool',    N'Sales',   N'Block credit sales past the customer limit'),
    (N'Sales.PrintCopies',          N'2',      N'int',     N'Sales',   N'Invoice copies to print'),
    (N'Purchase.UpdateProductRate', N'true',   N'bool',    N'Purchase',N'Push purchase rate onto the product master'),
    (N'Purchase.CostingMethod',     N'Batch',  N'string',  N'Purchase',N'Batch or WeightedAverage costing'),
    (N'Gst.EnableGst',              N'true',   N'bool',    N'Gst',     N'Enable GST calculation'),
    -- Auth.* settings intentionally absent. Token lifetimes and lockout policy
    -- live in appsettings.json alongside the signing key, so security config
    -- has ONE source of truth. Two places to set a lockout threshold means one
    -- of them is eventually wrong and nobody knows which is in force.
    (N'Ui.Theme',                   N'light',  N'string',  N'Ui',      N'Default theme'),
    (N'Ui.PageSize',                N'25',     N'int',     N'Ui',      N'Default rows per page'),
    (N'Ui.Currency',                N'INR',    N'string',  N'Ui',      N'Currency code'),
    (N'Ui.DateFormat',              N'dd-MM-yyyy', N'string', N'Ui',   N'Display date format')
) AS src (SettingKey, SettingValue, DataType, Category, Description)
    ON tgt.SettingKey = src.SettingKey
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SettingKey, SettingValue, DataType, Category, Description)
    VALUES (src.SettingKey, src.SettingValue, src.DataType, src.Category, src.Description);
GO

PRINT N'12_SeedData.sql completed.';
GO

/*------------------------------------------------------------------------------
  Summary
------------------------------------------------------------------------------*/
-- ROWCOUNT is a reserved keyword, hence the bracketed alias.
SELECT N'States'            AS SeededTable, COUNT(*) AS [RowCount] FROM States
UNION ALL SELECT N'Units',            COUNT(*) FROM Units
UNION ALL SELECT N'GstSlabs',         COUNT(*) FROM GstSlabs
UNION ALL SELECT N'HsnCodes',         COUNT(*) FROM HsnCodes
UNION ALL SELECT N'Categories',       COUNT(*) FROM ItemSubGroupMaster
UNION ALL SELECT N'Companies',        COUNT(*) FROM Companies
UNION ALL SELECT N'StorageLocations', COUNT(*) FROM StorageLocations
UNION ALL SELECT N'PaymentModes',     COUNT(*) FROM PaymentModes
UNION ALL SELECT N'ExpenseCategories',COUNT(*) FROM ExpenseCategories
UNION ALL SELECT N'Roles',            COUNT(*) FROM Roles
UNION ALL SELECT N'Permissions',      COUNT(*) FROM Permissions
UNION ALL SELECT N'RolePermissions',  COUNT(*) FROM RolePermissions
UNION ALL SELECT N'Users',            COUNT(*) FROM Users
UNION ALL SELECT N'FinancialYears',   COUNT(*) FROM FinancialYears
UNION ALL SELECT N'NumberSeries',     COUNT(*) FROM NumberSeries
UNION ALL SELECT N'AppSettings',      COUNT(*) FROM AppSettings;
GO
