using System.Net;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<Guid> LegacyMetadata(Harness h, string name = "Legacy")
    {
        var old = CodeGenPreviewTests.Input(); old.Table.Id = Guid.NewGuid(); old.Table.EntityName = name;
        old.Table.MenuName = "菜单 原件 "; old.Table.SeqBizKey = null; old.Table.CodeField = null;
        h.Db.Pub_GenTables.Add(old.Table);
        foreach (var column in old.Columns) { column.GenTableId = old.Table.Id; column.Label = "标签 原件 "; }
        h.Db.Pub_GenColumns.AddRange(old.Columns); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return old.Table.Id;
    }
    private static Task<string[]> LegacyRows(CP6Context db, Guid id) => db.Database.SqlQuery<string>($"""
        SELECT row_to_json(t)::text AS "Value" FROM public."Pub_GenTable" t WHERE "Id"={id}
        UNION ALL SELECT row_to_json(c)::text AS "Value" FROM public."Pub_GenColumn" c WHERE "GenTableId"={id} ORDER BY "Value"
        """).ToArrayAsync();

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_adoption_keeps_original_rows_IDs_and_audit_then_saves_by_CAS()
    {
        await using var h = await MetadataSeed(); var id = await LegacyMetadata(h); var owner = MetadataOwner(h, id);
        var rows = await LegacyRows(h.Db, id); var candidate = (await owner.ReadLegacyAsync())!;
        Assert.Equal("菜单 原件 ", candidate.Document.Table.MenuName);
        Assert.Null(candidate.Document.Table.SeqBizKey); Assert.Null(candidate.Document.Table.CodeField);
        Assert.All(candidate.Document.Columns, column => Assert.Equal("标签 原件 ", column.Label));
        var command = new CodeGenMetadataAdoptV1(new string('界', 3000) + "\0/adopt", candidate.SourceFingerprint);
        var first = await owner.AdoptAsync(command); Assert.Equal(id, first.Snapshot.MetadataId); Assert.Equal(1, first.Snapshot.Revision);
        SameMetadata(candidate.Document, first.Snapshot.Document); Assert.Equal(rows, await LegacyRows(h.Db, id));
        SameMetadata(first, await owner.AdoptAsync(command)); SameMetadata(first, await owner.ReadOperationAsync(command.OperationRef));
        Assert.Equal(409, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.AdoptAsync(command with { SourceFingerprint = "changed" }))).Status);
        Assert.Equal("CODEGEN_LEGACY_ALREADY_ADOPTED", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.ReadLegacyAsync())).Message);
        var updated = candidate.Document with { Table = candidate.Document.Table with { EntityName = "Renamed" } };
        var second = await owner.SaveAsync(new("next", 1, updated)); Assert.Equal(2, second.Snapshot.Revision);
        SameMetadata(first.Snapshot, await owner.ReadAsync(1)); SameMetadata(first, await owner.AdoptAsync(command));
        Assert.Equal(rows, await LegacyRows(h.Db, id));
        Assert.Equal("CODEGEN_LEGACY_METADATA_REQUIRES_ADOPTION", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => MetadataOwner(h, Guid.NewGuid()).SaveAsync(new("reuse-name", 0, candidate.Document)))).Message);
        Func<Task>[] writes = [
            () => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Pub_GenTable\" SET \"MenuName\"='changed' WHERE \"Id\"={id}"),
            () => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM public.\"Pub_GenTable\" WHERE \"Id\"={id}"),
            () => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Pub_GenColumn\" SET \"Label\"='changed' WHERE \"GenTableId\"={id}"),
            () => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM public.\"Pub_GenColumn\" WHERE \"GenTableId\"={id}"),
            () => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"Pub_GenColumn\""),
            () => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_codegen_metadata_v1.adoptions WHERE metadata_id={id}")];
        foreach (var write in writes) Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(write)).SqlState);
        var another = await LegacyMetadata(h, "Other");
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.\"Pub_GenColumn\" SET \"GenTableId\"={id} WHERE \"GenTableId\"={another}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.\"Pub_GenColumn\" SET \"GenTableId\"={another} WHERE \"GenTableId\"={id}"))).SqlState);
        Assert.Equal(rows, await LegacyRows(h.Db, id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_adoption_source_change_missing_tenant_and_invalid_original_fail_without_adopting()
    {
        await using var a = await MetadataSeed(); await using var b = await MetadataSeed(); var id = await LegacyMetadata(a);
        var owner = MetadataOwner(a, id); var candidate = (await owner.ReadLegacyAsync())!;
        Assert.Null(await MetadataOwner(b, id).ReadLegacyAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => MetadataOwner(b, id).AdoptAsync(new("cross", candidate.SourceFingerprint)))).Status);
        Assert.Equal("CODEGEN_LEGACY_METADATA_REQUIRES_ADOPTION", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.SaveAsync(new("wrong-entry", 0, MetadataDocument("AnotherName"))))).Message);
        await a.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Pub_GenColumn\" SET \"Label\"='New label' WHERE \"GenTableId\"={id}");
        Assert.Equal(412, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.AdoptAsync(new("stale", candidate.SourceFingerprint)))).Status);
        Assert.Null(await owner.ReadAsync()); Assert.Null(await owner.ReadOperationAsync("stale"));
        await a.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Pub_GenTable\" SET \"Module\"='unsafe/path' WHERE \"Id\"={id}");
        await Assert.ThrowsAsync<CodeGenPreviewValidationException>(() => owner.ReadLegacyAsync());
        await Assert.ThrowsAsync<CodeGenPreviewValidationException>(() => owner.AdoptAsync(new("invalid", candidate.SourceFingerprint)));
        Assert.Null(await owner.ReadAsync()); Assert.Contains("unsafe/path", string.Join("", await LegacyRows(a.Db, id)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_adoption_rollback_and_unknown_commit_recover_same_original_after_save_revocation()
    {
        await using var h = await MetadataSeed(); var id = await LegacyMetadata(h); var owner = MetadataOwner(h, id);
        var candidate = (await owner.ReadLegacyAsync())!; var command = new CodeGenMetadataAdoptV1("unknown", candidate.SourceFingerprint); var calls = 0;
        var fail = new CodeGenMetadataOwnerV1(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant }), h.Tenant, id,
            (_, _) => ++calls == 3 ? throw new IOException("before final decision") : Task.FromResult(h.User));
        await Assert.ThrowsAsync<IOException>(() => fail.AdoptAsync(command)); Assert.NotNull(await owner.ReadLegacyAsync()); Assert.Null(await owner.ReadAsync());
        Assert.Equal("CODEGEN_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => MetadataOwner(h, id, new NumberLostCommit { Armed = true }).AdoptAsync(command))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "save").ExecuteDeleteAsync();
        var recovered = await owner.ReadOperationAsync(command.OperationRef); Assert.NotNull(recovered); SameMetadata(recovered, await owner.AdoptAsync(command));
        Assert.Equal(1, (await owner.ReadAsync())!.Revision);
        Assert.Equal(403, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.AdoptAsync(command with { OperationRef = "new" }))).Status);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "view").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => owner.ReadOperationAsync(command.OperationRef))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_adoption_waits_for_legacy_write_then_rejects_stale_source_and_fences_waiting_old_save()
    {
        await using var h = await MetadataSeed(); var id = await LegacyMetadata(h); var owner = MetadataOwner(h, id);
        var candidate = (await owner.ReadLegacyAsync())!;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Pub_GenColumn\" SET \"Label\"='Legacy writer' WHERE \"GenTableId\"={id}");
            var adopting = owner.AdoptAsync(new("racing", candidate.SourceFingerprint));
            await WaitMetadataLock(h, "LOCK TABLE public.%");
            await tx.CommitAsync(); Assert.Equal(412, (await Assert.ThrowsAsync<CodeGenMetadataExceptionV1>(() => adopting)).Status);
        }
        candidate = (await owner.ReadLegacyAsync())!; var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var held = new CodeGenMetadataOwnerV1(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant }), h.Tenant, id,
            async (_, _) => { if (++calls == 3) { ready.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(12)); } return h.User; });
        var adoption = held.AdoptAsync(new("adopt-held", candidate.SourceFingerprint)); await ready.Task.WaitAsync(TimeSpan.FromSeconds(12));
        Task<Microsoft.AspNetCore.Mvc.IActionResult> oldSave;
        await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        try
        {
            var input = CodeGenPreviewTests.Input(); input.Table.EntityName = "Legacy";
            oldSave = new CP6.WebApi.Controllers.Pub.CodeGenController(db, new CodeGenService()).Save(new(input.Table, input.Columns));
            await WaitMetadataLock(h, "%Pub_Gen%");
        }
        finally { release.TrySetResult(); }
        await adoption;
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(await oldSave);
        SameMetadata(candidate.Document, (await owner.ReadAsync())!.Document);
    }
    private async Task WaitMetadataLock(Harness h, string pattern)
    {
        await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!await observer.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
              AND wait_event_type='Lock' AND query LIKE {pattern}) AS "Value"
            """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_adoption_http_uses_actual_session_original_ID_and_rejects_CSRF_tenant_and_forged_document()
    {
        await using var h = await MetadataSeed(); await using var other = await MetadataSeed(); var id = await LegacyMetadata(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix, object? body = null, bool csrf = true)
            => host.Send(OperationsRequest(cookies, method, MetadataPath(id, suffix), body, csrf));
        CodeGenMetadataLegacyV1 candidate;
        using (var response = await Send(HttpMethod.Get, "/legacy")) candidate = await NumberHttpData<CodeGenMetadataLegacyV1>(response);
        var command = new CodeGenMetadataAdoptV1("http-adopt", candidate.SourceFingerprint);
        using (var response = await Send(HttpMethod.Post, "/legacy/adopt", command, false)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Send(HttpMethod.Post, "/legacy/adopt", new { command.OperationRef, command.SourceFingerprint, candidate.Document })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var others = await Login(host, other);
        using (var response = await host.Send(OperationsRequest(others, HttpMethod.Post, MetadataPath(id, "/legacy/adopt"), command))) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        CodeGenMetadataReceiptV1 first;
        using (var response = await Send(HttpMethod.Post, "/legacy/adopt", command)) first = await NumberHttpData<CodeGenMetadataReceiptV1>(response);
        Assert.Equal(id, first.Snapshot.MetadataId); SameMetadata(candidate.Document, first.Snapshot.Document);
        using (var response = await Send(HttpMethod.Post, "/saves", new CodeGenMetadataSaveV1("version2", 1, candidate.Document))) Assert.Equal(2, (await NumberHttpData<CodeGenMetadataReceiptV1>(response)).Snapshot.Revision);
        var old = CodeGenPreviewTests.Input(); old.Table.EntityName = "Legacy";
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/pub/codegen/save", new { old.Table, old.Columns }))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "save").ExecuteDeleteAsync();
        using (var response = await Send(HttpMethod.Post, "/legacy/adopt", command)) SameMetadata(first, await NumberHttpData<CodeGenMetadataReceiptV1>(response));
        using (var response = await Send(HttpMethod.Post, "/saves/read", new { command.OperationRef })) SameMetadata(first, await NumberHttpData<CodeGenMetadataReceiptV1>(response));
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/auth/logout", new { }))) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await Send(HttpMethod.Post, "/legacy/adopt", command)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Codegen_metadata_adoption_upgrade_keeps_preexisting_V1_documents_and_legacy_rows()
    {
        await using var h = await MetadataSeed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var name = "codegen_adopt_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        { await root.OpenAsync(); await using var create = new NpgsqlCommand("CREATE DATABASE " + name, root); await create.ExecuteNonQueryAsync(); }
        settings.Database = name; var database = new DatabaseOptions(DatabaseProvider.PostgreSql); var policy = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString, policy.MigrationsAssembly, policy.HistoryTable, policy.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext());
        await db.GetService<IMigrator>().MigrateAsync("20261102080000_CodeGenMetadataV1");
        var id = Guid.NewGuid(); var bytes = new byte[] { 0, 255, 42 }; var hash = new byte[32];
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_codegen_metadata_v1.documents VALUES({TenantContext.DefaultTenant},{id},'Existing',1);
                INSERT INTO cp6_codegen_metadata_v1.versions VALUES({TenantContext.DefaultTenant},{id},1,{bytes});
                INSERT INTO cp6_codegen_metadata_v1.commands VALUES({TenantContext.DefaultTenant},{id},{hash},{bytes},{bytes},{h.User},1,{bytes})
                """); await tx.CommitAsync();
        }
        var old = CodeGenPreviewTests.Input(); old.Table.Id = Guid.NewGuid(); db.Pub_GenTables.Add(old.Table);
        foreach (var c in old.Columns) c.GenTableId = old.Table.Id;
        db.Pub_GenColumns.AddRange(old.Columns); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); var rows = await LegacyRows(db, old.Table.Id);
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(rows, await LegacyRows(db, old.Table.Id));
        Assert.Equal(bytes, await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_codegen_metadata_v1.versions WHERE metadata_id={id}").SingleAsync());
        Assert.Equal(bytes, await db.Database.SqlQuery<byte[]>($"SELECT result_bytes AS \"Value\" FROM cp6_codegen_metadata_v1.commands WHERE metadata_id={id}").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_codegen_metadata_v1.adoptions").SingleAsync());
    }
}
