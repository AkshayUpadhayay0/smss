using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System;
using System.Collections.Generic;
using System.Text;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace smss_api_service_layer.service
{
    public class SystemSetupService : ISystemSetupService
    {
        private readonly ISystemSetupRepository _repo;
        private readonly ILogger<SystemSetupService> _logger;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;

        public SystemSetupService(ISystemSetupRepository repo, ILogger<SystemSetupService> logger, IEmailService emailService, IConfiguration config)
        {
            _repo = repo;
            _logger = logger;
            _emailService = emailService;
            _config = config;
        }

        public async Task<ApiResponse<object>> BootstrapSuperAdminAsync(CreateSuperAdminRequest req)
        {
            try
            {
                if (await _repo.SuperAdminExistsAsync())
                    return Fail(409, "A Super Admin already exists. This endpoint only creates the first one.");

                var username = req.Username.Trim();
                var email = req.Email.Trim();

                if (await _repo.UsernameOrEmailExistsAsync(username, email))
                    return Fail(409, "Username or email is already in use.");

                var roleId = await _repo.GetRoleIdByNameAsync(RoleNames.SuperAdmin);
                if (roleId == null)
                    return ConfigError($"Role '{RoleNames.SuperAdmin}' is missing in lut_roles");

                var activeStatusId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeStatusId == null)
                    return ConfigError($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var orgUserId = $"SUAD{await _repo.GetNextSuperAdminNumberAsync():D3}";
                var now = DateTime.UtcNow;

                var user = new TbUsers
                {
                    SchoolId = null,                      // Super Admin belongs to no tenant
                    UserType = UserTypes.SuperAdmin,       // "SuperAdminId"
                    OrgUserId = orgUserId,                 // "SUAD001"
                    Username = username,
                    Email = email,
                    MobileNumber = req.MobileNumber,
                    PasswordHash = PasswordHelper.Hash(req.Password),
                    IsFirstLogin = false,                  // this one sets its own password directly
                    StatusId = activeStatusId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UserRoles = { new TbUserRoles { RoleId = roleId.Value, CreatedAt = now } }
                };

                await _repo.AddSuperAdminAsync(user);

                try
                {
                    var (subject, html) = EmailTemplates.SuperAdminWelcome(user.Username!, user.OrgUserId!, _config["Email:LoginUrl"]);
                    await _emailService.SendAsync(user.Email!, subject, html);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send welcome email to Super Admin {Email}", user.Email);
                }

                return Ok(201, "Super Admin created successfully", new SuperAdminResponse
                {
                    UserId = user.UserId,
                    OrgUserId = user.OrgUserId!,
                    Username = user.Username!,
                    Email = user.Email!,
                    MobileNumber = user.MobileNumber,
                    CreatedAt = user.CreatedAt
                });
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Fail(409, "Username, email or org user id is already in use.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while bootstrapping Super Admin");
                return Fail(500, "Something went wrong. Please try again later.");
            }
        }

        private static ApiResponse<object> Ok(int code, string message, object data) =>
            new() { Status = true, StatusCode = code, Message = message, Data = data };

        private static ApiResponse<object> Fail(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message, Data = null };

        private ApiResponse<object> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Fail(500, "The system is not configured correctly. Please contact support.");
        }
    }
}
