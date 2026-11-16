using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Erp;
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
        var mode = Environment.GetEnvironmentVariable("CP6_W664_MENU_MODE") ?? "put";
        var order = Environment.GetEnvironmentVariable("CP6_W664_MENU_ORDER");
        var ack = mode == "ack";
        var action = mode switch { "post" => "add", "delete" => "delete", _ => "edit" };
        var method = mode switch { "post" => HttpMethod.Post, "delete" => HttpMethod.Delete, _ => HttpMethod.Put };
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
            pairs[tenant.Tenant] = order is null ? (await GrantInquiry(tenant))[0] : await GrantIntake(tenant);
            if (order is not null)
            {
                var originalSubject = await tenant.Db.Sys_Users.SingleAsync(u => u.Id == tenant.ActorId);
                originalSubject.AuthenticationEpoch = Guid.NewGuid();
                originalSubject.DeptId = tenant.Parent;
                await tenant.Db.SaveChangesAsync();
                tenant.Db.ChangeTracker.Clear();
                await new IdentityBootstrapService(tenant.Db, ContextRuntime(tenant.Tenant, options.Issuer)).InitializeAsync(tenant.Tenant);
                tenant.Db.ChangeTracker.Clear();
            }
            await CopyRole71ForIndependentInquiry(tenant);
            tenant.Db.Sys_Users.Add(new Sys_User
            {
                UserName = independentName, RoleId = 72, DeptId = tenant.Parent, Enable = true,
                Password = new BCryptPasswordHasher(4).Hash(password),
            });
            await tenant.Db.SaveChangesAsync();
            tenant.Db.ChangeTracker.Clear();
        }

        if (mode != "post")
            first.Db.Sys_Menus.Add(new Sys_Menu { MenuId = menuId, MenuKey = resource, MenuName = "Original global menu", Enable = true });
        const int writerRole = 74;
        first.Db.Sys_Roles.Add(new Sys_Role { RoleId = writerRole, RoleName = "W664 menu writer", Enable = true });
        await GrantWriter("menu", action);
        if (mode == "delete") await GrantWriter("menu", "add");
        await GrantWriter("role", "edit");
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
            if (mode != "post") tenant.Db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 71, MenuId = menuId });
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
        var storeFirst = order is null
            ? await InstallMenuSealStore(crmFirst, first.Tenant, "w664_menu_seal_a")
            : new MenuSealStore(first.Tenant, Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!),
                new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION")));
        var storeSecond = await InstallMenuSealStore(crmSecond, second.Tenant, "w664_menu_seal_b");
        await using var crmRelay = ack ? new StockPostgresCommitRelay(crmFirst.Port, storeFirst.SealConnection.Username!,
            int.Parse(Environment.GetEnvironmentVariable("CP6_W664_CRM_ACK_PORT")!)) : null;
        var hostConnection = new NpgsqlConnectionStringBuilder(first.Db.Database.GetConnectionString());
        await using var mainRelay = ack ? new StockPostgresCommitRelay(hostConnection.Port, hostConnection.Username!,
            int.Parse(Environment.GetEnvironmentVariable("CP6_W664_MAIN_ACK_PORT")!)) : null;
        if (mainRelay is not null)
        {
            hostConnection.Port = mainRelay.Port;
            hostConnection.SslMode = SslMode.Disable;
        }

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
            var binding = new NpgsqlConnectionStringBuilder(store.SealConnection.ConnectionString);
            if (crmRelay is not null && store == storeFirst) binding.Port = crmRelay.Port;
            environment[prefix + "ConnectionString"] = binding.ConnectionString;
            environment[prefix + "StoreId"] = store.StoreId.ToString("D");
        }

        await using var host = await AuthenticationApiProcess.Start(hostConnection.ConnectionString, environment, allowAutoRedirect: false);
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

        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        if (order is not null)
        {
            await WriteCrmSessionAsync();
            await WaitConfigurationBridge(bridge, "ready");
        }

        var firstDbOid = await RequireMenuSealStore(crmFirst, storeFirst);
        var secondDbOid = await RequireMenuSealStore(crmSecond, storeSecond);
        Assert.NotEqual(firstDbOid, secondDbOid);
        var beforeMain = await ReadMainEffects();
        var beforeFirst = await ReadMenuRoleFence(crmFirst, first.Tenant);
        var beforeSecond = await ReadMenuRoleFence(crmSecond, second.Tenant);
        var sourceVersions = await first.Db.CrmIdentitySnapshots.AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId) && s.AggregateId == aggregate)
            .ToDictionaryAsync(s => s.TenantId, s => s.Version);
        Assert.Equal(2, sourceVersions.Count);
        Assert.Equal(0, beforeFirst.MinimumVersion);
        Assert.Equal(0, beforeSecond.MinimumVersion);
        object payload = mode == "delete"
            ? new[] { menuId }
            : new { MenuId = menuId, MenuName = "Changed global menu", MenuKey = resource + "-changed", Enable = true };
        Task<HttpResponseMessage> Write() => host.Send(Request(method, "api/menu", writer, payload));
        if (order is not null)
        {
            using var completed = await ExerciseOrderAsync();
            await AssertCommittedAsync(completed);
            await MarkAsync("paused");
            await WaitConfigurationBridge(bridge, "finished");
            return;
        }

        await SetMenuSealLogin(crmSecond, storeSecond, enabled: false);
        try
        {
            await using var denied = new NpgsqlConnection(storeSecond.SealConnection.ConnectionString);
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refusal.SqlState);
            await using var stillAvailable = new NpgsqlConnection(storeFirst.SealConnection.ConnectionString);
            await stillAvailable.OpenAsync();

            using var attempted = await Write();
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
            using var error = JsonDocument.Parse(await attempted.Content.ReadAsStringAsync());
            Assert.Equal(503, error.RootElement.GetProperty("code").GetInt32());
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", error.RootElement.GetProperty("message").GetString());
            var pending = await ReadMenuPlanAsync();
            Assert.Equal(2, pending.Length);
            Assert.Equal(new[] { first.Tenant, second.Tenant }, pending.Select(r => r.TenantId).ToArray());
            Assert.All(pending, child =>
            {
                Assert.False(child.SealStarted);
                Assert.Equal(71, child.RoleId);
                Assert.Equal(sourceVersions[child.TenantId], child.SourceVersion);
                Assert.Equal(child.MenuOperationId, pending[0].MenuOperationId);
                Assert.Equal(first.Tenant, child.ActorTenant);
                Assert.Equal(64, child.SourceHash.Length);
                Assert.Equal(64, child.SuccessorHash.Length);
            });
            await AssertChildUnavailableAsync(pending[0]);
            if (mode == "put")
            {
                var displayMenu = await first.Db.Sys_Menus.AsNoTracking().SingleAsync(m => m.MenuKey == "menu");
                var orderNo = displayMenu.OrderNo + 1;
                using var display = await host.Send(Request(HttpMethod.Put, "api/menu/tree", writer,
                    new[] { new { displayMenu.MenuId, displayMenu.ParentId, OrderNo = orderNo } }));
                var displayBody = await display.Content.ReadAsStringAsync();
                Assert.True(display.StatusCode == HttpStatusCode.OK, displayBody);
                var recordedOrder = await first.Db.Sys_Menus.AsNoTracking().Where(m => m.MenuId == displayMenu.MenuId)
                    .Select(m => m.OrderNo).SingleAsync();
                Assert.Equal(orderNo, recordedOrder);
                using var afterDisplay = JsonDocument.Parse(await ReadMainEffects());
                foreach (var original in originalMainRows.RootElement.EnumerateObject())
                {
                    var current = afterDisplay.RootElement.GetProperty(original.Name).GetRawText();
                    if (original.Name == "CatalogRevision") Assert.NotEqual(original.Value.GetRawText(), current);
                    else Assert.Equal(original.Value.GetRawText(), current);
                }
                Assert.Equal(beforeFirst, await ReadMenuRoleFence(crmFirst, first.Tenant));
                Assert.Equal(beforeSecond, await ReadMenuRoleFence(crmSecond, second.Tenant));
            }
            Assert.Equal(pending, await ReadMenuPlanAsync());
        }
        finally { await SetMenuSealLogin(crmSecond, storeSecond, enabled: true); }

        if (crmRelay is not null && mainRelay is not null)
        {
            crmRelay.Arm();
            using var interrupted = await Write();
            await crmRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, interrupted.StatusCode);
            Assert.Equal(beforeMain, await ReadMainEffects());
            var partialFirst = await ReadMenuRoleFence(crmFirst, first.Tenant);
            var partialSecond = await ReadMenuRoleFence(crmSecond, second.Tenant);
            Assert.Equal(new MenuRoleFence(sourceVersions[first.Tenant] + 1, beforeFirst.Revision + 1), partialFirst);
            Assert.Equal(beforeSecond, partialSecond);
            var stillAccepted = await ReadMenuPlanAsync();
            Assert.Equal(2, stillAccepted.Length);
            Assert.All(stillAccepted, row => Assert.True(row.SealStarted));

            await using var barrier = new NpgsqlConnection(observer.ConnectionString);
            await barrier.OpenAsync();
            await using var tx = await barrier.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SELECT crm_identity_lock_v1.hold_v1(@tenant)", barrier, tx))
            {
                hold.Parameters.AddWithValue("tenant", second.Tenant);
                await hold.ExecuteNonQueryAsync();
            }
            var repair = Write();
            int blocked;
            try
            {
                blocked = await WaitMenuSealBlockAsync(crmSecond, barrier.ProcessID, repair);
                Assert.Equal(beforeMain, await ReadMainEffects());
                mainRelay.Arm();
            }
            finally { await tx.RollbackAsync(); }
            using var response = await repair;
            await mainRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Empty(await ReadMenuPlanAsync());
            await File.WriteAllTextAsync(Path.Combine(bridge, "menu-ack.json"), JsonSerializer.Serialize(new
            {
                OriginalOperationId = stillAccepted[0].MenuOperationId, PartialFirst = partialFirst, PartialSecond = partialSecond,
                CrmAckDropped = true, MainAckDropped = true, BlockingSecondStoreBackend = barrier.ProcessID, SealBackend = blocked,
                MainOutcomeStatus = (int)response.StatusCode,
            }));
        }

        using var recovered = await Write();
        await AssertCommittedAsync(recovered);
        if (mode == "delete")
        {
            using var recreated = await host.Send(Request(HttpMethod.Post, "api/menu", writer,
                new { MenuId = menuId, MenuName = "Later non-CRM menu", MenuKey = "later-w664-" + menuId, Enable = true }));
            Assert.Equal(HttpStatusCode.OK, recreated.StatusCode);
            using var newDeletion = await Write();
            using var newResult = JsonDocument.Parse(await newDeletion.Content.ReadAsStringAsync());
            Assert.Equal(1, newResult.RootElement.GetProperty("count").GetInt32());
            using var again = await Write();
            using var absent = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert.Equal(0, absent.RootElement.GetProperty("count").GetInt32());
        }

        async Task GrantWriter(string key, string actionCode)
        {
            var menu = first.Db.Sys_Menus.Local.SingleOrDefault(m => m.MenuKey == key)
                ?? await first.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == key);
            if (menu is null)
            {
                menu = new Sys_Menu { MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = key, MenuName = key, Enable = true };
                first.Db.Sys_Menus.Add(menu);
            }
            first.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = writerRole, MenuId = menu.MenuId, ActionCode = actionCode });
        }

        async Task AssertChildUnavailableAsync(MenuPlanRow child)
        {
            var path = $"api/iam/roles/operations/{child.OperationId:D}";
            using var get = await host.Send(Request(HttpMethod.Get, path + "?kind=role-update", writer));
            Assert.Equal(HttpStatusCode.Conflict, get.StatusCode);
            Assert.Contains("IAM_CRM_MENU_SEAL_OWNED", await get.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var close = await host.Send(Request(HttpMethod.Post, path + "/close?kind=role-update", writer));
            Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
            Assert.Contains("IAM_CRM_MENU_SEAL_OWNED", await close.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var original = new IamRoleCommandV1
            {
                OperationId = child.OperationId, RoleId = 71, RoleName = "one", Enable = true,
                ExpectedVersion = await first.Authority.Version("role:71", default),
                ExpectedReferencesVersion = await first.Authority.ReferencesVersion(default),
                Reason = "W664 native ordinary role receipt ownership control",
            };
            using var put = await host.Send(Request(HttpMethod.Put, "api/iam/roles/71", writer, original));
            Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
            Assert.Contains("IAM_CRM_MENU_SEAL_OWNED", await put.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Task<MenuPlanRow[]> ReadMenuPlanAsync() => first.Db.Database.SqlQuery<MenuPlanRow>($"""
            SELECT r.tenant_id AS "TenantId", r.role_id AS "RoleId", r.operation_id AS "OperationId",
                   r.menu_operation_id AS "MenuOperationId", r.seal_started AS "SealStarted", r.source_version AS "SourceVersion",
                   r.source_hash AS "SourceHash", r.successor_hash AS "SuccessorHash", o.actor_tenant AS "ActorTenant"
            FROM cp6_authentication_v1.pending_crm_role_seals r JOIN cp6_authentication_v1.legacy_menu_operations o
              ON o.operation_id=r.menu_operation_id WHERE o.menu_ids @> ARRAY[{menuId}]
            ORDER BY r.tenant_id,r.role_id
            """).ToArrayAsync();

        async Task AssertCommittedAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            using var json = JsonDocument.Parse(body);
            if (mode == "delete") Assert.Equal(3, json.RootElement.GetProperty("count").GetInt32());
            else
            {
                Assert.Equal(menuId, json.RootElement.GetProperty("menuId").GetInt32());
                Assert.Equal(resource + "-changed", json.RootElement.GetProperty("menuKey").GetString());
            }

            var afterMain = await ReadMainEffects();
            Assert.NotEqual(beforeMain, afterMain);
            using var old = JsonDocument.Parse(beforeMain);
            using var final = JsonDocument.Parse(afterMain);
            Assert.Equal(old.RootElement.GetProperty("RoleActions").GetRawText(), final.RootElement.GetProperty("RoleActions").GetRawText());
            if (mode == "delete") Assert.Equal(JsonValueKind.Null, final.RootElement.GetProperty("RoleMenus").ValueKind);
            else Assert.Equal(old.RootElement.GetProperty("RoleMenus").GetRawText(), final.RootElement.GetProperty("RoleMenus").GetRawText());
            Assert.Equal(old.RootElement.GetProperty("Outbox").GetInt32() + 2, final.RootElement.GetProperty("Outbox").GetInt32());
            Assert.Equal(old.RootElement.GetProperty("PriorityOutbox").GetInt32(), final.RootElement.GetProperty("PriorityOutbox").GetInt32());
            Assert.Equal(old.RootElement.GetProperty("FieldAudit").GetInt32() + 1, final.RootElement.GetProperty("FieldAudit").GetInt32());
            Assert.Equal(old.RootElement.GetProperty("CommandAudit").GetInt32() + 1, final.RootElement.GetProperty("CommandAudit").GetInt32());

            var finalFirst = await ReadMenuRoleFence(crmFirst, first.Tenant);
            var finalSecond = await ReadMenuRoleFence(crmSecond, second.Tenant);
            Assert.Equal(new MenuRoleFence(sourceVersions[first.Tenant] + 1, beforeFirst.Revision + (ack ? 2 : 1)), finalFirst);
            Assert.Equal(new MenuRoleFence(sourceVersions[second.Tenant] + 1, beforeSecond.Revision + 1), finalSecond);
            Assert.Empty(await ReadMenuPlanAsync());
            Assert.All(await CurrentAdmission(), admission => Assert.True(admission.Affected && admission.Independent && admission.OriginalRead));
            using var replay = await Write();
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(body, await replay.Content.ReadAsStringAsync());
            Assert.Equal(afterMain, await ReadMainEffects());
            Assert.Equal(finalFirst, await ReadMenuRoleFence(crmFirst, first.Tenant));
            Assert.Equal(finalSecond, await ReadMenuRoleFence(crmSecond, second.Tenant));

            await File.WriteAllTextAsync(Path.Combine(bridge, "menu-committed.json"), JsonSerializer.Serialize(new
            {
                Mode = mode, Order = order, MenuId = menuId, Response = json.RootElement,
                Main = final.RootElement, First = finalFirst, Second = finalSecond,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task<HttpResponseMessage> ExerciseOrderAsync()
        {
            if (order == "owner-first")
            {
                await WaitConfigurationBridge(bridge, "owner-held");
                var write = Write();
                try
                {
                    var blocked = await WaitMenuSealBlockAsync(crmFirst, null, write);
                    Assert.Equal(beforeMain, await ReadMainEffects());
                    Assert.Equal(beforeFirst, await ReadMenuRoleFence(crmFirst, first.Tenant));
                    Assert.Equal(beforeSecond, await ReadMenuRoleFence(crmSecond, second.Tenant));
                    Assert.All(await ReadMenuPlanAsync(), entry => Assert.True(entry.SealStarted));
                    await File.WriteAllTextAsync(Path.Combine(bridge, "owner-wait.json"), JsonSerializer.Serialize(new { SealBackend = blocked }));
                }
                finally { await MarkAsync("release-owner"); }
                return await write;
            }

            Assert.Equal("pause-first", order);
            await WaitConfigurationBridge(bridge, "permission-held");
            return await Write();
        }

        async Task WriteCrmSessionAsync()
        {
            var pair = pairs[first.Tenant];
            var source = await first.Db.CrmIdentitySnapshots.AsNoTracking().Where(r => r.TenantId == first.Tenant)
                .OrderBy(r => r.AggregateId).ToArrayAsync();
            var account = await first.Db.Sys_Users.AsNoTracking().SingleAsync(u => u.Id == first.ActorId);
            await File.WriteAllTextAsync(Path.Combine(bridge, "session.json.tmp"), JsonSerializer.Serialize(new
            {
                marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = options.Issuer, origin = host.Origin.ToString(),
                accessToken = subjects[0].Affected, tenantId = first.Tenant, subjectId = first.ActorId, departmentId = first.Parent,
                siteId = pair.SiteId, formId = pair.FormId, keyId = options.KeyId, publicKeyBase64 = options.PublicKeyBase64,
                activeFromUtc = options.ActiveFromUtc, sourceRows = source.Select(r => new
                {
                    r.AggregateId, r.EventType, r.Version, r.PayloadSha256, r.IsDeleted, r.PayloadJson,
                }),
                authenticationEpoch = account.AuthenticationEpoch,
            }));
            File.Move(Path.Combine(bridge, "session.json.tmp"), Path.Combine(bridge, "session.json"));
        }

        async Task MarkAsync(string phase)
        {
            var destination = Path.Combine(bridge, phase);
            await File.WriteAllTextAsync(destination + ".tmp", "PASS");
            File.Move(destination + ".tmp", destination);
        }

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
              'PriorityOutbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"=ANY({tenantIds})),
              'FieldAudit',(SELECT count(*) FROM public."Sys_FieldAuditLogs" WHERE "EntityName"='Sys_Menu' AND "EntityKey"={menuId.ToString()}),
              'CommandAudit',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={first.Tenant}
                AND "ContextJson"::jsonb->>'kind' IN ('menu-add','menu-edit','menu-delete'))
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

    private static async Task<int> WaitMenuSealBlockAsync(NpgsqlConnection crm, int? blocker, Task request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await using var query = new NpgsqlCommand("""
            SELECT pid FROM pg_stat_activity
            WHERE datname=current_database() AND application_name='cp6-main-inquiry-tenant-pause-v1'
              AND wait_event_type='Lock' AND query LIKE '%advance_role_v1%'
              AND cardinality(pg_blocking_pids(pid))>0
              AND (@blocker::integer IS NULL OR @blocker=ANY(pg_blocking_pids(pid)))
            LIMIT 1
            """, crm);
        query.Parameters.AddWithValue("blocker", NpgsqlTypes.NpgsqlDbType.Integer, blocker is null ? DBNull.Value : (object)blocker.Value);
        while (!request.IsCompleted)
        {
            if (await query.ExecuteScalarAsync(timeout.Token) is int pid) return pid;
            await Task.Delay(25, timeout.Token);
        }
        throw new InvalidOperationException("Main returned before the original CRM role seal blocked.");
    }

    private sealed record MenuPlanRow(Guid TenantId, int RoleId, Guid OperationId, Guid MenuOperationId, bool SealStarted,
        int SourceVersion, string SourceHash, string SuccessorHash, Guid ActorTenant);

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
