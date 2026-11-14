namespace smss_api_db_layer.constants
{
    /// <summary>The two admission types the application itself relies on (lut_admission_type.type_code). Always looked up by CODE, never by id or display name.</summary>
    public static class AdmissionTypeCodes
    {
        public const string NewAdmission = "NEW_ADMISSION";
        public const string ExistingStudent = "EXISTING_STUDENT";

        public static string LabelOf(string code) => code == ExistingStudent ? "Existing Student" : "New Admission";
    }
}
