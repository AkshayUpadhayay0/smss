namespace smss_api_service_layer.dto
{
    // Stage 3: which transport area a student uses, and the student's own "Where is my bus" answer.

    /// <summary>PUT api/StudentTransport/{studentId}. Validated in the service AFTER the role check. AcademicYearId defaults to the school's current year.</summary>
    public class StudentTransportRequest
    {
        public long? AreaId { get; set; }
        public long? AcademicYearId { get; set; }
    }

    public class StudentTransportAssignmentDto
    {
        public long AreaId { get; set; }
        public string AreaName { get; set; } = null!;
        /// <summary>False when the area was deactivated after the student was assigned: the school should move the student.</summary>
        public bool AreaIsActive { get; set; }
    }

    /// <summary>For the School Admin / Principal (the Transport card on the student profile). Assignment is null when the student has none (or it was removed).</summary>
    public class StudentTransportDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public bool IsCurrentYear { get; set; }
        public StudentTransportAssignmentDto? Assignment { get; set; }
    }

    // ---------------------------------------------------------------- the student's own view
    // THIS IS WHAT A STUDENT (a child) SEES. It is deliberately tiny and a field-exposure test pins the exact property names: adding a field here must be a conscious,
    // reviewed decision. NEVER add: the driver's phone, id, photo, address, licence, login, the vehicle's owner or id, another student's anything.

    public class MyBusAreaDto
    {
        public string AreaName { get; set; } = null!;
    }

    public class MyBusItemDto
    {
        public string DriverName { get; set; } = null!;
        public string VehicleLabel { get; set; } = null!;
        public string VehiclePlate { get; set; } = null!;
        /// <summary>LIVE | STALE | OFFLINE.</summary>
        public string Status { get; set; } = null!;
        /// <summary>Only for LIVE and STALE (a STALE position is the last one received: the app marks it clearly). Never for OFFLINE.</summary>
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        /// <summary>Seconds since the last location arrived, for "updated X ago". Null for OFFLINE.</summary>
        public int? AgeSeconds { get; set; }
        /// <summary>This driver's approximate morning time for the STUDENT'S OWN area, "HH:mm", or null when not set.</summary>
        public string? MorningPickupTime { get; set; }
        /// <summary>This driver's approximate afternoon time for the student's own area, "HH:mm", or null when not set.</summary>
        public string? AfternoonDropTime { get; set; }
    }

    /// <summary>A driver of the student's area who is NOT on duty now but has approximate times there. No status, no position.</summary>
    public class MyBusScheduledDto
    {
        public string DriverName { get; set; } = null!;
        public string VehicleLabel { get; set; } = null!;
        public string VehiclePlate { get; set; } = null!;
        public string? MorningPickupTime { get; set; }
        public string? AfternoonDropTime { get; set; }
    }

    public class MyBusDto
    {
        /// <summary>False when the student has no active transport assignment this year (that is a 200, not an error).</summary>
        public bool Assigned { get; set; }
        public MyBusAreaDto? Area { get; set; }
        /// <summary>Drivers of the student's area who are on duty now. A driver appears here or in <see cref="Scheduled"/>, never in both.</summary>
        public List<MyBusItemDto> Buses { get; set; } = new();
        /// <summary>Active drivers of the student's area with at least one time who are not on duty now.</summary>
        public List<MyBusScheduledDto> Scheduled { get; set; } = new();
        public DateTime ServerTime { get; set; }
        public string? Message { get; set; }
    }
}
