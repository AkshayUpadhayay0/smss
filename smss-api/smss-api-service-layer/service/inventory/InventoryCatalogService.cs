using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Categories, suppliers, items (without any quantity), item photos and the small lookups.</summary>
    public class InventoryCatalogService : InventoryServiceBase, IInventoryCatalogService
    {
        private const string ItemCodeIndex = "uq_tb_inv_items_school_code";
        private const string BarcodeIndex = "uq_tb_inv_items_school_barcode";
        private readonly IInventoryFileStore _files;

        public InventoryCatalogService(IInventoryRepository repo, IInventoryFileStore files, ILogger<InventoryCatalogService> logger) : base(repo, logger) => _files = files;

        // ================================================================ categories
        public async Task<ApiResponse<InvListDto<InvCategoryDto>>> GetCategoriesAsync(string? schoolId, bool canManage, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvListDto<InvCategoryDto>>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvListDto<InvCategoryDto>>();
                var rows = await Repo.GetCategoriesAsync(schoolId, includeInactive ? null : active, ct);
                return Result(200, "Successfully fetched", new InvListDto<InvCategoryDto> { CanManage = canManage, Items = rows.Select(r => CategoryDto(r.Category, active.Value, r.ItemCount)).ToList() });
            }
            catch (Exception ex) { return Error<InvListDto<InvCategoryDto>>(ex, "listing inventory categories"); }
        }

        private static InvCategoryDto CategoryDto(TbInvCategory c, int activeId, int itemCount = 0) => new()
        {
            CategoryId = c.CategoryId, CategoryName = c.CategoryName, CategoryCode = c.CategoryCode, Description = c.Description, IsActive = c.StatusId == activeId, ItemCount = itemCount
        };

        public async Task<ApiResponse<InvCategoryDto>> CreateCategoryAsync(string? schoolId, SaveInvCategoryRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvCategoryDto>();
            try
            {
                var (name, code, err) = ValidateCategory(req);
                if (err != null) return Result<InvCategoryDto>(400, err, null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvCategoryDto>();
                if (await Repo.CategoryCodeExistsAsync(schoolId, code!, null, ct)) return Result<InvCategoryDto>(409, $"A category with the code \"{code}\" already exists.", null);

                var now = DateTime.UtcNow;
                var c = new TbInvCategory { SchoolId = schoolId, CategoryName = name!, CategoryCode = code!, Description = InventoryRules.Clean(req.Description), StatusId = active.Value, CreatedAt = now, UpdatedAt = now };
                await Repo.AddAsync(c, ct);
                try { await Repo.SaveChangesAsync(ct); }
                catch (Exception ex) when (IsUnique(ex)) { Repo.ClearTracking(); return Result<InvCategoryDto>(409, $"A category with the code \"{code}\" already exists.", null); }
                return Result(201, "Category created.", CategoryDto(c, active.Value));
            }
            catch (Exception ex) { return Error<InvCategoryDto>(ex, "creating an inventory category"); }
        }

        public async Task<ApiResponse<InvCategoryDto>> UpdateCategoryAsync(string? schoolId, long id, SaveInvCategoryRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvCategoryDto>();
            try
            {
                var (name, code, err) = ValidateCategory(req);
                if (err != null) return Result<InvCategoryDto>(400, err, null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvCategoryDto>();
                var c = await Repo.GetCategoryAsync(schoolId, id, ct);
                if (c == null) return Result<InvCategoryDto>(404, "Category not found.", null);
                if (await Repo.CategoryCodeExistsAsync(schoolId, code!, id, ct)) return Result<InvCategoryDto>(409, $"A category with the code \"{code}\" already exists.", null);
                c.CategoryName = name!; c.CategoryCode = code!; c.Description = InventoryRules.Clean(req.Description); c.UpdatedAt = DateTime.UtcNow;
                try { await Repo.SaveChangesAsync(ct); }
                catch (Exception ex) when (IsUnique(ex)) { Repo.ClearTracking(); return Result<InvCategoryDto>(409, $"A category with the code \"{code}\" already exists.", null); }
                return Result(200, "Category updated.", CategoryDto(c, active.Value));
            }
            catch (Exception ex) { return Error<InvCategoryDto>(ex, "updating an inventory category"); }
        }

        private static (string? Name, string? Code, string? Error) ValidateCategory(SaveInvCategoryRequest req)
        {
            var name = InventoryRules.Clean(req.CategoryName); var code = InventoryRules.NormalizeCode(req.CategoryCode);
            if (name == null) return (null, null, "Category name is required.");
            if (code == null) return (null, null, "Category code is required.");
            return (name, code, null);
        }

        public async Task<ApiResponse<InvCategoryDto>> ToggleCategoryAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvCategoryDto>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct); var inactive = await Repo.GetStatusIdAsync(StatusNames.Inactive, ct);
                if (active == null || inactive == null) return ConfigError<InvCategoryDto>();
                var c = await Repo.GetCategoryAsync(schoolId, id, ct);
                if (c == null) return Result<InvCategoryDto>(404, "Category not found.", null);
                c.StatusId = c.StatusId == active ? inactive.Value : active.Value; c.UpdatedAt = DateTime.UtcNow;
                await Repo.SaveChangesAsync(ct);
                return Result(200, c.StatusId == active ? "Category activated." : "Category deactivated.", CategoryDto(c, active.Value));
            }
            catch (Exception ex) { return Error<InvCategoryDto>(ex, "toggling an inventory category"); }
        }

        // ================================================================ suppliers
        private static InvSupplierDto SupplierDto(TbInvSupplier s, int activeId) => new()
        {
            SupplierId = s.SupplierId, SupplierName = s.SupplierName, ContactPerson = s.ContactPerson, Mobile = s.Mobile, Email = s.Email, Address = s.Address,
            Gstin = s.Gstin, Pan = s.Pan, IsActive = s.StatusId == activeId
        };

        public async Task<ApiResponse<InvListDto<InvSupplierDto>>> GetSuppliersAsync(string? schoolId, bool canManage, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvListDto<InvSupplierDto>>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvListDto<InvSupplierDto>>();
                var rows = await Repo.GetSuppliersAsync(schoolId, includeInactive ? null : active, ct);
                return Result(200, "Successfully fetched", new InvListDto<InvSupplierDto> { CanManage = canManage, Items = rows.Select(s => SupplierDto(s, active.Value)).ToList() });
            }
            catch (Exception ex) { return Error<InvListDto<InvSupplierDto>>(ex, "listing inventory suppliers"); }
        }

        private static string? ValidateSupplier(SaveInvSupplierRequest r, out string name, out string? gstin, out string? pan, out string? email, out string? mobile)
        {
            name = InventoryRules.Clean(r.SupplierName) ?? ""; gstin = InventoryRules.CleanUpper(r.Gstin); pan = InventoryRules.CleanUpper(r.Pan);
            email = InventoryRules.Clean(r.Email); mobile = InventoryRules.Clean(r.Mobile);
            if (name.Length == 0) return "Supplier name is required.";
            if (!InventoryRules.ValidGstin(gstin)) return "GSTIN is not valid (expected 15 characters, e.g. 22AAAAA0000A1Z5).";
            if (!InventoryRules.ValidPan(pan)) return "PAN is not valid (expected 10 characters, e.g. ABCDE1234F).";
            if (!InventoryRules.ValidPhone(mobile)) return "Mobile number is not valid.";
            if (email != null && !new EmailAddressAttribute().IsValid(email)) return "Email is not valid.";
            return null;
        }

        public async Task<ApiResponse<InvSupplierDto>> CreateSupplierAsync(string? schoolId, SaveInvSupplierRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvSupplierDto>();
            try
            {
                if (ValidateSupplier(req, out var name, out var gstin, out var pan, out var email, out var mobile) is { } err) return Result<InvSupplierDto>(400, err, null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvSupplierDto>();
                if (await Repo.SupplierNameExistsAsync(schoolId, name, null, ct)) return Result<InvSupplierDto>(409, $"A supplier named \"{name}\" already exists.", null);
                var now = DateTime.UtcNow;
                var s = new TbInvSupplier
                {
                    SchoolId = schoolId, SupplierName = name, ContactPerson = InventoryRules.Clean(req.ContactPerson), Mobile = mobile, Email = email, Address = InventoryRules.Clean(req.Address),
                    Gstin = gstin, Pan = pan, StatusId = active.Value, CreatedAt = now, UpdatedAt = now
                };
                await Repo.AddAsync(s, ct);
                try { await Repo.SaveChangesAsync(ct); }
                catch (Exception ex) when (IsUnique(ex)) { Repo.ClearTracking(); return Result<InvSupplierDto>(409, $"A supplier named \"{name}\" already exists.", null); }
                return Result(201, "Supplier created.", SupplierDto(s, active.Value));
            }
            catch (Exception ex) { return Error<InvSupplierDto>(ex, "creating an inventory supplier"); }
        }

        public async Task<ApiResponse<InvSupplierDto>> UpdateSupplierAsync(string? schoolId, long id, SaveInvSupplierRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvSupplierDto>();
            try
            {
                if (ValidateSupplier(req, out var name, out var gstin, out var pan, out var email, out var mobile) is { } err) return Result<InvSupplierDto>(400, err, null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvSupplierDto>();
                var s = await Repo.GetSupplierAsync(schoolId, id, ct);
                if (s == null) return Result<InvSupplierDto>(404, "Supplier not found.", null);
                if (await Repo.SupplierNameExistsAsync(schoolId, name, id, ct)) return Result<InvSupplierDto>(409, $"A supplier named \"{name}\" already exists.", null);
                s.SupplierName = name; s.ContactPerson = InventoryRules.Clean(req.ContactPerson); s.Mobile = mobile; s.Email = email; s.Address = InventoryRules.Clean(req.Address);
                s.Gstin = gstin; s.Pan = pan; s.UpdatedAt = DateTime.UtcNow;
                try { await Repo.SaveChangesAsync(ct); }
                catch (Exception ex) when (IsUnique(ex)) { Repo.ClearTracking(); return Result<InvSupplierDto>(409, $"A supplier named \"{name}\" already exists.", null); }
                return Result(200, "Supplier updated.", SupplierDto(s, active.Value));
            }
            catch (Exception ex) { return Error<InvSupplierDto>(ex, "updating an inventory supplier"); }
        }

        public async Task<ApiResponse<InvSupplierDto>> ToggleSupplierAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvSupplierDto>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct); var inactive = await Repo.GetStatusIdAsync(StatusNames.Inactive, ct);
                if (active == null || inactive == null) return ConfigError<InvSupplierDto>();
                var s = await Repo.GetSupplierAsync(schoolId, id, ct);
                if (s == null) return Result<InvSupplierDto>(404, "Supplier not found.", null);
                s.StatusId = s.StatusId == active ? inactive.Value : active.Value; s.UpdatedAt = DateTime.UtcNow;
                await Repo.SaveChangesAsync(ct);
                return Result(200, s.StatusId == active ? "Supplier activated." : "Supplier deactivated.", SupplierDto(s, active.Value));
            }
            catch (Exception ex) { return Error<InvSupplierDto>(ex, "toggling an inventory supplier"); }
        }

        // ================================================================ items
        public async Task<ApiResponse<InvPageDto<InvItemDto>>> GetItemsAsync(string? schoolId, bool canManage, long? categoryId, string? search, bool lowStock, bool expiring, string? status, int? page, int? pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvPageDto<InvItemDto>>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct); var inactive = await Repo.GetStatusIdAsync(StatusNames.Inactive, ct);
                if (active == null || inactive == null) return ConfigError<InvPageDto<InvItemDto>>();
                int? statusFilter = (status ?? "active").ToLowerInvariant() switch { "all" => null, "inactive" => inactive, _ => active };
                var p = InventoryRules.Page(page); var size = InventoryRules.PageSize(pageSize);
                var filter = new InvItemFilter
                {
                    CategoryId = categoryId, StatusId = statusFilter, Search = search, LowStock = lowStock,
                    ExpiringBy = expiring ? SchoolClock.Today().AddDays(InventoryRules.ExpiringSoonDays) : null
                };
                var (rows, total) = await Repo.GetItemsAsync(schoolId, filter, p, size, ct);
                var items = rows.Select(r => { var d = ToDto(r); d.IsActive = r.Item.StatusId == active; return d; }).ToList();
                return Result(200, "Successfully fetched", new InvPageDto<InvItemDto> { CanManage = canManage, Items = items, TotalCount = total, Page = p, PageSize = size });
            }
            catch (Exception ex) { return Error<InvPageDto<InvItemDto>>(ex, "listing inventory items"); }
        }

        private async Task<ApiResponse<InvItemDto>> ReplyAsync(string schoolId, long id, int code, string message, CancellationToken ct)
        {
            var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
            var row = await Repo.GetItemRowAsync(schoolId, id, ct);
            if (row == null || active == null) return Result<InvItemDto>(500, "Something went wrong. Please try again later.", null);
            var d = ToDto(row); d.IsActive = row.Item.StatusId == active;
            return Result(code, message, d);
        }

        public async Task<ApiResponse<InvItemDto>> GetItemAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvItemDto>();
            try
            {
                if (await Repo.GetItemRowAsync(schoolId, id, ct) == null) return Result<InvItemDto>(404, "Item not found.", null);
                return await ReplyAsync(schoolId, id, 200, "Successfully fetched", ct);
            }
            catch (Exception ex) { return Error<InvItemDto>(ex, "reading an inventory item"); }
        }

        private sealed record ItemFields(string Name, string? Barcode, string? Description, long CategoryId, long UnitId, string Type, decimal Purchase, decimal? Selling, bool Saleable, decimal Gst, decimal Min, DateOnly? Expiry);

        private static (ItemFields? Fields, string? Error) ValidateItem(SaveInvItemRequest r)
        {
            var name = InventoryRules.Clean(r.ItemName);
            if (name == null) return (null, "Item name is required.");
            var type = InventoryRules.CleanUpper(r.ItemType);
            if (type == null || !InvItemTypes.All.Contains(type)) return (null, "Item type must be CONSUMABLE or ASSET.");
            var purchase = r.PurchasePrice ?? 0m; var gst = r.GstPercent ?? 0m; var min = r.MinStockLevel ?? 0m;
            if (InventoryRules.MoneyError(purchase, "Purchase price") is { } e1) return (null, e1);
            if (r.SellingPrice is decimal sp && InventoryRules.MoneyError(sp, "Selling price") is { } e2) return (null, e2);
            if (r.IsSaleable && r.SellingPrice == null) return (null, "A saleable item needs a selling price.");
            if (gst < 0 || gst > 100 || !InventoryRules.HasAtMostDecimals(gst, 2)) return (null, "GST must be between 0 and 100 with at most 2 decimal places.");
            if (min < 0 || !InventoryRules.HasAtMostDecimals(min, 3) || min >= 999_999_999m) return (null, "Minimum stock level must be 0 or more with at most 3 decimal places.");
            var barcode = InventoryRules.Clean(r.Barcode);
            return (new ItemFields(name, barcode, InventoryRules.Clean(r.Description), r.CategoryId!.Value, r.UnitId!.Value, type, purchase, r.SellingPrice, r.IsSaleable, gst, min, r.ExpiryDate), null);
        }

        public async Task<ApiResponse<InvItemDto>> CreateItemAsync(string? schoolId, long userId, SaveInvItemRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvItemDto>();
            try
            {
                var (f, err) = ValidateItem(req);
                if (f == null) return Result<InvItemDto>(400, err!, null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvItemDto>();

                var category = await Repo.GetCategoryAsync(schoolId, f.CategoryId, ct);
                if (category == null) return Result<InvItemDto>(404, "Category not found.", null);
                if (category.StatusId != active) return Result<InvItemDto>(400, $"The category \"{category.CategoryName}\" is deactivated. Choose another.", null);
                if (await Repo.GetUnitAsync(f.UnitId, ct) == null) return Result<InvItemDto>(404, "Unit not found.", null);
                if (f.Barcode != null && await Repo.BarcodeExistsAsync(schoolId, f.Barcode, null, ct)) return Result<InvItemDto>(409, $"Another item already uses the barcode \"{f.Barcode}\".", null);

                // two items created at the same moment must not get the same code: lock per school, retry on the unique index as the backstop
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await using var tx = await Repo.BeginTransactionAsync(ct);
                    try
                    {
                        await Repo.LockNumbersAsync("invitem", schoolId, ct);
                        var next = await Repo.GetMaxItemSequenceAsync(schoolId, ct) + 1;
                        var now = DateTime.UtcNow;
                        var item = new TbInvItem
                        {
                            SchoolId = schoolId, ItemCode = InventoryRules.ItemCode(next), Barcode = f.Barcode, ItemName = f.Name, Description = f.Description,
                            CategoryId = f.CategoryId, UnitId = f.UnitId, ItemType = f.Type, PurchasePrice = f.Purchase, SellingPrice = f.Selling, IsSaleable = f.Saleable,
                            GstPercent = f.Gst, MinStockLevel = f.Min, CurrentQuantity = 0, ExpiryDate = f.Expiry, StatusId = active.Value, CreatedBy = userId, CreatedAt = now, UpdatedAt = now
                        };
                        await Repo.AddAsync(item, ct);
                        await Repo.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        return await ReplyAsync(schoolId, item.ItemId, 201, "Item created. Use Stock Adjustment (opening balance) or Add Stock to put quantity on it.", ct);
                    }
                    catch (Exception ex) when (IsUnique(ex, BarcodeIndex)) { Repo.ClearTracking(); return Result<InvItemDto>(409, $"Another item already uses the barcode \"{f.Barcode}\".", null); }
                    catch (Exception ex) when (IsUnique(ex, ItemCodeIndex) && attempt < 2) { Logger.LogWarning(ex, "Item code collision for school {SchoolId}; retrying", schoolId); Repo.ClearTracking(); }
                }
                return Result<InvItemDto>(409, "Could not allocate an item code. Nothing was saved. Please try again.", null);
            }
            catch (Exception ex) { return Error<InvItemDto>(ex, "creating an inventory item"); }
        }

        public async Task<ApiResponse<InvItemDto>> UpdateItemAsync(string? schoolId, long id, SaveInvItemRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvItemDto>();
            try
            {
                var (f, err) = ValidateItem(req);
                if (f == null) return Result<InvItemDto>(400, err!, null);
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvItemDto>();
                var item = await Repo.GetItemAsync(schoolId, id, ct);
                if (item == null) return Result<InvItemDto>(404, "Item not found.", null);

                if (f.CategoryId != item.CategoryId)
                {
                    var category = await Repo.GetCategoryAsync(schoolId, f.CategoryId, ct);
                    if (category == null) return Result<InvItemDto>(404, "Category not found.", null);
                    if (category.StatusId != active) return Result<InvItemDto>(400, $"The category \"{category.CategoryName}\" is deactivated. Choose another.", null);
                }
                if (f.UnitId != item.UnitId && await Repo.GetUnitAsync(f.UnitId, ct) == null) return Result<InvItemDto>(404, "Unit not found.", null);
                if (f.Barcode != null && await Repo.BarcodeExistsAsync(schoolId, f.Barcode, id, ct)) return Result<InvItemDto>(409, $"Another item already uses the barcode \"{f.Barcode}\".", null);

                // the code, the quantity, the photo and the status are NOT changed here (quantity: only through the ledger)
                item.ItemName = f.Name; item.Barcode = f.Barcode; item.Description = f.Description; item.CategoryId = f.CategoryId; item.UnitId = f.UnitId; item.ItemType = f.Type;
                item.PurchasePrice = f.Purchase; item.SellingPrice = f.Selling; item.IsSaleable = f.Saleable; item.GstPercent = f.Gst; item.MinStockLevel = f.Min; item.ExpiryDate = f.Expiry;
                item.UpdatedAt = DateTime.UtcNow;
                try { await Repo.SaveChangesAsync(ct); }
                catch (Exception ex) when (IsUnique(ex, BarcodeIndex)) { Repo.ClearTracking(); return Result<InvItemDto>(409, $"Another item already uses the barcode \"{f.Barcode}\".", null); }
                return await ReplyAsync(schoolId, id, 200, "Item updated.", ct);
            }
            catch (Exception ex) { return Error<InvItemDto>(ex, "updating an inventory item"); }
        }

        public async Task<ApiResponse<InvItemDto>> ToggleItemAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvItemDto>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct); var inactive = await Repo.GetStatusIdAsync(StatusNames.Inactive, ct);
                if (active == null || inactive == null) return ConfigError<InvItemDto>();
                var item = await Repo.GetItemAsync(schoolId, id, ct);
                if (item == null) return Result<InvItemDto>(404, "Item not found.", null);
                item.StatusId = item.StatusId == active ? inactive.Value : active.Value; item.UpdatedAt = DateTime.UtcNow;   // hidden, never removed; stock and history stay
                await Repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, id, 200, item.StatusId == active ? "Item activated." : "Item deactivated.", ct);
            }
            catch (Exception ex) { return Error<InvItemDto>(ex, "toggling an inventory item"); }
        }

        // ================================================================ photo
        public async Task<ApiResponse<InvItemDto>> UploadPhotoAsync(string? schoolId, long id, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvItemDto>();
            string? newKey = null;
            try
            {
                var item = await Repo.GetItemAsync(schoolId, id, ct);
                if (item == null) return Result<InvItemDto>(404, "Item not found.", null);
                var (image, error) = await ImageUploadValidator.ValidateAsync(content, fileName, length, "Photo", ct);
                if (image == null) return Result<InvItemDto>(400, error!, null);
                using (image)
                {
                    var code = await Repo.GetSchoolCodeAsync(schoolId, ct);
                    if (code == null) return Result<InvItemDto>(404, "School not found.", null);
                    var oldKey = item.PhotoPath;
                    newKey = await _files.SaveItemPhotoAsync(code, id, image.Content, image.Extension, ct);
                    item.PhotoPath = newKey; item.UpdatedAt = DateTime.UtcNow;
                    await Repo.SaveChangesAsync(ct);
                    if (oldKey != null) _files.Delete(oldKey);
                }
                return await ReplyAsync(schoolId, id, 200, "Photo saved.", ct);
            }
            catch (Exception ex)
            {
                if (newKey != null) _files.Delete(newKey);   // the row was not updated: do not leave an orphan file
                return Error<InvItemDto>(ex, "uploading an inventory item photo");
            }
        }

        public async Task<ApiResponse<InvItemDto>> RemovePhotoAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvItemDto>();
            try
            {
                var item = await Repo.GetItemAsync(schoolId, id, ct);
                if (item == null) return Result<InvItemDto>(404, "Item not found.", null);
                if (item.PhotoPath == null) return Result<InvItemDto>(404, "This item has no photo.", null);
                var key = item.PhotoPath; item.PhotoPath = null; item.UpdatedAt = DateTime.UtcNow;
                await Repo.SaveChangesAsync(ct);
                _files.Delete(key);
                return await ReplyAsync(schoolId, id, 200, "Photo removed.", ct);
            }
            catch (Exception ex) { return Error<InvItemDto>(ex, "removing an inventory item photo"); }
        }

        public async Task<(ApiResponse<InvItemDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenPhotoAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return (NoSchool<InvItemDto>(), null, null, null);
            try
            {
                var item = await Repo.GetItemRowAsync(schoolId, id, ct);   // school-scoped: another school's id is a plain 404
                if (item == null) return (Result<InvItemDto>(404, "Item not found.", null), null, null, null);
                if (item.Item.PhotoPath == null) return (Result<InvItemDto>(404, "This item has no photo.", null), null, null, null);
                var stream = _files.Open(item.Item.PhotoPath);
                if (stream == null)
                {
                    Logger.LogError("Inventory item {ItemId} has a photo in the database but its file is missing", id);
                    return (Result<InvItemDto>(404, "The photo file could not be found.", null), null, null, null);
                }
                var ext = Path.GetExtension(item.Item.PhotoPath).ToLowerInvariant();
                var type = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
                return (null, stream, type, $"{item.Item.ItemCode}{ext}");
            }
            catch (Exception ex) { return (Error<InvItemDto>(ex, "opening an inventory item photo"), null, null, null); }
        }

        // ================================================================ lookups
        public async Task<ApiResponse<List<InvUnitDto>>> GetUnitsAsync(CancellationToken ct)
        {
            try { return Result(200, "Successfully fetched", (await Repo.GetUnitsAsync(ct)).Select(u => new InvUnitDto { UnitId = u.UnitId, UnitName = u.UnitName, UnitCode = u.UnitCode }).ToList()); }
            catch (Exception ex) { return Error<List<InvUnitDto>>(ex, "listing inventory units"); }
        }

        public ApiResponse<List<InvReasonDto>> GetAdjustmentReasons() => Result(200, "Successfully fetched", new List<InvReasonDto>
        {
            new() { Code = InvReasonCodes.OpeningBalance, Label = "Opening balance", Direction = "IN" },
            new() { Code = InvReasonCodes.Damage, Label = "Damaged", Direction = "OUT" },
            new() { Code = InvReasonCodes.Loss, Label = "Lost", Direction = "OUT" },
            new() { Code = InvReasonCodes.Expired, Label = "Expired", Direction = "OUT" },
            new() { Code = InvReasonCodes.Correction, Label = "Count correction", Direction = "EITHER" },
        });
    }
}
