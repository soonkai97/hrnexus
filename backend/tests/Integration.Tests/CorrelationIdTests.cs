using HRNexus.Api.Middleware;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Verifies CorrelationIdMiddleware over the real HTTP pipeline: every response carries an
/// X-Correlation-Id, and a client-supplied id is propagated back unchanged (so the frontend can
/// pin one request end-to-end).
/// </summary>
[Collection("postgres")]
public class CorrelationIdTests(PostgresFixture fx) : IAsyncLifetime
{
    private TestApiFactory _factory = default!;

    public Task InitializeAsync()
    {
        _factory = new TestApiFactory(fx.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Response_CarriesGeneratedCorrelationId_WhenNoneSupplied()
    {
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/health");

        Assert.True(res.Headers.Contains(CorrelationIdMiddleware.HeaderName),
            "response should include an X-Correlation-Id header");
        var id = res.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task Response_EchoesSuppliedCorrelationId()
    {
        var client = _factory.CreateClient();
        const string supplied = "test-correlation-12345";

        using var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add(CorrelationIdMiddleware.HeaderName, supplied);
        var res = await client.SendAsync(req);

        var id = res.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(supplied, id);
    }
}
