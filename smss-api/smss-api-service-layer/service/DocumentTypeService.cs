using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class DocumentTypeService : IDocumentTypeService
    {
        private const string DuplicateMessage = "A document type with this code already exists for this school.";

        private readonly IDocumentTypeRepository _repository;
        private readonly ILogger<DocumentTypeService> _logger;

        public DocumentTypeService(IDocumentTypeRepository repository, ILogger<DocumentTypeService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<ApiResponse<List<DocumentTypeResponseDto>>> GetAllAsync(string schoolId, CancellationToken ct)
        {
            try
            {
                var entities = await _repository.GetAllAsync(schoolId, ct);
                var statusNames = await _repository.GetStatusNamesAsync(StatusNames.GeneralType, ct);
                return Result(200, "Document types fetched successfully.", entities.Select(e => MapToDto(e, statusNames)).ToList());
            }
            catch (Exception ex) { return Error<List<DocumentTypeResponseDto>>(ex, "fetching document types"); }
        }

        public async Task<ApiResponse<DocumentTypeResponseDto>> CreateAsync(string schoolId, CreateDocumentTypeRequestDto request, CancellationToken ct)
        {
            try
            {
                // Codes are stored upper-case, so the duplicate check must compare the normalised value
                var code = request.DocumentCode.Trim().ToUpperInvariant();
                if (await _repository.CodeExistsAsync(schoolId, code, null, ct))
                    return Result<DocumentTypeResponseDto>(409, DuplicateMessage, null);

                // New records start Active, resolved by name so no sid is hard-coded
                var activeStatusId = await _repository.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeStatusId is null)
                    return ConfigError<DocumentTypeResponseDto>("Active status missing from lut_status");

                var now = DateTime.UtcNow;
                var entity = new TbDocumentType
                {
                    SchoolId = schoolId,
                    DocumentName = request.DocumentName.Trim(),
                    DocumentCode = code,
                    AppliesTo = request.AppliesTo.Trim().ToUpperInvariant(),
                    IsRequired = request.IsRequired,
                    StatusId = activeStatusId.Value,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _repository.AddAsync(entity, ct);
                await _repository.SaveChangesAsync(ct);

                var statusNames = await _repository.GetStatusNamesAsync(StatusNames.GeneralType, ct);
                return Result(201, "Document type created successfully.", MapToDto(entity, statusNames));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Result<DocumentTypeResponseDto>(409, DuplicateMessage, null);   // lost a race with another request
            }
            catch (Exception ex) { return Error<DocumentTypeResponseDto>(ex, "creating document type"); }
        }

        public async Task<ApiResponse<DocumentTypeResponseDto>> UpdateAsync(string schoolId, UpdateDocumentTypeRequestDto request, CancellationToken ct)
        {
            try
            {
                var entity = await _repository.GetByIdAsync(request.DocumentTypeId, schoolId, ct);
                if (entity is null)
                    return Result<DocumentTypeResponseDto>(404, "Document type not found.", null);

                var code = request.DocumentCode.Trim().ToUpperInvariant();
                if (await _repository.CodeExistsAsync(schoolId, code, request.DocumentTypeId, ct))
                    return Result<DocumentTypeResponseDto>(409, DuplicateMessage, null);

                entity.DocumentName = request.DocumentName.Trim();
                entity.DocumentCode = code;
                entity.AppliesTo = request.AppliesTo.Trim().ToUpperInvariant();
                entity.IsRequired = request.IsRequired;
                entity.UpdatedAt = DateTime.UtcNow;

                await _repository.SaveChangesAsync(ct);

                var statusNames = await _repository.GetStatusNamesAsync(StatusNames.GeneralType, ct);
                return Result(200, "Document type updated successfully.", MapToDto(entity, statusNames));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Result<DocumentTypeResponseDto>(409, DuplicateMessage, null);
            }
            catch (Exception ex) { return Error<DocumentTypeResponseDto>(ex, "updating document type"); }
        }

        public async Task<ApiResponse<DocumentTypeResponseDto>> ToggleStatusAsync(string schoolId, long documentTypeId, CancellationToken ct)
        {
            try
            {
                var entity = await _repository.GetByIdAsync(documentTypeId, schoolId, ct);
                if (entity is null)
                    return Result<DocumentTypeResponseDto>(404, "Document type not found.", null);

                var activeId = await _repository.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                var inactiveId = await _repository.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId is null || inactiveId is null)
                    return ConfigError<DocumentTypeResponseDto>("'Active'/'Inactive' general status is missing in lut_status");

                var deactivating = entity.StatusId == activeId;
                entity.StatusId = deactivating ? inactiveId.Value : activeId.Value;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repository.SaveChangesAsync(ct);

                var statusNames = await _repository.GetStatusNamesAsync(StatusNames.GeneralType, ct);
                return Result(200, deactivating ? "Document type deactivated successfully." : "Document type activated successfully.",
                    MapToDto(entity, statusNames));
            }
            catch (Exception ex) { return Error<DocumentTypeResponseDto>(ex, "toggling document type status"); }
        }

        // IsActive comes from the row's real status (resolved by name), never assumed
        private static DocumentTypeResponseDto MapToDto(TbDocumentType e, Dictionary<int, string> statusNames) => new()
        {
            DocumentTypeId = e.DocumentTypeId,
            SchoolId = e.SchoolId,
            DocumentName = e.DocumentName,
            DocumentCode = e.DocumentCode,
            AppliesTo = e.AppliesTo,
            IsRequired = e.IsRequired,
            IsActive = statusNames.TryGetValue(e.StatusId, out var name) && name == StatusNames.Active,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        };

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }

        private ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }
    }
}
