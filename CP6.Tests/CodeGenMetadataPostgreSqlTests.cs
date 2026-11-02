using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static CodeGenMetadataDocumentV1 MetadataDocument(string name = "Demo") => new(
        new(name, "Erp", "Erp_Demo", "demo", null, null, "api/erp/demo", "元数据\0末尾 "),
        ImmutableArray.Create(new CodeGenMetadataColumnV1("Name", "string", "标签\0保留 ", false, true, true, 1)),
        CodeGenService.PreviewTemplateRef, CodeGenService.PreviewTemplateVersion);
    private CodeGenMetadataOwnerV1 MetadataOwner(Harness h, Guid id, params IInterceptor[] interceptors)
        => new(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, interceptors), h.Tenant, id, (_, _) => Task.FromResult(h.User));
    private async Task<Harness> MetadataSeed()
    {
        var h = await Seed(); await EngineeringAction(h, "pub-codegen", "view"); await EngineeringAction(h, "pub-codegen", "save"); return h;
    }
    private static void SameMetadata(object? expected, object? actual) => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    private static string MetadataPath(Guid id, string suffix = "") => $"api/pub/codegen/metadata-v1/{id}" + suffix;

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_upgrade_preserves_existing_legacy_rows_and_remains_idempotent()
    {
        await using var h = await MetadataSeed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var name = "codegen_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        {
            await root.OpenAsync(); await using var create = new NpgsqlCommand("CREATE DATABASE " + name, root);
            await create.ExecuteNonQueryAsync();
        }
        settings.Database = name;
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql); var policy = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString,
            policy.MigrationsAssembly, policy.HistoryTable, policy.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext());
        await db.GetService<IMigrator>().MigrateAsync("20261102040000_NumberRuleChangesV1");
        var old = CodeGenPreviewTests.Input(); old.Table.Id = Guid.NewGuid();
        db.Pub_GenTables.Add(old.Table);
        foreach (var column in old.Columns) column.GenTableId = old.Table.Id;
        db.Pub_GenColumns.AddRange(old.Columns); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        async Task<string[]> Rows() => await db.Database.SqlQueryRaw<string>("""
            SELECT row_to_json(t)::text AS "Value" FROM public."Pub_GenTable" t
            UNION ALL SELECT row_to_json(c)::text AS "Value" FROM public."Pub_GenColumn" c ORDER BY "Value"
            """).ToArrayAsync();
        var before = await Rows(); Assert.NotEmpty(before);
        await db.Database.MigrateAsync(); Assert.Equal(before, await Rows());
        await db.Database.MigrateAsync(); Assert.Equal(before, await Rows()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_codegen_metadata_v1.documents").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_original_CAS_history_and_byte_preservation_are_atomic()
    {
        await using var h = await MetadataSeed(); var id = Guid.NewGuid(); var owner = MetadataOwner(h, id);
        var command = new CodeGenMetadataSaveV1(new string('界', 3000) + "\0/原件", 0, MetadataDocument());
        var first = await owner.SaveAsync(command); Assert.Equal(1, first.Snapshot.Revision);
        SameMetadata(command.Document, first.Snapshot.Document);
        SameMetadata(first, await MetadataOwner(h, id).SaveAsync(command));
        Assert.Equal("CODEGEN_OPERATION_CONFLICT", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.SaveAsync(command with { ExpectedRevision = 1 }))).Message);
        var jobs = new[] { "left", "right" }.Select(async key =>
        {
            try { await MetadataOwner(h, id).SaveAsync(command with { OperationRef = key, ExpectedRevision = 1 }); return 200; }
            catch (CodeGenMetadataExceptionV1 e) { return e.Status; }
        });
        Assert.Equal(new[] { 200, 412 }, (await Task.WhenAll(jobs)).Order().ToArray());
        Assert.Equal(2, (await owner.ReadAsync())!.Revision);
        SameMetadata(first.Snapshot, await owner.ReadAsync(1)); SameMetadata(first, await owner.ReadOperationAsync(command.OperationRef));
        SameMetadata(first, await owner.SaveAsync(command));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_codegen_metadata_v1.versions WHERE tenant_id={h.Tenant}"));
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_codegen_metadata_v1.commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_tenant_names_legacy_ownership_and_current_permissions_are_enforced()
    {
        await using var a = await MetadataSeed(); await using var b = await MetadataSeed(); var id = Guid.NewGuid();
        var command = new CodeGenMetadataSaveV1("original", 0, MetadataDocument());
        var owner = MetadataOwner(a, id); var first = await owner.SaveAsync(command);
        Assert.Null(await MetadataOwner(b, id).ReadAsync());
        var other = await MetadataOwner(b, id).SaveAsync(command); Assert.NotEqual(first.Snapshot.TenantId, other.Snapshot.TenantId);
        Assert.Equal("CODEGEN_ENTITY_NAME_EXISTS", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => MetadataOwner(a, Guid.NewGuid()).SaveAsync(command with { Document = MetadataDocument("demo") }))).Message);
        await using (var db = server.Context(new TenantContext { CurrentTenantId = a.Tenant }))
        {
            var legacy = new CP6.WebApi.Controllers.Pub.CodeGenController(db, new CodeGenService());
            var input = CodeGenPreviewTests.Input(); input.Table.EntityName = "demo";
            Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(await legacy.Save(new(input.Table, input.Columns)));
        }
        var old = CodeGenPreviewTests.Input(); old.Table.EntityName = "Legacy"; old.Table.Id = Guid.NewGuid();
        a.Db.Pub_GenTables.Add(old.Table); old.Columns[0].GenTableId = old.Table.Id; a.Db.Pub_GenColumns.AddRange(old.Columns); await a.Db.SaveChangesAsync(); a.Db.ChangeTracker.Clear();
        Assert.Equal("CODEGEN_LEGACY_METADATA_REQUIRES_ADOPTION", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => MetadataOwner(a, Guid.NewGuid()).SaveAsync(command with { Document = MetadataDocument("legacy") }))).Message);
        Assert.Single(await a.Db.Pub_GenColumns.Where(x => x.GenTableId == old.Table.Id).ToListAsync());
        await a.Db.Sys_RoleActions.Where(x => x.ActionCode == "save").ExecuteDeleteAsync();
        SameMetadata(first, await owner.SaveAsync(command));
        Assert.Equal(403, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.SaveAsync(command with { OperationRef = "new", ExpectedRevision = 1 }))).Status);
        await a.Db.Sys_RoleActions.Where(x => x.ActionCode == "view").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.ReadOperationAsync("original"))).Status);
        SameMetadata(other, await MetadataOwner(b, id).ReadOperationAsync("original"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_final_failure_rolls_back_and_lost_commit_recovers_original()
    {
        await using var h = await MetadataSeed(); var id = Guid.NewGuid(); var calls = 0;
        var command = new CodeGenMetadataSaveV1("lost", 0, MetadataDocument());
        var fail = new CodeGenMetadataOwnerV1(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant }), h.Tenant, id,
            (_, _) => ++calls == 3 ? throw new IOException("before final decision") : Task.FromResult(h.User));
        await Assert.ThrowsAsync<IOException>(() => fail.SaveAsync(command));
        Assert.Null(await MetadataOwner(h, id).ReadAsync()); Assert.Null(await MetadataOwner(h, id).ReadOperationAsync("lost"));
        Assert.Equal("CODEGEN_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => MetadataOwner(h, id, new NumberLostCommit { Armed = true }).SaveAsync(command))).Message);
        var saved = await MetadataOwner(h, id).ReadOperationAsync("lost"); Assert.NotNull(saved);
        SameMetadata(saved, await MetadataOwner(h, id).SaveAsync(command)); Assert.Equal(1, (await MetadataOwner(h, id).ReadAsync())!.Revision);
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_http_real_session_CSRF_tenant_replay_and_history()
    {
        await using var h = await MetadataSeed(); await using var other = await MetadataSeed(); var id = Guid.NewGuid();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix = "", object? body = null, bool csrf = true)
            => host.Send(OperationsRequest(cookies, method, MetadataPath(id, suffix), body, csrf));
        var command = new CodeGenMetadataSaveV1("http\0" + new string('界', 3000), 0, MetadataDocument());
        using (var anonymous = await host.Send(new(HttpMethod.Get, MetadataPath(id)))) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var denied = await Send(HttpMethod.Post, "/saves", command, false)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        CodeGenMetadataReceiptV1 first;
        using (var response = await Send(HttpMethod.Post, "/saves", command)) first = await NumberHttpData<CodeGenMetadataReceiptV1>(response);
        using (var response = await Send(HttpMethod.Get)) SameMetadata(first.Snapshot, await NumberHttpData<CodeGenMetadataSnapshotV1>(response));
        using (var response = await Send(HttpMethod.Get, "/versions/1")) SameMetadata(first.Snapshot, await NumberHttpData<CodeGenMetadataSnapshotV1>(response));
        using (var response = await Send(HttpMethod.Post, "/saves/read", new { command.OperationRef })) SameMetadata(first, await NumberHttpData<CodeGenMetadataReceiptV1>(response));
        var otherCookies = await Login(host, other);
        using (var response = await host.Send(OperationsRequest(otherCookies, HttpMethod.Get, MetadataPath(id)))) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await Send(HttpMethod.Post, "/saves", new { command.OperationRef, command.ExpectedRevision, command.Document, TenantId = other.Tenant })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "save").ExecuteDeleteAsync();
        using (var response = await Send(HttpMethod.Post, "/saves", command)) SameMetadata(first, await NumberHttpData<CodeGenMetadataReceiptV1>(response));
        using (var response = await Send(HttpMethod.Post, "/saves", command with { OperationRef = "new", ExpectedRevision = 1 })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "view").ExecuteDeleteAsync();
        using (var response = await Send(HttpMethod.Get)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await EngineeringAction(h, "pub-codegen", "view");
        using (var logout = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/auth/logout", new { }))) Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using (var response = await Send(HttpMethod.Get)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Codegen_metadata_http_rechecks_credentials_and_save_grant_after_waiting(bool credentials)
    {
        await using var h = await MetadataSeed(); var id = Guid.NewGuid();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = host.Send(OperationsRequest(cookies, HttpMethod.Post, MetadataPath(id, "/saves"), new CodeGenMetadataSaveV1("queued", 0, MetadataDocument())));
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            if (credentials) await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.AuthenticationEpoch, Guid.NewGuid()));
            else await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "save").ExecuteDeleteAsync();
        }
        finally { await tx.CommitAsync(); }
        using var response = await attempt;
        Assert.True(response.StatusCode == (credentials ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden), await OperationsFailure(response));
        Assert.Null(await MetadataOwner(h, id).ReadAsync());
    }
}
