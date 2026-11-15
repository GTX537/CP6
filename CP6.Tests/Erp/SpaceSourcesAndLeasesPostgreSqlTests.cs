using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SpaceSourceUploadAction = "source:upload";
    private const string SpaceLeaseTakeoverAction = "model:lease:takeover";

    [TenantManagementPostgresTheory]
    [InlineData("attach", "pause-first")]
    [InlineData("remove", "pause-first")]
    [InlineData("acquire", "pause-first")]
    [InlineData("renew", "pause-first")]
    [InlineData("release", "pause-first")]
    [InlineData("takeover", "pause-first")]
    [InlineData("attach", "business-first")]
    [InlineData("remove", "business-first")]
    [InlineData("acquire", "business-first")]
    [InlineData("renew", "business-first")]
    [InlineData("release", "business-first")]
    [InlineData("takeover", "business-first")]
    [InlineData("attach", "permission-first")]
    [InlineData("takeover", "permission-first")]
    public async Task PGT017_space_source_and_lease_owners_use_the_current_authority(string operation, string order)
    {
        var sourceOperation = operation is "attach" or "remove";
        var revokeAction = sourceOperation ? SpaceSourceUploadAction : SpaceLeaseTakeoverAction;
        var ownerFirst = order == "business-first";
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        var menuId = await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction, SpaceLeaseTakeoverAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, lease) = await SeedSpaceDesignDraft(space, execution, site, storeLease: operation != "acquire");
        var request = await SourceOrLeaseInput(space, execution, operation, version, floor, lease);
        await using var host = await AuthenticationApiProcess.Start(connection, SpaceDesignSettings(writer.Tenant, site));
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var before = await SourceLeaseWriteSet(space);
        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        if (order == "permission-first")
        {
            Task<HttpResponseMessage> pending;
            FenceWait wait;
            await using (var revoke = await control.BeginTransactionAsync())
            {
                await using var delete = new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire();
                    DELETE FROM public."Sys_RoleAction"
                    WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"=@action;
                    """, control, revoke);
                delete.Parameters.AddWithValue("tenant", writer.Tenant);
                delete.Parameters.AddWithValue("menu", menuId);
                delete.Parameters.AddWithValue("action", revokeAction);
                Assert.Equal(1, await delete.ExecuteNonQueryAsync());
                pending = Send();
                wait = await WaitForFence(observer, control.ProcessID, "%cp6_authentication_v1.acquire%", null, pending);
                await revoke.CommitAsync();
            }
            using var denied = await pending;
            var deniedState = await SourceLeaseWriteSet(space);
            await DeviceEvidence($"space-source-lease-{operation}-{order}.json",
                new { Operation = operation, Revoked = revokeAction, Wait = wait, Status = (int)denied.StatusCode, Before = before, After = deniedState });
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(denied));
            Assert.Equal(before, deniedState);
            await GrantSpaceDesignActions(writer, revokeAction);
            using var granted = await Send();
            Assert.True(granted.StatusCode == request.Status, await OperationsFailure(granted));
            Assert.NotEqual(before, await SourceLeaseWriteSet(space));
            await AssertSourceLeaseEffect(space, operation, lease, version.Id);
            return;
        }

        var table = sourceOperation ? "Space_IdempotencyRecord" : "Space_EditLease";
        Task<HttpResponseMessage> command, pause;
        FenceWait commandWait, pauseWait;
        await using (var barrier = await business.BeginTransactionAsync())
        {
            using var quoting = new NpgsqlCommandBuilder();
            await using var hold = new NpgsqlCommand(
                $"LOCK TABLE public.{quoting.QuoteIdentifier(table)} IN SHARE MODE", business, barrier);
            await hold.ExecuteNonQueryAsync();
            if (ownerFirst)
            {
                command = Send();
                commandWait = await WaitForFence(observer, business.ProcessID, $"%{table}%", null, command);
                pause = Pause();
                pauseWait = await WaitForFence(observer, commandWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
                Assert.Equal(before, await SourceLeaseWriteSet(space));
            }
            else
            {
                await using var held = await control.BeginTransactionAsync();
                await using var tenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", control, held);
                tenant.Parameters.AddWithValue("tenant", writer.Tenant);
                Assert.Equal(1, await tenant.ExecuteScalarAsync());
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                command = Send();
                commandWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID, $"space-source-lease-{operation}", command);
                await held.RollbackAsync();
                await pause;
            }
            await barrier.RollbackAsync();
        }
        using var result = await command;
        using var paused = await pause;
        var suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
        Assert.False(suspended.Snapshot.Enable);
        var after = await SourceLeaseWriteSet(space);
        await DeviceEvidence($"space-source-lease-{operation}-{order}.json",
            new { Operation = operation, Order = order, commandWait, pauseWait, PausedRevision = suspended.Snapshot.Revision,
                Status = (int)result.StatusCode, Before = before, After = after });
        if (ownerFirst)
        {
            Assert.Equal(commandWait.Backend, pauseWait.Blocker);
            Assert.True(result.StatusCode == request.Status, await OperationsFailure(result));
            Assert.NotEqual(before, after);
            await AssertSourceLeaseEffect(space, operation, lease, version.Id);
        }
        else
        {
            Assert.Equal(pauseWait.Backend, commandWait.Blocker);
            Assert.True(result.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(result));
            Assert.Equal(before, after);
        }
        using (var obsolete = await Send())
            Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        Assert.Equal(after, await SourceLeaseWriteSet(space));

        using var restored = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), suspended.Snapshot.Revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);
        actor = await Login(host, writer);
        if (!ownerFirst || sourceOperation)
        {
            using var allowed = await Send();
            Assert.True(allowed.StatusCode == request.Status, await OperationsFailure(allowed));
        }
        var committed = await SourceLeaseWriteSet(space);
        if (ownerFirst) Assert.Equal(after, committed);
        else Assert.NotEqual(before, committed);
        await AssertSourceLeaseEffect(space, operation, lease, version.Id);
        if (sourceOperation)
        {
            using var replay = await Send();
            Assert.True(replay.StatusCode == request.Status, await OperationsFailure(replay));
            Assert.Equal(committed, await SourceLeaseWriteSet(space));
        }

        Task<HttpResponseMessage> Send()
        {
            var message = OperationsRequest(actor, HttpMethod.Post, request.Path, request.Body);
            if (sourceOperation) message.Headers.Add("Idempotency-Key", request.Key);
            return host.Send(message);
        }

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"), new TenantManagementCommandV1(
                Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static async Task<SpaceDesignInput> SourceOrLeaseInput(SpaceContext space, SpaceDesignExecution execution,
        string operation, SpaceModelVersion version, SpaceFloorRevision floor, SpaceEditLease lease)
    {
        var versionPath = $"{SpaceDesignPath}/versions/{version.Id:D}";
        var floorPath = $"{versionPath}/floors/{floor.LogicalId:D}";
        var key = Guid.NewGuid().ToString("N");
        if (operation is "attach" or "remove")
        {
            var file = SpaceFile.CreateUploading(Guid.NewGuid(), execution.TenantId, $"owned/{key}.pdf", "source.pdf",
                "application/pdf", SpaceFileRetentionClass.Source);
            file.CompleteQuarantine("application/pdf", ".pdf", 113, SpaceDesignSampleHash);
            file.BeginScanning();
            file.MarkClean("fixture", "test");
            space.Files.Add(file);
            if (operation == "attach")
            {
                await space.SaveChangesAsync();
                return new($"{versionPath}/sources", new CreateSpaceSourceRequest(file.Id, "Pdf", "Survey"),
                    HttpStatusCode.Created, key);
            }
            space.Versions.Attach(version);
            var source = new SpaceSourceCoordinator(execution).AddFileSource(version, file, SpaceSourceType.Pdf, "Survey");
            space.Sources.Add(source);
            await space.SaveChangesAsync();
            return new($"{versionPath}/sources/{source.Id:D}:remove",
                new RemoveSpaceSourceRequest(version.ContentRevision, Convert.ToBase64String(source.RowVersion)),
                HttpStatusCode.OK, key);
        }
        const HttpStatusCode ok = HttpStatusCode.OK;
        return operation switch
        {
            "acquire" => new($"{floorPath}/lease", new AcquireSpaceEditLeaseRequest(lease.ClientInstanceId), ok, key),
            "renew" or "release" => new($"{floorPath}/lease/{lease.LeaseId:D}:{operation}",
                new ContinueSpaceEditLeaseRequest(lease.ClientInstanceId), ok, key),
            "takeover" => new($"{floorPath}/lease:takeover", new TakeoverSpaceEditLeaseRequest(Guid.NewGuid(), "Simulated takeover"), ok, key),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<string> SourceLeaseWriteSet(SpaceContext space) => JsonSerializer.Serialize(new
    {
        Versions = await space.Versions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Files = await space.Files.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Sources = await space.Sources.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == space.CurrentTenantId).OrderBy(x => x.Id).ToArrayAsync(),
        Receipts = await space.IdempotencyRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Leases = await space.EditLeases.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Audits = await space.EditLeaseTakeoverAudits.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });

    private static async Task AssertSourceLeaseEffect(SpaceContext space, string operation, SpaceEditLease previous, Guid version)
    {
        if (operation is "attach" or "remove")
        {
            var source = await space.Sources.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                x.TenantId == space.CurrentTenantId && x.ModelVersionId == version);
            Assert.Equal(SpaceSourceState.Ready, source.State);
            Assert.Equal(operation == "remove", source.IsDeleted);
            Assert.Equal(SpaceFileState.Clean, (await space.Files.AsNoTracking().SingleAsync(x => x.Id == source.FileId)).State);
            Assert.Equal(1, await space.IdempotencyRecords.CountAsync());
            return;
        }
        var lease = await space.EditLeases.AsNoTracking().SingleAsync(x => x.ModelVersionId == version);
        if (operation == "takeover")
        {
            var audit = await space.EditLeaseTakeoverAudits.AsNoTracking().SingleAsync();
            Assert.Equal(previous.LeaseId, audit.PreviousLeaseId);
            Assert.Equal(lease.LeaseId, audit.NewLeaseId);
            Assert.NotEqual(previous.LeaseId, lease.LeaseId);
        }
        else
        {
            Assert.Equal(previous.ClientInstanceId, lease.ClientInstanceId);
            if (operation == "release") Assert.Equal(lease.LastRenewedAtUtc, lease.ExpiresAtUtc);
            else Assert.True(lease.LastRenewedAtUtc >= previous.LastRenewedAtUtc);
        }
    }
}
