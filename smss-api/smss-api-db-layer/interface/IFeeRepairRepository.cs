namespace smss_api_db_layer.@interface
{
    /// <summary>An enrolled student of the year who has no fee structure assigned for that year.</summary>
    public class MissingFeeRow
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        /// <summary>This school's ACTIVE structure for the class + year, or null when the class has none.</summary>
        public long? FeeStructureId { get; set; }
    }

    /// <summary>What a student chose for an OPTIONAL fee head in the previous year.</summary>
    public class PreviousChoiceRow
    {
        public string StudentId { get; set; } = null!;
        public long FeeTypeId { get; set; }
        public bool IsApplicable { get; set; }
        public string? InstallmentPlan { get; set; }
    }

    /// <summary>Repair "Assign fees for a year": read-only lookups. The writes go through the same code admission uses (FeeAutoAssignment).</summary>
    public interface IFeeRepairRepository
    {
        /// <summary>Active students with an ACTIVE enrolment in the year and no row in tb_student_fee_structure for it. Only this school's rows.</summary>
        Task<List<MissingFeeRow>> ListMissingAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>The optional-fee choices these students made in the school's latest year that starts before this one (none when there is no earlier year).</summary>
        Task<List<PreviousChoiceRow>> GetPreviousChoicesAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
    }
}
