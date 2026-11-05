using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Printable student ID cards (read-only; no tables of its own). The school always comes from the caller's token.</summary>
    public interface IStudentIdCardService
    {
        /// <summary>One student's card (front + back) as a PDF. 404 when the student is not in the caller's school; 400 when they cannot be issued one (inactive / not enrolled this year).</summary>
        Task<ApiResponse<IdCardFileDto>> GetStudentCardAsync(string? schoolId, string studentId, CancellationToken ct);

        /// <summary>The signed-in Student's OWN card as a PDF (the student is the caller's login id, never a client value). 404 with a calm message when the card cannot be issued (left / inactive / not enrolled this year).</summary>
        Task<ApiResponse<IdCardFileDto>> GetMyCardAsync(string? schoolId, long callerUserId, CancellationToken ct);

        /// <summary>Both faces of the Student's OWN card as PNG, with the school's saved template and settings. Same 404 rule.</summary>
        Task<ApiResponse<IdCardPreviewDto>> GetMyCardPreviewAsync(string? schoolId, long callerUserId, CancellationToken ct);

        /// <summary>A whole section's cards on A4 sheets. 404 when the section is not in the caller's school; 400 over the per-request cap.</summary>
        Task<ApiResponse<IdCardBulkResultDto>> GetSectionCardsAsync(string? schoolId, long sectionId, CancellationToken ct);

        /// <summary>Both faces of a template as PNG, using the given (possibly unsaved) settings; a real student of the caller's school, or built-in sample data.</summary>
        Task<ApiResponse<IdCardPreviewDto>> PreviewAsync(string? schoolId, IdCardPreviewRequest request, CancellationToken ct);
    }
}
