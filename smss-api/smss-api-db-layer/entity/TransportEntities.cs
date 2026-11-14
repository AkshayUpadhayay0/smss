using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Global lookup: Bus, Van, Car, Other (extendable by adding rows).</summary>
    [Table("lut_vehicle_type", Schema = "public")]
    public class LutVehicleType
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("vehicle_type_id")] public long VehicleTypeId { get; set; }
        [Column("type_code")][StringLength(20)] public string TypeCode { get; set; } = null!;
        [Column("type_name")][StringLength(50)] public string TypeName { get; set; } = null!;
        [Column("description")][StringLength(250)] public string? Description { get; set; }
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>Global lookup: Own / Rented.</summary>
    [Table("lut_vehicle_ownership", Schema = "public")]
    public class LutVehicleOwnership
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("ownership_id")] public long OwnershipId { get; set; }
        [Column("ownership_code")][StringLength(20)] public string OwnershipCode { get; set; } = null!;
        [Column("ownership_name")][StringLength(50)] public string OwnershipName { get; set; } = null!;
        [Column("description")][StringLength(250)] public string? Description { get; set; }
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A pickup area of a school. The fee is per period of the school's Transport fee type (no frequency column by decision).</summary>
    [Table("tb_transport_areas", Schema = "public")]
    public class TbTransportArea
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("area_id")] public long AreaId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("area_name")][StringLength(150)] public string AreaName { get; set; } = null!;
        [Column("distance_km", TypeName = "numeric(6,2)")] public decimal? DistanceKm { get; set; }
        [Column("transport_fee", TypeName = "numeric(10,2)")] public decimal TransportFee { get; set; }
        [Column("remarks")][StringLength(500)] public string? Remarks { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A school vehicle. Expiry badges are derived at read time and never stored.</summary>
    [Table("tb_vehicles", Schema = "public")]
    public class TbVehicle
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("vehicle_id")] public long VehicleId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        /// <summary>The school's own name, e.g. "Bus 3".</summary>
        [Column("vehicle_label")][StringLength(50)] public string VehicleLabel { get; set; } = null!;
        /// <summary>Plate, stored trimmed, upper-cased and without inner spaces.</summary>
        [Column("registration_number")][StringLength(20)] public string RegistrationNumber { get; set; } = null!;
        [Column("vehicle_type_id")] public long VehicleTypeId { get; set; }
        [Column("ownership_id")] public long OwnershipId { get; set; }
        [Column("model_year")] public short? ModelYear { get; set; }
        [Column("seating_capacity")] public short? SeatingCapacity { get; set; }
        /// <summary>Only for Rented vehicles (checked in the service).</summary>
        [Column("owner_name")][StringLength(150)] public string? OwnerName { get; set; }
        [Column("owner_contact")][StringLength(20)] public string? OwnerContact { get; set; }
        [Column("last_service_date", TypeName = "date")] public DateOnly? LastServiceDate { get; set; }
        [Column("puc_expiry_date", TypeName = "date")] public DateOnly? PucExpiryDate { get; set; }
        [Column("insurance_expiry_date", TypeName = "date")] public DateOnly? InsuranceExpiryDate { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// A school bus driver. NOT an employee row. status_id is "hide this row" (Active / Inactive); the login (tb_users, user type Driver) follows it.
    /// </summary>
    [Table("tb_drivers", Schema = "public")]
    public class TbDriver
    {
        // Business id, e.g. DPS001DR001 ({school short code}DR{nnn}): generated by the app once, reused as the login id (tb_users.org_user_id).
        [Key][DatabaseGenerated(DatabaseGeneratedOption.None)][Column("driver_id")][StringLength(20)] public string DriverId { get; set; } = null!;
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("full_name")][StringLength(150)] public string FullName { get; set; } = null!;
        [Column("mobile_number")][StringLength(20)] public string MobileNumber { get; set; } = null!;
        [Column("email")][StringLength(150)] public string? Email { get; set; }
        [Column("address")] public string Address { get; set; } = null!;
        [Column("dl_number")][StringLength(30)] public string DlNumber { get; set; } = null!;
        [Column("dl_valid_upto", TypeName = "date")] public DateOnly DlValidUpto { get; set; }
        /// <summary>A vehicle may have several Active drivers; there is deliberately no uniqueness here.</summary>
        [Column("vehicle_id")] public long? VehicleId { get; set; }
        /// <summary>Key in the PRIVATE store (never a public URL); null = no photo.</summary>
        [Column("photo_path")] public string? PhotoPath { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>The pickup areas of a driver: a pure mapping set, replaced wholesale on every save (rows are hard-deleted).</summary>
    [Table("tb_driver_areas", Schema = "public")]
    public class TbDriverArea
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("driver_area_id")] public long DriverAreaId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("driver_id")][StringLength(20)] public string DriverId { get; set; } = null!;
        [Column("area_id")] public long AreaId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// One stretch of duty by one driver. At most ONE open session (ended_at IS NULL) per driver, enforced by a partial unique index.
    /// No coordinates are stored here. The tracking token is stored only as its SHA-256 hash.
    /// </summary>
    [Table("tb_driver_duty_sessions", Schema = "public")]
    public class TbDriverDutySession
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("session_id")] public long SessionId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("driver_id")][StringLength(20)] public string DriverId { get; set; } = null!;
        /// <summary>The driver's vehicle when duty started (a snapshot).</summary>
        [Column("vehicle_id")] public long VehicleId { get; set; }
        [Column("started_at")] public DateTime StartedAt { get; set; }
        [Column("ended_at")] public DateTime? EndedAt { get; set; }
        /// <summary>DRIVER_ENDED | AUTO_TIMEOUT | DRIVER_DEACTIVATED | SESSION_EXPIRED; null while open.</summary>
        [Column("end_reason")][StringLength(20)] public string? EndReason { get; set; }
        [Column("tracking_token_hash")][StringLength(64)] public string TrackingTokenHash { get; set; } = null!;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    /// <summary>The LATEST fix of a driver on duty: one row per driver, no history. Written only by the raw upsert in DriverDutyRepository; read here.</summary>
    [Table("tb_driver_live_location", Schema = "public")]
    public class TbDriverLiveLocation
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.None)][Column("driver_id")][StringLength(20)] public string DriverId { get; set; } = null!;
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("session_id")] public long SessionId { get; set; }
        [Column("vehicle_id")] public long VehicleId { get; set; }
        [Column("latitude")] public double Latitude { get; set; }
        [Column("longitude")] public double Longitude { get; set; }
        [Column("accuracy_m")] public float? AccuracyM { get; set; }
        [Column("speed_mps")] public float? SpeedMps { get; set; }
        [Column("heading_deg")] public float? HeadingDeg { get; set; }
        /// <summary>The device clock: orders fixes only.</summary>
        [Column("recorded_at")] public DateTime RecordedAt { get; set; }
        /// <summary>The server clock: LIVE / STALE / OFFLINE are computed from this.</summary>
        [Column("received_at")] public DateTime ReceivedAt { get; set; }
    }

    /// <summary>Which transport area a student uses in an academic year. ONE row per student and year: removing sets status Inactive, assigning again reactivates the same row.</summary>
    [Table("tb_student_transport", Schema = "public")]
    public class TbStudentTransport
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("student_transport_id")] public long StudentTransportId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("area_id")] public long AreaId { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
