using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task ResourceCatalogue_W203_forward_migration_preserves_original_bytes_and_grants()
    {
        const string baseline = "20261124161000_PlanningDemandSourceIntakeV1";
        const string migration = "20261126031000_ResourceAssignmentAndCapabilityApprovalV1";
        const string kind = "MACHINE";
        var database = await server.CreateIsolatedDatabaseAsync(baseline);
        await using var h = await SeedForDatabase(database);
        Assert.Equal(baseline, (await h.Db.Database.GetAppliedMigrationsAsync()).Last());
        var pending = (await h.Db.Database.GetPendingMigrationsAsync()).ToArray();
        Assert.NotEmpty(pending);
        Assert.Equal(migration, pending[0]);

        var (_, draft, unit) = await CapabilitySetup(h);
        var item = Assert.Single(draft.Body.Capabilities) with { ValidFrom = "2020-01-01T00:00:00Z", ValidTo = null };
        var first = draft with { Body = draft.Body with { Capabilities = [item] } };
        var second = first with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", Body = first.Body with { Basis = "Second W203 declaration" } };
        var id = first.Body.Source.Id;
        var source = await new ResourceSourceOwnerV1(h.Db, RoutingAuthority(h)).ReadAsync(kind, id);
        var firstReceipt = await SeedW203Declaration(h, first, 1, source, unit);
        var secondReceipt = await SeedW203Declaration(h, second, 2, source, unit);
        var firstOriginal = await W203Original(h, firstReceipt);
        var secondOriginal = await W203Original(h, secondReceipt);
        using var firstBody = JsonDocument.Parse(firstOriginal.Body);
        using var secondBody = JsonDocument.Parse(secondOriginal.Body);
        Assert.False(firstBody.RootElement.TryGetProperty("approval", out _));
        Assert.False(secondBody.RootElement.TryGetProperty("approval", out _));
        Assert.Equal(firstReceipt, JsonSerializer.Deserialize<ResourceSourceResultV1>(firstOriginal.Receipt));
        Assert.Equal(secondReceipt, JsonSerializer.Deserialize<ResourceSourceResultV1>(secondOriginal.Receipt));

        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "mes-machine").Select(m => m.MenuId).SingleAsync();
        var grants = h.Db.Sys_RoleActions.IgnoreQueryFilters().OrderBy(g => g.Id)
            .Select(g => new { g.Id, g.TenantId, g.RoleId, g.MenuId, g.ActionCode });
        var beforeGrants = await grants.ToArrayAsync();
        Assert.Contains(beforeGrants, g => g.TenantId == h.Tenant && g.MenuId == menu && g.ActionCode == "capability-edit");
        Assert.DoesNotContain(beforeGrants, g => g.TenantId == h.Tenant && g.ActionCode == "capability-approve");
        var approvalAction = h.Db.Sys_MenuActions.Where(a => a.MenuId == menu && a.ActionCode == "capability-approve");
        Assert.False(await approvalAction.AnyAsync());

        await h.Db.GetService<IMigrator>().MigrateAsync(migration);
        Assert.Equal(migration, (await h.Db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(pending[1..], (await h.Db.Database.GetPendingMigrationsAsync()).ToArray());
        Assert.True(await approvalAction.AnyAsync());
        Assert.Equal(beforeGrants, await grants.ToArrayAsync());

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var path = CapabilityPath + "/" + id;
        var original = (await CatalogueCall(host, cookies, HttpMethod.Get, path + "?version=1"))
            .Deserialize<ResourceCapabilitySnapshotV1>(JsonSerializerOptions.Web)!;
        var current = (await CatalogueCall(host, cookies, HttpMethod.Get, path))
            .Deserialize<ResourceCapabilitySnapshotV1>(JsonSerializerOptions.Web)!;
        Assert.False(original.IsCurrent);
        Assert.True(current.IsCurrent);
        foreach (var (snapshot, receipt, before) in new[] { (original, firstReceipt, firstOriginal), (current, secondReceipt, secondOriginal) })
        {
            Assert.Equal(new ResourceCapabilityRefV1(ResourceCapabilityValuesV1.Owner, kind, id, receipt.Version, receipt.Digest), snapshot.Reference);
            Assert.Equal(before.Body, Encoding.UTF8.GetBytes(snapshot.OriginalJson));
            Assert.Null(snapshot.Approval);
            var recovered = (await CatalogueCall(host, cookies, HttpMethod.Get, CapabilityPath + "/operations/" + receipt.OperationId))
                .Deserialize<ResourceSourceOperationV1>(JsonSerializerOptions.Web)!;
            Assert.Equal("COMMITTED", recovered.State);
            Assert.Equal(receipt, recovered.Result);
            var after = await W203Original(h, receipt);
            Assert.Equal(before.Body, after.Body);
            Assert.Equal(before.Input, after.Input);
            Assert.Equal(before.Receipt, after.Receipt);
            Assert.Equal(before.Audit, after.Audit);
        }

        var reading = (await CatalogueCall(host, cookies, HttpMethod.Get, path + "/capability?key=WIDTH"))
            .Deserialize<ResourceCapabilityReadingV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("DECLARED_NOT_QUALIFIED", reading.State);
        Assert.Null(reading.Approval);
        await CatalogueCall(host, cookies, HttpMethod.Post, CapabilityPath + "/approvals",
            new ResourceCapabilityApprovalCommandV1(Guid.NewGuid(), current.Reference, "Synthetic migration permission check"),
            HttpStatusCode.Forbidden);
        Assert.Equal(beforeGrants, await grants.ToArrayAsync());

        await DeviceEvidence("wp08-16-w203-forward.json", new
        {
            baseline, migration, database, machine = id,
            originals = new[] { (firstReceipt, firstOriginal), (secondReceipt, secondOriginal) }.Select(row => new
            {
                row.Item1.OperationId, row.Item1.Version, bodySha256 = row.Item1.Digest,
                inputSha256 = MainOwnerOperationJournalV1.Digest(row.Item2.Input),
                receiptSha256 = MainOwnerOperationJournalV1.Digest(row.Item2.Receipt),
                auditSha256 = MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(row.Item2.Audit))
            }),
            exactOriginalsMatch = true, originalOperationStates = "COMMITTED", currentVersion = current.Reference.Version,
            historyIsCurrent = original.IsCurrent, capabilityState = reading.State,
            grantCountBeforeAndAfter = beforeGrants.Length, approvalActionRegistered = true, approvalHttpStatus = 403
        });
    }

    // Persist the pre-approval W203 format while its original constraints are active.
    private static Task<ResourceSourceResultV1> SeedW203Declaration(Harness h, ResourceCapabilityCommandV1 input,
        long revision, ResourceSourceVersionV1 source, UomProfileVersionV1 unit)
    {
        var tenant = h.Tenant;
        var kind = source.Reference.Kind;
        var id = source.Reference.Id;
        return RoutingAuthority(h).ExecuteAsync(OperationsOwnerV1.Machine, "capability-edit", true, async (access, token) =>
        {
            await access.RequireResourceSource(h.Db, kind, id, token);
            var actor = access.ActorId;
            if (revision == 1)
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_resource_capability_v1.heads(tenant_id,kind,resource_id,revision,creator_id)
                    VALUES({tenant},{kind},{id},{revision},{actor})
                    """, token);
            else
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_resource_capability_v1.heads SET revision={revision}
                    WHERE tenant_id={tenant} AND kind={kind} AND resource_id={id}
                    """, token);
            var version = revision.ToString(CultureInfo.InvariantCulture);
            var body = EngineeringProductStoreV1.Encode(new ResourceCapabilityStoredV1(tenant, id, kind, version, actor, actor,
                DateTimeOffset.UtcNow, input.Body, input.Reason, source.OriginalJson, [new(unit.Reference, unit.OriginalJson)], null));
            var digest = EngineeringProductStoreV1.Digest(body);
            var sourceRevision = ResourceSourceStoreV1.Revision(source.Reference.Version);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_resource_capability_v1.versions(tenant_id,kind,resource_id,revision,source_revision,operation_id,actor_id,body,digest)
                VALUES({tenant},{kind},{id},{revision},{sourceRevision},{input.OperationId},{actor},{body},{digest})
                """, token);
            var receipt = new ResourceSourceResultV1(input.OperationId, kind, id, version, digest, true);
            var reference = new ResourceCapabilityRefV1(ResourceCapabilityValuesV1.Owner, kind, id, version, digest);
            await new MainOwnerOperationJournalV1(h.Db).Store(ResourceCapabilityValuesV1.Journal(kind), ResourceCapabilityValuesV1.Contract,
                input.OperationId, actor, MainOwnerOperationJournalV1.Encode(input), receipt, [id],
                new { action = "record-resource-capability-declaration", actor, input.Reason, previous = input.ExpectedVersion,
                    reference, qualification = "DECLARED_NOT_QUALIFIED", receipt.Changed }, token);
            return receipt;
        }, default);
    }

    private sealed record W203OriginalRow(byte[] Body, byte[] Input, byte[] Receipt, string Audit);

    private static Task<W203OriginalRow> W203Original(Harness h, ResourceSourceResultV1 receipt) =>
        h.Db.Database.SqlQuery<W203OriginalRow>($"""
            SELECT v.body AS "Body", j.input_bytes AS "Input", j.result_bytes AS "Receipt", j.audit_json::text AS "Audit"
            FROM cp6_resource_capability_v1.versions v
            JOIN cp6_main_owner_v1.operations j ON j.tenant_id=v.tenant_id AND j.owner=v.journal AND j.operation_id=v.operation_id
            WHERE v.tenant_id={h.Tenant} AND v.kind={receipt.Kind} AND v.resource_id={receipt.ObjectId} AND v.operation_id={receipt.OperationId}
            """).SingleAsync();
}
