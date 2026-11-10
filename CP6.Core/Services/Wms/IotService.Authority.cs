using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class IotService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public IotService(CP6Context db, IWmsSequenceService sequence, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
    {
        _db = db; _seq = sequence; _administration = administration; _session = session;
        _requestCancellation = requestCancellation;
    }

    public Task<List<IotSensor>> SearchSensorsAsync(string? warehouseCd, string? sensorType)
        => ExecuteIot("view", null, _ => SearchSensorsCoreAsync(warehouseCd, sensorType), read: true);
    public Task<IotSensor?> GetSensorAsync(string sensorId)
        => ExecuteIot("view", null, _ => GetSensorCoreAsync(sensorId), read: true);
    public Task<string> CreateSensorAsync(IotSensorDto input, string? userName)
        => ExecuteIot("add", userName, actor => CreateSensorCoreAsync(input, actor), number: true);
    public Task UpdateSensorAsync(string sensorId, IotSensorDto input, string? userName)
        => ExecuteIotCommand("edit", userName, actor => UpdateSensorCoreAsync(sensorId, input, actor));
    public Task DeleteSensorAsync(string sensorId, string? userName)
        => ExecuteIotCommand("del", userName, actor => DeleteSensorCoreAsync(sensorId, actor));
    public Task<List<IotSensorReading>> GetReadingsAsync(string sensorId, DateTime? from, DateTime? to, int limit = 200)
        => ExecuteIot("view", null, _ => GetReadingsCoreAsync(sensorId, from, to, limit), read: true);
    public Task PostReadingAsync(string sensorId, decimal value, DateTime? readAt, string? userName)
        => ExecuteIotCommand("ingest", userName, actor => PostReadingCoreAsync(sensorId, value, readAt, actor));
    public Task<int> SimulateAsync(int countPerSensor = 1, string? userName = null)
        => ExecuteIot("simulate", userName, actor => SimulateCoreAsync(countPerSensor, actor));
    public Task<List<IotAlertDto>> CurrentAlertsAsync()
        => ExecuteIot("view", null, _ => CurrentAlertsCoreAsync(), read: true);

    private Task<bool> ExecuteIotCommand(string action, string? userName, Func<string?, Task> body)
        => ExecuteIot(action, userName, async actor => { await body(actor); return true; });

    private async Task<T> ExecuteIot<T>(string action, string? userName, Func<string?, Task<T>> body,
        bool read = false, bool number = false)
    {
        if (!_db.Database.IsNpgsql()) return await body(userName);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("IOT_AUTHORITY_UNAVAILABLE", 503);
        // The public creation boundary must bind allocation to the same parent Context.
        if (number && (_seq is not WmsSequenceService sequence || !sequence.Owns(_db)))
            throw new OrderPriceCorrectionException("IOT_SEQUENCE_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-iot:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_IotSensor\", public.\"T_IotSensorReading\" IN "
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
                ? "IOT_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("IOT_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
