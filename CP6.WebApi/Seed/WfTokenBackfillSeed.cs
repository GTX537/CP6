using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>在途实例 token 回填（幂等，守卫"无 token 才建"）。迁移前建的 Running/Suspended 实例补一个根 token。</summary>
public static class WfTokenBackfillSeed
{
    public static async Task EnsureAsync(CP6Context db)
    {
        var insts = await db.Wf_FlowInstances
            .Where(i => i.Status == FlowInstanceStatus.Running || i.Status == FlowInstanceStatus.Suspended)
            .ToListAsync();

        foreach (var inst in insts)
        {
            if (await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == inst.Id)) continue;   // 守卫：有 token 跳过

            var tok = new Wf_FlowToken
            {
                Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = inst.CurrentNode,
                Status = FlowTokenStatus.Active, ParentTokenId = null, ForkId = null,
                Creator = inst.StarterId.ToString(),
            };
            db.Wf_FlowTokens.Add(tok);

            var tasks = await db.Wf_FlowTasks
                .Where(t => t.InstanceId == inst.Id && t.TokenId == null
                            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
                .ToListAsync();
            foreach (var t in tasks) t.TokenId = tok.Id;
        }
        await db.SaveChangesAsync();
    }
}
