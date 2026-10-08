using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.dto
{
    public class ApiResponse<T>
    {
        public bool Status { get; set; }

        public int StatusCode { get; set; }

        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }


    public class SchoolContactResponse
    {
        public long ContactId { get; set; }
        public string ContactType { get; set; } = null!;
        public string ContactName { get; set; } = null!;
        public string? Designation { get; set; }
        public string? Email { get; set; }
        public string? MobileNumber { get; set; }
        public string? AlternateMobileNumber { get; set; }
        public bool IsPrimary { get; set; }
        public int? StatusId { get; set; }
    }

    public class SchoolResponse
    {
        public string SchoolId { get; set; } = null!;
        public string SchoolCode { get; set; } = null!;
        public string SchoolName { get; set; } = null!;
        public string? SchoolShortName { get; set; }
        public long? SchoolTypeId { get; set; }
        public long? SchoolLevelId { get; set; }
        public long? BoardTypeId { get; set; }
        public short? SchoolEstablishYear { get; set; }
        public string? SchoolGstin { get; set; }
        public string? SchoolPan { get; set; }
        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public int? DistrictId { get; set; }
        public int? CityId { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Pincode { get; set; }
        public string? Email { get; set; }
        public string? MobileNumber { get; set; }
        public string? Website { get; set; }
        public string? LogoUrl { get; set; }
        public long? SubscriptionPlanId { get; set; }
        public DateOnly? SubscriptionStartDate { get; set; }
        public DateOnly? SubscriptionEndDate { get; set; }
        public int? SubscriptionStatusId { get; set; }
        public int? SchoolStatusId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<SchoolContactResponse> Contacts { get; set; } = new();
    }

    // Returned once, right after registration
    public class SchoolRegistrationResponse
    {
        public SchoolResponse School { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string TemporaryPassword { get; set; } = null!;
        public bool EmailSent { get; set; }
        public string? EmailSentTo { get; set; }
    }
}
