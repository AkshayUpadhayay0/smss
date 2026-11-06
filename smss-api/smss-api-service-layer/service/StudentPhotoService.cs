using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentPhotoService : IStudentPhotoService
    {
        private readonly IStudentRepository _repo;
        private readonly IMyStudentsService _myStudents;
        private readonly IFileStorageService _storage;
        private readonly ILogger<StudentPhotoService> _logger;

        public StudentPhotoService(IStudentRepository repo, IMyStudentsService myStudents, IFileStorageService storage, ILogger<StudentPhotoService> logger)
        { _repo = repo; _myStudents = myStudents; _storage = storage; _logger = logger; }

        private static StudentPhotoResult Fail(int code, string message) => new() { StatusCode = code, Message = message };

        public async Task<StudentPhotoResult> GetAsync(string? schoolId, long callerUserId, PhotoViewer viewer, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var id = (studentId ?? string.Empty).Trim();
                // another school's student (or one that does not exist) is a 404: ids cannot be probed across schools
                var (exists, stored) = await _repo.GetPhotoKeyAsync(schoolId, id, ct);
                if (!exists) return Fail(404, "Student not found.");

                if (viewer == PhotoViewer.Student)
                {
                    var own = await _repo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                    if (!string.Equals(own, id, StringComparison.Ordinal)) return Fail(403, "You can only see your own photo.");
                }
                else if (viewer == PhotoViewer.Teacher)
                {
                    // the sections the teacher is class teacher of or timetabled in: the same scope as My Students
                    var mine = await _myStudents.GetMyClassAsync(schoolId, callerUserId, ct);
                    if (!mine.Status || mine.Data == null || !mine.Data.Sections.Any(s => s.Students.Any(x => string.Equals(x.StudentId, id, StringComparison.Ordinal))))
                        return Fail(403, "You can only see the photos of students in your own classes.");
                }

                if (string.IsNullOrEmpty(stored)) return Fail(404, "This student has no photo.");
                var stream = _storage.OpenPhoto(stored);
                if (stream == null) return Fail(404, "The photo file could not be found.");
                return new StudentPhotoResult { Content = stream, ContentType = StudentPhotos.ContentType(stored), ETag = StudentPhotos.ETag(stored) };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading a student photo");
                return Fail(500, "Something went wrong. Please try again later.");
            }
        }
    }
}
