using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Read-only aggregates. Each part is loaded on its own; a failed part is left null and named, so the screen shows "Unavailable", never a false 0.</summary>
    public class InventoryDashboardService : InventoryServiceBase, IInventoryDashboardService
    {
        public InventoryDashboardService(IInventoryRepository repo, ILogger<InventoryDashboardService> logger) : base(repo, logger) { }

        public async Task<ApiResponse<InvDashboardDto>> GetSummaryAsync(string? schoolId, bool canManage, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return NoSchool<InvDashboardDto>();
            try
            {
                var active = await Repo.GetStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<InvDashboardDto>();
                var today = SchoolClock.Today();
                var dto = new InvDashboardDto { CanManage = canManage, ExpiringSoonDays = InventoryRules.ExpiringSoonDays };
                var top = InventoryRules.DashboardListSize;

                async Task Part(string name, Func<Task> load)
                {
                    try { await load(); }
                    catch (Exception ex) { Logger.LogError(ex, "Inventory dashboard part {Part} failed", name); dto.UnavailableParts.Add(name); }
                }

                await Part("totalActiveItems", async () => dto.TotalActiveItems = await Repo.CountActiveItemsAsync(schoolId, active.Value, ct));
                await Part("totalStockValue", async () => dto.TotalStockValue = decimal.Round(await Repo.SumStockValueAsync(schoolId, active.Value, ct), 2));
                await Part("lowStock", async () => { var (n, rows) = await Repo.GetLowStockAsync(schoolId, active.Value, top, ct); dto.LowStockCount = n; dto.LowStock = rows.Select(ToDashboardItem).ToList(); });
                await Part("expiringSoon", async () =>
                {
                    var (n, rows) = await Repo.GetExpiryListAsync(schoolId, active.Value, today, today.AddDays(InventoryRules.ExpiringSoonDays), top, ct);
                    dto.ExpiringSoonCount = n; dto.ExpiringSoon = rows.Select(ToDashboardItem).ToList();
                });
                await Part("expired", async () => { var (n, rows) = await Repo.GetExpiredListAsync(schoolId, active.Value, today, top, ct); dto.ExpiredCount = n; dto.Expired = rows.Select(ToDashboardItem).ToList(); });
                await Part("issuedNotReturned", async () => dto.IssuedNotReturnedCount = await Repo.CountIssuedNotReturnedAsync(schoolId, ct));
                await Part("recentMovements", async () =>
                {
                    var (rows, _) = await Repo.GetMovementsAsync(schoolId, null, false, null, null, null, 1, top, ct);
                    dto.RecentMovements = rows.Select(ToDto).ToList();
                });
                await Part("operations", async () =>
                {
                    var since = today.AddDays(-30);
                    dto.StockInsLast30Days = await Repo.CountStockInsSinceAsync(schoolId, since, ct);
                    dto.IssuesLast30Days = await Repo.CountIssuesSinceAsync(schoolId, since, ct);
                });
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<InvDashboardDto>(ex, "loading the inventory dashboard"); }
        }
    }
}
