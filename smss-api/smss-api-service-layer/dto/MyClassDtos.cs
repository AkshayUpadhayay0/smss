namespace smss_api_service_layer.dto
{
    /// <summary>One student in a teacher's own class list. Deliberately no fee, login, contact or Aadhaar fields: the list is for knowing who is in the room.</summary>
    public class MyClassStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
        public string? GenderName { get; set; }
        public string StatusName { get; set; } = null!;
        public string? ProfilePhotoUrl { get; set; }
    }

    /// <summary>A section the caller is the class teacher of and/or teaches a subject in (per the Timetable), with its students.</summary>
    public class MyClassSectionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        /// <summary>True when the caller is the section's Active class teacher for the year (the must-have scope).</summary>
        public bool IsClassTeacher { get; set; }
        /// <summary>The subjects the caller is timetabled to teach in this section (empty when they are only its class teacher).</summary>
        public List<string> Subjects { get; set; } = new();
        public List<MyClassStudentDto> Students { get; set; } = new();
    }

    /// <summary>GET api/Student/my-class: the students a Teacher may see, grouped by section, for the school's current academic year.</summary>
    public class MyClassDto
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public List<MyClassSectionDto> Sections { get; set; } = new();
    }
}
