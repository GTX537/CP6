using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class CarrierService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public CarrierService(CP6Context db, IWmsSequenceService sequence, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
    {
        _db = db; _seq = sequence; _administration = administration; _session = session;
        _requestCancellation = requestCancellation;
    }

    public Task<List<CarrierShipment>> SearchAsync(CarrierSearchQuery query)
        => ExecuteCarrier("view", null, _ => SearchCoreAsync(query), read: true);
    public Task<CarrierShipment?> GetAsync(string shipmentNo)
        => ExecuteCarrier("view", null, _ => GetCoreAsync(shipmentNo), read: true);
    public Task<string> CreateShipmentAsync(CarrierShipmentDto input, string? userName)
        => ExecuteCarrier("add", userName, actor => CreateShipmentCoreAsync(input, actor), number: true);
    public Task AddEventAsync(string shipmentNo, CarrierEventDto input, string? userName)
        => ExecuteCarrierCommand("event", userName, actor => AddEventCoreAsync(shipmentNo, input, actor));
    public Task MarkPickedUpAsync(string shipmentNo, string? userName)
        => ExecuteCarrierCommand("event", userName, actor => MarkPickedUpCoreAsync(shipmentNo, actor));
    public Task MarkInTransitAsync(string shipmentNo, string? userName)
        => ExecuteCarrierCommand("event", userName, actor => MarkInTransitCoreAsync(shipmentNo, actor));
    public Task MarkDeliveredAsync(string shipmentNo, string? userName)
        => ExecuteCarrierCommand("event", userName, actor => MarkDeliveredCoreAsync(shipmentNo, actor));
    public Task MarkFailedAsync(string shipmentNo, string reason, string? userName)
        => ExecuteCarrierCommand("event", userName, actor => MarkFailedCoreAsync(shipmentNo, reason, actor));

    private Task<bool> ExecuteCarrierCommand(string action, string? userName, Func<string?, Task> body)
        => ExecuteCarrier(action, userName, async actor => { await body(actor); return true; });

    private async Task<T> ExecuteCarrier<T>(string action, string? userName, Func<string?, Task<T>> body,
        bool read = false, bool number = false)
    {
        if (!_db.Database.IsNpgsql()) return await body(userName);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("CARRIER_AUTHORITY_UNAVAILABLE", 503);
        if (number && (_seq is not WmsSequenceService sequence || !sequence.Owns(_db)))
            throw new OrderPriceCorrectionException("CARRIER_SEQUENCE_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-carrier:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    // Fence before loading the mutable event list, preserving the existing version check too.
                    await _db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_CarrierShipment\" IN "
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
                ? "CARRIER_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("CARRIER_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
