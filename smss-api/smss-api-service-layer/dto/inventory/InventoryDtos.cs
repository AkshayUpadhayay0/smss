using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Inventory & Stock, Stage 1. Responses carry canManage so the UI hides actions for the read-only Principal.
    // NOTHING here accepts a quantity on an item: quantity changes only through the stock ledger (stock-in, adjustment, issue, return and their voids).

    public class InvListDto<T> { public bool CanManage { get; set; } public List<T> Items { get; set; } = new(); }

    public class InvPageDto<T>
    {
        public bool CanManage { get; set; }
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // ---------------------------------------------------------------- categories
    public class InvCategoryDto
    {
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public string CategoryCode { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int ItemCount { get; set; }
    }

    public class SaveInvCategoryRequest
    {
        [Required, StringLength(100)] public string? CategoryName { get; set; }
        [Required, StringLength(30)] public string? CategoryCode { get; set; }
        [StringLength(1000)] public string? Description { get; set; }
    }

    // ---------------------------------------------------------------- suppliers
    public class InvSupplierDto
    {
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = null!;
        public string? ContactPerson { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Gstin { get; set; }
        public string? Pan { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveInvSupplierRequest
    {
        [Required, StringLength(150)] public string? SupplierName { get; set; }
        [StringLength(100)] public string? ContactPerson { get; set; }
        [StringLength(20)] public string? Mobile { get; set; }
        [StringLength(150)] public string? Email { get; set; }
        [StringLength(1000)] public string? Address { get; set; }
        [StringLength(15)] public string? Gstin { get; set; }
        [StringLength(10)] public string? Pan { get; set; }
    }

    // ---------------------------------------------------------------- items
    public class InvItemDto
    {
        public long ItemId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string? Barcode { get; set; }
        public string ItemName { get; set; } = null!;
        public string? Description { get; set; }
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public long UnitId { get; set; }
        public string UnitName { get; set; } = null!;
        public string UnitCode { get; set; } = null!;
        public string ItemType { get; set; } = null!;
        public decimal PurchasePrice { get; set; }
        public decimal? SellingPrice { get; set; }
        public bool IsSaleable { get; set; }
        public decimal GstPercent { get; set; }
        public decimal MinStockLevel { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal StockValue { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public bool HasPhoto { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Derived on read: min stock level is set and the quantity is at or below it.</summary>
        public bool IsLowStock { get; set; }
        public bool IsExpired { get; set; }
        public bool IsExpiringSoon { get; set; }
    }

    /// <summary>Create / update an item. There is deliberately NO quantity here: stock is only ever changed through the ledger (opening balance = an adjustment).</summary>
    public class SaveInvItemRequest
    {
        [Required, StringLength(150)] public string? ItemName { get; set; }
        [StringLength(100)] public string? Barcode { get; set; }
        [StringLength(2000)] public string? Description { get; set; }
        [Required] public long? CategoryId { get; set; }
        [Required] public long? UnitId { get; set; }
        [Required, StringLength(12)] public string? ItemType { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? SellingPrice { get; set; }
        public bool IsSaleable { get; set; }
        public decimal? GstPercent { get; set; }
        public decimal? MinStockLevel { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }

    public class InvUnitDto { public long UnitId { get; set; } public string UnitName { get; set; } = null!; public string UnitCode { get; set; } = null!; }
    public class InvReasonDto { public string Code { get; set; } = null!; public string Label { get; set; } = null!; public string Direction { get; set; } = null!; }

    // ---------------------------------------------------------------- stock in
    public class InvStockInDto
    {
        public long StockInId { get; set; }
        public long ItemId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string UnitCode { get; set; } = null!;
        public long? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
        public DateOnly ReceivedDate { get; set; }
        public string? BillNumber { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string? Remarks { get; set; }
        public string? ReceivedByName { get; set; }
        public string Status { get; set; } = null!;
        public string? VoidedByName { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateInvStockInRequest
    {
        [Required] public long? ItemId { get; set; }
        public long? SupplierId { get; set; }
        [Required] public decimal? Quantity { get; set; }
        [Required] public decimal? UnitCost { get; set; }
        [Required] public DateOnly? ReceivedDate { get; set; }
        [StringLength(50)] public string? BillNumber { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        [StringLength(500)] public string? Remarks { get; set; }
    }

    public class InvVoidRequest { [Required, StringLength(500)] public string? Reason { get; set; } }

    // ---------------------------------------------------------------- adjustments / ledger
    public class InvMovementDto
    {
        public long MovementId { get; set; }
        public long ItemId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string UnitCode { get; set; } = null!;
        public string MovementType { get; set; } = null!;
        public decimal QuantityChange { get; set; }
        public decimal QuantityAfter { get; set; }
        public string? ReasonCode { get; set; }
        public string? ReferenceType { get; set; }
        public long? ReferenceId { get; set; }
        public decimal? UnitCost { get; set; }
        public string? Remarks { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>A signed quantity change with a reason. DAMAGE / LOSS / EXPIRED must be negative; OPENING_BALANCE positive (once per item); CORRECTION either.</summary>
    public class CreateInvAdjustmentRequest
    {
        [Required] public long? ItemId { get; set; }
        [Required, StringLength(30)] public string? ReasonCode { get; set; }
        [Required] public decimal? QuantityChange { get; set; }
        [Required, StringLength(500)] public string? Remarks { get; set; }
    }

    // ---------------------------------------------------------------- issue / return
    public class InvIssueLineDto
    {
        public long IssueLineId { get; set; }
        public long ItemId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string UnitCode { get; set; } = null!;
        public decimal Quantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal OutstandingQuantity { get; set; }
        public List<InvReturnDto> Returns { get; set; } = new();
    }

    public class InvReturnDto
    {
        public long ReturnId { get; set; }
        public decimal Quantity { get; set; }
        public DateOnly ReturnDate { get; set; }
        public string Condition { get; set; } = null!;
        public string? Remarks { get; set; }
        public string? ReceivedByName { get; set; }
    }

    public class InvIssueDto
    {
        public long IssueId { get; set; }
        public string IssueNumber { get; set; } = null!;
        public string RecipientType { get; set; } = null!;
        public string RecipientId { get; set; } = null!;
        public string RecipientName { get; set; } = null!;
        public DateOnly IssueDate { get; set; }
        public string? Purpose { get; set; }
        public string? IssuedByName { get; set; }
        /// <summary>Derived: Outstanding / Partially Returned / Returned / Voided.</summary>
        public string Status { get; set; } = null!;
        public decimal TotalQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public string? VoidedByName { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<InvIssueLineDto> Lines { get; set; } = new();
    }

    public class CreateInvIssueLine { [Required] public long? ItemId { get; set; } [Required] public decimal? Quantity { get; set; } }

    public class CreateInvIssueRequest
    {
        [Required, StringLength(12)] public string? RecipientType { get; set; }
        /// <summary>The employee id (STAFF), student id (STUDENT) or department id (DEPARTMENT), as text.</summary>
        [Required, StringLength(20)] public string? RecipientId { get; set; }
        [Required] public DateOnly? IssueDate { get; set; }
        [StringLength(1000)] public string? Purpose { get; set; }
        [Required, MinLength(1), MaxLength(50)] public List<CreateInvIssueLine>? Lines { get; set; }
    }

    public class InvReturnLineRequest
    {
        [Required] public long? IssueLineId { get; set; }
        [Required] public decimal? Quantity { get; set; }
        [Required, StringLength(10)] public string? Condition { get; set; }
        [StringLength(500)] public string? Remarks { get; set; }
    }

    public class InvReturnRequest
    {
        [Required] public DateOnly? ReturnDate { get; set; }
        [Required, MinLength(1), MaxLength(50)] public List<InvReturnLineRequest>? Lines { get; set; }
    }

    public class InvRecipientDto { public string Id { get; set; } = null!; public string Name { get; set; } = null!; public string? Detail { get; set; } }

    // ---------------------------------------------------------------- dashboard
    public class InvDashboardItemDto
    {
        public long ItemId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string UnitCode { get; set; } = null!;
        public decimal CurrentQuantity { get; set; }
        public decimal MinStockLevel { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }

    /// <summary>
    /// The Inventory dashboard. Every part is loaded on its own: a part that failed is null (and named in <see cref="UnavailableParts"/>), so the screen shows
    /// "Unavailable" instead of a false 0.
    /// </summary>
    public class InvDashboardDto
    {
        public bool CanManage { get; set; }
        public int ExpiringSoonDays { get; set; }
        public int? TotalActiveItems { get; set; }
        public decimal? TotalStockValue { get; set; }
        public int? LowStockCount { get; set; }
        public List<InvDashboardItemDto>? LowStock { get; set; }
        public int? ExpiringSoonCount { get; set; }
        public List<InvDashboardItemDto>? ExpiringSoon { get; set; }
        public int? ExpiredCount { get; set; }
        public List<InvDashboardItemDto>? Expired { get; set; }
        public int? IssuedNotReturnedCount { get; set; }
        public List<InvMovementDto>? RecentMovements { get; set; }
        public int? StockInsLast30Days { get; set; }
        public int? IssuesLast30Days { get; set; }
        public List<string> UnavailableParts { get; set; } = new();
    }
}
