using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>What the caller of the ledger knows about a stock change (what is written next to the quantity).</summary>
    public class StockChangeInfo
    {
        public string? ReasonCode { get; set; }
        public string? ReferenceType { get; set; }
        public long? ReferenceId { get; set; }
        public decimal? UnitCost { get; set; }
        public string? Remarks { get; set; }
    }

    /// <summary>The outcome of one stock change. When <see cref="Ok"/> is false nothing was written.</summary>
    public class StockChangeResult
    {
        public bool Ok { get; set; }
        public decimal QuantityAfter { get; set; }
        public bool ItemNotFound { get; set; }
        public string? ItemName { get; set; }
        /// <summary>The quantity on hand when the change was refused for lack of stock.</summary>
        public decimal Available { get; set; }
        public long MovementId { get; set; }
    }

    /// <summary>
    /// THE ONLY writer of an item's quantity. One atomic UPDATE (with the "never below zero" guard inside the WHERE) plus one immutable ledger row, inside the caller's
    /// transaction. Stock-in, adjustments, issue, return and their voids all go through here.
    /// </summary>
    public interface IStockLedgerService
    {
        /// <summary>Applies a signed change. Throws InvalidOperationException when called outside a transaction (a programming error, never a user error).</summary>
        Task<StockChangeResult> ApplyAsync(string schoolId, long itemId, decimal delta, string movementType, long userId, StockChangeInfo info, CancellationToken ct);
    }

    public interface IInventoryCatalogService
    {
        Task<ApiResponse<InvListDto<InvCategoryDto>>> GetCategoriesAsync(string? schoolId, bool canManage, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<InvCategoryDto>> CreateCategoryAsync(string? schoolId, SaveInvCategoryRequest req, CancellationToken ct);
        Task<ApiResponse<InvCategoryDto>> UpdateCategoryAsync(string? schoolId, long id, SaveInvCategoryRequest req, CancellationToken ct);
        Task<ApiResponse<InvCategoryDto>> ToggleCategoryAsync(string? schoolId, long id, CancellationToken ct);

        Task<ApiResponse<InvListDto<InvSupplierDto>>> GetSuppliersAsync(string? schoolId, bool canManage, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<InvSupplierDto>> CreateSupplierAsync(string? schoolId, SaveInvSupplierRequest req, CancellationToken ct);
        Task<ApiResponse<InvSupplierDto>> UpdateSupplierAsync(string? schoolId, long id, SaveInvSupplierRequest req, CancellationToken ct);
        Task<ApiResponse<InvSupplierDto>> ToggleSupplierAsync(string? schoolId, long id, CancellationToken ct);

        Task<ApiResponse<InvPageDto<InvItemDto>>> GetItemsAsync(string? schoolId, bool canManage, long? categoryId, string? search, bool lowStock, bool expiring, string? status, int? page, int? pageSize, CancellationToken ct);
        Task<ApiResponse<InvItemDto>> GetItemAsync(string? schoolId, long id, CancellationToken ct);
        Task<ApiResponse<InvItemDto>> CreateItemAsync(string? schoolId, long userId, SaveInvItemRequest req, CancellationToken ct);
        Task<ApiResponse<InvItemDto>> UpdateItemAsync(string? schoolId, long id, SaveInvItemRequest req, CancellationToken ct);
        Task<ApiResponse<InvItemDto>> ToggleItemAsync(string? schoolId, long id, CancellationToken ct);

        Task<ApiResponse<InvItemDto>> UploadPhotoAsync(string? schoolId, long id, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<InvItemDto>> RemovePhotoAsync(string? schoolId, long id, CancellationToken ct);
        Task<(ApiResponse<InvItemDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenPhotoAsync(string? schoolId, long id, CancellationToken ct);

        Task<ApiResponse<List<InvUnitDto>>> GetUnitsAsync(CancellationToken ct);
        ApiResponse<List<InvReasonDto>> GetAdjustmentReasons();
    }

    public interface IInventoryStockService
    {
        Task<ApiResponse<InvPageDto<InvStockInDto>>> GetStockInsAsync(string? schoolId, bool canManage, long? itemId, long? supplierId, DateOnly? from, DateOnly? to, bool includeVoided, int? page, int? pageSize, CancellationToken ct);
        Task<ApiResponse<InvStockInDto>> CreateStockInAsync(string? schoolId, long userId, CreateInvStockInRequest req, CancellationToken ct);
        Task<ApiResponse<InvStockInDto>> VoidStockInAsync(string? schoolId, long userId, long id, InvVoidRequest req, CancellationToken ct);

        Task<ApiResponse<InvPageDto<InvMovementDto>>> GetAdjustmentsAsync(string? schoolId, bool canManage, long? itemId, string? reasonCode, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct);
        Task<ApiResponse<InvMovementDto>> CreateAdjustmentAsync(string? schoolId, long userId, CreateInvAdjustmentRequest req, CancellationToken ct);

        Task<ApiResponse<InvPageDto<InvMovementDto>>> GetMovementsAsync(string? schoolId, bool canManage, long? itemId, int? page, int? pageSize, CancellationToken ct);
    }

    public interface IInventoryIssueService
    {
        Task<ApiResponse<InvPageDto<InvIssueDto>>> GetIssuesAsync(string? schoolId, bool canManage, string? recipientType, string? status, string? search, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct);
        Task<ApiResponse<InvIssueDto>> GetIssueAsync(string? schoolId, long id, CancellationToken ct);
        Task<ApiResponse<InvIssueDto>> CreateIssueAsync(string? schoolId, long userId, CreateInvIssueRequest req, CancellationToken ct);
        Task<ApiResponse<InvIssueDto>> ReturnAsync(string? schoolId, long userId, long id, InvReturnRequest req, CancellationToken ct);
        Task<ApiResponse<InvIssueDto>> VoidIssueAsync(string? schoolId, long userId, long id, InvVoidRequest req, CancellationToken ct);
        Task<ApiResponse<List<InvRecipientDto>>> SearchRecipientsAsync(string? schoolId, string? type, string? q, CancellationToken ct);
    }

    public interface IInventoryDashboardService
    {
        Task<ApiResponse<InvDashboardDto>> GetSummaryAsync(string? schoolId, bool canManage, CancellationToken ct);
    }

    /// <summary>Private storage of item photos (never a public /uploads URL). Keys are {schoolCode}/inventory/items/{itemId}/photo_&lt;random&gt;.&lt;ext&gt;.</summary>
    public interface IInventoryFileStore
    {
        Task<string> SaveItemPhotoAsync(string schoolCode, long itemId, Stream content, string extension, CancellationToken ct);
        Stream? Open(string key);
        void Delete(string key);
    }
}
