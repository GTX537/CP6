using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

// Creates only a fresh, locally owned identity fixture. All measured business mutations
// are subsequently performed against the real Core API by the CRM acceptance runner.
static class IdentityLiveFixture
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task InitializeAsync(string inputPath, string privateParent)
    {
        var input = JsonSerializer.Deserialize<LiveInput>(await File.ReadAllTextAsync(inputPath), Json)!;
        foreach (var origin in new[] { input.CoreOrigin, input.CrmOrigin })
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "https" || uri.AbsolutePath != "/")
                throw new InvalidOperationException("C02_LIVE_REQUIRES_HTTPS_LOOPBACK");
        if (input.SamplesPerCategory is < 1 or > 200) throw new InvalidOperationException("C02_INVALID_SAMPLE_COUNT");
        var supplied = Environment.GetEnvironmentVariable("CP6_C02_TEST_SQL") ?? throw new InvalidOperationException("C02_REAL_SQL_INPUT_MISSING");
        var sql = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master", TrustServerCertificate = true, MultipleActiveResultSets = false };
        var host = sql.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split('\\', ',')[0];
        if (!new[] { "localhost", "127.0.0.1", ".", "(local)", Environment.MachineName }.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("C02_REQUIRES_ISOLATED_LOCAL_SQL");
        var database = "CP6C02Live_" + Guid.NewGuid().ToString("N");
        var output = Path.Combine(Path.GetFullPath(privateParent), database);
        Directory.CreateDirectory(output);
        // Persist ownership before CREATE so the wrapper can clean a partially initialized run.
        await File.WriteAllTextAsync(Path.Combine(output, "ownership.json"), JsonSerializer.Serialize(new { database }, Json));
        await using (var master = new SqlConnection(sql.ConnectionString)) await master.ExecuteAsync($"CREATE DATABASE [{database}]");
        sql.InitialCatalog = database;
        var connection = sql.ConnectionString;
        await using (var migration = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection, x => x.CommandTimeout(180)).Options))
            await migration.Database.MigrateAsync();
        var hasher = new BCryptPasswordHasher(11);
        var tenants = new List<LiveTenant>();
        var menus = new[] { (9901, "crm-lead", new[] { "query", "add", "edit", "view-pii" }),
            (9902, "user", new[] { "query", "add", "edit", "delete" }), (9903, "pub-dept", new[] { "query", "add", "edit", "delete" }),
            (9904, "pub-role-perm", new[] { "query", "edit" }), (9905, "pub-data-scope", new[] { "query", "edit" }),
            (9906, "role", new[] { "query", "add", "edit", "delete" }) };
        CP6Context Create(Guid tenant) => new(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options,
            new TenantContext { CurrentTenantId = tenant });
        await using (var shared = Create(TenantContext.DefaultTenant))
        {
            if (!await shared.Sys_Tenants.AnyAsync(t => t.Id == TenantContext.DefaultTenant))
                shared.Sys_Tenants.Add(new() { Id = TenantContext.DefaultTenant, TenantCode = "c02-platform", TenantName = "C02 platform fixture", Enable = true });
            foreach (var (id, key, actions) in menus)
            {
                if (await shared.Sys_Menus.AnyAsync(m => m.MenuId == id || m.MenuKey == key))
                    throw new InvalidOperationException("C02_FRESH_MENU_ID_COLLISION");
                shared.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key, Enable = true });
                foreach (var action in actions) shared.Sys_MenuActions.Add(new() { Id = Guid.NewGuid(), MenuId = id, ActionCode = action, ActionName = action });
            }
            await shared.SaveChangesAsync();
        }
        for (var index = 0; index < 2; index++)
        {
            var id = Guid.NewGuid();
            var slug = index == 0 ? "c02-primary" : "c02-isolated";
            var root = Guid.NewGuid(); var child = Guid.NewGuid(); var target = Guid.NewGuid();
            await using var db = Create(id);
            db.Sys_Tenants.Add(new() { Id = id, TenantCode = slug, TenantName = "C02 " + slug, Enable = true, TwoFactorMode = 0, ExpireDate = DateTime.Now.AddDays(2) });
            db.Sys_Depts.AddRange(new() { Id = root, DeptCode = "ROOT", DeptName = "C02 root", Path = $"/{root:D}/" },
                new() { Id = child, ParentId = root, DeptCode = "CHILD", DeptName = "C02 child", Path = $"/{root:D}/{child:D}/" },
                new() { Id = target, DeptCode = "TARGET", DeptName = "C02 target", Path = $"/{target:D}/" });
            foreach (var role in new[] { 1, 2, 3 })
            {
                db.Sys_Roles.Add(new() { RoleId = role, RoleName = role == 1 ? "administrator" : role == 2 ? "owner" : "extra", Enable = true });
                db.Sys_RoleDataScopes.Add(new() { Id = Guid.NewGuid(), RoleId = role, ResourceKey = "crm-lead", ScopeType = 1 });
                foreach (var (menuId, _, actions) in menus.Where(m => role == 1 || m.Item1 == 9901))
                {
                    db.Sys_RoleMenus.Add(new() { RoleId = role, MenuId = menuId });
                    foreach (var action in actions.Where(a => role != 3 || a == "query"))
                        db.Sys_RoleActions.Add(new() { Id = Guid.NewGuid(), RoleId = role, MenuId = menuId, ActionCode = action });
                }
            }
            var users = new List<LiveUser>();
            foreach (var label in new[] { "administrator", "scenario" }.Concat(Enumerable.Range(0, index == 0 ? input.SamplesPerCategory : 1).Select(i => "sample-" + i.ToString("D3"))))
            {
                var user = NewUser(label, slug, id, child, label == "administrator" ? 1 : 2);
                db.Sys_Users.Add(user.Entity); db.Sys_UserRoles.Add(new() { Id = Guid.NewGuid(), UserId = user.Entity.Id, RoleId = user.Entity.RoleId!.Value });
                users.Add(user.Record);
            }
            await db.SaveChangesAsync();
            tenants.Add(new(id, slug, "local", root, child, target, "c02-reader-" + index, RandomSecret(), users.ToArray()));
        }
        LiveUser platform;
        await using (var db = Create(TenantContext.DefaultTenant))
        {
            var created = NewUser("platform-administrator", "platform", TenantContext.DefaultTenant, null, 1);
            created.Entity.IsPlatformAdmin = true; db.Sys_Users.Add(created.Entity);
            if (!await db.Sys_Roles.AnyAsync(r => r.RoleId == 1)) db.Sys_Roles.Add(new() { RoleId = 1, RoleName = "platform", Enable = true });
            db.Sys_UserRoles.Add(new() { Id = Guid.NewGuid(), UserId = created.Entity.Id, RoleId = 1 });
            await db.SaveChangesAsync(); platform = created.Record;
        }
        using var rsa = RSA.Create(2048);
        var browserSecret = RandomSecret();
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = input.CoreOrigin, ActiveKeyId = "c02-live-issuer",
            Keys = [new() { Kid = "c02-live-issuer", Pem = rsa.ExportRSAPrivateKeyPem() }],
            Clients = [new() { ClientId = "CP6.Web", SecretSha256 = CrmOidcCrypto.Hash(browserSecret),
                RedirectUris = [input.CrmOrigin + "/crm/auth/callback"], PostLogoutRedirectUris = [input.CrmOrigin + "/crm/logged-out"] }],
            Organizations = tenants.Select(t => new CrmOidcOrganization { TenantId = t.Id, Slug = t.Slug, Region = t.Region, CrmEnabled = true }).ToList(),
            ServiceClients = tenants.Select(t => new CrmOidcServiceClient { ClientId = t.ReaderClientId, TenantId = t.Id, Enabled = true,
                SecretSha256 = CrmOidcCrypto.Hash(t.ReaderSecret), AllowedScopes = ["cp6.services"] }).ToList()
        };
        oidc.Validate(true);
        var identity = new CrmIdentityOptions { Enabled = true, Issuer = oidc.Issuer, Tenants = tenants.ToDictionary(t => t.Id, t => t.Region),
            ProjectionReaderClientIds = tenants.Select(t => t.ReaderClientId).ToArray(), DaprHttpEndpoint = input.DaprHttpEndpoint, DaprGrpcEndpoint = input.DaprGrpcEndpoint };
        var runtime = new CrmIdentityRuntime(identity, new(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), oidc.Issuer));
        foreach (var tenant in tenants)
        {
            await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options,
                new TenantContext { CurrentTenantId = tenant.Id }, identity: runtime);
            await new IdentityBootstrapService(db, runtime).InitializeAsync(tenant.Id);
        }
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(output, file));
        var config = new
        {
            Startup = new { Mode = "Api", SkipDatabaseInitialization = true, SkipHostedServices = true },
            ConnectionStrings = new { DefaultConnection = connection, Redis = "" }, Kafka = new { BootstrapServers = "" },
            JWT = new { Secret = RandomSecret(), Issuer = input.CoreOrigin, Audience = "CP6.Web" },
            Security = new { Cookie = new { Secure = true, SameSite = "Strict" }, Csrf = new { Enabled = true }, Token = new { AccessTokenMinutes = 15 } },
            Cors = new { AllowedOrigins = new[] { input.CoreOrigin } },
            Storage = new { Provider = "Local", LocalRoot = Path.Combine(output, "uploads") }, Space = new { Files = new { RootPath = Path.Combine(output, "space-files") } },
            Kestrel = new { Certificates = new { Default = new { Path = input.CertificatePath, Password = input.CertificatePassword } } },
            Logging = new { LogLevel = new Dictionary<string, string> { ["Default"] = "Warning", ["Microsoft.AspNetCore"] = "Warning" } },
            CrmOidc = oidc, CrmIdentity = identity
        };
        await File.WriteAllTextAsync(Path.Combine(output, "appsettings.Local.json"), JsonSerializer.Serialize(config, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "live-fixture.json"), JsonSerializer.Serialize(new
        { database, connection, coreContentRoot = output, input.CoreOrigin, input.CrmOrigin, browserSecret, tenants, platform, input.SamplesPerCategory }, Json));
        Console.WriteLine("C02 local Core database, actual business identities and snapshot baseline initialized.");

        (Sys_User Entity, LiveUser Record) NewUser(string label, string slug, Guid tenantId, Guid? department, int role)
        {
            var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
            var user = new Sys_User { Id = Guid.NewGuid(), TenantId = tenantId, UserName = "c02-" + slug + "-" + label,
                NickName = "C02 " + label, Password = hasher.Hash(password), PasswordChangedAt = DateTime.Now.AddSeconds(-2),
                Enable = true, MustChangePassword = false, RoleId = role, DeptId = department };
            return (user, new(user.Id, label, user.UserName, password, role, department));
        }
    }

    public static async Task DispatchAsync(string configPath)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(configPath), optional: false).Build();
        var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        if (!System.Text.RegularExpressions.Regex.IsMatch(connection.InitialCatalog, "^CP6C02Live_[a-f0-9]{32}$") ||
            Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(configPath))) != connection.InitialCatalog)
            throw new InvalidOperationException("C02_OWNED_DISPATCH_FIXTURE_REQUIRED");
        var options = config.GetSection("CrmIdentity").Get<CrmIdentityOptions>()!;
        var runtime = new CrmIdentityRuntime(options, new(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), options.Issuer));
        using var worker = new IdentityEventDispatchWorker(runtime, config, NullLogger<IdentityEventDispatchWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        Console.WriteLine("C02 actual priority and ordinary dispatch loops started.");
        await worker.ExecuteTask!;
    }

    public static async Task CleanupAsync(string privateRoot)
    {
        var root = Path.GetFullPath(privateRoot);
        var supplied = Environment.GetEnvironmentVariable("CP6_C02_TEST_SQL") ?? throw new InvalidOperationException("C02_REAL_SQL_INPUT_MISSING");
        var sql = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master", Pooling = false, TrustServerCertificate = true };
        var host = sql.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split('\\', ',')[0];
        if (!new[] { "localhost", "127.0.0.1", ".", "(local)", Environment.MachineName }.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("C02_REQUIRES_ISOLATED_LOCAL_SQL");
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in Directory.GetDirectories(root, "CP6C02Live_*"))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "ownership.json")));
            var name = document.RootElement.GetProperty("database").GetString()!;
            if (Path.GetFileName(directory) != name || !System.Text.RegularExpressions.Regex.IsMatch(name, "^CP6C02Live_[a-f0-9]{32}$"))
                throw new InvalidOperationException("C02_INVALID_DATABASE_OWNERSHIP");
            owned.Add(name);
        }
        var crm = Path.Combine(root, "crm-owned-databases.json");
        if (File.Exists(crm)) foreach (var name in JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(crm))!)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^CP6CrmC02Live_[a-f0-9]{32}$"))
                throw new InvalidOperationException("C02_INVALID_DATABASE_OWNERSHIP");
            owned.Add(name);
        }
        var failures = new List<Exception>();
        foreach (var name in owned)
        {
            try
            {
                await using var db = new SqlConnection(sql.ConnectionString);
                try { await db.ExecuteAsync($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", commandTimeout: 90); }
                catch (SqlException ex) when (ex.Number == -2)
                { if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@name", new { name }) != 0) throw; }
                if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@name", new { name }) != 0)
                    throw new InvalidOperationException("C02_DATABASE_CLEANUP_INCOMPLETE");
            }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
        Console.WriteLine($"C02 verified absence of {owned.Count} owned fixture databases.");
    }

    private static string RandomSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private sealed record LiveInput(string CoreOrigin, string CrmOrigin, string DaprHttpEndpoint, string DaprGrpcEndpoint,
        string CertificatePath, string CertificatePassword, int SamplesPerCategory = 100);
    private sealed record LiveTenant(Guid Id, string Slug, string Region, Guid RootDepartmentId, Guid ChildDepartmentId,
        Guid TargetDepartmentId, string ReaderClientId, string ReaderSecret, LiveUser[] Users);
    private sealed record LiveUser(Guid Id, string Label, string UserName, string Password, int RoleId, Guid? DepartmentId);
}
