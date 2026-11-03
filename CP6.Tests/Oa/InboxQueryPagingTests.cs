using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using System.Data.Common;

namespace CP6.Tests.Oa;

[Collection("TenantManagementPostgres")]
public sealed class InboxQueryPagingTests(TenantManagementPostgresFixture server)
{
    private sealed class Commands(List<string> values):DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,
            InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {values.Add(command.CommandText);return base.ReaderExecutingAsync(command,eventData,result,cancellationToken);}
    }
    [CurrentPermissionPostgresFact]
    public async Task Pending_and_stats_emit_server_side_limit_and_count_queries()
    {
        var commands = new List<string>();
        var tenant=Guid.NewGuid();
        await using var db=server.Context(new TenantContext{CurrentTenantId=tenant},null,new Commands(commands));
        db.Sys_Tenants.Add(new(){Id=tenant,TenantCode=tenant.ToString("N"),TenantName="Inbox pagination fixture"});
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
        await CP6.Tests.Wf.LegacyDefinitionFixture.ArrangePublishedHistory(db);
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
