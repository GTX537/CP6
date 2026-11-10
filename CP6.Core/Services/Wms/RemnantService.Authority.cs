using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class RemnantService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public RemnantService(CP6Context db, IWmsSequenceService sequence, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
    {
        _db = db; _seq = sequence; _administration = administration; _session = session;
        _requestCancellation = requestCancellation;
    }

    public Task<List<RemnantMaterial>> SearchAsync(RemnantSearchQuery q)
        => ExecuteRemnant("view", null, _ => SearchCoreAsync(q), read: true);
    public Task<RemnantMaterial?> GetAsync(string remnantNo)
        => ExecuteRemnant("view", null, _ => GetCoreAsync(remnantNo), read: true);
    public Task<string> CreateAsync(RemnantDto dto, string? userName)
        => ExecuteRemnant("add", userName, actor => CreateCoreAsync(dto, actor), number: true);
    public Task UpdateAsync(string remnantNo, RemnantDto dto, string? userName)
        => ExecuteRemnantCommand("edit", userName, actor => UpdateCoreAsync(remnantNo, dto, actor));
    public Task<List<RemnantMaterial>> MatchAsync(string materialType, int minWidthMm, int minLengthMm)
        => ExecuteRemnant("view", null, _ => MatchCoreAsync(materialType, minWidthMm, minLengthMm), read: true);
    public Task ReserveAsync(string remnantNo, string reservedFor, string? userName)
        => ExecuteRemnantCommand("reserve", userName, actor => ReserveCoreAsync(remnantNo, reservedFor, actor));
    public Task UnreserveAsync(string remnantNo, string? userName)
        => ExecuteRemnantCommand("reserve", userName, actor => UnreserveCoreAsync(remnantNo, actor));
    public Task MarkUsedAsync(string remnantNo, string? userName)
        => ExecuteRemnantCommand("use", userName, actor => MarkUsedCoreAsync(remnantNo, actor));
    public Task DisposeAsync(string remnantNo, string? userName)
        => ExecuteRemnantCommand("dispose", userName, actor => DisposeCoreAsync(remnantNo, actor));
    public Task DeleteAsync(string remnantNo, string? userName)
        => ExecuteRemnantCommand("del", userName, actor => DeleteCoreAsync(remnantNo, actor));

    private Task<bool> ExecuteRemnantCommand(string action, string? userName, Func<string?, Task> body)
        => ExecuteRemnant(action, userName, async actor => { await body(actor); return true; });

    private async Task<T> ExecuteRemnant<T>(string action, string? userName, Func<string?, Task<T>> body,
        bool read = false, bool number = false)
    {
        if (!_db.Database.IsNpgsql()) return await body(userName);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("REMNANT_AUTHORITY_UNAVAILABLE", 503);
        if (number && (_seq is not WmsSequenceService sequence || !sequence.Owns(_db)))
            throw new OrderPriceCorrectionException("REMNANT_SEQUENCE_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-remnant:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_RemnantMaterial\" IN "
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
                ? "REMNANT_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("REMNANT_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
