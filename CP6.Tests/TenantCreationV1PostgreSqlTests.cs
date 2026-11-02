using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<TenantCreationTemplateV1> CreationTemplateSeed(Harness h)
    {
        var source = TenantContext.DefaultTenant;
        if (!await h.Db.Sys_Tenants.AnyAsync(x => x.Id == source))
        {
            h.Db.Sys_Tenants.Add(new() { Id = source, TenantCode = "creation-template", TenantName = "Fixture default template" });
            await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        }
        var role = await h.Db.Sys_Roles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == source && x.RoleId == 1);
        if (role == null) h.Db.Sys_Roles.Add(new() { TenantId = source, RoleId = 1, RoleName = "模板管理员", Enable = true });
        else role.Enable = true;
        var menu = new Sys_Menu { MenuId = Random.Shared.Next(100000000, 200000000), MenuName = "创建模板菜单", MenuKey = "create-" + Guid.NewGuid().ToString("N"), Enable = true };
        h.Db.Sys_Menus.Add(menu); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == source && x.RoleId == 1).ExecuteDeleteAsync();
        h.Db.Sys_RoleMenus.Add(new() { TenantId = source, RoleId = 1, MenuId = menu.MenuId });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return await ManagementCall(h, (s, session) => s.ReadCreationTemplateAsync(session));
    }
    private static TenantCreationCommandV1 CreationCommand(TenantCreationTemplateV1 template)
        => new(Guid.NewGuid().ToString("N"), template.Version, "New_" + Guid.NewGuid().ToString("N"), "新租户 ",
            new DateTime(2035, 1, 2, 3, 4, 5).AddTicks(1234560), "保留备注 ", "first-admin");
    private Task<TenantCreationResultV1> CreationWrite(Harness h, TenantCreationCommandV1 command, params IInterceptor[] interceptors)
        => ManagementCall(h, (s, session) => s.CreateVersionedAsync(command, session), null, interceptors);
    private Task<TenantCreationResultV1?> CreationRead(Harness h, string operation)
        => ManagementCall(h, (s, session) => s.ReadCreationOperationAsync(operation, session));
    private static void SameCreation(TenantCreationReceiptV1 expected, TenantCreationResultV1? actual)
    {
        Assert.NotNull(actual); Assert.Null(actual.TempPassword); Assert.True(actual.NeedsCredentialReset);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual.Receipt));
    }
    private async Task<int[]> CreationCounts(Harness h) => [await h.Db.Sys_Tenants.CountAsync(), await h.Db.Sys_Users.IgnoreQueryFilters().CountAsync(),
        await h.Db.Sys_Roles.IgnoreQueryFilters().CountAsync(), await h.Db.Sys_RoleMenus.IgnoreQueryFilters().CountAsync(),
        await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync(), await h.Db.Sys_FieldAuditLogs.IgnoreQueryFilters().CountAsync(),
        await h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_tenant_creation_v1.commands").SingleAsync()];

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_atomic_template_copy_password_once_and_original_after_target_changes()
    {
        await using var h = await TenantManagementSeed(); var template = await CreationTemplateSeed(h); var command = CreationCommand(template);
        command = command with { OperationRef = new string('界', 3000) + "\0original" };
        var first = await CreationWrite(h, command); Assert.NotNull(first.TempPassword); Assert.False(first.NeedsCredentialReset);
        var id = first.Receipt.TenantId;
        var tenant = await h.Db.Sys_Tenants.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(command.Code, tenant.TenantCode); Assert.Equal(command.Name, tenant.TenantName); Assert.Equal(command.Remark, tenant.Remark); Assert.Equal(command.ExpireDate, tenant.ExpireDate);
        var admin = await h.Db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TenantId == id);
        Assert.Equal(first.Receipt.AdminUserId, admin.Id); Assert.True(admin.Enable && admin.MustChangePassword); Assert.False(admin.IsPlatformAdmin); Assert.Equal(1, admin.RoleId);
        Assert.True(new BCryptPasswordHasher(4).Verify(first.TempPassword, admin.Password));
        Assert.Single(await h.Db.Sys_Roles.IgnoreQueryFilters().Where(x => x.TenantId == id && x.RoleId == 1 && x.Enable).ToArrayAsync());
        Assert.Equal(template.MenuIds, await h.Db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == id).Select(x => x.MenuId).OrderBy(x => x).ToArrayAsync());
        var audit = await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().SingleAsync(x => x.RequestTenantCode == command.Code);
        Assert.Equal(h.User, audit.UserId); Assert.Equal(TenantContext.DefaultTenant, audit.TenantId);
        Assert.DoesNotContain(first.TempPassword, JsonSerializer.Serialize(audit));
        var persisted = await h.Db.Database.SqlQuery<byte[]>($"SELECT result_bytes AS \"Value\" FROM cp6_tenant_creation_v1.commands WHERE actor_id={h.User}").SingleAsync();
        Assert.DoesNotContain("password", Encoding.UTF8.GetString(persisted).ToLowerInvariant()); Assert.DoesNotContain(first.TempPassword, Encoding.UTF8.GetString(persisted));
        var counts = await CreationCounts(h); SameCreation(first.Receipt, await CreationWrite(h, command)); SameCreation(first.Receipt, await CreationRead(h, command.OperationRef));
        Assert.Equal(counts, await CreationCounts(h));
        await h.Db.Sys_Tenants.Where(x => x.Id == id).ExecuteDeleteAsync();
        await h.Db.Sys_Roles.IgnoreQueryFilters().Where(x => x.TenantId == TenantContext.DefaultTenant && x.RoleId == 1).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        SameCreation(first.Receipt, await CreationWrite(h, command)); Assert.False(await h.Db.Sys_Tenants.AnyAsync(x => x.Id == id));
        foreach (var sql in new[] { "DELETE FROM cp6_tenant_creation_v1.commands", "UPDATE cp6_tenant_creation_v1.commands SET actor_id=actor_id", "TRUNCATE cp6_tenant_creation_v1.commands" })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_concurrent_original_and_case_insensitive_codes_create_only_once()
    {
        await using var h = await TenantManagementSeed(); var template = await CreationTemplateSeed(h); var command = CreationCommand(template);
        var same = await Task.WhenAll(CreationWrite(h, command), CreationWrite(h, command));
        Assert.Equal(same[0].Receipt.TenantId, same[1].Receipt.TenantId); Assert.Single(same.Where(x => x.TempPassword != null));
        command = CreationCommand(template); var alternate = command with { OperationRef = Guid.NewGuid().ToString("N"), Code = command.Code.ToLowerInvariant() };
        var results = await Task.WhenAll(new[] { command, alternate }.Select(async x =>
        { try { await CreationWrite(h, x); return "OK"; } catch (TenantManagementExceptionV1 e) { return e.Message; } }));
        Assert.Equal(new[] { "E-SEC-033", "OK" }, results.Order());
        Assert.Equal(1, await h.Db.Sys_Tenants.CountAsync(x => x.TenantCode == command.Code));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_original_actor_input_and_invalid_boundaries_have_no_new_effects()
    {
        await using var h = await TenantManagementSeed(); await using var other = await TenantManagementSeed(); var template = await CreationTemplateSeed(h);
        var command = CreationCommand(template); await CreationWrite(h, command); var counts = await CreationCounts(h);
        Assert.Equal(403, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationRead(other, command.OperationRef))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(other, command))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(h, command with { Name = "不同" }))).Status);
        foreach (var bad in new[] { command with { Code = "" }, command with { Code = new string('x', 51) }, command with { AdminUserName = new string('x', 101) },
            command with { Code = "bad\0" }, command with { AdminUserName = "bad\ud800" }, command with { Name = " " },
            command with { Remark = new string('x', 501) }, command with { ExpireDate = new DateTime(2035,1,1).AddTicks(1) },
            command with { ExpireDate = new DateTime(2035,1,1,0,0,0,DateTimeKind.Utc) } })
            Assert.Equal(400, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(h, bad with { OperationRef = Guid.NewGuid().ToString("N") }))).Status);
        Assert.Equal(counts, await CreationCounts(h));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        await Assert.ThrowsAsync<PlatformAdminAuthorizationException>(() => CreationRead(h, command.OperationRef));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_template_ABA_empty_disabled_and_missing_menu_fail_before_creation()
    {
        await using var h = await TenantManagementSeed(); var template = await CreationTemplateSeed(h); var command = CreationCommand(template);
        var source = TenantContext.DefaultTenant; var menu = template.MenuIds.Single();
        await h.Db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == source && x.RoleId == 1).ExecuteDeleteAsync();
        h.Db.Sys_RoleMenus.Add(new() { TenantId = source, RoleId = 1, MenuId = menu }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(412, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(h, command))).Status);
        template = await ManagementCall(h, (s, session) => s.ReadCreationTemplateAsync(session)); command = command with { ExpectedTemplateVersion = template.Version };
        await h.Db.Sys_Menus.Where(x => x.MenuId == menu).ExecuteUpdateAsync(x => x.SetProperty(m => m.MenuName, "Changed"));
        Assert.Equal(412, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(h, command))).Status);
        foreach (var mutation in new Func<Task>[] {
            async () => { await h.Db.Sys_Menus.Where(x => x.MenuId == menu).ExecuteUpdateAsync(x => x.SetProperty(m => m.Enable, false)); },
            async () => { await h.Db.Sys_Menus.Where(x => x.MenuId == menu).ExecuteDeleteAsync(); },
            async () => { await h.Db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == source && x.RoleId == 1).ExecuteDeleteAsync(); },
            async () => { await h.Db.Sys_Roles.IgnoreQueryFilters().Where(x => x.TenantId == source && x.RoleId == 1).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false)); } })
        {
            await mutation(); var counts = await CreationCounts(h);
            Assert.Equal("TENANT_TEMPLATE_NOT_READY", (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(h, command))).Message);
            Assert.Equal(counts, await CreationCounts(h)); Assert.Null(await CreationRead(h, command.OperationRef));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_audit_and_final_failure_rollback_unknown_commit_recovers_without_password()
    {
        await using var h = await TenantManagementSeed(); var template = await CreationTemplateSeed(h); var command = CreationCommand(template); var counts = await CreationCounts(h);
        var constraint = "creation_" + Guid.NewGuid().ToString("N");
        await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT {constraint} CHECK (\"RequestTenantCode\" <> '{command.Code}')");
        try { await Assert.ThrowsAsync<DbUpdateException>(() => CreationWrite(h, command)); Assert.Equal(counts, await CreationCounts(h)); }
        finally { await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" DROP CONSTRAINT {constraint}"); }
        var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => ManagementCall(h, (s, session) => s.CreateVersionedAsync(command, session),
            _ => ++calls == 2 ? throw new IOException("fixture final session lost") : Task.FromResult(h.User)));
        Assert.Equal(counts, await CreationCounts(h)); Assert.Null(await CreationRead(h, command.OperationRef));
        Assert.Equal("TENANT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => CreationWrite(h, command, new NumberLostCommit { Armed = true }))).Message);
        var original = await CreationRead(h, command.OperationRef); Assert.NotNull(original); SameCreation(original.Receipt, await CreationWrite(h, command));
        Assert.Equal(1, await h.Db.Sys_Tenants.CountAsync(x => x.TenantCode == command.Code));
        Assert.Single(await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.RequestTenantCode == command.Code).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_http_current_session_CSRF_original_and_first_admin_login()
    {
        await using var h = await TenantManagementSeed(); await using var ordinary = await Seed(); await using var other = await TenantManagementSeed();
        var template = await CreationTemplateSeed(h); var command = CreationCommand(template);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        const string path = "api/platform/tenant/creation-v1";
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix, object? body = null, bool csrf = true)
            => host.Send(OperationsRequest(cookies, method, path + suffix, body, csrf));
        using (var response = await Send(HttpMethod.Get, "/template")) Assert.Equal(template.Version, (await NumberHttpData<TenantCreationTemplateV1>(response)).Version);
        using (var response = await Send(HttpMethod.Post, "/commands", command, false)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Send(HttpMethod.Post, "/commands", new { command.OperationRef, command.ExpectedTemplateVersion, command.Code, command.Name, command.ExpireDate, command.Remark, command.AdminUserName, ActorId = h.User }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        TenantCreationResultV1 first;
        using (var response = await Send(HttpMethod.Post, "/commands", command)) first = await NumberHttpData<TenantCreationResultV1>(response);
        Assert.NotNull(first.TempPassword);
        using (var response = await Send(HttpMethod.Post, "/commands", command)) SameCreation(first.Receipt, await NumberHttpData<TenantCreationResultV1>(response));
        using (var response = await Send(HttpMethod.Post, "/commands/read", new { command.OperationRef })) SameCreation(first.Receipt, await NumberHttpData<TenantCreationResultV1>(response));
        var ordinaryCookies = await Login(host, ordinary); var otherCookies = await Login(host, other);
        using (var denied = await host.Send(OperationsRequest(ordinaryCookies, HttpMethod.Get, path + "/template"))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await host.Send(OperationsRequest(otherCookies, HttpMethod.Post, path + "/commands/read", new { command.OperationRef }))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var login = await host.Send(new(HttpMethod.Post, "api/auth/login") { Content = JsonContent.Create(new { UserName = command.AdminUserName, Password = first.TempPassword, TenantCode = command.Code }) }))
        {
            Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
            using var profile = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            Assert.True(profile.RootElement.GetProperty("mustChangePassword").GetBoolean());
        }
        using (var logout = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/auth/logout", new { }))) Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using (var denied = await Send(HttpMethod.Post, "/commands/read", new { command.OperationRef })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_waiting_command_reads_native_template_change_before_copy()
    {
        await using var h = await TenantManagementSeed(); var template = await CreationTemplateSeed(h); var command = CreationCommand(template);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = CreationWrite(h, command);
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS \"Value\"").SingleAsync(timeout.Token))
            { Assert.False(attempt.IsCompleted); await Task.Delay(20, timeout.Token); }
            await h.Db.Sys_Menus.Where(x => x.MenuId == template.MenuIds[0]).ExecuteUpdateAsync(x => x.SetProperty(m => m.MenuName, "Changed while waiting"));
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal(412, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => attempt)).Status);
        Assert.False(await h.Db.Sys_Tenants.AnyAsync(x => x.TenantCode == command.Code)); Assert.Null(await CreationRead(h, command.OperationRef));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_creation_v1_http_rechecks_platform_after_wait_without_creating()
    {
        await using var h = await TenantManagementSeed(); await using var spare = await TenantManagementSeed(); var command = CreationCommand(await CreationTemplateSeed(h));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/platform/tenant/creation-v1/commands", command));
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS \"Value\"").SingleAsync(timeout.Token))
            { if (attempt.IsCompleted) Assert.Fail(await OperationsFailure(await attempt)); await Task.Delay(20, timeout.Token); }
            await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        }
        finally { await tx.CommitAsync(); }
        using var response = await attempt; Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
        Assert.False(await h.Db.Sys_Tenants.AnyAsync(x => x.TenantCode == command.Code)); Assert.Null(await CreationRead(spare, command.OperationRef));
    }
}
