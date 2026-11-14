using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // The school, ids, status and photo are never accepted from the client; blank optional text becomes null in the service.

    // ---------------------------------------------------------------- areas
    public class TransportAreaRequest
    {
        [Required(ErrorMessage = "Area name is required."), MaxLength(150)] public string? AreaName { get; set; }
        [Range(typeof(decimal), "0", "9999.99", ErrorMessage = "Distance must be between 0 and 9999.99 km.")] public decimal? DistanceKm { get; set; }
        [Required(ErrorMessage = "Transport fee is required."), Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Transport fee must be 0 or more.")] public decimal? TransportFee { get; set; }
        [MaxLength(500)] public string? Remarks { get; set; }
    }

    public class TransportAreaDto
    {
        public long AreaId { get; set; }
        public string AreaName { get; set; } = null!;
        public decimal? DistanceKm { get; set; }
        public decimal TransportFee { get; set; }
        public string? Remarks { get; set; }
        public string StatusName { get; set; } = null!;
        /// <summary>How many Active drivers have this area as a pickup area.</summary>
        public int ActiveDriverCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class TransportAreaPagedDto
    {
        public List<TransportAreaDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // ---------------------------------------------------------------- vehicles
    public class VehicleRequest
    {
        [Required(ErrorMessage = "Vehicle number / label is required."), MaxLength(50)] public string? VehicleLabel { get; set; }
        [Required(ErrorMessage = "Registration number is required."), MaxLength(30)] public string? RegistrationNumber { get; set; }
        [Required(ErrorMessage = "Vehicle type is required.")] public long? VehicleTypeId { get; set; }
        [Required(ErrorMessage = "Ownership is required.")] public long? OwnershipId { get; set; }
        public short? ModelYear { get; set; }
        [Range(1, 200, ErrorMessage = "Seating capacity must be between 1 and 200.")] public short? SeatingCapacity { get; set; }
        /// <summary>Only for Rented vehicles.</summary>
        [MaxLength(150)] public string? OwnerName { get; set; }
        [MaxLength(20)] public string? OwnerContact { get; set; }
        public DateOnly? LastServiceDate { get; set; }
        public DateOnly? PucExpiryDate { get; set; }
        public DateOnly? InsuranceExpiryDate { get; set; }
    }

    public class VehicleDto
    {
        public long VehicleId { get; set; }
        public string VehicleLabel { get; set; } = null!;
        public string RegistrationNumber { get; set; } = null!;
        public long VehicleTypeId { get; set; }
        public string VehicleTypeName { get; set; } = null!;
        public long OwnershipId { get; set; }
        public string OwnershipName { get; set; } = null!;
        public bool IsRented { get; set; }
        public short? ModelYear { get; set; }
        public short? SeatingCapacity { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerContact { get; set; }
        public DateOnly? LastServiceDate { get; set; }
        public DateOnly? PucExpiryDate { get; set; }
        /// <summary>EXPIRED, EXPIRING (within 30 days), OK or NOT_SET. Computed at read time, never stored.</summary>
        public string PucStatus { get; set; } = null!;
        public int? PucDaysLeft { get; set; }
        public DateOnly? InsuranceExpiryDate { get; set; }
        public string InsuranceStatus { get; set; } = null!;
        public int? InsuranceDaysLeft { get; set; }
        public string StatusName { get; set; } = null!;
        public int ActiveDriverCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class VehiclePagedDto
    {
        public List<VehicleDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class VehicleLookupsDto
    {
        public List<LookupItemDto> VehicleTypes { get; set; } = new();
        /// <summary>IsRented tells the form whether to show the owner / vendor fields.</summary>
        public List<VehicleOwnershipDto> Ownerships { get; set; } = new();
    }

    public class VehicleOwnershipDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsRented { get; set; }
    }

    // ---------------------------------------------------------------- drivers
    public class DriverRequest
    {
        [Required(ErrorMessage = "Full name is required."), MaxLength(150)] public string? FullName { get; set; }
        [Required(ErrorMessage = "Mobile number is required."), RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")] public string? MobileNumber { get; set; }
        [EmailAddress, MaxLength(150)] public string? Email { get; set; }
        [Required(ErrorMessage = "Address is required."), MaxLength(500)] public string? Address { get; set; }
        [Required(ErrorMessage = "Driving licence number is required."), MaxLength(40)] public string? DlNumber { get; set; }
        [Required(ErrorMessage = "Licence valid-upto date is required.")] public DateOnly? DlValidUpto { get; set; }
        /// <summary>Optional. A driver drives at most one vehicle; a vehicle may have several drivers.</summary>
        public long? VehicleId { get; set; }
        /// <summary>The driver's areas, each with optional approximate times. The list REPLACES the previous set on every save: an area not listed is removed,
        /// a listed area takes the times sent (null or blank clears a time); null or empty clears them all.</summary>
        [MaxLength(100)] public List<DriverAreaRequest>? Areas { get; set; }
        /// <summary>The OLD shape. It is no longer accepted: any request that carries it is refused with a 400 (never silently ignored, which would clear the areas).</summary>
        public List<long>? AreaIds { get; set; }
    }

    public class DriverAreaRequest
    {
        public long? AreaId { get; set; }
        /// <summary>Approximate time the bus reaches this area in the morning, "HH:mm" (school local time). Optional.</summary>
        public string? MorningPickupTime { get; set; }
        /// <summary>Approximate time the bus reaches this area in the afternoon, "HH:mm". Optional; must be later than the pickup time when both are set.</summary>
        public string? AfternoonDropTime { get; set; }
    }

    public class DriverAreaDto
    {
        public long AreaId { get; set; }
        public string AreaName { get; set; } = null!;
        public bool IsActive { get; set; }
        /// <summary>"HH:mm" or null.</summary>
        public string? MorningPickupTime { get; set; }
        /// <summary>"HH:mm" or null.</summary>
        public string? AfternoonDropTime { get; set; }
    }

    public class DriverListItemDto
    {
        public string DriverId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        public string DlNumber { get; set; } = null!;
        public DateOnly DlValidUpto { get; set; }
        public string DlStatus { get; set; } = null!;
        public int? DlDaysLeft { get; set; }
        public long? VehicleId { get; set; }
        public string? VehicleLabel { get; set; }
        public string? VehiclePlate { get; set; }
        public List<string> AreaNames { get; set; } = new();
        public string StatusName { get; set; } = null!;
        public string? PhotoUrl { get; set; }
        public bool HasLogin { get; set; }
    }

    public class DriverPagedDto
    {
        public List<DriverListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class DriverDto
    {
        public string DriverId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        public string? Email { get; set; }
        public string Address { get; set; } = null!;
        public string DlNumber { get; set; } = null!;
        public DateOnly DlValidUpto { get; set; }
        public string DlStatus { get; set; } = null!;
        public int? DlDaysLeft { get; set; }
        public long? VehicleId { get; set; }
        public string? VehicleLabel { get; set; }
        public string? VehiclePlate { get; set; }
        public List<DriverAreaDto> Areas { get; set; } = new();
        public string StatusName { get; set; } = null!;
        public string? PhotoUrl { get; set; }
        public bool HasLogin { get; set; }
        public string? LoginId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class DriverPhotoDto
    {
        public string DriverId { get; set; } = null!;
        public string? PhotoUrl { get; set; }
    }

    public class DriverLookupsDto
    {
        /// <summary>Active vehicles of the school (the name is "label (plate)").</summary>
        public List<LookupItemDto> Vehicles { get; set; } = new();
        /// <summary>Active areas of the school.</summary>
        public List<LookupItemDto> Areas { get; set; } = new();
    }

    public class DriverLoginCreatedDto
    {
        public string DriverId { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string OrgUserId { get; set; } = null!;
        /// <summary>Shown ONCE; only its hash is stored.</summary>
        public string TemporaryPassword { get; set; } = null!;
        public string Role { get; set; } = null!;
        public bool EmailSent { get; set; }
        public string? EmailSentTo { get; set; }
    }
}
