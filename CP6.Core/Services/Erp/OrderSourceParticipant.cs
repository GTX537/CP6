using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Platform.Messaging;
using CP6.Platform.Messaging.NativeContracts;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.OrderSourceFacts;

namespace CP6.Core.Services.Erp;

public sealed record OrderSourceScope(Guid OrderId, Guid LineId, string OrderVersion, string ScheduleId);
public sealed record OrderSourceReceipt(string Producer, string OperationKey, OrderSourceScope Scope, string LineageId,
    long SourceVersion, string? ReleaseId, string? ReleaseLineId,
    string SourceDigest, string BodySha256, string EnvelopeSha256, string Handoff, string State);
public sealed record OrderSourceGate(string State, long? CurrentSourceVersion);

// 仅参加已获授权的调用方事务。此端口的结果表示本地记录，业务决定和对端结果分别由其 Owner 持有。
internal sealed class OrderSourceParticipant(CP6Context db)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    internal const string SourceContract = "ORD-DEM-SOURCE/1";
    private static readonly string[] BodyFields =
    [
        "schemaVersion", "operationKey", "kind", "order", "salesReleaseId", "salesReleaseLineId", "releaseLineageId",
        "sourceVersion", "expectedAcceptedSourceVersion", "previousIssuedSourceVersion", "interveningFinalResults",
        "target", "controls", "approvalBindings", "cancelAllocation", "activationManifest", "sourceDigest"
    ];

    internal sealed class Confirmed
    {
        public string OperationKey { get; init; } = "";
        public Guid ActorId { get; init; }
        public Guid OrderId { get; init; }
        public Guid LineId { get; init; }
        public string OrderVersion { get; init; } = "";
        public string ScheduleId { get; init; } = "";
        public byte[] Proof { get; init; } = [];
        public byte[] Schedule { get; init; } = [];
        public string ProofSha256 { get; init; } = "";
        public string ScheduleSha256 { get; init; } = "";
        public string NativeOrderVersion { get; init; } = "";
        public string NativeLineVersion { get; init; } = "";
        public string SupplyDigest { get; init; } = "";
        public long LegacyRevision { get; init; }
        internal OrderSourceScope Scope => new(OrderId, LineId, OrderVersion, ScheduleId);
    }

    internal sealed class Saved
    {
        public Guid ActorId { get; init; }
        public byte[] Result { get; init; } = [];
        public byte[] Body { get; init; } = [];
        public byte[] Envelope { get; init; } = [];
        internal OrderSourceReceipt Receipt
        {
            get
            {
                var value = JsonSerializer.Deserialize<OrderSourceReceipt>(Result);
                if (value is null || Digest(Body) != value.BodySha256 || Digest(Envelope) != value.EnvelopeSha256)
                    throw Error("SALES_SOURCE_ORIGINAL_UNAVAILABLE", 503);
                return value;
            }
        }
    }

    internal async Task EnlistConfirm(DbTransaction caller, Guid actor, string operationKey, OrderSourceScope scope,
        string nativeOrderVersion, string nativeLineVersion, ReadOnlyMemory<byte> originalSchedule,
        ReadOnlyMemory<byte> originalConfirmation, CancellationToken ct = default)
    {
        RequireTransaction(db, caller);
        RequireActor(actor);
        ValidateScope(scope);
        Key(operationKey);
        var schedule = Copy(originalSchedule);
        var proof = Copy(originalConfirmation);
        ReadJson(schedule, root =>
        {
            RequireFields(root, "scheduleId", "orderLineId", "quantity", "requiredDate", "promisedDate", "supplyTarget");
            if (String(root, "scheduleId") != scope.ScheduleId || String(root, "orderLineId") != scope.LineId.ToString("D"))
                throw Error("SALES_SOURCE_SCOPE_CONFLICT", 409);
            Quantity(root.GetProperty("quantity"));
            return true;
        });

        await LockNative(db, scope.OrderId, ct);
        await Lock("confirm", operationKey, ct);
        var prior = await ReadConfirmedByKey(operationKey, ct);
        if (prior is not null)
        {
            if (prior.Scope != scope || prior.ActorId != actor || prior.NativeOrderVersion != nativeOrderVersion ||
                prior.NativeLineVersion != nativeLineVersion || !schedule.AsSpan().SequenceEqual(prior.Schedule) ||
                !proof.AsSpan().SequenceEqual(prior.Proof))
                throw Error("COMMAND_KEY_REUSED", 409);
            return;
        }

        var (order, line) = await LoadNative(scope, ct);
        if (order.NativeVersion != nativeOrderVersion || line.NativeVersion != nativeLineVersion
            || string.IsNullOrEmpty(nativeOrderVersion) || string.IsNullOrEmpty(nativeLineVersion))
            throw Error("VERSION_STALE", 412);
        if (await ConfirmedScope(scope, ct) is not null) throw Error("SOURCE_CONFLICT", 409);

        var revision = await Capture(db, order.Number, "Sales.BeforeConfirm", ct);
        var supply = SupplyDigest(order, line);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_sales_order_source_v1.confirmed_scopes
              (tenant_id, operation_key, actor_id, order_id, line_id, order_version, schedule_id, proof, proof_sha256,
               schedule, schedule_sha256, native_order_version, native_line_version, supply_sha256, legacy_revision)
            VALUES ({db.CurrentTenantId},{operationKey},{actor},{scope.OrderId},{scope.LineId},{scope.OrderVersion},
              {scope.ScheduleId},{proof},{Digest(proof)},{schedule},{Digest(schedule)},{nativeOrderVersion},
              {nativeLineVersion},{supply},{revision})
            """, ct);
    }

    internal Task<OrderSourceReceipt> EnlistRelease(DbTransaction caller, Guid actor, string producer,
        OrderSourceScope scope, ReadOnlyMemory<byte> originalBody, ReadOnlyMemory<byte> originalEnvelope,
        CancellationToken ct = default) =>
        EnlistPublication(caller, actor, producer, scope, "H10", originalBody, originalEnvelope, ct);

    internal Task<OrderSourceReceipt> EnlistControl(DbTransaction caller, Guid actor, string producer,
        OrderSourceScope scope, ReadOnlyMemory<byte> originalBody, ReadOnlyMemory<byte> originalEnvelope,
        CancellationToken ct = default) =>
        EnlistPublication(caller, actor, producer, scope, "H32", originalBody, originalEnvelope, ct);

    private async Task<OrderSourceReceipt> EnlistPublication(DbTransaction caller, Guid actor, string producer,
        OrderSourceScope scope, string handoff, ReadOnlyMemory<byte> originalBody, ReadOnlyMemory<byte> originalEnvelope,
        CancellationToken ct)
    {
        RequireTransaction(db, caller);
        RequireActor(actor);
        ValidateScope(scope);
        Key(producer);
        var body = Copy(originalBody);
        var envelope = Copy(originalEnvelope);
        var operation = Parse(body, scope, handoff);
        if (ReadJson(envelope, root =>
        {
            _ = String(root, "id");
            _ = String(root, "type");
            return String(root, "specversion") != "1.0" || String(root, "tenantid") != db.CurrentTenantId.ToString("D")
                || !Uri.TryCreate(String(root, "source"), UriKind.Absolute, out _)
                || String(root, "datacontenttype") != Cp6CloudEventCodec.JsonDataContentType
                || String(root, "aggregateid") != operation.Lineage
                || !Encoding.UTF8.GetBytes(root.GetProperty("data").GetRawText()).AsSpan().SequenceEqual(body);
        }))
            throw Error("SALES_SOURCE_ENVELOPE_MISMATCH", 409);
        await LockNative(db, scope.OrderId, ct);
        await Lock("operation", JsonSerializer.Serialize(new[] { producer, operation.Key }), ct);
        var prior = await ReadOperation(producer, operation.Key, ct);
        if (prior is not null)
        {
            var receipt = prior.Receipt;
            if (receipt.Scope != scope || receipt.Handoff != handoff || prior.ActorId != actor
                || !prior.Body.AsSpan().SequenceEqual(body))
                throw Error("COMMAND_KEY_REUSED", 409);
            // 运输重投可携带另一封装。原版本、原回执和原 Outbox 始终保留最初的字节。
            return receipt;
        }

        var (order, line) = await LoadNative(scope, ct, requireActive: handoff == "H10");
        await Lock("lineage", operation.Lineage, ct);
        var head = await ReadFrontier(operation.Lineage, ct);
        if (head is not null && (head.Scope.OrderId != scope.OrderId || head.Scope.LineId != scope.LineId ||
            head.Scope.ScheduleId != scope.ScheduleId))
            throw Error("SOURCE_CONFLICT", 409);
        if (operation.Previous != head?.SourceVersion || head?.SourceVersion == long.MaxValue ||
            operation.Version != (head?.SourceVersion ?? 0) + 1
            || handoff == "H32" && (operation.ReleaseId != head?.ReleaseId || operation.ReleaseLineId != head?.ReleaseLineId))
            throw Error("SOURCE_CONFLICT", 409);

        var confirmed = await ConfirmedScope(scope, ct) ?? throw Error("SALES_SOURCE_UNPROVEN", 503);
        if (handoff == "H10" && !await CurrentSupply(order, line, confirmed, ct))
            throw Error("LOCAL_ELIGIBILITY_CHANGED", 422);

        var result = new OrderSourceReceipt(producer, operation.Key, scope, operation.Lineage, operation.Version,
            operation.ReleaseId, operation.ReleaseLineId, operation.SourceDigest, Digest(body), Digest(envelope),
            handoff, "LOCAL_REQUEST");
        var receiptBytes = MainOwnerOperationJournalV1.Encode(result);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH original AS (
              INSERT INTO cp6_sales_order_source_v1.operations
                (tenant_id,producer,operation_key,actor_id,order_id,line_id,order_version,schedule_id,lineage_id,
                 source_version,source_digest,body_sha256,body,receipt)
              VALUES ({db.CurrentTenantId},{producer},{operation.Key},{actor},{scope.OrderId},{scope.LineId},
                {scope.OrderVersion},{scope.ScheduleId},{operation.Lineage},{operation.Version},{operation.SourceDigest},
                {result.BodySha256},{body},{receiptBytes})
              RETURNING tenant_id,producer,operation_key,lineage_id,source_version
            ), enqueued AS (
              INSERT INTO cp6_sales_order_source_v1.outbox
                (tenant_id,producer,operation_key,handoff,envelope,envelope_sha256)
              SELECT tenant_id,producer,operation_key,{handoff},{envelope},{result.EnvelopeSha256} FROM original
              RETURNING tenant_id,producer,operation_key
            )
            INSERT INTO cp6_sales_order_source_v1.frontiers (tenant_id,lineage_id,source_version)
            SELECT tenant_id,lineage_id,source_version FROM original JOIN enqueued USING(tenant_id,producer,operation_key)
            ON CONFLICT (tenant_id,lineage_id) DO UPDATE SET source_version=excluded.source_version
            """, ct);
        return result;
    }

    internal Task<OrderSourceGate> CheckCurrentSource(DbTransaction caller, OrderSourceScope scope,
        string lineageId, long sourceVersion, string digest, CancellationToken ct = default) =>
        CheckCurrent(caller, scope, lineageId, sourceVersion, digest, publishControl: false, ct);

    internal Task<OrderSourceGate> CheckCurrentPublication(DbTransaction caller, OrderSourceReceipt expected,
        CancellationToken ct = default) =>
        CheckCurrent(caller, expected.Scope, expected.LineageId, expected.SourceVersion, expected.SourceDigest,
            publishControl: expected.Handoff == "H32", ct);

    // 发布和发运调用方共用此门。调用方在同一事务内使用结论，本门只证明 Sales 当前来源。
    private async Task<OrderSourceGate> CheckCurrent(DbTransaction caller, OrderSourceScope scope,
        string lineageId, long sourceVersion, string digest, bool publishControl, CancellationToken ct)
    {
        RequireTransaction(db, caller);
        ValidateScope(scope);
        Key(lineageId);
        if (sourceVersion < 1 || !CommercialWireV1.IsDigest(digest)) throw Error("SCHEMA_INVALID", 400);
        await LockNative(db, scope.OrderId, ct);
        await Lock("lineage", lineageId, ct);
        var head = await ReadFrontier(lineageId, ct);
        if (head is null || head.Scope.OrderId != scope.OrderId || head.Scope.LineId != scope.LineId
            || head.Scope.ScheduleId != scope.ScheduleId)
            return new("UNPROVEN", null);
        var exact = head.Scope == scope && head.SourceVersion == sourceVersion && head.SourceDigest == digest;
        if (head.Handoff == "H32")
        {
            if (exact && publishControl && await ConfirmedScope(scope, ct) is not null)
                return new("CURRENT", head.SourceVersion);
            return new("FENCED", head.SourceVersion);
        }
        if (!exact || publishControl) return new("STALE", head.SourceVersion);
        var order = await ReadNative(db, scope.OrderId, ct);
        var line = order?.Lines.SingleOrDefault(x => x.Id == scope.LineId);
        if (order is null || line is null) return new("UNPROVEN", head.SourceVersion);
        if (!Active(order, line)) return new("FENCED", head.SourceVersion);
        var proof = await ConfirmedScope(scope, ct);
        return new(proof is not null && await CurrentSupply(order, line, proof, ct) ? "CURRENT" : "UNPROVEN",
            head.SourceVersion);
    }

    internal Task<Saved?> ReadOperation(string producer, string key, CancellationToken ct) =>
        db.Database.SqlQuery<Saved>($"""
            SELECT op.actor_id AS "ActorId",op.receipt AS "Result",op.body AS "Body",outbox.envelope AS "Envelope"
            FROM cp6_sales_order_source_v1.operations op
            JOIN cp6_sales_order_source_v1.outbox outbox USING (tenant_id,producer,operation_key)
            WHERE op.tenant_id={db.CurrentTenantId} AND op.producer={producer} AND op.operation_key={key}
            """).SingleOrDefaultAsync(ct);

    internal async Task<OrderSourceReceipt?> ReadFrontier(string lineage, CancellationToken ct) =>
        (await db.Database.SqlQuery<Saved>($"""
            SELECT op.actor_id AS "ActorId",op.receipt AS "Result",op.body AS "Body",outbox.envelope AS "Envelope"
            FROM cp6_sales_order_source_v1.frontiers frontier
            JOIN cp6_sales_order_source_v1.operations op USING (tenant_id,lineage_id,source_version)
            JOIN cp6_sales_order_source_v1.outbox outbox USING (tenant_id,producer,operation_key)
            WHERE frontier.tenant_id={db.CurrentTenantId} AND frontier.lineage_id={lineage}
            """).SingleOrDefaultAsync(ct))?.Receipt;

    private IQueryable<Confirmed> ConfirmedOriginals => db.Database.SqlQuery<Confirmed>($"""
        SELECT operation_key AS "OperationKey",actor_id AS "ActorId",order_id AS "OrderId",line_id AS "LineId",
          order_version AS "OrderVersion",schedule_id AS "ScheduleId",proof AS "Proof",schedule AS "Schedule",
          proof_sha256 AS "ProofSha256",schedule_sha256 AS "ScheduleSha256",supply_sha256 AS "SupplyDigest",
          legacy_revision AS "LegacyRevision",
          native_order_version AS "NativeOrderVersion",native_line_version AS "NativeLineVersion"
        FROM cp6_sales_order_source_v1.confirmed_scopes WHERE tenant_id={db.CurrentTenantId}
        """);

    internal async Task<Confirmed?> ConfirmedScope(OrderSourceScope scope, CancellationToken ct) =>
        Verify(await ConfirmedOriginals.SingleOrDefaultAsync(c => c.OrderId == scope.OrderId && c.LineId == scope.LineId
            && c.OrderVersion == scope.OrderVersion && c.ScheduleId == scope.ScheduleId, ct));

    private async Task<Confirmed?> ReadConfirmedByKey(string key, CancellationToken ct) =>
        Verify(await ConfirmedOriginals.SingleOrDefaultAsync(c => c.OperationKey == key, ct));

    private static Confirmed? Verify(Confirmed? value)
    {
        if (value is not null && (Digest(value.Proof) != value.ProofSha256 || Digest(value.Schedule) != value.ScheduleSha256))
            throw Error("SALES_SOURCE_ORIGINAL_UNAVAILABLE", 503);
        return value;
    }

    private Task<int> Lock(string scope, string key, CancellationToken ct)
    {
        var lockKey = JsonSerializer.Serialize(new[] { "Main.Sales/1", db.CurrentTenantId.ToString("D"), scope, key });
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
    }

    private async Task<(LegacyOrderSourceSnapshot Order, LegacyOrderSourceLine Line)> LoadNative(
        OrderSourceScope scope, CancellationToken ct, bool requireActive = true)
    {
        var order = await ReadNative(db, scope.OrderId, ct);
        var line = order?.Lines.SingleOrDefault(l => l.Id == scope.LineId);
        if (order is null || line is null) throw Error("SALES_SOURCE_UNPROVEN", 503);
        if (requireActive && !Active(order, line)) throw Error("LOCAL_ELIGIBILITY_CHANGED", 422);
        return (order, line);
    }

    private async Task<bool> CurrentSupply(LegacyOrderSourceSnapshot order, LegacyOrderSourceLine line,
        Confirmed proof, CancellationToken ct) =>
        SupplyDigest(order, line) == proof.SupplyDigest && !await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM cp6_sales_order_source_v1.legacy_originals
                WHERE tenant_id={db.CurrentTenantId} AND order_id={order.Id}
                  AND revision>{proof.LegacyRevision} AND writer='Order.Edit') AS "Value"
            """).SingleAsync(ct);

    private static bool Active(LegacyOrderSourceSnapshot order, LegacyOrderSourceLine line) =>
        !order.Deleted && !line.Deleted && order.CancelledAt is null &&
        order.Lifecycle is OrderLifecycleStatus.Confirmed or OrderLifecycleStatus.InProduction;

    private static void RequireActor(Guid actor)
    {
        if (actor == Guid.Empty) throw Error("SALES_SOURCE_AUTHORITY_UNPROVEN", 503);
    }

    internal static void ValidateScope(OrderSourceScope scope)
    {
        if (scope is null || scope.OrderId == Guid.Empty || scope.LineId == Guid.Empty) throw Error("SCHEMA_INVALID", 400);
        Key(scope.OrderVersion);
        Key(scope.ScheduleId);
    }

    internal static string Key(string? value)
    {
        try { return CommercialWireV1.RequireKey(value, 256); }
        catch (CommercialProtocolException) { throw Error("SCHEMA_INVALID", 400); }
    }

    private sealed record Operation(string Key, string Lineage, long Version, long? Previous,
        string? ReleaseId, string? ReleaseLineId, string SourceDigest);

    private static Operation Parse(byte[] body, OrderSourceScope scope, string handoff) => ReadJson<Operation>(body, root =>
    {
        RequireFields(root, BodyFields);
        if (String(root, "schemaVersion") != SourceContract) throw Error("SCHEMA_INVALID", 400);
        RequireScope(root.GetProperty("order"), scope);
        var lineage = String(root, "releaseLineageId");

        var target = root.GetProperty("target");
        var controls = ArrayLength(root, "controls");
        _ = ArrayLength(root, "approvalBindings");
        var intermediate = ArrayLength(root, "interveningFinalResults");
        var previous = NullableRevision(root, "previousIssuedSourceVersion");
        var accepted = NullableRevision(root, "expectedAcceptedSourceVersion");
        var kind = String(root, "kind");
        var release = OptionalKey(root, "salesReleaseId");
        var releaseLine = OptionalKey(root, "salesReleaseLineId");
        if ((release is null) != (releaseLine is null)) throw Error("SCHEMA_INVALID", 400);
        // 普通后继需要 Planning 的准确终态原件。当前参加端口支持初始释放和独立控制轴。
        if (handoff == "H10" && (kind != "INITIAL_RELEASE" || previous is not null || accepted is not null ||
            intermediate != 0 || release is null || controls != 0 || target.ValueKind != JsonValueKind.Object))
            throw Error("SALES_SOURCE_PREDECESSOR_UNPROVEN", 409);
        if (handoff == "H32" && (kind != "CONTROL_SOURCE" || controls == 0 || target.ValueKind != JsonValueKind.Null))
            throw Error("SCHEMA_INVALID", 400);
        if (root.GetProperty("cancelAllocation").ValueKind != JsonValueKind.Null ||
            root.GetProperty("activationManifest").ValueKind != JsonValueKind.Null)
            throw Error("SALES_SOURCE_PREDECESSOR_UNPROVEN", 409);
        if (handoff == "H10") RequireTarget(target);
        foreach (var control in root.GetProperty("controls").EnumerateArray()) RequireControl(control, scope, lineage);

        var digest = String(root, "sourceDigest");
        if (!CommercialWireV1.IsDigest(digest)
            || CommercialCanonicalV1.Digest("ORD-DEM-SOURCE-1", root, omitRoot: ["sourceDigest"]) != digest)
            throw Error("SALES_SOURCE_DIGEST_MISMATCH", 409);
        return new(String(root, "operationKey"), lineage, Revision(root.GetProperty("sourceVersion")), previous,
            release, releaseLine, digest);
    });

    private static void RequireScope(JsonElement order, OrderSourceScope scope)
    {
        RequireFields(order, "orderId", "orderLineId", "orderVersion", "scheduleId");
        if (String(order, "orderId") != scope.OrderId.ToString("D") || String(order, "orderLineId") != scope.LineId.ToString("D")
            || String(order, "orderVersion") != scope.OrderVersion || String(order, "scheduleId") != scope.ScheduleId)
            throw Error("SALES_SOURCE_SCOPE_CONFLICT", 409);
    }

    private static void RequireTarget(JsonElement target)
    {
        RequireFields(target, "transactionQuantity", "standardQuantity", "conversion", "site",
            "requiredDate", "technical", "supplyMode", "purposeEvidence");
        Quantity(target.GetProperty("transactionQuantity"));
        Quantity(target.GetProperty("standardQuantity"));
        _ = String(target, "site");
        if (String(target, "supplyMode") is not ("STOCK" or "NEW_MANUFACTURE" or "PURCHASE" or "REWORK"))
            throw Error("SCHEMA_INVALID", 400);
        RequireFields(target.GetProperty("requiredDate"), "kind", "date", "instantUtc", "node", "timeZone", "calendar");
        RequireFields(target.GetProperty("technical"), "item", "revision", "baseline", "configuration", "applicability");
        _ = ArrayLength(target, "purposeEvidence");
    }

    private static void RequireControl(JsonElement control, OrderSourceScope scope, string lineage)
    {
        RequireFields(control, "controlId", "controlVersion", "owner", "reasonId", "scope",
            "effectiveAt", "disposition", "authority", "predecessor");
        _ = Revision(control.GetProperty("controlVersion"));
        if (String(control, "disposition") is not ("BLOCK" or "RELEASE_OWN_REASON" or "TERMINATE"))
            throw Error("SCHEMA_INVALID", 400);
        var target = control.GetProperty("scope");
        RequireFields(target, "order", "releaseLineageId", "actionSet");
        RequireScope(target.GetProperty("order"), scope);
        if (String(target, "releaseLineageId") != lineage) throw Error("SALES_SOURCE_SCOPE_CONFLICT", 409);
        var actions = target.GetProperty("actionSet").EnumerateArray().Select(a => a.GetString()).ToArray();
        if (actions.Length is < 1 or > 5 || actions.Distinct(StringComparer.Ordinal).Count() != actions.Length
            || actions.Any(a => a is not ("PLAN" or "CONVERT" or "RELEASE" or "MANUFACTURE" or "SHIP")))
            throw Error("SCHEMA_INVALID", 400);
    }

    private static void Quantity(JsonElement quantity)
    {
        RequireFields(quantity, "value", "uom");
        var value = String(quantity, "value");
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,17})(\.[0-9]{1,6})?\z")
            || decimal.Parse(value, CultureInfo.InvariantCulture) <= 0)
            throw Error("SCHEMA_INVALID", 400);
        RequireFields(quantity.GetProperty("uom"), "owner", "contractId", "contractVersion",
            "evidenceId", "evidenceVersion", "contentDigest");
    }

    private static void RequireFields(JsonElement value, params string[] fields)
    {
        if (!value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(fields))
            throw Error("SCHEMA_INVALID", 400);
    }

    private static int ArrayLength(JsonElement root, string field)
    {
        var length = root.GetProperty(field).GetArrayLength();
        return length <= 200 ? length : throw Error("SCHEMA_INVALID", 400);
    }

    private static string String(JsonElement root, string field) => Key(root.GetProperty(field).GetString());

    private static string? OptionalKey(JsonElement root, string field) =>
        root.GetProperty(field).ValueKind == JsonValueKind.Null ? null : String(root, field);

    private static long? NullableRevision(JsonElement root, string field) =>
        root.GetProperty(field).ValueKind == JsonValueKind.Null ? null : Revision(root.GetProperty(field));

    private static long Revision(JsonElement value)
    {
        var text = value.GetString();
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) && revision > 0
            && text == revision.ToString(CultureInfo.InvariantCulture) ? revision : throw Error("SCHEMA_INVALID", 400);
    }

    private static T ReadJson<T>(byte[] bytes, Func<JsonElement, T> read)
    {
        try
        {
            using var document = Cp6NativeJson.Parse(bytes);
            return read(document.RootElement);
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or KeyNotFoundException
            or CommercialProtocolException || e is InvalidOperationException && e is not OrderPriceCorrectionException)
        {
            throw Error("SCHEMA_INVALID", 400);
        }
    }

    private static byte[] Copy(ReadOnlyMemory<byte> value) =>
        value.Length is > 0 and <= MaxBytes ? value.ToArray() : throw Error("SCHEMA_INVALID", 400);

    private static string Digest(byte[] original) => MainOwnerOperationJournalV1.Digest(original);
}
