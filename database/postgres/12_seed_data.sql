/*==============================================================================
  AgriERP  |  12_seed_data.sql   (PostgreSQL)
  ------------------------------------------------------------------------------
  Reference data the application cannot start without. PostgreSQL parallel of the
  SQL Server seed (12_SeedData.sql + the fixed reference rows that live in
  05/13/15/18): states, units, tax, item groups, transaction types, vouchers,
  roles/permissions, the bootstrap admin, financial years, number series and
  settings.

  Idempotent: every statement is INSERT ... ON CONFLICT DO NOTHING, so re-running
  is safe. Explicit-id inserts are followed by setval() so the identity sequence
  does not later hand out a colliding id.

  NOT seeded: suppliers, customers, products, opening stock, manufacturer GST
  numbers - that is the shop's own data. The admin password is the sentinel
  '!SEED-PENDING!'; the API seeder writes the real BCrypt hash on first run.
==============================================================================*/

/*---------------------------------- Roles ---------------------------------*/
INSERT INTO "Roles" ("RoleName","Description","IsSystemRole") VALUES
    ('Administrator','Full access to every module and setting',            true),
    ('Manager',      'All operations and reports; no user administration',  true),
    ('Salesman',     'Billing, customers and sales reports',                true),
    ('StoreKeeper',  'Stock, purchase and product maintenance',             true)
ON CONFLICT DO NOTHING;

/*------------------------------- Permissions ------------------------------*/
INSERT INTO "Permissions" ("PermissionCode","Module","DisplayName","DisplayOrder") VALUES
    ('Dashboard.View','Dashboard','View dashboard',1),
    ('Dashboard.ViewProfit','Dashboard','View profit figures',2),
    ('Category.View','Category','View categories',10),
    ('Category.Create','Category','Create category',11),
    ('Category.Edit','Category','Edit category',12),
    ('Category.Delete','Category','Delete category',13),
    ('Company.View','Company','View companies',20),
    ('Company.Create','Company','Create company',21),
    ('Company.Edit','Company','Edit company',22),
    ('Company.Delete','Company','Delete company',23),
    ('Supplier.View','Supplier','View suppliers',30),
    ('Supplier.Create','Supplier','Create supplier',31),
    ('Supplier.Edit','Supplier','Edit supplier',32),
    ('Supplier.Delete','Supplier','Delete supplier',33),
    ('Customer.View','Customer','View customers',40),
    ('Customer.Create','Customer','Create customer',41),
    ('Customer.Edit','Customer','Edit customer',42),
    ('Customer.Delete','Customer','Delete customer',43),
    ('Product.View','Product','View products',50),
    ('Product.Create','Product','Create product',51),
    ('Product.Edit','Product','Edit product',52),
    ('Product.Delete','Product','Delete product',53),
    ('Product.Import','Product','Bulk import products',54),
    ('Product.Export','Product','Bulk export products',55),
    ('Product.EditRate','Product','Change product rates',56),
    ('Stock.View','Stock','View stock and ledger',60),
    ('Stock.Adjust','Stock','Create stock adjustment',61),
    ('Stock.Transfer','Stock','Create stock transfer',62),
    ('Stock.Post','Stock','Post stock documents',63),
    ('Stock.Opening','Stock','Enter opening stock',64),
    ('Purchase.View','Purchase','View purchases',70),
    ('Purchase.Create','Purchase','Create purchase entry',71),
    ('Purchase.Edit','Purchase','Edit draft purchase',72),
    ('Purchase.Post','Purchase','Post purchase to stock',73),
    ('Purchase.Cancel','Purchase','Cancel posted purchase',74),
    ('Purchase.Return','Purchase','Create purchase return',75),
    ('Purchase.Order','Purchase','Manage purchase orders',76),
    ('Sales.View','Sales','View invoices',80),
    ('Sales.Create','Sales','Create invoice',81),
    ('Sales.Edit','Sales','Edit draft invoice',82),
    ('Sales.Post','Sales','Post invoice',83),
    ('Sales.Cancel','Sales','Cancel posted invoice',84),
    ('Sales.Return','Sales','Create sales return',85),
    ('Sales.Print','Sales','Print invoice',86),
    ('Sales.Discount','Sales','Apply discount',87),
    ('Sales.OverrideMinRate','Sales','Sell below minimum rate',88),
    ('Sales.CreditSale','Sales','Sell on credit',89),
    ('Payment.View','Payment','View payments',90),
    ('Payment.Create','Payment','Record payment or receipt',91),
    ('Payment.Cancel','Payment','Cancel payment',92),
    ('Expense.View','Payment','View expenses',93),
    ('Expense.Create','Payment','Record expense',94),
    ('Report.Sales','Report','Sales reports',100),
    ('Report.Purchase','Report','Purchase reports',101),
    ('Report.Stock','Report','Stock reports',102),
    ('Report.Profit','Report','Profit reports',103),
    ('Report.Gst','Report','GST reports',104),
    ('Report.Party','Report','Customer and supplier reports',105),
    ('Report.Export','Report','Export reports',106),
    ('User.View','User','View users',110),
    ('User.Create','User','Create user',111),
    ('User.Edit','User','Edit user',112),
    ('User.Delete','User','Delete user',113),
    ('User.ResetPassword','User','Reset another user password',114),
    ('Role.View','User','View roles',115),
    ('Role.Manage','User','Create and edit roles',116),
    ('Settings.View','Settings','View settings',120),
    ('Settings.Edit','Settings','Change settings',121),
    ('Settings.Audit','Settings','View audit log',122),
    ('Settings.YearEnd','Settings','Close financial year',123)
ON CONFLICT DO NOTHING;

/*----------------------------- RolePermissions ----------------------------*/
-- Administrator: everything.
INSERT INTO "RolePermissions" ("RoleId","PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r CROSS JOIN "Permissions" p
WHERE r."RoleName" = 'Administrator'
ON CONFLICT DO NOTHING;

-- Manager: everything operational except user admin and year-end close.
INSERT INTO "RolePermissions" ("RoleId","PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r CROSS JOIN "Permissions" p
WHERE r."RoleName" = 'Manager'
  AND p."Module" <> 'User' AND p."PermissionCode" <> 'Settings.YearEnd'
ON CONFLICT DO NOTHING;

-- Salesman: bill, collect, look up; no cancel, no min-rate override, no profit.
INSERT INTO "RolePermissions" ("RoleId","PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r CROSS JOIN "Permissions" p
WHERE r."RoleName" = 'Salesman'
  AND p."PermissionCode" IN ('Dashboard.View','Product.View','Stock.View',
      'Customer.View','Customer.Create','Customer.Edit',
      'Sales.View','Sales.Create','Sales.Post','Sales.Print','Sales.Discount','Sales.Return',
      'Payment.View','Payment.Create','Report.Sales','Report.Party')
ON CONFLICT DO NOTHING;

-- StoreKeeper: goods in, counted, moved. No billing, no money.
INSERT INTO "RolePermissions" ("RoleId","PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r CROSS JOIN "Permissions" p
WHERE r."RoleName" = 'StoreKeeper'
  AND p."PermissionCode" IN ('Dashboard.View','Category.View','Company.View','Supplier.View',
      'Product.View','Product.Create','Product.Edit','Product.Import','Product.Export',
      'Stock.View','Stock.Adjust','Stock.Transfer','Stock.Post','Stock.Opening',
      'Purchase.View','Purchase.Create','Purchase.Edit','Purchase.Post','Purchase.Return','Purchase.Order',
      'Report.Stock','Report.Purchase')
ON CONFLICT DO NOTHING;

/*-------------------------------- Users (admin) ---------------------------*/
INSERT INTO "Users" ("UserName","Email","PasswordHash","FullName","RoleId","MustChangePassword","IsActive")
SELECT 'admin', NULL, '!SEED-PENDING!', 'System Administrator',
       (SELECT "RoleId" FROM "Roles" WHERE "RoleName" = 'Administrator'), true, true
ON CONFLICT DO NOTHING;

/*-------------------------------- States ----------------------------------*/
INSERT INTO "States" ("StateId","StateCode","StateName","StateAbbr","IsUnionTerritory") VALUES
    (1,'01','Jammu and Kashmir','JK',true),(2,'02','Himachal Pradesh','HP',false),
    (3,'03','Punjab','PB',false),(4,'04','Chandigarh','CH',true),
    (5,'05','Uttarakhand','UK',false),(6,'06','Haryana','HR',false),
    (7,'07','Delhi','DL',true),(8,'08','Rajasthan','RJ',false),
    (9,'09','Uttar Pradesh','UP',false),(10,'10','Bihar','BR',false),
    (11,'11','Sikkim','SK',false),(12,'12','Arunachal Pradesh','AR',false),
    (13,'13','Nagaland','NL',false),(14,'14','Manipur','MN',false),
    (15,'15','Mizoram','MZ',false),(16,'16','Tripura','TR',false),
    (17,'17','Meghalaya','ML',false),(18,'18','Assam','AS',false),
    (19,'19','West Bengal','WB',false),(20,'20','Jharkhand','JH',false),
    (21,'21','Odisha','OD',false),(22,'22','Chhattisgarh','CG',false),
    (23,'23','Madhya Pradesh','MP',false),(24,'24','Gujarat','GJ',false),
    (26,'26','Dadra and Nagar Haveli and Daman and Diu','DD',true),
    (27,'27','Maharashtra','MH',false),(29,'29','Karnataka','KA',false),
    (30,'30','Goa','GA',false),(31,'31','Lakshadweep','LD',true),
    (32,'32','Kerala','KL',false),(33,'33','Tamil Nadu','TN',false),
    (34,'34','Puducherry','PY',true),(35,'35','Andaman and Nicobar Islands','AN',true),
    (36,'36','Telangana','TS',false),(37,'37','Andhra Pradesh','AP',false),
    (38,'38','Ladakh','LA',true),(97,'97','Other Territory','OT',true)
ON CONFLICT DO NOTHING;

/*-------------------------------- Units -----------------------------------*/
INSERT INTO "Units" ("UnitCode","UnitName","DisplayOrder","AllowDecimal") VALUES
    ('NOS','Numbers',1,false),('PKT','Packet',2,false),('BTL','Bottle',3,false),
    ('BAG','Bag',4,false),('BOX','Box',5,false),('KG','Kilogram',6,true),
    ('GM','Gram',7,true),('LTR','Litre',8,true),('ML','Millilitre',9,true),
    ('QTL','Quintal',10,true),('TON','Tonne',11,true),('MTR','Metre',12,true),
    ('SET','Set',13,false),('PAIR','Pair',14,false)
ON CONFLICT DO NOTHING;

/*-------------------------------- GstSlabs --------------------------------*/
INSERT INTO "GstSlabs" ("SlabName","TotalRate","CgstRate","SgstRate","IgstRate") VALUES
    ('GST 0%',0.000,0.000,0.000,0.000),
    ('GST 5%',5.000,2.500,2.500,5.000),
    ('GST 12%',12.000,6.000,6.000,12.000),
    ('GST 18%',18.000,9.000,9.000,18.000),
    ('GST 28%',28.000,14.000,14.000,28.000)
ON CONFLICT DO NOTHING;

/*-------------------------------- HsnCodes --------------------------------*/
INSERT INTO "HsnCodes" ("HsnCode","Description","DefaultGstSlabId")
SELECT v.code, v.descr, g."GstSlabId"
FROM (VALUES
    ('1209','Seeds, fruit and spores of a kind used for sowing',0.000),
    ('3101','Animal or vegetable fertilisers; bio-fertilisers',5.000),
    ('3102','Mineral or chemical fertilisers, nitrogenous (Urea)',5.000),
    ('3103','Mineral or chemical fertilisers, phosphatic',5.000),
    ('3104','Mineral or chemical fertilisers, potassic (MOP)',5.000),
    ('3105','Fertilisers containing two or three nutrients (DAP, NPK)',5.000),
    ('3808','Insecticides, fungicides, herbicides, plant growth regulators',18.000),
    ('3824','Micronutrients and chemical preparations',18.000),
    ('8201','Hand tools: spades, hoes, sickles, khurpi, pruners',12.000),
    ('8424','Mechanical appliances for spraying liquids or powders',12.000),
    ('3923','Plastic articles: mulching film, crates, tanks',18.000),
    ('5608','Knotted netting: crop cover, shade net, bird net',12.000)
) AS v(code, descr, rate)
LEFT JOIN "GstSlabs" g ON g."TotalRate" = v.rate
ON CONFLICT DO NOTHING;

/*------------------------------- PaymentModes -----------------------------*/
INSERT INTO "PaymentModes" ("ModeCode","ModeName","RequiresReference","IsBankMode","DisplayOrder") VALUES
    ('CASH','Cash',false,false,1),('UPI','UPI',true,true,2),
    ('CARD','Debit/Credit Card',true,true,3),('CHEQUE','Cheque',true,true,4),
    ('NEFT','NEFT / RTGS / IMPS',true,true,5),('CREDIT','On Credit',false,false,6)
ON CONFLICT DO NOTHING;

/*----------------------------- ExpenseCategories --------------------------*/
INSERT INTO "ExpenseCategories" ("CategoryCode","CategoryName") VALUES
    ('RENT','Shop Rent'),('SALARY','Salary and Wages'),('ELECTRIC','Electricity'),
    ('TRANSPORT','Transport and Freight'),('LOADING','Loading and Unloading'),
    ('TELEPHONE','Telephone and Internet'),('STATIONERY','Printing and Stationery'),
    ('REPAIR','Repairs and Maintenance'),('LICENCE','Licence and Statutory Fees'),
    ('MISC','Miscellaneous')
ON CONFLICT DO NOTHING;

/*----------------------------- TransactionTypes ---------------------------
  Fixed stock-movement codes (from 05_Inventory). Direction +1 = in, -1 = out.
  fn_post_stock_transaction looks these up, so stock cannot move without them. */
INSERT INTO "TransactionTypes" ("TransactionTypeId","TypeCode","TypeName","Direction") VALUES
    (1,'OpeningStock','Opening Stock',1),
    (2,'PurchaseIn','Purchase',1),
    (3,'PurchaseReturnOut','Purchase Return',-1),
    (4,'SalesOut','Sale',-1),
    (5,'SalesReturnIn','Sales Return',1),
    (6,'AdjustmentIn','Adjustment (Increase)',1),
    (7,'AdjustmentOut','Adjustment (Decrease)',-1),
    (8,'TransferOut','Transfer Out',-1),
    (9,'TransferIn','Transfer In',1),
    (10,'ExpiryWriteOff','Expiry Write-Off',-1),
    (11,'DamageWriteOff','Damage Write-Off',-1)
ON CONFLICT DO NOTHING;
SELECT setval(pg_get_serial_sequence('"TransactionTypes"','TransactionTypeId'),
              (SELECT max("TransactionTypeId") FROM "TransactionTypes"), true);

/*------------------------------- VoucherMaster ----------------------------*/
INSERT INTO "VoucherMaster" ("VoucherId","VoucherCode","VoucherName","VoucherType","Prefix","DisplayOrder") VALUES
    (1,'PO','Purchase Order','PurchaseOrder','PO',10),
    (2,'PGRN','Purchase GRN','Purchase','GRN',20),
    (3,'PREQ','Purchase Requisition','PurchaseRequisition','REQ',5)
ON CONFLICT DO NOTHING;
SELECT setval(pg_get_serial_sequence('"VoucherMaster"','VoucherId'),
              (SELECT max("VoucherId") FROM "VoucherMaster"), true);

/*----------------------------- StorageLocations ---------------------------*/
INSERT INTO "StorageLocations" ("LocationCode","LocationName","LocationType","IsDefault") VALUES
    ('MAIN','Main Shop','Counter',true),
    ('GODOWN','Godown','Godown',false)
ON CONFLICT DO NOTHING;

/*-------------------------------- Companies -------------------------------*/
INSERT INTO "Companies" ("CompanyCode","CompanyName") VALUES
    ('UPL','UPL Limited'),('BAYER','Bayer CropScience Limited'),
    ('SYNGENTA','Syngenta India Limited'),('BASF','BASF India Limited'),
    ('CORO','Coromandel International Limited'),
    ('IFFCO','Indian Farmers Fertiliser Cooperative Limited'),
    ('RALLIS','Rallis India Limited'),('DHANUKA','Dhanuka Agritech Limited'),
    ('PIIND','PI Industries Limited'),('FMC','FMC India Private Limited'),
    ('ADAMA','ADAMA India Private Limited'),('GSP','GSP Crop Science Private Limited'),
    ('INSECT','Insecticides (India) Limited'),
    ('KRIBHCO','Krishak Bharati Cooperative Limited'),
    ('NFL','National Fertilizers Limited'),
    ('MAHYCO','Maharashtra Hybrid Seeds Company'),
    ('NUZIVEEDU','Nuziveedu Seeds Limited'),('KALASH','Kalash Seeds Private Limited'),
    ('OTHER','Other / Local')
ON CONFLICT DO NOTHING;

/*----------------------------- ItemGroupMaster ----------------------------*/
INSERT INTO "ItemGroupMaster" ("ItemGroupId","ItemGroupCode","ItemGroupName","ItemCodePrefix","DisplayOrder") VALUES
    (1,'PRDGRP','Product Master','P',1),
    (2,'FRTGRP','Fertilizers Master','F',2),
    (3,'SEDGRP','Seed Master','S',3),
    (4,'OTHGRP','Other Master','R',4)
ON CONFLICT DO NOTHING;
SELECT setval(pg_get_serial_sequence('"ItemGroupMaster"','ItemGroupId'),
              (SELECT max("ItemGroupId") FROM "ItemGroupMaster"), true);

/*---------------------------- ItemSubGroupMaster --------------------------
  Explicit ids so the seed sub-categories can reference their parent (SEED). */
INSERT INTO "ItemSubGroupMaster"
    ("ItemSubGroupId","ItemSubGroupCode","ItemSubGroupName","Description","ItemGroupId","ParentItemSubGroupId","DisplayOrder") VALUES
    (1,'INSEC','Insecticide','Products controlling insect pests',1,NULL,1),
    (2,'PESTI','Pesticide','General pest control products',1,NULL,2),
    (3,'FUNGI','Fungicide','Products controlling fungal disease',1,NULL,3),
    (4,'HERBI','Herbicide','Weed control products',1,NULL,4),
    (5,'FERT','Fertilizer','Chemical and mineral fertilizers',2,NULL,5),
    (6,'BIOFERT','Bio Fertilizer','Microbial and organic fertilizers',2,NULL,6),
    (7,'MICRO','Micronutrient','Zinc, boron, iron and mixed micronutrients',2,NULL,7),
    (8,'PGR','Plant Growth Regulator','Hormones and growth promoters',1,NULL,8),
    (9,'SEED','Seeds','All sowing seed',3,NULL,9),
    (10,'EQUIP','Farming Equipment','Sprayers, tools and implements',4,NULL,10),
    (11,'ACCES','Agriculture Accessories','Nozzles, pipes, nets and sundries',4,NULL,11),
    (12,'OTHER','Others','Uncategorised items',4,NULL,99),
    (13,'SEEDVEG','Vegetable Seeds','Tomato, chilli, onion, brinjal and similar',3,9,1),
    (14,'SEEDFIELD','Field Crop Seeds','Wheat, soybean, cotton, gram and similar',3,9,2),
    (15,'SEEDFLOW','Flower Seeds','Marigold, rose and ornamental seed',3,9,3),
    (16,'SEEDFRUIT','Fruit Seeds','Papaya, watermelon and fruit seed',3,9,4)
ON CONFLICT DO NOTHING;
SELECT setval(pg_get_serial_sequence('"ItemSubGroupMaster"','ItemSubGroupId'),
              (SELECT max("ItemSubGroupId") FROM "ItemSubGroupMaster"), true);

/*------------------------------ FinancialYears ----------------------------
  Derived from today so the script never goes stale. FY runs 1 Apr - 31 Mar;
  the year containing today is active. */
WITH base AS (
    SELECT CASE WHEN extract(month FROM CURRENT_DATE) >= 4
                THEN extract(year FROM CURRENT_DATE)::int
                ELSE extract(year FROM CURRENT_DATE)::int - 1 END AS fy
),
yrs AS (SELECT b.fy, v.y FROM base b CROSS JOIN LATERAL (VALUES (b.fy - 1), (b.fy), (b.fy + 1)) AS v(y))
INSERT INTO "FinancialYears" ("YearCode","StartDate","EndDate","IsActive")
SELECT y::text || '-' || right((y + 1)::text, 2), make_date(y, 4, 1), make_date(y + 1, 3, 31), (y = fy)
FROM yrs
ON CONFLICT DO NOTHING;

/*------------------------------- NumberSeries -----------------------------
  Documents carry the financial year (INV/2026-27/00042); master codes do not
  (PRD-000001), so they use a hyphen and no year segment. */
INSERT INTO "NumberSeries" ("DocumentType","FinancialYearId","Prefix","PaddingLength","IncludeYearCode","Separator")
SELECT v.dt, (SELECT "FinancialYearId" FROM "FinancialYears" WHERE "IsActive" = true),
       v.pfx, v.pad,
       CASE WHEN v.dt IN ('Product','Customer','Supplier') THEN false ELSE true END,
       CASE WHEN v.dt IN ('Product','Customer','Supplier') THEN '-' ELSE '/' END
FROM (VALUES
    ('Sale','INV',5),('SalesReturn','SR',5),('Purchase','PUR',5),('PurchaseReturn','PR',5),
    ('PurchaseOrder','PO',5),('PurchaseRequisition','REQ',5),('StockAdjustment','ADJ',5),('StockTransfer','TRF',5),
    ('Receipt','RCT',5),('Payment','PMT',5),('Expense','EXP',5),
    ('Product','PRD',6),('Customer','CUS',5),('Supplier','SUP',5)
) AS v(dt, pfx, pad)
ON CONFLICT DO NOTHING;

-- Per-item-group code series (DocumentType 'Item_<GroupCode>'), one per item
-- group. Item codes are permanent (no financial-year reset), so FinancialYearId
-- is NULL and IncludeYearCode is false. Prefix = the group's ItemCodePrefix.
INSERT INTO "NumberSeries" ("DocumentType","FinancialYearId","Prefix","PaddingLength","IncludeYearCode","Separator")
SELECT 'Item_' || "ItemGroupCode", NULL, "ItemCodePrefix", 6, false, '-'
FROM "ItemGroupMaster"
ON CONFLICT DO NOTHING;

-- Warehouse codes: WH00001 - no financial year, no separator.
INSERT INTO "NumberSeries" ("DocumentType","FinancialYearId","Prefix","PaddingLength","IncludeYearCode","Separator")
SELECT 'Warehouse', (SELECT "FinancialYearId" FROM "FinancialYears" WHERE "IsActive" = true), 'WH', 5, false, ''
ON CONFLICT DO NOTHING;

/*------------------------------ CompanyProfile ----------------------------*/
INSERT INTO "CompanyProfile" ("CompanyProfileId","ShopName","InvoiceTerms","InvoiceFooterNote")
VALUES (1, 'My Agriculture Shop',
    E'1. Goods once sold will not be taken back.\n2. Seed germination and crop results depend on field conditions; no guarantee is implied.\n3. Use pesticides strictly as per the label and leaflet.\n4. Subject to local jurisdiction.',
    'Thank you for your business.')
ON CONFLICT DO NOTHING;

/*-------------------------------- AppSettings -----------------------------*/
INSERT INTO "AppSettings" ("SettingKey","SettingValue","DataType","Category","Description") VALUES
    ('Stock.AllowNegative','false','bool','Stock','Allow billing beyond available stock'),
    ('Stock.ExpiryWarningDays','90','int','Stock','Days before expiry to raise a warning'),
    ('Stock.BlockExpiredSale','true','bool','Stock','Prevent selling expired batches'),
    ('Stock.DefaultPickingMethod','FEFO','string','Stock','FEFO or FIFO batch suggestion'),
    ('Sales.DefaultPriceType','Retail','string','Sales','Default price list on a new invoice'),
    ('Sales.RoundOffInvoice','true','bool','Sales','Round invoice total to the nearest rupee'),
    ('Sales.MaxDiscountPercent','20','decimal','Sales','Maximum discount without an override'),
    ('Sales.EnforceCreditLimit','true','bool','Sales','Block credit sales past the customer limit'),
    ('Sales.PrintCopies','2','int','Sales','Invoice copies to print'),
    ('Purchase.UpdateProductRate','true','bool','Purchase','Push purchase rate onto the product master'),
    ('Purchase.CostingMethod','Batch','string','Purchase','Batch or WeightedAverage costing'),
    ('Gst.EnableGst','true','bool','Gst','Enable GST calculation'),
    ('Ui.Theme','light','string','Ui','Default theme'),
    ('Ui.PageSize','25','int','Ui','Default rows per page'),
    ('Ui.Currency','INR','string','Ui','Currency code'),
    ('Ui.DateFormat','dd-MM-yyyy','string','Ui','Display date format')
ON CONFLICT DO NOTHING;
