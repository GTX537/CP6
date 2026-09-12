using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Sys;
using CP6.Entity;
using CP6.Entity.DomainModels.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpLive.Fixture;

internal static partial class ErpLiveFixture
{
    public static async Task InitializeAsync(string inputPath, string privateParent, CancellationToken ct)
    {
        var input = System.Text.Json.JsonSerializer.Deserialize<LiveInput>(await File.ReadAllTextAsync(inputPath, ct), Json)
            ?? throw new FixtureException("C03_LIVE_INPUT_REQUIRED");
        ValidateOrigin(input.CoreOrigin, httpsOnly: true);
        ValidateOrigin(input.CrmOrigin, httpsOnly: true);
        ValidateOrigin(input.DaprHttpEndpoint, httpsOnly: false);
        ValidateOrigin(input.DaprGrpcEndpoint, httpsOnly: false);
        if (input.CoreOrigin == input.CrmOrigin || input.DaprAppToken is not { Length: >= 32 and <= 4096 } ||
            input.DaprApiToken is not { Length: >= 32 and <= 4096 } || input.DaprApiToken == input.DaprAppToken ||
            input.DaprApiToken.Any(char.IsControl) || input.DaprAppToken.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(input.CertificatePath) || !File.Exists(input.CertificatePath))
            throw new FixtureException("C03_LIVE_ENDPOINT_TOKEN_OR_CERTIFICATE_INVALID");

        var parent = RequirePrivateDirectory(privateParent);
        var sql = MasterConnection();
        var database = "CP6C03Live_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(parent, database);
        if (Directory.Exists(directory)) throw new FixtureException("C03_FRESH_FIXTURE_DIRECTORY_REQUIRED");
        Directory.CreateDirectory(directory);
        var tenantIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var ownership = new DatabaseOwnership(OwnershipSchema, database, sql.DataSource,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), DateTimeOffset.UtcNow, tenantIds);
        // CREATE fails for an existing database. The database marker independently validates cleanup ownership.
        await WriteJsonAsync(Path.Combine(directory, "ownership.json"), ownership, ct);
        await using (var master = new SqlConnection(sql.ConnectionString))
        {
            await master.ExecuteAsync(new CommandDefinition($"CREATE DATABASE [{database}]", commandTimeout: 90, cancellationToken: ct));
            await master.ExecuteAsync(new CommandDefinition(
                $"EXEC [{database}].sys.sp_addextendedproperty @name=@property, @value=@token",
                new { property = OwnershipProperty, token = ownership.OwnerToken }, commandTimeout: 30, cancellationToken: ct));
            await master.ExecuteAsync(new CommandDefinition($"ALTER DATABASE [{database}] SET ALLOW_SNAPSHOT_ISOLATION ON",
                commandTimeout: 60, cancellationToken: ct));
        }
        var connection = OwnedConnection(ownership);
        string[] migrations;
        await using (var migration = BusinessContext(connection, tenantIds[0]))
        {
            await migration.Database.MigrateAsync(ct);
            migrations = (await migration.Database.GetAppliedMigrationsAsync(ct)).ToArray();
            if (migrations.Length == 0 || (await migration.Database.GetPendingMigrationsAsync(ct)).Any())
                throw new FixtureException("C03_FULL_CORE_MIGRATION_REQUIRED");
        }

        var hasher = new BCryptPasswordHasher(11);
        var tenants = new List<LiveTenant>();
        for (var index = 0; index < tenantIds.Length; index++)
            tenants.Add(await SeedTenantAsync(connection, tenantIds[index], index, hasher, ct));

        using var rsa = RSA.Create(2048);
        var browserSecret = RandomSecret();
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = input.CoreOrigin, ActiveKeyId = "c03-live-issuer",
            Keys = [new() { Kid = "c03-live-issuer", Pem = rsa.ExportRSAPrivateKeyPem() }],
            Clients = [new() { ClientId = "CP6.Web", SecretSha256 = CrmOidcCrypto.Hash(browserSecret),
                RedirectUris = [input.CrmOrigin + "/crm/auth/callback"],
                PostLogoutRedirectUris = [input.CrmOrigin + "/crm/logged-out"] }],
            Organizations = tenants.Select(t => new CrmOidcOrganization
                { TenantId = t.Id, Slug = t.Slug, Region = t.Region, CrmEnabled = true }).ToList(),
            ServiceClients = tenants.Select(t => new CrmOidcServiceClient
            {
                ClientId = t.ReaderClientId, TenantId = t.Id, Enabled = true,
                SecretSha256 = CrmOidcCrypto.Hash(t.ReaderSecret), AllowedScopes = ["cp6.services"]
            }).ToList()
        };
        oidc.Validate(development: true);
        var identity = new CrmIdentityOptions
        {
            Enabled = true, Issuer = input.CoreOrigin, Tenants = tenants.ToDictionary(t => t.Id, t => t.Region),
            ProjectionReaderClientIds = tenants.Select(t => t.ReaderClientId).ToArray(),
            DaprHttpEndpoint = input.DaprHttpEndpoint, DaprGrpcEndpoint = input.DaprGrpcEndpoint
        };
        var erp = new ErpIntegrationOptions
        {
            Enabled = true, Tenants = tenants.ToDictionary(t => t.Id, t => t.Region),
            ReaderClientIds = tenants.Select(t => t.ReaderClientId).ToArray(),
            DaprHttpEndpoint = input.DaprHttpEndpoint, DaprGrpcEndpoint = input.DaprGrpcEndpoint,
            DaprApiToken = input.DaprApiToken, DaprAppToken = input.DaprAppToken,
            MaxInboxAttempts = 3, InitialRetrySeconds = 1, MaximumRetrySeconds = 8
        };
        _ = new ErpIntegrationRuntime(erp, new ErpEventValidator(Cp6ContractBundle.Load(ContractDirectory("erp"))));
        var identityRuntime = new CrmIdentityRuntime(identity,
            new IdentityEventValidator(Cp6ContractBundle.Load(ContractDirectory("platform")), oidc.Issuer));
        foreach (var tenant in tenants)
        {
            await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options,
                new TenantContext { CurrentTenantId = tenant.Id }, identity: identityRuntime);
            await new IdentityBootstrapService(db, identityRuntime).InitializeAsync(tenant.Id, ct);
        }

        foreach (var name in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var source = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(directory, name), overwrite: false);
        }
        var config = new
        {
            Startup = new { Mode = "Api", SkipDatabaseInitialization = true, SkipHostedServices = true },
            ConnectionStrings = new { DefaultConnection = connection, Redis = "" }, Kafka = new { BootstrapServers = "" },
            JWT = new { Secret = RandomSecret(), Issuer = input.CoreOrigin, Audience = "CP6.Web" },
            Security = new { Cookie = new { Secure = true, SameSite = "Strict" }, Csrf = new { Enabled = true },
                Token = new { AccessTokenMinutes = 15 }, Password = new { ExpiryDays = 0 } },
            Cors = new { AllowedOrigins = new[] { input.CoreOrigin } },
            Storage = new { Provider = "Local", LocalRoot = Path.Combine(directory, "uploads") },
            Space = new { Files = new { RootPath = Path.Combine(directory, "space-files") } },
            WmsBridge = new { Enabled = false }, MesBridge = new { Enabled = false },
            Kestrel = new { Certificates = new { Default = new { Path = input.CertificatePath, Password = input.CertificatePassword } } },
            Logging = new { LogLevel = new Dictionary<string, string>
                { ["Default"] = "Warning", ["Microsoft.AspNetCore"] = "Warning", ["Microsoft.EntityFrameworkCore.Database.Command"] = "Warning" } },
            CrmOidc = oidc, CrmIdentity = identity, ErpIntegration = erp
        };
        await WriteJsonAsync(Path.Combine(directory, "appsettings.Local.json"), config, ct);
        var canonical = await ReadCanonicalAsync(connection, tenantIds, ct);
        var baseline = new BaselineAuthority("real-sql-server-migrated-erp-fixture",
            Hash(await File.ReadAllBytesAsync(typeof(CP6Context).Assembly.Location, ct)),
            Hash(await File.ReadAllBytesAsync(typeof(CrmOidcOptions).Assembly.Location, ct)),
            Hash(await File.ReadAllBytesAsync(Path.Combine(ContractDirectory("erp"), Cp6ContractBundle.IndexFileName), ct)),
            Hash(await File.ReadAllBytesAsync(Path.Combine(ContractDirectory("platform"), Cp6ContractBundle.IndexFileName), ct)),
            migrations, Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { migrations, canonical }, Json)), DateTimeOffset.UtcNow);
        var document = new LiveFixtureDocument(FixtureSchema, database, connection, directory, input.CoreOrigin,
            input.CrmOrigin, "cp6-core", browserSecret, tenants.ToArray(), baseline);
        await WriteJsonAsync(Path.Combine(directory, "live-fixture.json"), document, ct);
        Console.WriteLine("C03 owned ERP fixture initialized. Credentials are stored only in private fixture files.");
        Console.WriteLine(Path.Combine(directory, "live-fixture.json"));
    }

    private static async Task<LiveTenant> SeedTenantAsync(string connection, Guid tenantId, int index,
        BCryptPasswordHasher hasher, CancellationToken ct)
    {
        var slug = index == 0 ? "c03-primary" : "c03-isolated";
        var accountId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        var userId = Guid.NewGuid();
        var userName = "c03-erp-staff-" + index;
        var user = new LiveUser(userId, "erp-staff", userName, password, 1, departmentId, "B01", "S01", "S02");
        await using var db = BusinessContext(connection, tenantId);
        db.Sys_Tenants.Add(new() { Id = tenantId, TenantCode = slug, TenantName = "C03 isolated ERP fixture",
            Enable = true, TwoFactorMode = 0, ExpireDate = DateTime.UtcNow.AddDays(2) });
        db.Sys_Depts.Add(new() { Id = departmentId, DeptCode = "ERP", DeptName = "Fixture ERP department",
            Path = $"/{departmentId:D}/", LeaderId = userId, Enable = true });
        db.Sys_Roles.Add(new() { RoleId = 1, RoleName = "ERP fixture staff", Enable = true });
        db.Sys_Users.Add(new()
        {
            Id = userId, UserName = userName, NickName = "ERP fixture staff", Password = hasher.Hash(password),
            PasswordChangedAt = DateTime.UtcNow.AddSeconds(-2), Enable = true, MustChangePassword = false,
            RoleId = 1, DeptId = departmentId
        });
        db.Sys_UserRoles.Add(new() { Id = Guid.NewGuid(), UserId = userId, RoleId = 1 });
        await db.SaveChangesAsync(ct);
        await SeedPermissionsAsync(db, ct);
        db.MasterBases.Add(new MasterBase { BaseCd = user.BaseCode, BaseName = "C03 ERP base" });
        db.MasterStaffs.AddRange(
            new MasterStaff { StaffCd = user.SalesStaffCode, StaffName = "C03 sales staff", BaseCd = user.BaseCode, SysUserId = user.Id },
            new MasterStaff { StaffCd = user.BusinessStaffCode, StaffName = "C03 business staff", BaseCd = user.BaseCode, SysUserId = user.Id });
        await db.SaveChangesAsync(ct);

        var actor = userId.ToString("D");
        var partnerKey = "BP" + Guid.NewGuid().ToString("N")[..12];
        // A single legal partner may serve as its own AR/billing/receipt partner. No dangling fake AR master.
        await new BusinessPartnerService(db).CreateAsync(new()
        {
            BpCd = partnerKey, BpName = "C03 ERP-owned customer " + index, BaseCd = user.BaseCode,
            CustomerFlg = true, AccountsReceivableFlg = true, BillingFlg = true, ReceiptFlg = true,
            AccountsReceivableCd = partnerKey, BillingCd = partnerKey, ReceiptCd = partnerKey,
            SalesStaffCd = user.SalesStaffCode, BusinessStaffCd = user.BusinessStaffCode
        }, actor, preRegister: true);
        var partner = await db.BusinessPartners.SingleAsync(x => x.BpCd == partnerKey, ct);
        var currency = index == 0 ? "JPY" : "USD";
        var commerce = new ErpCommerceAuthority(db);
        await commerce.SetBusinessPartnerProfileAsync(partnerKey, new(Version(partner), accountId, currency, false), actor, ct);
        await db.Entry(partner).ReloadAsync(ct);
        if (partner.Status != 0) throw new FixtureException("C03_PREREGISTRATION_SEED_INVALID");
        if (currency != "JPY")
            await new FxRateService(db).CreateAsync(new() { CurrencyCd = currency, RateDate = DateTime.UtcNow.Date, Rate = 150m }, actor, ct);

        var quotationKey = await new QuotationService(db).CreateAsync(new()
        {
            BaseCd = user.BaseCode, StaffCd = user.SalesStaffCode, CustomerCd = partnerKey,
            CustomerName = partner.BpName, EstimateCheckFlg = 9,
            Details = [new() { DetailNo = 1, ItemName1 = "C03 packaging fixture", Quantity = 2m, UnitPrice = 150m, Unit = "PCS" }]
        }, actor);
        var quotation = await db.Quotations.Include(q => q.Details).SingleAsync(q => q.QtnNo == quotationKey, ct);
        await commerce.SetQuotationTermsAsync(quotationKey,
            new(Version(quotation), currency, DateTimeOffset.UtcNow.AddDays(7), "10", DateTime.UtcNow.AddDays(3).Date), actor, ct);
        await db.Entry(quotation).ReloadAsync(ct);
        await commerce.AcceptQuotationAsync(quotationKey,
            new(Version(quotation), "c03-fixture-customer-confirmation-" + Guid.NewGuid().ToString("N")), actor, ct);
        await db.Entry(quotation).ReloadAsync(ct);
        if (!ErpQuotationAcceptance.IsCurrent(quotation, DateTimeOffset.UtcNow))
            throw new FixtureException("C03_EXPLICIT_QUOTATION_ACCEPTANCE_SEED_INVALID");
        var productKey = "P" + Guid.NewGuid().ToString("N")[..15];
        var product = new ProductMaster
        {
            ProductCd = productKey, ItemCd = "C03ITEM" + index, SetProductCd = productKey,
            CustomerCd = partnerKey, QuotationNo = quotationKey, Branch1 = "0001", SalesPriceDiv = "2",
            Status = 1, WfApprovalFlg = true, CpItemName1 = "C03 approved packaging fixture", QtyUnit = "PCS", UnitPriceUnit = "PCS",
            Creator = actor, CreateDate = DateTime.UtcNow
        };
        db.ProductMasters.Add(product);
        await db.SaveChangesAsync(ct);
        await db.Entry(product).ReloadAsync(ct);
        return new(tenantId, slug, "local", accountId, departmentId, "c03-reader-" + index, RandomSecret(), [user],
            new(partner.Id, partner.BpCd, Convert.ToBase64String(Version(partner)), partner.Status, accountId, partner.IsFrozen, currency),
            new(quotation.Id, quotation.QtnNo, Convert.ToBase64String(Version(quotation)), partnerKey,
                quotation.TotalAmount!.Value, currency, quotation.ValidUntilUtc!.Value, quotation.CustomerAcceptedAtUtc!.Value,
                quotation.AcceptedContentSha256!, 1, 2m, 150m),
            new(product.Id, product.ProductCd, Convert.ToBase64String(Version(product)), quotationKey, "0001", product.Status, product.WfApprovalFlg));
    }

    private static async Task SeedPermissionsAsync(CP6Context db, CancellationToken ct)
    {
        var permissions = new[]
        {
            (Key: "erp-business-partner", Actions: new[] { "query", "add", "edit" }),
            (Key: "erp-quotation", Actions: new[] { "query", "add", "edit", "confirm" }),
            (Key: "erp-order", Actions: new[] { "query", "add", "edit" })
        };
        foreach (var item in permissions)
        {
            var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == item.Key, ct);
            if (menu is null)
            {
                menu = new Sys_Menu { MenuId = Math.Max(9900, await db.Sys_Menus.MaxAsync(x => (int?)x.MenuId, ct) ?? 0) + 1,
                    MenuKey = item.Key, MenuName = item.Key, Enable = true };
                db.Sys_Menus.Add(menu);
                await db.SaveChangesAsync(ct);
            }
            db.Sys_RoleMenus.Add(new() { RoleId = 1, MenuId = menu.MenuId });
            db.Sys_RoleDataScopes.Add(new() { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = item.Key, ScopeType = 5 });
            foreach (var action in item.Actions)
            {
                if (!await db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == action, ct))
                    db.Sys_MenuActions.Add(new() { Id = Guid.NewGuid(), MenuId = menu.MenuId, ActionCode = action, ActionName = action });
                db.Sys_RoleActions.Add(new() { Id = Guid.NewGuid(), RoleId = 1, MenuId = menu.MenuId, ActionCode = action });
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private static CP6Context BusinessContext(string connection, Guid tenant)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection, o => o.CommandTimeout(180)).Options,
            new TenantContext { CurrentTenantId = tenant });
    private static byte[] Version(BaseBizEntity entity)
        => entity.RowVersion is { Length: 8 } value ? value.ToArray() : throw new FixtureException("C03_REAL_SQL_ROWVERSION_REQUIRED");
    private static string ContractDirectory(string kind) => Path.Combine(AppContext.BaseDirectory, "contracts", "events", kind);
}
