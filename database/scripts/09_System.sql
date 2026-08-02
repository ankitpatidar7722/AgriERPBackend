/*==============================================================================
  AgriERP  |  09_System.sql
  ------------------------------------------------------------------------------
  Shop profile, financial years, document numbering, settings and audit trail.

  DOCUMENT NUMBERING DESERVES A TABLE
  -----------------------------------
  Invoice numbers cannot have gaps and cannot repeat - a GST auditor will ask.
  Deriving the next number with MAX(InvoiceNumber)+1 breaks the moment two
  salesmen bill at the same instant: both read the same maximum and one insert
  fails, or worse, both succeed under different formats. NumberSeries holds
  the counter, and usp_GetNextDocumentNumber increments it under an UPDLOCK in
  a single atomic statement, so concurrent callers queue instead of colliding.
  Series reset per financial year, which is what the format PUR/2025-26/00042
  implies.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* CompanyProfile  - the shop itself                                       */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'CompanyProfile', N'U') IS NULL
BEGIN
    CREATE TABLE CompanyProfile
    (
        -- Not an IDENTITY: see CK_CompanyProfile_Singleton below.
        CompanyProfileId    INT             NOT NULL CONSTRAINT DF_CompanyProfile_Id DEFAULT (1),
        ShopName            NVARCHAR(150)   NOT NULL,
        LegalName           NVARCHAR(150)   NULL,
        GstNumber           VARCHAR(15)     NULL,
        PanNumber           VARCHAR(10)     NULL,
        -- Pesticide/seed/fertiliser dealer licences, printed on the invoice as
        -- required by the state agriculture department.
        PesticideLicenceNo  NVARCHAR(50)    NULL,
        SeedLicenceNo       NVARCHAR(50)    NULL,
        FertilizerLicenceNo NVARCHAR(50)    NULL,

        AddressLine1        NVARCHAR(200)   NULL,
        AddressLine2        NVARCHAR(200)   NULL,
        City                NVARCHAR(80)    NULL,
        -- Drives the CGST+SGST vs IGST decision on every document.
        StateId             INT             NULL,
        Pincode             VARCHAR(6)      NULL,
        Phone               NVARCHAR(15)    NULL,
        AlternatePhone      NVARCHAR(15)    NULL,
        Email               NVARCHAR(150)   NULL,
        Website             NVARCHAR(200)   NULL,

        BankName            NVARCHAR(120)   NULL,
        BankAccountNumber   NVARCHAR(30)    NULL,
        BankIfsc            VARCHAR(11)     NULL,
        BankBranch          NVARCHAR(120)   NULL,
        UpiId               NVARCHAR(100)   NULL,

        LogoPath            NVARCHAR(300)   NULL,
        InvoiceTerms        NVARCHAR(1000)  NULL,
        InvoiceFooterNote   NVARCHAR(500)   NULL,

        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,
        CONSTRAINT PK_CompanyProfile PRIMARY KEY CLUSTERED (CompanyProfileId),
        CONSTRAINT FK_CompanyProfile_State FOREIGN KEY (StateId) REFERENCES States (StateId),
        -- Exactly one shop. A second row would make "which GST number is ours?"
        -- ambiguous on every invoice. Pinning the primary key to a single legal
        -- value is the enforcement: SQL Server has no expression indexes, so the
        -- usual Postgres trick of a unique index on a constant is unavailable.
        CONSTRAINT CK_CompanyProfile_Singleton CHECK (CompanyProfileId = 1)
    );
END
GO

/*----------------------------------------------------------------------------*/
/* FinancialYears                                                          */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'FinancialYears', N'U') IS NULL
BEGIN
    CREATE TABLE FinancialYears
    (
        FinancialYearId INT             IDENTITY(1,1) NOT NULL,
        YearCode        NVARCHAR(10)    NOT NULL,      -- '2025-26'
        StartDate       DATE            NOT NULL,      -- 01-Apr
        EndDate         DATE            NOT NULL,      -- 31-Mar
        IsActive        BIT             NOT NULL CONSTRAINT DF_FinancialYears_IsActive DEFAULT (0),
        -- Once closed, no document may be posted into this year.
        IsClosed        BIT             NOT NULL CONSTRAINT DF_FinancialYears_IsClosed DEFAULT (0),
        ClosedAt        DATETIME2(3)    NULL,
        ClosedBy        INT             NULL,
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_FinancialYears_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_FinancialYears PRIMARY KEY CLUSTERED (FinancialYearId),
        CONSTRAINT UQ_FinancialYears_YearCode UNIQUE (YearCode),
        CONSTRAINT CK_FinancialYears_Dates CHECK (EndDate > StartDate)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_FinancialYears_Active
        ON FinancialYears (IsActive) WHERE IsActive = 1;
END
GO

/*----------------------------------------------------------------------------*/
/* NumberSeries                                                            */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'NumberSeries', N'U') IS NULL
BEGIN
    CREATE TABLE NumberSeries
    (
        NumberSeriesId  INT             IDENTITY(1,1) NOT NULL,
        DocumentType    NVARCHAR(30)    NOT NULL,      -- Sale, Purchase, Payment, ...
        FinancialYearId INT             NULL,          -- NULL = series never resets
        Prefix          NVARCHAR(20)    NOT NULL CONSTRAINT DF_NumberSeries_Prefix DEFAULT (N''),
        Suffix          NVARCHAR(20)    NOT NULL CONSTRAINT DF_NumberSeries_Suffix DEFAULT (N''),
        -- Optional middle segment, e.g. the year code in PUR/2025-26/00042.
        Separator       NVARCHAR(3)     NOT NULL CONSTRAINT DF_NumberSeries_Separator DEFAULT (N'/'),
        IncludeYearCode BIT             NOT NULL CONSTRAINT DF_NumberSeries_IncludeYearCode DEFAULT (1),
        CurrentNumber   INT             NOT NULL CONSTRAINT DF_NumberSeries_CurrentNumber DEFAULT (0),
        PaddingLength   TINYINT         NOT NULL CONSTRAINT DF_NumberSeries_PaddingLength DEFAULT (5),
        IsActive        BIT             NOT NULL CONSTRAINT DF_NumberSeries_IsActive DEFAULT (1),
        UpdatedAt       DATETIME2(3)    NULL,
        CONSTRAINT PK_NumberSeries PRIMARY KEY CLUSTERED (NumberSeriesId),
        CONSTRAINT FK_NumberSeries_FinancialYear
            FOREIGN KEY (FinancialYearId) REFERENCES FinancialYears (FinancialYearId),
        CONSTRAINT CK_NumberSeries_CurrentNumber CHECK (CurrentNumber >= 0),
        CONSTRAINT CK_NumberSeries_PaddingLength CHECK (PaddingLength BETWEEN 1 AND 12)
    );

    -- One live counter per document type per year; two would produce duplicates.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_NumberSeries_Type_Year
        ON NumberSeries (DocumentType, FinancialYearId);
END
GO

/*----------------------------------------------------------------------------*/
/* AppSettings                                                             */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'AppSettings', N'U') IS NULL
BEGIN
    CREATE TABLE AppSettings
    (
        SettingId       INT             IDENTITY(1,1) NOT NULL,
        SettingKey      NVARCHAR(80)    NOT NULL,
        SettingValue    NVARCHAR(500)   NULL,
        DataType        NVARCHAR(20)    NOT NULL CONSTRAINT DF_AppSettings_DataType DEFAULT ('string'),
        Category        NVARCHAR(40)    NULL,
        Description     NVARCHAR(300)   NULL,
        -- Settings the UI must not expose (internal toggles).
        IsSystem        BIT             NOT NULL CONSTRAINT DF_AppSettings_IsSystem DEFAULT (0),
        UpdatedAt       DATETIME2(3)    NULL,
        UpdatedBy       INT             NULL,
        CONSTRAINT PK_AppSettings PRIMARY KEY CLUSTERED (SettingId),
        CONSTRAINT UQ_AppSettings_SettingKey UNIQUE (SettingKey),
        CONSTRAINT CK_AppSettings_DataType CHECK (DataType IN ('string','int','decimal','bool','date','json'))
    );
END
GO

/*----------------------------------------------------------------------------*/
/* AuditLogs                                                               */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE AuditLogs
    (
        AuditLogId      BIGINT          IDENTITY(1,1) NOT NULL,
        -- Written by an EF Core SaveChanges interceptor, not by triggers:
        -- the interceptor knows the authenticated user and request id, which a
        -- trigger cannot see.
        TableName       NVARCHAR(120)   NOT NULL,
        PrimaryKeyValue NVARCHAR(60)    NULL,
        Action          NVARCHAR(10)    NOT NULL,      -- Insert | Update | Delete
        OldValues       NVARCHAR(MAX)   NULL,          -- JSON
        NewValues       NVARCHAR(MAX)   NULL,          -- JSON
        ChangedColumns  NVARCHAR(1000)  NULL,
        UserId          INT             NULL,
        UserName        NVARCHAR(60)    NULL,
        IpAddress       NVARCHAR(45)    NULL,
        RequestId       NVARCHAR(50)    NULL,
        LoggedAt        DATETIME2(3)    NOT NULL CONSTRAINT DF_AuditLogs_LoggedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AuditLogs PRIMARY KEY CLUSTERED (AuditLogId),
        CONSTRAINT CK_AuditLogs_Action CHECK (Action IN ('Insert','Update','Delete'))
    );

    CREATE NONCLUSTERED INDEX IX_AuditLogs_Table_Key
        ON AuditLogs (TableName, PrimaryKeyValue, LoggedAt DESC);

    CREATE NONCLUSTERED INDEX IX_AuditLogs_User_Date
        ON AuditLogs (UserId, LoggedAt DESC) INCLUDE (TableName, Action);

    CREATE NONCLUSTERED INDEX IX_AuditLogs_LoggedAt
        ON AuditLogs (LoggedAt);
END
GO

PRINT N'09_System.sql completed.';
GO
