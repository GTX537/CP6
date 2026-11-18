using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string RetentionPath = OperationLogPath + "/retention";
    private const string AuditTelemetryPath = "api/sys/audit/TECH_TELEMETRY";

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_forward_migration_preserves_all_three_legacy_sources_and_provisions_actions_without_grants()
    {
        var tenant = Guid.NewGuid();
        var context = new TenantContext { CurrentTenantId = tenant };
        await using var owner = server.Context(context);
        var database = "audit_migration_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database}");
        await using var db = server.Context(context);
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = database }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261117110000_IdentitySourcePublicationV1");
        const string fieldJson = """[{"Field":"Status","Old":"1","New":"2"}]""";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."Sys_Menus" ("MenuId","MenuKey","MenuName","OrderNo","Enable","CreateDate")
                VALUES(882699,'operlog','迁移验证',1,true,TIMESTAMP '2026-10-02 12:30:00');
            INSERT INTO public."Sys_Tenants" ("Id","TenantCode","TenantName","Enable","TwoFactorMode","CreateDate")
                VALUES({tenant},{tenant.ToString("N")},'旧库',true,0,TIMESTAMP '2026-10-02 12:30:00');
            INSERT INTO public."Sys_OperLogs" ("TenantId","StatusCode","ElapsedMs","CreateDate","IsAlert","RequestBody")
                VALUES({tenant},200,7,TIMESTAMP '2026-10-02 12:34:00',false,'原请求材料');
            INSERT INTO public."Sys_FieldAuditLogs" ("Id","TenantId","EntityName","EntityKey","Operation","Changes","ChangedAt","CreateDate")
                VALUES(gen_random_uuid(),{tenant},'Machine','old-ref',2,{fieldJson},
                    TIMESTAMP '2026-10-02 12:35:00',TIMESTAMP '2026-10-02 12:35:00');
            INSERT INTO public."Sys_SecurityLogs" ("Id","TenantId","EventType","Reason","CreatedAt","CreateDate")
                VALUES(gen_random_uuid(),{tenant},2,'原安全事件',TIMESTAMP '2026-10-02 12:36:00',TIMESTAMP '2026-10-02 12:36:00');
            """);
        var before = await Native();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(before, await Native());
        Assert.Null((await db.Sys_OperLogs.SingleAsync()).AuditAtUtc);
        Assert.Null((await db.Sys_SecurityLogs.SingleAsync()).AuditEvidenceJson);
        Assert.Null((await db.Sys_FieldAuditLogs.SingleAsync()).AuditAtUtc);
        await CheckDefinitions(tenant);
        var future = Guid.NewGuid();
        db.Sys_Tenants.Add(new() { Id = future, TenantCode = future.ToString("N"), TenantName = "新租户" });
        await db.SaveChangesAsync();
        await CheckDefinitions(future);

        Task<string> Native() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
                (SELECT to_jsonb(l)-'AuditAtUtc'-'AuditEvidenceJson' FROM public."Sys_OperLogs" l WHERE l."TenantId"={tenant}),
                (SELECT to_jsonb(l)-'AuditAtUtc'-'AuditEvidenceJson' FROM public."Sys_SecurityLogs" l WHERE l."TenantId"={tenant}),
                (SELECT to_jsonb(l)-'AuditAtUtc'-'AuditEvidenceJson' FROM public."Sys_FieldAuditLogs" l WHERE l."TenantId"={tenant})
            )::text AS "Value"
            """).SingleAsync();
        async Task CheckDefinitions(Guid id)
        {
            var actions = await db.Sys_MenuActions.IgnoreQueryFilters().Where(a => a.TenantId == id && a.MenuId == 882699)
                .Select(a => a.ActionCode).OrderBy(x => x).ToArrayAsync();
            Assert.Equal(new[] { "export", "read-content", "retention-manage" }, actions);
            Assert.False(await db.Sys_RoleActions.IgnoreQueryFilters().AnyAsync(a => a.TenantId == id && a.MenuId == 882699));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_actual_platform_summary_filters_only_trusted_targets_and_rechecks_current_platform_role()
    {
        await using var h = await Seed(); await using var foreign = await Seed();
        var route = $"api/platform/audit?tenantCode={foreign.Tenant:N}";
        var platformRecord = new Sys_SecurityLog { TenantId = TenantContext.DefaultTenant, CreatedAt = DateTime.Now,
            EventType = (int)SecurityEventType.TenantUpdated, RequestTenantCode = foreign.Tenant.ToString("N"), Reason = "PRIVATE-PLATFORM" };
        var attempt = new Sys_SecurityLog { TenantId = h.Tenant, CreatedAt = DateTime.Now, EventType = (int)SecurityEventType.LoginFailed,
            RequestTenantCode = foreign.Tenant.ToString("N"), UserAgent = "PRIVATE-LOGIN" };
        h.Db.Sys_SecurityLogs.AddRange(platformRecord, attempt); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using (var prior = await Read()) Assert.Equal(HttpStatusCode.Forbidden, prior.StatusCode);
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, true));
        cookies = await Login(host, h);
        using (var allowed = await Read())
        {
            Assert.True(allowed.Headers.CacheControl?.NoStore);
            var data = await JsonResult(allowed);
            Assert.Equal(1, data.GetProperty("total").GetInt32());
            var row = Assert.Single(data.GetProperty("rows").EnumerateArray());
            Assert.Equal(platformRecord.Id, row.GetProperty("id").GetGuid());
            Assert.DoesNotContain("PRIVATE-", row.GetRawText());
        }
        // 另保留一名平台管理员，使原有末位保护不替代撤权场景。
        await foreign.Db.Sys_Users.Where(u => u.Id == foreign.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, true));
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        using var revoked = await Read();
        Assert.Contains(revoked.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.DoesNotContain(platformRecord.Id.ToString("D"), await revoked.Content.ReadAsStringAsync());

        Task<HttpResponseMessage> Read() => host.Send(OperationsRequest(cookies, HttpMethod.Get, route));
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_tenant_security_content_and_export_apply_current_field_policy()
    {
        await using var h = await Seed(); await using var foreign = await Seed();
        const string contentPath = "api/sys/audit/SECURITY_EVENT";
        const string nativeIp = "192.0.2.34", nativeUa = "SIM-NATIVE-UA", nativeReason = "SIM-NATIVE-REASON";
        const string nativeContext = "{\"diagnostic\":\"SIM-NATIVE-CONTEXT\"}";
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "sys-security-log");
        if (menu == null)
        {
            menu = new() { MenuId = 882694, MenuKey = "sys-security-log", MenuName = "模拟安全日志" };
            h.Db.Sys_Menus.Add(menu); await h.Db.SaveChangesAsync();
        }
        foreach (var action in new[] { "query", "read-content", "export" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }

        var own = new Sys_SecurityLog { TenantId = h.Tenant, UserId = h.User, CreatedAt = DateTime.Now,
            EventType = (int)SecurityEventType.LoginFailed, ClientIp = nativeIp, UserAgent = nativeUa, Reason = nativeReason, ContextJson = nativeContext };
        var other = new Sys_SecurityLog { TenantId = foreign.Tenant, UserId = foreign.User, CreatedAt = DateTime.Now,
            EventType = own.EventType, ClientIp = "192.0.2.99" };
        h.Db.Sys_SecurityLogs.Add(own); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        foreign.Db.Sys_SecurityLogs.Add(other); await foreign.Db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var list = await JsonResult(await Send(HttpMethod.Get, $"api/sys/security-log?eventType={own.EventType}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Check(Assert.Single(list.GetProperty("rows").EnumerateArray()));
        Check(await JsonResult(await Send(HttpMethod.Get, $"{contentPath}/records/{own.Id:D}")));
        using (var hidden = await Send(HttpMethod.Get, $"{contentPath}/records/{other.Id:D}"))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var export = await JsonResult(await Send(HttpMethod.Post, $"{contentPath}/exports", new AuditExportRequestV1(
            Guid.NewGuid(), list.GetProperty("policyVersion").GetString()!, new(EventType: own.EventType))));
        var file = await JsonResult(await Send(HttpMethod.Get, $"{contentPath}/exports/{export.GetProperty("id").GetGuid():D}/download"));
        Check(Assert.Single(file.GetProperty("rows").EnumerateArray()));
        var original = await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x => x.Id == own.Id);
        Assert.Equal((nativeIp, nativeUa, nativeReason), (original.ClientIp, original.UserAgent, original.Reason));
        using var native = JsonDocument.Parse(original.ContextJson!);
        Assert.Equal("SIM-NATIVE-CONTEXT", native.RootElement.GetProperty("diagnostic").GetString());

        Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
            => host.Send(OperationsRequest(cookies, method, path, body));
        void Check(JsonElement row)
        {
            Assert.Equal(own.Id, row.GetProperty("id").GetGuid());
            Assert.DoesNotContain(nativeIp, row.GetRawText());
            Assert.DoesNotContain("SIM-NATIVE-", row.GetRawText());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_legacy_and_explicit_cleanup_exclude_indeterminate_alert_and_native_references()
    {
        await using var h = await OperationLogSeed();
        await using var other = await OperationLogSeed();
        var eligible = await OperationLogIds(h);
        var utc = new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero);
        var durableRef = Guid.NewGuid();
        var bank = new Sys_OperLog { Controller = "BankReconciliation", Action = "GenerateBankVoucherLine", HttpMethod = "POST",
            StatusCode = 200, CreateDate = utc.DateTime, RequestBody = "SIM-VOUCHER-RECEIPT" };
        var digestReference = NewTelemetry(utc, "OWNER_RECORDED");
        digestReference.AuditEvidenceJson = new AuditEvidenceV1("OWNER_UTC", "BEST_EFFORT_TELEMETRY", "USER",
            Outcome: "OWNER_RECORDED", OriginalOperationDigest: AuditBrowseV1.NativeDigest("已冻结原命令")).Encode();
        var records = new[] { bank, digestReference, NewTelemetry(utc, "UNKNOWN"), NewTelemetry(utc, "PROCESSING"),
            NewTelemetry(utc, "RESCHEDULING"), NewTelemetry(utc, "DISPUTE"), NewTelemetry(utc, "OWNER_RECORDED", durableRef),
            NewTelemetry(utc, "HTTP_OBSERVED", alert: true), NewTelemetry(DateTimeOffset.UtcNow.AddDays(-1), "HTTP_OBSERVED") };
        h.Db.Sys_OperLogs.AddRange(records);
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var protectedIds = records.Select(x => x.Id).ToArray();
        var original = await OperationLogRows(h, protectedIds);
        var otherIds = await OperationLogIds(other);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var cookies = await Login(host, h);
        var plan = Guid.NewGuid(); var hold = Guid.NewGuid();
        var preview = await JsonResult(await Send(HttpMethod.Post, $"{RetentionPath}/plans/{plan:D}?retentionClass=TECH_TELEMETRY"));
        Assert.Equal(3, preview.GetProperty("eligible").GetInt32());
        Assert.Equal("READY", preview.GetProperty("state").GetString());

        // 保留授权先进入真实 PostgreSQL 最终门，执行不得使用预览时的旧许可。
        await using var control = new NpgsqlConnection(connection); await control.OpenAsync();
        await using var observer = new NpgsqlConnection(connection); await observer.OpenAsync();
        var version = Guid.NewGuid();
        Task<HttpResponseMessage> execution;
        await using (var locking = await control.BeginTransactionAsync())
        {
            await using var change = new NpgsqlCommand("""
                SELECT cp6_authentication_v1.acquire();
                INSERT INTO cp6_audit_v1.safeguards(tenant_id,retention_class,native_id,reference_id,kind,active,version,owner_id)
                VALUES(@tenant,'TECH_TELEMETRY',@id,@reference,'HOLD',true,@version,@owner)
                """, control, locking);
            change.Parameters.AddWithValue("tenant", h.Tenant);
            change.Parameters.AddWithValue("id", eligible[0].ToString(System.Globalization.CultureInfo.InvariantCulture));
            change.Parameters.AddWithValue("reference", hold); change.Parameters.AddWithValue("version", version); change.Parameters.AddWithValue("owner", h.User);
            await change.ExecuteNonQueryAsync();
            execution = Send(HttpMethod.Post, $"{RetentionPath}/plans/{plan:D}/execute");
            var wait = await OperationLogControlWait(observer, control.ProcessID, execution);
            Assert.NotNull(wait); Assert.Equal(control.ProcessID, wait.Blocker);
            await locking.CommitAsync();
        }
        var blocked = await JsonResult(await execution);
        Assert.Equal("BLOCKED", blocked.GetProperty("state").GetString());
        Assert.Equal("SAFEGUARDS_CHANGED_OR_ACTIVE", blocked.GetProperty("reason").GetString());
        Assert.Equal(eligible, await OperationLogIds(h));

        using var cleared = await Send(HttpMethod.Delete, OperationLogPath);
        var applied = await JsonResult(cleared);
        Assert.Equal(2, applied.GetProperty("count").GetInt32());
        var receipt = applied.GetProperty("receipt");
        Assert.Equal(h.User, receipt.GetProperty("actualActorId").GetGuid());
        var originalReceipt = await JsonResult(await Send(HttpMethod.Get, $"{OperationLogPath}/legacy-clear"));
        Assert.Equal(receipt.GetRawText(), originalReceipt.GetRawText());
        await JsonResult(await Send(HttpMethod.Post, $"{RetentionPath}/safeguards/TECH_TELEMETRY",
            new AuditSafeguardChangeV1(hold, eligible[0].ToString(System.Globalization.CultureInfo.InvariantCulture), "HOLD", false, version)));
        var replay = await JsonResult(await Send(HttpMethod.Delete, OperationLogPath));
        Assert.Equal(receipt.GetRawText(), replay.GetProperty("receipt").GetRawText());
        Assert.Equal([eligible[0]], await OperationLogIds(h));
        Assert.Equal(original, await OperationLogRows(h, protectedIds));
        Assert.Equal(otherIds, await OperationLogIds(other));
        Assert.DoesNotContain("SIM-VOUCHER", receipt.GetRawText());
        Assert.DoesNotContain("candidates", receipt.GetRawText());
        await DeviceEvidence("audit-v1-retention.json", new { Blocked = blocked, Applied = originalReceipt,
            OriginalPlanUnchanged = true, Held = eligible[0], Protected = protectedIds.Length, OtherTenantUnchanged = true });

        Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
            => host.Send(OperationsRequest(cookies, method, path, body));
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_export_freezes_native_ids_and_reauthorizes_every_download()
    {
        await using var h = await OperationLogSeed(); await using var other = await OperationLogSeed();
        var at = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var a = NewTelemetry(at, "HTTP_OBSERVED"); var b = NewTelemetry(at, "UNKNOWN");
        var end = NewTelemetry(at.AddMinutes(1), "HTTP_OBSERVED");
        h.Db.Sys_OperLogs.AddRange(a, b, end); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var actor = await h.Db.Sys_Menus.Where(x => x.MenuKey == "operlog").Select(x => x.MenuId).SingleAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h); var foreign = await Login(host, other);
        const string range = "&fromUtc=2026-11-10T10%3A00%3A00Z&toUtc=2026-11-10T10%3A01%3A00Z";
        var list = await JsonResult(await Send(HttpMethod.Get, OperationLogPath + "?pageSize=1" + range));
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        var first = Assert.Single(list.GetProperty("rows").EnumerateArray());
        Assert.Equal(b.Id, first.GetProperty("id").GetInt32());
        Assert.Equal("UNKNOWN", first.GetProperty("outcome").GetString());
        var second = await JsonResult(await Send(HttpMethod.Get, OperationLogPath + "?pageSize=1&cursor=" + list.GetProperty("nextCursor").GetString() + range));
        Assert.Equal(a.Id, Assert.Single(second.GetProperty("rows").EnumerateArray()).GetProperty("id").GetInt32());
        var detail = await JsonResult(await Send(HttpMethod.Get, $"{AuditTelemetryPath}/records/{a.Id}"));
        Assert.Equal("TECH_TELEMETRY", list.GetProperty("evidenceClass").GetString());
        Assert.Equal("HTTP_OBSERVED", detail.GetProperty("evidence").GetProperty("outcome").GetString());
        var request = new AuditExportRequestV1(Guid.NewGuid(), list.GetProperty("policyVersion").GetString()!,
            new(FromUtc: at, ToUtc: at.AddMinutes(1)));
        var export = await JsonResult(await Send(HttpMethod.Post, AuditTelemetryPath + "/exports", request));
        var exportId = export.GetProperty("id").GetGuid();
        var downloadPath = $"{AuditTelemetryPath}/exports/{exportId:D}/download";
        h.Db.Sys_OperLogs.Add(NewTelemetry(at, "HTTP_OBSERVED")); await h.Db.SaveChangesAsync();
        using var download = await Send(HttpMethod.Get, downloadPath);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.True(download.Headers.CacheControl?.NoStore);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(export.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal([b.Id, a.Id], document.RootElement.GetProperty("rows").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToArray());
        Assert.DoesNotContain("SIM-SECRET", document.RootElement.GetRawText());
        using (var hidden = await host.Send(OperationsRequest(foreign, HttpMethod.Get, downloadPath)))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        await h.Db.Sys_RoleActions.Where(x => x.MenuId == actor && x.ActionCode == "read-content").ExecuteDeleteAsync();
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[] {
            (HttpMethod.Get, downloadPath, null), (HttpMethod.Get, $"{AuditTelemetryPath}/records/{a.Id}", null),
            (HttpMethod.Post, AuditTelemetryPath + "/exports", request) })
        {
            using var denied = await Send(method, path, body);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain("SIM-SECRET", await denied.Content.ReadAsStringAsync());
        }
        var safe = await JsonResult(await Send(HttpMethod.Get, OperationLogPath + "?pageSize=1" + range));
        Assert.Equal(3, safe.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, Assert.Single(safe.GetProperty("rows").EnumerateArray()).GetProperty("evidence").ValueKind);
        await DeviceEvidence("audit-v1-download.json", new { Export = export, NativeIds = new[] { b.Id, a.Id },
            CrossTenant = "404", Revoked = "403", ListAfterRevocation = safe.GetProperty("total").GetInt32() });

        Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
            => host.Send(OperationsRequest(cookies, method, path, body));
    }

    private static Sys_OperLog NewTelemetry(DateTimeOffset time, string outcome, Guid? operation = null, bool alert = false)
        => new() { Controller = "AuditV1Fixture", Action = "FixedAction", HttpMethod = "POST", StatusCode = 200,
            RequestUrl = "/api/sim/{nativeId}", RequestBody = "SIM-SECRET", CreateDate = time.UtcDateTime.ToLocalTime(),
            AuditAtUtc = time, IsAlert = alert, AuditEvidenceJson = new AuditEvidenceV1("REQUEST_RECEIVED_UTC", "BEST_EFFORT_TELEMETRY",
                "UNBOUND", OperationId: operation, Outcome: outcome, RecordedAtUtc: time, DataPolicy: "HTTP_TELEMETRY_V1").Encode() };

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_worker_lost_commit_acknowledgement_recovers_original_postgresql_receipt()
    {
        await using var h = await OperationLogSeed(approvedPolicy: false);
        await using var other = await OperationLogSeed(approvedPolicy: false);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var basisRef = Guid.NewGuid(); var policyRef = Guid.NewGuid();
        var published = await JsonResult(await host.Send(OperationsRequest(cookies, HttpMethod.Put, $"{RetentionPath}/policies/TECH_TELEMETRY",
            new AuditRetentionPolicyChangeV1(null, policyRef, basisRef, true, true, 7, "UTC"))));
        var policyVersion = published.GetProperty("version").GetGuid();
        var fault = new AuditLostWorkerAcknowledgement();
        await using var services = new ServiceCollection().AddScoped<CP6Context>(_ =>
            server.Context(new TenantContext(), null, fault)).BuildServiceProvider();
        var worker = new OperLogCleanupService(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), NullLogger<OperLogCleanupService>.Instance);
        var otherBefore = await OperationLogIds(other);
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => worker.CleanupOnceAsync());
        Assert.Equal("AUDIT_RETENTION_OUTCOME_UNKNOWN", error.Message);
        Assert.Empty(await OperationLogIds(h));
        var original = await WorkerReceiptAsync();
        Assert.Contains(AuditRetentionV1.Worker, original);
        Assert.Contains(policyVersion.ToString("D"), original);
        Assert.Contains("COMPLETED", original);

        var newerEligible = NewTelemetry(DateTimeOffset.UtcNow.AddDays(-45), "HTTP_OBSERVED");
        h.Db.Sys_OperLogs.Add(newerEligible); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await worker.CleanupOnceAsync();
        Assert.Equal(original, await WorkerReceiptAsync());
        Assert.True(await h.Db.Sys_OperLogs.AnyAsync(x => x.Id == newerEligible.Id));
        Assert.Equal(otherBefore, await OperationLogIds(other));
        await DeviceEvidence("audit-v1-worker-unknown.json", new { Failure = error.Message, Receipt = original,
            CommitCallbackCount = fault.Commits, NextEligibleUnchanged = true, OtherTenantUnchanged = true });

        async Task<string> WorkerReceiptAsync() => await h.Db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object('plan',p.id,'service',p.service,'policy',p.policy_version,'state',r.state,'affected',r.affected,'at',r.at)::text
              AS "Value" FROM cp6_audit_v1.plans p
            JOIN cp6_audit_v1.receipts r ON p.tenant_id=r.tenant_id AND p.id=r.plan_id
            WHERE p.tenant_id={h.Tenant} AND p.mode='WORKER'
            """).SingleAsync();
    }

    private sealed class AuditLostWorkerAcknowledgement : DbTransactionInterceptor
    {
        public int Commits;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref Commits) == 2) throw new IOException("模拟处置提交后回执传输中断");
            return Task.CompletedTask;
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditV1_policy_change_blocks_old_plan_and_anonymization_preserves_allowed_native_metadata()
    {
        await using var h = await OperationLogSeed(approvedPolicy: false);
        var records = await OperationLogIds(h);
        await h.Db.Sys_OperLogs.Where(x => records.Contains(x.Id)).ExecuteUpdateAsync(x => x.SetProperty(r => r.RequestBody, "SIM-SECRET"));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var version = Guid.Empty;
        var p1 = await Publish("DELETE", []);
        version = p1.GetProperty("version").GetGuid();
        var oldPlan = Guid.NewGuid(); var newPlan = Guid.NewGuid();
        await Preview(oldPlan);
        using (var rejected = await host.Send(OperationsRequest(cookies, HttpMethod.Put, $"{RetentionPath}/policies/TECH_TELEMETRY",
            new AuditRetentionPolicyChangeV1(version, Guid.NewGuid(), Guid.NewGuid(), true, false, 7, "UTC", "ANONYMIZE", ["statusCode"]))))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var newPolicy = await Publish("ANONYMIZE", ["requestBody"]);
        var refused = await Execute(oldPlan);
        Assert.Equal("POLICY_CHANGED", refused.GetProperty("reason").GetString());
        Assert.Equal(3, await h.Db.Sys_OperLogs.CountAsync(x => records.Contains(x.Id) && x.RequestBody == "SIM-SECRET"));
        var ready = await Preview(newPlan);
        Assert.Equal("ANONYMIZE", ready.GetProperty("method").GetString());
        var receipt = await Execute(newPlan);
        Assert.Equal(3, receipt.GetProperty("affected").GetInt32());
        var after = await h.Db.Sys_OperLogs.AsNoTracking().Where(x => records.Contains(x.Id)).ToArrayAsync();
        Assert.Equal(3, after.Length);
        Assert.All(after, x => { Assert.Null(x.RequestBody); Assert.Equal("SIM operator", x.UserName); Assert.Equal(200, x.StatusCode); });
        Assert.Equal(receipt.GetRawText(), (await Execute(newPlan)).GetRawText());
        Assert.Equal(0, (await Preview(Guid.NewGuid())).GetProperty("eligible").GetInt32());
        await DeviceEvidence("audit-v1-anonymize.json", new { Previous = refused, Policy = newPolicy, Receipt = receipt });

        Task<JsonElement> Publish(string method, string[] fields) => Send(HttpMethod.Put, $"{RetentionPath}/policies/TECH_TELEMETRY",
            new AuditRetentionPolicyChangeV1(version == Guid.Empty ? null : version, Guid.NewGuid(), Guid.NewGuid(), true, false, 7, "UTC", method, fields));
        Task<JsonElement> Preview(Guid id) => Send(HttpMethod.Post, $"{RetentionPath}/plans/{id:D}?retentionClass=TECH_TELEMETRY");
        Task<JsonElement> Execute(Guid id) => Send(HttpMethod.Post, $"{RetentionPath}/plans/{id:D}/execute");
        async Task<JsonElement> Send(HttpMethod method, string url, object? data = null)
            => await JsonResult(await host.Send(OperationsRequest(cookies, method, url, data)));
    }

    private static async Task<JsonElement> JsonResult(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await OperationsFailure(response)}");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }
    }
}
