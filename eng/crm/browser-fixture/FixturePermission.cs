using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Test-only administrative mutation. It cannot target ordinary tenant databases.
internal static class FixturePermission
{
    public static async Task SetPiiAsync(string fixturePath, string role, string value)
    {
        if (role != "owner" || !bool.TryParse(value, out var enabled)) throw new ArgumentException("Only the fixture owner PII action can be changed.");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var root = fixture.RootElement;
        var databaseName = root.GetProperty("DatabaseName").GetString()!;
        if (!Regex.IsMatch(databaseName, "^CP6OidcTest_[0-9a-f]{32}$") ||
            Path.GetFileName(fixturePath) != "browser-fixture.json" ||
            Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(fixturePath))) != databaseName)
            throw new ArgumentException("Expected a generated local browser fixture.");
        var organizationId = root.GetProperty("OrganizationId").GetGuid();
        var userId = root.GetProperty("Users").EnumerateArray().Single(u => u.GetProperty("Role").GetString() == role).GetProperty("Id").GetGuid();
        var configured = Environment.GetEnvironmentVariable("CP6_OIDC_TEST_SQL") ?? throw new ArgumentException("Explicit test SQL connection required.");
        var connection = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };
        var tenant = new TenantContext { CurrentTenantId = organizationId };
        await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection.ConnectionString).Options, tenant);
        if (!await db.Sys_Tenants.AnyAsync(t => t.Id == organizationId && t.TenantName == "CRM Browser Fixture"))
            throw new ArgumentException("Database does not contain the generated fixture organization.");
        var user = await db.Sys_Users.SingleAsync(u => u.Id == userId && u.UserName == "crm-owner" && u.RoleId == 2);
        if (!await db.Sys_Roles.AnyAsync(r => r.RoleId == user.RoleId && r.RoleName == "owner")) throw new ArgumentException("Fixture role differs.");
        var action = db.Sys_RoleActions.Where(a => a.RoleId == user.RoleId && a.MenuId == 910 && a.ActionCode == "view-pii");
        if (enabled)
        {
            if (!await action.AnyAsync()) { db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 2, MenuId = 910, ActionCode = "view-pii" }); await db.SaveChangesAsync(); }
        }
        else await action.ExecuteDeleteAsync();
        Console.WriteLine("Local fixture owner PII permission updated.");
    }
}
