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

        //Status
        Task<ApiResponse<object>> GetStatusAsync();

        // BOARD TYPE
        Task<ApiResponse<object>> GetBoardTypesAsync(); 
        
        // SCHOOL TYPE
        Task<ApiResponse<object>> GetSchoolTypesAsync();
        
        // SCHOOL LEVEL
        Task<ApiResponse<object>> GetSchoolLevelsAsync();

        //Roles
        Task<ApiResponse<object>> GetRolesAsync();
    }
}
