using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;

namespace CP6.Core.Services.Integration.StockOutbox;

internal static class StockOutboxProtocolV1
{
    internal const string SourceOwner = "Main.Wms.StockMovement", SourceContract = "Main.Wms.StockOperation.v1";
    internal const string CreationSourceContract = "Main.Wms.StockOperation.v2";
    internal const string Owner = "Main.Wms.InventoryNotification", Domain = "Main.Stock.Notification.v1", Route = "InventoryInvalidatedV1";
    internal const string Publisher = "Main.SignalR.InventoryInvalidated.v1", PolicyVersion = "Main.StockOutbox.Technical.v1";
    internal const string Profile = "Main.StockOutbox.Background.v1", Purpose = "inventory-invalidation-delivery";
    internal const string Scope = "main.stock-outbox.dispatch", Topic = "main.inventory.invalidated.v1";
    internal static OutboxRootKeyV2 Root(Guid tenant) => new(Owner, tenant, Domain, Route);
    // A fixed technical namespace plus a deterministic root key. The route's native UNIQUE lock key
    // rejects collisions rather than silently rebinding another root.
    internal static long RouteLock(OutboxRootKeyV2 root)
        => ((long)0x4350364F << 32) | BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(root.Id)));
    internal static string Message(Guid tenant, Guid operation) => $"stock-hint:{tenant:N}:{operation:N}";
    internal static Cp6OutboxEnvelope Envelope(Guid tenant, Guid operation) => new(Message(tenant, operation), tenant,
        Topic, tenant.ToString("D"), "{}"u8.ToArray(), operation.ToString("D"), operation.ToString("D"), operation.ToString("D"), 1);
    internal static OutboxPolicyV2 Policy => new(PolicyVersion, 32, 10, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1),
        TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
    internal static bool Valid(Cp6OutboxEnvelope e) => e.TenantId != Guid.Empty && e.TopicName == Topic
        && Guid.TryParseExact(e.AggregateId, "D", out var operation) && operation != Guid.Empty
        && e.MessageId == Message(e.TenantId, operation) && e.PartitionKey == e.TenantId.ToString("D")
        && e.AggregateVersion == 1 && e.CorrelationId == e.AggregateId && e.CausationId == e.AggregateId
        && e.Payload.Span.SequenceEqual("{}"u8);
}

internal sealed class StockOutboxEnvelopeValidatorV1 : ICp6OutboxEnvelopeValidator
{
    public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope)
        => StockOutboxProtocolV1.Valid(envelope) ? Cp6MessageValidationResult.Valid : new(false, "STOCK_NOTIFICATION_CONTRACT_INVALID");
}
