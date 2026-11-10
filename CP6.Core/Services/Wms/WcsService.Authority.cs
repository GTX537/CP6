using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class WcsService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public WcsService(CP6Context db, IWmsSequenceService sequence, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
    {
        _db = db; _seq = sequence; _administration = administration; _session = session;
        _requestCancellation = requestCancellation;
    }

    public Task<List<WcsTask>> SearchAsync(WcsTaskSearchQuery query)
        => ExecuteWcs("view", null, _ => SearchCoreAsync(query), read: true);

    public Task<WcsTask?> GetAsync(string taskNo)
        => ExecuteWcs("view", null, _ => GetCoreAsync(taskNo), read: true);

    public Task<string> CreateAsync(WcsTaskDto input, string? userName)
        => ExecuteWcs("add", userName, actor => CreateCoreAsync(input, actor), number: true);

    public Task DispatchAsync(string taskNo, string deviceCd, string? userName)
        => ExecuteWcsCommand("dispatch", userName, actor => DispatchCoreAsync(taskNo, deviceCd, actor));

    public Task StartAsync(string taskNo, string? userName)
        => ExecuteWcsCommand("start", userName, actor => StartCoreAsync(taskNo, actor));

    public Task CompleteAsync(string taskNo, string? userName)
        => ExecuteWcsCommand("complete", userName, actor => CompleteCoreAsync(taskNo, actor));

    public Task FailAsync(string taskNo, string errorMessage, string? userName)
        => ExecuteWcsCommand("fail", userName, actor => FailCoreAsync(taskNo, errorMessage, actor));

    public Task DeleteAsync(string taskNo, string? userName)
        => ExecuteWcsCommand("del", userName, actor => DeleteCoreAsync(taskNo, actor));

    private Task<bool> ExecuteWcsCommand(string action, string? userName, Func<string?, Task> body)
        => ExecuteWcs(action, userName, async actor => { await body(actor); return true; });

    private async Task<T> ExecuteWcs<T>(string action, string? userName, Func<string?, Task<T>> body,
        bool read = false, bool number = false)
    {
        if (!_db.Database.IsNpgsql()) return await body(userName);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("WCS_AUTHORITY_UNAVAILABLE", 503);
        // The public creation boundary must bind allocation to the same parent Context.
        if (number && (_seq is not WmsSequenceService sequence || !sequence.Owns(_db)))
            throw new OrderPriceCorrectionException("WCS_SEQUENCE_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-wcs-task:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_WcsTask\" IN "
                        + (read ? "SHARE" : "SHARE ROW EXCLUSIVE") + " MODE", token);
                    if (number)
                        await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_WmsSequence\" IN SHARE ROW EXCLUSIVE MODE", token);
                    var result = await body(actor.UserName);
                    if (await _session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, _requestCancellation);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "WCS_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("WCS_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
