using smss_api_db_layer.entity;
using smss_api_service_layer.dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    public static class SchoolMapper
    {
        public static SchoolResponse ToResponse(TbSchools s) => new()
        {
            SchoolId = s.SchoolId,
            SchoolCode = s.SchoolCode,
            SchoolName = s.SchoolName,
            SchoolShortName = s.SchoolShortName,
            SchoolTypeId = s.SchoolTypeId,
            SchoolLevelId = s.SchoolLevelId,
            BoardTypeId = s.BoardTypeId,
            SchoolEstablishYear = s.SchoolEstablishYear,
            SchoolGstin = s.SchoolGstin,
            SchoolPan = s.SchoolPan,
            CountryId = s.CountryId,
            StateId = s.StateId,
            DistrictId = s.DistrictId,
            CityId = s.CityId,
            AddressLine1 = s.AddressLine1,
            AddressLine2 = s.AddressLine2,
            Pincode = s.Pincode,
            Email = s.Email,
            MobileNumber = s.MobileNumber,
            Website = s.Website,
            LogoUrl = s.LogoUrl,
            SubscriptionPlanId = s.SubscriptionPlanId,
            SubscriptionStartDate = s.SubscriptionStartDate,
            SubscriptionEndDate = s.SubscriptionEndDate,
            SubscriptionStatusId = s.SubscriptionStatusId,
            SchoolStatusId = s.SchoolStatusId,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            Contacts = s.Contacts.Select(c => new SchoolContactResponse
            {
                ContactId = c.ContactId,
                ContactType = c.ContactType,
                ContactName = c.ContactName,
                Designation = c.Designation,
                Email = c.Email,
                MobileNumber = c.MobileNumber,
                AlternateMobileNumber = c.AlternateMobileNumber,
                IsPrimary = c.IsPrimary,
                StatusId = c.StatusId
            }).ToList()
        };
    }
}
