using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.ErpIntegration.IntegrationOpsContractV1;

namespace CP6.Core.Services.ErpIntegration;

public sealed class ErpIntegrationOpsSourceV1(IDbContextFactory<ErpIntegrationContext> factory,
    ErpIntegrationRuntime runtime) : IIntegrationOpsSourceV1
{
    public string OwnerId => Owner;
    public string RouteEpoch => runtime.Options.OpsRouteEpoch;

    public Task<bool> CanReadAsync(Guid tenant, Guid actor, CancellationToken ct) =>
        Task.FromResult(runtime.Options.Tenants.ContainsKey(tenant));

    public async Task<IReadOnlyList<IntegrationFailureRecordV1>?> ReadAsync(Guid tenant, Guid actor,
        IntegrationFailureWindowV1 window, IntegrationFailureKeyV1? after, int take, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        if (!queue.Database.IsNpgsql()) return null;
        var rows = await queue.Database.SqlQuery<FailureRow>($"""
            WITH failures AS (
                SELECT {Inbox}::text COLLATE "C" AS "Type", "MessageId"::text COLLATE "C" AS "Id",
                    "RowVersion" AS "Version", "ProcessedAtUtc" AS "At", "ReceivedAtUtc" AS "Seen",
                    "ErrorCode" AS "Code", "AttemptCount" AS "Attempts"
                FROM erp_integration."CommandInbox"
                WHERE "TenantId"={tenant} AND "Status"={(int)ErpInboxStatus.DeadLettered}
                UNION ALL
                SELECT {Outbox}::text COLLATE "C", "Id"::text COLLATE "C", "RowVersion", "DeadLetteredAtUtc", "AvailableAtUtc",
                    "LastErrorCode", "AttemptCount"
                FROM erp_integration."Cp6_OutboxMessage"
                WHERE "TenantId"={tenant} AND "TopicName"={ErpEventContracts.ResultTopic} AND "Status"={(int)Cp6OutboxStatus.DeadLettered}
                UNION ALL
                SELECT {Bridge}::text COLLATE "C", "OrderKey"::text COLLATE "C", "RowVersion", NULL::timestamptz, "AvailableAtUtc",
                    "LastErrorCode", "AttemptCount"
                FROM erp_integration."OrderBridgeDispatch"
                WHERE "TenantId"={tenant} AND "CompletedAtUtc" IS NULL AND "AttemptCount">=10
            )
            SELECT "Type","Id","Version","At","Code","Attempts" FROM failures
            WHERE ("At" IS NULL AND "Seen"<={window.SnapshotAt} OR "At">={window.FromUtc} AND "At"<={window.UntilUtc})
              AND ({after == null} OR
                (CASE WHEN "At" IS NULL THEN 1 ELSE 0 END, COALESCE("At",'-infinity'::timestamptz),
                  {Owner}::text COLLATE "C","Type","Id")
                  > ({after?.TimePartition ?? 0},{after?.OccurredAtUtc ?? DateTimeOffset.MinValue},
                    {after?.OwnerId ?? ""}::text COLLATE "C",{after?.NativeType ?? ""}::text COLLATE "C",
                    {after?.NativeId ?? ""}::text COLLATE "C"))
            ORDER BY "At" ASC NULLS LAST,"Type","Id" LIMIT {take}
            """).ToArrayAsync(ct);
        return rows.Select(row => Record(row.Type, row.Id, row.At, row.Version, row.Code, row.Attempts, "DEAD_LETTERED")).ToArray();
    }

    internal static IntegrationFailureRecordV1 Record(string type, string id, DateTimeOffset? at, byte[] version,
        string? code, int attempts, string state) =>
        new(new(at.HasValue ? 0 : 1, at, Owner, type, id, FailureKey(type, id)), state, Convert.ToBase64String(version),
            code, attempts);

    internal static string FailureKey(string type, string id) =>
        ErpEventContracts.Hash(Encoding.UTF8.GetBytes(Owner + "\n" + type + "\n" + id));

    private sealed class FailureRow
    {
        public string Type { get; init; } = "";
        public string Id { get; init; } = "";
        public byte[] Version { get; init; } = [];
        public DateTimeOffset? At { get; init; }
        public string? Code { get; init; }
        public int Attempts { get; init; }
    }

    internal sealed record Native(IntegrationFailureRecordV1 Record, IntegrationReplayEvidenceV1 Evidence,
        IntegrationRequestRefV1 OriginalRequest, string? MessageId, string? EnvelopeRef, string? ResultVersion,
        string OwnerOutcome, bool Eligible, ErpRequestKind? RequestKind = null, Guid? AggregateId = null);

    internal async Task<Native> ReadNativeAsync(ErpIntegrationContext queue, CP6Context db, IntegrationQueueRefV1 key,
        CancellationToken ct)
    {
        if (key.OwnerId != Owner) throw Error("FAILURE_NOT_FOUND", 404);
        var tenant = db.CurrentTenantId;
        var now = runtime.Clock.GetUtcNow();
        if (key.NativeType == Inbox)
        {
            var item = await queue.Inbox.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.MessageId == key.NativeId, ct) ?? throw Error("FAILURE_NOT_FOUND", 404);
            var command = ErpRequestHandler.ReadOriginal(item, runtime) ?? throw Error("ORIGINAL_EVIDENCE_UNAVAILABLE");
            var prior = await queue.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.RequestId == command.RequestId, ct);
            var superseded = await queue.Aggregates.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Kind == command.Kind && x.AggregateId == command.AggregateId &&
                (x.LastRequestVersion > command.RequestVersion || x.LastAggregateVersion > command.AggregateVersion), ct);
            var conflict = prior is not null && (prior.Kind != command.Kind || prior.AggregateId != command.AggregateId ||
                prior.RequestVersion != command.RequestVersion || prior.InputSha256 != command.InputSha256);
            var outcome = conflict ? "EVIDENCE_CONFLICT" : superseded ? "SUPERSEDED" :
                prior?.Terminal == true ? Outcome(prior) : "UNKNOWN";
            return new(Record(Inbox, item.MessageId, item.ProcessedAtUtc, item.RowVersion, item.ErrorCode, item.AttemptCount,
                    item.Status == ErpInboxStatus.DeadLettered ? "DEAD_LETTERED" : item.Status.ToString()),
                new(Replay, key, Digest(Inbox, item.PayloadSha256)), Original(command.RequestId, command.RequestVersion),
                item.MessageId, item.MessageId, null, outcome,
                item.Status == ErpInboxStatus.DeadLettered && !(item.RetryAtUtc > now) && outcome == "UNKNOWN",
                command.Kind, command.AggregateId);
        }
        if (key.NativeType == Outbox && Guid.TryParseExact(key.NativeId, "D", out var id))
        {
            var item = await queue.Set<Cp6OutboxMessage>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == id, ct) ?? throw Error("FAILURE_NOT_FOUND", 404);
            var envelope = new Cp6OutboxEnvelope(item.MessageId, item.TenantId, item.TopicName, item.PartitionKey, item.Payload,
                item.CorrelationId, item.CausationId, item.AggregateId, item.AggregateVersion);
            if (item.TopicName != ErpEventContracts.ResultTopic || ErpEventContracts.Hash(item.Payload) != item.PayloadSha256 ||
                !runtime.Validator.Validate(envelope).IsValid) throw Error("ORIGINAL_EVIDENCE_UNAVAILABLE");
            using var document = JsonDocument.Parse(item.Payload);
            var root = document.RootElement;
            var data = root.GetProperty("data");
            var requestId = data.GetProperty("requestId").GetGuid();
            var requestVersion = data.GetProperty("requestVersion").GetInt32();
            var resultVersion = data.GetProperty("resultVersion").GetInt32();
            var prior = await queue.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.RequestId == requestId, ct);
            if (root.GetProperty("region").GetString() != runtime.Options.Tenants[tenant] || prior is null ||
                prior.AccountId != data.GetProperty("accountId").GetGuid() || prior.RequestVersion != requestVersion ||
                prior.AggregateId != root.GetProperty("aggregateid").GetGuid() || item.AggregateVersion != resultVersion ||
                resultVersion > prior.ResultVersion ||
                resultVersion == prior.ResultVersion && (root.GetProperty("type").GetString() != prior.ResultType ||
                    !JsonNode.DeepEquals(JsonNode.Parse(prior.ResultDataJson), JsonNode.Parse(data.GetRawText()))))
                throw Error("ORIGINAL_EVIDENCE_UNAVAILABLE");
            var deliverable = item.Status == Cp6OutboxStatus.DeadLettered &&
                !(item.LeaseExpiresAtUtc > now) && (item.LeaseOwner == null || item.LeaseExpiresAtUtc != null);
            var outcome = resultVersion == prior.ResultVersion && prior.Terminal ? Outcome(prior) : "RESULT_COMMITTED";
            return new(Record(Outbox, key.NativeId, item.DeadLetteredAtUtc, item.RowVersion, item.LastErrorCode, item.AttemptCount,
                item.Status == Cp6OutboxStatus.DeadLettered ? "DEAD_LETTERED" : item.Status.ToString()),
                new(Deliver, key, Digest(Outbox, item.PayloadSha256)), Original(requestId, requestVersion),
                item.MessageId, item.MessageId, Number(resultVersion), outcome, deliverable);
        }
        if (key.NativeType != Bridge) throw Error("FAILURE_NOT_FOUND", 404);
        var bridge = await queue.OrderBridges.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant &&
            x.OrderKey == key.NativeId, ct) ?? throw Error("FAILURE_NOT_FOUND", 404);
        var priorOrders = await queue.Requests.FromSqlInterpolated($"""
            SELECT * FROM erp_integration."Request"
            WHERE "TenantId"={tenant} AND "Kind"={(int)ErpRequestKind.Order} AND "Terminal" AND "Succeeded"
              AND "ResultType"={ErpEventContracts.OrderCreated} AND "ResultDataJson"::jsonb->>'orderKey'={key.NativeId}
            LIMIT 2
            """).AsNoTracking().ToArrayAsync(ct);
        if (priorOrders.Length != 1 || !await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant && x.WebOrderNo == key.NativeId && !x.IsDeleted, ct))
            throw Error("ORIGINAL_EVIDENCE_UNAVAILABLE");
        var request = priorOrders[0];
        var eligible = bridge.CompletedAtUtc is null && bridge.AttemptCount >= 10 &&
            bridge.LastErrorCode == "C03_BRIDGE_REPLAY_REQUIRED" && !(bridge.LeaseExpiresAtUtc > now) &&
            (bridge.LeaseOwner == null || bridge.LeaseExpiresAtUtc != null);
        return new(Record(Bridge, key.NativeId, null, bridge.RowVersion, bridge.LastErrorCode, bridge.AttemptCount,
                bridge.CompletedAtUtc != null ? "COMPLETED" : bridge.AttemptCount >= 10 ? "DEAD_LETTERED" : "PENDING"),
            new(Resume, key, Digest(Bridge, ErpDeliveryReplayService.BridgeHash(tenant, key.NativeId))),
            Original(request.RequestId, request.RequestVersion), null, null, Number(request.ResultVersion), "APPLIED", eligible);
    }

    internal static string Outcome(ErpIntegrationRequest request) =>
        request.Terminal ? request.Succeeded ? "APPLIED" : "BUSINESS_REJECTED" : "UNKNOWN";
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    internal static IntegrationRequestRefV1 Original(Guid id, int version) => new(id, Number(version));
}
