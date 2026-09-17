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
        public MasterDataService(IMasterDataRepository masterDataRepository) {
            _masterDataRepository = masterDataRepository;
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



        // ========================================================= // GET COUNTRIES // =========================================================
        public async Task<ApiResponse<object>> GetStatusAsync()
        {
            try
            {
                List<LutStatus> status = await _masterDataRepository.GetStatusAsync();
                if (status == null || !status.Any())
                {
                    return new ApiResponse<object> { Status = false, StatusCode = 404, Message = "No status found", Data = null };
                }
                return new ApiResponse<object> { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = status };
            }
            catch (Exception ex)
            {
                return new ApiResponse<object> { Status = false, StatusCode = 500, Message = ex.Message, Data = null };
            }
        }


        // =========================================================
        // GET ACTIVE BOARD TYPES
        // =========================================================

        public async Task<ApiResponse<object>> GetBoardTypesAsync()
        {
            try
            {
                List<LutBoardType> boardTypes =
                    await _masterDataRepository.GetBoardTypesAsync();

                if (boardTypes == null || !boardTypes.Any())
                {
                    return new ApiResponse<object>
                    {
                        Status = false,
                        StatusCode = 404,
                        Message = "No board types found",
                        Data = null
                    };
                }

                return new ApiResponse<object>
                {
                    Status = true,
                    StatusCode = 200,
                    Message = "Successfully fetched",
                    Data = boardTypes
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<object>
                {
                    Status = false,
                    StatusCode = 500,
                    Message = ex.Message,
                    Data = null
                };
            }
        }


        // =========================================================
        // GET ACTIVE SCHOOL TYPES
        // =========================================================

        public async Task<ApiResponse<object>> GetSchoolTypesAsync()
        {
            try
            {
                List<LutSchoolType> schoolTypes =
                    await _masterDataRepository.GetSchoolTypesAsync();

                if (schoolTypes == null || !schoolTypes.Any())
                {
                    return new ApiResponse<object>
                    {
                        Status = false,
                        StatusCode = 404,
                        Message = "No school types found",
                        Data = null
                    };
                }

                return new ApiResponse<object>
                {
                    Status = true,
                    StatusCode = 200,
                    Message = "Successfully fetched",
                    Data = schoolTypes
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<object>
                {
                    Status = false,
                    StatusCode = 500,
                    Message = ex.Message,
                    Data = null
                };
            }
        }


        // =========================================================
        // GET ACTIVE SCHOOL LEVELS
        // =========================================================

        public async Task<ApiResponse<object>> GetSchoolLevelsAsync()
        {
            try
            {
                List<LutSchoolLevel> schoolLevels =
                    await _masterDataRepository.GetSchoolLevelsAsync();

                if (schoolLevels == null || !schoolLevels.Any())
                {
                    return new ApiResponse<object>
                    {
                        Status = false,
                        StatusCode = 404,
                        Message = "No school levels found",
                        Data = null
                    };
                }

                return new ApiResponse<object>
                {
                    Status = true,
                    StatusCode = 200,
                    Message = "Successfully fetched",
                    Data = schoolLevels
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<object>
                {
                    Status = false,
                    StatusCode = 500,
                    Message = ex.Message,
                    Data = null
                };
            }
        }


        // =========================================================
        // GET ACTIVE SCHOOL LEVELS
        // =========================================================

        public async Task<ApiResponse<object>> GetRolesAsync()
        {
            try
            {
                List<LutRole> schoolLevels =
                    await _masterDataRepository.GetRolesAsync();

                if (schoolLevels == null || !schoolLevels.Any())
                {
                    return new ApiResponse<object>
                    {
                        Status = false,
                        StatusCode = 404,
                        Message = "No school levels found",
                        Data = null
                    };
                }

                return new ApiResponse<object>
                {
                    Status = true,
                    StatusCode = 200,
                    Message = "Successfully fetched",
                    Data = schoolLevels
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<object>
                {
                    Status = false,
                    StatusCode = 500,
                    Message = ex.Message,
                    Data = null
                };
            }
        }

    }
}
