using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public partial class SpaceMasterService
{
    private async Task DeactivateAndDeleteTopologyAsync(Guid id, bool rack, string? user)
    {
        var publisher = _topologyPublisher ?? throw new OrderPriceCorrectionException("SPACE_TOPOLOGY_AUTHORITY_UNAVAILABLE", 503);
        var locks = rack ? DeleteRackLocks : DeleteAisleLocks;
        var selected = await ExecuteTopologyMutationAsync<Guid[]?>("space-floor:edit", locks, async token =>
        {
            var children = await PublishedChildren(id, rack).Select(x => x.Id).OrderBy(x => x).ToArrayAsync(token);
            if (children.Length != 0) return children;
            var exists = rack
                ? await _db.Space_Racks.AnyAsync(x => x.Id == id, token)
                : await _db.Space_Aisles.AnyAsync(x => x.Id == id, token);
            return exists ? children : null;
        });
        if (selected is null) return;

        // These are separate accepted decisions. A later refusal does not undo a successful stop.
        foreach (var location in selected)
        {
            await publisher.DeactivateForTopologyAsync(
                token => PublishedChildren(id, rack).SingleOrDefaultAsync(x => x.Id == location, token), user);
        }

        // A newly published or reattached child is checked by the existing default guard.
        await ExecuteTopologyMutationAsync("space-floor:edit", locks, token => rack
            ? DeleteRackCoreAsync(id, null, null, user, withinParent: true, token)
            : DeleteAisleCoreAsync(id, null, null, user, withinParent: true, token));
    }

    private IQueryable<Space_Location> PublishedChildren(Guid id, bool rack)
    {
        var sourceRacks = rack
            ? _db.Space_Racks.Where(r => r.Id == id)
            : _db.Space_Racks.Where(r => r.AisleId == id && _db.Space_Aisles.Any(a => a.Id == id));
        return _db.Space_Locations.Where(x => x.Status == 1 && sourceRacks.Any(r => r.Id == x.RackId));
    }
}
