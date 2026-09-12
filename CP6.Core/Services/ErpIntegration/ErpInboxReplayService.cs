using System.Data;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.ErpIntegration;

public sealed record ErpReplayRequest(Guid OperationId, byte[] RowVersion, string PayloadSha256, string ReasonCode);
public sealed record ErpReplayScheduled(Guid OperationId, string MessageId, DateTimeOffset ReplayedAtUtc);

/// <summary>Schedules the original command for the durable worker; the operator cannot replace its payload or version.</summary>
public sealed class ErpInboxReplayService(IDbContextFactory<ErpIntegrationContext> factory, ErpIntegrationRuntime runtime)
{
    public async Task<ErpReplayScheduled> ScheduleAsync(Guid tenant, string messageId, ErpReplayRequest input,
        string actor, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || !runtime.Options.Tenants.ContainsKey(tenant) || input is null || input.OperationId == Guid.Empty ||
            messageId is not { Length: > 0 and <= 128 } || messageId.Any(char.IsControl) ||
            input.RowVersion is not { Length: 8 } || input.PayloadSha256 is not { Length: 64 } ||
            !input.PayloadSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            input.ReasonCode is not ("dependency-recovered" or "contract-verified") ||
            actor is not { Length: > 0 and <= 100 } || actor.Any(char.IsControl))
            throw Error("C03_REPLAY_INPUT_INVALID");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!db.Database.IsSqlServer()) throw new InvalidOperationException("C03_REQUIRES_REAL_SQL_SERVER");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var business = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(db.Database.GetDbConnection()).Options, new TenantContext { CurrentTenantId = tenant });
        await business.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        var tenantCheckedAt = runtime.Clock.GetUtcNow().UtcDateTime;
        if (!await business.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Id == tenant && x.Enable &&
                (x.ExpireDate == null || x.ExpireDate > tenantCheckedAt), ct))
            throw Error("C03_REPLAY_TENANT_DISABLED");
        await ErpSqlLock.AcquireAsync(db, $"c03:replay:{tenant:D}:{input.OperationId:D}", ct);
        var previous = await db.ReplayAudits.SingleOrDefaultAsync(x => x.TenantId == tenant && x.OperationId == input.OperationId, ct);
        if (previous is not null)
        {
            if (previous.MessageId != messageId || previous.PayloadSha256 != input.PayloadSha256 || previous.ActorId != actor ||
                previous.ReasonCode != input.ReasonCode || !previous.InputRowVersion.SequenceEqual(input.RowVersion))
                throw Error("C03_REPLAY_OPERATION_CONFLICT");
            await tx.CommitAsync(ct);
            return new(previous.OperationId, previous.MessageId, previous.ReplayedAtUtc);
        }
        await ErpSqlLock.AcquireAsync(db, "c03:message:" + ErpEventContracts.Hash(Encoding.UTF8.GetBytes(messageId)), ct);
        var receipt = await db.Inbox.SingleOrDefaultAsync(x => x.TenantId == tenant && x.MessageId == messageId, ct)
            ?? throw Error("C03_REPLAY_NOT_FOUND");
        if (!receipt.RowVersion.SequenceEqual(input.RowVersion) || receipt.PayloadSha256 != input.PayloadSha256)
            throw Error("C03_REPLAY_PRECONDITION_FAILED");
        if (receipt.Status != ErpInboxStatus.DeadLettered) throw Error("C03_REPLAY_NOT_DEADLETTERED");
        if (ErpEventContracts.Hash(receipt.Payload) != receipt.PayloadSha256 || !runtime.Validator.IsValid(receipt.Payload))
            throw Error("C03_REPLAY_PAYLOAD_INVALID");
        using var document = JsonDocument.Parse(receipt.Payload);
        var root = document.RootElement;
        if (root.GetProperty("id").GetString() != messageId || root.GetProperty("tenantid").GetGuid() != tenant ||
            root.GetProperty("aggregateid").GetGuid() != receipt.AggregateId ||
            root.GetProperty("aggregateversion").GetInt32() != receipt.AggregateVersion ||
            root.GetProperty("type").GetString() != receipt.EventType ||
            root.GetProperty("region").GetString() != runtime.Options.Tenants[tenant] ||
            ErpEventContracts.TopicFor(receipt.EventType) != ErpEventContracts.RequestTopic)
            throw Error("C03_REPLAY_PAYLOAD_INVALID");
        var now = runtime.Clock.GetUtcNow();
        db.ReplayAudits.Add(new()
        {
            TenantId = tenant, OperationId = input.OperationId, MessageId = messageId,
            PayloadSha256 = receipt.PayloadSha256, InputRowVersion = input.RowVersion.ToArray(), ActorId = actor,
            ReasonCode = input.ReasonCode, PreviousAttemptCount = receipt.AttemptCount, ReplayedAtUtc = now
        });
        receipt.Status = ErpInboxStatus.Processing; receipt.AttemptCount = 0;
        receipt.ProcessedAtUtc = null; receipt.ErrorCode = null; receipt.RetryAtUtc = now;
        receipt.ReplayedAtUtc = now; receipt.ReplayReasonCode = input.ReasonCode;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(input.OperationId, messageId, now);
    }

    private static ErpCommerceException Error(string code) => new(code);
}
