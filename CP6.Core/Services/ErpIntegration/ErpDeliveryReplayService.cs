using System.Data;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.ErpIntegration;

public sealed record ErpDeliveryDeadLetter(string Kind, string TargetId, string? MessageId, int AttemptCount,
    string PayloadSha256, string? ErrorCode, DateTimeOffset AvailableAtUtc, byte[] RowVersion);

public sealed record ErpDeliveryReplayScheduled(Guid OperationId, string Kind, string TargetId,
    string? MessageId, DateTimeOffset ReplayedAtUtc);

/// <summary>Requeues original durable deliveries after an authorized operator verifies their immutable identity.</summary>
public sealed class ErpDeliveryReplayService(IDbContextFactory<ErpIntegrationContext> factory, ErpIntegrationRuntime runtime)
{
    public async Task<IReadOnlyList<ErpDeliveryDeadLetter>> ListAsync(Guid tenant, CancellationToken ct = default)
    {
        RequireConfiguredTenant(tenant);
        await using var queue = await factory.CreateDbContextAsync(ct);
        RequireSql(queue);
        await using var business = BusinessContext(queue, tenant);
        await RequireActiveTenantAsync(business, tenant, ct);
        var results = await queue.Set<Cp6OutboxMessage>().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Status == Cp6OutboxStatus.DeadLettered)
            .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.Id).Take(100)
            .Select(x => new { x.Id, x.MessageId, x.AttemptCount, x.PayloadSha256, x.LastErrorCode, x.AvailableAtUtc, x.RowVersion })
            .ToArrayAsync(ct);
        var bridges = await queue.OrderBridges.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CompletedAtUtc == null && x.AttemptCount >= 10)
            .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.OrderKey).Take(100)
            .Select(x => new { x.OrderKey, x.AttemptCount, x.LastErrorCode, x.AvailableAtUtc, x.RowVersion }).ToArrayAsync(ct);
        return results.Select(x => new ErpDeliveryDeadLetter("result", x.Id.ToString("D"), x.MessageId,
                x.AttemptCount, x.PayloadSha256, x.LastErrorCode, x.AvailableAtUtc, x.RowVersion))
            .Concat(bridges.Select(x => new ErpDeliveryDeadLetter("bridge", x.OrderKey, null, x.AttemptCount,
                BridgeHash(tenant, x.OrderKey), x.LastErrorCode, x.AvailableAtUtc, x.RowVersion)))
            .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.TargetId, StringComparer.Ordinal).Take(100).ToArray();
    }

    public Task<ErpDeliveryReplayScheduled> ScheduleResultAsync(Guid tenant, Guid outboxId, ErpReplayRequest input,
        string actor, CancellationToken ct = default)
    {
        if (outboxId == Guid.Empty) throw Error("INPUT_INVALID");
        return ScheduleAsync(tenant, "result", outboxId.ToString("D"), input, actor, ct);
    }

    public Task<ErpDeliveryReplayScheduled> ScheduleBridgeAsync(Guid tenant, string orderKey, ErpReplayRequest input,
        string actor, CancellationToken ct = default)
    {
        if (orderKey is not { Length: > 0 and <= 20 } || orderKey.Any(char.IsControl)) throw Error("INPUT_INVALID");
        return ScheduleAsync(tenant, "bridge", orderKey, input, actor, ct);
    }

    private async Task<ErpDeliveryReplayScheduled> ScheduleAsync(Guid tenant, string kind, string target,
        ErpReplayRequest input, string actor, CancellationToken ct)
    {
        RequireConfiguredTenant(tenant);
        if (input is null || input.OperationId == Guid.Empty || input.RowVersion is not { Length: 8 } ||
            input.PayloadSha256 is not { Length: 64 } || !input.PayloadSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            input.ReasonCode is not ("dependency-recovered" or "contract-verified") ||
            actor is not { Length: > 0 and <= 100 } || actor.Any(char.IsControl)) throw Error("INPUT_INVALID");
        await using var queue = await factory.CreateDbContextAsync(ct);
        RequireSql(queue);
        await using var transaction = await queue.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var business = BusinessContext(queue, tenant);
        await business.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        await RequireActiveTenantAsync(business, tenant, ct);
        // Operator replay is infrequent. Serialize a tenant's audit inserts before reading missing
        // operation keys so two different operation ids cannot deadlock on Serializable index gaps.
        await ErpSqlLock.AcquireAsync(queue, $"c03:delivery-replay:{tenant:D}", ct);
        var previous = await queue.Set<ErpDeliveryReplayAudit>()
            .SingleOrDefaultAsync(x => x.TenantId == tenant && x.OperationId == input.OperationId, ct);
        if (previous is not null)
        {
            if (previous.Kind != kind || previous.TargetId != target || previous.PayloadSha256 != input.PayloadSha256 ||
                previous.ActorId != actor || previous.ReasonCode != input.ReasonCode ||
                !previous.InputRowVersion.SequenceEqual(input.RowVersion)) throw Error("OPERATION_CONFLICT");
            await transaction.CommitAsync(ct);
            return Scheduled(previous);
        }

        var now = runtime.Clock.GetUtcNow();
        var audit = new ErpDeliveryReplayAudit
        {
            TenantId = tenant, OperationId = input.OperationId, Kind = kind, TargetId = target,
            PayloadSha256 = input.PayloadSha256, InputRowVersion = input.RowVersion.ToArray(), ActorId = actor,
            ReasonCode = input.ReasonCode, ReplayedAtUtc = now
        };
        if (kind == "result") await RequeueResultAsync(queue, tenant, Guid.Parse(target), input, audit, now, ct);
        else await RequeueBridgeAsync(queue, business, tenant, target, input, audit, now, ct);
        queue.Set<ErpDeliveryReplayAudit>().Add(audit);
        await queue.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Scheduled(audit);
    }

    private async Task RequeueResultAsync(ErpIntegrationContext queue, Guid tenant, Guid id, ErpReplayRequest input,
        ErpDeliveryReplayAudit audit, DateTimeOffset now, CancellationToken ct)
    {
        await ErpSqlLock.AcquireAsync(queue, $"c03:delivery-result:{tenant:D}:{id:D}", ct);
        var message = await queue.Set<Cp6OutboxMessage>().FromSqlInterpolated(
                $"SELECT * FROM erp_integration.Cp6_OutboxMessage WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={tenant} AND Id={id}")
            .SingleOrDefaultAsync(ct) ?? throw Error("NOT_FOUND");
        RequirePrecondition(message.RowVersion, message.PayloadSha256, input);
        if (message.Status != Cp6OutboxStatus.DeadLettered) throw Error("NOT_DEADLETTERED");
        var envelope = new Cp6OutboxEnvelope(message.MessageId, message.TenantId, message.TopicName, message.PartitionKey,
            message.Payload, message.CorrelationId, message.CausationId, message.AggregateId, message.AggregateVersion);
        if (message.TopicName != ErpEventContracts.ResultTopic || ErpEventContracts.Hash(message.Payload) != message.PayloadSha256 ||
            !runtime.Validator.Validate(envelope).IsValid) throw Error("EVIDENCE_INVALID");
        using var document = JsonDocument.Parse(message.Payload);
        if (document.RootElement.GetProperty("region").GetString() != runtime.Options.Tenants[tenant]) throw Error("EVIDENCE_INVALID");
        var deadLetters = await queue.Set<Cp6DeadLetterRecord>()
            .Where(x => x.Direction == Cp6DeadLetterDirection.Outbound && x.MessageId == message.MessageId && x.ReplayedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc).Take(2).ToArrayAsync(ct);
        if (deadLetters.Length != 1 || deadLetters[0].TenantId != tenant || deadLetters[0].PayloadSha256 != message.PayloadSha256)
            throw Error("EVIDENCE_INVALID");
        deadLetters[0].RecordReplay(input.ReasonCode, now);
        audit.MessageId = message.MessageId;
        audit.PreviousAttemptCount = message.AttemptCount;

        // Platform 0.10.2 entity.Requeue is internal and its public store opens another transaction.
        // Mirror only its scheduling transition here so the operator audit shares this SQL commit.
        var entry = queue.Entry(message);
        entry.Property(x => x.Status).CurrentValue = Cp6OutboxStatus.Pending;
        entry.Property(x => x.AttemptCount).CurrentValue = 0;
        entry.Property(x => x.AvailableAtUtc).CurrentValue = now;
        entry.Property(x => x.DeadLetteredAtUtc).CurrentValue = null;
        entry.Property(x => x.LastErrorCode).CurrentValue = null;
        entry.Property(x => x.SupportReference).CurrentValue = null;
        entry.Property(x => x.LeaseOwner).CurrentValue = null;
        entry.Property(x => x.LeaseToken).CurrentValue = null;
        entry.Property(x => x.LeaseExpiresAtUtc).CurrentValue = null;
    }

    private static async Task RequeueBridgeAsync(ErpIntegrationContext queue, CP6Context business, Guid tenant, string key,
        ErpReplayRequest input, ErpDeliveryReplayAudit audit, DateTimeOffset now, CancellationToken ct)
    {
        // This is the same transaction-owned lock used by ErpOrderBridgeWorker around its real hooks.
        await ErpSqlLock.AcquireAsync(queue, $"c03:bridge:{tenant:D}:{key}", ct);
        var bridge = await queue.OrderBridges.FromSqlInterpolated(
                $"SELECT * FROM erp_integration.OrderBridgeDispatch WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={tenant} AND OrderKey={key}")
            .SingleOrDefaultAsync(ct) ?? throw Error("NOT_FOUND");
        RequirePrecondition(bridge.RowVersion, BridgeHash(tenant, key), input);
        if (bridge.CompletedAtUtc is not null || bridge.AttemptCount < 10 ||
            bridge.LastErrorCode != "C03_BRIDGE_REPLAY_REQUIRED" || bridge.LeaseExpiresAtUtc > now ||
            (bridge.LeaseOwner is not null && bridge.LeaseExpiresAtUtc is null)) throw Error("NOT_DEADLETTERED");
        var orderKey = await business.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.WebOrderNo == key && !x.IsDeleted)
            .Select(x => x.WebOrderNo).SingleOrDefaultAsync(ct);
        if (orderKey != key) throw Error("EVIDENCE_INVALID");
        audit.PreviousAttemptCount = bridge.AttemptCount;
        bridge.AttemptCount = 0;
        bridge.AvailableAtUtc = now;
        bridge.LastErrorCode = null;
        bridge.LeaseOwner = null;
        bridge.LeaseExpiresAtUtc = null;
    }

    private void RequireConfiguredTenant(Guid tenant)
    {
        if (tenant == Guid.Empty || !runtime.Options.Enabled || !runtime.Options.Tenants.ContainsKey(tenant))
            throw Error("TENANT_DISABLED");
    }

    private async Task RequireActiveTenantAsync(CP6Context business, Guid tenant, CancellationToken ct)
    {
        var now = runtime.Clock.GetUtcNow().UtcDateTime;
        if (!await business.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Id == tenant && x.Enable &&
                (x.ExpireDate == null || x.ExpireDate > now), ct)) throw Error("TENANT_DISABLED");
    }

    private static CP6Context BusinessContext(ErpIntegrationContext queue, Guid tenant) =>
        new(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(queue.Database.GetDbConnection()).Options,
            new TenantContext { CurrentTenantId = tenant });

    private static void RequireSql(ErpIntegrationContext queue)
    {
        if (!queue.Database.IsSqlServer()) throw new InvalidOperationException("C03_REQUIRES_REAL_SQL_SERVER");
    }

    private static void RequirePrecondition(byte[] version, string hash, ErpReplayRequest input)
    {
        if (!version.SequenceEqual(input.RowVersion) || hash != input.PayloadSha256) throw Error("PRECONDITION_FAILED");
    }

    private static string BridgeHash(Guid tenant, string key) =>
        ErpEventContracts.Hash(Encoding.UTF8.GetBytes($"c03:bridge:{tenant:D}:{key}"));

    private static ErpDeliveryReplayScheduled Scheduled(ErpDeliveryReplayAudit audit) =>
        new(audit.OperationId, audit.Kind, audit.TargetId, audit.MessageId, audit.ReplayedAtUtc);

    private static ErpCommerceException Error(string suffix) => new("C03_DELIVERY_REPLAY_" + suffix);
}
