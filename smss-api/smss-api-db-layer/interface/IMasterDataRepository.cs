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

        // RELIGION CATEGORYS
        Task<List<LutReligionCategory>> GetReligionCategoriesAsync(bool includeInactive = false);
        Task<LutReligionCategory?> GetReligionCategoryByIdAsync(long id);
        Task<bool> ReligionCategoryExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddReligionCategoryAsync(LutReligionCategory entity);      // false = unique constraint violation
        Task<bool> UpdateReligionCategoryAsync(LutReligionCategory entity);   // false = unique constraint violation

        // BLOOD GROUPS
        Task<List<LutBloodGroup>> GetBloodGroupsAsync(bool includeInactive = false);
        Task<LutBloodGroup?> GetBloodGroupByIdAsync(long id);
        Task<bool> BloodGroupExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddBloodGroupAsync(LutBloodGroup entity);      // false = unique constraint violation
        Task<bool> UpdateBloodGroupAsync(LutBloodGroup entity);   // false = unique constraint violation

        // GENDERS
        Task<List<LutGender>> GetGendersAsync(bool includeInactive = false);
        Task<LutGender?> GetGenderByIdAsync(long id);
        Task<bool> GenderExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddGenderAsync(LutGender entity);      // false = unique constraint violation
        Task<bool> UpdateGenderAsync(LutGender entity);   // false = unique constraint violation

        // DOCUMENT TYPES
        Task<List<LutDocumentType>> GetDocumentTypesAsync(bool includeInactive = false);
        Task<LutDocumentType?> GetDocumentTypeByIdAsync(long id);
        Task<bool> DocumentTypeExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddDocumentTypeAsync(LutDocumentType entity);      // false = unique constraint violation
        Task<bool> UpdateDocumentTypeAsync(LutDocumentType entity);   // false = unique constraint violation

        // STUDENT CATEGORYS
        Task<List<LutStudentCategory>> GetStudentCategoriesAsync(bool includeInactive = false);
        Task<LutStudentCategory?> GetStudentCategoryByIdAsync(long id);
        Task<bool> StudentCategoryExistsAsync(string code, string name, long? excludeId = null);
        Task<bool> AddStudentCategoryAsync(LutStudentCategory entity);      // false = unique constraint violation
        Task<bool> UpdateStudentCategoryAsync(LutStudentCategory entity);   // false = unique constraint violation
    }
}
