using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Oa;

public sealed class OaP0HistoricalSqlServerTests
{
    private readonly string? _connectionString =
        OaP0SharedStageSqlServer.GetValidatedConnectionString();

    private CP6Context NewContext() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer(_connectionString!).Options);

    private static string Schema(Guid approver) =>
        $$"""
        {
          "start": "start",
          "nodes": [
            { "id": "start", "type": "start" },
            {
              "id": "approve",
              "type": "approval",
              "approverStrategy": "Specified",
              "approverUserId": "{{approver}}"
            },
            { "id": "end", "type": "end" }
          ],
          "edges": [
            { "from": "start", "to": "approve" },
            { "from": "approve", "to": "end" }
          ]
        }
        """;

    [SqlServerFact]
    public async Task Historical_stage_keeps_v1_pin_and_supports_feature_rollback_without_downgrade()
    {
        await using var db = NewContext();
        var flowKey = $"oa-p0-pin-{Guid.NewGuid():N}";
        var v1Approver = Guid.NewGuid();
        var v2Approver = Guid.NewGuid();
        var definitions = new FlowDefService(db);

        var v1Draft = await definitions.SaveDraftAsync(
            flowKey, "OA P0 pin v1", null, Schema(v1Approver), null, "oa-p0-pin-drill");
        var v1 = await definitions.PublishAsync(flowKey, v1Draft.RowVersion, Guid.NewGuid());
        var engine = new FlowEngine(db, new ApproverResolver(db));
        var v1InstanceId = await engine.SubmitAsync(flowKey, Guid.NewGuid(), "{}");

        var v2Draft = await definitions.SaveDraftAsync(
            flowKey, "OA P0 pin v2", null, Schema(v2Approver), null, "oa-p0-pin-drill");
        var v2 = await definitions.PublishAsync(flowKey, v2Draft.RowVersion, Guid.NewGuid());
        var v2InstanceId = await engine.SubmitAsync(flowKey, Guid.NewGuid(), "{}");

        var v1Instance = await db.Wf_FlowInstances.SingleAsync(x => x.Id == v1InstanceId);
        var v2Instance = await db.Wf_FlowInstances.SingleAsync(x => x.Id == v2InstanceId);
        Assert.Equal(v1.VersionId, v1Instance.FlowDefVersionId);
        Assert.Equal(v2.VersionId, v2Instance.FlowDefVersionId);
        Assert.Equal(v1Approver,
            (await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == v1InstanceId)).AssigneeId);
        Assert.Equal(v2Approver,
            (await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == v2InstanceId)).AssigneeId);

        // Feature/application rollback: close the new-entry path while preserving the
        // expanded schema, pinned instances, and legacy head-table read contract.
        var head = await db.Wf_FlowDefs.SingleAsync(x => x.FlowKey == flowKey);
        head.Enable = false;
        await db.SaveChangesAsync();
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.SubmitAsync(flowKey, Guid.NewGuid(), "{}"));
        Assert.Equal("E-WF-029", blocked.Message);

        var v1Task = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == v1InstanceId);
        await engine.ActAsync(v1Task.Id, v1Approver, true);
        db.ChangeTracker.Clear();

        var legacyRead = await definitions.GetDefAsync(flowKey);
        Assert.NotNull(legacyRead);
        Assert.Equal(2, legacyRead!.Version);
        Assert.False(legacyRead.Enable);
        Assert.False(string.IsNullOrWhiteSpace(legacyRead.SchemaJson));

        var completedV1 = await db.Wf_FlowInstances.SingleAsync(x => x.Id == v1InstanceId);
        var runningV2 = await db.Wf_FlowInstances.SingleAsync(x => x.Id == v2InstanceId);
        Assert.Equal(FlowInstanceStatus.Approved, completedV1.Status);
        Assert.Equal(v1.VersionId, completedV1.FlowDefVersionId);
        Assert.Equal(FlowInstanceStatus.Running, runningV2.Status);
        Assert.Equal(v2.VersionId, runningV2.FlowDefVersionId);
        Assert.Equal(2, await db.Wf_FlowDefVersions.CountAsync(x => x.FlowDefId == v1.DefinitionId));
        Assert.Equal(
            "20260724000423_OaP0DraftAccess",
            await db.Database.SqlQueryRaw<string>(
                    """SELECT TOP(1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC""")
                .SingleAsync());
    }
}
