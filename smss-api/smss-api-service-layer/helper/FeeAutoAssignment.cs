using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Automatic fee structure assignment for a newly created student (confirm admission / existing-student onboarding).
    /// It is a convenience on top of creating the student, so it runs AFTER the student has been saved and can never undo
    /// or fail that: if it cannot be written, the student stays and the caller gets a warning to assign it by hand.
    /// </summary>
    public static class FeeAutoAssignment
    {
        public static string NoStructureWarning(string className, string yearName) =>
            $"No fee structure found for {className}, AY {yearName} — assign manually from the student's Fees tab.";

        public const string AssignFailedWarning =
            "The student was added, but the fee structure could not be assigned automatically — assign it from the student's Fees tab.";

        /// <summary>Null when the assignment was saved; otherwise the warning to show.</summary>
        public static async Task<string?> TryAssignAsync(
            IAdmissionRepository repo, ILogger logger, string schoolId, string studentId, long academicYearId, long feeStructureId, DateTime now, CancellationToken ct)
        {
            try
            {
                await repo.AddStudentFeeStructureAsync(new TbStudentFeeStructure
                {
                    StudentId = studentId,
                    FeeStructureId = feeStructureId,
                    SchoolId = schoolId,
                    AcademicYearId = academicYearId,
                    CreatedAt = now,
                    UpdatedAt = now
                }, ct);
                await repo.SaveChangesAsync(ct);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Student {StudentId} was created but its fee structure {FeeStructureId} could not be assigned", studentId, feeStructureId);
                repo.ClearTracking();   // forget the row that failed to save
                return AssignFailedWarning;
            }
        }
    }
}
