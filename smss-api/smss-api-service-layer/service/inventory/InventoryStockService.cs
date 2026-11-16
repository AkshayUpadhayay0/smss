using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Stock-in (with void), adjustments and the movement history. Every quantity change goes through <see cref="IStockLedgerService"/>.</summary>
    public class InventoryStockService : InventoryServiceBase, IInventoryStockService
    {
        private readonly IStockLedgerService _ledger;

        public InventoryStockService(IInventoryRepository repo, IStockLedgerService ledger, ILogger<InventoryStockService> logger) : base(repo, logger) => _ledger = ledger;

        private static InvStockInDto ToDto(InvStockInRow r) => new()
        {
            StockInId = r.StockIn.StockInId, ItemId = r.StockIn.ItemId, ItemCode = r.ItemCode, ItemName = r.ItemName, UnitCode = r.UnitCode,
            SupplierId = r.StockIn.SupplierId, SupplierName = r.SupplierName, Quantity = r.StockIn.Quantity, UnitCost = r.StockIn.UnitCost,
            TotalCost = decimal.Round(r.StockIn.Quantity * r.StockIn.UnitCost, 2), ReceivedDate = r.StockIn.ReceivedDate, BillNumber = r.StockIn.BillNumber,
            ExpiryDate = r.StockIn.ExpiryDate, Remarks = r.StockIn.Remarks, ReceivedByName = r.ReceivedByName, Status = r.StockIn.Status,
            VoidedByName = r.VoidedByName, VoidedAt = r.StockIn.VoidedAt, VoidReason = r.StockIn.VoidReason, CreatedAt = r.StockIn.CreatedAt
        };

        public async Task<ApiResponse<InvPageDto<InvStockInDto>>> GetStockInsAsync(string? schoolId, bool canManage, long? itemId, long? supplierId, DateOnly? from, DateOnly? to, bool includeVoided, int? page, int? pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvPageDto<InvStockInDto>>();
            try
            {
                if (from != null && to != null && to < from) return Result<InvPageDto<InvStockInDto>>(400, "The 'to' date can't be before the 'from' date.", null);
                var p = InventoryRules.Page(page); var size = InventoryRules.PageSize(pageSize);
                var (rows, total) = await Repo.GetStockInsAsync(schoolId, itemId, supplierId, from, to, includeVoided, p, size, ct);
                return Result(200, "Successfully fetched", new InvPageDto<InvStockInDto> { CanManage = canManage, Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = p, PageSize = size });
            }
            catch (Exception ex) { return Error<InvPageDto<InvStockInDto>>(ex, "listing stock-ins"); }
        }

        public async Task<ApiResponse<InvStockInDto>> CreateStockInAsync(string? schoolId, long userId, CreateInvStockInRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvStockInDto>();
            try
            {
                var qty = req.Quantity!.Value; var cost = req.UnitCost!.Value; var received = req.ReceivedDate!.Value;
                if (InventoryRules.QuantityError(qty) is { } qe) return Result<InvStockInDto>(400, qe, null);
                if (InventoryRules.MoneyError(cost, "Unit cost") is { } ce) return Result<InvStockInDto>(400, ce, null);
                if (received > SchoolClock.Today()) return Result<InvStockInDto>(400, "The received date can't be in the future.", null);
                if (req.ExpiryDate is DateOnly exp && exp <= received) return Result<InvStockInDto>(400, "The expiry date must be after the received date.", null);

                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvStockInDto>();

                await using var tx = await Repo.BeginTransactionAsync(ct);
                var item = await Repo.GetItemAsync(schoolId, req.ItemId!.Value, ct);
                if (item == null) return Result<InvStockInDto>(404, "Item not found.", null);
                if (item.StatusId != active) return Result<InvStockInDto>(400, $"The item \"{item.ItemName}\" is deactivated. Activate it before adding stock.", null);
                if (req.SupplierId is long sid)
                {
                    var supplier = await Repo.GetSupplierAsync(schoolId, sid, ct);
                    if (supplier == null) return Result<InvStockInDto>(404, "Supplier not found.", null);
                    if (supplier.StatusId != active) return Result<InvStockInDto>(400, $"The supplier \"{supplier.SupplierName}\" is deactivated. Choose another or none.", null);
                }

                var now = DateTime.UtcNow;
                var stockIn = new TbInvStockIn
                {
                    SchoolId = schoolId, ItemId = item.ItemId, SupplierId = req.SupplierId, Quantity = qty, UnitCost = cost, ReceivedDate = received,
                    BillNumber = InventoryRules.Clean(req.BillNumber), ExpiryDate = req.ExpiryDate, Remarks = InventoryRules.Clean(req.Remarks),
                    ReceivedBy = userId, Status = PaymentStatuses.Active, CreatedAt = now
                };
                await Repo.AddAsync(stockIn, ct);
                await Repo.SaveChangesAsync(ct);   // gives the stock-in its id, which the ledger row refers to

                var change = await _ledger.ApplyAsync(schoolId, item.ItemId, qty, InvMovementTypes.StockIn, userId,
                    new StockChangeInfo { ReferenceType = InvReferenceTypes.StockIn, ReferenceId = stockIn.StockInId, UnitCost = cost, Remarks = stockIn.Remarks }, ct);
                if (!change.Ok) return Result<InvStockInDto>(500, "Something went wrong. Please try again later.", null);

                item.PurchasePrice = cost;   // the last stock-in cost (not an average)
                if (req.ExpiryDate != null) item.ExpiryDate = req.ExpiryDate;
                item.UpdatedAt = now;
                await Repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var row = await Repo.GetStockInRowAsync(schoolId, stockIn.StockInId, ct);
                return Result(201, $"Added {qty:0.###} to \"{item.ItemName}\".", ToDto(row!));
            }
            catch (Exception ex) { return Error<InvStockInDto>(ex, "recording a stock-in"); }
        }

        public async Task<ApiResponse<InvStockInDto>> VoidStockInAsync(string? schoolId, long userId, long id, InvVoidRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvStockInDto>();
            var reason = InventoryRules.Clean(req.Reason);
            if (reason == null) return Result<InvStockInDto>(400, "A reason is required to void a stock-in.", null);
            if (reason.Length > InventoryRules.MaxRemarks) return Result<InvStockInDto>(400, "The reason can be at most 500 characters.", null);
            try
            {
                await using var tx = await Repo.BeginTransactionAsync(ct);
                var stockIn = await Repo.LockStockInAsync(schoolId, id, ct);   // two voids of the same stock-in can't run together
                if (stockIn == null) return Result<InvStockInDto>(404, "Stock-in not found.", null);
                if (stockIn.Status == PaymentStatuses.Voided) return Result<InvStockInDto>(409, "This stock-in has already been voided.", null);

                var change = await _ledger.ApplyAsync(schoolId, stockIn.ItemId, -stockIn.Quantity, InvMovementTypes.StockInVoid, userId,
                    new StockChangeInfo { ReferenceType = InvReferenceTypes.StockIn, ReferenceId = stockIn.StockInId, UnitCost = stockIn.UnitCost, Remarks = reason }, ct);
                if (!change.Ok)
                    return change.ItemNotFound
                        ? Result<InvStockInDto>(404, "Item not found.", null)
                        : Result<InvStockInDto>(409, $"This stock-in can't be voided: only {change.Available:0.###} of \"{change.ItemName}\" is on hand, but {stockIn.Quantity:0.###} would be taken back. Nothing was changed.", null);

                stockIn.Status = PaymentStatuses.Voided; stockIn.VoidedBy = userId; stockIn.VoidedAt = DateTime.UtcNow; stockIn.VoidReason = reason;
                await Repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                var row = await Repo.GetStockInRowAsync(schoolId, id, ct);
                return Result(200, "Stock-in voided and the stock taken back.", ToDto(row!));
            }
            catch (Exception ex) { return Error<InvStockInDto>(ex, "voiding a stock-in"); }
        }

        // ================================================================ adjustments
        public async Task<ApiResponse<InvPageDto<InvMovementDto>>> GetAdjustmentsAsync(string? schoolId, bool canManage, long? itemId, string? reasonCode, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvPageDto<InvMovementDto>>();
            try
            {
                var reason = InventoryRules.CleanUpper(reasonCode);
                if (reason != null && !InvReasonCodes.All.Contains(reason)) return Result<InvPageDto<InvMovementDto>>(400, "Unknown reason.", null);
                if (from != null && to != null && to < from) return Result<InvPageDto<InvMovementDto>>(400, "The 'to' date can't be before the 'from' date.", null);
                var p = InventoryRules.Page(page); var size = InventoryRules.PageSize(pageSize);
                var (rows, total) = await Repo.GetMovementsAsync(schoolId, itemId, true, reason, from, to, p, size, ct);
                return Result(200, "Successfully fetched", new InvPageDto<InvMovementDto> { CanManage = canManage, Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = p, PageSize = size });
            }
            catch (Exception ex) { return Error<InvPageDto<InvMovementDto>>(ex, "listing adjustments"); }
        }

        public async Task<ApiResponse<InvMovementDto>> CreateAdjustmentAsync(string? schoolId, long userId, CreateInvAdjustmentRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvMovementDto>();
            try
            {
                var reason = InventoryRules.CleanUpper(req.ReasonCode) ?? "";
                var change = req.QuantityChange!.Value;
                if (InventoryRules.AdjustmentError(reason, change) is { } err) return Result<InvMovementDto>(400, err, null);
                var remarks = InventoryRules.Clean(req.Remarks);
                if (remarks == null) return Result<InvMovementDto>(400, "Remarks are required for an adjustment.", null);
                if (remarks.Length > InventoryRules.MaxRemarks) return Result<InvMovementDto>(400, "Remarks can be at most 500 characters.", null);

                await using var tx = await Repo.BeginTransactionAsync(ct);
                var item = await Repo.GetItemAsync(schoolId, req.ItemId!.Value, ct);
                if (item == null) return Result<InvMovementDto>(404, "Item not found.", null);

                var type = InvMovementTypes.Adjustment;
                if (reason == InvReasonCodes.OpeningBalance)
                {
                    if (await Repo.HasOpeningMovementAsync(schoolId, item.ItemId, ct))
                        return Result<InvMovementDto>(409, $"\"{item.ItemName}\" already has an opening balance. Use a count correction instead.", null);
                    type = InvMovementTypes.Opening;
                }

                var result = await _ledger.ApplyAsync(schoolId, item.ItemId, change, type, userId, new StockChangeInfo { ReasonCode = reason, Remarks = remarks }, ct);
                if (!result.Ok)
                    return result.ItemNotFound
                        ? Result<InvMovementDto>(404, "Item not found.", null)
                        : Result<InvMovementDto>(409, $"Not enough stock: only {result.Available:0.###} of \"{item.ItemName}\" is on hand. Nothing was changed.", null);
                await tx.CommitAsync(ct);
                var row = await Repo.GetMovementRowAsync(schoolId, result.MovementId, ct);
                return Result(201, "Adjustment recorded.", ToDto(row!));
            }
            catch (Exception ex) when (IsUnique(ex)) { return Result<InvMovementDto>(409, "This item already has an opening balance.", null); }
            catch (Exception ex) { return Error<InvMovementDto>(ex, "recording a stock adjustment"); }
        }

        public async Task<ApiResponse<InvPageDto<InvMovementDto>>> GetMovementsAsync(string? schoolId, bool canManage, long? itemId, int? page, int? pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvPageDto<InvMovementDto>>();
            try
            {
                if (itemId == null) return Result<InvPageDto<InvMovementDto>>(400, "itemId is required.", null);
                if (await Repo.GetItemRowAsync(schoolId, itemId.Value, ct) == null) return Result<InvPageDto<InvMovementDto>>(404, "Item not found.", null);
                var p = InventoryRules.Page(page); var size = InventoryRules.PageSize(pageSize);
                var (rows, total) = await Repo.GetMovementsAsync(schoolId, itemId, false, null, null, null, p, size, ct);
                return Result(200, "Successfully fetched", new InvPageDto<InvMovementDto> { CanManage = canManage, Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = p, PageSize = size });
            }
            catch (Exception ex) { return Error<InvPageDto<InvMovementDto>>(ex, "listing stock movements"); }
        }
    }
}
