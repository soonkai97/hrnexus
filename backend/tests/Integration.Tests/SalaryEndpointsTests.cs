using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRNexus.Integration.Tests;

/// <summary>
/// Phase 8a against REAL Postgres: salary is only visible with a payroll permission, history is
/// append-only, and salary and bank account are encrypted at rest (the raw column never holds the number).
/// </summary>
[Collection("postgres")]
public class SalaryEndpointsTests(PostgresFixture fx) : IAsyncLifetime
{
    private TestApiFactory _factory = default!;

    public Task InitializeAsync() { _factory = new TestApiFactory(fx.ConnectionString); return Task.CompletedTask; }
    public Task DisposeAsync()    { _factory.Dispose(); return Task.CompletedTask; }

    private async Task<HttpClient> LoginAsync(string staffId, string password, string ip)
    {
        var client = _factory.CreateClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { staffId, password, portal = (string?)null }),
        };
        login.Headers.Add("X-Test-Ip", ip);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);
        return client;
    }

    /// <summary>Hires a plain employee (no payroll permission) and returns their id.</summary>
    private static async Task<(Guid Id, string StaffId)> HireEmployeeAsync(HttpClient admin)
    {
        // A fresh custom job: it carries no permissions, so the person has no payroll access.
        var jobName = $"SALJOB{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/staff/roles", new { name = jobName })).StatusCode);
        var roles = JsonDocument.Parse(await admin.GetStringAsync("/api/staff/roles")).RootElement;
        var employeeRole = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == jobName).GetProperty("id").GetGuid();
        var staffId = $"SAL{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var create = await admin.PostAsJsonAsync("/api/staff", new
        {
            staffId, firstName = "Pay", lastName = "Test", email = (string?)null, password = "Test@1234", roles = new[] { employeeRole },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        return (id, staffId);
    }

    [Fact]
    public async Task SalaryIsInvisibleWithoutAPayrollPermission_AndVisibleWithOne()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.8.1");
        var (id, staffId) = await HireEmployeeAsync(admin);
        var add = await admin.PostAsJsonAsync($"/api/staff/{id}/salary", new
        {
            basicSalary = 3500m, payFrequency = "monthly", effectiveFrom = "2026-01-01",
            bankName = "Maybank", bankAccount = "1234567890", bankHolder = "Pay Test",
        });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        var employee = await LoginAsync(staffId, "Test@1234", "10.0.8.2");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync($"/api/staff/{id}/salary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync($"/api/staff/{id}/salary", new
        {
            basicSalary = 9999m, payFrequency = "monthly", effectiveFrom = "2026-02-01",
        })).StatusCode);

        var seen = JsonDocument.Parse(await admin.GetStringAsync($"/api/staff/{id}/salary")).RootElement;
        Assert.Equal(3500m, seen.GetProperty("current").GetProperty("basicSalary").GetDecimal());

        // The ordinary staff detail never carries salary: it stays masked for everyone without payroll.view.
        var detail = await admin.GetStringAsync($"/api/staff/{id}");
        Assert.DoesNotContain("basicSalary", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task History_IsAppendOnly_ADuplicateEffectiveDateIsAConflict()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.8.3");
        var (id, _) = await HireEmployeeAsync(admin);
        object Body(decimal salary) => new { basicSalary = salary, payFrequency = "monthly", effectiveFrom = "2026-01-01" };

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/staff/{id}/salary", Body(3000m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/staff/{id}/salary", Body(4000m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/staff/{id}/salary", new
        {
            basicSalary = 3000m, payFrequency = "weekly", effectiveFrom = "2026-03-01",
        })).StatusCode);

        var history = JsonDocument.Parse(await admin.GetStringAsync($"/api/staff/{id}/salary")).RootElement.GetProperty("history");
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(3000m, history[0].GetProperty("basicSalary").GetDecimal());
    }

    [Fact]
    public async Task SalaryAndBankAccount_AreEncryptedAtRest()
    {
        var admin = await LoginAsync("HRADMIN", "Admin@1234", "10.0.8.4");
        var (id, _) = await HireEmployeeAsync(admin);
        await admin.PostAsJsonAsync($"/api/staff/{id}/salary", new
        {
            basicSalary = 4567.89m, payFrequency = "monthly", effectiveFrom = "2026-01-01",
            bankName = "CIMB", bankAccount = "9876543210", bankHolder = "Pay Test",
        });

        await using var db = fx.NewDb();
        var raw = await db.Database
            .SqlQuery<string>($"""SELECT "BasicSalary" AS "Value" FROM "SalaryProfiles" WHERE "EmployeeId" = {id}""")
            .SingleAsync();
        var rawAccount = await db.Database
            .SqlQuery<string>($"""SELECT "BankAccount" AS "Value" FROM "SalaryProfiles" WHERE "EmployeeId" = {id}""")
            .SingleAsync();

        Assert.DoesNotContain("4567", raw);
        Assert.DoesNotContain("9876543210", rawAccount);
        Assert.StartsWith("enc1:", raw);
        Assert.StartsWith("enc1:", rawAccount);
    }
}
