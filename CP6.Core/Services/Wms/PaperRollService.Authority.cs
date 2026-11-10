using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class PaperRollService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public PaperRollService(CP6Context db, IWmsSequenceService sequence, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
    {
        _db = db; _seq = sequence; _administration = administration; _session = session;
        _requestCancellation = requestCancellation;
    }

    public Task<List<PaperRoll>> SearchAsync(PaperRollSearchQuery query)
        => ExecuteRoll("view", null, _ => SearchCoreAsync(query), read: true);
    public Task<PaperRoll?> GetAsync(string rollNo)
        => ExecuteRoll("view", null, _ => GetCoreAsync(rollNo), read: true);
    public Task<string> CreateAsync(PaperRollDto input, string? userName)
        => ExecuteRoll("add", userName, actor => CreateCoreAsync(input, actor), number: true);
    public Task ConsumeAsync(string rollNo, decimal lengthM, string? userName)
        => ExecuteRollCommand("consume", userName, actor => ConsumeCoreAsync(rollNo, lengthM, actor));
    public Task<List<PaperRoll>> MatchAsync(string grade, int width, string grain, decimal length)
        => ExecuteRoll("view", null, _ => MatchCoreAsync(grade, width, grain, length), read: true);
    public Task<List<string>> SlitAsync(SlitRequest input, string? userName)
        => ExecuteRoll("slit", userName, actor => SlitCoreAsync(input, actor), number: true);
    public Task DisposeAsync(string rollNo, string? userName)
        => ExecuteRollCommand("dispose", userName, actor => DisposeCoreAsync(rollNo, actor));

    private Task<bool> ExecuteRollCommand(string action, string? userName, Func<string?, Task> body)
        => ExecuteRoll(action, userName, async actor => { await body(actor); return true; });

    private async Task<T> ExecuteRoll<T>(string action, string? userName, Func<string?, Task<T>> body,
        bool read = false, bool number = false)
    {
        if (!_db.Database.IsNpgsql()) return await body(userName);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("ROLL_AUTHORITY_UNAVAILABLE", 503);
        if (number && (_seq is not WmsSequenceService sequence || !sequence.Owns(_db)))
            throw new OrderPriceCorrectionException("ROLL_SEQUENCE_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-paper-roll:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_PaperRoll\" IN "
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
                ? "ROLL_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("ROLL_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
