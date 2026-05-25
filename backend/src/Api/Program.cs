using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using HRNexus.Api.Converters;
using HRNexus.Api.Filters;
using HRNexus.Api.Middleware;
using HRNexus.Api.ModelBinders;
using HRNexus.Application.Auth.Commands;
using HRNexus.Infrastructure;
using HRNexus.Infrastructure.Hubs;
using HRNexus.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/hrnexus-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Services ──────────────────────────────────────────────────────────────────
// Force all incoming DateTime (query params + JSON body) to Kind=Utc.
// Prevents "Cannot write DateTime with Kind=Unspecified" on PostgreSQL timestamptz columns.
builder.Services.AddScoped<FeatureFlagFilter>();
builder.Services.AddControllers(o =>
{
    o.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider());
    o.Filters.AddService<FeatureFlagFilter>();
})
.AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    o.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeJsonConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "HRNexus API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Name   = "Authorization",
        Type   = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
    });
    c.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });
});

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly));
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(
        builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200", "http://localhost:4300"])
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// ── App pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

// Ensure wwwroot/uploads exists for file storage
Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "uploads", "locations"));

// Ensure DB schema exists and seed demo data on startup
await using (var scope = app.Services.CreateAsyncScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<HRNexus.Application.Common.Interfaces.IPasswordHasher>();
    await db.Database.MigrateAsync();
    await HRNexus.Infrastructure.Persistence.SeedData.SeedDemoUsersAsync(db, hasher);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// Hangfire dashboard — development only; not exposed in production builds
if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");
app.MapControllers();
app.MapHub<ShiftHub>("/hubs/shift");

DependencyInjection.RegisterHangfireJobs(app.Services);

app.Run();
