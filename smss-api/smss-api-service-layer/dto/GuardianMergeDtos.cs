using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class DuplicateStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class DuplicateGuardianDto
    {
        public long GuardianId { get; set; }
        public string Name { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        /// <summary>The whole number: this report is for the School Admin and the Principal only.</summary>
        public string MobileNumber { get; set; } = null!;
        public bool HasWhatsApp { get; set; }
        public bool HasEmail { get; set; }
        /// <summary>The guardian already has a login (a later stage creates them): a merge of two guardians that both have one is refused.</summary>
        public bool HasLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>The oldest guardian of the group: the default survivor.</summary>
        public bool IsOldest { get; set; }
        public List<DuplicateStudentDto> Students { get; set; } = new();
    }

    public class DuplicateGroupDto
    {
        /// <summary>The 10-digit number the guardians share (fn_phone_key).</summary>
        public string PhoneKey { get; set; } = null!;
        /// <summary>SAME (the names are the same), SIMILAR (nearly the same or one contains the other) or DIFFERENT (a shared phone, but not the same person's name).</summary>
        public string NameMatch { get; set; } = null!;
        public List<DuplicateGuardianDto> Guardians { get; set; } = new();
    }

    public class DuplicateReportDto
    {
        public int GroupCount { get; set; }
        public int GuardianCount { get; set; }
        public List<DuplicateGroupDto> Groups { get; set; } = new();
    }

    public class MergeRequestDto
    {
        /// <summary>The guardian that stays (the default in the screen is the oldest of the pair).</summary>
        [Required] public long SurvivorId { get; set; }
        /// <summary>The guardian that is absorbed: deactivated and pointed at the survivor, never deleted.</summary>
        [Required] public long AbsorbedId { get; set; }
        /// <summary>The explicit confirmation: a merge is refused without it. (Ignored by the preview.)</summary>
        public bool Confirm { get; set; }
        [MaxLength(500)] public string? Reason { get; set; }
    }

    public class MergeGuardianBriefDto
    {
        public long GuardianId { get; set; }
        public string Name { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        public string? WhatsAppNumber { get; set; }
        public string? Email { get; set; }
        public string? Occupation { get; set; }
        public bool HasLogin { get; set; }
    }

    public class MergeLinkDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string Relationship { get; set; } = null!;
        public bool IsPrimary { get; set; }
        /// <summary>MOVE: the link goes to the survivor. COMBINE: the survivor is already linked to this student: the flags are combined and the extra link is dropped.</summary>
        public string Action { get; set; } = null!;
    }

    public class MergeApplicationDto
    {
        public long AdmissionId { get; set; }
        public string ApplicationNumber { get; set; } = null!;
        /// <summary>PRIMARY (tb_admissions.guardian_id) or EXTRA (tb_admission_guardians).</summary>
        public string Role { get; set; } = null!;
        /// <summary>REPOINT, or DROP when the survivor is already on that application.</summary>
        public string Action { get; set; } = null!;
    }

    public class MergePreviewDto
    {
        public MergeGuardianBriefDto Survivor { get; set; } = null!;
        public MergeGuardianBriefDto Absorbed { get; set; } = null!;
        public List<MergeLinkDto> Links { get; set; } = new();
        /// <summary>Only the fields the survivor has none of: whatsapp_number, email, occupation.</summary>
        public Dictionary<string, string> FieldsToCopy { get; set; } = new();
        public List<MergeApplicationDto> Applications { get; set; } = new();
        /// <summary>Guardians already merged into the absorbed one: they are repointed to the survivor (no chains).</summary>
        public List<long> GuardiansToRepoint { get; set; } = new();
        /// <summary>NONE, MOVE (only the absorbed guardian has a login: it moves to the survivor) or BLOCKED (both have one).</summary>
        public string Account { get; set; } = "NONE";
        public bool CanMerge { get; set; }
        /// <summary>Why the merge cannot be done now (empty when it can).</summary>
        public List<string> Blockers { get; set; } = new();
    }

    public class MergeResultDto
    {
        public long MergeId { get; set; }
        public long SurvivorId { get; set; }
        public long AbsorbedId { get; set; }
        public int LinksMoved { get; set; }
        public int LinksCombined { get; set; }
        public int ApplicationsRepointed { get; set; }
        public int GuardiansRepointed { get; set; }
        public List<string> FieldsCopied { get; set; } = new();
        public bool AccountMoved { get; set; }
    }
}
