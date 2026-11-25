using System.Text.Json;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    /// <summary>What is saved in tb_fee_payments.late_fee_rule_snapshot: the rule in force, the date the days were counted to, and each due's figures (also for a waiver: what would have been charged).</summary>
    public sealed class LateFeeSnapshot
    {
        public string RuleType { get; set; } = "";
        public decimal? Amount { get; set; }
        public decimal? Percent { get; set; }
        public int GraceDays { get; set; }
        public decimal? CapPerDue { get; set; }
        public bool TaxApplies { get; set; }
        public string AsOf { get; set; } = "";
        public decimal Total { get; set; }
        public List<LateFeeSnapshotLine> Lines { get; set; } = new();
    }

    public sealed class LateFeeSnapshotLine
    {
        public long FeeDueId { get; set; }
        public string FeeType { get; set; } = "";
        public string DueDate { get; set; } = "";
        public int DaysLate { get; set; }
        public int DaysCharged { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal ComputedTotal { get; set; }
        public decimal AlreadyCollected { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>Builds the late fee part of a receipt, the history and My fees from what was SAVED with the payment (never from today's rule).</summary>
    public static class PaymentLateFee
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static string Serialize(LateFeeSnapshot s) => JsonSerializer.Serialize(s, Json);
        public static LateFeeSnapshot? Parse(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LateFeeSnapshot>(json, Json); } catch (JsonException) { return null; } }

        private static string Money(decimal v) => v == decimal.Truncate(v) ? $"₹{v:0}" : $"₹{v:0.00}";

        /// <summary>The rule in words, as it was when the payment was taken.</summary>
        public static string RuleText(LateFeeSnapshot s)
        {
            var core = s.RuleType switch
            {
                LateFeeRuleTypes.Flat => $"{Money(s.Amount ?? 0)} for each late installment",
                LateFeeRuleTypes.PerDay => $"{Money(s.Amount ?? 0)} for each day late",
                LateFeeRuleTypes.Percent => $"{s.Percent:0.##}% of the amount paid against the installment",
                _ => "Late fee",
            };
            var grace = s.GraceDays > 0 ? $", after {s.GraceDays} grace day{(s.GraceDays == 1 ? "" : "s")}" : "";
            var cap = s.CapPerDue is { } c ? $", at most {Money(c)} per installment" : "";
            return core + grace + cap;
        }

        /// <summary>The late fee of a payment as the receipt shows it, or null when none was due. A student (staffView = false) sees only a charged late fee: never a waiver, its note or who decided.</summary>
        public static PaymentLateFeeDto? Build(TbFeePayment p, IReadOnlyList<PaymentLateFeeLineRow> rows, string? decidedByName, bool staffView)
        {
            if (p.LateFeeDecision == null) return null;
            if (p.LateFeeDecision == "WAIVED" && !staffView) return null;
            var snap = Parse(p.LateFeeRuleSnapshot);
            var dto = new PaymentLateFeeDto { Decision = p.LateFeeDecision, Amount = p.LateFeeAmount, TaxAmount = p.LateFeeTaxAmount, RuleText = snap == null ? "" : RuleText(snap) };
            if (p.LateFeeDecision == "APPLIED")
                dto.Lines = rows.Select(r => new PaymentLateFeeLineDto { FeeType = r.FeeTypeName, DueDate = r.DueDate, DaysLate = r.DaysLate, DaysCharged = r.DaysCharged, BaseAmount = r.BaseAmount, Amount = r.Amount }).ToList();
            else if (snap != null)
                dto.Lines = snap.Lines.Select(l => new PaymentLateFeeLineDto { FeeType = l.FeeType, DueDate = DateOnly.Parse(l.DueDate), DaysLate = l.DaysLate, DaysCharged = l.DaysCharged, BaseAmount = l.BaseAmount, Amount = l.Amount }).ToList();
            if (staffView)
            {
                dto.DecidedByName = decidedByName;
                if (p.LateFeeDecision == "WAIVED") { dto.Note = p.LateFeeNote; dto.WaivedAmount = snap?.Total; }
            }
            return dto;
        }
    }
}
