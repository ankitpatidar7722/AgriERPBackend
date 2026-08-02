using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AgriERP.Persistence.Services;

/// <summary>
/// Thin wrapper over usp_GetNextDocumentNumber.
///
/// The counter increment stays in the database on purpose. Reading
/// CurrentNumber into C#, adding one and writing it back gives two salesmen
/// pressing Save at the same instant the same invoice number - and a duplicate
/// or missing invoice number is the first thing a GST auditor finds.
///
/// The procedure does read-and-increment in a single atomic UPDATE, so SQL
/// Server's own row lock serialises callers. Because this runs on the
/// DbContext connection, it also joins whatever transaction the caller has
/// open: if invoice posting rolls back, the number is released with it.
/// </summary>
public class DocumentNumberService : IDocumentNumberService
{
    private readonly AgriErpDbContext _context;

    public DocumentNumberService(AgriErpDbContext context) => _context = context;

    public async Task<string> NextAsync(string documentType, CancellationToken ct = default)
    {
        var documentTypeParameter = new SqlParameter("@DocumentType", SqlDbType.NVarChar, 30)
        {
            Value = documentType
        };

        var financialYearParameter = new SqlParameter("@FinancialYearId", SqlDbType.Int)
        {
            // NULL lets the procedure resolve the active financial year itself,
            // so callers never have to know which year is open.
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

        return documentNumberParameter.Value as string
            ?? throw new InvalidOperationException(
                $"No active number series is configured for document type '{documentType}'.");
    }

    public async Task<string?> PeekNextAsync(string documentType, CancellationToken ct = default)
    {
        // The same formatting as usp_GetNextDocumentNumber, but read-only:
        // CurrentNumber + 1 with no UPDATE, so the series is not consumed. Used
        // to show an indicative number on a create form; the real number is
        // still assigned atomically by the procedure on save.
        var documentTypeParameter = new SqlParameter("@DocumentType", SqlDbType.NVarChar, 30)
        {
            Value = documentType
        };

        var documentNumberParameter = new SqlParameter("@DocumentNumber", SqlDbType.NVarChar, 30)
        {
            Direction = ParameterDirection.Output
        };

        const string sql = @"
SELECT TOP (1) @DocumentNumber =
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

        await _context.Database.ExecuteSqlRawAsync(
            sql, new object[] { documentTypeParameter, documentNumberParameter }, ct);

        return documentNumberParameter.Value as string;
    }
}
