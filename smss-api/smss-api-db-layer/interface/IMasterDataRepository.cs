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

        // BOARD TYPE
        Task<List<LutBoardType>> GetBoardTypesAsync(bool includeInactive = false);
        Task<LutBoardType?> GetBoardTypeByIdAsync(long id);
        Task<bool> BoardTypeExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddBoardTypeAsync(LutBoardType entity);      // false = unique constraint violation
        Task<bool> UpdateBoardTypeAsync(LutBoardType entity);   // false = unique constraint violation

        // SCHOOL TYPE
        Task<List<LutSchoolType>> GetSchoolTypesAsync(bool includeInactive = false);
        Task<LutSchoolType?> GetSchoolTypeByIdAsync(long id);
        Task<bool> SchoolTypeExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddSchoolTypeAsync(LutSchoolType entity);      // false = unique constraint violation
        Task<bool> UpdateSchoolTypeAsync(LutSchoolType entity);   // false = unique constraint violation

        // SCHOOL LEVEL
        Task<List<LutSchoolLevel>> GetSchoolLevelsAsync(bool includeInactive = false);
        Task<LutSchoolLevel?> GetSchoolLevelByIdAsync(long id);
        Task<bool> SchoolLevelExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddSchoolLevelAsync(LutSchoolLevel entity);      // false = unique constraint violation
        Task<bool> UpdateSchoolLevelAsync(LutSchoolLevel entity);   // false = unique constraint violation

        // STATUS
        Task<List<LutStatus>> GetStatusAsync(bool includeInactive = false);
        Task<LutStatus?> GetStatusByIdAsync(int id);
        Task<bool> StatusExistsAsync(string name, string type, int? excludeId = null);
        Task<bool> AddStatusAsync(LutStatus entity);      // false = unique constraint violation
        Task<bool> UpdateStatusAsync(LutStatus entity);   // false = unique constraint violation

        // ROLES
        Task<List<LutRole>> GetRolesAsync(bool includeInactive = false);
        Task<int?> GetStatusIdByNameAsync(string name, string type);
        Task<LutRole?> GetRoleByIdAsync(long id);
        Task<bool> RoleExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddRoleAsync(LutRole entity);       // false = unique constraint violation
        Task<bool> UpdateRoleAsync(LutRole entity);    // false = unique constraint violation
        Task<int> CountRoleAssignmentsAsync(long roleId);
    }
}
