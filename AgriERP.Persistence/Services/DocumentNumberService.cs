using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace AgriERP.Persistence.Services;

/// <summary>
/// Next-document-number routine, one provider on each side of the same contract:
///   SQL Server  -> usp_GetNextDocumentNumber (@.. OUTPUT)
///   PostgreSQL  -> fn_get_next_document_number($1, $2)
///
/// Both do the read-increment-return as ONE atomic statement, so two salesmen
/// pressing Save at the same instant are serialised by the engine and receive
/// consecutive numbers - a duplicate or missing invoice number is the first
/// thing a GST auditor finds. Runs on the DbContext connection and joins the
/// caller's open transaction, so a rolled-back invoice releases its number too.
/// </summary>
public class DocumentNumberService : IDocumentNumberService
{
    private readonly AgriErpDbContext _context;

    public DocumentNumberService(AgriErpDbContext context) => _context = context;

    public async Task<string> NextAsync(string documentType, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
            return await NextNpgsqlAsync(documentType, ct);

        // ---------------------------- SQL Server ----------------------------
        var documentTypeParameter = new SqlParameter("@DocumentType", SqlDbType.NVarChar, 30)
        {
            Value = documentType
        };

        var financialYearParameter = new SqlParameter("@FinancialYearId", SqlDbType.Int)
        {
            // NULL lets the procedure resolve the active financial year itself.
            Value = DBNull.Value
        };

        var documentNumberParameter = new SqlParameter("@DocumentNumber", SqlDbType.NVarChar, 30)
        {
            Direction = ParameterDirection.Output
        };

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_GetNextDocumentNumber @DocumentType, @FinancialYearId, @DocumentNumber OUTPUT",
            new object[] { documentTypeParameter, financialYearParameter, documentNumberParameter },
            ct);

        return documentNumberParameter.Value as string ?? throw NoSeries(documentType);
    }

    private async Task<string> NextNpgsqlAsync(string documentType, CancellationToken ct)
    {
        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            EnlistTransaction(command);
            command.CommandText =
                "SELECT fn_get_next_document_number(@DocumentType::text, @FinancialYearId::int)";
            AddParam(command, "DocumentType", documentType);
            AddParam(command, "FinancialYearId", DBNull.Value);

            return (await command.ExecuteScalarAsync(ct)) as string ?? throw NoSeries(documentType);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    public async Task<string?> PeekNextAsync(string documentType, CancellationToken ct = default)
    {
        // Read-only: CurrentNumber + 1 with no UPDATE, so the series is not
        // consumed. Used to show an indicative number on a create form; the real
        // number is still assigned atomically by NextAsync on save.
        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            EnlistTransaction(command);
            command.CommandText = _context.Database.IsNpgsql() ? PeekNpgsql : PeekSqlServer;
            AddParam(command, "DocumentType", documentType);

            return (await command.ExecuteScalarAsync(ct)) as string;
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    private const string PeekSqlServer = @"
SELECT TOP (1)
       ns.Prefix
     + CASE WHEN ns.IncludeYearCode = 1 AND fy.YearCode IS NOT NULL
            THEN ns.Separator + fy.YearCode ELSE N'' END
     + ns.Separator
     + RIGHT(REPLICATE(N'0', ns.PaddingLength) + CAST(ns.CurrentNumber + 1 AS NVARCHAR(12)), ns.PaddingLength)
     + ns.Suffix
FROM NumberSeries AS ns
LEFT JOIN FinancialYears AS fy
       ON fy.FinancialYearId = ISNULL(ns.FinancialYearId, (SELECT FinancialYearId FROM FinancialYears WHERE IsActive = 1))
WHERE ns.DocumentType = @DocumentType
  AND ns.IsActive = 1
  AND (ns.FinancialYearId = (SELECT FinancialYearId FROM FinancialYears WHERE IsActive = 1) OR ns.FinancialYearId IS NULL)
ORDER BY CASE WHEN ns.FinancialYearId = (SELECT FinancialYearId FROM FinancialYears WHERE IsActive = 1) THEN 0 ELSE 1 END,
         ns.NumberSeriesId;";

    private const string PeekNpgsql = @"
SELECT ns.""Prefix""
     || CASE WHEN ns.""IncludeYearCode"" AND fy.""YearCode"" IS NOT NULL
             THEN ns.""Separator"" || fy.""YearCode"" ELSE '' END
     || ns.""Separator""
     || right(repeat('0', ns.""PaddingLength"") || (ns.""CurrentNumber"" + 1)::text, ns.""PaddingLength"")
     || ns.""Suffix""
FROM ""NumberSeries"" AS ns
LEFT JOIN ""FinancialYears"" AS fy
       ON fy.""FinancialYearId"" = COALESCE(ns.""FinancialYearId"", (SELECT ""FinancialYearId"" FROM ""FinancialYears"" WHERE ""IsActive"" = true))
WHERE ns.""DocumentType"" = @DocumentType
  AND ns.""IsActive"" = true
  AND (ns.""FinancialYearId"" = (SELECT ""FinancialYearId"" FROM ""FinancialYears"" WHERE ""IsActive"" = true) OR ns.""FinancialYearId"" IS NULL)
ORDER BY CASE WHEN ns.""FinancialYearId"" = (SELECT ""FinancialYearId"" FROM ""FinancialYears"" WHERE ""IsActive"" = true) THEN 0 ELSE 1 END,
         ns.""NumberSeriesId""
LIMIT 1;";

    private void EnlistTransaction(DbCommand command)
    {
        if (_context.Database.CurrentTransaction is { } transaction)
            command.Transaction = transaction.GetDbTransaction();
    }

    private static void AddParam(DbCommand command, string name, object? value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        command.Parameters.Add(p);
    }

    private static InvalidOperationException NoSeries(string documentType)
        => new($"No active number series is configured for document type '{documentType}'.");
}
