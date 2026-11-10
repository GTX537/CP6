using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class WmsRoleScopeService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public WmsRoleScopeService(CP6Context db, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
    {
        this.db = db; _administration = administration; _session = session;
        _requestCancellation = requestCancellation;
    }

    public Task<IReadOnlyList<WmsRoleScopeDto>> GetAsync(int roleId, CancellationToken ct = default)
        => ExecuteRoleScope("query", null, true, (_, token) => GetCoreAsync(roleId, token), ct);

    public Task<IReadOnlyList<WmsRoleScopeDto>> ReplaceAsync(int roleId, ReplaceWmsRoleScopesRequest request,
        string? userName, CancellationToken ct = default)
        => ExecuteRoleScope("edit", userName, false, (actor, token) => ReplaceCoreAsync(roleId, request, actor, token), ct);

    private async Task<T> ExecuteRoleScope<T>(string action, string? userName, bool read,
        Func<string?, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return await body(userName, ct);
        if (_administration?.Owns(db) != true || _session == null)
            throw new OrderPriceCorrectionException("WMS_SCOPE_AUTHORITY_UNAVAILABLE", 503);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _requestCancellation);
        try
        {
            return await _administration.ExecuteCurrentAsync("pub-data-scope:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_WmsRoleScope\" IN "
                        + (read ? "SHARE" : "SHARE ROW EXCLUSIVE") + " MODE", token);
                    await db.Database.ExecuteSqlRawAsync("""
                        LOCK TABLE public."Sys_Roles", public."T_Warehouse", public."T_Location" IN SHARE MODE
                        """, token);
                    var result = await body(actor.UserName, token);
                    if (await _session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, linked.Token);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "WMS_SCOPE_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("WMS_SCOPE_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
