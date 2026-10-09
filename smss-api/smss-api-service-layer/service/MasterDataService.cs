using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_db_layer.constants;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace smss_api_service_layer.service
{
    public class MasterDataService : IMasterDataService
    {
        private readonly IMasterDataRepository _masterDataRepository;
        private readonly ILogger<MasterDataService> _logger;
        public MasterDataService(IMasterDataRepository masterDataRepository, ILogger<MasterDataService> logger) {
            _masterDataRepository = masterDataRepository;
            _logger = logger;
        }


        // ========================================================= // GET COUNTRIES // =========================================================
        public async Task<ApiResponse<object>> GetCountriesAsync() 
        { 
            try 
            { 
                List<LutCountry> countries = await _masterDataRepository.GetCountriesAsync(); 
                if (countries == null || !countries.Any()) 
                { 
                    return new ApiResponse<object> { Status = false, StatusCode = 404, Message = "No countries found", Data = null }; 
                } 
                return new ApiResponse<object> { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = countries }; 
            } 
            catch (Exception ex) 
            { 
                return new ApiResponse<object> { Status = false, StatusCode = 500, Message = ex.Message, Data = null }; 
            } 
        } 
        
        // ========================================================= // GET STATES BY COUNTRY // =========================================================
        public async Task<ApiResponse<object>> GetStatesAsync( int countryId) 
        { 
            try 
            { 
                List<LutState> states = await _masterDataRepository.GetStatesAsync(countryId); 
                if (states == null || !states.Any()) 
                { 
                    return new ApiResponse<object> 
                    { 
                        Status = false, StatusCode = 404, Message = "No states found for the selected country", Data = null }; 
                } 
                return new ApiResponse<object> { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = states }; 
            } 
            catch (Exception ex) 
            { 
                return new ApiResponse<object> { Status = false, StatusCode = 500, Message = ex.Message, Data = null }; 
            } 
        } 
        // ========================================================= // GET DISTRICTS BY COUNTRY + STATE // =========================================================
        public async Task<ApiResponse<object>> GetDistrictsAsync( int countryId, int stateId) 
        { 
            try 
            { 
                List<LutDistrict> districts = await _masterDataRepository.GetDistrictsAsync( countryId, stateId); 
                if (districts == null || !districts.Any()) 
                { 
                    return new ApiResponse<object> { Status = false, StatusCode = 404, Message = "No districts found for the selected state", Data = null }; 
                } 
                return new ApiResponse<object> { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = districts }; 
            } 
            catch (Exception ex) 
            { 
                return new ApiResponse<object> { Status = false, StatusCode = 500, Message = ex.Message, Data = null }; 
            } 
        } 
        
        // ========================================================= // GET CITIES BY COUNTRY + STATE + DISTRICT // =========================================================
        public async Task<ApiResponse<object>> GetCitiesAsync( int countryId, int stateId, int districtId) 
        { 
            try 
            { 
                List<LutCity> cities = await _masterDataRepository.GetCitiesAsync( countryId, stateId, districtId); 
                if (cities == null || !cities.Any()) 
                { 
                    return new ApiResponse<object> { Status = false, StatusCode = 404, Message = "No cities found for the selected district", Data = null }; 
                } 
                return new ApiResponse<object> { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = cities }; 
            } 
            catch (Exception ex) 
            { 
                return new ApiResponse<object> { Status = false, StatusCode = 500, Message = ex.Message, Data = null }; 
            } 
        }

        // =========================================================
        // BOARD TYPES
        // =========================================================

        public async Task<ApiResponse<object>> GetBoardTypesAsync(bool includeInactive = false)
        {
            try
            {
                List<LutBoardType> boardTypes = await _masterDataRepository.GetBoardTypesAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (boardTypes == null || !boardTypes.Any()))
                    return Result(false, 404, "No board types found", null);

                return Result(true, 200, "Successfully fetched", boardTypes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch board types.");
                return Result(false, 500, "Unable to fetch board types.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateBoardTypeAsync(CreateBoardTypeRequestDto request)
        {
            try
            {
                string code = request.BoardCode.Trim().ToUpperInvariant();
                string name = request.BoardName.Trim();

                if (await _masterDataRepository.BoardTypeExistsAsync(code, name))
                    return Result(false, 409, "A board type with the same code or name already exists.", null);

                var entity = new LutBoardType
                {
                    BoardCode = code,
                    BoardName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddBoardTypeAsync(entity))
                    return Result(false, 409, "A board type with the same code or name already exists.", null);

                _logger.LogInformation("Board type {Code} created.", code);
                return Result(true, 201, "Board type created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create board type.");
                return Result(false, 500, "Unable to create board type.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateBoardTypeAsync(long id, UpdateBoardTypeRequestDto request)
        {
            try
            {
                LutBoardType? entity = await _masterDataRepository.GetBoardTypeByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Board type not found.", null);

                string name = request.BoardName.Trim();

                if (await _masterDataRepository.BoardTypeExistsAsync(entity.BoardCode, name, excludeId: id))
                    return Result(false, 409, "Another board type with the same name already exists.", null);

                entity.BoardName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateBoardTypeAsync(entity))
                    return Result(false, 409, "Another board type with the same name already exists.", null);

                _logger.LogInformation("Board type {Id} updated.", id);
                return Result(true, 200, "Board type updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update board type {Id}.", id);
                return Result(false, 500, "Unable to update board type.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleBoardTypeStatusAsync(long id)
        {
            try
            {
                LutBoardType? entity = await _masterDataRepository.GetBoardTypeByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Board type not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateBoardTypeAsync(entity);

                _logger.LogInformation("Board type {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "Board type activated." : "Board type deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle board type {Id}.", id);
                return Result(false, 500, "Unable to change board type status.", null);
            }
        }

        private static ApiResponse<object> Result(bool status, int statusCode, string message, object? data) =>
            new ApiResponse<object> { Status = status, StatusCode = statusCode, Message = message, Data = data };

        // =========================================================
        // SCHOOL TYPES
        // =========================================================

        public async Task<ApiResponse<object>> GetSchoolTypesAsync(bool includeInactive = false)
        {
            try
            {
                List<LutSchoolType> schoolTypes = await _masterDataRepository.GetSchoolTypesAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (schoolTypes == null || !schoolTypes.Any()))
                    return Result(false, 404, "No school types found", null);

                return Result(true, 200, "Successfully fetched", schoolTypes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch school types.");
                return Result(false, 500, "Unable to fetch school types.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateSchoolTypeAsync(CreateSchoolTypeRequestDto request)
        {
            try
            {
                string code = request.SchoolTypeCode.Trim().ToUpperInvariant();
                string name = request.SchoolTypeName.Trim();

                if (await _masterDataRepository.SchoolTypeExistsAsync(code, name))
                    return Result(false, 409, "A school type with the same code or name already exists.", null);

                var entity = new LutSchoolType
                {
                    SchoolTypeCode = code,
                    SchoolTypeName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddSchoolTypeAsync(entity))
                    return Result(false, 409, "A school type with the same code or name already exists.", null);

                _logger.LogInformation("School type {Code} created.", code);
                return Result(true, 201, "School type created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create school type.");
                return Result(false, 500, "Unable to create school type.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateSchoolTypeAsync(long id, UpdateSchoolTypeRequestDto request)
        {
            try
            {
                LutSchoolType? entity = await _masterDataRepository.GetSchoolTypeByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "School type not found.", null);

                string name = request.SchoolTypeName.Trim();

                if (await _masterDataRepository.SchoolTypeExistsAsync(entity.SchoolTypeCode, name, excludeId: id))
                    return Result(false, 409, "Another school type with the same name already exists.", null);

                entity.SchoolTypeName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateSchoolTypeAsync(entity))
                    return Result(false, 409, "Another school type with the same name already exists.", null);

                _logger.LogInformation("School type {Id} updated.", id);
                return Result(true, 200, "School type updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update school type {Id}.", id);
                return Result(false, 500, "Unable to update school type.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleSchoolTypeStatusAsync(long id)
        {
            try
            {
                LutSchoolType? entity = await _masterDataRepository.GetSchoolTypeByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "School type not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateSchoolTypeAsync(entity);

                _logger.LogInformation("School type {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "School type activated." : "School type deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle school type {Id}.", id);
                return Result(false, 500, "Unable to change school type status.", null);
            }
        }


        // =========================================================
        // SCHOOL LEVELS
        // =========================================================

        public async Task<ApiResponse<object>> GetSchoolLevelsAsync(bool includeInactive = false)
        {
            try
            {
                List<LutSchoolLevel> schoolLevels = await _masterDataRepository.GetSchoolLevelsAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (schoolLevels == null || !schoolLevels.Any()))
                    return Result(false, 404, "No school levels found", null);

                return Result(true, 200, "Successfully fetched", schoolLevels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch school levels.");
                return Result(false, 500, "Unable to fetch school levels.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateSchoolLevelAsync(CreateSchoolLevelRequestDto request)
        {
            try
            {
                string code = request.SchoolLevelCode.Trim().ToUpperInvariant();
                string name = request.SchoolLevelName.Trim();

                if (await _masterDataRepository.SchoolLevelExistsAsync(code, name))
                    return Result(false, 409, "A school level with the same code or name already exists.", null);

                var entity = new LutSchoolLevel
                {
                    SchoolLevelCode = code,
                    SchoolLevelName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddSchoolLevelAsync(entity))
                    return Result(false, 409, "A school level with the same code or name already exists.", null);

                _logger.LogInformation("School level {Code} created.", code);
                return Result(true, 201, "School level created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create school level.");
                return Result(false, 500, "Unable to create school level.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateSchoolLevelAsync(long id, UpdateSchoolLevelRequestDto request)
        {
            try
            {
                LutSchoolLevel? entity = await _masterDataRepository.GetSchoolLevelByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "School level not found.", null);

                string name = request.SchoolLevelName.Trim();

                if (await _masterDataRepository.SchoolLevelExistsAsync(entity.SchoolLevelCode, name, excludeId: id))
                    return Result(false, 409, "Another school level with the same name already exists.", null);

                entity.SchoolLevelName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateSchoolLevelAsync(entity))
                    return Result(false, 409, "Another school level with the same name already exists.", null);

                _logger.LogInformation("School level {Id} updated.", id);
                return Result(true, 200, "School level updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update school level {Id}.", id);
                return Result(false, 500, "Unable to update school level.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleSchoolLevelStatusAsync(long id)
        {
            try
            {
                LutSchoolLevel? entity = await _masterDataRepository.GetSchoolLevelByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "School level not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateSchoolLevelAsync(entity);

                _logger.LogInformation("School level {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "School level activated." : "School level deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle school level {Id}.", id);
                return Result(false, 500, "Unable to change school level status.", null);
            }
        }

        // =========================================================
        // STATUS
        // =========================================================

        public async Task<ApiResponse<object>> GetStatusAsync(bool includeInactive = false)
        {
            try
            {
                List<LutStatus> status = await _masterDataRepository.GetStatusAsync(includeInactive);

                if (!includeInactive && (status == null || !status.Any()))
                    return Result(false, 404, "No status found", null);

                return Result(true, 200, "Successfully fetched", status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch status.");
                return Result(false, 500, "Unable to fetch status.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateStatusAsync(CreateStatusRequestDto request)
        {
            try
            {
                string name = request.Sname.Trim();
                string type = request.Stype.Trim();

                if (await _masterDataRepository.StatusExistsAsync(name, type))
                    return Result(false, 409, "This status already exists for the selected type.", null);

                var entity = new LutStatus { Sname = name, Stype = type, IsActive = true };

                if (!await _masterDataRepository.AddStatusAsync(entity))
                    return Result(false, 409, "This status already exists for the selected type.", null);

                _logger.LogInformation("Status {Name} ({Type}) created.", name, type);
                return Result(true, 201, "Status created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create status.");
                return Result(false, 500, "Unable to create status.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateStatusAsync(int id, UpdateStatusRequestDto request)
        {
            try
            {
                LutStatus? entity = await _masterDataRepository.GetStatusByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Status not found.", null);

                string name = request.Sname.Trim();
                string type = request.Stype.Trim();

                if (await _masterDataRepository.StatusExistsAsync(name, type, excludeId: id))
                    return Result(false, 409, "This status already exists for the selected type.", null);

                entity.Sname = name;
                entity.Stype = type;

                if (!await _masterDataRepository.UpdateStatusAsync(entity))
                    return Result(false, 409, "This status already exists for the selected type.", null);

                _logger.LogInformation("Status {Id} updated.", id);
                return Result(true, 200, "Status updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status {Id}.", id);
                return Result(false, 500, "Unable to update status.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleStatusStatusAsync(int id)
        {
            try
            {
                LutStatus? entity = await _masterDataRepository.GetStatusByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Status not found.", null);

                entity.IsActive = !entity.IsActive;
                await _masterDataRepository.UpdateStatusAsync(entity);

                _logger.LogInformation("Status {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "Status activated." : "Status deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle status {Id}.", id);
                return Result(false, 500, "Unable to change status.", null);
            }
        }

        // =========================================================
        // ROLES
        // =========================================================

        // Platform logic depends on these; they can never be deactivated.
        private static readonly HashSet<string> SystemRoleCodes =
            new(StringComparer.OrdinalIgnoreCase) { "SUPER_ADMIN", "SCHOOL_ADMIN" };

        // NULL status_id counts as active.
        private static RoleDto ToRoleDto(LutRole r, int inactiveStatusId) => new()
        {
            RoleId = r.RoleId,
            RoleCode = r.RoleCode,
            RoleName = r.RoleName,
            Description = r.Description,
            StatusId = r.StatusId,
            IsActive = r.StatusId != inactiveStatusId,
            IsProtected = SystemRoleCodes.Contains(r.RoleCode),
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };

        public async Task<ApiResponse<object>> GetRolesAsync(bool includeInactive = false)
        {
            try
            {
                List<LutRole> roles = await _masterDataRepository.GetRolesAsync(includeInactive);

                if (!includeInactive && (roles == null || !roles.Any()))
                    return Result(false, 404, "No roles found", null);

                int inactiveId = await GetInactiveStatusIdAsync();
                return Result(true, 200, "Successfully fetched", roles.Select(r => ToRoleDto(r, inactiveId)).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch roles.");
                return Result(false, 500, "Unable to fetch roles.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateRoleAsync(CreateRoleRequestDto request)
        {
            try
            {
                string code = request.RoleCode.Trim().ToUpperInvariant();
                string name = request.RoleName.Trim();

                if (await _masterDataRepository.RoleExistsAsync(code, name))
                    return Result(false, 409, "A role with the same code or name already exists.", null);

                int activeId = await GetActiveStatusIdAsync();
                int inactiveId = await GetInactiveStatusIdAsync();

                var entity = new LutRole
                {
                    RoleCode = code,
                    RoleName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    StatusId = activeId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddRoleAsync(entity))
                    return Result(false, 409, "A role with the same code or name already exists.", null);

                _logger.LogInformation("Role {Code} created.", code);
                return Result(true, 201, "Role created successfully.", ToRoleDto(entity, inactiveId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create role.");
                return Result(false, 500, "Unable to create role.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateRoleAsync(long id, UpdateRoleRequestDto request)
        {
            try
            {
                LutRole? entity = await _masterDataRepository.GetRoleByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Role not found.", null);

                string name = request.RoleName.Trim();

                if (await _masterDataRepository.RoleExistsAsync(entity.RoleCode, name, excludeId: id))
                    return Result(false, 409, "Another role with the same name already exists.", null);

                entity.RoleName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateRoleAsync(entity))
                    return Result(false, 409, "Another role with the same name already exists.", null);

                int inactiveId = await GetInactiveStatusIdAsync();
                _logger.LogInformation("Role {Id} updated.", id);
                return Result(true, 200, "Role updated successfully.", ToRoleDto(entity, inactiveId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update role {Id}.", id);
                return Result(false, 500, "Unable to update role.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleRoleStatusAsync(long id)
        {
            try
            {
                LutRole? entity = await _masterDataRepository.GetRoleByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Role not found.", null);

                int activeId = await GetActiveStatusIdAsync();
                int inactiveId = await GetInactiveStatusIdAsync();
                bool isActive = entity.StatusId != inactiveId;

                if (isActive)
                {
                    // Deactivating: enforce the guards.
                    if (SystemRoleCodes.Contains(entity.RoleCode))
                        return Result(false, 403, "System roles cannot be deactivated.", null);

                    int assigned = await _masterDataRepository.CountRoleAssignmentsAsync(id);
                    if (assigned > 0)
                        return Result(false, 409,
                            $"Cannot deactivate this role: {assigned} user(s) are assigned to it. Reassign them first.", null);

                    entity.StatusId = inactiveId;
                }
                else
                {
                    entity.StatusId = activeId;
                }

                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateRoleAsync(entity);

                bool nowActive = entity.StatusId != inactiveId;
                _logger.LogInformation("Role {Id} set to {State}.", id, nowActive ? "active" : "inactive");
                return Result(true, 200, nowActive ? "Role activated." : "Role deactivated.", ToRoleDto(entity, inactiveId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle role {Id}.", id);
                return Result(false, 500, "Unable to change role status.", null);
            }
        }
        // ---------- core status resolution (by name, never by hardcoded id) ----------
        private int? _activeStatusId;
        private int? _inactiveStatusId;

        private async Task<int> GetActiveStatusIdAsync()
        {
            _activeStatusId ??= await ResolveStatusIdAsync(StatusLookup.Active);
            return _activeStatusId.Value;
        }

        private async Task<int> GetInactiveStatusIdAsync()
        {
            _inactiveStatusId ??= await ResolveStatusIdAsync(StatusLookup.Inactive);
            return _inactiveStatusId.Value;
        }

        private async Task<int> ResolveStatusIdAsync(string name)
        {
            int? id = await _masterDataRepository.GetStatusIdByNameAsync(name, StatusLookup.GeneralType);
            return id ?? throw new InvalidOperationException(
                $"Core status '{name}' ({StatusLookup.GeneralType}) was not found in lut_status.");
        }

        // The rows the platform looks up by name: they must keep their name, type and active flag.
        private static bool IsCoreStatus(LutStatus s) =>
            s.Stype.Equals(StatusLookup.GeneralType, StringComparison.OrdinalIgnoreCase) &&
            (s.Sname.Equals(StatusLookup.Active, StringComparison.OrdinalIgnoreCase) ||
             s.Sname.Equals(StatusLookup.Inactive, StringComparison.OrdinalIgnoreCase));

        




        // =========================================================
        // RELIGION CATEGORYS
        // =========================================================

        public async Task<ApiResponse<object>> GetReligionCategoriesAsync(bool includeInactive = false)
        {
            try
            {
                List<LutReligionCategory> items = await _masterDataRepository.GetReligionCategoriesAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (items == null || !items.Any()))
                    return Result(false, 404, "No religion category records found", null);

                return Result(true, 200, "Successfully fetched", items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch religion category records.");
                return Result(false, 500, "Unable to fetch religion category records.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateReligionCategoryAsync(CreateReligionCategoryRequestDto request)
        {
            try
            {
                string code = request.ReligionCode.Trim().ToUpperInvariant();
                string name = request.ReligionName.Trim();

                if (await _masterDataRepository.ReligionCategoryExistsAsync(code, name))
                    return Result(false, 409, "A religion category with the same code or name already exists.", null);

                var entity = new LutReligionCategory
                {
                    ReligionCode = code,
                    ReligionName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddReligionCategoryAsync(entity))
                    return Result(false, 409, "A religion category with the same code or name already exists.", null);

                _logger.LogInformation("Religion category {Code} created.", code);
                return Result(true, 201, "Religion category created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create religion category.");
                return Result(false, 500, "Unable to create religion category.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateReligionCategoryAsync(long id, UpdateReligionCategoryRequestDto request)
        {
            try
            {
                LutReligionCategory? entity = await _masterDataRepository.GetReligionCategoryByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Religion category not found.", null);

                string name = request.ReligionName.Trim();

                if (await _masterDataRepository.ReligionCategoryExistsAsync(entity.ReligionCode, name, excludeId: id))
                    return Result(false, 409, "Another religion category with the same name already exists.", null);

                entity.ReligionName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateReligionCategoryAsync(entity))
                    return Result(false, 409, "Another religion category with the same name already exists.", null);

                _logger.LogInformation("Religion category {Id} updated.", id);
                return Result(true, 200, "Religion category updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update religion category {Id}.", id);
                return Result(false, 500, "Unable to update religion category.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleReligionCategoryStatusAsync(long id)
        {
            try
            {
                LutReligionCategory? entity = await _masterDataRepository.GetReligionCategoryByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Religion category not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateReligionCategoryAsync(entity);

                _logger.LogInformation("Religion category {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "Religion category activated." : "Religion category deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle religion category {Id}.", id);
                return Result(false, 500, "Unable to change religion category status.", null);
            }
        }

        // =========================================================
        // BLOOD GROUPS
        // =========================================================

        public async Task<ApiResponse<object>> GetBloodGroupsAsync(bool includeInactive = false)
        {
            try
            {
                List<LutBloodGroup> items = await _masterDataRepository.GetBloodGroupsAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (items == null || !items.Any()))
                    return Result(false, 404, "No blood group records found", null);

                return Result(true, 200, "Successfully fetched", items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch blood group records.");
                return Result(false, 500, "Unable to fetch blood group records.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateBloodGroupAsync(CreateBloodGroupRequestDto request)
        {
            try
            {
                string code = request.BloodGroupCode.Trim().ToUpperInvariant();
                string name = request.BloodGroupName.Trim();

                if (await _masterDataRepository.BloodGroupExistsAsync(code, name))
                    return Result(false, 409, "A blood group with the same code or name already exists.", null);

                var entity = new LutBloodGroup
                {
                    BloodGroupCode = code,
                    BloodGroupName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddBloodGroupAsync(entity))
                    return Result(false, 409, "A blood group with the same code or name already exists.", null);

                _logger.LogInformation("Blood group {Code} created.", code);
                return Result(true, 201, "Blood group created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create blood group.");
                return Result(false, 500, "Unable to create blood group.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateBloodGroupAsync(long id, UpdateBloodGroupRequestDto request)
        {
            try
            {
                LutBloodGroup? entity = await _masterDataRepository.GetBloodGroupByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Blood group not found.", null);

                string name = request.BloodGroupName.Trim();

                if (await _masterDataRepository.BloodGroupExistsAsync(entity.BloodGroupCode, name, excludeId: id))
                    return Result(false, 409, "Another blood group with the same name already exists.", null);

                entity.BloodGroupName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateBloodGroupAsync(entity))
                    return Result(false, 409, "Another blood group with the same name already exists.", null);

                _logger.LogInformation("Blood group {Id} updated.", id);
                return Result(true, 200, "Blood group updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update blood group {Id}.", id);
                return Result(false, 500, "Unable to update blood group.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleBloodGroupStatusAsync(long id)
        {
            try
            {
                LutBloodGroup? entity = await _masterDataRepository.GetBloodGroupByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Blood group not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateBloodGroupAsync(entity);

                _logger.LogInformation("Blood group {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "Blood group activated." : "Blood group deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle blood group {Id}.", id);
                return Result(false, 500, "Unable to change blood group status.", null);
            }
        }

        // =========================================================
        // GENDERS
        // =========================================================

        public async Task<ApiResponse<object>> GetGendersAsync(bool includeInactive = false)
        {
            try
            {
                List<LutGender> items = await _masterDataRepository.GetGendersAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (items == null || !items.Any()))
                    return Result(false, 404, "No gender records found", null);

                return Result(true, 200, "Successfully fetched", items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch gender records.");
                return Result(false, 500, "Unable to fetch gender records.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateGenderAsync(CreateGenderRequestDto request)
        {
            try
            {
                string code = request.GenderCode.Trim().ToUpperInvariant();
                string name = request.GenderName.Trim();

                if (await _masterDataRepository.GenderExistsAsync(code, name))
                    return Result(false, 409, "A gender with the same code or name already exists.", null);

                var entity = new LutGender
                {
                    GenderCode = code,
                    GenderName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddGenderAsync(entity))
                    return Result(false, 409, "A gender with the same code or name already exists.", null);

                _logger.LogInformation("Gender {Code} created.", code);
                return Result(true, 201, "Gender created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create gender.");
                return Result(false, 500, "Unable to create gender.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateGenderAsync(long id, UpdateGenderRequestDto request)
        {
            try
            {
                LutGender? entity = await _masterDataRepository.GetGenderByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Gender not found.", null);

                string name = request.GenderName.Trim();

                if (await _masterDataRepository.GenderExistsAsync(entity.GenderCode, name, excludeId: id))
                    return Result(false, 409, "Another gender with the same name already exists.", null);

                entity.GenderName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateGenderAsync(entity))
                    return Result(false, 409, "Another gender with the same name already exists.", null);

                _logger.LogInformation("Gender {Id} updated.", id);
                return Result(true, 200, "Gender updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update gender {Id}.", id);
                return Result(false, 500, "Unable to update gender.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleGenderStatusAsync(long id)
        {
            try
            {
                LutGender? entity = await _masterDataRepository.GetGenderByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Gender not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateGenderAsync(entity);

                _logger.LogInformation("Gender {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "Gender activated." : "Gender deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle gender {Id}.", id);
                return Result(false, 500, "Unable to change gender status.", null);
            }
        }

        // =========================================================
        // STUDENT CATEGORIES
        // =========================================================

        public async Task<ApiResponse<object>> GetStudentCategoriesAsync(bool includeInactive = false)
        {
            try
            {
                List<LutStudentCategory> items = await _masterDataRepository.GetStudentCategoriesAsync(includeInactive);

                // Dropdown callers keep the old 404 behaviour; the admin grid gets an empty list.
                if (!includeInactive && (items == null || !items.Any()))
                    return Result(false, 404, "No student category records found", null);

                return Result(true, 200, "Successfully fetched", items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch student category records.");
                return Result(false, 500, "Unable to fetch student category records.", null);
            }
        }

        public async Task<ApiResponse<object>> CreateStudentCategoryAsync(CreateStudentCategoryRequestDto request)
        {
            try
            {
                string code = request.CategoryCode.Trim().ToUpperInvariant();
                string name = request.CategoryName.Trim();

                if (await _masterDataRepository.StudentCategoryExistsAsync(code, name))
                    return Result(false, 409, "A student category with the same code or name already exists.", null);

                var entity = new LutStudentCategory
                {
                    CategoryCode = code,
                    CategoryName = name,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!await _masterDataRepository.AddStudentCategoryAsync(entity))
                    return Result(false, 409, "A student category with the same code or name already exists.", null);

                _logger.LogInformation("Student category {Code} created.", code);
                return Result(true, 201, "Student category created successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create student category.");
                return Result(false, 500, "Unable to create student category.", null);
            }
        }

        public async Task<ApiResponse<object>> UpdateStudentCategoryAsync(long id, UpdateStudentCategoryRequestDto request)
        {
            try
            {
                LutStudentCategory? entity = await _masterDataRepository.GetStudentCategoryByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Student category not found.", null);

                string name = request.CategoryName.Trim();

                if (await _masterDataRepository.StudentCategoryExistsAsync(entity.CategoryCode, name, excludeId: id))
                    return Result(false, 409, "Another student category with the same name already exists.", null);

                entity.CategoryName = name;
                entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;

                if (!await _masterDataRepository.UpdateStudentCategoryAsync(entity))
                    return Result(false, 409, "Another student category with the same name already exists.", null);

                _logger.LogInformation("Student category {Id} updated.", id);
                return Result(true, 200, "Student category updated successfully.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update student category {Id}.", id);
                return Result(false, 500, "Unable to update student category.", null);
            }
        }

        public async Task<ApiResponse<object>> ToggleStudentCategoryStatusAsync(long id)
        {
            try
            {
                LutStudentCategory? entity = await _masterDataRepository.GetStudentCategoryByIdAsync(id);
                if (entity == null)
                    return Result(false, 404, "Student category not found.", null);

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _masterDataRepository.UpdateStudentCategoryAsync(entity);

                _logger.LogInformation("Student category {Id} set to {State}.", id, entity.IsActive ? "active" : "inactive");
                return Result(true, 200, entity.IsActive ? "Student category activated." : "Student category deactivated.", entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle student category {Id}.", id);
                return Result(false, 500, "Unable to change student category status.", null);
            }
        }
    }
}
