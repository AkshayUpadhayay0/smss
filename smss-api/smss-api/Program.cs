using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Microsoft.Extensions.FileProviders;


var builder = WebApplication.CreateBuilder(args);

// Add DbContext with PostgreSQL
builder.Services.AddDbContext<dbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));


// Add services to the container.
builder.Services.AddScoped<IMasterDataRepository, MasterDataRepository>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();

builder.Services.AddScoped<ISchoolRegistrationRepository, SchoolRegistrationRepository>();
builder.Services.AddScoped<ISchoolRegistrationService, SchoolRegistrationService>();

// File storage: relative paths resolve against the project folder
var uploadsRoot = builder.Configuration["FileStorage:RootPath"] ?? "uploads";
if (!Path.IsPathRooted(uploadsRoot))
    uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, uploadsRoot);
Directory.CreateDirectory(uploadsRoot);
const string uploadsRequestPath = "/uploads";

builder.Services.AddSingleton<IFileStorageService>(sp =>
    new LocalFileStorageService(uploadsRoot, uploadsRequestPath, sp.GetRequiredService<ILogger<LocalFileStorageService>>()));

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

// Configure the HTTP request pipeline.
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

app.UseAuthorization();

app.MapControllers();

app.Run();
