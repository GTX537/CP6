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
