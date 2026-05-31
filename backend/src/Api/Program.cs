using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HRNexus.Api.Authorization;
using HRNexus.Api.Converters;
using HRNexus.Api.Filters;
using HRNexus.Api.Health;
using HRNexus.Api.Middleware;
using HRNexus.Api.ModelBinders;
using HRNexus.Application.Auth.Commands;
using HRNexus.Infrastructure;
using HRNexus.Infrastructure.Hubs;
using HRNexus.Infrastructure.Persistence;
using HRNexus.Api.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

// Phase-2 sink is env-gated: set Seq__ServerUrl (e.g. http://seq:5341) to ship structured logs to
// Seq for search/dashboards/alerts. Unset (local/CI) → console + rolling JSON file exactly as before.
var seqServerUrl = Environment.GetEnvironmentVariable("Seq__ServerUrl");
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    // Stamp CorrelationId + authenticated StaffId on every event for the current request.
    .Enrich.With<HttpContextEnricher>()
    // Console stays human-readable; {Properties:j} surfaces CorrelationId/StaffId inline for local dev.
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    // File is structured JSON (one object per line) — greppable by CorrelationId/StaffId.
    .WriteTo.File(new CompactJsonFormatter(), "logs/hrnexus-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
if (!string.IsNullOrWhiteSpace(seqServerUrl))
    loggerConfig.WriteTo.Seq(seqServerUrl, apiKey: Environment.GetEnvironmentVariable("Seq__ApiKey"));
Log.Logger = loggerConfig.CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
// Required so HttpContextEnricher (and its AsyncLocal-backed accessor) sees the per-request context.
builder.Services.AddHttpContextAccessor();

// ── JWT signing-key safety check ──────────────────────────────────────────────
// A missing key can't sign tokens (fatal). The dev-default or a short key allows token forgery, so
// surface it LOUDLY at startup (also lands in Seq). Not a hard throw: the zero-config local Docker
// stack runs as Production with the compose default, and we must not break it — but in real prod the
// env MUST set a strong unique JWT_SECRET (see the .env section in CLAUDE.md).
{
    const string DevDefaultJwtKey = "HRNexus_Dev_Secret_Change_In_Production_Min32Chars";
    var jwtKey = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey))
        throw new InvalidOperationException("Jwt:Key is not configured — set JWT_SECRET in the environment.");
    if (jwtKey == DevDefaultJwtKey || jwtKey.Length < 32)
        Log.Error("SECURITY: Jwt:Key is the dev default or shorter than 32 chars — tokens are forgeable. " +
                  "Set a strong unique JWT_SECRET in the environment before exposing this instance.");

    // PII-at-rest encryption key. Like the JWT key, the local stack falls back to a compose default;
    // real prod MUST set a strong unique PII_ENCRYPTION_KEY (and must NOT change it once data is
    // encrypted, or that data becomes unreadable).
    const string DevDefaultPiiKey = "HRNexus_Dev_PII_Key_Change_In_Production_32c";
    var piiKey = builder.Configuration["Pii:Key"];
    if (string.IsNullOrWhiteSpace(piiKey))
        throw new InvalidOperationException("Pii:Key is not configured — set PII_ENCRYPTION_KEY in the environment.");
    if (piiKey == DevDefaultPiiKey || piiKey.Length < 32)
        Log.Error("SECURITY: Pii:Key is the dev default or shorter than 32 chars — encrypted PII is weakly protected. " +
                  "Set a strong unique PII_ENCRYPTION_KEY in the environment before exposing this instance.");
}

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

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly);
    // Behaviors run outermost-first: logging wraps everything (so it times validation too),
    // then validation runs before the handler.
    cfg.AddOpenBehavior(typeof(HRNexus.Application.Common.Behaviors.LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(HRNexus.Application.Common.Behaviors.ValidationBehavior<,>));
});
// Auto-register any FluentValidation validators in the Application assembly (none yet — the
// ValidationBehavior is a no-op until a command adds one; existing commands validate inline).
builder.Services.AddValidatorsFromAssembly(typeof(LoginCommand).Assembly);
builder.Services.AddInfrastructure(builder.Configuration);

// Permission-based authorization. Controllers use [Authorize(Policy = "perm:<key>")]; the policy
// provider builds each requirement on the fly and the handler resolves it against IPermissionResolver.
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// ── Health checks ───────────────────────────────────────────────────────────
// /health/live  = process is up (no dependency probes) — liveness.
// /health/ready = database reachable — readiness gating for orchestrators / load balancers.
// The existing /api/health (HealthController) is left untouched — maintenance mode polls it.
builder.Services.AddHealthChecks()
    .AddCheck<DbReadyHealthCheck>("database", tags: ["ready"]);

// ── OpenTelemetry (traces + metrics) ──────────────────────────────────────────
// Env-gated: set OTEL_EXPORTER_OTLP_ENDPOINT (e.g. an OTLP collector, or Seq's OTLP ingest for
// traces) to export. Unset (local/CI) → OTel is not wired at all, zero overhead. ASP.NET +
// HttpClient spans, plus runtime/ASP.NET metrics, tagged with the service name.
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? builder.Configuration["Otel:Endpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("hrnexus-api"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

// ── Rate limiting ───────────────────────────────────────────────────────────
// Per-IP fixed-window limiter on the auth endpoints. This is the FLOOD/credential-stuffing
// defence (many accounts probed from one source); per-account lockout (see LoginCommand) is
// the complementary single-account brute-force defence. The "auth" policy is applied via
// [EnableRateLimiting("auth")] on AuthController.
const string AuthRateLimitPolicy = "auth";
// Coarse anti-flood ceiling, NOT the primary brute-force control (that's the per-account lockout
// in LoginCommand, which a shared IP can't dilute). Whole venues sit behind ONE wifi/NAT public IP,
// so at shift change dozens of legitimate staff log in from the same IP — the limit must clear that
// burst comfortably while still capping a genuine flood. Tunable via config (RateLimit:Auth*) so ops
// can adjust per environment without a code change; tests set it low to exercise the 429 path.
var authPermitLimit   = builder.Configuration.GetValue<int?>("RateLimit:AuthPermitLimit")   ?? 100;
var authWindowSeconds = builder.Configuration.GetValue<int?>("RateLimit:AuthWindowSeconds") ?? 60;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new { error = "Too many requests. Please slow down and try again shortly." }),
            token);
    };
    options.AddPolicy(AuthRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window      = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit  = 0,
            }));
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(
        builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200", "http://localhost:4300"])
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()
     // Let browser JS read the correlation id off the response (for FE error reporting in a later phase).
     .WithExposedHeaders(HRNexus.Api.Middleware.CorrelationIdMiddleware.HeaderName)));

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
    // Encrypt any pre-existing plaintext PII left over from before field encryption (idempotent).
    await HRNexus.Infrastructure.Persistence.PiiBackfill.EncryptExistingAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Behind nginx the api only ever sees the proxy's container IP. nginx forwards the real client
// in X-Forwarded-For / X-Real-IP, so honour it FIRST — otherwise Connection.RemoteIpAddress is the
// proxy and the per-IP rate limiter degenerates into a single global bucket (false 429s at shift
// change). KnownProxies/Networks are cleared because only nginx can reach this container (internal
// docker network, no host port in prod), so the forwarded header is trusted.
// XForwardedProto is also honoured so Request.IsHttps reflects the ORIGINAL client scheme (nginx
// terminates TLS and proxies to the api over http). The auth cookies key their Secure flag off this,
// so it must be accurate: https at the edge → Secure cookies in prod; plain http on the local stack
// → non-Secure (or the browser would silently drop them).
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

// (Security response headers — X-Content-Type-Options, X-Frame-Options, X-XSS-Protection,
// Referrer-Policy, Permissions-Policy — are already applied at the nginx edge for ALL responses,
// including the SPA HTML. An API-level duplicate produced invalid doubled values, so it's omitted.)

// First after IP resolution: assign/echo the correlation id so it's present in HttpContext.Items
// before anything (including ExceptionMiddleware) logs.
app.UseMiddleware<CorrelationIdMiddleware>();
// One structured completion log per request (method, path, status, elapsed ms), carrying the
// CorrelationId/StaffId enrichers — request-level observability in both the file and Seq.
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Hangfire dashboard — development only; not exposed in production builds
if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");
app.MapControllers();
app.MapHub<ShiftHub>("/hubs/shift");

// Liveness has no dependency probes; readiness includes the "ready"-tagged DB check.
app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static c => c.Tags.Contains("ready") });

DependencyInjection.RegisterHangfireJobs(app.Services);

app.Run();

// Exposed so the integration test project can host the real pipeline via WebApplicationFactory<Program>.
public partial class Program;
