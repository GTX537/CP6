using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.PlateMoldSourceStoreV1;

namespace CP6.Core.Services.Erp;

public sealed class PlateMoldSourceOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<PlateMoldSourceScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.PlateMold, "query", false, (a, _) => Task.FromResult(new PlateMoldSourceScopeV1(db.CurrentTenantId, a.ActorId)), ct,
        readOnlyAuthorityFence: true);
    public Task<PlateMoldSourceVersionV1[]> ListAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (search?.Length > 100 || page is < 1 or > 100000 || pageSize is not (20 or 50 or 100)) throw Error("TOOL_SOURCE_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.PlateMold, "query", false, async (a, token) =>
        {
            var store = new PlateMoldSourceStoreV1(db); await store.Fence(token);
            var ids = await a.PlateMolds().Where(p => search == null || p.BaseCd.Contains(search) || p.WdPtnNo.Contains(search) || p.VrsnName.Contains(search))
                .OrderBy(p => p.BaseCd).ThenBy(p => p.WdPtnNo).ThenBy(p => p.WdRev).ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).Select(p => p.Id).ToArrayAsync(token);
            var result = new List<PlateMoldSourceVersionV1>();
            foreach (var id in ids) result.Add(await store.Read(a, id, null, token));
            return result.ToArray();
        }, ct);
    }
    public Task<PlateMoldSourceVersionV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("TOOL_SOURCE_ID_INVALID", 400);
        var exact = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.PlateMold, "query", false, async (a, token) =>
        {
            var store = new PlateMoldSourceStoreV1(db); await store.Fence(token);
            return await store.Read(a, id, exact, token);
        }, ct);
    }
}
