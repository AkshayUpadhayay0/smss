namespace smss_api_service_layer.dto
{
    /// <summary>One step of the first-login setup checklist. Derived live from existing tables; nothing is stored.</summary>
    public class OnboardingStepDto
    {
        public string Code { get; set; } = null!;
        public string Title { get; set; } = null!;
        public bool Done { get; set; }
        /// <summary>The Angular route that completes this step.</summary>
        public string Route { get; set; } = null!;
        /// <summary>A short, safe sentence about what is still missing (or what is done).</summary>
        public string Detail { get; set; } = string.Empty;
    }

    public class OnboardingStatusDto
    {
        /// <summary>True when every step is done, or the school already has an active student (grandfathered).</summary>
        public bool IsComplete { get; set; }
        /// <summary>The first step not done; null when IsComplete.</summary>
        public OnboardingStepDto? NextStep { get; set; }
        public List<OnboardingStepDto> Steps { get; set; } = new();
    }

    /// <summary>The Data of a 409 SETUP_INCOMPLETE answer.</summary>
    public class SetupIncompleteDto
    {
        public string Code { get; set; } = SetupGate.Code;
        public OnboardingStepDto? NextStep { get; set; }
    }

    public static class SetupGate
    {
        public const string Code = "SETUP_INCOMPLETE";
        public const string Message = "Finish the school setup first. Add the academic year, classes, fee types and fee structures, then try again.";
    }

    public static class OnboardingSteps
    {
        public const string PasswordChanged = "PASSWORD_CHANGED";
        public const string AcademicYear = "ACADEMIC_YEAR";
        public const string ImportMastersOrClasses = "IMPORT_MASTERS_OR_CLASSES";
        public const string SchoolSetup = "SCHOOL_SETUP";
        public const string FeeTypes = "FEE_TYPES";
        public const string FeeStructure = "FEE_STRUCTURE";
    }
}
