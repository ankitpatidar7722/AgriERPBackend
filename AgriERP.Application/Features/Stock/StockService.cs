using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Stock.Dtos;
using AgriERP.Domain.Entities.Inventory;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Enums;
using AgriERP.Domain.ReadModels;
using AgriERP.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Stock;

public interface IStockService
{
    Task<PagedResult<StockLedgerLineDto>> GetLedgerAsync(StockLedgerQueryParameters parameters, CancellationToken ct = default);
    Task<IReadOnlyList<BatchStockView>> GetBatchStockAsync(int? itemId, int? locationId, CancellationToken ct = default);

    Task<StockAdjustmentDto> CreateOpeningStockAsync(OpeningStockRequest request, CancellationToken ct = default);

    Task<PagedResult<StockAdjustmentDto>> GetAdjustmentsAsync(StockAdjustmentQueryParameters parameters, CancellationToken ct = default);
    Task<StockAdjustmentDto> GetAdjustmentAsync(long id, CancellationToken ct = default);
    Task<StockAdjustmentDto> CreateAdjustmentAsync(SaveStockAdjustmentRequest request, CancellationToken ct = default);
    Task<StockAdjustmentDto> PostAdjustmentAsync(long id, CancellationToken ct = default);

    Task<PagedResult<StockTransferDto>> GetTransfersAsync(StockTransferQueryParameters parameters, CancellationToken ct = default);
    Task<StockTransferDto> GetTransferAsync(long id, CancellationToken ct = default);
    Task<StockTransferDto> CreateTransferAsync(SaveStockTransferRequest request, CancellationToken ct = default);
    Task<StockTransferDto> PostTransferAsync(long id, CancellationToken ct = default);
}

public class StockService : IStockService
{
    private readonly IUnitOfWork _uow;
    private readonly IStockPostingService _posting;
    private readonly IDocumentNumberService _numbers;
    private readonly IDateTimeProvider _clock;

    public StockService(
        IUnitOfWork uow,
        IStockPostingService posting,
        IDocumentNumberService numbers,
        IDateTimeProvider clock)
    {
        _uow = uow;
        _posting = posting;
        _numbers = numbers;
        _clock = clock;
    }

    /* ============================ ledger ============================ */

    public async Task<PagedResult<StockLedgerLineDto>> GetLedgerAsync(
        StockLedgerQueryParameters parameters, CancellationToken ct = default)
    {
        var query = _uow.Repository<StockLedgerView>().Query()
            .WhereIf(parameters.ItemId.HasValue, l => l.ItemId == parameters.ItemId)
            .WhereIf(parameters.BatchId.HasValue, l => l.BatchId == parameters.BatchId)
            .WhereIf(parameters.LocationId.HasValue, l => l.LocationId == parameters.LocationId)
            .WhereIf(parameters.FromDate.HasValue, l => l.TransactionDate >= parameters.FromDate!.Value)
            // Inclusive end date: a user asking for "to 31-Mar" means the whole
            // of the 31st, and TransactionDate carries a time component.
            .WhereIf(parameters.ToDate.HasValue, l => l.TransactionDate < parameters.ToDate!.Value.Date.AddDays(1))
            .WhereIf(parameters.TransactionType.HasValue,
                     l => l.TransactionTypeId == (byte)parameters.TransactionType!.Value);

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<StockLedgerLineDto>.Empty(parameters.Page, parameters.PageSize);

        // Newest first for on-screen review; the running balance in the view is
        // still computed in journal order, so it stays meaningful either way.
        var items = await query
            .OrderByDescending(l => l.TransactionDate)
            .ThenByDescending(l => l.StockTransactionId)
            .Skip(parameters.Skip)
            .Take(parameters.PageSize)
            .Select(l => new StockLedgerLineDto
            {
                StockTransactionId  = l.StockTransactionId,
                TransactionDate     = l.TransactionDate,
                TransactionTypeName = l.TransactionTypeName,
                ItemId           = l.ItemId,
                ItemName         = l.ItemName,
                BatchNumber         = l.BatchNumber,
                ExpiryDate          = l.ExpiryDate,
                LocationName        = l.LocationName,
                UnitCode            = l.UnitCode,
                InwardQty           = l.InwardQty,
                OutwardQty          = l.OutwardQty,
                Rate                = l.Rate,
                Value               = l.Value,
                RunningBalance      = l.RunningBalance,
                ReferenceType       = l.ReferenceType,
                ReferenceNumber     = l.ReferenceNumber,
                Remarks             = l.Remarks
            })
            .ToListAsync(ct);

        return PagedResult<StockLedgerLineDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<BatchStockView>> GetBatchStockAsync(
        int? itemId, int? locationId, CancellationToken ct = default)
        => await _uow.Repository<BatchStockView>().Query()
            .WhereIf(itemId.HasValue, b => b.ItemId == itemId)
            .WhereIf(locationId.HasValue, b => b.LocationId == locationId)
            .Where(b => b.CurrentQty != 0)
            .OrderBy(b => b.ItemName)
            .ThenBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ToListAsync(ct);

    /* ============================ opening stock ============================ */

    /// <summary>
    /// Loads the shop's starting stock as a posted adjustment, so day one is a
    /// journal entry like everything else. Without it, the first sale would
    /// have no batch to come from and no cost to report profit against.
    /// </summary>
    public async Task<StockAdjustmentDto> CreateOpeningStockAsync(
        OpeningStockRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        var adjustmentId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var adjustment = new StockAdjustment
            {
                AdjustmentNumber = await _numbers.NextAsync(DocumentType.StockAdjustment, token),
                AdjustmentDate   = request.EntryDate.Date,
                AdjustmentType   = StockAdjustmentType.Opening,
                LocationId       = locationId,
                Reason           = "Opening stock",
                Remarks          = request.Remarks,
                Status           = DocumentStatus.Draft
            };

            await _uow.Repository<StockAdjustment>().AddAsync(adjustment, token);
            await _uow.SaveChangesAsync(token);

            var lineNumber = 0;
            decimal totalIncrease = 0m, totalValue = 0m;

            foreach (var line in request.Lines)
            {
                if (line.Quantity <= 0)
                    throw new ValidationException(nameof(line.Quantity), "Opening quantity must be greater than zero.");

                var batch = await ResolveBatchAsync(
                    line.ItemId, line.BatchNumber, locationId,
                    line.ManufacturingDate, line.ExpiryDate,
                    line.PurchaseRate, line.Mrp, line.SellingRate, token);

                await _uow.Repository<StockAdjustmentDetail>().AddAsync(new StockAdjustmentDetail
                {
                    AdjustmentId = adjustment.AdjustmentId,
                    LineNumber   = ++lineNumber,
                    ItemId    = line.ItemId,
                    BatchId      = batch.BatchId,
                    // System quantity is zero by definition: this IS the opening
                    // position, so the whole amount is the difference.
                    SystemQty    = 0m,
                    PhysicalQty  = line.Quantity,
                    Rate         = line.PurchaseRate,
                    Reason       = "Opening stock"
                }, token);

                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.OpeningStock,
                    request.EntryDate,
                    line.ItemId,
                    batch.BatchId,
                    locationId,
                    line.Quantity,
                    line.PurchaseRate,
                    StockReferenceType.Adjustment,
                    adjustment.AdjustmentId,
                    null,
                    adjustment.AdjustmentNumber,
                    "Opening stock"), token);

                totalIncrease += line.Quantity;
                totalValue += line.Quantity * line.PurchaseRate;
            }

            adjustment.TotalIncreaseQty = totalIncrease;
            adjustment.TotalValueImpact = Math.Round(totalValue, 2, MidpointRounding.AwayFromZero);
            adjustment.Status = DocumentStatus.Posted;
            adjustment.PostedAt = _clock.UtcNow;

            return adjustment.AdjustmentId;
        }, ct);

        return await GetAdjustmentAsync(adjustmentId, ct);
    }

    /* ============================ adjustments ============================ */

    public async Task<PagedResult<StockAdjustmentDto>> GetAdjustmentsAsync(
        StockAdjustmentQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<StockAdjustment>().Query()
            .WhereIf(parameters.FromDate.HasValue, a => a.AdjustmentDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, a => a.AdjustmentDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.AdjustmentType.HasValue, a => a.AdjustmentType == parameters.AdjustmentType!.Value)
            .WhereIf(parameters.Status.HasValue, a => a.Status == parameters.Status!.Value)
            .WhereIf(parameters.LocationId.HasValue, a => a.LocationId == parameters.LocationId)
            .WhereIf(search is not null, a =>
                a.AdjustmentNumber.Contains(search!) ||
                (a.Reason != null && a.Reason.Contains(search!)));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<StockAdjustmentDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(a => a.AdjustmentDate).ThenByDescending(a => a.AdjustmentId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(a => new StockAdjustmentDto
            {
                AdjustmentId     = a.AdjustmentId,
                AdjustmentNumber = a.AdjustmentNumber,
                AdjustmentDate   = a.AdjustmentDate,
                AdjustmentType   = a.AdjustmentType,
                LocationId       = a.LocationId,
                LocationName     = a.Location!.LocationName,
                Reason           = a.Reason,
                Remarks          = a.Remarks,
                TotalIncreaseQty = a.TotalIncreaseQty,
                TotalDecreaseQty = a.TotalDecreaseQty,
                TotalValueImpact = a.TotalValueImpact,
                Status           = a.Status,
                PostedAt         = a.PostedAt,
                CreatedAt        = a.CreatedAt
            })
            .ToListAsync(ct);

        return PagedResult<StockAdjustmentDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<StockAdjustmentDto> GetAdjustmentAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<StockAdjustment>().Query()
            .Where(a => a.AdjustmentId == id)
            .Select(a => new StockAdjustmentDto
            {
                AdjustmentId     = a.AdjustmentId,
                AdjustmentNumber = a.AdjustmentNumber,
                AdjustmentDate   = a.AdjustmentDate,
                AdjustmentType   = a.AdjustmentType,
                LocationId       = a.LocationId,
                LocationName     = a.Location!.LocationName,
                Reason           = a.Reason,
                Remarks          = a.Remarks,
                TotalIncreaseQty = a.TotalIncreaseQty,
                TotalDecreaseQty = a.TotalDecreaseQty,
                TotalValueImpact = a.TotalValueImpact,
                Status           = a.Status,
                PostedAt         = a.PostedAt,
                CreatedAt        = a.CreatedAt
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Stock adjustment", id);

        dto.Lines = await _uow.Repository<StockAdjustmentDetail>().Query()
            .Where(d => d.AdjustmentId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new StockAdjustmentLineDto
            {
                AdjustmentDetailId = d.AdjustmentDetailId,
                ItemId          = d.ItemId,
                ItemName        = d.Item!.ItemName,
                BatchId            = d.BatchId,
                BatchNumber        = d.Batch!.BatchNumber,
                ExpiryDate         = d.Batch.ExpiryDate,
                SystemQty          = d.SystemQty,
                PhysicalQty        = d.PhysicalQty,
                DifferenceQty      = d.DifferenceQty,
                Rate               = d.Rate,
                ValueImpact        = d.ValueImpact,
                WarehouseId        = d.WarehouseId,
                WarehouseName      = d.Warehouse != null ? d.Warehouse.WarehouseName : null,
                BinName            = d.BinName,
                Reason             = d.Reason
            })
            .ToListAsync(ct);

        return dto;
    }

    public async Task<StockAdjustmentDto> CreateAdjustmentAsync(
        SaveStockAdjustmentRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        var adjustment = new StockAdjustment
        {
            AdjustmentNumber = await _numbers.NextAsync(DocumentType.StockAdjustment, ct),
            AdjustmentDate   = request.AdjustmentDate.Date,
            AdjustmentType   = request.AdjustmentType,
            LocationId       = locationId,
            Reason           = request.Reason,
            Remarks          = request.Remarks,
            // Created as a draft. A physical count is entered over hours and
            // must not move stock until someone signs it off.
            Status           = DocumentStatus.Draft
        };

        await _uow.Repository<StockAdjustment>().AddAsync(adjustment, ct);
        await _uow.SaveChangesAsync(ct);

        var lineNumber = 0;

        foreach (var line in request.Lines)
        {
            ItemBatch batch;
            decimal rate;

            if (line.BatchId is long existingBatchId)
            {
                batch = await _uow.Repository<ItemBatch>()
                    .FirstOrDefaultAsync(b => b.BatchId == existingBatchId, tracking: false, ct)
                    ?? throw new NotFoundException("Batch", existingBatchId);
                rate = batch.PurchaseRate;
            }
            else
            {
                // New-stock line: physically counted stock the system did not
                // know about. Resolve/create the batch so the count posts as a
                // rise from a system quantity of zero.
                if (line.ItemId is null)
                    throw new ValidationException(nameof(line.ItemId),
                        "An item is required on a new-stock line.");

                batch = await ResolveBatchAsync(
                    line.ItemId.Value, line.BatchNumber, locationId,
                    line.ManufacturingDate, line.ExpiryDate,
                    line.Rate ?? 0m, line.Mrp ?? 0m, line.SellingRate ?? 0m, ct);
                // A blank/zero rate means "count only" - value the variance at the
                // batch's own rate rather than wiping it to zero.
                rate = line.Rate is > 0m ? line.Rate!.Value : batch.PurchaseRate;
            }

            await _uow.Repository<StockAdjustmentDetail>().AddAsync(new StockAdjustmentDetail
            {
                AdjustmentId = adjustment.AdjustmentId,
                LineNumber   = ++lineNumber,
                ItemId    = batch.ItemId,
                BatchId      = batch.BatchId,
                // Snapshot of what the system believed at entry time. Kept so
                // the variance report can show what actually changed, even if
                // stock moved again before the count was posted.
                SystemQty    = batch.CurrentQty,
                PhysicalQty  = line.PhysicalQty,
                Rate         = rate,
                WarehouseId  = line.WarehouseId,
                BinName      = string.IsNullOrWhiteSpace(line.BinName) ? null : line.BinName.Trim(),
                Reason       = line.Reason
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);
        await RecalculateAdjustmentTotalsAsync(adjustment.AdjustmentId, ct);

        return await GetAdjustmentAsync(adjustment.AdjustmentId, ct);
    }

    public async Task<StockAdjustmentDto> PostAdjustmentAsync(long id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var adjustment = await _uow.Repository<StockAdjustment>()
                .FirstOrDefaultAsync(a => a.AdjustmentId == id, tracking: true, token)
                ?? throw new NotFoundException("Stock adjustment", id);

            if (adjustment.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"Adjustment {adjustment.AdjustmentNumber} is already {adjustment.Status}.",
                    "NOT_DRAFT");

            var lines = await _uow.Repository<StockAdjustmentDetail>().Query()
                .Where(d => d.AdjustmentId == id)
                .ToListAsync(token);

            if (lines.Count == 0)
                throw new BusinessRuleException("Cannot post an adjustment with no lines.", "NO_LINES");

            foreach (var line in lines)
            {
                // Re-read the batch: stock may have moved between drafting the
                // count and posting it, and the movement must be relative to
                // what is there NOW, not what was there at entry time.
                var currentQty = await _uow.Repository<ItemBatch>().Query()
                    .Where(b => b.BatchId == line.BatchId)
                    .Select(b => b.CurrentQty)
                    .FirstAsync(token);

                var difference = line.PhysicalQty - currentQty;

                if (difference == 0) continue;

                await _posting.PostAsync(new StockMovement(
                    difference > 0 ? StockTransactionTypeId.AdjustmentIn : StockTransactionTypeId.AdjustmentOut,
                    adjustment.AdjustmentDate,
                    line.ItemId,
                    line.BatchId,
                    adjustment.LocationId,
                    Math.Abs(difference),
                    line.Rate,
                    StockReferenceType.Adjustment,
                    adjustment.AdjustmentId,
                    line.AdjustmentDetailId,
                    adjustment.AdjustmentNumber,
                    line.Reason ?? adjustment.Reason), token);
            }

            adjustment.Status = DocumentStatus.Posted;
            adjustment.PostedAt = _clock.UtcNow;

            return null;
        }, ct);

        await RecalculateAdjustmentTotalsAsync(id, ct);
        return await GetAdjustmentAsync(id, ct);
    }

    /* ============================ transfers ============================ */

    public async Task<PagedResult<StockTransferDto>> GetTransfersAsync(
        StockTransferQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<StockTransfer>().Query()
            .WhereIf(parameters.FromDate.HasValue, t => t.TransferDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, t => t.TransferDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.Status.HasValue, t => t.Status == parameters.Status!.Value)
            .WhereIf(parameters.FromLocationId.HasValue, t => t.FromLocationId == parameters.FromLocationId)
            .WhereIf(search is not null, t => t.TransferNumber.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<StockTransferDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(t => t.TransferDate).ThenByDescending(t => t.TransferId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(t => new StockTransferDto
            {
                TransferId       = t.TransferId,
                TransferNumber   = t.TransferNumber,
                TransferDate     = t.TransferDate,
                FromLocationId   = t.FromLocationId,
                FromLocationName = t.FromLocation!.LocationName,
                ToLocationId     = t.ToLocationId,
                ToLocationName   = t.ToLocation!.LocationName,
                TotalQty         = t.TotalQty,
                TotalValue       = t.TotalValue,
                Status           = t.Status,
                PostedAt         = t.PostedAt,
                Remarks          = t.Remarks
            })
            .ToListAsync(ct);

        return PagedResult<StockTransferDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<StockTransferDto> GetTransferAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<StockTransfer>().Query()
            .Where(t => t.TransferId == id)
            .Select(t => new StockTransferDto
            {
                TransferId       = t.TransferId,
                TransferNumber   = t.TransferNumber,
                TransferDate     = t.TransferDate,
                FromLocationId   = t.FromLocationId,
                FromLocationName = t.FromLocation!.LocationName,
                ToLocationId     = t.ToLocationId,
                ToLocationName   = t.ToLocation!.LocationName,
                TotalQty         = t.TotalQty,
                TotalValue       = t.TotalValue,
                Status           = t.Status,
                PostedAt         = t.PostedAt,
                Remarks          = t.Remarks
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Stock transfer", id);

        dto.Lines = await _uow.Repository<StockTransferDetail>().Query()
            .Where(d => d.TransferId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new StockTransferLineDto
            {
                TransferDetailId = d.TransferDetailId,
                ItemId        = d.ItemId,
                ItemName      = d.Item!.ItemName,
                FromBatchId      = d.FromBatchId,
                ToBatchId        = d.ToBatchId,
                BatchNumber      = d.FromBatch!.BatchNumber,
                Quantity         = d.Quantity,
                Rate             = d.Rate,
                LineValue        = d.LineValue
            })
            .ToListAsync(ct);

        return dto;
    }

    public async Task<StockTransferDto> CreateTransferAsync(
        SaveStockTransferRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        if (request.FromLocationId == request.ToLocationId)
            throw new ValidationException(nameof(request.ToLocationId),
                "Source and destination locations must be different.");

        var transfer = new StockTransfer
        {
            TransferNumber = await _numbers.NextAsync(DocumentType.StockTransfer, ct),
            TransferDate   = request.TransferDate.Date,
            FromLocationId = request.FromLocationId,
            ToLocationId   = request.ToLocationId,
            Remarks        = request.Remarks,
            Status         = DocumentStatus.Draft
        };

        await _uow.Repository<StockTransfer>().AddAsync(transfer, ct);
        await _uow.SaveChangesAsync(ct);

        var lineNumber = 0;
        decimal totalQty = 0m, totalValue = 0m;

        foreach (var line in request.Lines)
        {
            var batch = await _uow.Repository<ItemBatch>()
                .FirstOrDefaultAsync(b => b.BatchId == line.FromBatchId, tracking: false, ct)
                ?? throw new NotFoundException("Batch", line.FromBatchId);

            if (batch.LocationId != request.FromLocationId)
                throw new ValidationException(nameof(line.FromBatchId),
                    $"Batch {batch.BatchNumber} is not held at the source location.");

            if (line.Quantity <= 0)
                throw new ValidationException(nameof(line.Quantity), "Transfer quantity must be greater than zero.");

            await _uow.Repository<StockTransferDetail>().AddAsync(new StockTransferDetail
            {
                TransferId  = transfer.TransferId,
                LineNumber  = ++lineNumber,
                ItemId   = batch.ItemId,
                FromBatchId = batch.BatchId,
                Quantity    = line.Quantity,
                Rate        = batch.PurchaseRate,
                Remarks     = line.Remarks
            }, ct);

            totalQty += line.Quantity;
            totalValue += line.Quantity * batch.PurchaseRate;
        }

        transfer.TotalQty = totalQty;
        transfer.TotalValue = Math.Round(totalValue, 2, MidpointRounding.AwayFromZero);
        await _uow.SaveChangesAsync(ct);

        return await GetTransferAsync(transfer.TransferId, ct);
    }

    public async Task<StockTransferDto> PostTransferAsync(long id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var transfer = await _uow.Repository<StockTransfer>()
                .FirstOrDefaultAsync(t => t.TransferId == id, tracking: true, token)
                ?? throw new NotFoundException("Stock transfer", id);

            if (transfer.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"Transfer {transfer.TransferNumber} is already {transfer.Status}.", "NOT_DRAFT");

            var lines = await _uow.Repository<StockTransferDetail>().Query(tracking: true)
                .Where(d => d.TransferId == id)
                .ToListAsync(token);

            if (lines.Count == 0)
                throw new BusinessRuleException("Cannot post a transfer with no lines.", "NO_LINES");

            foreach (var line in lines)
            {
                var source = await _uow.Repository<ItemBatch>()
                    .FirstOrDefaultAsync(b => b.BatchId == line.FromBatchId, tracking: false, token)
                    ?? throw new NotFoundException("Batch", line.FromBatchId);

                // The destination gets its own batch row carrying the SAME batch
                // number, expiry and cost. Moving a shelf does not change what
                // the goods are or what they cost.
                var destination = await ResolveBatchAsync(
                    source.ItemId, source.BatchNumber, transfer.ToLocationId,
                    source.ManufacturingDate, source.ExpiryDate,
                    source.PurchaseRate, source.Mrp, source.SellingRate, token);

                line.ToBatchId = destination.BatchId;

                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.TransferOut, transfer.TransferDate, line.ItemId,
                    source.BatchId, transfer.FromLocationId, line.Quantity, line.Rate,
                    StockReferenceType.Transfer, transfer.TransferId, line.TransferDetailId,
                    transfer.TransferNumber, "Transfer out"), token);

                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.TransferIn, transfer.TransferDate, line.ItemId,
                    destination.BatchId, transfer.ToLocationId, line.Quantity, line.Rate,
                    StockReferenceType.Transfer, transfer.TransferId, line.TransferDetailId,
                    transfer.TransferNumber, "Transfer in"), token);
            }

            transfer.Status = DocumentStatus.Posted;
            transfer.PostedAt = _clock.UtcNow;

            return null;
        }, ct);

        return await GetTransferAsync(id, ct);
    }

    /* ============================ helpers ============================ */

    /// <summary>
    /// Finds the batch for (item, batch number, location) or creates it.
    /// Batch numbers repeat across consignments of the same lot, so buying the
    /// same batch twice must add to the existing row rather than split the
    /// stock figure across two.
    /// </summary>
    private async Task<ItemBatch> ResolveBatchAsync(
        int itemId, string? batchNumber, int locationId,
        DateTime? manufacturingDate, DateTime? expiryDate,
        decimal purchaseRate, decimal mrp, decimal sellingRate,
        CancellationToken ct)
    {
        var number = string.IsNullOrWhiteSpace(batchNumber) ? "GEN" : batchNumber.Trim();

        var existing = await _uow.Repository<ItemBatch>()
            .FirstOrDefaultAsync(
                b => b.ItemId == itemId && b.BatchNumber == number && b.LocationId == locationId,
                tracking: true, ct);

        if (existing is not null)
        {
            // Rates refresh to the latest consignment; dates do not overwrite
            // what is already recorded, because the earlier entry is the one
            // that matches the physical goods on the shelf.
            if (purchaseRate > 0) existing.PurchaseRate = purchaseRate;
            if (mrp > 0) existing.Mrp = mrp;
            if (sellingRate > 0) existing.SellingRate = sellingRate;
            existing.ManufacturingDate ??= manufacturingDate;
            existing.ExpiryDate ??= expiryDate;
            return existing;
        }

        var batch = new ItemBatch
        {
            ItemId         = itemId,
            BatchNumber       = number,
            LocationId        = locationId,
            ManufacturingDate = manufacturingDate,
            ExpiryDate        = expiryDate,
            PurchaseRate      = purchaseRate,
            Mrp               = mrp,
            SellingRate       = sellingRate
        };

        await _uow.Repository<ItemBatch>().AddAsync(batch, ct);
        await _uow.SaveChangesAsync(ct);   // needed so BatchId is available to the caller

        return batch;
    }

    private async Task RecalculateAdjustmentTotalsAsync(long adjustmentId, CancellationToken ct)
    {
        var adjustment = await _uow.Repository<StockAdjustment>()
            .FirstOrDefaultAsync(a => a.AdjustmentId == adjustmentId, tracking: true, ct);

        if (adjustment is null) return;

        var totals = await _uow.Repository<StockAdjustmentDetail>().Query()
            .Where(d => d.AdjustmentId == adjustmentId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Increase = g.Sum(d => d.DifferenceQty > 0 ? d.DifferenceQty : 0m),
                Decrease = g.Sum(d => d.DifferenceQty < 0 ? -d.DifferenceQty : 0m),
                Value    = g.Sum(d => d.ValueImpact)
            })
            .FirstOrDefaultAsync(ct);

        adjustment.TotalIncreaseQty = totals?.Increase ?? 0m;
        adjustment.TotalDecreaseQty = totals?.Decrease ?? 0m;
        adjustment.TotalValueImpact = totals?.Value ?? 0m;

        await _uow.SaveChangesAsync(ct);
    }

    private async Task<int> GetDefaultLocationIdAsync(CancellationToken ct)
    {
        var id = await _uow.Repository<StorageLocation>().Query()
            .Where(l => l.IsDefault && !l.IsDeleted)
            .Select(l => l.LocationId)
            .FirstOrDefaultAsync(ct);

        return id != 0
            ? id
            : throw new BusinessRuleException(
                "No default storage location is configured. Set one in Storage Locations first.",
                "NO_DEFAULT_LOCATION");
    }
}
