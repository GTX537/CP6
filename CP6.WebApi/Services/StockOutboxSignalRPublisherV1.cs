using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

internal sealed class StockOutboxSignalRPublisherV1(CP6Context db, IHubContext<WmsHub> hub) : IOutboxPublisherV2
{
    public async ValueTask<OutboxTransportResultV2> PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.CurrentTenantId != message.TenantId
            || !StockOutboxProtocolV1.Valid(new(message.MessageId, message.TenantId, message.TopicName, message.PartitionKey,
                message.Payload, message.CorrelationId, message.CausationId, message.AggregateId, message.AggregateVersion)))
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_PUBLISHER_SCOPE_INVALID");
        await hub.Clients.Group(WmsHub.InventoryGroup(message.TenantId))
            .SendCoreAsync(StockOutboxProtocolV1.Route, [], ct);
        // Server transport accepted the empty refresh hint. This is not a client acknowledgement,
        // user-read receipt, financial posting, broker confirmation or downstream OwnerApplied.
        return new(OutboxTransportDecisionV2.Confirmed);
    }
}
