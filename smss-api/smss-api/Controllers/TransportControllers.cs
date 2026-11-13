using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Transport (areas, vehicles, drivers). Reads: School Admin and Principal (CanViewTransport). Every write, the login creation and the photo upload:
    // School Admin only (CanConfigureTransport). Every other role, a Driver included, is refused with 403. The role check is the FIRST statement of every action.
    // The school always comes from the token; no school id is accepted. Another school's id is a 404.

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TransportAreasController : ControllerBase
    {
        private readonly ITransportAreaService _service;
        public TransportAreasController(ITransportAreaService service) => _service = service;
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/TransportAreas?search=&status=All|Active|Inactive&page=1&pageSize=10
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.ListAsync(User.GetSchoolId(), search, status, page, pageSize, ct));
        }

        // GET api/TransportAreas/5
        [HttpGet("{areaId:long}")]
        public async Task<IActionResult> Get(long areaId, CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.GetAsync(User.GetSchoolId(), areaId, ct));
        }

        // POST api/TransportAreas
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TransportAreaRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change transport areas.");
            return Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));
        }

        // PUT api/TransportAreas/5
        [HttpPut("{areaId:long}")]
        public async Task<IActionResult> Update(long areaId, [FromBody] TransportAreaRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change transport areas.");
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), areaId, request, ct));
        }

        // POST api/TransportAreas/5/toggle-status   (POST, not PATCH: PATCH is blocked in this environment)
        [HttpPost("{areaId:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long areaId, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change transport areas.");
            return Reply(await _service.ToggleStatusAsync(User.GetSchoolId(), areaId, ct));
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VehiclesController : ControllerBase
    {
        private readonly IVehicleService _service;
        public VehiclesController(IVehicleService service) => _service = service;
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/Vehicles/lookups   (vehicle types and ownerships)
        [HttpGet("lookups")]
        public async Task<IActionResult> GetLookups(CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.GetLookupsAsync(User.GetSchoolId(), ct));
        }

        // GET api/Vehicles?search=&status=All|Active|Inactive&vehicleTypeId=&page=1&pageSize=10   (search: vehicle number / label or plate)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, [FromQuery] long? vehicleTypeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.ListAsync(User.GetSchoolId(), search, status, vehicleTypeId, page, pageSize, ct));
        }

        // GET api/Vehicles/5
        [HttpGet("{vehicleId:long}")]
        public async Task<IActionResult> Get(long vehicleId, CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.GetAsync(User.GetSchoolId(), vehicleId, ct));
        }

        // POST api/Vehicles
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VehicleRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change vehicles.");
            return Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));
        }

        // PUT api/Vehicles/5
        [HttpPut("{vehicleId:long}")]
        public async Task<IActionResult> Update(long vehicleId, [FromBody] VehicleRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change vehicles.");
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), vehicleId, request, ct));
        }

        // POST api/Vehicles/5/toggle-status
        [HttpPost("{vehicleId:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long vehicleId, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change vehicles.");
            return Reply(await _service.ToggleStatusAsync(User.GetSchoolId(), vehicleId, ct));
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DriversController : ControllerBase
    {
        private readonly IDriverService _service;
        private readonly IDriverPhotoService _photos;
        public DriversController(IDriverService service, IDriverPhotoService photos) { _service = service; _photos = photos; }
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/Drivers/lookups   (the school's Active vehicles and Active areas, for the driver form)
        [HttpGet("lookups")]
        public async Task<IActionResult> GetLookups(CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.GetLookupsAsync(User.GetSchoolId(), ct));
        }

        // GET api/Drivers?search=&status=All|Active|Inactive&vehicleId=&page=1&pageSize=10   (search: name, mobile, driver id or licence number)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, [FromQuery] long? vehicleId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.ListAsync(User.GetSchoolId(), search, status, vehicleId, page, pageSize, ct));
        }

        // GET api/Drivers/{driverId}/photo   (the photo, from PRIVATE storage; there is no public URL for a driver photo)
        // Image content type, Cache-Control: private, an ETag (If-None-Match gives a 304). Another school's driver is a 404.
        [HttpGet("{driverId:length(1,20)}/photo")]
        public async Task<IActionResult> GetPhoto(string driverId, CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            var r = await _photos.GetAsync(User.GetSchoolId(), driverId, ct);
            if (r.Content == null) return StatusCode(r.StatusCode, new ApiResponse<object> { Status = false, StatusCode = r.StatusCode, Message = r.Message, Data = null });
            Response.Headers.CacheControl = "private, no-cache";
            return File(r.Content, r.ContentType, lastModified: null, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(r.ETag));
        }

        // GET api/Drivers/{driverId}
        [HttpGet("{driverId:length(1,20)}")]
        public async Task<IActionResult> Get(string driverId, CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view transport.");
            return Reply(await _service.GetAsync(User.GetSchoolId(), driverId, ct));
        }

        // POST api/Drivers
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DriverRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change drivers.");
            return Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));
        }

        // PUT api/Drivers/{driverId}   (areaIds REPLACES the driver's pickup areas)
        [HttpPut("{driverId:length(1,20)}")]
        public async Task<IActionResult> Update(string driverId, [FromBody] DriverRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change drivers.");
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), driverId, request, ct));
        }

        // POST api/Drivers/{driverId}/toggle-status
        [HttpPost("{driverId:length(1,20)}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(string driverId, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change drivers.");
            return Reply(await _service.ToggleStatusAsync(User.GetSchoolId(), driverId, ct));
        }

        // POST api/Drivers/{driverId}/photo   (multipart/form-data, field name: file; PNG / JPG / WEBP, 2 MB)
        [HttpPost("{driverId:length(1,20)}/photo")]
        [RequestSizeLimit(LogoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadPhoto(string driverId, IFormFile? file, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change drivers.");
            if (file == null) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "Photo file is required.", Data = null });
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadPhotoAsync(User.GetSchoolId(), driverId, stream, file.FileName, file.Length, ct));
        }

        // POST api/Drivers/{driverId}/photo/remove
        [HttpPost("{driverId:length(1,20)}/photo/remove")]
        public async Task<IActionResult> RemovePhoto(string driverId, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change drivers.");
            return Reply(await _service.RemovePhotoAsync(User.GetSchoolId(), driverId, ct));
        }

        // POST api/Drivers/{driverId}/create-login   (no body: the role is always DRIVER)
        // Returns the generated login id + temporary password ONCE (it is never stored in plain text), like the employee create-login.
        [HttpPost("{driverId:length(1,20)}/create-login")]
        public async Task<IActionResult> CreateLogin(string driverId, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change drivers.");
            return Reply(await _service.CreateLoginAsync(User.GetSchoolId(), driverId, ct));
        }
    }
}
