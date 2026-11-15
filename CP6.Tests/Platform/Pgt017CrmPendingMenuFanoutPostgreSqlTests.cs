using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_global_menu_key_refused_second_store_stops_fresh_authority_without_partial_seal()
    {
        await using var tenantOne = await Seed();
        await using var tenantTwo = await Seed();
        var tenants = new[] { tenantOne, tenantTwo }.OrderBy(h => h.Tenant).ToArray();
        var tenantIds = tenants.Select(h => h.Tenant).ToArray();
        var first = tenants[0];
        var second = tenants[1];
        var menuId = Interlocked.Increment(ref policyMenuId);
        var resource = "crm-menu-cold-" + Guid.NewGuid().ToString("N");
        var options = InquiryPermissionSigningTests.Options();
        var clientSecret = CrmOidcCrypto.RandomToken();
        const string redirect = "https://crm.cp6.test/callback";
        const string independentName = "w664-menu-independent";
        const string writerName = "w664-menu-writer";
        const string password = "Iam1!Password";
        const string fenceHeader = "X-CP6-Inquiry-Tenant-Fence";
        const string aggregate = "permission:role:71";

        var pairs = new Dictionary<Guid, InquirySiteFormScopeV1>();
        foreach (var tenant in tenants)
        {
            pairs[tenant.Tenant] = (await GrantInquiry(tenant))[0];
            await CopyRole71ForIndependentInquiry(tenant);
            tenant.Db.Sys_Users.Add(new Sys_User
            {
                UserName = independentName, RoleId = 72, DeptId = tenant.Parent, Enable = true,
                Password = new BCryptPasswordHasher(4).Hash(password),
            });
            await tenant.Db.SaveChangesAsync();
            tenant.Db.ChangeTracker.Clear();
        }

        first.Db.Sys_Menus.Add(new Sys_Menu { MenuId = menuId, MenuKey = resource, MenuName = "Original global menu", Enable = true });
        var adminMenu = await first.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "menu");
        if (adminMenu is null)
        {
            adminMenu = new Sys_Menu
            {
                MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = "menu", MenuName = "Menus", Enable = true,
            };
            first.Db.Sys_Menus.Add(adminMenu);
        }

        const int writerRole = 74;
        first.Db.Sys_Roles.Add(new Sys_Role { RoleId = writerRole, RoleName = "W664 menu writer", Enable = true });
        first.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = writerRole, MenuId = adminMenu.MenuId, ActionCode = "edit" });
        first.Db.Sys_Users.Add(new Sys_User
        {
            UserName = writerName, RoleId = writerRole, DeptId = first.Parent, Enable = true,
            Password = new BCryptPasswordHasher(4).Hash(password),
        });
        await first.Db.SaveChangesAsync();
        first.Db.ChangeTracker.Clear();

        foreach (var tenant in tenants)
        {
            tenant.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 71, MenuId = menuId, ActionCode = "query" });
            tenant.Db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 71, MenuId = menuId });
            await tenant.Db.SaveChangesAsync();
            tenant.Db.ChangeTracker.Clear();
        }

        var observer = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"))
        {
            Pooling = false,
        };
        await using var crmFirst = new NpgsqlConnection(observer.ConnectionString);
        await crmFirst.OpenAsync();
        var secondDatabase = "w664_menu_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand("CREATE DATABASE " + QuoteMenuIdentifier(secondDatabase), crmFirst))
            await create.ExecuteNonQueryAsync();

        observer.Database = secondDatabase;
        await using var crmSecond = new NpgsqlConnection(observer.ConnectionString);
        await crmSecond.OpenAsync();
        var storeFirst = await InstallMenuSealStore(crmFirst, first.Tenant, "w664_menu_seal_a");
        var storeSecond = await InstallMenuSealStore(crmSecond, second.Tenant, "w664_menu_seal_b");
        var firstDbOid = await RequireMenuSealStore(crmFirst, storeFirst);
        var secondDbOid = await RequireMenuSealStore(crmSecond, storeSecond);
        Assert.NotEqual(firstDbOid, secondDbOid);

        var environment = IdentityEnvironment(first.Tenant);
        environment["CrmIdentity__Tenants__" + second.Tenant.ToString("D")] = "us";
        environment["CrmOidc__Organizations__0__Slug"] = "w664-first";
        environment["CrmOidc__Organizations__1__TenantId"] = second.Tenant.ToString("D");
        environment["CrmOidc__Organizations__1__Slug"] = "w664-second";
        environment["CrmOidc__Organizations__1__Region"] = "us";
        environment["CrmOidc__Organizations__1__CrmEnabled"] = "true";
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = options.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        foreach (var store in new[] { storeFirst, storeSecond })
        {
            var prefix = $"CrmInquiryTenantPause__Tenants__{store.Tenant:D}__";
            environment[prefix + "ConnectionString"] = store.SealConnection.ConnectionString;
            environment[prefix + "StoreId"] = store.StoreId.ToString("D");
        }

        await using var host = await AuthenticationApiProcess.Start(first.Db.Database.GetConnectionString()!, environment, allowAutoRedirect: false);
        var writer = await Login(host, first, userName: writerName);
        var subjects = new List<(MenuSealStore Store, string Affected, string Independent)>();
        foreach (var (tenant, store) in new[] { (first, storeFirst), (second, storeSecond) })
        {
            var affected = await Exchange(tenant.Tenant, await Login(host, tenant));
            var independent = await Exchange(tenant.Tenant, await Login(host, tenant, userName: independentName));
            subjects.Add((store, affected, independent));
        }

        var initial = await CurrentAdmission();
        Assert.All(initial, admission =>
        {
            Assert.True(admission.Affected);
            Assert.True(admission.Independent);
            Assert.True(admission.OriginalRead);
        });

        var beforeMain = await ReadMainEffects();
        var beforeFirst = await ReadMenuRoleFence(crmFirst, first.Tenant);
        var beforeSecond = await ReadMenuRoleFence(crmSecond, second.Tenant);
        var sourceVersions = await first.Db.CrmIdentitySnapshots.AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId) && s.AggregateId == aggregate)
            .ToDictionaryAsync(s => s.TenantId, s => s.Version);
        Assert.Equal(2, sourceVersions.Count);
        Assert.Equal(0, beforeFirst.MinimumVersion);
        Assert.Equal(0, beforeSecond.MinimumVersion);
        await SetMenuSealLogin(crmSecond, storeSecond, enabled: false);
        try
        {
            await using var denied = new NpgsqlConnection(storeSecond.SealConnection.ConnectionString);
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refusal.SqlState);
            await using var stillAvailable = new NpgsqlConnection(storeFirst.SealConnection.ConnectionString);
            await stillAvailable.OpenAsync();

            using var attempted = await host.Send(Request(HttpMethod.Put, "api/menu", writer,
                new { MenuId = menuId, MenuName = "Changed global menu", MenuKey = resource + "-changed", Enable = true }));
            var afterMain = await ReadMainEffects();
            var afterFirst = await ReadMenuRoleFence(crmFirst, first.Tenant);
            var afterSecond = await ReadMenuRoleFence(crmSecond, second.Tenant);
            var afterAdmission = await CurrentAdmission();
            var observed = new MenuColdOutcome((int)attempted.StatusCode, beforeMain == afterMain,
                beforeFirst == afterFirst, beforeSecond == afterSecond,
                !afterAdmission[0].Affected && !afterAdmission[1].Affected,
                afterAdmission.All(a => a.Independent && a.OriginalRead));

            var evidence = Path.Combine(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!, "menu-cold.json");
            using var originalMainRows = JsonDocument.Parse(beforeMain);
            using var currentMainRows = JsonDocument.Parse(afterMain);
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                MenuId = menuId, StoreA = new { storeFirst.Tenant, storeFirst.StoreId, DatabaseOid = firstDbOid },
                StoreB = new { storeSecond.Tenant, storeSecond.StoreId, DatabaseOid = secondDbOid },
                Refusal = refusal.SqlState, SourceVersions = sourceVersions, BeforeMain = originalMainRows.RootElement,
                AfterMain = currentMainRows.RootElement, BeforeFirst = beforeFirst, AfterFirst = afterFirst,
                BeforeSecond = beforeSecond, AfterSecond = afterSecond, InitialAdmission = initial, AfterAdmission = afterAdmission,
                IssuedGrantIds = subjects.Select(s => new { s.Store.Tenant, Affected = GrantId(s.Affected), Independent = GrantId(s.Independent) }),
                Actual = observed,
            }, new JsonSerializerOptions { WriteIndented = true }));

            Assert.Equal(new MenuColdOutcome(503, true, true, true, true, true), observed);
        }
        finally { await SetMenuSealLogin(crmSecond, storeSecond, enabled: true); }

        async Task<string> Exchange(Guid tenant, Dictionary<string, string> login)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var authorize = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code",
                ["code_challenge_method"] = "S256", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
                ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken(),
                ["scope"] = "openid profile crm", ["organization_id"] = tenant.ToString("D"),
            });
            using var start = await host.Send(Request(HttpMethod.Get, authorize, login));
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            using var continued = await host.Send(Request(HttpMethod.Get, start.Headers.Location!.PathAndQuery, login));
            Assert.Equal(HttpStatusCode.Redirect, continued.StatusCode);
            Assert.Equal(redirect, continued.Headers.Location!.GetLeftPart(UriPartial.Path));
            var code = QueryHelpers.ParseQuery(continued.Headers.Location.Query)["code"].ToString();
            Assert.NotEmpty(code);
            using var exchange = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
                }),
            };
            exchange.Headers.Authorization = new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var reply = await host.Send(exchange);
            using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            return json.RootElement.GetProperty("access_token").GetString()!;
        }

        async Task<MenuAdmission[]> CurrentAdmission()
        {
            var results = new List<MenuAdmission>();
            foreach (var (store, affected, independent) in subjects)
            {
                var affectedWrite = await Project(store, affected, "configuration:write");
                var independentWrite = await Project(store, independent, "configuration:write");
                var affectedRead = await Project(store, affected, "configuration:read");
                results.Add(new(store.Tenant, affectedWrite, independentWrite, !affectedRead));
            }

            return results.ToArray();
        }

        async Task<bool> Project(MenuSealStore store, string bearer, string purpose)
        {
            var pair = pairs[store.Tenant];
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-permission")
            {
                Content = JsonContent.Create(new InquiryPermissionRequestV1(purpose, pair.SiteId, pair.FormId)),
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await host.Send(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(store.Tenant, json.RootElement.GetProperty("organizationId").GetGuid());
            if (!response.Headers.TryGetValues(fenceHeader, out var values)) return false;
            Assert.StartsWith(store.StoreId.ToString("D") + "/", Assert.Single(values), StringComparison.Ordinal);
            return true;
        }

        Task<string> ReadMainEffects() => first.Db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'Menu',(SELECT to_jsonb(m) FROM public."Sys_Menus" m WHERE m."MenuId"={menuId}),
              'RoleActions',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a."TenantId", a."Id")
                FROM public."Sys_RoleAction" a WHERE a."MenuId"={menuId}),
              'RoleMenus',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m."TenantId", m."Id")
                FROM public."Sys_RoleMenus" m WHERE m."MenuId"={menuId}),
              'Snapshots',(SELECT jsonb_agg(jsonb_build_array(s."TenantId",s."AggregateId",s."Version",s."EventType",s."PayloadSha256",s."PayloadJson") ORDER BY s."TenantId",s."AggregateId")
                FROM crm_identity."Snapshot" s WHERE s."TenantId"=ANY({tenantIds}) AND s."AggregateId" IN ('permission:role:71','permission:role:72')),
              'Generations',(SELECT jsonb_agg(to_jsonb(g) ORDER BY g."TenantId")
                FROM public."CrmIdentityTenantGenerations" g WHERE g."TenantId"=ANY({tenantIds})),
              'CatalogRevision',(SELECT jsonb_agg(jsonb_build_array(r.tenant_id,r.revision) ORDER BY r.tenant_id) FROM cp6_authentication_v1.identity_admission_revisions r
                WHERE r.tenant_id=ANY({tenantIds}) AND r.object_key='permission-catalog'),
              'Outbox',(SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"=ANY({tenantIds})),
              'PriorityOutbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"=ANY({tenantIds}))
            )::text AS "Value"
            """).SingleAsync();
    }

    private static async Task<MenuSealStore> InstallMenuSealStore(NpgsqlConnection crm, Guid tenant, string login)
    {
        var directory = Environment.GetEnvironmentVariable("CP6_W664_CRM_SCHEMA_SQL")!;
        var store = Guid.NewGuid();
        var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var seal = new NpgsqlConnectionStringBuilder(crm.ConnectionString)
        {
            Username = login, Password = secret, Pooling = false, SslMode = SslMode.Disable,
        };
        await using var tx = await crm.BeginTransactionAsync();
        foreach (var file in new[]
        {
            "SessionSchemaV1", "BaselineSchemaV1", "ProjectionSchemaV1", "IdentityLockSchemaV1",
            "InquiryTenantPauseSchemaV1", "InquiryRoleFenceSchemaV1", "InquiryIdentityFenceSchemaV1", "InquiryStoreTimelineSchemaV1",
        })
        {
            await using var install = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory, file + ".sql")), crm, tx);
            await install.ExecuteNonQueryAsync();
        }

        var quotedLogin = QuoteMenuIdentifier(login);
        var database = QuoteMenuIdentifier(crm.Database);
        await using var register = new NpgsqlCommand($"""
            CREATE ROLE {quotedLogin} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD '{secret}';
            GRANT CONNECT ON DATABASE {database} TO {quotedLogin};
            GRANT USAGE ON SCHEMA crm_inquiry_tenant_pause_v1 TO {quotedLogin};
            GRANT EXECUTE ON FUNCTION crm_inquiry_tenant_pause_v1.advance_role_v1(uuid,uuid,text,integer) TO {quotedLogin};
            INSERT INTO crm_inquiry_tenant_pause_v1.fences(tenant_id,store_id,seal_login,reopened_at)
            VALUES (@tenant,@store,@login,1);
            INSERT INTO crm_auth."IdentityFenceV1"("TenantId","Revision") VALUES (@tenant,1);
            """, crm, tx);
        register.Parameters.AddWithValue("tenant", tenant);
        register.Parameters.AddWithValue("store", store);
        register.Parameters.AddWithValue("login", login);
        await register.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return new(tenant, store, seal);
    }

    private static async Task<long> RequireMenuSealStore(NpgsqlConnection crm, MenuSealStore store)
    {
        await using var verify = new NpgsqlCommand("""
            SELECT database_oid::bigint FROM crm_inquiry_tenant_pause_v1.fences
            WHERE tenant_id=@tenant AND store_id=@store AND seal_login=@login
              AND database_oid=crm_inquiry_tenant_pause_v1.database_oid_v1()
              AND cluster_id=crm_inquiry_tenant_pause_v1.cluster_id_v1()
            """, crm);
        verify.Parameters.AddWithValue("tenant", store.Tenant);
        verify.Parameters.AddWithValue("store", store.StoreId);
        verify.Parameters.AddWithValue("login", store.SealConnection.Username!);
        return (long)(await verify.ExecuteScalarAsync())!;
    }

    private static async Task SetMenuSealLogin(NpgsqlConnection crm, MenuSealStore store, bool enabled)
    {
        var role = QuoteMenuIdentifier(store.SealConnection.Username!);
        var login = enabled ? "LOGIN" : "NOLOGIN";
        await using var alter = new NpgsqlCommand($"ALTER ROLE {role} {login}", crm);
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task<MenuRoleFence> ReadMenuRoleFence(NpgsqlConnection crm, Guid tenant)
    {
        await using var query = new NpgsqlCommand("""
            SELECT COALESCE((SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.role_fences
                   WHERE tenant_id=@tenant AND aggregate_id='permission:role:71'), 0), "Revision"
            FROM crm_auth."IdentityFenceV1" WHERE "TenantId"=@tenant
            """, crm);
        query.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await query.ExecuteReaderAsync(CommandBehavior.SingleRow);
        Assert.True(await reader.ReadAsync());
        return new(reader.GetInt32(0), reader.GetInt64(1));
    }

    private static string QuoteMenuIdentifier(string value)
    {
        using var quote = new NpgsqlCommandBuilder();
        return quote.QuoteIdentifier(value);
    }

    private sealed record MenuSealStore(Guid Tenant, Guid StoreId, NpgsqlConnectionStringBuilder SealConnection);
    private sealed record MenuRoleFence(int MinimumVersion, long Revision);
    private sealed record MenuAdmission(Guid Tenant, bool Affected, bool Independent, bool OriginalRead);
    private sealed record MenuColdOutcome(int HttpStatus, bool MainUnchanged, bool FirstStoreUnchanged, bool SecondStoreUnchanged,
        bool AffectedIssuanceStopped, bool IndependentAndReadAvailable);
}
