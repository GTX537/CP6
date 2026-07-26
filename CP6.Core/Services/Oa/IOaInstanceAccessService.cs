namespace CP6.Core.Services.Oa;

public sealed record InstanceAccessDecision(Guid InstanceId, Guid EffectiveUserId, bool CanRead);

public interface IOaInstanceAccessService
{
    Task<InstanceAccessDecision> GetAsync(
        Guid actualUserId, Guid effectiveUserId, Guid instanceId,
        CancellationToken ct = default);

    IQueryable<Guid> VisibleInstanceIds(Guid effectiveUserId);
}
