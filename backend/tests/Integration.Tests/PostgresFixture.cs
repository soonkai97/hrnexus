using HRNexus.Application.Common.Interfaces;
using HRNexus.Infrastructure.Auth;
using HRNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Spins up a throwaway PostgreSQL 17 container once for the whole test collection, applies the
/// real EF migrations, and hands out AppDbContext instances pointed at it. EF Core InMemory can't
/// model transactions, isolation levels, the xmin system column, or partial unique indexes — so
/// these concurrency guarantees can ONLY be proven against a real Postgres, which is what this gives.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("hrnexus_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    // Real field encryptor with a fixed test key, so the Staff PII converters work against Postgres.
    private static readonly IFieldEncryptor Encryptor = new FieldEncryptor(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pii:Key"] = "Integration_Test_PII_Key_0123456789_abcdefXYZ" })
            .Build());

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Apply the real migration history to the fresh container.
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        // The documented first-run account is created only in an EMPTY database, and other tests add staff
        // before the API starts, so seed here (while it is empty) the way a first start would.
        await SeedData.SeedDemoUsersAsync(db, new PasswordHasher());
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A fresh AppDbContext on the container. Each concurrent operation needs its OWN
    /// context — DbContext is not thread-safe — so callers building parallel races create several.
    /// Retry-on-failure is enabled to mirror production (the execution strategy retries 40001
    /// serialization failures), which is exactly the mechanism the Serializable test relies on.</summary>
    public AppDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .Options;
        return new AppDbContext(opts, new TestCurrentUser(), Encryptor);
    }

    /// <summary>A context on a DIFFERENT, throwaway database in the same container (created on first
    /// migrate), for tests that need an empty or specially prepared database.</summary>
    public AppDbContext NewDbOn(string database)
    {
        var cs = ConnectionString.Replace("Database=hrnexus_test", $"Database={database}");
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
        return new AppDbContext(opts, new TestCurrentUser(), Encryptor);
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid?   UserId   => null;
        public string? UserName => "IntegrationTest";
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
