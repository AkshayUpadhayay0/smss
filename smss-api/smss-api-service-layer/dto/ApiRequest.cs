using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace smss_api_service_layer.dto
{
    internal class ApiRequest
    {
    }

    public static class Rx
    {
        public const string Mobile = @"^[6-9][0-9]{9}$";
        public const string Gstin = @"(?i)^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$";
        public const string Pan = @"(?i)^[A-Z]{5}[0-9]{4}[A-Z]$";
        public const string Pincode = @"^[1-9][0-9]{5}$";
    }

    // ---------- Contacts ----------
    public class SchoolContactRequest
    {
        [Required, StringLength(100)] public string ContactType { get; set; } = null!;   // School Owner, Principal, Accountant...
        [Required, StringLength(200)] public string ContactName { get; set; } = null!;
        [StringLength(150)] public string? Designation { get; set; }
        [EmailAddress, StringLength(150)] public string? Email { get; set; }
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")]
        public string? MobileNumber { get; set; }
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")]
        public string? AlternateMobileNumber { get; set; }
        public bool IsPrimary { get; set; }
        public int? StatusId { get; set; }
    }

    public class SchoolContactUpdateRequest : SchoolContactRequest
    {
        public long? ContactId { get; set; }   // present = update, null = add new
    }

    // ---------- School (shared fields) ----------
    public abstract class SchoolBaseRequest : IValidatableObject
    {
        [Required, StringLength(250)] public string SchoolName { get; set; } = null!;
        [StringLength(100)] public string? SchoolShortName { get; set; }

        public long? SchoolTypeId { get; set; }
        public long? SchoolLevelId { get; set; }
        public long? BoardTypeId { get; set; }
        [Range(1800, 32767)] public short? SchoolEstablishYear { get; set; }

        [RegularExpression(Rx.Gstin, ErrorMessage = "Invalid GSTIN.")] public string? SchoolGstin { get; set; }
        [RegularExpression(Rx.Pan, ErrorMessage = "Invalid PAN.")] public string? SchoolPan { get; set; }

        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public int? DistrictId { get; set; }
        public int? CityId { get; set; }

        [StringLength(250)] public string? AddressLine1 { get; set; }
        [StringLength(250)] public string? AddressLine2 { get; set; }
        [RegularExpression(Rx.Pincode, ErrorMessage = "Invalid pincode.")] public string? Pincode { get; set; }

        [EmailAddress, StringLength(150)] public string? Email { get; set; }
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")]
        public string? MobileNumber { get; set; }
        [Url, StringLength(200)] public string? Website { get; set; }
        public string? LogoUrl { get; set; }

        public long? SubscriptionPlanId { get; set; }
        public DateOnly? SubscriptionStartDate { get; set; }
        public DateOnly? SubscriptionEndDate { get; set; }
        public int? SubscriptionStatusId { get; set; }
        public int? SchoolStatusId { get; set; }

        public virtual IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (SchoolEstablishYear > DateTime.UtcNow.Year)
                yield return new ValidationResult("Establish year cannot be in the future.", new[] { nameof(SchoolEstablishYear) });

            if (SubscriptionStartDate.HasValue && SubscriptionEndDate.HasValue
                && SubscriptionEndDate < SubscriptionStartDate)
                yield return new ValidationResult("Subscription end date cannot be before start date.", new[] { nameof(SubscriptionEndDate) });

            // Geography: the DB uses composite foreign keys, so parents are required for children
            if (StateId.HasValue && !CountryId.HasValue)
                yield return new ValidationResult("CountryId is required when StateId is given.", new[] { nameof(CountryId) });
            if (DistrictId.HasValue && !StateId.HasValue)
                yield return new ValidationResult("StateId is required when DistrictId is given.", new[] { nameof(StateId) });
            if (CityId.HasValue && !DistrictId.HasValue)
                yield return new ValidationResult("DistrictId is required when CityId is given.", new[] { nameof(DistrictId) });
        }

        protected static IEnumerable<ValidationResult> ValidateContacts(IEnumerable<SchoolContactRequest>? contacts, string field)
        {
            var list = contacts?.ToList();
            if (list == null || list.Count == 0) yield break;
            if (list.Count(c => c.IsPrimary) != 1)
                yield return new ValidationResult("Exactly one contact must be marked as primary.", new[] { field });
        }
    }

    // ---------- Create / Update ----------
    public class CreateSchoolRequest : SchoolBaseRequest
    {
        [Required, StringLength(100)] public string SchoolCode { get; set; } = null!;
        public List<SchoolContactRequest> Contacts { get; set; } = new();

        public override IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            foreach (var r in base.Validate(context)) yield return r;
            foreach (var r in ValidateContacts(Contacts, nameof(Contacts))) yield return r;
        }
    }

    public class UpdateSchoolRequest : SchoolBaseRequest
    {
        public List<SchoolContactUpdateRequest> Contacts { get; set; } = new();

        public override IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            foreach (var r in base.Validate(context)) yield return r;
            foreach (var r in ValidateContacts(Contacts, nameof(Contacts))) yield return r;
        }
    }
}
