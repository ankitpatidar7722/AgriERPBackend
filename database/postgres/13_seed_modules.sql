/*==============================================================================
  AgriERP  |  13_seed_modules.sql   (PostgreSQL)
  Navigation menu master - drives the sidebar. Ported from the live ModuleMaster.
==============================================================================*/
INSERT INTO "ModuleMaster" ("ModuleID","ModuleName","ModuleDisplayName","ModuleHeadName","ModuleHeadDisplayName","ModuleHeadDisplayOrder","ModuleDisplayOrder","SetGroupIndex","IconName","IsDeletedTransaction") VALUES
(1,'/dashboard','Dashboard','Overview','Overview',1,1,1,'LayoutDashboard',true),
(2,'/sales','Sales','Trading','Trading',2,1,2,'Receipt',false),
(3,'/purchases','Purchase GRN','Inventory','Inventory',3,3,3,'PackageCheck',false),
(4,'/payments','Payments','Trading','Trading',2,3,2,'Wallet',true),
(5,'/stock','Stock','Inventory','Inventory',3,4,3,'Warehouse',false),
(6,'/reports','Reports','Inventory','Inventory',3,5,3,'BarChart3',false),
(7,'/items','Items','Masters','Masters',4,3,4,'Package',false),
(8,'/item-subgroups','Item Sub Groups','Masters','Masters',4,4,4,'FolderTree',false),
(9,'/companies','Companies','Masters','Masters',4,5,4,'Building2',false),
(10,'/suppliers','Suppliers','Masters','Masters',4,6,4,'Truck',false),
(11,'/customers','Customers','Masters','Masters',4,7,4,'Users',false),
(12,'/units','Units','Masters','Masters',4,8,4,'Ruler',false),
(15,'/item-groups','Item Groups','Masters','Masters',4,2,4,'Layers',false),
(16,'/purchases/orders','Purchase Order','Inventory','Inventory',3,2,3,'ClipboardList',false),
(17,'/purchases/requisitions','Purchase Requisition','Inventory','Inventory',3,1,3,'FileText',false),
(18,'/shop-warehouse','Shop & Warehouse','Masters','Masters',4,9,4,'Store',false),
(19,'/accounts/customer-payment','Customer Payment','Accounts','Accounts',5,1,5,'Wallet',false),
(20,'/accounts/customer-ledger','Customer Ledger','Accounts','Accounts',5,3,5,'BookOpen',false),
(21,'/accounts/supplier-payment','Supplier Payment','Accounts','Accounts',5,2,5,'HandCoins',false),
(22,'/accounts/supplier-ledger','Supplier Ledger','Accounts','Accounts',5,4,5,'BookOpen',false),
(23,'/accounts/expenses','Expenses','Accounts','Accounts',5,5,5,'Receipt',false),
(24,'/sales/returns','Sales Return','Trading','Trading',2,2,2,'Undo2',false),
(1022,'/dashboard/sales','Sales Dashboard','Dashboard','Dashboard',1,1,1,'TrendingUp',false),
(1023,'/dashboard/purchase','Purchase Dashboard','Dashboard','Dashboard',1,2,1,'PieChart',false),
(25,'/bulk-import','Bulk Import','Masters','Masters',4,20,4,'Upload',false)
ON CONFLICT DO NOTHING;
SELECT setval(pg_get_serial_sequence('"ModuleMaster"','ModuleID'), (SELECT max("ModuleID") FROM "ModuleMaster"), true);
