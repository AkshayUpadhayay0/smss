using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.@interface
{
    public interface IMasterDataRepository
    {
        // Countries
        Task<List<LutCountry>> GetCountriesAsync(); 
        
        // States by Country
        Task<List<LutState>> GetStatesAsync(int countryId); 
        
        // Districts by Country + State
        Task<List<LutDistrict>> GetDistrictsAsync( int countryId, int stateId); 
        
        // Cities by Country + State + District
        Task<List<LutCity>> GetCitiesAsync( int countryId, int stateId, int districtId);

        // Countries
        Task<List<LutStatus>> GetStatusAsync();

        // BOARD TYPE
        Task<List<LutBoardType>> GetBoardTypesAsync();

        // SCHOOL TYPE
        Task<List<LutSchoolType>> GetSchoolTypesAsync();

        // SCHOOL LEVEL
        Task<List<LutSchoolLevel>> GetSchoolLevelsAsync();

        // Roles
        Task<List<LutRole>> GetRolesAsync();
    }
}
