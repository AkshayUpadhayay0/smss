namespace smss_api_service_layer.helper
{
    public record CatalogClass(string Name, string Code, short SequenceOrder);
    public record CatalogSimple(string Name, string? Code = null, string? Description = null);
    public record CatalogDocument(string Code, string Name, string AppliesTo, bool IsRequired);

    /// <summary>
    /// The ready-made choices offered on the "Import Masters" page. A school picks from these; the server only ever
    /// creates what is listed here (a client cannot invent names through this endpoint — the individual master screens
    /// are for that). Keys are the Name, except document types, which use the Code.
    /// </summary>
    public static class MasterImportCatalog
    {
        /// <summary>Every imported class gets this one section.</summary>
        public const string DefaultSectionName = "A";

        public static readonly IReadOnlyList<CatalogClass> Classes = new CatalogClass[]
        {
            new("Nursery", "NUR", 1),
            new("LKG", "LKG", 2),
            new("UKG", "UKG", 3),
            new("Class 1", "1", 4),
            new("Class 2", "2", 5),
            new("Class 3", "3", 6),
            new("Class 4", "4", 7),
            new("Class 5", "5", 8),
            new("Class 6", "6", 9),
            new("Class 7", "7", 10),
            new("Class 8", "8", 11),
            new("Class 9", "9", 12),
            new("Class 10", "10", 13),
            new("Class 11", "11", 14),
            new("Class 12", "12", 15),
        };

        public static readonly IReadOnlyList<CatalogSimple> Subjects = new CatalogSimple[]
        {
            new("English", "ENG"),
            new("Hindi", "HIN"),
            new("Mathematics", "MATH"),
            new("Science", "SCI"),
            new("Social Science", "SST"),
            new("Environmental Studies", "EVS"),
            new("Computer Science", "CS"),
            new("General Knowledge", "GK"),
            new("Sanskrit", "SAN"),
            new("Physics", "PHY"),
            new("Chemistry", "CHEM"),
            new("Biology", "BIO"),
            new("History", "HIST"),
            new("Geography", "GEO"),
            new("Civics", "CIV"),
            new("Economics", "ECO"),
            new("Accountancy", "ACC"),
            new("Business Studies", "BST"),
            new("Physical Education", "PE"),
            new("Art & Craft", "ART"),
            new("Music", "MUS"),
            new("Moral Science", "MORAL"),
        };

        public static readonly IReadOnlyList<CatalogSimple> Designations = new CatalogSimple[]
        {
            new("Principal"),
            new("Vice Principal"),
            new("Headmistress / Headmaster"),
            new("Coordinator"),
            new("Teacher"),
            new("Senior Teacher"),
            new("Class Teacher"),
            new("Physical Education Teacher"),
            new("Librarian"),
            new("Accountant"),
            new("Office Administrator"),
            new("Clerk"),
            new("Receptionist"),
            new("Lab Assistant"),
            new("Counsellor"),
            new("Transport In-charge"),
            new("Driver"),
            new("Peon"),
        };

        public static readonly IReadOnlyList<CatalogSimple> Departments = new CatalogSimple[]
        {
            new("Administration"),
            new("Academics"),
            new("Accounts & Finance"),
            new("Examination"),
            new("Admissions & Front Office"),
            new("Library"),
            new("Laboratory"),
            new("Sports"),
            new("Transport"),
            new("IT & Computers"),
            new("Maintenance"),
            new("Human Resources"),
        };

        // "Existing Student" is the name the Add Existing Student screen looks for (StudentOnboardingService.ExistingStudentTypeName)
        public static readonly IReadOnlyList<CatalogSimple> AdmissionTypes = new CatalogSimple[]
        {
            new("New Admission"),
            new("Existing Student"),
            new("Transfer Admission"),
            new("Re-admission"),
            new("RTE Admission"),
            new("Sibling Admission"),
            new("Staff Ward"),
        };

        public static readonly IReadOnlyList<CatalogDocument> DocumentTypes = new CatalogDocument[]
        {
            new("BIRTH_CERT", "Birth Certificate", "STUDENT", true),
            new("AADHAAR", "Aadhaar Card", "BOTH", false),
            new("TC", "Transfer Certificate", "STUDENT", false),
            new("MARKSHEET", "Previous Marksheet", "STUDENT", false),
            new("PHOTO", "Passport Size Photo", "BOTH", true),
            new("ADDR_PROOF", "Address Proof", "BOTH", false),
            new("CASTE_CERT", "Caste Certificate", "STUDENT", false),
            new("MEDICAL_CERT", "Medical Certificate", "STUDENT", false),
            new("PARENT_ID", "Parent ID Proof", "STUDENT", false),
            new("PAN", "PAN Card", "EMPLOYEE", false),
            new("QUAL_CERT", "Qualification Certificate", "EMPLOYEE", false),
            new("EXP_CERT", "Experience Certificate", "EMPLOYEE", false),
        };
    }
}
