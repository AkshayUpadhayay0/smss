namespace smss_api_service_layer.@interface
{
    /// <summary>How the caller reaches a student's photo: their own (Student login), the sections they teach (Teacher), or any student of the school (the other staff roles).</summary>
    public enum PhotoViewer { Student, Teacher, Staff }

    public sealed class StudentPhotoResult
    {
        public Stream? Content { get; init; }
        public string ContentType { get; init; } = "";
        public string ETag { get; init; } = "";
        public int StatusCode { get; init; } = 200;
        public string Message { get; init; } = "";
    }

    /// <summary>Authenticated read of a student's photo from private storage. The school always comes from the token; another school's student is a 404.</summary>
    public interface IStudentPhotoService
    {
        Task<StudentPhotoResult> GetAsync(string? schoolId, long callerUserId, PhotoViewer viewer, string studentId, CancellationToken ct);
    }
}
