/*==============================================================================
  AgriERP  |  80_foreign_keys.sql   (PostgreSQL)
  ------------------------------------------------------------------------------
  Every foreign key, added after all tables exist so table creation never
  depends on run order (and self / cyclic references just work). NO ACTION is
  PostgreSQL's default on delete; CASCADE is stated explicitly.
  Mirrors the FK set of the live SQL Server AgriERP database.
==============================================================================*/

-- Security
ALTER TABLE "RolePermissions"   ADD CONSTRAINT "FK_RolePermissions_Role"       FOREIGN KEY ("RoleId")       REFERENCES "Roles" ("RoleId")             ON DELETE CASCADE;
ALTER TABLE "RolePermissions"   ADD CONSTRAINT "FK_RolePermissions_Permission" FOREIGN KEY ("PermissionId") REFERENCES "Permissions" ("PermissionId") ON DELETE CASCADE;
ALTER TABLE "Users"             ADD CONSTRAINT "FK_Users_Role"                 FOREIGN KEY ("RoleId")       REFERENCES "Roles" ("RoleId");
ALTER TABLE "UserRefreshTokens" ADD CONSTRAINT "FK_UserRefreshTokens_User"     FOREIGN KEY ("UserId")       REFERENCES "Users" ("UserId")             ON DELETE CASCADE;
ALTER TABLE "UserPasswordResets" ADD CONSTRAINT "FK_UserPasswordResets_User"   FOREIGN KEY ("UserId")       REFERENCES "Users" ("UserId")             ON DELETE CASCADE;

-- Masters
ALTER TABLE "Companies"          ADD CONSTRAINT "FK_Companies_State"      FOREIGN KEY ("StateId")          REFERENCES "States" ("StateId");
ALTER TABLE "Customers"          ADD CONSTRAINT "FK_Customers_State"      FOREIGN KEY ("StateId")          REFERENCES "States" ("StateId");
ALTER TABLE "Suppliers"          ADD CONSTRAINT "FK_Suppliers_State"      FOREIGN KEY ("StateId")          REFERENCES "States" ("StateId");
ALTER TABLE "ShopMaster"         ADD CONSTRAINT "FK_ShopMaster_State"     FOREIGN KEY ("StateId")          REFERENCES "States" ("StateId");
ALTER TABLE "HsnCodes"           ADD CONSTRAINT "FK_HsnCodes_GstSlab"     FOREIGN KEY ("DefaultGstSlabId") REFERENCES "GstSlabs" ("GstSlabId");
ALTER TABLE "StorageLocations"   ADD CONSTRAINT "FK_StorageLocations_Parent" FOREIGN KEY ("ParentLocationId") REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "WarehouseBins"      ADD CONSTRAINT "FK_WarehouseBins_Warehouse" FOREIGN KEY ("WarehouseId")   REFERENCES "WarehouseMaster" ("WarehouseId") ON DELETE CASCADE;
ALTER TABLE "ItemSubGroupMaster" ADD CONSTRAINT "FK_ItemSubGroupMaster_Group"  FOREIGN KEY ("ItemGroupId")           REFERENCES "ItemGroupMaster" ("ItemGroupId");
ALTER TABLE "ItemSubGroupMaster" ADD CONSTRAINT "FK_ItemSubGroupMaster_Parent" FOREIGN KEY ("ParentItemSubGroupId") REFERENCES "ItemSubGroupMaster" ("ItemSubGroupId");

-- Items
ALTER TABLE "ItemGroupFieldMaster" ADD CONSTRAINT "FK_ItemGroupFieldMaster_Group" FOREIGN KEY ("ItemGroupId")     REFERENCES "ItemGroupMaster" ("ItemGroupId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_Company"      FOREIGN KEY ("CompanyId")         REFERENCES "Companies" ("CompanyId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_Group"        FOREIGN KEY ("ItemGroupId")       REFERENCES "ItemGroupMaster" ("ItemGroupId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_GstSlab"      FOREIGN KEY ("GstSlabId")         REFERENCES "GstSlabs" ("GstSlabId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_Hsn"          FOREIGN KEY ("HsnId")             REFERENCES "HsnCodes" ("HsnId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_ItemSubGroup" FOREIGN KEY ("ItemSubGroupId")    REFERENCES "ItemSubGroupMaster" ("ItemSubGroupId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_Location"     FOREIGN KEY ("DefaultLocationId") REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_PackingUnit"  FOREIGN KEY ("PackingUnitId")     REFERENCES "Units" ("UnitId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_PurchaseUnit" FOREIGN KEY ("PurchaseUnitId")    REFERENCES "Units" ("UnitId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_StockUnit"    FOREIGN KEY ("StockUnitId")       REFERENCES "Units" ("UnitId");
ALTER TABLE "ItemMaster"         ADD CONSTRAINT "FK_ItemMaster_Unit"         FOREIGN KEY ("UnitId")            REFERENCES "Units" ("UnitId");
ALTER TABLE "ItemMasterDetails"  ADD CONSTRAINT "FK_ItemMasterDetails_Field" FOREIGN KEY ("ItemGroupFieldId")  REFERENCES "ItemGroupFieldMaster" ("ItemGroupFieldId");
ALTER TABLE "ItemMasterDetails"  ADD CONSTRAINT "FK_ItemMasterDetails_Item"  FOREIGN KEY ("ItemId")            REFERENCES "ItemMaster" ("ItemId") ON DELETE CASCADE;
ALTER TABLE "ItemBatches"        ADD CONSTRAINT "FK_ItemBatches_Item"        FOREIGN KEY ("ItemId")            REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "ItemBatches"        ADD CONSTRAINT "FK_ItemBatches_Location"    FOREIGN KEY ("LocationId")        REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "ItemImages"         ADD CONSTRAINT "FK_ItemImages_Item"         FOREIGN KEY ("ItemId")            REFERENCES "ItemMaster" ("ItemId") ON DELETE CASCADE;
ALTER TABLE "ItemPriceHistory"   ADD CONSTRAINT "FK_ItemPriceHistory_Item"   FOREIGN KEY ("ItemId")            REFERENCES "ItemMaster" ("ItemId");

-- Inventory
ALTER TABLE "StockTransactions"      ADD CONSTRAINT "FK_StockTransactions_Batch"    FOREIGN KEY ("BatchId")               REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "StockTransactions"      ADD CONSTRAINT "FK_StockTransactions_Location" FOREIGN KEY ("LocationId")            REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "StockTransactions"      ADD CONSTRAINT "FK_StockTransactions_Product"  FOREIGN KEY ("ItemId")                REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "StockTransactions"      ADD CONSTRAINT "FK_StockTransactions_Reverses"  FOREIGN KEY ("ReversesTransactionId") REFERENCES "StockTransactions" ("StockTransactionId");
ALTER TABLE "StockTransactions"      ADD CONSTRAINT "FK_StockTransactions_Type"     FOREIGN KEY ("TransactionTypeId")     REFERENCES "TransactionTypes" ("TransactionTypeId");
ALTER TABLE "StockAdjustments"       ADD CONSTRAINT "FK_StockAdjustments_Location"  FOREIGN KEY ("LocationId")            REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "StockAdjustmentDetails" ADD CONSTRAINT "FK_StockAdjustmentDetails_Adjustment" FOREIGN KEY ("AdjustmentId")   REFERENCES "StockAdjustments" ("AdjustmentId") ON DELETE CASCADE;
ALTER TABLE "StockAdjustmentDetails" ADD CONSTRAINT "FK_StockAdjustmentDetails_Batch"      FOREIGN KEY ("BatchId")        REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "StockAdjustmentDetails" ADD CONSTRAINT "FK_StockAdjustmentDetails_Product"    FOREIGN KEY ("ItemId")         REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "StockAdjustmentDetails" ADD CONSTRAINT "FK_StockAdjustmentDetails_Warehouse"  FOREIGN KEY ("WarehouseId")    REFERENCES "WarehouseMaster" ("WarehouseId");
ALTER TABLE "StockTransfers"         ADD CONSTRAINT "FK_StockTransfers_FromLocation" FOREIGN KEY ("FromLocationId")       REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "StockTransfers"         ADD CONSTRAINT "FK_StockTransfers_ToLocation"   FOREIGN KEY ("ToLocationId")         REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "StockTransferDetails"   ADD CONSTRAINT "FK_StockTransferDetails_FromBatch" FOREIGN KEY ("FromBatchId")       REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "StockTransferDetails"   ADD CONSTRAINT "FK_StockTransferDetails_Product"   FOREIGN KEY ("ItemId")            REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "StockTransferDetails"   ADD CONSTRAINT "FK_StockTransferDetails_ToBatch"    FOREIGN KEY ("ToBatchId")         REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "StockTransferDetails"   ADD CONSTRAINT "FK_StockTransferDetails_Transfer"   FOREIGN KEY ("TransferId")        REFERENCES "StockTransfers" ("TransferId") ON DELETE CASCADE;

-- Purchase
ALTER TABLE "PurchaseRequisitions"       ADD CONSTRAINT "FK_PurchaseRequisitions_Location" FOREIGN KEY ("LocationId") REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "PurchaseRequisitions"       ADD CONSTRAINT "FK_PurchaseRequisitions_Voucher"  FOREIGN KEY ("VoucherId")  REFERENCES "VoucherMaster" ("VoucherId");
ALTER TABLE "PurchaseRequisitionDetails" ADD CONSTRAINT "FK_PurchaseRequisitionDetails_Item" FOREIGN KEY ("ItemId")        REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "PurchaseRequisitionDetails" ADD CONSTRAINT "FK_PurchaseRequisitionDetails_Req"  FOREIGN KEY ("RequisitionId") REFERENCES "PurchaseRequisitions" ("RequisitionId") ON DELETE CASCADE;
ALTER TABLE "PurchaseRequisitionDetails" ADD CONSTRAINT "FK_PurchaseRequisitionDetails_Unit" FOREIGN KEY ("UnitId")        REFERENCES "Units" ("UnitId");
ALTER TABLE "PurchaseOrders"       ADD CONSTRAINT "FK_PurchaseOrders_Location" FOREIGN KEY ("LocationId") REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "PurchaseOrders"       ADD CONSTRAINT "FK_PurchaseOrders_Supplier" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("SupplierId");
ALTER TABLE "PurchaseOrders"       ADD CONSTRAINT "FK_PurchaseOrders_Voucher"  FOREIGN KEY ("VoucherId")  REFERENCES "VoucherMaster" ("VoucherId");
ALTER TABLE "PurchaseOrderDetails" ADD CONSTRAINT "FK_PurchaseOrderDetails_Order"       FOREIGN KEY ("PurchaseOrderId")     REFERENCES "PurchaseOrders" ("PurchaseOrderId") ON DELETE CASCADE;
ALTER TABLE "PurchaseOrderDetails" ADD CONSTRAINT "FK_PurchaseOrderDetails_Product"     FOREIGN KEY ("ItemId")              REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "PurchaseOrderDetails" ADD CONSTRAINT "FK_PurchaseOrderDetails_Requisition" FOREIGN KEY ("RequisitionDetailId") REFERENCES "PurchaseRequisitionDetails" ("RequisitionDetailId");
ALTER TABLE "PurchaseOrderDetails" ADD CONSTRAINT "FK_PurchaseOrderDetails_Unit"        FOREIGN KEY ("UnitId")              REFERENCES "Units" ("UnitId");
ALTER TABLE "Purchases"       ADD CONSTRAINT "FK_Purchases_Location"  FOREIGN KEY ("LocationId")      REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "Purchases"       ADD CONSTRAINT "FK_Purchases_Order"     FOREIGN KEY ("PurchaseOrderId") REFERENCES "PurchaseOrders" ("PurchaseOrderId");
ALTER TABLE "Purchases"       ADD CONSTRAINT "FK_Purchases_State"     FOREIGN KEY ("SupplierStateId") REFERENCES "States" ("StateId");
ALTER TABLE "Purchases"       ADD CONSTRAINT "FK_Purchases_Supplier"  FOREIGN KEY ("SupplierId")      REFERENCES "Suppliers" ("SupplierId");
ALTER TABLE "Purchases"       ADD CONSTRAINT "FK_Purchases_Voucher"   FOREIGN KEY ("VoucherId")       REFERENCES "VoucherMaster" ("VoucherId");
ALTER TABLE "Purchases"       ADD CONSTRAINT "FK_Purchases_Warehouse" FOREIGN KEY ("WarehouseId")     REFERENCES "WarehouseMaster" ("WarehouseId");
ALTER TABLE "PurchaseDetails" ADD CONSTRAINT "FK_PurchaseDetails_Batch"    FOREIGN KEY ("BatchId")    REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "PurchaseDetails" ADD CONSTRAINT "FK_PurchaseDetails_Product"  FOREIGN KEY ("ItemId")     REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "PurchaseDetails" ADD CONSTRAINT "FK_PurchaseDetails_Purchase" FOREIGN KEY ("PurchaseId") REFERENCES "Purchases" ("PurchaseId") ON DELETE CASCADE;
ALTER TABLE "PurchaseDetails" ADD CONSTRAINT "FK_PurchaseDetails_Unit"     FOREIGN KEY ("UnitId")     REFERENCES "Units" ("UnitId");
ALTER TABLE "PurchaseDetails" ADD CONSTRAINT "FK_PurchaseDetails_OrderDetail" FOREIGN KEY ("PurchaseOrderDetailId") REFERENCES "PurchaseOrderDetails" ("PurchaseOrderDetailId");
ALTER TABLE "PurchaseReturns"       ADD CONSTRAINT "FK_PurchaseReturns_Location" FOREIGN KEY ("LocationId") REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "PurchaseReturns"       ADD CONSTRAINT "FK_PurchaseReturns_Purchase" FOREIGN KEY ("PurchaseId") REFERENCES "Purchases" ("PurchaseId");
ALTER TABLE "PurchaseReturns"       ADD CONSTRAINT "FK_PurchaseReturns_Supplier" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("SupplierId");
ALTER TABLE "PurchaseReturnDetails" ADD CONSTRAINT "FK_PurchaseReturnDetails_Batch"          FOREIGN KEY ("BatchId")          REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "PurchaseReturnDetails" ADD CONSTRAINT "FK_PurchaseReturnDetails_Product"        FOREIGN KEY ("ItemId")           REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "PurchaseReturnDetails" ADD CONSTRAINT "FK_PurchaseReturnDetails_PurchaseDetail" FOREIGN KEY ("PurchaseDetailId") REFERENCES "PurchaseDetails" ("PurchaseDetailId");
ALTER TABLE "PurchaseReturnDetails" ADD CONSTRAINT "FK_PurchaseReturnDetails_Return"         FOREIGN KEY ("PurchaseReturnId") REFERENCES "PurchaseReturns" ("PurchaseReturnId") ON DELETE CASCADE;
ALTER TABLE "PurchaseReturnDetails" ADD CONSTRAINT "FK_PurchaseReturnDetails_Unit"           FOREIGN KEY ("UnitId")           REFERENCES "Units" ("UnitId");

-- Sales
ALTER TABLE "Sales"        ADD CONSTRAINT "FK_Sales_Customer"      FOREIGN KEY ("CustomerId")           REFERENCES "Customers" ("CustomerId");
ALTER TABLE "Sales"        ADD CONSTRAINT "FK_Sales_Location"      FOREIGN KEY ("LocationId")           REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "Sales"        ADD CONSTRAINT "FK_Sales_PlaceOfSupply" FOREIGN KEY ("PlaceOfSupplyStateId")  REFERENCES "States" ("StateId");
ALTER TABLE "Sales"        ADD CONSTRAINT "FK_Sales_Salesman"      FOREIGN KEY ("SalesmanId")           REFERENCES "Users" ("UserId");
ALTER TABLE "SalesDetails" ADD CONSTRAINT "FK_SalesDetails_Batch"   FOREIGN KEY ("BatchId") REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "SalesDetails" ADD CONSTRAINT "FK_SalesDetails_Product" FOREIGN KEY ("ItemId")  REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "SalesDetails" ADD CONSTRAINT "FK_SalesDetails_Sale"    FOREIGN KEY ("SaleId")  REFERENCES "Sales" ("SaleId") ON DELETE CASCADE;
ALTER TABLE "SalesDetails" ADD CONSTRAINT "FK_SalesDetails_Unit"    FOREIGN KEY ("UnitId")  REFERENCES "Units" ("UnitId");
ALTER TABLE "SalePayments" ADD CONSTRAINT "FK_SalePayments_PaymentMode" FOREIGN KEY ("PaymentModeId") REFERENCES "PaymentModes" ("PaymentModeId");
ALTER TABLE "SalePayments" ADD CONSTRAINT "FK_SalePayments_Sale"        FOREIGN KEY ("SaleId")        REFERENCES "Sales" ("SaleId") ON DELETE CASCADE;
ALTER TABLE "SalesReturns"       ADD CONSTRAINT "FK_SalesReturns_Customer" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId");
ALTER TABLE "SalesReturns"       ADD CONSTRAINT "FK_SalesReturns_Location" FOREIGN KEY ("LocationId") REFERENCES "StorageLocations" ("LocationId");
ALTER TABLE "SalesReturns"       ADD CONSTRAINT "FK_SalesReturns_Sale"     FOREIGN KEY ("SaleId")     REFERENCES "Sales" ("SaleId");
ALTER TABLE "SalesReturnDetails" ADD CONSTRAINT "FK_SalesReturnDetails_Batch"       FOREIGN KEY ("BatchId")       REFERENCES "ItemBatches" ("BatchId");
ALTER TABLE "SalesReturnDetails" ADD CONSTRAINT "FK_SalesReturnDetails_Product"     FOREIGN KEY ("ItemId")        REFERENCES "ItemMaster" ("ItemId");
ALTER TABLE "SalesReturnDetails" ADD CONSTRAINT "FK_SalesReturnDetails_Return"      FOREIGN KEY ("SalesReturnId") REFERENCES "SalesReturns" ("SalesReturnId") ON DELETE CASCADE;
ALTER TABLE "SalesReturnDetails" ADD CONSTRAINT "FK_SalesReturnDetails_SalesDetail" FOREIGN KEY ("SalesDetailId") REFERENCES "SalesDetails" ("SalesDetailId");
ALTER TABLE "SalesReturnDetails" ADD CONSTRAINT "FK_SalesReturnDetails_Unit"        FOREIGN KEY ("UnitId")        REFERENCES "Units" ("UnitId");

-- Finance & system
ALTER TABLE "Payments"           ADD CONSTRAINT "FK_Payments_Customer"      FOREIGN KEY ("CustomerId")     REFERENCES "Customers" ("CustomerId");
ALTER TABLE "Payments"           ADD CONSTRAINT "FK_Payments_Mode"          FOREIGN KEY ("PaymentModeId")  REFERENCES "PaymentModes" ("PaymentModeId");
ALTER TABLE "Payments"           ADD CONSTRAINT "FK_Payments_Supplier"      FOREIGN KEY ("SupplierId")     REFERENCES "Suppliers" ("SupplierId");
ALTER TABLE "PaymentAllocations" ADD CONSTRAINT "FK_PaymentAllocations_Payment" FOREIGN KEY ("PaymentId")  REFERENCES "Payments" ("PaymentId") ON DELETE CASCADE;
ALTER TABLE "Expenses"           ADD CONSTRAINT "FK_Expenses_Category"      FOREIGN KEY ("ExpenseCategoryId") REFERENCES "ExpenseCategories" ("ExpenseCategoryId");
ALTER TABLE "Expenses"           ADD CONSTRAINT "FK_Expenses_PaymentMode"   FOREIGN KEY ("PaymentModeId")  REFERENCES "PaymentModes" ("PaymentModeId");
ALTER TABLE "CompanyProfile"     ADD CONSTRAINT "FK_CompanyProfile_State"   FOREIGN KEY ("StateId")        REFERENCES "States" ("StateId");
ALTER TABLE "NumberSeries"       ADD CONSTRAINT "FK_NumberSeries_FinancialYear" FOREIGN KEY ("FinancialYearId") REFERENCES "FinancialYears" ("FinancialYearId");
