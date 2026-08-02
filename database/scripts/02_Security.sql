/*==============================================================================
  AgriERP  |  02_Security.sql
  ------------------------------------------------------------------------------
  Users, roles, granular permissions and JWT refresh-token storage.

  Design notes
  ------------
  * Permissions are stored as codes ("Product.Create"), not booleans per screen.
    Adding a module later means inserting rows, never altering a table.
  * A user has one role. If the shop later needs a salesman who also does
    purchase entry, add UserPermissions as an override table rather than
    multi-role - it keeps "who can do what" answerable with one query.
  * Refresh tokens are stored HASHED. A leaked database backup must not hand
    the attacker live sessions.
  * CreatedBy / UpdatedBy columns across the database intentionally carry NO
    foreign key to Users. Eighty FKs into one table costs write throughput
    and buys little; the API always writes a valid UserId.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* Roles                                                                   */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Roles', N'U') IS NULL
BEGIN
    CREATE TABLE Roles
    (
        RoleId          INT             IDENTITY(1,1) NOT NULL,
        RoleName        NVARCHAR(50)    NOT NULL,
        NormalizedName  AS UPPER(RoleName) PERSISTED,
        Description     NVARCHAR(250)   NULL,
        -- System roles cannot be renamed or deleted from the UI.
        IsSystemRole    BIT             NOT NULL CONSTRAINT DF_Roles_IsSystemRole DEFAULT (0),
        IsActive        BIT             NOT NULL CONSTRAINT DF_Roles_IsActive     DEFAULT (1),
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_Roles_CreatedAt    DEFAULT (SYSUTCDATETIME()),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        UpdatedBy       INT             NULL,
        RowVersion      ROWVERSION      NOT NULL,
        CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED (RoleId),
        CONSTRAINT UQ_Roles_RoleName UNIQUE (RoleName)
    );
END
GO

/*----------------------------------------------------------------------------*/
/* Permissions                                                             */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Permissions', N'U') IS NULL
BEGIN
    CREATE TABLE Permissions
    (
        PermissionId    INT             IDENTITY(1,1) NOT NULL,
        -- "Module.Action", e.g. Product.Create / Sales.Delete / Report.Gst
        PermissionCode  NVARCHAR(100)   NOT NULL,
        Module          NVARCHAR(50)    NOT NULL,
        DisplayName     NVARCHAR(120)   NOT NULL,
        Description     NVARCHAR(250)   NULL,
        DisplayOrder    INT             NOT NULL CONSTRAINT DF_Permissions_DisplayOrder DEFAULT (0),
        CONSTRAINT PK_Permissions PRIMARY KEY CLUSTERED (PermissionId),
        CONSTRAINT UQ_Permissions_Code UNIQUE (PermissionCode)
    );

    CREATE NONCLUSTERED INDEX IX_Permissions_Module
        ON Permissions (Module, DisplayOrder);
END
GO

/*----------------------------------------------------------------------------*/
/* RolePermissions                                                         */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'RolePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE RolePermissions
    (
        RoleId          INT             NOT NULL,
        PermissionId    INT             NOT NULL,
        GrantedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_RolePermissions_GrantedAt DEFAULT (SYSUTCDATETIME()),
        GrantedBy       INT             NULL,
        CONSTRAINT PK_RolePermissions PRIMARY KEY CLUSTERED (RoleId, PermissionId),
        CONSTRAINT FK_RolePermissions_Role
            FOREIGN KEY (RoleId)       REFERENCES Roles (RoleId)             ON DELETE CASCADE,
        CONSTRAINT FK_RolePermissions_Permission
            FOREIGN KEY (PermissionId) REFERENCES Permissions (PermissionId) ON DELETE CASCADE
    );

    -- Serves "which roles grant this permission?" during permission maintenance.
    CREATE NONCLUSTERED INDEX IX_RolePermissions_PermissionId
        ON RolePermissions (PermissionId) INCLUDE (RoleId);
END
GO

/*----------------------------------------------------------------------------*/
/* Users                                                                   */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Users', N'U') IS NULL
BEGIN
    CREATE TABLE Users
    (
        UserId              INT             IDENTITY(1,1) NOT NULL,
        UserName            NVARCHAR(60)    NOT NULL,
        NormalizedUserName  AS UPPER(UserName) PERSISTED,
        Email               NVARCHAR(150)   NULL,
        NormalizedEmail     AS UPPER(Email)  PERSISTED,
        -- BCrypt hash produced by the API. Never a plain or reversible value.
        PasswordHash        NVARCHAR(300)   NOT NULL,
        -- Rotated on password/role change; lets the API invalidate live JWTs.
        SecurityStamp       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Users_SecurityStamp DEFAULT (NEWID()),
        FullName            NVARCHAR(120)   NOT NULL,
        Mobile              NVARCHAR(15)    NULL,
        RoleId              INT             NOT NULL,
        AvatarPath          NVARCHAR(300)   NULL,

        MustChangePassword  BIT             NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT (0),
        LastLoginAt         DATETIME2(3)    NULL,
        LastPasswordChangeAt DATETIME2(3)   NULL,

        -- Brute-force throttling, enforced by the API on failed logins.
        AccessFailedCount   INT             NOT NULL CONSTRAINT DF_Users_AccessFailedCount DEFAULT (0),
        LockoutEndAt        DATETIME2(3)    NULL,

        IsActive            BIT             NOT NULL CONSTRAINT DF_Users_IsActive   DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_Users_IsDeleted  DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Users_CreatedAt  DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT FK_Users_Role FOREIGN KEY (RoleId) REFERENCES Roles (RoleId)
    );

    -- Filtered so a deleted user's name can be reused.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Users_UserName
        ON Users (NormalizedUserName) WHERE IsDeleted = 0;

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Users_Email
        ON Users (NormalizedEmail) WHERE IsDeleted = 0 AND Email IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_Users_RoleId
        ON Users (RoleId) INCLUDE (FullName, IsActive) WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* UserRefreshTokens                                                       */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'UserRefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE UserRefreshTokens
    (
        RefreshTokenId  BIGINT          IDENTITY(1,1) NOT NULL,
        UserId          INT             NOT NULL,
        -- SHA-256 of the opaque token handed to the client.
        TokenHash       VARBINARY(32)   NOT NULL,
        ExpiresAt       DATETIME2(3)    NOT NULL,
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_UserRefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByIp     NVARCHAR(45)    NULL,          -- 45 chars covers IPv6
        UserAgent       NVARCHAR(300)   NULL,
        RevokedAt       DATETIME2(3)    NULL,
        RevokedByIp     NVARCHAR(45)    NULL,
        -- Set when this token was rotated; a reuse attempt on a rotated token
        -- indicates theft and should revoke the whole chain.
        ReplacedByTokenId BIGINT        NULL,
        CONSTRAINT PK_UserRefreshTokens PRIMARY KEY CLUSTERED (RefreshTokenId),
        CONSTRAINT FK_UserRefreshTokens_User
            FOREIGN KEY (UserId) REFERENCES Users (UserId) ON DELETE CASCADE
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_UserRefreshTokens_TokenHash
        ON UserRefreshTokens (TokenHash);

    CREATE NONCLUSTERED INDEX IX_UserRefreshTokens_UserId_Active
        ON UserRefreshTokens (UserId, ExpiresAt) WHERE RevokedAt IS NULL;
END
GO

/*----------------------------------------------------------------------------*/
/* UserPasswordResets                                                      */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'UserPasswordResets', N'U') IS NULL
BEGIN
    CREATE TABLE UserPasswordResets
    (
        PasswordResetId BIGINT          IDENTITY(1,1) NOT NULL,
        UserId          INT             NOT NULL,
        TokenHash       VARBINARY(32)   NOT NULL,
        ExpiresAt       DATETIME2(3)    NOT NULL,
        UsedAt          DATETIME2(3)    NULL,
        RequestedAt     DATETIME2(3)    NOT NULL CONSTRAINT DF_UserPasswordResets_RequestedAt DEFAULT (SYSUTCDATETIME()),
        RequestedByIp   NVARCHAR(45)    NULL,
        CONSTRAINT PK_UserPasswordResets PRIMARY KEY CLUSTERED (PasswordResetId),
        CONSTRAINT FK_UserPasswordResets_User
            FOREIGN KEY (UserId) REFERENCES Users (UserId) ON DELETE CASCADE
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_UserPasswordResets_TokenHash
        ON UserPasswordResets (TokenHash);
END
GO

PRINT N'02_Security.sql completed.';
GO
