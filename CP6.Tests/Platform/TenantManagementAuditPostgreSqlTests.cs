using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using System.Security.Claims;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class TenantManagementAuditPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Create_rolls_back_when_required_management_audit_is_rejected()
    {
        await EnsureDefaultAdminMenu();
        var requestTenant = Guid.NewGuid();
        var tenant = new TenantContext { CurrentTenantId = requestTenant };
        await using var db = server.Context(tenant);
        var service = new TenantAdminService(db, new TestHasher());
        var before = await Counts(db);
        var logTable = db.Model.FindEntityType(typeof(Sys_SecurityLog))!.GetTableName()!;
        var quoted = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(logTable, "public");
        await FixtureDdl(db, $"ALTER TABLE {quoted} ADD CONSTRAINT reject_management_audit CHECK (\"RequestTenantCode\" <> 'AUDIT-FAIL' OR \"EventType\" <> {(int)SecurityEventType.TenantCreated})");

        var error = await Record.ExceptionAsync(() => service.CreateAsync("AUDIT-FAIL", "Audit failure", null, null, "fixture_admin"));

        await using var observer = server.Context(tenant);
        AssertConstraint(error, "reject_management_audit");
        Assert.Equal(before, await Counts(observer));
        Assert.Equal(requestTenant, tenant.CurrentTenantId);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [TenantManagementPostgresTheory]
    [InlineData("update")]
    [InlineData("suspend")]
    [InlineData("reactivate")]
    public async Task Audit_rejection_rolls_back_each_existing_tenant_change(string operation)
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var db = server.Context(tenant);
        var service = new TenantAdminService(db, new TestHasher());
        var code = "T" + Guid.NewGuid().ToString("N");
        var created = await service.CreateAsync(code, "original", null, "original remark", "admin");
        if (operation == "reactivate") await service.SuspendAsync(created.TenantId);
        var before = await Counts(db);
        var original = await db.Sys_Tenants.AsNoTracking().SingleAsync(x => x.Id == created.TenantId);
        var type = operation switch { "update" => SecurityEventType.TenantUpdated, "suspend" => SecurityEventType.TenantSuspended, _ => SecurityEventType.TenantReactivated };
        await FixtureDdl(db, $"ALTER TABLE {Table<Sys_SecurityLog>(db)} ADD CONSTRAINT reject_{code} CHECK (\"RequestTenantCode\" <> '{code}' OR \"EventType\" <> {(int)type})");

        var error = await Record.ExceptionAsync(() => operation switch
        {
            "update" => service.UpdateAsync(created.TenantId, "changed", new DateTime(2030, 1, 1), "changed remark", "UTC"),
            "suspend" => service.SuspendAsync(created.TenantId),
            _ => service.ReactivateAsync(created.TenantId)
        });

        AssertConstraint(error, ("reject_" + code).ToLowerInvariant());
        await using var observer = server.Context(tenant);
        var after = await observer.Sys_Tenants.SingleAsync(x => x.Id == created.TenantId);
        Assert.Equal((original.TenantName, original.Enable, original.ExpireDate, original.Remark, original.TimeZoneId),
            (after.TenantName, after.Enable, after.ExpireDate, after.Remark, after.TimeZoneId));
        Assert.Equal(before, await Counts(observer));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [CurrentPermissionPostgresFact]
    public async Task Management_events_are_once_per_call_with_platform_actor_and_target()
    {
        await EnsureDefaultAdminMenu();
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var requestTenant = tenant.CurrentTenantId;
        var actor = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Name, "platform operator")
        }, "fixture"));
        var current = new CurrentUserAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
        await using var db = server.Context(tenant);
        var service = new TenantAdminService(db, new BCryptPasswordHasher(4), current);
        var code = "T" + Guid.NewGuid().ToString("N");
        var created = await service.CreateAsync(code, "original", null, null, "admin");
        await service.UpdateAsync(created.TenantId, "changed", new DateTime(2030, 1, 1), "remark", "UTC");
        await service.SuspendAsync(created.TenantId);
        await service.ReactivateAsync(created.TenantId);
        await using var observer = server.Context(tenant);
        var logs = await observer.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.RequestTenantCode == code).ToArrayAsync();
        Assert.Equal(new[] { SecurityEventType.TenantCreated, SecurityEventType.TenantUpdated,
            SecurityEventType.TenantSuspended, SecurityEventType.TenantReactivated }.Select(x => (int)x).Order(),
            logs.Select(x => x.EventType).Order());
        Assert.All(logs, log =>
        {
            Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
            Assert.Equal(actor, log.UserId); Assert.Equal("platform operator", log.UserName);
            Assert.Contains($"tenant={created.TenantId:D}", log.Reason);
            Assert.DoesNotContain(created.TempPassword, log.Reason!);
        });
        var user = await observer.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.TenantId == created.TenantId);
        Assert.Contains(await observer.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == created.TenantId)
            .Select(x => x.MenuId).ToArrayAsync(), id => id == 70018);
        Assert.True(new BCryptPasswordHasher(4).Verify(created.TempPassword, user.Password));
        Assert.True(user.MustChangePassword); Assert.False(user.IsPlatformAdmin);
        Assert.Equal(requestTenant, tenant.CurrentTenantId);
        Assert.Null(db.Database.CurrentTransaction);
    }

    private async Task EnsureDefaultAdminMenu()
    {
        await using var db = server.Context(new TenantContext());
        if (!await db.Sys_Roles.AnyAsync(x => x.RoleId == 1))
            db.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "fixture administrator" });
        if (!await db.Sys_Menus.AnyAsync(x => x.MenuId == 70018))
            db.Sys_Menus.Add(new Sys_Menu { MenuId = 70018, MenuName = "fixture menu", MenuKey = "tenant-audit-fixture" });
        if (!await db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 1 && x.MenuId == 70018))
            db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 70018 });
        await db.SaveChangesAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Caller_transaction_keeps_prior_work_after_late_field_audit_failure()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var db = server.Context(tenant);
        var service = new TenantAdminService(db, new TestHasher());
        var code = "T" + Guid.NewGuid().ToString("N");
        var prior = await service.CreateAsync(code + "P", "prior", null, null, "admin");
        var target = await service.CreateAsync(code, "original", null, null, "admin");
        var before = await Counts(db);
        // NOT VALID keeps prior audit history; only future rows for this request tenant are rejected.
        await FixtureDdl(db, $"ALTER TABLE {Table<Sys_FieldAuditLog>(db)} ADD CONSTRAINT late_{code} CHECK (\"TenantId\" <> '{tenant.CurrentTenantId:D}'::uuid) NOT VALID");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"TenantName\"='prior uncommitted' WHERE \"Id\"={prior.TenantId}");
        var error = await Record.ExceptionAsync(() => service.UpdateAsync(target.TenantId, "must rollback", null, null));
        AssertConstraint(error, ("late_" + code).ToLowerInvariant());
        Assert.Same(transaction, db.Database.CurrentTransaction);
        await using (var beforeCommit = server.Context(tenant))
            Assert.Equal("prior", (await beforeCommit.Sys_Tenants.SingleAsync(x => x.Id == prior.TenantId)).TenantName);
        // The caller explicitly chooses to commit its earlier, independent work.
        await transaction.CommitAsync();
        await using var observer = server.Context(tenant);
        Assert.Equal("prior uncommitted", (await observer.Sys_Tenants.SingleAsync(x => x.Id == prior.TenantId)).TenantName);
        Assert.Equal("original", (await observer.Sys_Tenants.SingleAsync(x => x.Id == target.TenantId)).TenantName);
        Assert.Equal(before, await Counts(observer));
    }

    [TenantManagementPostgresTheory]
    [InlineData("snapshot")]
    [InlineData("normal_outbox")]
    [InlineData("priority_outbox")]
    public async Task Identity_stage_failure_rolls_back_management_audit_and_business(string stage)
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var code = "T" + Guid.NewGuid().ToString("N");
        Guid id;
        await using (var seed = server.Context(tenant))
            id = (await new TenantAdminService(seed, new TestHasher()).CreateAsync(code, "original", null, null, "admin")).TenantId;
        var runtime = Runtime(id);
        await using (var initial = server.Context(tenant, runtime))
            await new TenantAdminService(initial, new TestHasher()).UpdateAsync(id, "original", new DateTime(2030, 1, 1), null);
        var before = await IdentityState(tenant, id);
        await using var db = server.Context(tenant, runtime);
        var table = stage switch
        {
            "snapshot" => "crm_identity.\"Snapshot\"",
            "normal_outbox" => "crm_identity.\"Cp6_OutboxMessage\"",
            _ => "crm_identity_priority.\"Cp6_OutboxMessage\""
        };
        var constraint = "fail_" + stage + "_" + id.ToString("N");
        await FixtureDdl(db, $"ALTER TABLE {table} ADD CONSTRAINT {constraint} CHECK (\"TenantId\" <> '{id:D}'::uuid) NOT VALID");
        var service = new TenantAdminService(db, new TestHasher());
        var error = await Record.ExceptionAsync(() => stage == "priority_outbox"
            ? service.SuspendAsync(id)
            : service.UpdateAsync(id, "must rollback", new DateTime(2031, 1, 1), "must rollback"));
        AssertConstraint(error, constraint);
        Assert.Equal(before, await IdentityState(tenant, id));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [CurrentPermissionPostgresFact]
    public async Task Caller_rollback_removes_management_audit_snapshot_and_priority_message_together()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var code = "T" + Guid.NewGuid().ToString("N");
        Guid id;
        await using (var seed = server.Context(tenant))
            id = (await new TenantAdminService(seed, new TestHasher()).CreateAsync(code, "original", null, null, "admin")).TenantId;
        var before = await IdentityState(tenant, id);
        await using var db = server.Context(tenant, Runtime(id));
        await using var transaction = await db.Database.BeginTransactionAsync();
        await new TenantAdminService(db, new TestHasher()).SuspendAsync(id);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        Assert.False((await db.Sys_Tenants.AsNoTracking().SingleAsync(x => x.Id == id)).Enable);
        Assert.Equal(1, (await db.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id)).Version);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={id}
            """).SingleAsync());
        // A distinct connection sees none of the caller's pending business or identity effects.
        Assert.Equal(before, await IdentityState(tenant, id));
        await transaction.RollbackAsync();
        Assert.Equal(before, await IdentityState(tenant, id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancellation_interrupts_actual_audit_lock_wait_and_rolls_back_creation()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var locker = server.Context(tenant);
        var before = await Counts(locker);
        var table = Table<Sys_SecurityLog>(locker);
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await FixtureDdl(locker, $"LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE");
        await using var db = server.Context(tenant, null, new CaptureAuditCommand(table, reached));
        using var cancel = new CancellationTokenSource();
        var code = "C" + Guid.NewGuid().ToString("N");
        var creating = new TenantAdminService(db, new TestHasher()).CreateAsync(code, "cancelled", null, null, "admin", cancel.Token);
        var released = false;
        try
        {
            var pid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = false;
            for (var attempt = 0; attempt < 100 && !waiting; attempt++)
            {
                waiting = await locker.Database.SqlQuery<bool>($"""
                    SELECT COALESCE(wait_event_type='Lock',false) AS "Value" FROM pg_catalog.pg_stat_activity WHERE pid={pid}
                    """).SingleAsync();
                if (!waiting) await Task.Delay(20);
            }
            Assert.True(waiting, "The actual audit command must be waiting in PostgreSQL.");
            await cancel.CancelAsync();
            var completed = await Task.WhenAny(creating, Task.Delay(TimeSpan.FromSeconds(3))) == creating;
            await transaction.RollbackAsync(); released = true;
            if (!completed) await creating;
            Assert.True(completed, "Cancellation must finish before the audit relation lock is released.");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await creating);
            Assert.Null(db.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            await using var observer = server.Context(tenant);
            Assert.Equal(before, await Counts(observer));
        }
        finally
        {
            if (!released) await transaction.RollbackAsync();
            try { await creating; } catch (OperationCanceledException) { }
        }
    }

    private sealed class CaptureAuditCommand(string qualifiedTable, TaskCompletionSource<int> reached) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // EF may omit the public schema in generated INSERTs.
            var table = qualifiedTable.Replace("public.", "", StringComparison.Ordinal).Replace("\"public\".", "", StringComparison.Ordinal);
            if (command.CommandText.Contains(table, StringComparison.Ordinal))
                reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_platform_gates_and_required_audit_failure_use_the_real_tenant_controller()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var actor = new Sys_User { UserName = "operator", Password = "fixture-only-hash", IsPlatformAdmin = true };
        await using (var seed = server.Context(tenant))
        {
            // This case tests revocation of the request actor, not removal of the last
            // platform administrator. Keep an independent recovery administrator.
            seed.Sys_Users.AddRange(actor, new Sys_User { UserName = "recovery", Password = "fixture-only-hash", IsPlatformAdmin = true });
            await seed.SaveChangesAsync();
        }
        await using var host = await TenantManagementHttpHost.Start(server);
        var code = "H" + Guid.NewGuid().ToString("N");
        object Request(string suffix) => new { Code = code + suffix, Name = "HTTP tenant", AdminUserName = "admin" };
        async Task<HttpResponseMessage> Send(string token, string suffix)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/platform/tenant") { Content = JsonContent.Create(Request(suffix)) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await host.Client.SendAsync(request);
        }
        using (var response = await Send(host.Token(actor.Id, tenant.CurrentTenantId, platform: false), "N"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Send(host.Token(actor.Id, tenant.CurrentTenantId, impersonation: true), "I"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using (var revoke = server.Context(tenant))
        {
            (await revoke.Sys_Users.SingleAsync(x => x.Id == actor.Id)).IsPlatformAdmin = false;
            await revoke.SaveChangesAsync();
        }
        var signedPlatform = host.Token(actor.Id, tenant.CurrentTenantId);
        using (var response = await Send(signedPlatform, "R")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using (var restore = server.Context(tenant))
        {
            (await restore.Sys_Users.SingleAsync(x => x.Id == actor.Id)).IsPlatformAdmin = true;
            await restore.SaveChangesAsync();
        }
        Guid created;
        using (var response = await Send(signedPlatform, "OK"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            created = (await response.Content.ReadFromJsonAsync<CreateTenantResult>())!.TenantId;
        }
        await using var verify = server.Context(tenant);
        var audit = await verify.Sys_SecurityLogs.IgnoreQueryFilters().SingleAsync(x => x.RequestTenantCode == code + "OK");
        Assert.Equal(actor.Id, audit.UserId); Assert.Equal(TenantContext.DefaultTenant, audit.TenantId);
        Assert.Contains(created.ToString("D"), audit.Reason);
        Assert.Equal(1, await verify.Sys_Tenants.CountAsync(x => x.TenantCode.StartsWith(code)));
        var before = await Counts(verify);
        await FixtureDdl(verify, $"ALTER TABLE {Table<Sys_SecurityLog>(verify)} ADD CONSTRAINT http_{code} CHECK (\"RequestTenantCode\" <> '{code}FAIL')");
        using (var response = await Send(signedPlatform, "FAIL"))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var observer = server.Context(tenant);
        Assert.Equal(before, await Counts(observer));
    }

    private static CrmIdentityRuntime Runtime(Guid id)
    {
        const string issuer = "https://tenant-management.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        options.Tenants.Add(id, "us");
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        return new CrmIdentityRuntime(options, new IdentityEventValidator(bundle, issuer));
    }

    private async Task<string[]> IdentityState(ITenantContext tenant, Guid id)
    {
        await using var db = server.Context(tenant);
        var row = await db.Sys_Tenants.SingleAsync(x => x.Id == id);
        var result = new List<string> { $"tenant:{row.Enable}:{row.TenantName}:{row.ExpireDate:O}:{row.Remark}",
            "counts:" + string.Join(',', await Counts(db)) };
        result.AddRange((await db.CrmIdentitySnapshots.Where(x => x.TenantId == id).ToArrayAsync())
            .Select(x => $"snapshot:{x.AggregateId}:{x.Version}:{x.PayloadSha256}:{Convert.ToHexString(x.RowVersion)}"));
        result.AddRange((await db.CrmIdentityTenantGenerations.Where(x => x.TenantId == id).ToArrayAsync())
            .Select(x => $"generation:{x.Generation}"));
        result.AddRange((await db.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id).ToArrayAsync())
            .Select(x => $"normal:{x.MessageId}:{x.AggregateVersion}:{x.PayloadSha256}"));
        await using var priority = server.PriorityContext();
        result.AddRange((await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id).ToArrayAsync())
            .Select(x => $"priority:{x.MessageId}:{x.AggregateVersion}:{x.PayloadSha256}"));
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static string Table<T>(CP6Context db)
        => db.GetService<ISqlGenerationHelper>().DelimitIdentifier(db.Model.FindEntityType(typeof(T))!.GetTableName()!, "public");

    // DDL identifiers are EF-quoted; all literal values are test constants, GUIDs or enum integers.
    private static Task<int> FixtureDdl(CP6Context db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    private static void AssertConstraint(Exception? error, string constraint)
    {
        var native = Assert.IsType<PostgresException>(Assert.IsType<DbUpdateException>(error).InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, native.SqlState);
        Assert.Equal(constraint, native.ConstraintName);
    }

    private static async Task<int[]> Counts(CP6Context db) =>
    [
        await db.Sys_Tenants.CountAsync(), await db.Sys_Users.IgnoreQueryFilters().CountAsync(),
        await db.Sys_Roles.IgnoreQueryFilters().CountAsync(), await db.Sys_RoleMenus.IgnoreQueryFilters().CountAsync(),
        await db.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync(), await db.Sys_FieldAuditLogs.IgnoreQueryFilters().CountAsync()
    ];

    // Hashing is outside this transaction failure scenario; no password or secret is logged.
    private sealed class TestHasher : IPasswordHasher
    {
        public string Hash(string plain) => "fixture-only-hash";
        public bool Verify(string plain, string hash) => hash == "fixture-only-hash";
        public bool IsHashed(string value) => value == "fixture-only-hash";
    }
}
