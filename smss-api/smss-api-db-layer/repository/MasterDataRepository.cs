using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        // ========================================================= // GET Status // =========================================================
        public async Task<List<LutStatus>> GetStatusAsync()
        {
            return await _dbContext.LutStatus.AsNoTracking().OrderBy(x => x.Sname).ToListAsync();
        }

        // =========================================================
        // GET ACTIVE BOARD TYPES
        // =========================================================

        public async Task<List<LutBoardType>> GetBoardTypesAsync()
        {
            return await _dbContext.LutBoardTypes
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.BoardName)
                .ToListAsync();
        }


        // =========================================================
        // GET ACTIVE SCHOOL TYPES
        // =========================================================

        public async Task<List<LutSchoolType>> GetSchoolTypesAsync()
        {
            return await _dbContext.LutSchoolTypes
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SchoolTypeName)
                .ToListAsync();
        }


        // =========================================================
        // GET ACTIVE SCHOOL LEVELS
        // =========================================================

        public async Task<List<LutSchoolLevel>> GetSchoolLevelsAsync()
        {
            return await _dbContext.LutSchoolLevels
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SchoolLevelName)
                .ToListAsync();
        }



        // =========================================================
        // GET ACTIVE Roles
        // =========================================================

        public async Task<List<LutRole>> GetRolesAsync()
        {
            return await _dbContext.LutRoles
                .AsNoTracking()
                .OrderBy(x => x.RoleName)
                .ToListAsync();
        }
    }
}
