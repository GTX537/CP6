using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public sealed class InboxQueryPagingTests
{
    [Fact]
    public async Task Pending_and_stats_emit_server_side_limit_and_count_queries()
    {
        await using var connection = WfTestDb.NewSqliteWithSchema();
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlite(connection)
            .LogTo(commands.Add, new[] { RelationalEventId.CommandExecuted })
            .Options;
        await using var db = new CP6Context(options);
        var user = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = "approver", Password = "x", Enable = true
        };
        db.Sys_Users.Add(user);
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "flow", FlowName = "Flow", SchemaJson = "{}"
        });
        for (var index = 0; index < 25; index++)
        {
            var instance = new Wf_FlowInstance
            {
                Id = Guid.NewGuid(), FlowKey = "flow", StarterId = Guid.NewGuid(),
                Status = FlowInstanceStatus.Running, CurrentNode = "approve"
            };
            db.Wf_FlowInstances.Add(instance);
            db.Wf_FlowTasks.Add(new Wf_FlowTask
            {
                Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "approve",
                AssigneeId = user.Id, Status = FlowTaskStatus.Pending,
                CreateDate = DateTime.UtcNow.AddMinutes(index)
            });
        }
        await db.SaveChangesAsync();
        commands.Clear();
        var resolver = new ApproverResolver(db);
        var inbox = new InboxService(db, new FlowEngine(db, resolver),
            new ForecastService(db, resolver, new ApprovalStagePlanner(resolver)));

        var page = await inbox.PendingAsync(user.Id, "merged", page: 2, pageSize: 10);
        var stats = await inbox.StatsAsync(user.Id);

        Assert.Equal(10, page.Count);
        Assert.Equal(25, stats.PendingCount);
        Assert.Contains(commands, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));
    }
}
