/*==============================================================================
  AgriERP  |  13_Modules.sql
  ------------------------------------------------------------------------------
  ModuleMaster - the sidebar, as data.

  WHY THE MENU LIVES IN A TABLE
  -----------------------------
  The navigation used to be a TypeScript array shipped in the web bundle, so
  adding a screen - or just reordering two entries - meant a code change and a
  redeploy. Here one row is one sidebar entry: the route, the label, the group
  it sits under, and where it sorts. Inserting a row is the whole deployment.

  TWO ORDERING COLUMNS, NOT ONE
  -----------------------------
  ModuleHeadDisplayOrder sorts the GROUPS ("Masters" comes after "Trading");
  ModuleDisplayOrder sorts the items INSIDE a group. Both are needed - a single
  global sequence would force every row in a group to be renumbered whenever a
  group moved. SetGroupIndex carries the group's sequence on every member row so
  the API can group without a self-join.

  ROWS ARE RETIRED, NOT DELETED
  -----------------------------
  IsDeletedTransaction = 1 hides an entry. A hard DELETE would silently change
  what a role can reach with nothing left to explain why.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* ModuleMaster                                                            */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'ModuleMaster', N'U') IS NULL
BEGIN
    CREATE TABLE ModuleMaster
    (
        ModuleID                INT             IDENTITY(1,1) NOT NULL,
        -- The frontend route, stored with its leading slash: '/products'.
        ModuleName              NVARCHAR(200)   NOT NULL,
        -- What the sidebar prints for this entry.
        ModuleDisplayName       NVARCHAR(200)   NOT NULL,
        -- Internal group key ('Master'); stable even if the heading is retitled.
        ModuleHeadName          NVARCHAR(100)   NOT NULL,
        -- The heading the sidebar prints above the group.
        ModuleHeadDisplayName   NVARCHAR(100)   NOT NULL,
        -- Sorts the groups against each other.
        ModuleHeadDisplayOrder  INT             NOT NULL,
        -- Sorts the items within one group.
        ModuleDisplayOrder      INT             NOT NULL,
        -- The group's sequence, repeated on every member row.
        SetGroupIndex           INT             NOT NULL,
        -- Lucide React icon name, e.g. 'Package'. NULL falls back in the UI.
        IconName                NVARCHAR(100)   NULL,
        IsDeletedTransaction    BIT             NOT NULL
            CONSTRAINT DF_ModuleMaster_IsDeletedTransaction DEFAULT (0),
        CreatedDate             DATETIME        NOT NULL
            CONSTRAINT DF_ModuleMaster_CreatedDate DEFAULT (GETDATE()),
        CONSTRAINT PK_ModuleMaster PRIMARY KEY CLUSTERED (ModuleID)
    );

    -- One live entry per route. Two rows for '/products' would render the item
    -- twice, and filtered on IsDeletedTransaction so a retired row does not
    -- block re-adding the route later.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ModuleMaster_ModuleName
        ON ModuleMaster (ModuleName) WHERE IsDeletedTransaction = 0;

    -- Covers the only query this table serves: the whole live menu, in order.
    CREATE NONCLUSTERED INDEX IX_ModuleMaster_Display
        ON ModuleMaster (IsDeletedTransaction, ModuleHeadDisplayOrder, ModuleDisplayOrder)
        INCLUDE (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
                 SetGroupIndex, IconName);
END
GO

/*----------------------------------------------------------------------------*/
/* Seed - the menu exactly as it shipped hardcoded                             */
/*                                                                             */
/* Matched on ModuleName so a re-run leaves an edited label or reordered row    */
/* alone. This script is expected to be run again over a live database.        */
/*----------------------------------------------------------------------------*/
;WITH Seed (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
            ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName) AS
(
    SELECT * FROM (VALUES
        -- Overview
        (N'/dashboard',  N'Dashboard',  N'Overview',  N'Overview',  1, 1, 1, N'LayoutDashboard'),

        -- Trading: what the counter touches every day, in the order of the day.
        (N'/sales',      N'Sales',      N'Trading',   N'Trading',   2, 1, 2, N'Receipt'),
        (N'/purchases',  N'Purchases',  N'Trading',   N'Trading',   2, 2, 2, N'ShoppingCart'),
        (N'/payments',   N'Payments',   N'Trading',   N'Trading',   2, 3, 2, N'Wallet'),

        -- Inventory
        (N'/stock',      N'Stock',      N'Inventory', N'Inventory', 3, 1, 3, N'Warehouse'),
        (N'/reports',    N'Reports',    N'Inventory', N'Inventory', 3, 2, 3, N'BarChart3'),

        -- Masters: rarely touched, so they sit at the bottom.
        (N'/products',   N'Products',   N'Masters',   N'Masters',   4, 1, 4, N'Package'),
        (N'/categories', N'Categories', N'Masters',   N'Masters',   4, 2, 4, N'FolderTree'),
        (N'/companies',  N'Companies',  N'Masters',   N'Masters',   4, 3, 4, N'Building2'),
        (N'/suppliers',  N'Suppliers',  N'Masters',   N'Masters',   4, 4, 4, N'Truck'),
        (N'/customers',  N'Customers',  N'Masters',   N'Masters',   4, 5, 4, N'Users'),
        (N'/units',      N'Units',      N'Masters',   N'Masters',   4, 6, 4, N'Ruler')
    ) AS v (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
            ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName)
)
INSERT INTO ModuleMaster
    (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
     ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName)
SELECT s.ModuleName, s.ModuleDisplayName, s.ModuleHeadName, s.ModuleHeadDisplayName,
       s.ModuleHeadDisplayOrder, s.ModuleDisplayOrder, s.SetGroupIndex, s.IconName
FROM   Seed AS s
WHERE  NOT EXISTS (SELECT 1 FROM ModuleMaster AS m WHERE m.ModuleName = s.ModuleName);
GO

PRINT N'13_Modules.sql completed.';
GO
