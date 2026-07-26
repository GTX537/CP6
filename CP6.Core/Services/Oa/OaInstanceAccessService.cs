using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class OaInstanceAccessService : IOaInstanceAccessService
{
    private readonly CP6Context _db;
    private readonly IDelegateService _delegates;

    public OaInstanceAccessService(CP6Context db, IDelegateService delegates)
    {
        _db = db;
        _delegates = delegates;
    }

    public IQueryable<Guid> VisibleInstanceIds(Guid effectiveUserId)
    {
        var started = _db.Wf_FlowInstances
            .Where(x => x.StarterId == effectiveUserId)
            .Select(x => x.Id);
        var tasks = _db.Wf_FlowTasks
            .Where(x => x.AssigneeId == effectiveUserId)
            .Select(x => x.InstanceId);
        var expected = _db.Wf_FlowFormTos
            .Where(x => x.ExpectedHandlerId == effectiveUserId)
            .Select(x => x.InstanceId);
        var actual = _db.Wf_FlowFormTos
            .Where(x => x.ActualHandlerId == effectiveUserId)
            .Select(x => x.InstanceId);
        var onBehalf = _db.Wf_FlowFormTos
            .Where(x => x.OnBehalfOfId == effectiveUserId)
            .Select(x => x.InstanceId);
        var cc = _db.Wf_FlowCcs
            .Where(x => x.RecipientId == effectiveUserId)
            .Select(x => x.InstanceId);

        return started.Concat(tasks).Concat(expected).Concat(actual).Concat(onBehalf).Concat(cc).Distinct();
    }

    public async Task<InstanceAccessDecision> GetAsync(
        Guid actualUserId, Guid effectiveUserId, Guid instanceId, CancellationToken ct = default)
    {
        if (actualUserId != effectiveUserId)
            await _delegates.AssertActiveGrantAsync(actualUserId, effectiveUserId);

        var canRead = await VisibleInstanceIds(effectiveUserId).AnyAsync(x => x == instanceId, ct);
        if (!canRead) throw new UnauthorizedAccessException("E-WF-043");
        return new(instanceId, effectiveUserId, true);
    }
}
