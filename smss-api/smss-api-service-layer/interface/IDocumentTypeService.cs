using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IDocumentTypeService
    {
        Task<ApiResponse<List<DocumentTypeResponseDto>>> GetAllAsync(string schoolId, CancellationToken ct);
        Task<ApiResponse<DocumentTypeResponseDto>> CreateAsync(string schoolId, CreateDocumentTypeRequestDto request, CancellationToken ct);
        Task<ApiResponse<DocumentTypeResponseDto>> UpdateAsync(string schoolId, UpdateDocumentTypeRequestDto request, CancellationToken ct);
        Task<ApiResponse<DocumentTypeResponseDto>> ToggleStatusAsync(string schoolId, long documentTypeId, CancellationToken ct);
    }
}
