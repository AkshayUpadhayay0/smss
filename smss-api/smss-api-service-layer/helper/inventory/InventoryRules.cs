using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper
{
    /// <summary>Movement types written to tb_inv_stock_movements (CHECK-constrained in the database).</summary>
    public static class InvMovementTypes
    {
        public const string Opening = "OPENING";
        public const string StockIn = "STOCK_IN";
        public const string Adjustment = "ADJUSTMENT";
        public const string Issue = "ISSUE";
        public const string Return = "RETURN";
        public const string StockInVoid = "STOCK_IN_VOID";
        public const string IssueVoid = "ISSUE_VOID";
    }

    public static class InvReasonCodes
    {
        public const string Damage = "DAMAGE";
        public const string Loss = "LOSS";
        public const string Expired = "EXPIRED";
        public const string Correction = "CORRECTION";
        public const string OpeningBalance = "OPENING_BALANCE";
        public static readonly string[] All = { Damage, Loss, Expired, Correction, OpeningBalance };
        /// <summary>Reasons that can only take stock out.</summary>
        public static readonly string[] MustBeNegative = { Damage, Loss, Expired };
    }

    public static class InvReferenceTypes
    {
        public const string StockIn = "STOCK_IN";
        public const string IssueLine = "ISSUE_LINE";
        public const string IssueReturn = "ISSUE_RETURN";
    }

    public static class InvRecipientTypes
    {
        public const string Staff = "STAFF";
        public const string Student = "STUDENT";
        public const string Department = "DEPARTMENT";
        public static readonly string[] All = { Staff, Student, Department };
    }

    public static class InvItemTypes
    {
        public const string Consumable = "CONSUMABLE";
        public const string Asset = "ASSET";
        public static readonly string[] All = { Consumable, Asset };
    }

    public static class InvReturnConditions
    {
        public const string Good = "GOOD";
        public const string Damaged = "DAMAGED";
        public static readonly string[] All = { Good, Damaged };
    }

    /// <summary>The derived status of an issue (never stored).</summary>
    public static class InvIssueStatuses
    {
        public const string Outstanding = "Outstanding";
        public const string PartiallyReturned = "Partially Returned";
        public const string Returned = "Returned";
        public const string Voided = "Voided";

        public static string Of(string status, decimal issued, decimal returned) =>
            status == PaymentStatuses.Voided ? Voided : returned <= 0 ? Outstanding : returned >= issued ? Returned : PartiallyReturned;
    }

    /// <summary>Pure validation / formatting rules of the Inventory module (kept apart from the services so they can be tested without a database).</summary>
    public static class InventoryRules
    {
        /// <summary>Items expiring within this many days are listed as "expiring soon" (named constant, design doc section 5).</summary>
        public const int ExpiringSoonDays = 30;
        public const int MaxRemarks = 500;
        public const int MaxPageSize = 100;
        public const int RecipientSearchCap = 20;
        public const int DashboardListSize = 10;

        private static readonly Regex Gstin = new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.Compiled);
        private static readonly Regex Pan = new(@"^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.Compiled);
        private static readonly Regex Phone = new(@"^[0-9+\-\s()]{7,20}$", RegexOptions.Compiled);

        /// <summary>Blank optional text becomes null (an empty string must never reach [EmailAddress]-style checks or the database).</summary>
        public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public static string? CleanUpper(string? s) => Clean(s)?.ToUpperInvariant();

        /// <summary>Codes are stored upper-case with no inner spaces.</summary>
        public static string? NormalizeCode(string? s) => Clean(s) is { } t ? Regex.Replace(t, @"\s+", "").ToUpperInvariant() : null;

        public static bool ValidGstin(string? v) => v == null || Gstin.IsMatch(v);
        public static bool ValidPan(string? v) => v == null || Pan.IsMatch(v);
        public static bool ValidPhone(string? v) => v == null || Phone.IsMatch(v);

        public static bool HasAtMostDecimals(decimal v, int places) => decimal.Round(v, places) == v;

        /// <summary>A quantity: positive, at most 3 decimal places and within NUMERIC(12,3).</summary>
        public static string? QuantityError(decimal v, string label = "Quantity")
        {
            if (v <= 0) return $"{label} must be greater than 0.";
            if (!HasAtMostDecimals(v, 3)) return $"{label} can have at most 3 decimal places.";
            if (v >= 999_999_999m) return $"{label} is too large.";
            return null;
        }

        public static string? MoneyError(decimal v, string label)
        {
            if (v < 0) return $"{label} can't be negative.";
            if (!HasAtMostDecimals(v, 2)) return $"{label} can have at most 2 decimal places.";
            if (v >= 9_999_999_999m) return $"{label} is too large.";
            return null;
        }

        /// <summary>The adjustment rules: sign per reason, non-zero, 3 decimals. Returns an error message or null.</summary>
        public static string? AdjustmentError(string reasonCode, decimal change)
        {
            if (!InvReasonCodes.All.Contains(reasonCode)) return $"Reason must be one of: {string.Join(", ", InvReasonCodes.All)}.";
            if (change == 0) return "The quantity change can't be 0.";
            if (!HasAtMostDecimals(change, 3)) return "The quantity change can have at most 3 decimal places.";
            if (Math.Abs(change) >= 999_999_999m) return "The quantity change is too large.";
            if (reasonCode == InvReasonCodes.OpeningBalance && change < 0) return "An opening balance must be a positive quantity.";
            if (InvReasonCodes.MustBeNegative.Contains(reasonCode) && change > 0) return $"A {reasonCode.ToLowerInvariant()} adjustment must take stock out (a negative quantity).";
            return null;
        }

        public static string IssueNumber(int year, int sequence) => $"ISS-{year}-{sequence:D4}";
        public static string ItemCode(int sequence) => $"ITM{sequence:D4}";

        public static int PageSize(int? requested) => Math.Clamp(requested ?? 20, 1, MaxPageSize);
        public static int Page(int? requested) => Math.Max(requested ?? 1, 1);

        /// <summary>Escapes LIKE wildcards so a search for "50%" does not match everything.</summary>
        public static string LikePattern(string term) => "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    }
}
