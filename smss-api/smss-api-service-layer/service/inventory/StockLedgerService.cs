using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <inheritdoc cref="IStockLedgerService"/>
    public class StockLedgerService : IStockLedgerService
    {
        private readonly IInventoryRepository _repo;
        public StockLedgerService(IInventoryRepository repo) => _repo = repo;

        public async Task<StockChangeResult> ApplyAsync(string schoolId, long itemId, decimal delta, string movementType, long userId, StockChangeInfo info, CancellationToken ct)
        {
            if (!_repo.InTransaction) throw new InvalidOperationException("A stock change must run inside a transaction.");
            if (delta == 0) throw new ArgumentException("A stock change of 0 is not allowed.", nameof(delta));

            // atomic: the quantity can never go below zero, and two concurrent callers can never both take the last units
            var after = await _repo.ApplyQuantityDeltaAsync(schoolId, itemId, delta, ct);
            if (after == null)
            {
                var onHand = await _repo.GetQuantityAsync(schoolId, itemId, ct);
                if (onHand == null) return new StockChangeResult { Ok = false, ItemNotFound = true };
                var item = await _repo.GetItemRowAsync(schoolId, itemId, ct);
                return new StockChangeResult { Ok = false, Available = onHand.Value, ItemName = item?.Item.ItemName };
            }

            var movement = new TbInvStockMovement
            {
                SchoolId = schoolId, ItemId = itemId, MovementType = movementType, QuantityChange = delta, QuantityAfter = after.Value,
                ReasonCode = info.ReasonCode, ReferenceType = info.ReferenceType, ReferenceId = info.ReferenceId, UnitCost = info.UnitCost, Remarks = info.Remarks,
                CreatedBy = userId, CreatedAt = DateTime.UtcNow
            };
            await _repo.AddAsync(movement, ct);
            await _repo.SaveChangesAsync(ct);
            return new StockChangeResult { Ok = true, QuantityAfter = after.Value, MovementId = movement.MovementId };
        }
    }
}
