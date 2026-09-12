using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>At-least-once ERP commands with a business-key journal and one SQL transaction for Inbox/business/Outbox.</summary>
public sealed class ErpRequestHandler(IDbContextFactory<ErpIntegrationContext> factory, ErpIntegrationRuntime runtime,
    Func<CP6Context, OrderService> createOrderService, TimeProvider? clock = null,
    Func<string, CancellationToken, Task>? observeStage = null)
{
    private readonly TimeProvider clock = clock ?? runtime.Clock;

    public async Task<Cp6InboxProcessingResult> ConsumeAsync(ReadOnlyMemory<byte> payload, string topic, string partitionKey,
        CancellationToken ct = default)
    {
        var digest = ErpEventContracts.Hash(payload.Span);
        if (topic != ErpEventContracts.RequestTopic || !runtime.Validator.IsValid(payload)) return Invalid(digest);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var tenant = root.GetProperty("tenantid").GetGuid();
        if (!runtime.Options.Tenants.TryGetValue(tenant, out var region) || root.GetProperty("region").GetString() != region ||
            root.GetProperty("type").GetString() is not (ErpEventContracts.BusinessPartnerRequested or ErpEventContracts.OrderRequested))
            return Invalid(digest);
        var delivery = new Cp6InboxDelivery("cp6-erp-commands-v1", root.GetProperty("id").GetString()!, tenant,
            topic, partitionKey, payload, root.GetProperty("aggregateid").GetString()!, root.GetProperty("aggregateversion").GetInt32());
        if (!runtime.Validator.Validate(delivery).IsValid) return Invalid(digest);
        var command = Command.Read(root, payload.ToArray(), digest);
        try { return await ProcessAsync(command, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // ProcessAsync's context/transaction have already been disposed and rolled back.
            // Retry evidence and a retryable result use a new transaction, never the failed change tracker.
            try { return await RecordFailureAsync(command, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { return new(Cp6InboxDisposition.RetryScheduled, "C03_ERP_UNAVAILABLE", digest); }
        }
    }

    private async Task<Cp6InboxProcessingResult> ProcessAsync(Command command, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        RequireSql(queue);
        await using var transaction = await queue.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockMessageAsync(queue, command, ct);
        var receipt = await queue.Inbox.SingleOrDefaultAsync(x => x.MessageId == command.MessageId, ct);
        var existingResult = InspectReceipt(receipt, command);
        if (existingResult is not null)
        {
            if (existingResult.Disposition == Cp6InboxDisposition.PayloadConflict) await queue.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return existingResult;
        }
        receipt ??= NewReceipt(command);
        if (queue.Entry(receipt).State == EntityState.Detached) queue.Inbox.Add(receipt);
        receipt.AttemptCount++;
        receipt.Status = ErpInboxStatus.Processing;
        receipt.RetryAtUtc = null;
        await queue.SaveChangesAsync(ct);
        var aggregate = await LoadAggregateAsync(queue, command, ct);
        var previous = await LoadRequestAsync(queue, command, ct);
        var handled = await HandlePriorAsync(queue, command, aggregate, previous, ct);
        if (!handled)
        {
            await using var db = CreateBusinessContext(queue, command.TenantId);
            await db.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
            try
            {
                var now = clock.GetUtcNow().UtcDateTime;
                if (!await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == command.TenantId && t.Enable &&
                        (t.ExpireDate == null || t.ExpireDate > now), ct))
                    throw new ErpCommerceException("C03_TENANT_DISABLED");
                if (command.BusinessPartner is { } bpRequest)
                {
                    var partner = await db.BusinessPartners.FromSqlInterpolated($"SELECT * FROM dbo.T_WebBusinessPartner WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={command.TenantId} AND CrmAccountId={command.AccountId}")
                        .SingleOrDefaultAsync(ct);
                    ErpQuotationOrderFactory.RequireBusinessPartner(partner, command.AccountId, allowPreRegistered: true);
                    var service = new BusinessPartnerService(db);
                    var dto = await service.GetByCdAsync(partner!.BpCd);
                    var errors = new List<string>(); service.Validate(dto!, true, dto, errors);
                    if (errors.Count != 0) throw new ErpCommerceException("C03_BUSINESS_PARTNER_MASTER_DATA_REQUIRED");
                    partner.Status = 1; partner.Modifier = "crm-integration"; partner.ModifyDate = now;
                    await db.SaveChangesAsync(ct);
                    var version = NextResultVersion(aggregate, command);
                    SaveOutcome(queue, command, previous, ErpEventContracts.BusinessPartnerSynchronized,
                        new ErpBusinessPartnerSynchronized(command.TenantId, command.RequestId, command.AccountId,
                            command.RequestVersion, version, partner.BpCd), version, true, true);
                }
                else
                {
                    var result = await new ErpQuotationOrderFactory(db, createOrderService(db), clock)
                        .CreateAsync(command.Order!, command.InputSha256, ct);
                    if (observeStage is not null) await observeStage("order-staged", ct);
                    queue.OrderBridges.Add(new() { TenantId = command.TenantId, OrderKey = result.OrderKey, AvailableAtUtc = clock.GetUtcNow() });
                    var version = NextResultVersion(aggregate, command);
                    var order = command.Order!;
                    SaveOutcome(queue, command, previous, ErpEventContracts.OrderCreated,
                        new ErpOrderCreated(order.TenantId, order.RequestId, order.AccountId, order.RequestVersion,
                            order.OpportunityId, version, result.OrderKey, order.QuotationKey, order.BusinessPartnerKey,
                            result.Amount, order.Currency), version, true, true);
                }
            }
            catch (ErpCommerceException ex)
            {
                // Validation errors are raised before any ERP domain mutation. Unexpected failures escape and roll back.
                SaveFailure(queue, command, aggregate, previous, ex.Code, retryable: false);
            }
        }
        receipt.Status = ErpInboxStatus.Processed; receipt.ProcessedAtUtc = clock.GetUtcNow(); receipt.ErrorCode = null;
        await queue.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(Cp6InboxDisposition.Applied, "", command.PayloadSha256);
    }

    private async Task<Cp6InboxProcessingResult> RecordFailureAsync(Command command, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        RequireSql(queue);
        await using var transaction = await queue.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockMessageAsync(queue, command, ct);
        var receipt = await queue.Inbox.SingleOrDefaultAsync(x => x.MessageId == command.MessageId, ct);
        var existing = InspectReceipt(receipt, command);
        if (existing is not null)
        {
            if (existing.Disposition == Cp6InboxDisposition.PayloadConflict) await queue.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct); return existing;
        }
        receipt ??= NewReceipt(command);
        if (queue.Entry(receipt).State == EntityState.Detached) queue.Inbox.Add(receipt);
        receipt.AttemptCount++;
        var aggregate = await LoadAggregateAsync(queue, command, ct);
        var previous = await LoadRequestAsync(queue, command, ct);
        if (await HandlePriorAsync(queue, command, aggregate, previous, ct))
        {
            receipt.Status = ErpInboxStatus.Processed; receipt.ProcessedAtUtc = clock.GetUtcNow();
        }
        else
        {
            SaveFailure(queue, command, aggregate, previous, "C03_ERP_UNAVAILABLE", retryable: true);
            receipt.ErrorCode = "C03_ERP_UNAVAILABLE";
            receipt.RetryAtUtc = clock.GetUtcNow().AddSeconds(Math.Min(runtime.Options.MaximumRetrySeconds,
                runtime.Options.InitialRetrySeconds * Math.Pow(2, Math.Min(receipt.AttemptCount - 1, 20))));
            if (receipt.AttemptCount >= runtime.Options.MaxInboxAttempts)
            { receipt.Status = ErpInboxStatus.DeadLettered; receipt.ProcessedAtUtc = clock.GetUtcNow(); }
        }
        await queue.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return receipt.Status switch
        {
            ErpInboxStatus.Processed => new(Cp6InboxDisposition.Applied, "", command.PayloadSha256),
            ErpInboxStatus.DeadLettered => new(Cp6InboxDisposition.DeadLettered, "C03_REPLAY_REQUIRED", command.PayloadSha256),
            _ => new(Cp6InboxDisposition.RetryScheduled, "C03_ERP_UNAVAILABLE", command.PayloadSha256)
        };
    }

    private async Task<bool> HandlePriorAsync(ErpIntegrationContext queue, Command command,
        ErpIntegrationAggregate aggregate, ErpIntegrationRequest? previous, CancellationToken ct)
    {
        if (previous is not null && previous.InputSha256 != command.InputSha256)
        { EmitFailure(queue, command, aggregate, "C03_IDEMPOTENCY_CONFLICT"); return true; }
        if (previous?.Terminal == true)
        { Emit(queue, command, previous.ResultType, JsonSerializer.Deserialize<JsonElement>(previous.ResultDataJson), previous.ResultVersion); return true; }
        if (await queue.Requests.AnyAsync(x => x.TenantId == command.TenantId && x.RequestId == command.RequestId &&
                (x.Kind != command.Kind || x.AggregateId != command.AggregateId || x.RequestVersion != command.RequestVersion), ct))
        { EmitFailure(queue, command, aggregate, "C03_IDEMPOTENCY_CONFLICT"); return true; }
        if (command.RequestVersion < aggregate.LastRequestVersion ||
            (previous is null && command.AggregateVersion <= aggregate.LastAggregateVersion && aggregate.LastAggregateVersion != 0))
        { SaveFailure(queue, command, aggregate, previous, "C03_REQUEST_SUPERSEDED", false); return true; }
        return false;
    }

    private void SaveFailure(ErpIntegrationContext queue, Command command, ErpIntegrationAggregate aggregate,
        ErpIntegrationRequest? previous, string code, bool retryable)
    {
        var version = NextResultVersion(aggregate, command);
        var (type, data) = Failure(command, version, code, retryable);
        SaveOutcome(queue, command, previous, type, data, version, !retryable, false);
    }

    private void EmitFailure(ErpIntegrationContext queue, Command command, ErpIntegrationAggregate aggregate, string code)
    {
        var version = NextResultVersion(aggregate, command);
        var (type, data) = Failure(command, version, code, false);
        Emit(queue, command, type, data, version);
    }

    private static (string Type, object Data) Failure(Command c, int version, string code, bool retryable)
        => c.Kind == ErpRequestKind.BusinessPartner
            ? (ErpEventContracts.BusinessPartnerFailed, new ErpBusinessPartnerFailed(c.TenantId, c.RequestId, c.AccountId,
                c.RequestVersion, version, code, retryable))
            : (ErpEventContracts.OrderFailed, new ErpOrderFailed(c.TenantId, c.RequestId, c.AccountId, c.RequestVersion,
                c.AggregateId, version, code, retryable));

    private void SaveOutcome(ErpIntegrationContext queue, Command command, ErpIntegrationRequest? previous,
        string type, object data, int version, bool terminal, bool succeeded)
    {
        var request = previous ?? new ErpIntegrationRequest
        {
            TenantId = command.TenantId, Kind = command.Kind, AggregateId = command.AggregateId,
            RequestVersion = command.RequestVersion, RequestId = command.RequestId, AccountId = command.AccountId,
            InputSha256 = command.InputSha256
        };
        request.Terminal = terminal; request.Succeeded = succeeded; request.ResultVersion = version;
        request.ResultType = type; request.ResultDataJson = JsonSerializer.Serialize(data, data.GetType(), ErpEventContracts.Json);
        request.UpdatedAtUtc = clock.GetUtcNow();
        if (previous is null) queue.Requests.Add(request);
        Emit(queue, command, type, data, version);
    }

    private void Emit(ErpIntegrationContext queue, Command command, string type, object data, int version)
        => new Cp6OutboxStore<ErpIntegrationContext>(queue, runtime.Validator, clock).Enqueue(
            ErpEventContracts.Create(command.TenantId, command.AggregateId.ToString("D"), version, type,
                data, command.Region, command.CorrelationId, command.MessageId, clock));

    private static int NextResultVersion(ErpIntegrationAggregate aggregate, Command command)
    {
        aggregate.LastRequestVersion = Math.Max(aggregate.LastRequestVersion, command.RequestVersion);
        aggregate.LastAggregateVersion = Math.Max(aggregate.LastAggregateVersion, command.AggregateVersion);
        return aggregate.LastResultVersion = checked(aggregate.LastResultVersion + 1);
    }

    private async Task<ErpIntegrationAggregate> LoadAggregateAsync(ErpIntegrationContext queue, Command command, CancellationToken ct)
    {
        await ErpSqlLock.AcquireAsync(queue, $"c03:aggregate:{command.TenantId:D}:{(int)command.Kind}:{command.AggregateId:D}", ct);
        var result = await queue.Aggregates.SingleOrDefaultAsync(x => x.TenantId == command.TenantId &&
            x.Kind == command.Kind && x.AggregateId == command.AggregateId, ct);
        if (result is not null) return result;
        result = new() { TenantId = command.TenantId, Kind = command.Kind, AggregateId = command.AggregateId };
        queue.Aggregates.Add(result); return result;
    }

    private static Task<ErpIntegrationRequest?> LoadRequestAsync(ErpIntegrationContext queue, Command command, CancellationToken ct)
        => queue.Requests.SingleOrDefaultAsync(x => x.TenantId == command.TenantId && x.Kind == command.Kind &&
            x.AggregateId == command.AggregateId && x.RequestVersion == command.RequestVersion, ct);

    private Cp6InboxProcessingResult? InspectReceipt(ErpInboxReceipt? receipt, Command command)
    {
        if (receipt is null) return null;
        if (receipt.PayloadSha256 != command.PayloadSha256 || receipt.TenantId != command.TenantId)
        {
            receipt.ConflictCount++; receipt.LastConflictSha256 = command.PayloadSha256;
            return new(Cp6InboxDisposition.PayloadConflict, "C03_MESSAGE_ID_CONFLICT", command.PayloadSha256);
        }
        if (receipt.Status == ErpInboxStatus.Processed) return new(Cp6InboxDisposition.Duplicate, "", command.PayloadSha256);
        if (receipt.Status == ErpInboxStatus.DeadLettered) return new(Cp6InboxDisposition.DeadLettered, "C03_REPLAY_REQUIRED", command.PayloadSha256);
        if (receipt.RetryAtUtc > clock.GetUtcNow()) return new(Cp6InboxDisposition.RetryScheduled, "C03_RETRY_NOT_DUE", command.PayloadSha256);
        return null;
    }

    private ErpInboxReceipt NewReceipt(Command c) => new()
    {
        MessageId = c.MessageId, TenantId = c.TenantId, EventType = c.EventType, AggregateId = c.AggregateId,
        AggregateVersion = c.AggregateVersion, Payload = c.Payload, PayloadSha256 = c.PayloadSha256,
        ReceivedAtUtc = clock.GetUtcNow(), Status = ErpInboxStatus.Processing
    };

    private static CP6Context CreateBusinessContext(ErpIntegrationContext queue, Guid tenant)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(queue.Database.GetDbConnection()).Options,
            new TenantContext { CurrentTenantId = tenant });

    private static Task LockMessageAsync(ErpIntegrationContext queue, Command c, CancellationToken ct)
        => ErpSqlLock.AcquireAsync(queue, "c03:message:" + ErpEventContracts.Hash(Encoding.UTF8.GetBytes(c.MessageId)), ct);
    private static void RequireSql(ErpIntegrationContext queue)
    {
        if (!queue.Database.IsSqlServer()) throw new InvalidOperationException("C03_REQUIRES_REAL_SQL_SERVER");
    }
    private static Cp6InboxProcessingResult Invalid(string digest) => new(Cp6InboxDisposition.Invalid, "C03_EVENT_INVALID", digest);

    private sealed record Command(Guid TenantId, Guid RequestId, Guid AccountId, int RequestVersion, Guid AggregateId,
        int AggregateVersion, ErpRequestKind Kind, string MessageId, string EventType, string CorrelationId,
        string Region, byte[] Payload, string PayloadSha256, string InputSha256,
        ErpBusinessPartnerRequested? BusinessPartner, ErpOrderRequested? Order)
    {
        public static Command Read(JsonElement root, byte[] payload, string digest)
        {
            var type = root.GetProperty("type").GetString()!;
            var data = root.GetProperty("data");
            var bp = type == ErpEventContracts.BusinessPartnerRequested ? data.Deserialize<ErpBusinessPartnerRequested>(ErpEventContracts.Json) : null;
            var order = type == ErpEventContracts.OrderRequested ? data.Deserialize<ErpOrderRequested>(ErpEventContracts.Json) : null;
            // A numerically identical amount retains the same command identity across
            // JSON serializers and SQL decimal scales (300 and 300.00 are one request).
            object input = bp is not null ? bp : new
            {
                order!.TenantId, order.RequestId, order.AccountId, order.RequestVersion, order.OpportunityId,
                order.BusinessPartnerKey, order.QuotationKey, order.QuotationVersion,
                ExpectedAmount = order.ExpectedAmount.ToString("G29", CultureInfo.InvariantCulture), order.Currency
            };
            var hash = ErpEventContracts.Hash(JsonSerializer.SerializeToUtf8Bytes(input, input.GetType(), ErpEventContracts.Json));
            return new(data.GetProperty("tenantId").GetGuid(), data.GetProperty("requestId").GetGuid(),
                data.GetProperty("accountId").GetGuid(), data.GetProperty("requestVersion").GetInt32(),
                root.GetProperty("aggregateid").GetGuid(), root.GetProperty("aggregateversion").GetInt32(),
                bp is null ? ErpRequestKind.Order : ErpRequestKind.BusinessPartner, root.GetProperty("id").GetString()!, type,
                root.GetProperty("correlationid").GetString()!, root.GetProperty("region").GetString()!, payload, digest, hash, bp, order);
        }
    }
}
