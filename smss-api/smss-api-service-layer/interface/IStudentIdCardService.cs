using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Printable student ID cards (read-only; no tables of its own). The school always comes from the caller's token.</summary>
    public interface IStudentIdCardService
    {
        /// <summary>One student's card (front + back) as a PDF. 404 when the student is not in the caller's school; 400 when they cannot be issued one (inactive / not enrolled this year).</summary>
        Task<ApiResponse<IdCardFileDto>> GetStudentCardAsync(string? schoolId, string studentId, CancellationToken ct);

        /// <summary>A whole section's cards on A4 sheets. 404 when the section is not in the caller's school; 400 over the per-request cap.</summary>
        Task<ApiResponse<IdCardBulkResultDto>> GetSectionCardsAsync(string? schoolId, long sectionId, CancellationToken ct);
    }
}
