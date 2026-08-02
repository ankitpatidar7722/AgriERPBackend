using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AgriERP.Persistence.Services;

/// <summary>
/// Wraps usp_PostStockTransaction and usp_ReverseDocumentStock.
///
/// The procedures run on the DbContext connection, so they join whatever
/// transaction the caller has open. Posting an invoice therefore writes the
/// header, the lines and every stock movement as one unit - or none of it.
/// </summary>
public class StockPostingService : IStockPostingService
{
    /// <summary>Raised by the procedure when a movement would take a batch negative.</summary>
    private const int InsufficientStockError = 50024;

    private readonly AgriErpDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public StockPostingService(AgriErpDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<long> PostAsync(StockMovement movement, CancellationToken ct = default)
    {
        var outputId = new SqlParameter("@StockTransactionId", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Output
        };

        var parameters = new object[]
        {
            new SqlParameter("@TransactionTypeId", SqlDbType.TinyInt) { Value = (byte)movement.TransactionType },
            new SqlParameter("@TransactionDate", SqlDbType.DateTime2) { Value = movement.TransactionDate },
            new SqlParameter("@ItemId", SqlDbType.Int) { Value = movement.ItemId },
            new SqlParameter("@BatchId", SqlDbType.BigInt) { Value = movement.BatchId },
            new SqlParameter("@LocationId", SqlDbType.Int) { Value = movement.LocationId },
            new SqlParameter("@Quantity", SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = movement.Quantity },
            new SqlParameter("@Rate", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = movement.Rate },
            new SqlParameter("@ReferenceType", SqlDbType.NVarChar, 30) { Value = movement.ReferenceType },
            new SqlParameter("@ReferenceId", SqlDbType.BigInt) { Value = movement.ReferenceId },
            new SqlParameter("@ReferenceDetailId", SqlDbType.BigInt)
                { Value = (object?)movement.ReferenceDetailId ?? DBNull.Value },
            new SqlParameter("@ReferenceNumber", SqlDbType.NVarChar, 30) { Value = movement.ReferenceNumber },
            new SqlParameter("@Remarks", SqlDbType.NVarChar, 300) { Value = (object?)movement.Remarks ?? DBNull.Value },
            new SqlParameter("@FinancialYearId", SqlDbType.Int) { Value = DBNull.Value },
            new SqlParameter("@UserId", SqlDbType.Int) { Value = (object?)_currentUser.UserId ?? DBNull.Value },
            outputId
        };

        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "EXEC usp_PostStockTransaction @TransactionTypeId, @TransactionDate, @ItemId, @BatchId, " +
                "@LocationId, @Quantity, @Rate, @ReferenceType, @ReferenceId, @ReferenceDetailId, " +
                "@ReferenceNumber, @Remarks, @FinancialYearId, @UserId, @StockTransactionId OUTPUT",
                parameters, ct);
        }
        catch (SqlException ex) when (ex.Number == InsufficientStockError)
        {
            // The procedure's message already names the item and the
            // shortfall, so it is surfaced verbatim rather than replaced with
            // something vaguer. This is a business rule, not a fault.
            throw new BusinessRuleException(ex.Message, "INSUFFICIENT_STOCK");
        }

        return outputId.Value is long id
            ? id
            : throw new InvalidOperationException("Stock posting did not return a transaction id.");
    }

    public async Task PostManyAsync(IEnumerable<StockMovement> movements, CancellationToken ct = default)
    {
        // Sequential, not parallel: they share one DbContext, and the whole
        // point is that they land in the caller's single transaction.
        foreach (var movement in movements)
            await PostAsync(movement, ct);
    }

    public async Task<int> ReverseDocumentAsync(
        string referenceType, long referenceId, string? remarks = null, CancellationToken ct = default)
    {
        var parameters = new object[]
        {
            new SqlParameter("@ReferenceType", SqlDbType.NVarChar, 30) { Value = referenceType },
            new SqlParameter("@ReferenceId", SqlDbType.BigInt) { Value = referenceId },
            new SqlParameter("@ReversalDate", SqlDbType.DateTime2) { Value = DBNull.Value },
            new SqlParameter("@Remarks", SqlDbType.NVarChar, 300) { Value = (object?)remarks ?? DBNull.Value },
            new SqlParameter("@UserId", SqlDbType.Int) { Value = (object?)_currentUser.UserId ?? DBNull.Value }
        };

        // The procedure returns a row count as a result set. ExecuteSqlRaw
        // discards result sets, which is fine - the count is informational and
        // the reversal itself either happened or the call threw.
        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_ReverseDocumentStock @ReferenceType, @ReferenceId, @ReversalDate, @Remarks, @UserId",
            parameters, ct);

        return await _context.StockTransactions
            .CountAsync(t => t.ReferenceType == referenceType
                             && t.ReferenceId == referenceId
                             && t.ReversesTransactionId != null, ct);
    }
}
