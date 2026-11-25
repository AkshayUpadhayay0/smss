namespace smss_api_service_layer.helper
{
    /// <summary>The school's rule as the arithmetic needs it (a snapshot of this is saved with every payment that charges a late fee).</summary>
    public sealed record LateFeeRuleValues(string Type, decimal? Amount, decimal? Percent, int GraceDays, decimal? CapPerDue);

    /// <summary>What one late due is charged by one payment, with everything that went into it (this is what is saved in tb_fee_payment_late_fees).</summary>
    public sealed record LateFeeCharge(int DaysLate, int DaysCharged, decimal BaseAmount, decimal ComputedTotal, decimal AlreadyCollected, decimal Amount);

    /// <summary>
    /// The late fee arithmetic, kept free of the database so the preview and the payment use exactly the same code. The rules (approved 25 Nov 2026):
    ///  - A due is late only for the calendar days BEYOND the grace days counted from its due date; "today" is the school's date in India.
    ///  - FLAT and PER_DAY are worked out per late due: the total as of the payment date (FLAT: the amount; PER_DAY: the amount times the days beyond grace), capped per due, minus what non-voided payments already collected
    ///    on that due (a due paid in two partial payments is never charged a flat late fee twice; a voided payment frees its amount again).
    ///  - PERCENT is a percentage of the amount applied to that due in THIS payment (before any concession: a concession never discounts a late fee); the cap per due still limits what the due can ever collect.
    ///  - Amounts are rounded to 2 decimals, half away from zero (as tax is).
    /// </summary>
    public static class LateFeeMath
    {
        public static int DaysBeyondGrace(DateOnly dueDate, DateOnly today, int graceDays)
        {
            var late = today.DayNumber - dueDate.DayNumber;
            return Math.Max(0, late - Math.Max(0, graceDays));
        }

        /// <summary>The charge on one due, or null when nothing is charged (not late beyond grace, nothing applied, or the cap is already reached).</summary>
        public static LateFeeCharge? Charge(LateFeeRuleValues rule, DateOnly dueDate, DateOnly today, decimal appliedToDue, decimal alreadyCollected)
        {
            if (appliedToDue <= 0) return null;
            var daysLate = today.DayNumber - dueDate.DayNumber;
            var charged = DaysBeyondGrace(dueDate, today, rule.GraceDays);
            if (charged <= 0) return null;
            var already = Math.Max(0m, alreadyCollected);
            decimal total, charge;
            switch (rule.Type)
            {
                case LateFeeRuleTypes.Flat:
                case LateFeeRuleTypes.PerDay:
                {
                    var raw = rule.Type == LateFeeRuleTypes.Flat ? (rule.Amount ?? 0m) : (rule.Amount ?? 0m) * charged;
                    total = Round(rule.CapPerDue is { } cap ? Math.Min(raw, cap) : raw);
                    charge = Math.Max(0m, total - already);
                    break;
                }
                case LateFeeRuleTypes.Percent:
                {
                    var raw = Round(appliedToDue * (rule.Percent ?? 0m) / 100m);
                    total = rule.CapPerDue is { } c2 ? Math.Min(raw, c2) : raw;
                    charge = rule.CapPerDue is { } c3 ? Math.Max(0m, Math.Min(raw, c3 - already)) : raw;
                    break;
                }
                default: return null;
            }
            charge = Round(charge);
            if (charge <= 0) return null;
            return new LateFeeCharge(daysLate, charged, appliedToDue, total, already, charge);
        }

        public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }
}
