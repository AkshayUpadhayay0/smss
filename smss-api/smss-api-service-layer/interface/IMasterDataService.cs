using smss_api_service_layer.dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface IMasterDataService
    {

        // Countries
        Task<ApiResponse<object>> GetCountriesAsync();

        // States by Country
        Task<ApiResponse<object>> GetStatesAsync(int countryId);

        // Districts by Country + State
        Task<ApiResponse<object>> GetDistrictsAsync(int countryId, int stateId);

        // Cities by Country + State + District
        Task<ApiResponse<object>> GetCitiesAsync(int countryId, int stateId, int districtId);

        // BOARD TYPE
        Task<ApiResponse<object>> GetBoardTypesAsync(bool includeInactive = false);
        Task<ApiResponse<object>> CreateBoardTypeAsync(CreateBoardTypeRequestDto request);
        Task<ApiResponse<object>> UpdateBoardTypeAsync(long id, UpdateBoardTypeRequestDto request);
        Task<ApiResponse<object>> ToggleBoardTypeStatusAsync(long id);

        // SCHOOL TYPE
        Task<ApiResponse<object>> GetSchoolTypesAsync(bool includeInactive = false);
        Task<ApiResponse<object>> CreateSchoolTypeAsync(CreateSchoolTypeRequestDto request);
        Task<ApiResponse<object>> UpdateSchoolTypeAsync(long id, UpdateSchoolTypeRequestDto request);
        Task<ApiResponse<object>> ToggleSchoolTypeStatusAsync(long id);

        // SCHOOL LEVEL
        Task<ApiResponse<object>> GetSchoolLevelsAsync(bool includeInactive = false);
        Task<ApiResponse<object>> CreateSchoolLevelAsync(CreateSchoolLevelRequestDto request);
        Task<ApiResponse<object>> UpdateSchoolLevelAsync(long id, UpdateSchoolLevelRequestDto request);
        Task<ApiResponse<object>> ToggleSchoolLevelStatusAsync(long id);

        // STATUS
        Task<ApiResponse<object>> GetStatusAsync(bool includeInactive = false);
        Task<ApiResponse<object>> CreateStatusAsync(CreateStatusRequestDto request);
        Task<ApiResponse<object>> UpdateStatusAsync(int id, UpdateStatusRequestDto request);
        Task<ApiResponse<object>> ToggleStatusStatusAsync(int id);

        // ROLES
        Task<ApiResponse<object>> GetRolesAsync(bool includeInactive = false);
        Task<ApiResponse<object>> CreateRoleAsync(CreateRoleRequestDto request);
        Task<ApiResponse<object>> UpdateRoleAsync(long id, UpdateRoleRequestDto request);
        Task<ApiResponse<object>> ToggleRoleStatusAsync(long id);
    }
}
