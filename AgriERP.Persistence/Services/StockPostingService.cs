using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Data;
using System.Data.Common;

namespace AgriERP.Persistence.Services;

/// <summary>
/// Wraps the stock-posting and reversal routines on both providers:
///   SQL Server  -> usp_PostStockTransaction / usp_ReverseDocumentStock
///   PostgreSQL  -> fn_post_stock_transaction / fn_reverse_document_stock
///
/// The routines run on the DbContext connection, so they join whatever
/// transaction the caller has open: posting an invoice writes the header, the
/// lines and every stock movement as one unit - or none of it.
/// </summary>
public class StockPostingService : IStockPostingService
{
    /// <summary>Raised by the SQL Server procedure when a movement would take a batch negative.</summary>
    private const int InsufficientStockError = 50024;

    /// <summary>The PostgreSQL SQLSTATE the function raises for the same condition.</summary>
    private const string InsufficientStockSqlState = "AG024";

    private readonly AgriErpDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public StockPostingService(AgriErpDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<long> PostAsync(StockMovement movement, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
            return await PostNpgsqlAsync(movement, ct);

        // ---------------------------- SQL Server ----------------------------
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
            // The procedure's message already names the item and the shortfall.
            throw new BusinessRuleException(ex.Message, "INSUFFICIENT_STOCK");
        }

        return outputId.Value is long id
            ? id
            : throw new InvalidOperationException("Stock posting did not return a transaction id.");
    }

    private async Task<long> PostNpgsqlAsync(StockMovement movement, CancellationToken ct)
    {
        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            EnlistTransaction(command);
            command.CommandText =
                "SELECT fn_post_stock_transaction(" +
                "@TransactionTypeId::smallint, @TransactionDate::timestamp, @ItemId::int, @BatchId::bigint, " +
                "@LocationId::int, @Quantity::numeric, @Rate::numeric, @ReferenceType::text, @ReferenceId::bigint, " +
                "@ReferenceDetailId::bigint, @ReferenceNumber::text, @Remarks::text, @FinancialYearId::int, @UserId::int)";

            AddParam(command, "TransactionTypeId", (short)(byte)movement.TransactionType);
            AddParam(command, "TransactionDate", DateTime.SpecifyKind(movement.TransactionDate, DateTimeKind.Unspecified));
            AddParam(command, "ItemId", movement.ItemId);
            AddParam(command, "BatchId", movement.BatchId);
            AddParam(command, "LocationId", movement.LocationId);
            AddParam(command, "Quantity", movement.Quantity);
            AddParam(command, "Rate", movement.Rate);
            AddParam(command, "ReferenceType", movement.ReferenceType);
            AddParam(command, "ReferenceId", movement.ReferenceId);
            AddParam(command, "ReferenceDetailId", (object?)movement.ReferenceDetailId ?? DBNull.Value);
            AddParam(command, "ReferenceNumber", movement.ReferenceNumber);
            AddParam(command, "Remarks", (object?)movement.Remarks ?? DBNull.Value);
            AddParam(command, "FinancialYearId", DBNull.Value);
            AddParam(command, "UserId", (object?)_currentUser.UserId ?? DBNull.Value);

            var result = await command.ExecuteScalarAsync(ct);
            return result is long id
                ? id
                : throw new InvalidOperationException("Stock posting did not return a transaction id.");
        }
        catch (PostgresException ex) when (ex.SqlState == InsufficientStockSqlState)
        {
            throw new BusinessRuleException(ex.MessageText, "INSUFFICIENT_STOCK");
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
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
        if (_context.Database.IsNpgsql())
            await ReverseNpgsqlAsync(referenceType, referenceId, remarks, ct);
        else
            await ReverseSqlServerAsync(referenceType, referenceId, remarks, ct);

        // The reversal row count is read back the same way on both providers.
        return await _context.StockTransactions
            .CountAsync(t => t.ReferenceType == referenceType
                             && t.ReferenceId == referenceId
                             && t.ReversesTransactionId != null, ct);
    }

    private async Task ReverseSqlServerAsync(
        string referenceType, long referenceId, string? remarks, CancellationToken ct)
    {
        var parameters = new object[]
        {
            new SqlParameter("@ReferenceType", SqlDbType.NVarChar, 30) { Value = referenceType },
            new SqlParameter("@ReferenceId", SqlDbType.BigInt) { Value = referenceId },
            new SqlParameter("@ReversalDate", SqlDbType.DateTime2) { Value = DBNull.Value },
            new SqlParameter("@Remarks", SqlDbType.NVarChar, 300) { Value = (object?)remarks ?? DBNull.Value },
            new SqlParameter("@UserId", SqlDbType.Int) { Value = (object?)_currentUser.UserId ?? DBNull.Value }
        };

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_ReverseDocumentStock @ReferenceType, @ReferenceId, @ReversalDate, @Remarks, @UserId",
            parameters, ct);
    }

    private async Task ReverseNpgsqlAsync(
        string referenceType, long referenceId, string? remarks, CancellationToken ct)
    {
        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            EnlistTransaction(command);
            command.CommandText =
                "SELECT fn_reverse_document_stock(" +
                "@ReferenceType::text, @ReferenceId::bigint, @ReversalDate::timestamp, @Remarks::text, @UserId::int)";
            AddParam(command, "ReferenceType", referenceType);
            AddParam(command, "ReferenceId", referenceId);
            AddParam(command, "ReversalDate", DBNull.Value);
            AddParam(command, "Remarks", (object?)remarks ?? DBNull.Value);
            AddParam(command, "UserId", (object?)_currentUser.UserId ?? DBNull.Value);
            await command.ExecuteScalarAsync(ct);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

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
}
