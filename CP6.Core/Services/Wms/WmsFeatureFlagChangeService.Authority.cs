using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class WmsFeatureFlagChangeService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public WmsFeatureFlagChangeService(CP6Context db, IApprovalService approval, ITaskCenterService taskCenter,
        IamAdministrationV1? administration, TenantManagementSessionV1? session,
        CancellationToken requestCancellation = default) : this(db, approval, taskCenter)
    {
        _administration = administration; _session = session; _requestCancellation = requestCancellation;
    }

    public Task<WmsFeatureFlagChangeDto> SubmitAsync(CreateWmsFeatureFlagChangeRequest request,
        Guid requestedById, string? requestedBy, CancellationToken ct = default)
        => ExecuteFeatureRequest(false, (actor, token) => SubmitCoreAsync(request,
            actor == null ? requestedById : actor.UserId, actor == null ? requestedBy : actor.UserName, token), ct);

    public Task<IReadOnlyList<WmsFeatureFlagChangeDto>> GetAsync(WmsFeatureFlagChangeQuery query,
        CancellationToken ct = default)
        => ExecuteFeatureRequest(true, (_, token) => GetCoreAsync(query, token), ct);

    public Task<IReadOnlyList<WmsFeatureFlagDto>> GetFeaturesAsync(CancellationToken ct = default)
        => ExecuteFeatureRequest(true, (_, token) => GetFeaturesCoreAsync(token), ct, featureConfiguration: true);

    public async Task CancelAsync(Guid id, Guid requestedById, string? requestedBy, CancellationToken ct = default)
    {
        await ExecuteFeatureRequest(false, async (actor, token) =>
        {
            await CancelCoreAsync(id, actor == null ? requestedById : actor.UserId,
                actor == null ? requestedBy : actor.UserName, token);
            return true;
        }, ct);
    }

    private async Task<T> ExecuteFeatureRequest<T>(bool read,
        Func<UserPermissionContext?, CancellationToken, Task<T>> body, CancellationToken ct, bool featureConfiguration = false)
    {
        if (!_db.Database.IsNpgsql()) return await body(null, ct);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("WMS_FEATURE_AUTHORITY_UNAVAILABLE", 503);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _requestCancellation);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-mobile:device-manage", _session.CurrentActor,
                async (actor, token) =>
                {
                    await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\""
                        + (featureConfiguration ? "T_WmsFeatureFlag" : "T_WmsFeatureFlagChange") + "\" IN "
                        + (read ? "SHARE" : "SHARE ROW EXCLUSIVE") + " MODE", token);
                    if (!read)
                        await _db.Database.ExecuteSqlRawAsync("""
                            LOCK TABLE public."T_Warehouse", public."T_WmsFeatureFlag", public."Wf_ApprovalBinding",
                                public."Wf_FlowDef", public."Wf_FlowDefVersion" IN SHARE MODE
                            """, token);
                    var result = await body(actor, token);
                    if (await _session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, linked.Token);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "WMS_FEATURE_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (InvalidOperationException error) when (error.Message is "E-WF-031" or "E-WF-032" or "E-WF-033" or "E-WF-034")
        { throw new OrderPriceCorrectionException("WMS_FEATURE_WORKFLOW_UNAVAILABLE", 503, error); }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("WMS_FEATURE_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
