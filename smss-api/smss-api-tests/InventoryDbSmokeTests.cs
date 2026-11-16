using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.context;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;

namespace smss_api_tests
{
    /// <summary>A fact that runs only against a SCRATCH database: set SMSS_INV_TEST_CONN to its connection string. Without it the tests are reported as skipped, never as passed.</summary>
    public sealed class InvDbFactAttribute : FactAttribute
    {
        public const string Variable = "SMSS_INV_TEST_CONN";
        public InvDbFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))) Skip = $"Set {Variable} to a scratch database connection string to run the inventory database smoke tests."; }
    }

    /// <summary>One request's worth of services over a fresh DbContext, like the scoped DI container builds them.</summary>
    public sealed class InvRig : IDisposable
    {
        public readonly dbContext Db; public readonly InventoryRepository Repo;
        public readonly InventoryCatalogService Catalog; public readonly InventoryStockService Stock; public readonly InventoryIssueService Issue; public readonly InventoryDashboardService Dashboard;
        public InvRig(string conn, string photoRoot)
        {
            // strict on purpose: an unordered, unfiltered First/FirstOrDefault anywhere in the Inventory code (EF event 10103) would throw here instead of only logging a warning in production
            Db = new dbContext(new DbContextOptionsBuilder<dbContext>().UseNpgsql(conn).ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)).Options);
            Repo = new InventoryRepository(Db);
            var ledger = new StockLedgerService(Repo);
            Catalog = new InventoryCatalogService(Repo, new InventoryFileStore(photoRoot, NullLogger<InventoryFileStore>.Instance), NullLogger<InventoryCatalogService>.Instance);
            Stock = new InventoryStockService(Repo, ledger, NullLogger<InventoryStockService>.Instance);
            Issue = new InventoryIssueService(Repo, ledger, NullLogger<InventoryIssueService>.Instance);
            Dashboard = new InventoryDashboardService(Repo, NullLogger<InventoryDashboardService>.Instance);
        }
        public void Dispose() => Db.Dispose();
    }

    public class InventoryDbFixture
    {
        public readonly string Conn = Environment.GetEnvironmentVariable(InvDbFactAttribute.Variable) ?? "";
        public readonly string PhotoRoot = Path.Combine(Path.GetTempPath(), "smss-inv-photos-" + Guid.NewGuid().ToString("N"));
        public string A = "", B = "";               // two real schools in the scratch copy
        public long UserA, UserB, UnitId;
        public string EmpA = "", EmpB = "", StudentA = "";
        public long DeptA, DeptB;
        public string Tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();   // makes names / codes unique per run

        public InventoryDbFixture()
        {
            if (Conn == "") return;
            // guard rail: never run against the real database
            var db = new NpgsqlConnectionStringBuilder(Conn).Database ?? "";
            if (!db.StartsWith("smss_scratch", StringComparison.Ordinal)) throw new InvalidOperationException($"Refusing to run: database '{db}' is not a scratch database (its name must start with 'smss_scratch').");
            A = Scalar<string>("select school_id from tb_schools where school_code='GHS-RKE-2026'");
            B = Scalar<string>("select school_id from tb_schools where school_code='LAS-DDN-2026'");
            UserA = Scalar<long>($"select user_id from tb_users where school_id='{A}' order by user_id limit 1");
            UserB = Scalar<long>($"select user_id from tb_users where school_id='{B}' order by user_id limit 1");
            UnitId = Scalar<long>("select unit_id from lut_inv_unit where unit_code='pcs'");
            var active = "(select sid from lut_status where sname='Active' and stype='general status')";
            EmpA = Scalar<string>($"select employee_id from tb_employees where school_id='{A}' and status_id={active} order by employee_id limit 1");
            EmpB = Scalar<string>($"select employee_id from tb_employees where school_id='{B}' and status_id={active} order by employee_id limit 1");
            StudentA = Scalar<string>($"select student_id from tb_students where school_id='{A}' and status_id={active} order by student_id limit 1");
            DeptA = Scalar<long>($"select department_id from tb_employee_departments where school_id='{A}' and status_id={active} order by department_id limit 1");
            DeptB = Scalar<long>($"select department_id from tb_employee_departments where school_id='{B}' and status_id={active} order by department_id limit 1");
        }

        public T Scalar<T>(string sql) { using var c = new NpgsqlConnection(Conn); c.Open(); using var cmd = new NpgsqlCommand(sql, c); return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T)); }

        /// <summary>Runs raw SQL and returns the Postgres SQLSTATE it failed with, or null when it succeeded.</summary>
        public string? SqlState(string sql)
        {
            try { using var c = new NpgsqlConnection(Conn); c.Open(); using var cmd = new NpgsqlCommand(sql, c); cmd.ExecuteNonQuery(); return null; }
            catch (PostgresException ex) { return ex.SqlState; }
        }

        public InvRig Rig() => new(Conn, PhotoRoot);
    }

    public class InventoryDbSmokeTests : IClassFixture<InventoryDbFixture>
    {
        private const string Check = "23514", Fk = "23503", Unique = "23505", Restrict = "23001";
        private readonly InventoryDbFixture F;
        public InventoryDbSmokeTests(InventoryDbFixture f) => F = f;

        // ---------------------------------------------------------------- helpers
        private async Task<long> CategoryAsync(string school, long user, string? suffix = null)
        {
            using var r = F.Rig();
            var res = await r.Catalog.CreateCategoryAsync(school, new SaveInvCategoryRequest { CategoryName = "Cat " + F.Tag + suffix, CategoryCode = "C" + F.Tag + suffix + Guid.NewGuid().ToString("N")[..4] }, default);
            Assert.Equal(201, res.StatusCode);
            return res.Data!.CategoryId;
        }

        private async Task<long> ItemAsync(string school, long user, decimal opening, DateOnly? expiry = null, decimal min = 0)
        {
            var cat = await CategoryAsync(school, user);
            using var r = F.Rig();
            var res = await r.Catalog.CreateItemAsync(school, user, new SaveInvItemRequest
            {
                ItemName = "Item " + F.Tag + Guid.NewGuid().ToString("N")[..5], CategoryId = cat, UnitId = F.UnitId, ItemType = "CONSUMABLE", PurchasePrice = 10, MinStockLevel = min, ExpiryDate = expiry
            }, default);
            Assert.Equal(201, res.StatusCode);
            Assert.Equal(0, res.Data!.CurrentQuantity);   // a new item always starts at zero
            if (opening > 0) Assert.Equal(201, (await Adjust(school, user, res.Data.ItemId, "OPENING_BALANCE", opening)).StatusCode);
            return res.Data.ItemId;
        }

        private async Task<ApiResponse<InvMovementDto>> Adjust(string school, long user, long item, string reason, decimal change, string remarks = "test")
        {
            using var r = F.Rig();
            return await r.Stock.CreateAdjustmentAsync(school, user, new CreateInvAdjustmentRequest { ItemId = item, ReasonCode = reason, QuantityChange = change, Remarks = remarks }, default);
        }

        private async Task<ApiResponse<InvIssueDto>> IssueAsync(string school, long user, long item, decimal qty, string? deptOrNull = null)
        {
            using var r = F.Rig();
            return await r.Issue.CreateIssueAsync(school, user, new CreateInvIssueRequest
            {
                RecipientType = "DEPARTMENT", RecipientId = deptOrNull ?? (school == F.A ? F.DeptA : F.DeptB).ToString(), IssueDate = SchoolDate(),
                Lines = new() { new CreateInvIssueLine { ItemId = item, Quantity = qty } }
            }, default);
        }

        private static DateOnly SchoolDate() => smss_api_service_layer.helper.SchoolClock.Today();

        private decimal Qty(long item) => F.Scalar<decimal>($"select current_quantity from tb_inv_items where item_id={item}");
        private decimal LedgerSum(long item) => F.Scalar<decimal>($"select coalesce(sum(quantity_change),0) from tb_inv_stock_movements where item_id={item}");
        private void AssertReconciled(long item) { Assert.Equal(Qty(item), LedgerSum(item)); }

        private const string Reconciliation =
            "select count(*) from tb_inv_items i left join (select item_id, sum(quantity_change) t from tb_inv_stock_movements group by item_id) m on m.item_id=i.item_id where i.current_quantity <> coalesce(m.t,0)";

        // ---------------------------------------------------------------- concurrency
        [InvDbFact]
        public async Task Eight_simultaneous_issues_of_the_last_five_units_issue_exactly_five()
        {
            var item = await ItemAsync(F.A, F.UserA, 5);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => IssueAsync(F.A, F.UserA, item, 1))));
            Assert.Equal(5, results.Count(x => x.StatusCode == 201));
            Assert.Equal(3, results.Count(x => x.StatusCode == 409));
            Assert.All(results.Where(x => x.StatusCode == 409), x => Assert.Contains("Not enough stock", x.Message));
            Assert.Equal(0, Qty(item));
            AssertReconciled(item);
            Assert.Equal(5, F.Scalar<int>($"select count(*) from tb_inv_stock_movements where item_id={item} and movement_type='ISSUE'"));
            Assert.Equal(0, F.Scalar<int>(Reconciliation));
            // a refused issue writes nothing at all: only 5 issue headers exist for this item
            Assert.Equal(5, F.Scalar<int>($"select count(distinct issue_id) from tb_inv_issue_lines where item_id={item}"));
        }

        [InvDbFact]
        public async Task Eight_simultaneous_adjustments_cannot_take_more_than_is_on_hand_and_the_ledger_matches()
        {
            // no advisory lock here: this exercises the atomic UPDATE ... WHERE current_quantity + delta >= 0 on its own
            var item = await ItemAsync(F.A, F.UserA, 5);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Adjust(F.A, F.UserA, item, "LOSS", -1))));
            Assert.Equal(5, results.Count(x => x.StatusCode == 201));
            Assert.Equal(3, results.Count(x => x.StatusCode == 409));
            Assert.Equal(0, Qty(item));
            AssertReconciled(item);
            Assert.Equal(0, F.Scalar<int>(Reconciliation));
        }

        [InvDbFact]
        public async Task Concurrent_issues_of_two_units_each_from_five_leave_one()
        {
            var item = await ItemAsync(F.A, F.UserA, 5);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => IssueAsync(F.A, F.UserA, item, 2))));
            Assert.Equal(2, results.Count(x => x.StatusCode == 201));
            Assert.Equal(6, results.Count(x => x.StatusCode == 409));
            Assert.Equal(1, Qty(item));
            AssertReconciled(item);
        }

        // ---------------------------------------------------------------- ledger / reconciliation
        [InvDbFact]
        public async Task Every_operation_keeps_the_ledger_equal_to_the_quantity()
        {
            var item = await ItemAsync(F.A, F.UserA, 20);
            using (var r = F.Rig())
            {
                var si = await r.Stock.CreateStockInAsync(F.A, F.UserA, new CreateInvStockInRequest { ItemId = item, Quantity = 7.5m, UnitCost = 12.5m, ReceivedDate = SchoolDate(), BillNumber = "B1" }, default);
                Assert.Equal(201, si.StatusCode);
                Assert.Equal(27.5m, Qty(item));
                Assert.Equal(12.5m, F.Scalar<decimal>($"select purchase_price from tb_inv_items where item_id={item}"));   // last stock-in cost
            }
            var issue = await IssueAsync(F.A, F.UserA, item, 4);
            Assert.Equal(201, issue.StatusCode);
            Assert.Matches(@"^ISS-\d{4}-\d{4}$", issue.Data!.IssueNumber);
            Assert.Equal("Outstanding", issue.Data.Status);
            using (var r = F.Rig())
            {
                var lineId = issue.Data.Lines[0].IssueLineId;
                var good = await r.Issue.ReturnAsync(F.A, F.UserA, issue.Data.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = lineId, Quantity = 1, Condition = "GOOD" } } }, default);
                Assert.Equal(200, good.StatusCode);
                Assert.Equal(24.5m, Qty(item));   // 27.5 - 4 + 1
                Assert.Equal("Partially Returned", good.Data!.Status);
            }
            using (var r = F.Rig())
            {
                var lineId = issue.Data.Lines[0].IssueLineId;
                var dmg = await r.Issue.ReturnAsync(F.A, F.UserA, issue.Data.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = lineId, Quantity = 3, Condition = "DAMAGED" } } }, default);
                Assert.Equal(200, dmg.StatusCode);
                Assert.Equal(24.5m, Qty(item));   // a DAMAGED return is recorded, no stock goes back
                Assert.Equal("Returned", dmg.Data!.Status);
            }
            Assert.Equal((await Adjust(F.A, F.UserA, item, "DAMAGE", -2.25m)).StatusCode, 201);
            Assert.Equal(22.25m, Qty(item));
            AssertReconciled(item);
            Assert.Equal(0, F.Scalar<int>(Reconciliation));
            // quantity_after of every row follows the running total
            Assert.Equal(0, F.Scalar<int>($@"select count(*) from (select quantity_after, sum(quantity_change) over (order by movement_id) run from tb_inv_stock_movements where item_id={item}) x where x.quantity_after <> x.run"));
        }

        // ---------------------------------------------------------------- adjustments
        [InvDbFact]
        public async Task Adjustment_rules()
        {
            var item = await ItemAsync(F.A, F.UserA, 0);
            Assert.Equal(400, (await Adjust(F.A, F.UserA, item, "DAMAGE", 1)).StatusCode);                 // damage must take stock out
            Assert.Equal(400, (await Adjust(F.A, F.UserA, item, "CORRECTION", 0)).StatusCode);
            Assert.Equal(400, (await Adjust(F.A, F.UserA, item, "OPENING_BALANCE", 5, "  ")).StatusCode);  // remarks required
            Assert.Equal(400, (await Adjust(F.A, F.UserA, item, "NOPE", 5)).StatusCode);
            Assert.Equal(409, (await Adjust(F.A, F.UserA, item, "LOSS", -1)).StatusCode);                  // nothing on hand
            Assert.Equal(201, (await Adjust(F.A, F.UserA, item, "OPENING_BALANCE", 5)).StatusCode);
            Assert.Equal(409, (await Adjust(F.A, F.UserA, item, "OPENING_BALANCE", 5)).StatusCode);        // only one opening balance per item
            Assert.Equal(201, (await Adjust(F.A, F.UserA, item, "CORRECTION", -2)).StatusCode);
            Assert.Equal(201, (await Adjust(F.A, F.UserA, item, "CORRECTION", 4)).StatusCode);
            Assert.Equal(7, Qty(item));
            AssertReconciled(item);
        }

        [InvDbFact]
        public async Task Item_update_never_changes_the_quantity()
        {
            var item = await ItemAsync(F.A, F.UserA, 9);
            using var r = F.Rig();
            var row = (await r.Catalog.GetItemAsync(F.A, item, default)).Data!;
            var upd = await r.Catalog.UpdateItemAsync(F.A, item, new SaveInvItemRequest
            {
                ItemName = row.ItemName + " v2", CategoryId = row.CategoryId, UnitId = row.UnitId, ItemType = "ASSET", PurchasePrice = 99, MinStockLevel = 3, IsSaleable = true, SellingPrice = 120
            }, default);
            Assert.Equal(200, upd.StatusCode);
            Assert.Equal(9, Qty(item));
            Assert.Equal(row.ItemCode, upd.Data!.ItemCode);
            AssertReconciled(item);
        }

        // ---------------------------------------------------------------- void rules
        [InvDbFact]
        public async Task Stock_in_void_rules()
        {
            var item = await ItemAsync(F.A, F.UserA, 0);
            long Make(decimal q) { using var r = F.Rig(); var s = r.Stock.CreateStockInAsync(F.A, F.UserA, new CreateInvStockInRequest { ItemId = item, Quantity = q, UnitCost = 5, ReceivedDate = SchoolDate() }, default).Result; Assert.Equal(201, s.StatusCode); return s.Data!.StockInId; }
            var first = Make(10);
            Assert.Equal(201, (await IssueAsync(F.A, F.UserA, item, 8)).StatusCode);   // 2 left
            using (var r = F.Rig())
            {
                var refused = await r.Stock.VoidStockInAsync(F.A, F.UserA, first, new InvVoidRequest { Reason = "wrong" }, default);
                Assert.Equal(409, refused.StatusCode);        // would take the quantity below zero
                Assert.Contains("only 2", refused.Message);
            }
            Assert.Equal(2, Qty(item));
            Assert.Equal("Active", F.Scalar<string>($"select status from tb_inv_stock_ins where stock_in_id={first}"));
            var second = Make(3);   // 5 on hand
            using (var r = F.Rig())
            {
                Assert.Equal(400, (await r.Stock.VoidStockInAsync(F.A, F.UserA, second, new InvVoidRequest { Reason = " " }, default)).StatusCode);   // a reason is required
                var ok = await r.Stock.VoidStockInAsync(F.A, F.UserA, second, new InvVoidRequest { Reason = "entered twice" }, default);
                Assert.Equal(200, ok.StatusCode);
                Assert.Equal("Voided", ok.Data!.Status);
            }
            using (var r = F.Rig()) Assert.Equal(409, (await r.Stock.VoidStockInAsync(F.A, F.UserA, second, new InvVoidRequest { Reason = "again" }, default)).StatusCode);   // never twice
            Assert.Equal(2, Qty(item));
            AssertReconciled(item);
            Assert.Equal(1, F.Scalar<int>($"select count(*) from tb_inv_stock_movements where item_id={item} and movement_type='STOCK_IN_VOID'"));
        }

        [InvDbFact]
        public async Task Stock_in_validation()
        {
            var item = await ItemAsync(F.A, F.UserA, 0);
            using var r = F.Rig();
            CreateInvStockInRequest Req(Action<CreateInvStockInRequest> tweak) { var q = new CreateInvStockInRequest { ItemId = item, Quantity = 1, UnitCost = 1, ReceivedDate = SchoolDate() }; tweak(q); return q; }
            Assert.Equal(400, (await r.Stock.CreateStockInAsync(F.A, F.UserA, Req(q => q.Quantity = 0), default)).StatusCode);
            Assert.Equal(400, (await r.Stock.CreateStockInAsync(F.A, F.UserA, Req(q => q.UnitCost = -1), default)).StatusCode);
            Assert.Equal(400, (await r.Stock.CreateStockInAsync(F.A, F.UserA, Req(q => q.ReceivedDate = SchoolDate().AddDays(2)), default)).StatusCode);   // not in the future
            Assert.Equal(400, (await r.Stock.CreateStockInAsync(F.A, F.UserA, Req(q => q.ExpiryDate = SchoolDate()), default)).StatusCode);                 // expiry after received
            var ok = await r.Stock.CreateStockInAsync(F.A, F.UserA, Req(q => q.ExpiryDate = SchoolDate().AddDays(100)), default);
            Assert.Equal(201, ok.StatusCode);
            Assert.Equal(SchoolDate().AddDays(100), DateOnly.FromDateTime(F.Scalar<DateTime>($"select expiry_date::timestamp from tb_inv_items where item_id={item}")));
        }

        [InvDbFact]
        public async Task Issue_void_and_return_rules()
        {
            var item = await ItemAsync(F.A, F.UserA, 10);
            var issue = await IssueAsync(F.A, F.UserA, item, 6);
            Assert.Equal(4, Qty(item));
            var lineId = issue.Data!.Lines[0].IssueLineId;
            using (var r = F.Rig())
            {
                var over = await r.Issue.ReturnAsync(F.A, F.UserA, issue.Data.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = lineId, Quantity = 7, Condition = "GOOD" } } }, default);
                Assert.Equal(400, over.StatusCode);       // can't return more than is outstanding
                var bad = await r.Issue.ReturnAsync(F.A, F.UserA, issue.Data.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = lineId, Quantity = 1, Condition = "BROKEN" } } }, default);
                Assert.Equal(400, bad.StatusCode);
            }
            Assert.Equal(4, Qty(item));
            using (var r = F.Rig())
                Assert.Equal(200, (await r.Issue.ReturnAsync(F.A, F.UserA, issue.Data.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = lineId, Quantity = 1, Condition = "GOOD" } } }, default)).StatusCode);
            using (var r = F.Rig())
            {
                var refused = await r.Issue.VoidIssueAsync(F.A, F.UserA, issue.Data.IssueId, new InvVoidRequest { Reason = "oops" }, default);
                Assert.Equal(409, refused.StatusCode);    // something was already returned
            }
            // a fresh issue with nothing returned can be voided, once, and restores its stock
            var clean = await IssueAsync(F.A, F.UserA, item, 2);
            Assert.Equal(3, Qty(item));
            using (var r = F.Rig())
            {
                Assert.Equal(400, (await r.Issue.VoidIssueAsync(F.A, F.UserA, clean.Data!.IssueId, new InvVoidRequest { Reason = "" }, default)).StatusCode);
                var ok = await r.Issue.VoidIssueAsync(F.A, F.UserA, clean.Data.IssueId, new InvVoidRequest { Reason = "issued by mistake" }, default);
                Assert.Equal(200, ok.StatusCode);
                Assert.Equal("Voided", ok.Data!.Status);
            }
            Assert.Equal(5, Qty(item));
            using (var r = F.Rig())
            {
                Assert.Equal(409, (await r.Issue.VoidIssueAsync(F.A, F.UserA, clean.Data.IssueId, new InvVoidRequest { Reason = "again" }, default)).StatusCode);
                var ret = await r.Issue.ReturnAsync(F.A, F.UserA, clean.Data.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = clean.Data.Lines[0].IssueLineId, Quantity = 1, Condition = "GOOD" } } }, default);
                Assert.Equal(409, ret.StatusCode);        // nothing can be returned against a voided issue
            }
            AssertReconciled(item);
        }

        [InvDbFact]
        public async Task Issue_recipient_rules()
        {
            var item = await ItemAsync(F.A, F.UserA, 10);
            using var r = F.Rig();
            CreateInvIssueRequest Req(string type, string id) => new() { RecipientType = type, RecipientId = id, IssueDate = SchoolDate(), Lines = new() { new CreateInvIssueLine { ItemId = item, Quantity = 1 } } };
            Assert.Equal(201, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("STAFF", F.EmpA), default)).StatusCode);
            Assert.Equal(201, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("STUDENT", F.StudentA), default)).StatusCode);
            Assert.Equal(404, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("STAFF", F.EmpB), default)).StatusCode);          // another school's employee
            Assert.Equal(404, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("DEPARTMENT", F.DeptB.ToString()), default)).StatusCode);
            Assert.Equal(404, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("STUDENT", "NOPE"), default)).StatusCode);
            Assert.Equal(400, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("ALIEN", "x"), default)).StatusCode);
            var dup = Req("STAFF", F.EmpA); dup.Lines!.Add(new CreateInvIssueLine { ItemId = item, Quantity = 1 });
            Assert.Equal(400, (await r.Issue.CreateIssueAsync(F.A, F.UserA, dup, default)).StatusCode);                              // the same item twice
            var future = Req("STAFF", F.EmpA); future.IssueDate = SchoolDate().AddDays(3);
            Assert.Equal(400, (await r.Issue.CreateIssueAsync(F.A, F.UserA, future, default)).StatusCode);
            // an inactive staff member is refused (when the school has one)
            var inactiveEmp = F.Scalar<int>($"select count(*) from tb_employees where school_id='{F.A}' and status_id <> (select sid from lut_status where sname='Active' and stype='general status')");
            if (inactiveEmp > 0)
            {
                var id = F.Scalar<string>($"select employee_id from tb_employees where school_id='{F.A}' and status_id <> (select sid from lut_status where sname='Active' and stype='general status') limit 1");
                Assert.Equal(404, (await r.Issue.CreateIssueAsync(F.A, F.UserA, Req("STAFF", id), default)).StatusCode);
            }
        }

        // ---------------------------------------------------------------- tenant isolation
        [InvDbFact]
        public async Task Another_schools_ids_are_all_404()
        {
            var itemA = await ItemAsync(F.A, F.UserA, 10);
            var catA = await CategoryAsync(F.A, F.UserA);
            long supA; long stockInA;
            using (var r = F.Rig())
            {
                var s = await r.Catalog.CreateSupplierAsync(F.A, new SaveInvSupplierRequest { SupplierName = "Sup " + F.Tag + Guid.NewGuid().ToString("N")[..4] }, default);
                Assert.Equal(201, s.StatusCode); supA = s.Data!.SupplierId;
                var si = await r.Stock.CreateStockInAsync(F.A, F.UserA, new CreateInvStockInRequest { ItemId = itemA, Quantity = 1, UnitCost = 1, ReceivedDate = SchoolDate() }, default);
                stockInA = si.Data!.StockInId;
            }
            var issueA = (await IssueAsync(F.A, F.UserA, itemA, 1)).Data!;
            var catB = await CategoryAsync(F.B, F.UserB);
            var itemB = await ItemAsync(F.B, F.UserB, 3);

            using var b = F.Rig();
            var save = new SaveInvItemRequest { ItemName = "x", CategoryId = catB, UnitId = F.UnitId, ItemType = "CONSUMABLE" };
            Assert.Equal(404, (await b.Catalog.GetItemAsync(F.B, itemA, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.UpdateItemAsync(F.B, itemA, save, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.ToggleItemAsync(F.B, itemA, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.RemovePhotoAsync(F.B, itemA, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.OpenPhotoAsync(F.B, itemA, default)).Failure!.StatusCode);
            Assert.Equal(404, (await b.Catalog.UpdateCategoryAsync(F.B, catA, new SaveInvCategoryRequest { CategoryName = "n", CategoryCode = "n" }, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.ToggleCategoryAsync(F.B, catA, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.UpdateSupplierAsync(F.B, supA, new SaveInvSupplierRequest { SupplierName = "n" }, default)).StatusCode);
            Assert.Equal(404, (await b.Catalog.ToggleSupplierAsync(F.B, supA, default)).StatusCode);
            save.CategoryId = catA;
            Assert.Equal(404, (await b.Catalog.CreateItemAsync(F.B, F.UserB, save, default)).StatusCode);                 // another school's category
            Assert.Equal(404, (await b.Stock.CreateStockInAsync(F.B, F.UserB, new CreateInvStockInRequest { ItemId = itemA, Quantity = 1, UnitCost = 1, ReceivedDate = SchoolDate() }, default)).StatusCode);
            Assert.Equal(404, (await b.Stock.CreateStockInAsync(F.B, F.UserB, new CreateInvStockInRequest { ItemId = itemB, SupplierId = supA, Quantity = 1, UnitCost = 1, ReceivedDate = SchoolDate() }, default)).StatusCode);
            Assert.Equal(404, (await b.Stock.VoidStockInAsync(F.B, F.UserB, stockInA, new InvVoidRequest { Reason = "x" }, default)).StatusCode);
            Assert.Equal(404, (await b.Stock.CreateAdjustmentAsync(F.B, F.UserB, new CreateInvAdjustmentRequest { ItemId = itemA, ReasonCode = "LOSS", QuantityChange = -1, Remarks = "x" }, default)).StatusCode);
            Assert.Equal(404, (await b.Stock.GetMovementsAsync(F.B, true, itemA, 1, 10, default)).StatusCode);
            Assert.Equal(404, (await b.Issue.GetIssueAsync(F.B, issueA.IssueId, default)).StatusCode);
            Assert.Equal(404, (await b.Issue.ReturnAsync(F.B, F.UserB, issueA.IssueId, new InvReturnRequest { ReturnDate = SchoolDate(), Lines = new() { new InvReturnLineRequest { IssueLineId = issueA.Lines[0].IssueLineId, Quantity = 1, Condition = "GOOD" } } }, default)).StatusCode);
            Assert.Equal(404, (await b.Issue.VoidIssueAsync(F.B, F.UserB, issueA.IssueId, new InvVoidRequest { Reason = "x" }, default)).StatusCode);
            Assert.Equal(404, (await IssueAsync(F.B, F.UserB, itemA, 1)).StatusCode);                                      // another school's item
            // and school A's data is untouched by all of that
            Assert.Equal(10, Qty(itemA));   // 10 opening + 1 stock-in - 1 issued
            AssertReconciled(itemA);
            // lists never show another school's rows
            var listB = await b.Catalog.GetItemsAsync(F.B, true, null, null, false, false, "all", 1, 100, default);
            Assert.DoesNotContain(listB.Data!.Items, i => i.ItemId == itemA);
            var catsB = await b.Catalog.GetCategoriesAsync(F.B, true, true, default);
            Assert.DoesNotContain(catsB.Data!.Items, c => c.CategoryId == catA);
        }

        // ---------------------------------------------------------------- raw SQL: the database's own guards
        [InvDbFact]
        public async Task Raw_sql_check_constraints_reject_bad_rows()
        {
            var cat = await CategoryAsync(F.A, F.UserA);
            var catB = await CategoryAsync(F.B, F.UserB);
            var item = await ItemAsync(F.A, F.UserA, 5);
            var active = "(select sid from lut_status where sname='Active' and stype='general status')";
            string NewItem(string extraCols, string extraVals, string school = "", long? category = null) =>
                $"insert into tb_inv_items (school_id,item_code,item_name,category_id,unit_id,item_type,status_id,created_by{extraCols}) values ('{(school == "" ? F.A : school)}','RAW{Guid.NewGuid().ToString("N")[..8]}','raw',{category ?? cat},{F.UnitId},'CONSUMABLE',{active},{F.UserA}{extraVals})";

            Assert.Equal(Check, F.SqlState(NewItem(",current_quantity", ",-1")));                                // quantity can never be negative
            Assert.Equal(Check, F.SqlState(NewItem(",is_saleable", ",true")));                                   // saleable needs a selling price
            Assert.Equal(Check, F.SqlState(NewItem(",gst_percent", ",101")));
            Assert.Equal(Check, F.SqlState(NewItem(",purchase_price", ",-0.01")));
            Assert.Equal(Check, F.SqlState(NewItem(",min_stock_level", ",-1")));
            Assert.Equal(Check, F.SqlState(NewItem("", "").Replace("'CONSUMABLE'", "'GADGET'")));               // item_type
            Assert.Equal(Fk, F.SqlState(NewItem("", "", F.A, catB)));                                            // composite FK: a category of another school
            Assert.Equal(Fk, F.SqlState(NewItem("", "", F.B, cat)));
            // partial unique barcode
            Assert.Null(F.SqlState($"update tb_inv_items set barcode='BC{F.Tag}' where item_id={item}"));
            Assert.Equal(Unique, F.SqlState(NewItem(",barcode", $",'BC{F.Tag}'")));
            // category codes are unique per school
            Assert.Equal(Unique, F.SqlState($"insert into tb_inv_categories (school_id,category_name,category_code,status_id) select school_id,'dup',category_code,status_id from tb_inv_categories where category_id={cat}"));

            // movements: sign / reason / remarks rules, composite FK, one OPENING per item
            string Mov(string type, string change, string after, string reason = "null", string remarks = "null", string school = "", long? itm = null) =>
                $"insert into tb_inv_stock_movements (school_id,item_id,movement_type,quantity_change,quantity_after,reason_code,remarks,created_by) values ('{(school == "" ? F.A : school)}',{itm ?? item},'{type}',{change},{after},{reason},{remarks},{F.UserA})";
            Assert.Equal(Check, F.SqlState(Mov("STOCK_IN", "0", "5")));                                          // zero change
            Assert.Equal(Check, F.SqlState(Mov("STOCK_IN", "-1", "4")));                                         // wrong sign for the type
            Assert.Equal(Check, F.SqlState(Mov("ISSUE", "1", "6")));
            Assert.Equal(Check, F.SqlState(Mov("STOCK_IN", "1", "-1")));                                         // quantity_after >= 0
            Assert.Equal(Check, F.SqlState(Mov("ADJUSTMENT", "-1", "4", "'DAMAGE'")));                           // adjustment without remarks
            Assert.Equal(Check, F.SqlState(Mov("ADJUSTMENT", "1", "6", "'DAMAGE'", "'x'")));                    // damage must be negative
            Assert.Equal(Check, F.SqlState(Mov("ADJUSTMENT", "1", "6", "'WHATEVER'", "'x'")));
            Assert.Equal(Check, F.SqlState(Mov("STOCK_IN", "1", "6", "'LOSS'")));                                // only adjustments carry a reason
            Assert.Equal(Check, F.SqlState(Mov("OTHER", "1", "6")));
            Assert.Equal(Fk, F.SqlState(Mov("STOCK_IN", "1", "6", school: F.B)));                                // a movement can't point at another school's item
            Assert.Equal(Unique, F.SqlState(Mov("OPENING", "1", "6", "'OPENING_BALANCE'", "'x'")));              // the item already has its opening row

            // stock-in checks
            string SI(string qty, string cost, string extra = "") =>
                $"insert into tb_inv_stock_ins (school_id,item_id,quantity,unit_cost,received_date,received_by{extra.Split('|')[0]}) values ('{F.A}',{item},{qty},{cost},current_date,{F.UserA}{(extra.Contains('|') ? extra.Split('|')[1] : "")})";
            Assert.Equal(Check, F.SqlState(SI("0", "1")));
            Assert.Equal(Check, F.SqlState(SI("1", "-1")));
            Assert.Equal(Check, F.SqlState(SI("1", "1", ",expiry_date|,current_date")));                          // expiry must be after received
            Assert.Equal(Check, F.SqlState(SI("1", "1", ",status|,'Voided'")));                                   // voided without who / when / why
            Assert.Equal(Check, F.SqlState(SI("1", "1", ",status|,'Gone'")));
            Assert.Equal(Fk, F.SqlState(SI("1", "1", ",supplier_id|,999999999")));
        }

        [InvDbFact]
        public async Task Raw_sql_issue_recipient_checks_and_the_recipient_trigger_on_insert_and_update()
        {
            var item = await ItemAsync(F.A, F.UserA, 5);
            string Ins(string cols, string vals, string school = "") =>
                $"insert into tb_inv_issues (school_id,issue_number,recipient_type,issue_date,issued_by{cols}) values ('{(school == "" ? F.A : school)}','RAW-{Guid.NewGuid().ToString("N")[..10]}',{vals})";
            Assert.Equal(Check, F.SqlState(Ins("", $"'STAFF',current_date,{F.UserA}")));   // STAFF with no employee_id
            Assert.Equal(Check, F.SqlState(Ins(",employee_id,student_id", $"'STAFF',current_date,{F.UserA},'{F.EmpA}','{F.StudentA}'")));  // two recipient columns
            Assert.Equal(Check, F.SqlState(Ins(",employee_id", $"'DEPARTMENT',current_date,{F.UserA},'{F.EmpA}'")));                       // column does not match the type
            Assert.Equal(Check, F.SqlState(Ins(",employee_id", $"'ROBOT',current_date,{F.UserA},'{F.EmpA}'")));
            Assert.Equal(Check, F.SqlState(Ins(",status,employee_id", $"'STAFF',current_date,{F.UserA},'Voided','{F.EmpA}'")));   // voided without who / when / why
            // INSERT: another school's employee / department / student
            Assert.Equal(Fk, F.SqlState(Ins(",employee_id", $"'STAFF',current_date,{F.UserA},'{F.EmpB}'")));
            Assert.Equal(Fk, F.SqlState(Ins(",department_id", $"'DEPARTMENT',current_date,{F.UserA},{F.DeptB}")));
            Assert.Equal(Fk, F.SqlState(Ins(",student_id", $"'STUDENT',current_date,{F.UserA},'{F.StudentA}'", F.B)));                  // composite FK on students
            // a valid issue, then UPDATEs that try to move it to another school's recipient
            var issue = (await IssueAsync(F.A, F.UserA, item, 1)).Data!;
            Assert.Equal(Fk, F.SqlState($"update tb_inv_issues set recipient_type='STAFF', employee_id='{F.EmpB}', department_id=null where issue_id={issue.IssueId}"));
            Assert.Equal(Fk, F.SqlState($"update tb_inv_issues set department_id={F.DeptB} where issue_id={issue.IssueId}"));
            // the row is unchanged, and an update to its own school's recipient still works
            Assert.Equal(F.DeptA.ToString(), F.Scalar<string>($"select department_id::text from tb_inv_issues where issue_id={issue.IssueId}"));
            Assert.Null(F.SqlState($"update tb_inv_issues set recipient_type='STAFF', employee_id='{F.EmpA}', department_id=null where issue_id={issue.IssueId}"));
            // issue lines / returns
            string Line(string issueId, string qty, string returned) => $"insert into tb_inv_issue_lines (issue_id,school_id,item_id,quantity,returned_quantity) values ({issueId},'{F.A}',{item},{qty},{returned})";
            var other = (await IssueAsync(F.A, F.UserA, await ItemAsync(F.A, F.UserA, 5), 1)).Data!;
            Assert.Equal(Check, F.SqlState(Line(other.IssueId.ToString(), "0", "0")));
            Assert.Equal(Check, F.SqlState($"update tb_inv_issue_lines set returned_quantity = quantity + 1 where issue_line_id={other.Lines[0].IssueLineId}"));
            Assert.Equal(Check, F.SqlState($"insert into tb_inv_issue_returns (issue_line_id,school_id,quantity,return_date,condition,received_by) values ({other.Lines[0].IssueLineId},'{F.A}',1,current_date,'MESSY',{F.UserA})"));
            Assert.Equal(Fk, F.SqlState($"insert into tb_inv_issue_returns (issue_line_id,school_id,quantity,return_date,condition,received_by) values ({other.Lines[0].IssueLineId},'{F.B}',1,current_date,'GOOD',{F.UserA})"));
        }

        [InvDbFact]
        public async Task The_ledger_cannot_be_updated_deleted_or_truncated()
        {
            var item = await ItemAsync(F.A, F.UserA, 5);
            var id = F.Scalar<long>($"select min(movement_id) from tb_inv_stock_movements where item_id={item}");
            Assert.Equal(Restrict, F.SqlState($"update tb_inv_stock_movements set remarks='edited' where movement_id={id}"));
            Assert.Equal(Restrict, F.SqlState($"update tb_inv_stock_movements set quantity_change=quantity_change where movement_id={id}"));
            Assert.Equal(Restrict, F.SqlState($"delete from tb_inv_stock_movements where movement_id={id}"));
            Assert.Equal(Restrict, F.SqlState("truncate table tb_inv_stock_movements"));
            Assert.Equal(1, F.Scalar<int>($"select count(*) from tb_inv_stock_movements where movement_id={id}"));
            AssertReconciled(item);
            await Task.CompletedTask;
        }

        // ---------------------------------------------------------------- photo
        [InvDbFact]
        public async Task Item_photo_is_private_validated_and_school_scoped()
        {
            var item = await ItemAsync(F.A, F.UserA, 1);
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Enumerable.Repeat((byte)7, 200)).ToArray();
            using var r = F.Rig();
            Assert.Equal(400, (await r.Catalog.UploadPhotoAsync(F.A, item, new MemoryStream(new byte[300]), "x.png", 300, default)).StatusCode);                  // signature does not match
            Assert.Equal(400, (await r.Catalog.UploadPhotoAsync(F.A, item, new MemoryStream(png), "x.gif", png.Length, default)).StatusCode);                     // extension
            Assert.Equal(400, (await r.Catalog.UploadPhotoAsync(F.A, item, new MemoryStream(png), "x.png", 3 * 1024 * 1024, default)).StatusCode);               // over 2 MB
            Assert.Equal(400, (await r.Catalog.UploadPhotoAsync(F.A, item, new MemoryStream(), "x.png", 0, default)).StatusCode);
            var ok = await r.Catalog.UploadPhotoAsync(F.A, item, new MemoryStream(png), "x.png", png.Length, default);
            Assert.Equal(200, ok.StatusCode);
            Assert.True(ok.Data!.HasPhoto);

            var key = F.Scalar<string>($"select photo_path from tb_inv_items where item_id={item}");
            Assert.DoesNotContain("/uploads", key); Assert.DoesNotContain("http", key);
            Assert.True(File.Exists(Path.Combine(F.PhotoRoot, key.Replace('/', Path.DirectorySeparatorChar))));          // in the private root, nowhere public

            var (failure, content, type, name) = await r.Catalog.OpenPhotoAsync(F.A, item, default);
            Assert.Null(failure); Assert.Equal("image/png", type); Assert.EndsWith(".png", name);
            using (content) { var ms = new MemoryStream(); await content!.CopyToAsync(ms); Assert.Equal(png, ms.ToArray()); }

            using var b = F.Rig();
            Assert.Equal(404, (await b.Catalog.OpenPhotoAsync(F.B, item, default)).Failure!.StatusCode);                 // cross-school
            Assert.Equal(404, (await b.Catalog.UploadPhotoAsync(F.B, item, new MemoryStream(png), "x.png", png.Length, default)).StatusCode);

            // replacing deletes the old file; removing deletes the file and clears the key
            var again = await r.Catalog.UploadPhotoAsync(F.A, item, new MemoryStream(png), "y.png", png.Length, default);
            Assert.Equal(200, again.StatusCode);
            Assert.False(File.Exists(Path.Combine(F.PhotoRoot, key.Replace('/', Path.DirectorySeparatorChar))));
            var key2 = F.Scalar<string>($"select photo_path from tb_inv_items where item_id={item}");
            Assert.Equal(200, (await r.Catalog.RemovePhotoAsync(F.A, item, default)).StatusCode);
            Assert.False(File.Exists(Path.Combine(F.PhotoRoot, key2.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(404, (await r.Catalog.RemovePhotoAsync(F.A, item, default)).StatusCode);
        }

        // ---------------------------------------------------------------- items, categories, suppliers, dashboard
        [InvDbFact]
        public async Task Item_master_rules_and_codes()
        {
            var cat = await CategoryAsync(F.A, F.UserA);
            using var r = F.Rig();
            SaveInvItemRequest Req(Action<SaveInvItemRequest>? t = null) { var q = new SaveInvItemRequest { ItemName = "I" + Guid.NewGuid().ToString("N")[..6], CategoryId = cat, UnitId = F.UnitId, ItemType = "ASSET" }; t?.Invoke(q); return q; }
            Assert.Equal(400, (await r.Catalog.CreateItemAsync(F.A, F.UserA, Req(q => q.IsSaleable = true), default)).StatusCode);                 // saleable without a price
            Assert.Equal(400, (await r.Catalog.CreateItemAsync(F.A, F.UserA, Req(q => q.ItemType = "STUFF"), default)).StatusCode);
            Assert.Equal(400, (await r.Catalog.CreateItemAsync(F.A, F.UserA, Req(q => q.GstPercent = 150), default)).StatusCode);
            Assert.Equal(404, (await r.Catalog.CreateItemAsync(F.A, F.UserA, Req(q => q.UnitId = 99999), default)).StatusCode);
            var bc = "BAR" + F.Tag + Guid.NewGuid().ToString("N")[..4];
            var one = await r.Catalog.CreateItemAsync(F.A, F.UserA, Req(q => q.Barcode = bc), default);
            Assert.Equal(201, one.StatusCode);
            Assert.Matches(@"^ITM\d{4,}$", one.Data!.ItemCode);
            Assert.Equal(409, (await r.Catalog.CreateItemAsync(F.A, F.UserA, Req(q => q.Barcode = bc), default)).StatusCode);                     // duplicate barcode in the school
            Assert.Equal(201, (await r.Catalog.CreateItemAsync(F.B, F.UserB, Req(q => { q.Barcode = bc; q.CategoryId = CategoryAsync(F.B, F.UserB).Result; }), default)).StatusCode);   // same barcode in another school is fine
            // codes are unique and sequential even when created together
            var many = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () => { using var x = F.Rig(); return await x.Catalog.CreateItemAsync(F.A, F.UserA, Req(), default); })));
            Assert.All(many, m => Assert.Equal(201, m.StatusCode));
            Assert.Equal(6, many.Select(m => m.Data!.ItemCode).Distinct().Count());
            // deactivated: hidden from the default list, still readable, cannot receive stock or be issued
            Assert.False((await r.Catalog.ToggleItemAsync(F.A, one.Data.ItemId, default)).Data!.IsActive);
            Assert.DoesNotContain((await r.Catalog.GetItemsAsync(F.A, true, cat, null, false, false, null, 1, 100, default)).Data!.Items, i => i.ItemId == one.Data.ItemId);
            Assert.Contains((await r.Catalog.GetItemsAsync(F.A, true, cat, null, false, false, "inactive", 1, 100, default)).Data!.Items, i => i.ItemId == one.Data.ItemId);
            Assert.Equal(400, (await r.Stock.CreateStockInAsync(F.A, F.UserA, new CreateInvStockInRequest { ItemId = one.Data.ItemId, Quantity = 1, UnitCost = 1, ReceivedDate = SchoolDate() }, default)).StatusCode);
            Assert.Equal(400, (await IssueAsync(F.A, F.UserA, one.Data.ItemId, 1)).StatusCode);
            // search by name / code / barcode, with wildcards treated literally
            Assert.Single((await r.Catalog.GetItemsAsync(F.A, true, null, bc, false, false, "all", 1, 20, default)).Data!.Items);
            Assert.Empty((await r.Catalog.GetItemsAsync(F.A, true, null, "%", false, false, "all", 1, 20, default)).Data!.Items);
        }

        [InvDbFact]
        public async Task Category_and_supplier_rules()
        {
            using var r = F.Rig();
            var code = "K" + F.Tag + Guid.NewGuid().ToString("N")[..4];
            Assert.Equal(201, (await r.Catalog.CreateCategoryAsync(F.A, new SaveInvCategoryRequest { CategoryName = "n", CategoryCode = code.ToLower() }, default)).StatusCode);
            Assert.Equal(409, (await r.Catalog.CreateCategoryAsync(F.A, new SaveInvCategoryRequest { CategoryName = "n2", CategoryCode = code }, default)).StatusCode);   // codes are case-insensitive via normalisation
            Assert.Equal(201, (await r.Catalog.CreateCategoryAsync(F.B, new SaveInvCategoryRequest { CategoryName = "n", CategoryCode = code }, default)).StatusCode);       // another school may reuse it
            var name = "Acme " + F.Tag + Guid.NewGuid().ToString("N")[..4];
            Assert.Equal(400, (await r.Catalog.CreateSupplierAsync(F.A, new SaveInvSupplierRequest { SupplierName = name, Gstin = "BAD" }, default)).StatusCode);
            Assert.Equal(400, (await r.Catalog.CreateSupplierAsync(F.A, new SaveInvSupplierRequest { SupplierName = name, Pan = "BAD" }, default)).StatusCode);
            Assert.Equal(400, (await r.Catalog.CreateSupplierAsync(F.A, new SaveInvSupplierRequest { SupplierName = name, Email = "not-an-email" }, default)).StatusCode);
            var ok = await r.Catalog.CreateSupplierAsync(F.A, new SaveInvSupplierRequest { SupplierName = name, Gstin = "22aaaaa0000a1z5", Pan = "abcde1234f", Email = "  " }, default);
            Assert.Equal(201, ok.StatusCode);
            Assert.Equal("22AAAAA0000A1Z5", ok.Data!.Gstin); Assert.Null(ok.Data.Email);   // a blank optional string becomes null
            Assert.Equal(409, (await r.Catalog.CreateSupplierAsync(F.A, new SaveInvSupplierRequest { SupplierName = name.ToUpperInvariant() }, default)).StatusCode);   // names are case-insensitive unique
            Assert.Equal(201, (await r.Catalog.CreateSupplierAsync(F.B, new SaveInvSupplierRequest { SupplierName = name }, default)).StatusCode);
            Assert.False((await r.Catalog.ToggleSupplierAsync(F.A, ok.Data.SupplierId, default)).Data!.IsActive);
            Assert.True((await r.Catalog.ToggleSupplierAsync(F.A, ok.Data.SupplierId, default)).Data!.IsActive);
        }

        [InvDbFact]
        public async Task Dashboard_summary_is_consistent_and_school_scoped()
        {
            var low = await ItemAsync(F.B, F.UserB, 2, min: 5);
            var expiring = await ItemAsync(F.B, F.UserB, 3, SchoolDate().AddDays(10));
            var expired = await ItemAsync(F.B, F.UserB, 3, SchoolDate().AddDays(-1));
            var issued = await ItemAsync(F.B, F.UserB, 4);
            Assert.Equal(201, (await IssueAsync(F.B, F.UserB, issued, 1)).StatusCode);
            using var r = F.Rig();
            var d = (await r.Dashboard.GetSummaryAsync(F.B, true, default)).Data!;
            Assert.Empty(d.UnavailableParts);
            Assert.Contains(d.LowStock!, i => i.ItemId == low);
            Assert.Contains(d.ExpiringSoon!, i => i.ItemId == expiring);
            Assert.Contains(d.Expired!, i => i.ItemId == expired);
            Assert.DoesNotContain(d.ExpiringSoon!, i => i.ItemId == expired);
            Assert.True(d.IssuedNotReturnedCount >= 1);
            Assert.True(d.StockInsLast30Days >= 0 && d.IssuesLast30Days >= 1);
            Assert.NotEmpty(d.RecentMovements!);
            Assert.Equal(30, d.ExpiringSoonDays);
            // the stock value equals the sum over this school's active items
            var expected = F.Scalar<decimal>($"select coalesce(sum(current_quantity*purchase_price),0) from tb_inv_items where school_id='{F.B}' and status_id=(select sid from lut_status where sname='Active' and stype='general status')");
            Assert.Equal(decimal.Round(expected, 2), d.TotalStockValue);
            Assert.All(d.RecentMovements!, m => Assert.NotEqual(0, m.QuantityChange));
            // read-only caller: the same numbers, canManage false
            Assert.False((await r.Dashboard.GetSummaryAsync(F.B, false, default)).Data!.CanManage);
        }

        [InvDbFact]
        public void Global_reconciliation_has_no_mismatches_and_nothing_else_changed()
        {
            Assert.Equal(0, F.Scalar<int>(Reconciliation));
            // the only lut_roles addition is STORE_KEEPER (once)
            Assert.Equal(1, F.Scalar<int>("select count(*) from lut_roles where role_code='STORE_KEEPER'"));
        }
    }
}
