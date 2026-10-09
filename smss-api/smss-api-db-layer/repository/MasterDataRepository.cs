using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using smss_api_db_layer.constants;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace smss_api_db_layer.repository
{
    public class MasterDataRepository : IMasterDataRepository
    {
        private const string PgUniqueViolation = "23505";
        private readonly dbContext _dbContext;
        public MasterDataRepository(dbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // ========================================================= // GET COUNTRIES // =========================================================
        public async Task<List<LutCountry>> GetCountriesAsync() 
        { 
            return await _dbContext.LutCountries .AsNoTracking() .OrderBy(x => x.Cname) .ToListAsync(); 
        } 
        
        // ========================================================= // GET STATES BY COUNTRY // =========================================================
        public async Task<List<LutState>> GetStatesAsync( int countryId) 
        { 
            return await _dbContext.LutStates .AsNoTracking() .Where(x => x.Cid == countryId) .OrderBy(x => x.Sname) .ToListAsync(); 
        } 
        
        // ========================================================= // GET DISTRICTS BY COUNTRY + STATE // =========================================================
        public async Task<List<LutDistrict>> GetDistrictsAsync( int countryId, int stateId) 
        { 
            return await _dbContext.LutDistricts .AsNoTracking() .Where(x => x.Cid == countryId && x.Sid == stateId) .OrderBy(x => x.Dname) .ToListAsync(); 
        } 
        
        // ========================================================= // GET CITIES BY COUNTRY + STATE + DISTRICT // =========================================================
        public async Task<List<LutCity>> GetCitiesAsync( int countryId, int stateId, int districtId) 
        { 
            return await _dbContext.LutCities .AsNoTracking() .Where(x => x.Cid == countryId && x.Sid == stateId && x.Did == districtId) .OrderBy(x => x.CityName) .ToListAsync(); 
        }

        // =========================================================
        // GET ACTIVE BOARD TYPES
        // =========================================================

        public async Task<List<LutBoardType>> GetBoardTypesAsync(bool includeInactive = false)
        {
            IQueryable<LutBoardType> query = _dbContext.LutBoardTypes.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.BoardName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutBoardType?> GetBoardTypeByIdAsync(long id)
        {
            return await _dbContext.LutBoardTypes.FirstOrDefaultAsync(x => x.BoardTypeId == id);
        }

        public async Task<bool> BoardTypeExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutBoardTypes.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.BoardTypeId != excludeId) &&
                (x.BoardCode.ToUpper() == upperCode || x.BoardName.ToLower() == lowerName));
        }

        public async Task<bool> AddBoardTypeAsync(LutBoardType entity)
        {
            try
            {
                _dbContext.LutBoardTypes.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateBoardTypeAsync(LutBoardType entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }


        // =========================================================
        // GET ACTIVE SCHOOL TYPES
        // =========================================================

        public async Task<List<LutSchoolType>> GetSchoolTypesAsync(bool includeInactive = false)
        {
            IQueryable<LutSchoolType> query = _dbContext.LutSchoolTypes.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.SchoolTypeName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutSchoolType?> GetSchoolTypeByIdAsync(long id)
        {
            return await _dbContext.LutSchoolTypes.FirstOrDefaultAsync(x => x.SchoolTypeId == id);
        }

        public async Task<bool> SchoolTypeExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutSchoolTypes.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.SchoolTypeId != excludeId) &&
                (x.SchoolTypeCode.ToUpper() == upperCode || x.SchoolTypeName.ToLower() == lowerName));
        }

        public async Task<bool> AddSchoolTypeAsync(LutSchoolType entity)
        {
            try
            {
                _dbContext.LutSchoolTypes.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateSchoolTypeAsync(LutSchoolType entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }


        // =========================================================
        // GET ACTIVE SCHOOL LEVELS
        // =========================================================

        public async Task<List<LutSchoolLevel>> GetSchoolLevelsAsync(bool includeInactive = false)
        {
            IQueryable<LutSchoolLevel> query = _dbContext.LutSchoolLevels.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.SchoolLevelName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutSchoolLevel?> GetSchoolLevelByIdAsync(long id)
        {
            return await _dbContext.LutSchoolLevels.FirstOrDefaultAsync(x => x.SchoolLevelId == id);
        }

        public async Task<bool> SchoolLevelExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutSchoolLevels.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.SchoolLevelId != excludeId) &&
                (x.SchoolLevelCode.ToUpper() == upperCode || x.SchoolLevelName.ToLower() == lowerName));
        }

        public async Task<bool> AddSchoolLevelAsync(LutSchoolLevel entity)
        {
            try
            {
                _dbContext.LutSchoolLevels.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateSchoolLevelAsync(LutSchoolLevel entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }

        // ========================================================= 
        // GET Status 
        // =========================================================
        public async Task<List<LutStatus>> GetStatusAsync(bool includeInactive = false)
        {
            IQueryable<LutStatus> query = _dbContext.LutStatus.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.Sname).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutStatus?> GetStatusByIdAsync(int id)
        {
            return await _dbContext.LutStatus.FirstOrDefaultAsync(x => x.Sid == id);
        }

        // Names must be unique within a status type.
        public async Task<bool> StatusExistsAsync(string name, string type, int? excludeId = null)
        {
            string lowerName = name.ToLower();
            string lowerType = type.ToLower();

            return await _dbContext.LutStatus.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.Sid != excludeId) &&
                x.Sname.ToLower() == lowerName && x.Stype.ToLower() == lowerType);
        }

        public async Task<bool> AddStatusAsync(LutStatus entity)
        {
            try
            {
                _dbContext.LutStatus.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateStatusAsync(LutStatus entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }


        // =========================================================
        // GET Roles
        // =========================================================

        public async Task<List<LutRole>> GetRolesAsync(bool includeInactive = false)
        {
            IQueryable<LutRole> query = _dbContext.LutRoles.AsNoTracking();

            if (!includeInactive)
            {
                string inactiveName = StatusLookup.Inactive.ToLower();
                string generalType = StatusLookup.GeneralType.ToLower();

                // Hide roles whose status is the "Inactive" general status. NULL status counts as active.
                query = query.Where(r => r.StatusId == null || !_dbContext.LutStatus.Any(s =>
                    s.Sid == r.StatusId &&
                    s.Sname.ToLower() == inactiveName &&
                    s.Stype.ToLower() == generalType));
            }

            return await query.OrderBy(x => x.RoleName).ToListAsync();
        }

        public async Task<int?> GetStatusIdByNameAsync(string name, string type)
        {
            string lowerName = name.Trim().ToLower();
            string lowerType = type.Trim().ToLower();

            return await _dbContext.LutStatus.AsNoTracking()
                .Where(s => s.Sname.ToLower() == lowerName && s.Stype.ToLower() == lowerType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutRole?> GetRoleByIdAsync(long id)
        {
            return await _dbContext.LutRoles.FirstOrDefaultAsync(x => x.RoleId == id);
        }

        public async Task<bool> RoleExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutRoles.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.RoleId != excludeId) &&
                (x.RoleCode.ToUpper() == upperCode || x.RoleName.ToLower() == lowerName));
        }

        public async Task<bool> AddRoleAsync(LutRole entity)
        {
            try
            {
                _dbContext.LutRoles.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateRoleAsync(LutRole entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }

        // Raw SQL because tb_user_roles needs no entity here. Requires EF Core 7+.
        // If your DbContext already has a DbSet for tb_user_roles, use CountAsync on it instead.
        public async Task<int> CountRoleAssignmentsAsync(long roleId)
        {
            return await _dbContext.Database
                .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM public.tb_user_roles WHERE role_id = {roleId}")
                .SingleAsync();
        }


        // =========================================================
        // GET ACTIVE RELIGION CATEGORYS
        // =========================================================

        public async Task<List<LutReligionCategory>> GetReligionCategoriesAsync(bool includeInactive = false)
        {
            IQueryable<LutReligionCategory> query = _dbContext.LutReligionCategories.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.ReligionName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutReligionCategory?> GetReligionCategoryByIdAsync(long id)
        {
            return await _dbContext.LutReligionCategories.FirstOrDefaultAsync(x => x.ReligionCategoryId == id);
        }

        public async Task<bool> ReligionCategoryExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutReligionCategories.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.ReligionCategoryId != excludeId) &&
                (x.ReligionCode.ToUpper() == upperCode || x.ReligionName.ToLower() == lowerName));
        }

        public async Task<bool> AddReligionCategoryAsync(LutReligionCategory entity)
        {
            try
            {
                _dbContext.LutReligionCategories.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateReligionCategoryAsync(LutReligionCategory entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }

        // =========================================================
        // GET ACTIVE BLOOD GROUPS
        // =========================================================

        public async Task<List<LutBloodGroup>> GetBloodGroupsAsync(bool includeInactive = false)
        {
            IQueryable<LutBloodGroup> query = _dbContext.LutBloodGroups.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.BloodGroupName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutBloodGroup?> GetBloodGroupByIdAsync(long id)
        {
            return await _dbContext.LutBloodGroups.FirstOrDefaultAsync(x => x.BloodGroupId == id);
        }

        public async Task<bool> BloodGroupExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutBloodGroups.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.BloodGroupId != excludeId) &&
                (x.BloodGroupCode.ToUpper() == upperCode || x.BloodGroupName.ToLower() == lowerName));
        }

        public async Task<bool> AddBloodGroupAsync(LutBloodGroup entity)
        {
            try
            {
                _dbContext.LutBloodGroups.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateBloodGroupAsync(LutBloodGroup entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }

        // =========================================================
        // GET ACTIVE GENDERS
        // =========================================================

        public async Task<List<LutGender>> GetGendersAsync(bool includeInactive = false)
        {
            IQueryable<LutGender> query = _dbContext.LutGenders.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.GenderName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutGender?> GetGenderByIdAsync(long id)
        {
            return await _dbContext.LutGenders.FirstOrDefaultAsync(x => x.GenderId == id);
        }

        public async Task<bool> GenderExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutGenders.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.GenderId != excludeId) &&
                (x.GenderCode.ToUpper() == upperCode || x.GenderName.ToLower() == lowerName));
        }

        public async Task<bool> AddGenderAsync(LutGender entity)
        {
            try
            {
                _dbContext.LutGenders.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateGenderAsync(LutGender entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }

        // =========================================================
        // GET ACTIVE DOCUMENT TYPES
        // =========================================================

        public async Task<List<LutDocumentType>> GetDocumentTypesAsync(bool includeInactive = false)
        {
            IQueryable<LutDocumentType> query = _dbContext.LutDocumentTypes.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.DocumentTypeName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutDocumentType?> GetDocumentTypeByIdAsync(long id)
        {
            return await _dbContext.LutDocumentTypes.FirstOrDefaultAsync(x => x.DocumentTypeId == id);
        }

        public async Task<bool> DocumentTypeExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutDocumentTypes.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.DocumentTypeId != excludeId) &&
                (x.DocumentTypeCode.ToUpper() == upperCode || x.DocumentTypeName.ToLower() == lowerName));
        }

        public async Task<bool> AddDocumentTypeAsync(LutDocumentType entity)
        {
            try
            {
                _dbContext.LutDocumentTypes.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateDocumentTypeAsync(LutDocumentType entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }

        // =========================================================
        // GET ACTIVE STUDENT CATEGORYS
        // =========================================================

        public async Task<List<LutStudentCategory>> GetStudentCategoriesAsync(bool includeInactive = false)
        {
            IQueryable<LutStudentCategory> query = _dbContext.LutStudentCategories.AsNoTracking();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            return await query.OrderBy(x => x.CategoryName).ToListAsync();
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<LutStudentCategory?> GetStudentCategoryByIdAsync(long id)
        {
            return await _dbContext.LutStudentCategories.FirstOrDefaultAsync(x => x.StudentCategoryId == id);
        }

        public async Task<bool> StudentCategoryExistsAsync(string code, string name, long? excludeId = null)
        {
            string upperCode = code.ToUpper();
            string lowerName = name.ToLower();

            return await _dbContext.LutStudentCategories.AsNoTracking().AnyAsync(x =>
                (excludeId == null || x.StudentCategoryId != excludeId) &&
                (x.CategoryCode.ToUpper() == upperCode || x.CategoryName.ToLower() == lowerName));
        }

        public async Task<bool> AddStudentCategoryAsync(LutStudentCategory entity)
        {
            try
            {
                _dbContext.LutStudentCategories.Add(entity);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<bool> UpdateStudentCategoryAsync(LutStudentCategory entity)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                return false;
            }
        }
    }
}
