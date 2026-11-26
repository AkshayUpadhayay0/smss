using Microsoft.EntityFrameworkCore.Storage;

namespace smss_api_db_layer.@interface
{
    /// <summary>Everything needed to decide and to print a transfer certificate for one student, read from the live data at issue time (the printed copy is then frozen in a snapshot).</summary>
    public class TcContext
    {
        public string StudentId { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string AdmissionNumber { get; set; } = null!;
        public DateOnly DateOfBirth { get; set; }
        public DateOnly? AdmissionDate { get; set; }
        public string? Gender { get; set; }
        // the latest enrolment
        public long EnrollmentId { get; set; }
        public string? Outcome { get; set; }
        public DateTime? OutcomeAt { get; set; }
        public DateOnly? LeavingDate { get; set; }
        public string? LeavingReason { get; set; }
        public long YearId { get; set; }
        public string YearName { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public DateOnly EnrollmentDate { get; set; }
        /// <summary>PROMOTE or REPEAT when this enrolment was created by a promotion run, else null.</summary>
        public string? EnteredBy { get; set; }
        public string? FromClassName { get; set; }
        /// <summary>The ISSUED certificate already on this enrolment, if any.</summary>
        public long? ExistingCertificateId { get; set; }
        public string? ExistingCertificateNumber { get; set; }
        // the school
        public string SchoolName { get; set; } = null!;
        public string SchoolCode { get; set; } = null!;
        public string? BoardName { get; set; }
        public int? EstablishYear { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? CityName { get; set; }
        public string? StateName { get; set; }
        public string? Pincode { get; set; }
        public string? SchoolEmail { get; set; }
        public string? SchoolMobile { get; set; }
        public string? Website { get; set; }
        public string? PrincipalName { get; set; }
        // the parents
        public string? GuardianName { get; set; }
        public string? GuardianRelationship { get; set; }
        public string? FatherName { get; set; }
        public string? MotherName { get; set; }
    }

    public class TcRow
    {
        public long CertificateId { get; set; }
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long EnrollmentId { get; set; }
        public string CertificateNumber { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string Snapshot { get; set; } = null!;
        public string Conduct { get; set; } = null!;
        public string? Remarks { get; set; }
        public bool FeesCleared { get; set; }
        public bool OverrideApplied { get; set; }
        public string? OverrideReason { get; set; }
        public decimal OutstandingAmount { get; set; }
        public DateTime IssuedAt { get; set; }
        public string? IssuedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidedBy { get; set; }
        public string? VoidReason { get; set; }
        public int Duplicates { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? Outcome { get; set; }
    }

    public class TcCopyRow
    {
        public string Kind { get; set; } = null!;
        public int CopyNo { get; set; }
        public DateTime PrintedAt { get; set; }
        public string? PrintedBy { get; set; }
    }

    public record TcInsert(string SchoolId, string StudentId, long EnrollmentId, string Number, string YearLabel, int Sequence, string SnapshotJson, string Conduct, string? Remarks, bool FeesCleared,
        bool OverrideApplied, string? OverrideReason, decimal Outstanding, long UserId);

    /// <summary>Transfer certificates (Stage 8b). Plain SQL, every statement filtered by the school id.</summary>
    public interface ITransferCertificateRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>The student's latest enrolment with the school, the parents and any ISSUED certificate. Null when the student is not in this school or has no enrolment.</summary>
        Task<TcContext?> GetContextAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>Takes the next number for the school and year label (a per-school advisory lock, then an atomic +1: a number is never handed out twice, a void one is never reused). Call inside a transaction.</summary>
        Task<int> AllocateNumberAsync(string schoolId, string yearLabel, CancellationToken ct);
        /// <summary>Writes the certificate and the ORIGINAL audit row. Returns the new id.</summary>
        Task<long> InsertAsync(TcInsert w, CancellationToken ct);
        Task<TcRow?> GetAsync(string schoolId, long certificateId, CancellationToken ct);
        Task<List<TcRow>> ListAsync(string schoolId, string? studentId, string? status, CancellationToken ct);
        Task<List<TcCopyRow>> ListCopiesAsync(string schoolId, long certificateId, CancellationToken ct);
        /// <summary>Audits one print. A DUPLICATE gets the next copy number of that certificate (under a lock); the original is always 0. Returns the copy number.</summary>
        Task<int> AddCopyAsync(string schoolId, long certificateId, bool duplicate, long userId, CancellationToken ct);
        /// <summary>ISSUED to VOID, only while ISSUED. The row and its number stay. False when it was not ISSUED.</summary>
        Task<bool> VoidAsync(string schoolId, long certificateId, string reason, long userId, CancellationToken ct);
    }
}
