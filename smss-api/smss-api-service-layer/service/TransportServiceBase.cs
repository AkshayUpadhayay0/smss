using Microsoft.Extensions.Logging;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;

namespace smss_api_service_layer.service
{
    /// <summary>The small helpers the three Transport services share (response shape, paging checks, the status filter, messages that name drivers).</summary>
    public abstract class TransportServiceBase
    {
        protected const int MaxPageSize = 100;
        private readonly ILogger _logger;
        protected TransportServiceBase(ILogger logger) => _logger = logger;

        protected static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        protected ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }

        protected ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        protected static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        protected static string? ConstraintOf(DbUpdateException ex) => (ex.InnerException as PostgresException)?.ConstraintName;

        /// <summary>null when the paging values are fine, otherwise the 400 message.</summary>
        protected static string? PagingError(int page, int pageSize) =>
            page < 1 ? "page must be 1 or more." : pageSize < 1 || pageSize > MaxPageSize ? $"pageSize must be between 1 and {MaxPageSize}." : null;

        /// <summary>The status filter (All / Active / Inactive, any case) as a status id; null id = no filter. Error is set for an unknown value or a missing lut_status row.</summary>
        protected static async Task<(int? StatusId, string? Error, bool Config)> ParseStatusAsync(string? status, Func<string, string, CancellationToken, Task<int?>> lookup, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(status) || status.Trim().Equals("All", StringComparison.OrdinalIgnoreCase)) return (null, null, false);
            var name = new[] { StatusNames.Active, StatusNames.Inactive }.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return (null, "status must be All, Active or Inactive.", false);
            var id = await lookup(name, StatusNames.GeneralType, ct);
            return id == null ? (null, $"Status '{name}' ({StatusNames.GeneralType}) is missing in lut_status", true) : (id, null, false);
        }

        /// <summary>"Ramesh (DPS001DR002), Suresh (DPS001DR003) and 2 more": the drivers named in a refusal.</summary>
        protected static string NameDrivers(IReadOnlyList<DriverRef> drivers)
        {
            var shown = drivers.Take(5).Select(d => $"{d.FullName} ({d.DriverId})").ToList();
            var text = string.Join(", ", shown);
            return drivers.Count > shown.Count ? $"{text} and {drivers.Count - shown.Count} more" : text;
        }
    }
}
