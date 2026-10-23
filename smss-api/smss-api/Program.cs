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

// Add DbContext with PostgreSQL
builder.Services.AddDbContext<dbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));


// Add services to the container.
builder.Services.AddScoped<IMasterDataRepository, MasterDataRepository>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();

builder.Services.AddScoped<IAuthRepository, AuthRepository>();
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
builder.Services.AddScoped<IStudentRepository, StudentRepository>();
builder.Services.AddScoped<IStudentService, StudentService>();
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
builder.Services.AddScoped<IHomeworkRepository, HomeworkRepository>();
builder.Services.AddScoped<IHomeworkService, HomeworkService>();
builder.Services.AddScoped<ILeaveRepository, LeaveRepository>();
builder.Services.AddScoped<ILeaveTypeService, LeaveTypeService>();
builder.Services.AddScoped<IEmployeeLeaveService, EmployeeLeaveService>();
builder.Services.AddScoped<IStudentLeaveService, StudentLeaveService>();
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IClassTeacherRepository, ClassTeacherRepository>();
builder.Services.AddScoped<IClassTeacherService, ClassTeacherService>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IFeeReportRepository, FeeReportRepository>();
builder.Services.AddScoped<IFeeReportService, FeeReportService>();

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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapOpenApi();
}

app.UseCors("AngularDev");

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = uploadsRequestPath,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
// Authoritative forced first-login password change (the Angular route guard only mirrors it)
app.UseMiddleware<smss_api.Helpers.FirstLoginEnforcementMiddleware>();

app.MapControllers();

app.Run();
