using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Row shapes: an entity plus the names read from related tables (no navigation properties on the inventory entities).
    public class InvItemRow { public TbInvItem Item { get; set; } = null!; public string CategoryName { get; set; } = null!; public string UnitName { get; set; } = null!; public string UnitCode { get; set; } = null!; }
    public class InvCategoryRow { public TbInvCategory Category { get; set; } = null!; public int ItemCount { get; set; } }
    public class InvStockInRow
    {
        public TbInvStockIn StockIn { get; set; } = null!; public string ItemCode { get; set; } = null!; public string ItemName { get; set; } = null!; public string UnitCode { get; set; } = null!;
        public string? SupplierName { get; set; } public string? ReceivedByName { get; set; } public string? VoidedByName { get; set; }
    }
    public class InvMovementRow
    {
        public TbInvStockMovement Movement { get; set; } = null!; public string ItemCode { get; set; } = null!; public string ItemName { get; set; } = null!; public string UnitCode { get; set; } = null!; public string? CreatedByName { get; set; }
    }
    public class InvIssueRow
    {
        public TbInvIssue Issue { get; set; } = null!; public string RecipientName { get; set; } = null!; public string? IssuedByName { get; set; } public string? VoidedByName { get; set; }
        public decimal TotalQuantity { get; set; } public decimal ReturnedQuantity { get; set; }
    }
    public class InvIssueLineRow { public TbInvIssueLine Line { get; set; } = null!; public string ItemCode { get; set; } = null!; public string ItemName { get; set; } = null!; public string UnitCode { get; set; } = null!; }
    public class InvReturnRow { public TbInvIssueReturn Return { get; set; } = null!; public string? ReceivedByName { get; set; } }
    public class InvRecipientRow { public string Id { get; set; } = null!; public string Name { get; set; } = null!; public string? Detail { get; set; } }

    public class InvItemFilter
    {
        public long? CategoryId { get; set; }
        /// <summary>Only items with this status id (null = every status).</summary>
        public int? StatusId { get; set; }
        public string? Search { get; set; }
        public bool LowStock { get; set; }
        /// <summary>Expiry date on or before this day, with stock on hand (includes already-expired).</summary>
        public DateOnly? ExpiringBy { get; set; }
    }

    public class InvIssueFilter
    {
        public string? RecipientType { get; set; }
        public string? Search { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        /// <summary>One of the derived statuses (Outstanding, Partially Returned, Returned, Voided), or null.</summary>
        public string? DerivedStatus { get; set; }
    }

    /// <summary>All reads and writes of the Inventory module. Every method is scoped by school id; ids from another school simply are not found.</summary>
    public interface IInventoryRepository
    {
        Task<int?> GetStatusIdAsync(string name, CancellationToken ct);
        Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        bool InTransaction { get; }
        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracking();
        /// <summary>One caller at a time per (key, school): used for item codes and issue numbers. Held until the transaction ends.</summary>
        Task LockNumbersAsync(string key, string schoolId, CancellationToken ct);

        // lookups
        Task<List<LutInvUnit>> GetUnitsAsync(CancellationToken ct);
        Task<LutInvUnit?> GetUnitAsync(long unitId, CancellationToken ct);

        // categories
        Task<List<InvCategoryRow>> GetCategoriesAsync(string schoolId, int? statusId, CancellationToken ct);
        Task<TbInvCategory?> GetCategoryAsync(string schoolId, long categoryId, CancellationToken ct);
        Task<bool> CategoryCodeExistsAsync(string schoolId, string code, long? exceptId, CancellationToken ct);
        Task AddAsync(TbInvCategory c, CancellationToken ct);

        // suppliers
        Task<List<TbInvSupplier>> GetSuppliersAsync(string schoolId, int? statusId, CancellationToken ct);
        Task<TbInvSupplier?> GetSupplierAsync(string schoolId, long supplierId, CancellationToken ct);
        Task<bool> SupplierNameExistsAsync(string schoolId, string name, long? exceptId, CancellationToken ct);
        Task AddAsync(TbInvSupplier s, CancellationToken ct);

        // items
        Task<(List<InvItemRow> Rows, int Total)> GetItemsAsync(string schoolId, InvItemFilter filter, int page, int pageSize, CancellationToken ct);
        Task<InvItemRow?> GetItemRowAsync(string schoolId, long itemId, CancellationToken ct);
        /// <summary>The tracked item. Never assign CurrentQuantity on it: only <see cref="ApplyQuantityDeltaAsync"/> writes quantity.</summary>
        Task<TbInvItem?> GetItemAsync(string schoolId, long itemId, CancellationToken ct);
        Task<bool> BarcodeExistsAsync(string schoolId, string barcode, long? exceptId, CancellationToken ct);
        Task<int> GetMaxItemSequenceAsync(string schoolId, CancellationToken ct);
        Task AddAsync(TbInvItem i, CancellationToken ct);

        // the ledger (the ONLY quantity writer)
        /// <summary>
        /// UPDATE tb_inv_items SET current_quantity = current_quantity + delta WHERE ... AND current_quantity + delta >= 0 RETURNING current_quantity.
        /// Returns the new quantity, or null when the item is not this school's or the stock is not enough (nothing was changed). Must run inside a transaction.
        /// </summary>
        Task<decimal?> ApplyQuantityDeltaAsync(string schoolId, long itemId, decimal delta, CancellationToken ct);
        Task<decimal?> GetQuantityAsync(string schoolId, long itemId, CancellationToken ct);
        Task AddAsync(TbInvStockMovement m, CancellationToken ct);
        Task<bool> HasOpeningMovementAsync(string schoolId, long itemId, CancellationToken ct);
        Task<(List<InvMovementRow> Rows, int Total)> GetMovementsAsync(string schoolId, long? itemId, bool adjustmentsOnly, string? reasonCode, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct);
        Task<InvMovementRow?> GetMovementRowAsync(string schoolId, long movementId, CancellationToken ct);

        // stock in
        Task<(List<InvStockInRow> Rows, int Total)> GetStockInsAsync(string schoolId, long? itemId, long? supplierId, DateOnly? from, DateOnly? to, bool includeVoided, int page, int pageSize, CancellationToken ct);
        Task<InvStockInRow?> GetStockInRowAsync(string schoolId, long stockInId, CancellationToken ct);
        /// <summary>The stock-in row, locked (FOR UPDATE) so two voids of it cannot run together.</summary>
        Task<TbInvStockIn?> LockStockInAsync(string schoolId, long stockInId, CancellationToken ct);
        Task AddAsync(TbInvStockIn s, CancellationToken ct);

        // issues
        Task<(List<InvIssueRow> Rows, int Total)> GetIssuesAsync(string schoolId, InvIssueFilter filter, int page, int pageSize, CancellationToken ct);
        Task<InvIssueRow?> GetIssueRowAsync(string schoolId, long issueId, CancellationToken ct);
        Task<List<InvIssueLineRow>> GetIssueLinesAsync(string schoolId, IReadOnlyCollection<long> issueIds, CancellationToken ct);
        Task<List<(long IssueLineId, InvReturnRow Row)>> GetReturnsAsync(string schoolId, IReadOnlyCollection<long> issueLineIds, CancellationToken ct);
        Task<TbInvIssue?> LockIssueAsync(string schoolId, long issueId, CancellationToken ct);
        Task<List<TbInvIssueLine>> GetTrackedLinesAsync(string schoolId, long issueId, CancellationToken ct);
        Task<int> GetMaxIssueSequenceAsync(string schoolId, string prefix, CancellationToken ct);
        Task AddAsync(TbInvIssue i, CancellationToken ct);
        Task AddAsync(TbInvIssueLine l, CancellationToken ct);
        Task AddAsync(TbInvIssueReturn r, CancellationToken ct);

        // recipients (READ-ONLY access to the employee / student / department tables)
        Task<InvRecipientRow?> GetActiveRecipientAsync(string schoolId, string recipientType, string recipientId, int activeStatusId, CancellationToken ct);
        Task<List<InvRecipientRow>> SearchRecipientsAsync(string schoolId, string recipientType, string? q, int activeStatusId, int cap, CancellationToken ct);

        // dashboard (each its own query: one failing does not hide the others)
        Task<int> CountActiveItemsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<decimal> SumStockValueAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<(int Count, List<InvItemRow> Top)> GetLowStockAsync(string schoolId, int activeStatusId, int top, CancellationToken ct);
        Task<(int Count, List<InvItemRow> Top)> GetExpiryListAsync(string schoolId, int activeStatusId, DateOnly from, DateOnly to, int top, CancellationToken ct);
        Task<(int Count, List<InvItemRow> Top)> GetExpiredListAsync(string schoolId, int activeStatusId, DateOnly today, int top, CancellationToken ct);
        Task<int> CountIssuedNotReturnedAsync(string schoolId, CancellationToken ct);
        Task<int> CountStockInsSinceAsync(string schoolId, DateOnly since, CancellationToken ct);
        Task<int> CountIssuesSinceAsync(string schoolId, DateOnly since, CancellationToken ct);
    }
}
