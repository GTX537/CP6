using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public partial class SpaceMasterService
{
    private const string SiteWrite = "LOCK TABLE public.\"Space_Site\" IN SHARE ROW EXCLUSIVE MODE;";
    private const string SiteRead = "LOCK TABLE public.\"Space_Site\" IN SHARE MODE;";
    private const string FloorWrite = "LOCK TABLE public.\"Space_Floor\" IN SHARE ROW EXCLUSIVE MODE;";
    private const string FloorRead = "LOCK TABLE public.\"Space_Floor\" IN SHARE MODE;";
    private const string LocationRead = "LOCK TABLE public.\"Space_Location\" IN SHARE MODE;";
    private const string FloorChildrenRead = "LOCK TABLE public.\"Space_Zone\", public.\"Space_Marker\" IN SHARE MODE;";

    private const string ZoneRead = "LOCK TABLE public.\"Space_Zone\" IN SHARE MODE;";
    private const string ZoneWrite = "LOCK TABLE public.\"Space_Zone\" IN SHARE ROW EXCLUSIVE MODE;";
    private const string AisleRead = "LOCK TABLE public.\"Space_Aisle\" IN SHARE MODE;";
    private const string AisleWrite = "LOCK TABLE public.\"Space_Aisle\" IN SHARE ROW EXCLUSIVE MODE;";
    private const string RackRead = "LOCK TABLE public.\"Space_Rack\" IN SHARE MODE;";
    private const string RackWrite = "LOCK TABLE public.\"Space_Rack\" IN SHARE ROW EXCLUSIVE MODE;";
    private const string LocationWrite = "LOCK TABLE public.\"Space_Location\" IN SHARE ROW EXCLUSIVE MODE;";

    private const string BinWrite = "LOCK TABLE public.\"T_WmsBin\" IN SHARE ROW EXCLUSIVE MODE;";
    private const string DeleteAisleLocks = SiteRead + FloorRead + ZoneRead + AisleWrite + RackWrite + LocationWrite + BinWrite;
    private const string DeleteRackLocks = SiteRead + FloorRead + ZoneRead + AisleRead + RackWrite + LocationWrite + BinWrite;

    private readonly IamAdministrationV1? _topologyAdministration;
    private readonly TenantManagementSessionV1? _topologySession;
    private readonly CancellationToken _topologyRequestCancellation;

    public SpaceMasterService(CP6Context db, LocationGeometryService geo, ILocationPublishService publish,
        IamAdministrationV1? administration, TenantManagementSessionV1? session, CancellationToken requestCancellation)
        : this(db, geo, publish)
    {
        _topologyAdministration = administration;
        _topologySession = session;
        _topologyRequestCancellation = requestCancellation;
    }

    public Task<Guid> CreateSiteAsync(SiteDto d, string? user)
        => ExecuteTopologyMutationAsync<Guid>("space-site:add", SiteWrite, token => CreateSiteCoreAsync(d, user, token));

    public Task UpdateSiteAsync(Guid id, SiteDto d, string? user)
        => ExecuteTopologyMutationAsync("space-site:edit", SiteWrite + FloorRead + LocationRead, token => UpdateSiteCoreAsync(id, d, user, token));

    public Task DeleteSiteAsync(Guid id)
        => ExecuteTopologyMutationAsync("space-site:delete", SiteWrite + FloorRead, token => DeleteSiteCoreAsync(id, token));

    public Task<Guid> CreateFloorAsync(FloorDto d, string? user)
        => ExecuteTopologyMutationAsync<Guid>("space-floor:add", SiteRead + FloorWrite, token => CreateFloorCoreAsync(d, user, token));

    public Task UpdateFloorAsync(Guid id, FloorDto d, string? user)
        => ExecuteTopologyMutationAsync("space-floor:edit", FloorWrite, token => UpdateFloorCoreAsync(id, d, user, token));

    public Task DeleteFloorAsync(Guid id)
        => ExecuteTopologyMutationAsync("space-floor:delete", FloorWrite + FloorChildrenRead, token => DeleteFloorCoreAsync(id, token));

    public Task<Guid> CreateZoneAsync(ZoneDto d, string? user)
        => ExecuteTopologyMutationAsync<Guid>("space-floor:edit", FloorRead + ZoneWrite, token => CreateZoneCoreAsync(d, user, token));

    public Task UpdateZoneAsync(Guid id, ZoneDto d, string? user)
        => ExecuteTopologyMutationAsync("space-floor:edit", ZoneWrite, token => UpdateZoneCoreAsync(id, d, user, token));

    public Task DeleteZoneAsync(Guid id)
        => ExecuteTopologyMutationAsync("space-floor:edit", ZoneWrite + AisleRead + RackRead, token => DeleteZoneCoreAsync(id, token));

    public Task<Guid> CreateAisleAsync(AisleDto d, string? user)
        => ExecuteTopologyMutationAsync<Guid>("space-floor:edit", ZoneRead + AisleWrite, token => CreateAisleCoreAsync(d, user, token));

    public Task UpdateAisleAsync(Guid id, AisleDto d, string? user)
        => ExecuteTopologyMutationAsync("space-floor:edit", AisleWrite, token => UpdateAisleCoreAsync(id, d, user, token));

    public Task<Guid> CreateRackAsync(RackDto d, string? user)
        => ExecuteTopologyMutationAsync<Guid>("space-floor:edit", ZoneRead + AisleRead + RackWrite, token => CreateRackCoreAsync(d, user, token));

    public Task UpdateRackAsync(Guid id, RackDto d, string? user)
        => ExecuteTopologyMutationAsync("space-floor:edit", AisleRead + RackWrite + LocationWrite, token => UpdateRackCoreAsync(id, d, user, token));

    public Task DeleteAisleAsync(Guid id, string? mode = null, Guid? targetAisleId = null, string? user = null)
        => _db.Database.IsNpgsql() && mode != "deactivate"
            ? ExecuteTopologyMutationAsync("space-floor:edit", DeleteAisleLocks,
                token => DeleteAisleCoreAsync(id, mode, targetAisleId, user, withinParent: true, token))
            : DeleteAisleCoreAsync(id, mode, targetAisleId, user, withinParent: false, default);

    public Task DeleteRackAsync(Guid id, string? mode = null, Guid? targetRackId = null, string? user = null)
        => _db.Database.IsNpgsql() && mode != "deactivate"
            ? ExecuteTopologyMutationAsync("space-floor:edit", DeleteRackLocks,
                token => DeleteRackCoreAsync(id, mode, targetRackId, user, withinParent: true, token))
            : DeleteRackCoreAsync(id, mode, targetRackId, user, withinParent: false, default);

    private async Task RequireCurrentRackAisleAsync(Guid zoneId, Guid? aisleId, CancellationToken token)
    {
        if (aisleId is Guid id && !await _db.Space_Aisles.AnyAsync(a => a.Id == id && a.ZoneId == zoneId, token))
            throw new OrderPriceCorrectionException("SPACE_AISLE_SCOPE_INVALID", 400);
    }


    private Task ExecuteTopologyMutationAsync(string permission, string tableLocks, Func<CancellationToken, Task> body)
        => ExecuteTopologyMutationAsync<bool>(permission, tableLocks, async token =>
        {
            await body(token);
            return true;
        });

    private async Task<T> ExecuteTopologyMutationAsync<T>(string permission, string tableLocks, Func<CancellationToken, Task<T>> body)
    {
        if (!_db.Database.IsNpgsql()) return await body(CancellationToken.None);
        if (_topologyAdministration?.Owns(_db) != true || _topologySession == null)
            throw new OrderPriceCorrectionException("SPACE_TOPOLOGY_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await _topologyAdministration.ExecuteCurrentAsync(permission, _topologySession.CurrentActor,
                async (actor, token) =>
                {
                    await _db.Database.ExecuteSqlRawAsync(tableLocks, token);
                    var result = await body(token);
                    if (await _topologySession.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, _topologyRequestCancellation);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "SPACE_TOPOLOGY_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("SPACE_TOPOLOGY_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
