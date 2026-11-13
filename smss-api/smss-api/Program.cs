using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using smss_api_service_layer.helper;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Fail fast, naming the missing SETTING (never a value): secrets are not in the committed files (user-secrets in development, environment variables in production).
smss_api.Helpers.StartupConfigGuard.Validate(builder.Configuration, builder.Environment);
smss_api_service_layer.helper.reportcard.ReportCardBulk.MaxConcurrent = Math.Max(1, builder.Configuration.GetValue<int?>("ReportCards:BulkMaxConcurrent") ?? smss_api_service_layer.helper.reportcard.ReportCardBulk.DefaultMaxConcurrent);
smss_api_service_layer.helper.reportcard.ReportCardBulk.MaxStudentsPerRequest = Math.Max(1, builder.Configuration.GetValue<int?>("ReportCards:BulkMaxStudents") ?? smss_api_service_layer.helper.reportcard.ReportCardBulk.DefaultMaxStudentsPerRequest);

// Add DbContext with PostgreSQL
builder.Services.AddDbContext<dbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));


// Add services to the container.
builder.Services.AddScoped<IMasterDataRepository, MasterDataRepository>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();

builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddSingleton<smss_api_service_layer.@interface.ILoginThrottle>(sp =>
    new smss_api_service_layer.helper.LoginThrottle(smss_api_service_layer.helper.LoginThrottleOptions.From(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<JwtTokenGenerator>();

builder.Services.AddScoped<ISchoolRegistrationRepository, SchoolRegistrationRepository>();
builder.Services.AddScoped<ISchoolRegistrationService, SchoolRegistrationService>();

builder.Services.AddScoped<IAcademicYearRepository, AcademicYearRepository>();
builder.Services.AddScoped<IAcademicYearService, AcademicYearService>();
builder.Services.AddScoped<IClassRepository, ClassRepository>();
builder.Services.AddScoped<IClassService, ClassService>();
builder.Services.AddScoped<ISectionRepository, SectionRepository>();
builder.Services.AddScoped<ISectionService, SectionService>();
builder.Services.AddScoped<IClassSubjectRepository, ClassSubjectRepository>();
builder.Services.AddScoped<IClassSubjectService, ClassSubjectService>();
builder.Services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
builder.Services.AddScoped<IDocumentTypeService, DocumentTypeService>();
builder.Services.AddScoped<IAdmissionRepository, AdmissionRepository>();
builder.Services.AddScoped<IAdmissionService, AdmissionService>();
builder.Services.AddScoped<IStudentOnboardingService, StudentOnboardingService>();
builder.Services.AddScoped<IStudentImportRepository, StudentImportRepository>();
builder.Services.AddScoped<IStudentImportService, StudentImportService>();
builder.Services.AddScoped<IStudentRepository, StudentRepository>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IStudentMyFeesService, StudentMyFeesService>();
builder.Services.AddScoped<IReportSetupRepository, ReportSetupRepository>();
builder.Services.AddScoped<IReportEntryGuard, ReportEntryGuard>();   // Stage 2: the real "has anything been entered" check (marks, submitted grids, locks, class-teacher inputs)
builder.Services.AddScoped<IReportMarksRepository, ReportMarksRepository>();
builder.Services.AddScoped<IReportMarksService, ReportMarksService>();
builder.Services.AddScoped<IReportClassService, ReportClassService>();
builder.Services.AddScoped<ReportAccess>();
builder.Services.AddScoped<IReportResultRepository, ReportResultRepository>();      // Stage 3: results and publishing
builder.Services.AddScoped<smss_api_service_layer.helper.reportcard.ReportResultBuilder>();
builder.Services.AddScoped<IReportResultService, ReportResultService>();
builder.Services.AddScoped<IReportCardSettingsRepository, ReportCardSettingsRepository>();   // Stage 5a: report card settings, designs and the preview
builder.Services.AddScoped<smss_api_service_layer.helper.reportcard.ReportCardImageReader>();
builder.Services.AddScoped<IReportCardSettingsService, ReportCardSettingsService>();
builder.Services.AddScoped<IGradeScaleService, GradeScaleService>();
builder.Services.AddScoped<IReportSchemeService, ReportSchemeService>();
builder.Services.AddScoped<IReportActivityAreaService, ReportActivityAreaService>();
builder.Services.AddScoped<IReportScheduleService, ReportScheduleService>();
builder.Services.AddScoped<IFeeSectionStatusService, FeeSectionStatusService>();
builder.Services.AddScoped<IStudentIdCardRepository, StudentIdCardRepository>();
builder.Services.AddScoped<IStudentIdCardService, StudentIdCardService>();
builder.Services.AddScoped<IIdCardSettingsRepository, IdCardSettingsRepository>();
builder.Services.AddScoped<IIdCardSettingsService, IdCardSettingsService>();
builder.Services.AddScoped<IMyStudentsService, MyStudentsService>();
builder.Services.AddScoped<IStudentPhotoService, StudentPhotoService>();
builder.Services.AddScoped<IEmployeePhotoService, EmployeePhotoService>();
builder.Services.AddScoped<IStudentLoginRepository, StudentLoginRepository>();
builder.Services.AddScoped<IStudentLoginService, StudentLoginService>();
builder.Services.AddScoped<ISubjectRepository, SubjectRepository>();
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<IEmployeeDesignationRepository, EmployeeDesignationRepository>();
builder.Services.AddScoped<IEmployeeDesignationService, EmployeeDesignationService>();
builder.Services.AddScoped<IEmployeeDepartmentRepository, EmployeeDepartmentRepository>();
builder.Services.AddScoped<IEmployeeDepartmentService, EmployeeDepartmentService>();
builder.Services.AddScoped<IAdmissionTypeRepository, AdmissionTypeRepository>();
builder.Services.AddScoped<IAdmissionTypeService, AdmissionTypeService>();
builder.Services.AddScoped<IFeeTypeRepository, FeeTypeRepository>();
builder.Services.AddScoped<IFeeTypeService, FeeTypeService>();
builder.Services.AddScoped<IFeeStructureRepository, FeeStructureRepository>();
builder.Services.AddScoped<IFeeStructureService, FeeStructureService>();
builder.Services.AddScoped<IFeeConcessionRepository, FeeConcessionRepository>();
builder.Services.AddScoped<IFeeConcessionService, FeeConcessionService>();
builder.Services.AddScoped<IStudentFeeRepository, StudentFeeRepository>();
builder.Services.AddScoped<IStudentFeeService, StudentFeeService>();
builder.Services.AddScoped<IStudentFeeBillingRepository, StudentFeeBillingRepository>();
builder.Services.AddScoped<IStudentFeeBillingService, StudentFeeBillingService>();
builder.Services.AddScoped<IFeePaymentRepository, FeePaymentRepository>();
builder.Services.AddScoped<IFeePaymentService, FeePaymentService>();
builder.Services.AddScoped<IPeriodRepository, PeriodRepository>();
builder.Services.AddScoped<IPeriodService, PeriodService>();
builder.Services.AddScoped<ITimetableRepository, TimetableRepository>();
builder.Services.AddScoped<ITimetableService, TimetableService>();
builder.Services.AddScoped<ISchoolEventRepository, SchoolEventRepository>();
builder.Services.AddScoped<ISchoolEventService, SchoolEventService>();
builder.Services.AddScoped<IAdmissionDocumentRepository, AdmissionDocumentRepository>();
builder.Services.AddScoped<IAdmissionDocumentService, AdmissionDocumentService>();
builder.Services.AddScoped<INoticeRepository, NoticeRepository>();
builder.Services.AddScoped<INoticeService, NoticeService>();
builder.Services.AddScoped<IAssessmentRepository, AssessmentRepository>();
builder.Services.AddScoped<IAssessmentService, AssessmentService>();
builder.Services.AddScoped<IHomeworkRepository, HomeworkRepository>();
builder.Services.AddScoped<IHomeworkService, HomeworkService>();
builder.Services.AddScoped<ILeaveRepository, LeaveRepository>();
builder.Services.AddScoped<ILeaveTypeService, LeaveTypeService>();
builder.Services.AddScoped<IEmployeeLeaveService, EmployeeLeaveService>();
builder.Services.AddScoped<IStudentLeaveService, StudentLeaveService>();
builder.Services.AddScoped<IStudentDashboardService, StudentDashboardService>();
builder.Services.AddScoped<ITeacherDashboardRepository, TeacherDashboardRepository>();
builder.Services.AddScoped<ITeacherDashboardService, TeacherDashboardService>();
builder.Services.AddScoped<ISchoolDashboardRepository, SchoolDashboardRepository>();
builder.Services.AddScoped<ISchoolDashboardService, SchoolDashboardService>();
builder.Services.AddScoped<IPrincipalDashboardService, PrincipalDashboardService>();
builder.Services.AddScoped<IAccountantDashboardRepository, AccountantDashboardRepository>();
builder.Services.AddScoped<IAccountantDashboardService, AccountantDashboardService>();
builder.Services.AddScoped<IAdmissionDashboardRepository, AdmissionDashboardRepository>();
builder.Services.AddScoped<IAdmissionDashboardService, AdmissionDashboardService>();
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IClassTeacherRepository, ClassTeacherRepository>();
builder.Services.AddScoped<IClassTeacherService, ClassTeacherService>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<ISubscriptionPricingService, SubscriptionPricingService>();
builder.Services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
builder.Services.AddScoped<ISuperAdminDashboardService, SuperAdminDashboardService>();
builder.Services.AddScoped<ICompanyFinanceRepository, CompanyFinanceRepository>();
builder.Services.AddScoped<ICompanyEmployeeService, CompanyEmployeeService>();
builder.Services.AddScoped<ICompanyExpenseService, CompanyExpenseService>();
builder.Services.AddScoped<ICompanySalaryService, CompanySalaryService>();
builder.Services.AddScoped<ICompanyFinanceService, CompanyFinanceService>();
builder.Services.AddScoped<IFeeReportRepository, FeeReportRepository>();
builder.Services.AddScoped<IFeeReportService, FeeReportService>();

builder.Services.AddScoped<IEnquiryRepository, EnquiryRepository>();
builder.Services.AddScoped<IEnquiryService, EnquiryService>();

builder.Services.AddScoped<IMasterImportRepository, MasterImportRepository>();
builder.Services.AddScoped<IMasterImportService, MasterImportService>();
builder.Services.AddScoped<ISystemSetupRepository, SystemSetupRepository>();
builder.Services.AddScoped<ISystemSetupService, SystemSetupService>();

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// File storage: relative paths resolve against the project folder
var uploadsRoot = builder.Configuration["FileStorage:RootPath"] ?? "uploads";
if (!Path.IsPathRooted(uploadsRoot))
    uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, uploadsRoot);
Directory.CreateDirectory(uploadsRoot);
const string uploadsRequestPath = "/uploads";

// Private store (employee documents): NOT under the static-files root, so nothing in it is ever reachable by URL
var privateRoot = builder.Configuration["FileStorage:PrivateRootPath"] ?? "uploads-private";
if (!Path.IsPathRooted(privateRoot))
    privateRoot = Path.Combine(builder.Environment.ContentRootPath, privateRoot);
Directory.CreateDirectory(privateRoot);

builder.Services.AddSingleton<IFileStorageService>(sp =>
    new LocalFileStorageService(uploadsRoot, uploadsRequestPath, privateRoot, sp.GetRequiredService<ILogger<LocalFileStorageService>>()));

// JWT authentication
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
var jwt = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters long.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    x => x.Key,
                    x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

            return new BadRequestObjectResult(new ApiResponse<object>
            {
                Status = false,
                StatusCode = 400,
                Message = "Validation failed",
                Data = errors
            });
        };
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Public enquiry form: a fixed window per client IP (appsettings RateLimiting:PublicEnquiries). Over the limit -> 429 in the project's response shape.
var enquiryLimit = Math.Max(1, builder.Configuration.GetValue<int?>("RateLimiting:PublicEnquiries:PermitLimit") ?? 5);
var enquiryWindow = TimeSpan.FromMinutes(Math.Max(1, builder.Configuration.GetValue<int?>("RateLimiting:PublicEnquiries:WindowMinutes") ?? 10));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(smss_api.Controllers.PublicEnquiriesController.RateLimitPolicy, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            smss_api_service_layer.helper.ClientAddress.PartitionKey(httpContext.Connection.RemoteIpAddress),
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = enquiryLimit, Window = enquiryWindow, QueueLimit = 0, AutoReplenishment = true }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiResponse<object> { Status = false, StatusCode = 429, Message = "Too many requests. Please wait a few minutes and try again.", Data = null },
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }, cancellationToken: token);
    };
});

// Behind a reverse proxy / load balancer the connection address is the proxy's. X-Forwarded-For / -Proto are honoured ONLY when the request comes from a
// configured proxy (ForwardedHeaders:KnownProxies = addresses, ForwardedHeaders:KnownNetworks = CIDR ranges). With nothing configured only loopback is trusted,
// so an arbitrary client can never choose its own "IP". Needs both the rate limiter and the stored enquiry ip_address to see the real client.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = Math.Max(1, builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1);
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
    {
        if (!System.Net.IPAddress.TryParse(proxy, out var address))
            throw new InvalidOperationException("ForwardedHeaders:KnownProxies contains a value that is not an IP address.");
        options.KnownProxies.Add(address);
    }
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
    {
        if (!System.Net.IPNetwork.TryParse(network, out var cidr))
            throw new InvalidOperationException("ForwardedHeaders:KnownNetworks contains a value that is not a CIDR range such as 10.0.0.0/8.");
        options.KnownIPNetworks.Add(cidr);
    }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy =>
    {
        // Origins come from Cors:AllowedOrigins (StartupConfigGuard has already refused to start outside Development without one)
        policy.WithOrigins(smss_api.Helpers.StartupConfigGuard.CorsOrigins(builder.Configuration, builder.Environment.IsDevelopment()))
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// A failure on a background thread or an unobserved task is not tied to any request and cannot be turned into a 500,
// so at least leave the real cause in the log (without this, a dead process leaves no trace of why).
app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogWarning("API is stopping"));
AppDomain.CurrentDomain.UnhandledException += (_, e) => app.Logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception outside a request (terminating: {Terminating})", e.IsTerminating);
TaskScheduler.UnobservedTaskException += (_, e) => app.Logger.LogError(e.Exception, "Unobserved task exception");

// Configure the HTTP request pipeline.
// First, so it sees everything below it (in Development too: a JSON ApiResponse instead of an HTML error page)
app.UseMiddleware<smss_api.Helpers.GlobalExceptionMiddleware>();

// Before anything that reads the client address or scheme (HTTPS redirection, the rate limiter, the enquiry controller)
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapOpenApi();
}

app.UseCors("AngularDev");

// Student and employee photos are PRIVATE (served only by GET /api/Student/{id}/photo and GET /api/Employee/{id}/photo). Whatever an old deployment still has under /uploads/<school>/students/ or /employees/ is not served either.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith(uploadsRequestPath + "/", StringComparison.OrdinalIgnoreCase) && (path.Contains("/students/", StringComparison.OrdinalIgnoreCase) || path.Contains("/employees/", StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = uploadsRequestPath,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});

app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Authoritative forced first-login password change (the Angular route guard only mirrors it)
app.UseMiddleware<smss_api.Helpers.FirstLoginEnforcementMiddleware>();

app.MapControllers();

app.Run();
