namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    // An observation has its own callback tree. A failed source observation must not leave
    // incomplete callbacks on the successful UNKNOWN CheckRun; successful reads still join
    // the root's final authorization and object checks, on this exact connection/transaction.
    internal async Task<T> EngineeringObservation<T>(Func<OperationsAccessV1, CancellationToken, Task<T>> read,
        CancellationToken ct)
    {
        var observation = new OperationsAccessV1(db, owner, authority, [authority], storedSubjectSource);
        var result = await read(observation, ct);
        Authorities.AddRange(observation.Authorities);
        AtCommit(observation.ValidateFinalAsync);
        return result;
    }

    // Used inside an existing final callback: validate the separate callback tree immediately,
    // rather than modifying the enumerated root callback list. Owner checks these authorities afterwards.
    internal async Task<T> EngineeringFinalObservation<T>(Func<OperationsAccessV1, CancellationToken, Task<T>> read,
        CancellationToken ct)
    {
        var observation = new OperationsAccessV1(db, owner, authority, [authority], storedSubjectSource);
        var result = await read(observation, ct);
        await observation.ValidateFinalAsync(ct);
        Authorities.AddRange(observation.Authorities);
        return result;
    }
    internal string EngineeringAuthorityWatermark() => CP6.Core.Services.Plm.EngineeringSourceCheckValuesV1.Digest(
        Authorities.Select(x => new { x.Resource, x.Source.SubjectId, x.Source.SourcePermissionVersion,
            x.Source.RegistryVersion, x.Scope, x.OrganizationVersion }).Distinct()
            .OrderBy(x => x.Resource, StringComparer.Ordinal).ToArray());
}
