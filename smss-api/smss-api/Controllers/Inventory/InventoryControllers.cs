using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Inventory & Stock, Stage 1. The school and the acting user ALWAYS come from the token. Server-side role checks:
    //   CanViewInventory   (School Admin, Store Keeper, Principal): every read; responses carry canManage (false for the Principal)
    //   CanManageInventory (School Admin, Store Keeper): every write
    // Everyone else (Teacher, Accountant, Admission Staff, Student, Guardian, Driver, Super Admin) gets 403; an anonymous caller gets 401.
    // Non-idempotent single-purpose actions are POST (never PATCH), by project convention.
    [ApiController]
    [Authorize]
    public abstract class InventoryControllerBase : ControllerBase
    {
        protected IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);
        protected IActionResult? RefuseView() => User.CanViewInventory() ? null : this.Forbidden("You do not have access to Inventory.");
        protected IActionResult? RefuseManage() => User.CanManageInventory() ? null : this.Forbidden("Only a School Admin or a Store Keeper can change Inventory.");
        protected string? SchoolId => User.GetSchoolId();
        protected bool CanManage => User.CanManageInventory();
        protected bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    [Route("api/InventoryCategory")]
    public class InventoryCategoryController : InventoryControllerBase
    {
        private readonly IInventoryCatalogService _s;
        public InventoryCategoryController(IInventoryCatalogService s) => _s = s;

        [HttpGet] public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetCategoriesAsync(SchoolId, CanManage, includeInactive, ct));
        [HttpPost] public async Task<IActionResult> Create([FromBody] SaveInvCategoryRequest r, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.CreateCategoryAsync(SchoolId, r, ct));
        [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, [FromBody] SaveInvCategoryRequest r, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.UpdateCategoryAsync(SchoolId, id, r, ct));
        [HttpPost("{id:long}/toggle-status")] public async Task<IActionResult> Toggle(long id, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.ToggleCategoryAsync(SchoolId, id, ct));
    }

    [Route("api/InventorySupplier")]
    public class InventorySupplierController : InventoryControllerBase
    {
        private readonly IInventoryCatalogService _s;
        public InventorySupplierController(IInventoryCatalogService s) => _s = s;

        [HttpGet] public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetSuppliersAsync(SchoolId, CanManage, includeInactive, ct));
        [HttpPost] public async Task<IActionResult> Create([FromBody] SaveInvSupplierRequest r, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.CreateSupplierAsync(SchoolId, r, ct));
        [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, [FromBody] SaveInvSupplierRequest r, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.UpdateSupplierAsync(SchoolId, id, r, ct));
        [HttpPost("{id:long}/toggle-status")] public async Task<IActionResult> Toggle(long id, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.ToggleSupplierAsync(SchoolId, id, ct));
    }

    [Route("api/InventoryItem")]
    public class InventoryItemController : InventoryControllerBase
    {
        private readonly IInventoryCatalogService _s;
        public InventoryItemController(IInventoryCatalogService s) => _s = s;

        // GET api/InventoryItem?categoryId=&search=&lowStock=&expiring=&status=active|inactive|all&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? categoryId, [FromQuery] string? search, [FromQuery] bool lowStock, [FromQuery] bool expiring, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetItemsAsync(SchoolId, CanManage, categoryId, search, lowStock, expiring, status, page, pageSize, ct));

        [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => RefuseView() ?? Reply(await _s.GetItemAsync(SchoolId, id, ct));

        // Create / update take NO quantity: stock changes only through stock-in, adjustments, issue and return.
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveInvItemRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.CreateItemAsync(SchoolId, userId, r, ct));
        }

        [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, [FromBody] SaveInvItemRequest r, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.UpdateItemAsync(SchoolId, id, r, ct));
        [HttpPost("{id:long}/toggle-status")] public async Task<IActionResult> Toggle(long id, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.ToggleItemAsync(SchoolId, id, ct));

        // POST api/InventoryItem/{id}/photo   (multipart/form-data: file)   one photo per item; sending another replaces it
        [HttpPost("{id:long}/photo")]
        [RequestSizeLimit(smss_api_service_layer.helper.LogoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadPhoto(long id, IFormFile? file, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (file == null) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "A photo file is required.", Data = null });
            await using var stream = file.OpenReadStream();
            return Reply(await _s.UploadPhotoAsync(SchoolId, id, stream, file.FileName, file.Length, ct));
        }

        [HttpPost("{id:long}/photo/remove")] public async Task<IActionResult> RemovePhoto(long id, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _s.RemovePhotoAsync(SchoolId, id, ct));

        // GET api/InventoryItem/{id}/photo   (authenticated download: the file is in the PRIVATE store, never under /uploads)
        [HttpGet("{id:long}/photo")]
        public async Task<IActionResult> DownloadPhoto(long id, CancellationToken ct)
        {
            if (RefuseView() is { } refused) return refused;
            var (failure, content, contentType, fileName) = await _s.OpenPhotoAsync(SchoolId, id, ct);
            if (failure != null) return Reply(failure);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(content!, contentType!, fileName!);
        }
    }

    [Route("api/InventoryStockIn")]
    public class InventoryStockInController : InventoryControllerBase
    {
        private readonly IInventoryStockService _s;
        public InventoryStockInController(IInventoryStockService s) => _s = s;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? itemId, [FromQuery] long? supplierId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool includeVoided, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetStockInsAsync(SchoolId, CanManage, itemId, supplierId, from, to, includeVoided, page, pageSize, ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateInvStockInRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.CreateStockInAsync(SchoolId, userId, r, ct));
        }

        [HttpPost("{id:long}/void")]
        public async Task<IActionResult> Void(long id, [FromBody] InvVoidRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.VoidStockInAsync(SchoolId, userId, id, r, ct));
        }
    }

    [Route("api/InventoryStockAdjustment")]
    public class InventoryStockAdjustmentController : InventoryControllerBase
    {
        private readonly IInventoryStockService _s;
        public InventoryStockAdjustmentController(IInventoryStockService s) => _s = s;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? itemId, [FromQuery] string? reasonCode, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetAdjustmentsAsync(SchoolId, CanManage, itemId, reasonCode, from, to, page, pageSize, ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateInvAdjustmentRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.CreateAdjustmentAsync(SchoolId, userId, r, ct));
        }
    }

    [Route("api/InventoryStockMovement")]
    public class InventoryStockMovementController : InventoryControllerBase
    {
        private readonly IInventoryStockService _s;
        public InventoryStockMovementController(IInventoryStockService s) => _s = s;

        // GET api/InventoryStockMovement?itemId=&page=&pageSize=   (one item's history, newest first)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? itemId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetMovementsAsync(SchoolId, CanManage, itemId, page, pageSize, ct));
    }

    [Route("api/InventoryIssue")]
    public class InventoryIssueController : InventoryControllerBase
    {
        private readonly IInventoryIssueService _s;
        public InventoryIssueController(IInventoryIssueService s) => _s = s;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? recipientType, [FromQuery] string? status, [FromQuery] string? search, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
            RefuseView() ?? Reply(await _s.GetIssuesAsync(SchoolId, CanManage, recipientType, status, search, from, to, page, pageSize, ct));

        [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => RefuseView() ?? Reply(await _s.GetIssueAsync(SchoolId, id, ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateInvIssueRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.CreateIssueAsync(SchoolId, userId, r, ct));
        }

        [HttpPost("{id:long}/return")]
        public async Task<IActionResult> Return(long id, [FromBody] InvReturnRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.ReturnAsync(SchoolId, userId, id, r, ct));
        }

        [HttpPost("{id:long}/void")]
        public async Task<IActionResult> Void(long id, [FromBody] InvVoidRequest r, CancellationToken ct)
        {
            if (RefuseManage() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _s.VoidIssueAsync(SchoolId, userId, id, r, ct));
        }
    }

    [Route("api/InventoryLookup")]
    public class InventoryLookupController : InventoryControllerBase
    {
        private readonly IInventoryCatalogService _catalog;
        private readonly IInventoryIssueService _issues;
        public InventoryLookupController(IInventoryCatalogService catalog, IInventoryIssueService issues) { _catalog = catalog; _issues = issues; }

        [HttpGet("units")] public async Task<IActionResult> Units(CancellationToken ct) => RefuseView() ?? Reply(await _catalog.GetUnitsAsync(ct));
        [HttpGet("adjustment-reasons")] public IActionResult Reasons() => RefuseView() ?? Reply(_catalog.GetAdjustmentReasons());

        // GET api/InventoryLookup/recipients?type=STAFF|STUDENT|DEPARTMENT&q=   (school-scoped, Active only, capped; manage tier only: it is only used to issue)
        [HttpGet("recipients")]
        public async Task<IActionResult> Recipients([FromQuery] string? type, [FromQuery] string? q, CancellationToken ct) =>
            RefuseManage() ?? Reply(await _issues.SearchRecipientsAsync(SchoolId, type, q, ct));
    }

    [Route("api/InventoryDashboard")]
    public class InventoryDashboardController : InventoryControllerBase
    {
        private readonly IInventoryDashboardService _s;
        public InventoryDashboardController(IInventoryDashboardService s) => _s = s;

        [HttpGet("summary")] public async Task<IActionResult> Summary(CancellationToken ct) => RefuseView() ?? Reply(await _s.GetSummaryAsync(SchoolId, CanManage, ct));
    }
}
