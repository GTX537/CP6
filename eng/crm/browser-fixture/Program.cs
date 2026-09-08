using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Migrations;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Explicit local fixture; starts no server and creates no authenticated session.
if (args.Length == 4 && args[0] == "--set-fixture-pii")
{
    await FixturePermission.SetPiiAsync(args[1], args[2], args[3]);
    return;
}
if (args.Length != 2)
    throw new ArgumentException("Usage: browser-fixture <settings.json> <output-parent-directory>");
var input = JsonSerializer.Deserialize<FixtureSettings>(await File.ReadAllTextAsync(args[0]),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("Invalid settings.");
foreach (var raw in new[] { input.CoreOrigin, input.CrmCallbackUri, input.CrmPostLogoutUri })
    if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !(uri.IsLoopback || uri.Host.EndsWith(".dev.localhost", StringComparison.OrdinalIgnoreCase)) || uri.Scheme != "https")
        throw new ArgumentException("Fixture endpoints must be explicit HTTPS loopback URLs.");
if (input.OrganizationId == Guid.Empty) throw new ArgumentException("OrganizationId must identify the test organization.");
var sql = Environment.GetEnvironmentVariable("CP6_OIDC_TEST_SQL")
    ?? throw new ArgumentException("CP6_OIDC_TEST_SQL must point to the isolated test SQL Server.");
var databaseName = "CP6OidcTest_" + Guid.NewGuid().ToString("N");
var output = Path.Combine(Path.GetFullPath(args[1]), databaseName);
Directory.CreateDirectory(output);
using var rsa = RSA.Create(2048);
var clientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
var oidc = new CrmOidcOptions
{
    Enabled = true, Issuer = input.CoreOrigin, ActiveKeyId = "local-browser-fixture",
    Keys = [new() { Kid = "local-browser-fixture", Pem = rsa.ExportRSAPrivateKeyPem() }],
    Clients = [new() { ClientId = "CP6.Web", SecretSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret))),
        RedirectUris = [input.CrmCallbackUri], PostLogoutRedirectUris = [input.CrmPostLogoutUri] }],
    Organizations = [new() { TenantId = input.OrganizationId, Slug = input.OrganizationSlug, Region = input.Region, CrmEnabled = true }]
};
oidc.Validate(development: true);
var connection = new SqlConnectionStringBuilder(sql) { InitialCatalog = databaseName };
var master = new SqlConnectionStringBuilder(sql) { InitialCatalog = "master" };
await using (var admin = new SqlConnection(master.ConnectionString))
{
    await admin.OpenAsync();
    await using var create = admin.CreateCommand();
    // Identifier comes only from a generated GUID, never user input.
    create.CommandText = $"CREATE DATABASE [{databaseName}]";
    await create.ExecuteNonQueryAsync();
}
var tenant = new TenantContext { CurrentTenantId = input.OrganizationId };
await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
    .UseSqlServer(connection.ConnectionString, o => o.CommandTimeout(120)).Options, tenant);
// Current complete EF schema supports real auth/menu/audit/SSO configuration reads.
// Upgrade compatibility is tested separately; this is a new disposable database.
await db.Database.EnsureCreatedAsync();
await db.Database.ExecuteSqlRawAsync(CrmOidcGrantStore.CreateSql);
db.Sys_Tenants.Add(new() { Id = input.OrganizationId, TenantCode = input.OrganizationSlug,
    TenantName = "CRM Browser Fixture", Enable = true, TwoFactorMode = 0, ExpireDate = DateTime.Now.AddDays(2) });
var rootDept = Guid.NewGuid();
var childDept = Guid.NewGuid();
var otherDept = Guid.NewGuid();
db.Sys_Depts.AddRange(
    new() { Id = rootDept, DeptCode = "SALES", DeptName = "Sales", Path = $"/{rootDept}/", Enable = true },
    new() { Id = childDept, ParentId = rootDept, DeptCode = "SALES-CHILD", DeptName = "Sales Child", Path = $"/{rootDept}/{childDept}/", Enable = true },
    new() { Id = otherDept, DeptCode = "OTHER", DeptName = "Other Department", Path = $"/{otherDept}/", Enable = true });
db.Sys_Menus.AddRange(
    new() { MenuId = 10, MenuKey = "dashboard", MenuName = "Dashboard", RoutePath = "/dashboard", Enable = true },
    new() { MenuId = 910, MenuKey = "crm-lead", MenuName = "CRM Leads", Enable = true },
    new() { MenuId = 911, MenuKey = "crm-site", MenuName = "CRM Site", Enable = true });
foreach (var action in new[] { "query", "add", "edit", "assign", "view-pii" })
    db.Sys_MenuActions.Add(new() { Id = Guid.NewGuid(), MenuId = 910, ActionCode = action, ActionName = action });
foreach (var action in new[] { "query", "configure" })
{
    db.Sys_MenuActions.Add(new() { Id = Guid.NewGuid(), MenuId = 911, ActionCode = action, ActionName = action });
    db.Sys_RoleActions.Add(new() { Id = Guid.NewGuid(), RoleId = 1, MenuId = 911, ActionCode = action });
}
db.Sys_RoleMenus.Add(new() { RoleId = 1, MenuId = 911 });
var users = new List<object>();
var hasher = new BCryptPasswordHasher(11);
foreach (var (roleId, name, scope, dept, actions) in new[]
{
    (1, "supervisor", 5, rootDept, new[] { "query", "add", "edit", "assign", "view-pii" }),
    (2, "owner", 1, childDept, new[] { "query", "add", "edit", "view-pii" }),
    (3, "queryonly", 1, rootDept, new[] { "query" }),
    (4, "otherowner", 1, otherDept, new[] { "query", "add", "edit", "view-pii" }),
    (5, "masked", 1, rootDept, new[] { "query", "edit" })
})
{
    var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
    var user = new Sys_User { Id = Guid.NewGuid(), UserName = "crm-" + name, NickName = "CRM " + name,
        Password = hasher.Hash(password), PasswordChangedAt = DateTime.Now, Enable = true,
        MustChangePassword = false, RoleId = roleId, DeptId = dept };
    db.Sys_Users.Add(user);
    db.Sys_Roles.Add(new() { RoleId = roleId, RoleName = name, Enable = true });
    db.Sys_UserRoles.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, RoleId = roleId });
    db.Sys_RoleMenus.AddRange(new() { RoleId = roleId, MenuId = 10 }, new() { RoleId = roleId, MenuId = 910 });
    foreach (var action in actions)
        db.Sys_RoleActions.Add(new() { Id = Guid.NewGuid(), RoleId = roleId, MenuId = 910, ActionCode = action });
    db.Sys_RoleDataScopes.Add(new() { Id = Guid.NewGuid(), RoleId = roleId, ResourceKey = "crm-lead", ScopeType = scope });
    users.Add(new { user.Id, user.UserName, Password = password, Role = name, DepartmentId = dept });
}
await db.SaveChangesAsync();
var local = new
{
    Startup = new { Mode = "Api", SkipDatabaseInitialization = true, SkipHostedServices = true },
    ConnectionStrings = new { DefaultConnection = connection.ConnectionString, Redis = "" },
    Kafka = new { BootstrapServers = "" }, // Exercise the supported SQL audit sink in this local fixture.
    JWT = new { Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), Issuer = input.CoreOrigin, Audience = "CP6.Web" },
    Security = new { Cookie = new { Secure = true, SameSite = "Strict" }, Csrf = new { Enabled = true }, Token = new { AccessTokenMinutes = 15 } },
    Cors = new { AllowedOrigins = new[] { input.CoreOrigin } },
    Storage = new { Provider = "Local", LocalRoot = Path.Combine(output, "uploads") },
    Space = new { Files = new { RootPath = Path.Combine(output, "space-files") } },
    CrmOidc = oidc
};
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(Path.Combine(output, "appsettings.Local.json"), JsonSerializer.Serialize(local, jsonOptions));
await File.WriteAllTextAsync(Path.Combine(output, "browser-fixture.json"), JsonSerializer.Serialize(new
{
    DatabaseName = databaseName, input.OrganizationId, input.OrganizationSlug, input.Region,
    input.CoreOrigin, input.CrmCallbackUri, input.CrmPostLogoutUri, ClientId = "CP6.Web", ClientSecret = clientSecret,
    Users = users, RootDepartmentId = rootDept, ChildDepartmentId = childDept, OtherDepartmentId = otherDept
}, jsonOptions));
// Preserve the WebApi's non-secret defaults when running with this isolated content root.
foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
    if (File.Exists(Path.Combine(AppContext.BaseDirectory, file)))
        File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(output, file));
Console.WriteLine($"Created isolated database {databaseName}.");
Console.WriteLine($"Fixture files: {output}");
Console.WriteLine("Credentials and key material are only in fixture files. Start the real WebApi in Development using this directory as its content root.");

internal sealed record FixtureSettings(string CoreOrigin, string CrmCallbackUri, string CrmPostLogoutUri,
    Guid OrganizationId, string OrganizationSlug, string Region);
