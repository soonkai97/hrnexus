using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Infrastructure.Auth;
using NightclubWorkforce.Infrastructure.Hubs;
using NightclubWorkforce.Infrastructure.Jobs;
using NightclubWorkforce.Infrastructure.Persistence;
using NightclubWorkforce.Infrastructure.Files;
using NightclubWorkforce.Infrastructure.Services;

namespace NightclubWorkforce.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connStr = config.GetConnectionString("DefaultConnection")!;

        // EF Core — PostgreSQL
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connStr, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null))
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();

        // Auth
        // Venue configuration (timezone etc.)
        services.Configure<VenueOptions>(config.GetSection(VenueOptions.SectionName));

        // Auth
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IFeatureMgmtService, FeatureMgmtService>();

        // Cross-cutting abstractions
        services.AddHttpContextAccessor();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // SignalR
        services.AddSignalR();
        services.AddScoped<INotificationHub, NotificationHubAdapter>();

        // Hangfire — PostgreSQL
        services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connStr)));
        services.AddHangfireServer();
        services.AddScoped<ShiftJobs>();

        // JWT
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey        = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
                    ValidateIssuer   = true,
                    ValidIssuer      = config["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience    = config["Jwt:Audience"],
                    ClockSkew        = TimeSpan.Zero,
                };
                // Allow SignalR to get token from query string
                opts.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    public static void RegisterHangfireJobs()
    {
        RecurringJob.AddOrUpdate<ShiftJobs>("remind-long-running-worklogs",
            j => j.RemindLongRunningWorkLogsAsync(), "*/15 * * * *");

        RecurringJob.AddOrUpdate<ShiftJobs>("reset-leave-entitlements",
            j => j.ResetLeaveEntitlementsAsync(), "0 1 1 1 *"); // 01:00 UTC on Jan 1

        RecurringJob.AddOrUpdate<ShiftJobs>("auto-reject-overdue-leave",
            j => j.AutoRejectOverdueLeaveAsync(), "0 2 * * *"); // 02:00 UTC daily
    }
}
