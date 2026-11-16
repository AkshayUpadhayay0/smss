using Xunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;

namespace smss_api_tests
{
    /// <summary>Inventory Stage 1 tests that need no database: validation rules, the role matrix on every controller, and the "no quantity on an item" guarantee.</summary>
    public class InventoryRulesTests
    {
        [Theory]
        [InlineData(1, true)] [InlineData(0.001, true)] [InlineData(0, false)] [InlineData(-1, false)] [InlineData(0.0001, false)]
        public void Quantity_must_be_positive_with_at_most_three_decimals(double q, bool ok) =>
            Assert.Equal(ok, InventoryRules.QuantityError((decimal)q) == null);

        [Theory]
        [InlineData("DAMAGE", -1, true)] [InlineData("DAMAGE", 1, false)] [InlineData("LOSS", 2, false)] [InlineData("EXPIRED", -3, true)] [InlineData("EXPIRED", 3, false)]
        [InlineData("CORRECTION", 5, true)] [InlineData("CORRECTION", -5, true)] [InlineData("CORRECTION", 0, false)]
        [InlineData("OPENING_BALANCE", 10, true)] [InlineData("OPENING_BALANCE", -10, false)] [InlineData("NOPE", 1, false)]
        public void Adjustment_sign_rules(string reason, double change, bool ok) =>
            Assert.Equal(ok, InventoryRules.AdjustmentError(reason, (decimal)change) == null);

        [Theory]
        [InlineData("22AAAAA0000A1Z5", true)] [InlineData("22AAAAA0000A1Z", false)] [InlineData("abc", false)] [InlineData(null, true)]
        public void Gstin_format(string? v, bool ok) => Assert.Equal(ok, InventoryRules.ValidGstin(v));

        [Theory]
        [InlineData("ABCDE1234F", true)] [InlineData("ABCDE12345", false)] [InlineData(null, true)]
        public void Pan_format(string? v, bool ok) => Assert.Equal(ok, InventoryRules.ValidPan(v));

        [Fact]
        public void Blank_optional_text_becomes_null_and_codes_are_normalised()
        {
            Assert.Null(InventoryRules.Clean("   "));
            Assert.Null(InventoryRules.Clean(""));
            Assert.Equal("ab c", InventoryRules.Clean("  ab c "));
            Assert.Equal("STA-01", InventoryRules.NormalizeCode(" sta -01 "));
        }

        [Fact]
        public void Numbers_are_fixed_width()
        {
            Assert.Equal("ISS-2026-0001", InventoryRules.IssueNumber(2026, 1));
            Assert.Equal("ITM0042", InventoryRules.ItemCode(42));
        }

        [Fact]
        public void Issue_status_is_derived_from_the_quantities()
        {
            Assert.Equal("Outstanding", InvIssueStatuses.Of("Active", 5, 0));
            Assert.Equal("Partially Returned", InvIssueStatuses.Of("Active", 5, 2));
            Assert.Equal("Returned", InvIssueStatuses.Of("Active", 5, 5));
            Assert.Equal("Voided", InvIssueStatuses.Of("Voided", 5, 0));
        }

        [Fact]
        public void LikePattern_escapes_wildcards() => Assert.Equal("%50\\%\\_x%", InventoryRules.LikePattern("50%_x"));

        [Fact]
        public void Item_requests_carry_no_quantity_field()
        {
            // the item create / update endpoints must not be able to receive a quantity: stock changes only through the ledger
            var names = typeof(SaveInvItemRequest).GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain(names, n => n.Contains("Quantity", StringComparison.OrdinalIgnoreCase) || n.Contains("Stock", StringComparison.OrdinalIgnoreCase) && n != nameof(SaveInvItemRequest.MinStockLevel));
            Assert.DoesNotContain(names, n => n.Contains("Opening", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Store_keeper_can_be_given_to_an_employee_login_and_driver_still_cannot()
        {
            Assert.Contains("STORE_KEEPER", RoleCodes.AssignableToEmployees);
            Assert.DoesNotContain("DRIVER", RoleCodes.AssignableToEmployees);
            foreach (var old in new[] { "SCHOOL_ADMIN", "PRINCIPAL", "TEACHER", "ACCOUNTANT", "ADMISSION_STAFF" }) Assert.Contains(old, RoleCodes.AssignableToEmployees);
        }
    }

    public class InventoryRoleMatrixTests
    {
        private static readonly string[] AllRoles = { "School Admin", "Store Keeper", "Principal", "Teacher", "Accountant", "Admission Staff", "Student", "Guardian", "Driver", "Super Admin" };
        private static readonly string[] Managers = { "School Admin", "Store Keeper" };
        private static readonly string[] Viewers = { "School Admin", "Store Keeper", "Principal" };
        private const string School = "SCH1";

        private static ApiResponse<T> Ok<T>(T d) => Fake.Ok(d);

        private static IInventoryCatalogService Catalog() => Fake.Of<IInventoryCatalogService>(new()
        {
            ["GetCategoriesAsync"] = Ok(new InvListDto<InvCategoryDto>()), ["CreateCategoryAsync"] = Ok(new InvCategoryDto()), ["UpdateCategoryAsync"] = Ok(new InvCategoryDto()), ["ToggleCategoryAsync"] = Ok(new InvCategoryDto()),
            ["GetSuppliersAsync"] = Ok(new InvListDto<InvSupplierDto>()), ["CreateSupplierAsync"] = Ok(new InvSupplierDto()), ["UpdateSupplierAsync"] = Ok(new InvSupplierDto()), ["ToggleSupplierAsync"] = Ok(new InvSupplierDto()),
            ["GetItemsAsync"] = Ok(new InvPageDto<InvItemDto>()), ["GetItemAsync"] = Ok(new InvItemDto()), ["CreateItemAsync"] = Ok(new InvItemDto()), ["UpdateItemAsync"] = Ok(new InvItemDto()), ["ToggleItemAsync"] = Ok(new InvItemDto()),
            ["RemovePhotoAsync"] = Ok(new InvItemDto()), ["GetUnitsAsync"] = Ok(new List<InvUnitDto>()),
        });
        private static IInventoryStockService Stock() => Fake.Of<IInventoryStockService>(new()
        {
            ["GetStockInsAsync"] = Ok(new InvPageDto<InvStockInDto>()), ["CreateStockInAsync"] = Ok(new InvStockInDto()), ["VoidStockInAsync"] = Ok(new InvStockInDto()),
            ["GetAdjustmentsAsync"] = Ok(new InvPageDto<InvMovementDto>()), ["CreateAdjustmentAsync"] = Ok(new InvMovementDto()), ["GetMovementsAsync"] = Ok(new InvPageDto<InvMovementDto>()),
        });
        private static IInventoryIssueService Issues() => Fake.Of<IInventoryIssueService>(new()
        {
            ["GetIssuesAsync"] = Ok(new InvPageDto<InvIssueDto>()), ["GetIssueAsync"] = Ok(new InvIssueDto()), ["CreateIssueAsync"] = Ok(new InvIssueDto()), ["ReturnAsync"] = Ok(new InvIssueDto()),
            ["VoidIssueAsync"] = Ok(new InvIssueDto()), ["SearchRecipientsAsync"] = Ok(new List<InvRecipientDto>()),
        });
        private static IInventoryDashboardService Dashboard() => Fake.Of<IInventoryDashboardService>(new() { ["GetSummaryAsync"] = Ok(new InvDashboardDto()) });

        private static T As<T>(T c, string role) where T : ControllerBase => Fake.WithUser(c, Fake.User(School, 7, role));

        // (name, needs manage, call)
        private static List<(string Name, bool Manage, Func<string, Task<IActionResult>> Call)> Endpoints()
        {
            var cat = Catalog(); var st = Stock(); var iss = Issues(); var dash = Dashboard();
            return new()
            {
                ("category list", false, r => As(new InventoryCategoryController(cat), r).List(false, default)),
                ("category create", true, r => As(new InventoryCategoryController(cat), r).Create(new SaveInvCategoryRequest(), default)),
                ("category update", true, r => As(new InventoryCategoryController(cat), r).Update(1, new SaveInvCategoryRequest(), default)),
                ("category toggle", true, r => As(new InventoryCategoryController(cat), r).Toggle(1, default)),
                ("supplier list", false, r => As(new InventorySupplierController(cat), r).List(false, default)),
                ("supplier create", true, r => As(new InventorySupplierController(cat), r).Create(new SaveInvSupplierRequest(), default)),
                ("supplier update", true, r => As(new InventorySupplierController(cat), r).Update(1, new SaveInvSupplierRequest(), default)),
                ("supplier toggle", true, r => As(new InventorySupplierController(cat), r).Toggle(1, default)),
                ("item list", false, r => As(new InventoryItemController(cat), r).List(null, null, false, false, null, null, null, default)),
                ("item get", false, r => As(new InventoryItemController(cat), r).Get(1, default)),
                ("item create", true, r => As(new InventoryItemController(cat), r).Create(new SaveInvItemRequest(), default)),
                ("item update", true, r => As(new InventoryItemController(cat), r).Update(1, new SaveInvItemRequest(), default)),
                ("item toggle", true, r => As(new InventoryItemController(cat), r).Toggle(1, default)),
                ("item photo remove", true, r => As(new InventoryItemController(cat), r).RemovePhoto(1, default)),
                ("stock-in list", false, r => As(new InventoryStockInController(st), r).List(null, null, null, null, false, null, null, default)),
                ("stock-in create", true, r => As(new InventoryStockInController(st), r).Create(new CreateInvStockInRequest(), default)),
                ("stock-in void", true, r => As(new InventoryStockInController(st), r).Void(1, new InvVoidRequest(), default)),
                ("adjustment list", false, r => As(new InventoryStockAdjustmentController(st), r).List(null, null, null, null, null, null, default)),
                ("adjustment create", true, r => As(new InventoryStockAdjustmentController(st), r).Create(new CreateInvAdjustmentRequest(), default)),
                ("movement list", false, r => As(new InventoryStockMovementController(st), r).List(1, null, null, default)),
                ("issue list", false, r => As(new InventoryIssueController(iss), r).List(null, null, null, null, null, null, null, default)),
                ("issue get", false, r => As(new InventoryIssueController(iss), r).Get(1, default)),
                ("issue create", true, r => As(new InventoryIssueController(iss), r).Create(new CreateInvIssueRequest(), default)),
                ("issue return", true, r => As(new InventoryIssueController(iss), r).Return(1, new InvReturnRequest(), default)),
                ("issue void", true, r => As(new InventoryIssueController(iss), r).Void(1, new InvVoidRequest(), default)),
                ("lookup units", false, r => As(new InventoryLookupController(cat, iss), r).Units(default)),
                ("lookup recipients", true, r => As(new InventoryLookupController(cat, iss), r).Recipients("STAFF", null, default)),
                ("dashboard", false, r => As(new InventoryDashboardController(dash), r).Summary(default)),
            };
        }

        [Fact]
        public async Task Every_role_gets_403_or_200_exactly_as_the_matrix_says()
        {
            foreach (var (name, manage, call) in Endpoints())
                foreach (var role in AllRoles)
                {
                    var expected = (manage ? Managers : Viewers).Contains(role) ? 200 : 403;
                    var code = Fake.Code(await call(role));
                    Assert.True(expected == code, $"{name} as {role}: expected {expected} but got {code}");
                }
        }

        [Fact]
        public void Reasons_lookup_and_photo_upload_follow_the_same_rules()
        {
            var catalog = new InventoryCatalogService(null!, null!, NullLogger<InventoryCatalogService>.Instance);
            foreach (var role in AllRoles)
            {
                var lookup = As(new InventoryLookupController(catalog, Issues()), role).Reasons();
                Assert.Equal(Viewers.Contains(role) ? 200 : 403, Fake.Code(lookup));
            }
        }

        [Fact]
        public async Task Photo_upload_is_refused_to_non_managers_before_anything_else()
        {
            foreach (var role in AllRoles)
            {
                var code = Fake.Code(await As(new InventoryItemController(Catalog()), role).UploadPhoto(1, null, default));
                Assert.Equal(Managers.Contains(role) ? 400 : 403, code);   // managers reach the "file is required" check
                // a viewer gets past the role check (the unconfigured fake then returns nothing to stream, which throws inside File()); anyone else is a clean 403
                var dlCode = 0;
                try { dlCode = Fake.Code(await As(new InventoryItemController(Catalog()), role).DownloadPhoto(1, default)); } catch (FormatException) { dlCode = -1; }
                if (Viewers.Contains(role)) Assert.NotEqual(403, dlCode); else Assert.Equal(403, dlCode);
            }
        }

        [Fact]
        public void Every_inventory_controller_requires_authentication()
        {
            var controllers = typeof(InventoryControllerBase).Assembly.GetTypes().Where(t => typeof(InventoryControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            Assert.True(controllers.Count >= 9);
            foreach (var c in controllers) Assert.NotEmpty(c.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        }

        [Fact]
        public async Task No_school_in_the_token_is_a_400_not_a_leak()
        {
            var svc = new InventoryDashboardService(null!, NullLogger<InventoryDashboardService>.Instance);
            var r = await svc.GetSummaryAsync(null, true, default);
            Assert.Equal(400, r.StatusCode);
        }
    }
}
