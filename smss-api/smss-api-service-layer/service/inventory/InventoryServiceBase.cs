using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;

namespace smss_api_service_layer.service
{
    /// <summary>Shared plumbing of the Inventory services: the response envelope, error logging, status lookup and the mapping of rows to DTOs.</summary>
    public abstract class InventoryServiceBase
    {
        protected readonly IInventoryRepository Repo;
        protected readonly ILogger Logger;
        protected InventoryServiceBase(IInventoryRepository repo, ILogger logger) { Repo = repo; Logger = logger; }

        protected static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        protected ApiResponse<T> Error<T>(Exception ex, string action)
        {
            Logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }

        protected ApiResponse<T> ConfigError<T>()
        {
            Logger.LogError("Configuration problem: Active / Inactive is missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        protected static ApiResponse<T> NoSchool<T>() => Result<T>(400, TenantMessages.NoSchool, default);

        /// <summary>True when the exception is a unique-index violation of the named constraint / index.</summary>
        protected static bool IsUnique(Exception ex, params string[] constraints) =>
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg } && (constraints.Length == 0 || constraints.Contains(pg.ConstraintName));

        protected static string Today() => SchoolClock.Today().ToString("yyyy-MM-dd");

        // ---------------------------------------------------------------- mapping
        protected static InvItemDto ToDto(InvItemRow r)
        {
            var i = r.Item;
            var today = SchoolClock.Today();
            return new InvItemDto
            {
                ItemId = i.ItemId, ItemCode = i.ItemCode, Barcode = i.Barcode, ItemName = i.ItemName, Description = i.Description,
                CategoryId = i.CategoryId, CategoryName = r.CategoryName, UnitId = i.UnitId, UnitName = r.UnitName, UnitCode = r.UnitCode, ItemType = i.ItemType,
                PurchasePrice = i.PurchasePrice, SellingPrice = i.SellingPrice, IsSaleable = i.IsSaleable, GstPercent = i.GstPercent,
                MinStockLevel = i.MinStockLevel, CurrentQuantity = i.CurrentQuantity, StockValue = decimal.Round(i.CurrentQuantity * i.PurchasePrice, 2),
                ExpiryDate = i.ExpiryDate, HasPhoto = i.PhotoPath != null, IsActive = false,   // set by the caller (needs the Active status id)
                IsLowStock = i.MinStockLevel > 0 && i.CurrentQuantity <= i.MinStockLevel,
                IsExpired = i.ExpiryDate != null && i.ExpiryDate < today && i.CurrentQuantity > 0,
                IsExpiringSoon = i.ExpiryDate != null && i.ExpiryDate >= today && i.ExpiryDate <= today.AddDays(InventoryRules.ExpiringSoonDays) && i.CurrentQuantity > 0
            };
        }

        protected static InvMovementDto ToDto(InvMovementRow r) => new()
        {
            MovementId = r.Movement.MovementId, ItemId = r.Movement.ItemId, ItemCode = r.ItemCode, ItemName = r.ItemName, UnitCode = r.UnitCode,
            MovementType = r.Movement.MovementType, QuantityChange = r.Movement.QuantityChange, QuantityAfter = r.Movement.QuantityAfter, ReasonCode = r.Movement.ReasonCode,
            ReferenceType = r.Movement.ReferenceType, ReferenceId = r.Movement.ReferenceId, UnitCost = r.Movement.UnitCost, Remarks = r.Movement.Remarks,
            CreatedByName = r.CreatedByName, CreatedAt = r.Movement.CreatedAt
        };

        protected static InvDashboardItemDto ToDashboardItem(InvItemRow r) => new()
        {
            ItemId = r.Item.ItemId, ItemCode = r.Item.ItemCode, ItemName = r.Item.ItemName, UnitCode = r.UnitCode,
            CurrentQuantity = r.Item.CurrentQuantity, MinStockLevel = r.Item.MinStockLevel, ExpiryDate = r.Item.ExpiryDate
        };
    }
}
