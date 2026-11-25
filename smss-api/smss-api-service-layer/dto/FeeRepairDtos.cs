namespace smss_api_service_layer.dto
{
    /// <summary>One class that has students without fees but no active fee structure for the year.</summary>
    public class FeeRepairNoStructureDto
    {
        public string ClassName { get; set; } = null!;
        public int Students { get; set; }
    }

    /// <summary>A student the repair could not assign, with the reason.</summary>
    public class FeeRepairStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string ClassName { get; set; } = null!;
        public string Reason { get; set; } = null!;
    }

    /// <summary>"Assign fees for a year": what a run WOULD do (nothing is saved).</summary>
    public class FeeRepairPreviewDto
    {
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        /// <summary>Active enrolled students of the year with no fee structure assigned.</summary>
        public int MissingCount { get; set; }
        /// <summary>Of those, the ones whose class has an active structure: a run would assign them (at most MaxPerRun).</summary>
        public int WouldAssign { get; set; }
        /// <summary>A run takes at most this many students; run again for the rest.</summary>
        public int MaxPerRun { get; set; }
        public List<FeeRepairNoStructureDto> ClassesWithoutStructure { get; set; } = new();
    }

    /// <summary>The result of a run. Assigning is best-effort per student: a failure never stops the others and never undoes them.</summary>
    public class FeeRepairResultDto
    {
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        public int Assigned { get; set; }
        /// <summary>Students whose class has no active fee structure: left as they were.</summary>
        public int SkippedNoStructure { get; set; }
        /// <summary>Students still without fees after this run because the run is capped (MaxPerRun).</summary>
        public int Remaining { get; set; }
        public List<FeeRepairStudentDto> Failed { get; set; } = new();
    }
}
