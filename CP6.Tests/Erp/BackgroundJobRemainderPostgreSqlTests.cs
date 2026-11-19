using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_run_distinguishes_attempt_message_and_owner_operation()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, configure: graph =>
        {
            var node = graph.Nodes.Single(n => n.Id == "a");
            node.TimeoutHours = 1;
            node.TimeoutAction = "remind";
        });
        var due = await h.Db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT due_at AS "Value" FROM cp6_native_timeout_v2.deadlines WHERE task_id={form.Task}
            """).SingleAsync();
        var owner = await TimeoutOwner(h, form.Owner, due.AddSeconds(1));
        var claim = Assert.Single(await owner.ClaimAsync());
        var result = await owner.ExecuteAsync(claim);
        using var view = JsonSerializer.SerializeToDocument((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var diagnostics = view.RootElement;
        var runs = Assert.Single(diagnostics.GetProperty("runs").EnumerateArray());
        Assert.Equal("COMPLETED_WITH_VERIFIED_RESULTS", runs.GetProperty("state").GetString());
        Assert.Equal(1, runs.GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, runs.GetProperty("appliedCount").GetInt32());
        Assert.Equal(0, runs.GetProperty("alreadyTerminalCount").GetInt32());
        Assert.Equal(0, runs.GetProperty("blockedCount").GetInt32());
        Assert.Equal(0, runs.GetProperty("unknownCount").GetInt32());
        var unit = Assert.Single(runs.GetProperty("units").EnumerateArray());
        Assert.Equal(claim.ClaimId, unit.GetProperty("attemptId").GetGuid());
        Assert.Equal(claim.ExecutionId, unit.GetProperty("operationId").GetGuid());
        Assert.Equal(result.CommandId, unit.GetProperty("commandId").GetGuid());
        Assert.NotEqual(claim.ExecutionId, runs.GetProperty("runId").GetGuid());
        var message = Assert.Single(unit.GetProperty("messageIds").EnumerateArray()).GetGuid();
        Assert.NotEqual(result.CommandId, message);
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_unapproved_workflow_source_keeps_expired_row()
    {
        await using var h = await Seed();
        var anchor = DateTimeOffset.UtcNow;
        var candidate = RemainderJob(anchor.UtcDateTime.AddDays(-200), ServiceJobStatus.Succeeded);
        h.Db.Wf_ServiceJobs.Add(candidate);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var cleanup = new WfCleanupService(h.Db, new WfsInfraOptions());
        var result = await cleanup.CleanupOnceAsync(anchor.UtcDateTime, default);
        Assert.Equal("RETENTION_SOURCE_NOT_APPROVED", result.ServiceJobsPlan!.Reason);
        Assert.True(await h.Db.Wf_ServiceJobs.AnyAsync(j => j.Id == candidate.Id));
    }

    private static Wf_ServiceJob RemainderJob(DateTime completed, int status) => new()
    {
        Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(), Kind = "timer", NodeId = "service",
        Status = status, DueAtUtc = completed, NextAttemptAtUtc = completed, CompletedAtUtc = completed
    };
}
