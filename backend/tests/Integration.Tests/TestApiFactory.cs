using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Hosts the REAL API pipeline (controllers, ExceptionMiddleware, rate limiter, auth, MediatR)
/// in-memory via WebApplicationFactory, pointed at the throwaway Postgres container. This is the
/// only way to exercise behaviour that lives in the HTTP pipeline rather than a handler — the
/// per-IP rate limiter and the exception→status-code mapping.
/// </summary>
public sealed class TestApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the app at the test container instead of the docker-compose 'postgres' host.
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        // Force a small auth rate-limit window so the flood test deterministically trips 429
        // (10 per minute). The lockout test uses a distinct IP and only 6 requests, so it stays
        // under this and continues to exercise the per-account lockout path, not the IP limiter.
        builder.UseSetting("RateLimit:AuthPermitLimit", "10");
        builder.UseSetting("RateLimit:AuthWindowSeconds", "60");

        builder.ConfigureTestServices(services =>
        {
            // Lets each test pick its own rate-limit partition by sending an X-Test-Ip header,
            // so the flood test can't starve the other tests' per-IP budget within the window.
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, FakeRemoteIpStartupFilter>();
        });
    }

    private sealed class FakeRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMw) =>
            {
                var ip = ctx.Request.Headers["X-Test-Ip"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out var addr))
                    ctx.Connection.RemoteIpAddress = addr;
                await nextMw();
            });
            next(app);
        };
    }
}
