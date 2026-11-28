namespace smss_api_db_layer.@interface
{
    /// <summary>What one student still owes from ONE academic year that starts before a reference date (nothing is carried forward: the dues stay in their own year, this is only a sum read from them).</summary>
    public class EarlierYearAmountRow
    {
        public string StudentId { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        public DateOnly YearStart { get; set; }
        public decimal Outstanding { get; set; }
        /// <summary>Installments of that year, still unpaid, due before the cut-off date passed to the query.</summary>
        public int OverdueCount { get; set; }
    }

    /// <summary>One unpaid installment of an earlier year, for the payment dialog (so it can be paid, oldest first).</summary>
    public class EarlierDueRow
    {
        public long FeeDueId { get; set; }
        public long StudentFeeItemId { get; set; }
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FrequencyName { get; set; } = null!;
        public string InstallmentPlan { get; set; } = "NONE";
        public string Source { get; set; } = "AUTO";
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
    }

    /// <summary>Read-only sums over tb_student_fee_dues of years BEFORE a reference year. Every query carries the school id; a student id of another school simply finds nothing.</summary>
    public interface IEarlierYearDuesRepository
    {
        /// <summary>Per student and earlier year, the outstanding amount (only years with something unpaid). <paramref name="referenceStarts"/> is parallel to <paramref name="studentIds"/>: the start date of the year shown for that student; earlier = starts before it.</summary>
        Task<List<EarlierYearAmountRow>> GetOutstandingAsync(string schoolId, IReadOnlyList<string> studentIds, IReadOnlyList<DateOnly> referenceStarts, DateOnly overdueBefore, CancellationToken ct);
        /// <summary>The unpaid installments of one student in years that start before the date, oldest first.</summary>
        Task<List<EarlierDueRow>> GetDuesAsync(string schoolId, string studentId, DateOnly referenceStart, CancellationToken ct);
    }
}
