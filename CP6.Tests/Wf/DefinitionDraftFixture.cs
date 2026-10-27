using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;
internal static class DefinitionDraftFixture
{
    // Pinning/unit fixtures explicitly arrange the next draft. PostgreSQL Owner creation is covered separately.
    internal static async Task FromPublished(CP6Context db, Guid sourceId)
    {
        var source = await db.Wf_FlowDefVersions.AsNoTracking().SingleAsync(x => x.Id == sourceId && x.Status == 1);
        db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = source.FlowDefId,
            Version = await db.Wf_FlowDefVersions.Where(x => x.FlowDefId == source.FlowDefId).MaxAsync(x => x.Version) + 1,
            Status = 0, FlowNameSnapshot = source.FlowNameSnapshot, SchemaJson = source.SchemaJson });
        await db.SaveChangesAsync();
    }
}
