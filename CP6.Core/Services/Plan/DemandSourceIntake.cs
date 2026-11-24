using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Platform.Messaging.NativeContracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Plan;

internal sealed class DemandSourceIntakeException(string code, int status) : InvalidOperationException(code)
{
    internal int Status { get; } = status;
}

// Main-internal ORD-DEM-SOURCE/1 receiver. The authenticated tenant and the
// persisted Sales owner operation supply authority; a caller cannot name a producer.
internal sealed partial class DemandSourceIntake(CP6Context db, TimeProvider? clock = null)
{
    internal const string Owner = "Main.Planning";
    private const string Producer = "Main.Sales";
    private const string Contract = OrderSourceParticipant.SourceContract;
    private const int MaxControls = 200;
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    internal sealed record SourceResultQuery(string OperationKey, string ReleaseLineageId,
        string SourceVersion, string SourceDigest);

    internal sealed record OwnerRef(string Owner, string ContractId, string ContractVersion,
        string EvidenceId, string EvidenceVersion, string ContentDigest);

    internal sealed record SourceResult(Guid ResultId, string ResultVersion, string OperationKey, string SourceDigest,
        string SourceVersion, string Outcome, Guid? DemandId, string? DemandVersion, JsonElement? AcceptedTarget,
        OwnerRef[] ControlRefs, string[] ReasonCodes, string CommittedAt);

    internal sealed record FactWatermark(string Owner, string ScopeDigest, string? Version,
        bool Complete, string ObservedAt, string? UnknownReason);

    internal sealed record DemandCurrent(string ReleaseLineageId, string? AcceptedSourceVersion, Guid? DemandId,
        string? DemandVersion, JsonElement? Target, JsonElement[] Controls, string Effectivity,
        OwnerRef[] Successors, FactWatermark Watermark);

    internal sealed record SourceResultRead(string Knowledge, JsonElement? Historical, DemandCurrent Current,
        bool NoEffectProven, OwnerRef? Finality);

    private sealed record Input(SourceResultQuery Query, long Version, string Kind, JsonElement Target, JsonElement[] Controls);
    private sealed record Control(JsonElement Fact, DateTimeOffset EffectiveAt, string Disposition, bool Plans);

    private DateTimeOffset Now() => (clock ?? TimeProvider.System).GetUtcNow();
    private static string Revision(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DemandSourceIntakeException Invalid() => new("SCHEMA_INVALID", 400);
    private static DemandSourceIntakeException Conflict() => new("SOURCE_ORIGINAL_CONFLICT", 409);
    private static DemandSourceIntakeException Unproven() => new("PLANNING_SOURCE_UNPROVEN", 503);

    internal async Task<byte[]> ReceiveAsync(ReadOnlyMemory<byte> sourceBody, CancellationToken ct = default)
    {
        if (sourceBody.Length is 0 or > 2 * 1024 * 1024) throw Invalid();
        var body = sourceBody.ToArray();
        var input = Parse(body);
        var query = input.Query;

        await using var tx = await Begin(query, ct);
        var prior = await FindInbox(query.OperationKey, ct);
        if (prior is not null) return (await ReadOriginal(prior, query, body, ct)).Result;
        if (await ObservedVersion(query.ReleaseLineageId, input.Version, ct)) throw Conflict();

        var sales = new OrderSourceParticipant(db, clock);
        var original = await sales.ReadOperation(Producer, query.OperationKey, ct) ?? throw Unproven();
        var source = original.Receipt;
        if (!original.Body.AsSpan().SequenceEqual(body) || source.Producer != Producer ||
            source.OperationKey != query.OperationKey || source.LineageId != query.ReleaseLineageId ||
            source.SourceVersion != input.Version || source.SourceDigest != query.SourceDigest)
            throw Conflict();
        if (original.ActorId == Guid.Empty || source.State != "LOCAL_REQUEST") throw Unproven();

        var lineage = await FindLineage(source.LineageId, ct);
        if (lineage is not null && !lineage.Matches(source.Scope)) throw Conflict();

        // Both operations take this same lineage slot. The Sales owner check takes
        // its native locks inside this transaction before any Planning write.
        var publication = await sales.CheckCurrentPublication(tx.GetDbTransaction(), source, ct);
        var now = Now();
        var controlFacts = Array.Empty<Control>();
        var reasons = Array.Empty<string>();
        Guid? demand = null;
        string outcome;
        if (source.Handoff == "H32" && input.Kind == "CONTROL_SOURCE")
        {
            if (publication.State != "CURRENT" || input.Controls.Length is 0 or > MaxControls) throw Unproven();
            controlFacts = input.Controls.Select(ParseControl).ToArray();
            if (controlFacts.Any(fact => fact.Disposition == "RELEASE_OWN_REASON")) throw Unproven();
            outcome = "SOURCE_CONTROL_APPLIED";
        }
        else if (source.Handoff == "H10" && input.Kind == "INITIAL_RELEASE" && input.Version == 1)
        {
            var controls = await ReadControls(source.LineageId, ct);
            var stopped = ControlsForPlanning(controls, source.Scope.OrderVersion, now);
            if (stopped.Length != 0)
            {
                outcome = "BUSINESS_REJECTED";
                reasons = ["SOURCE_CONTROL_BLOCKED"];
            }
            else
            {
                if (publication.State != "CURRENT" || source.ReleaseId is null || source.ReleaseLineId is null)
                    throw Unproven();
                if (await FindDemand(source.LineageId, ct) is not null) throw Conflict();
                outcome = "BUSINESS_ACCEPTED";
                demand = Guid.NewGuid();
            }
        }
        else throw Unproven();

        var receipt = new SourceResult(Guid.NewGuid(), "1", source.OperationKey, source.SourceDigest,
            query.SourceVersion, outcome, demand, demand is null ? null : "1", demand is null ? null : input.Target,
            controlFacts.Select(fact => ControlReference(fact.Fact)).ToArray(), reasons, Utc(now));
        var result = JsonSerializer.SerializeToElement(receipt, WireJson);

        if (lineage is null) await AddLineage(source, ct);
        // Reuse the Main owner original/audit transaction. The inbox keeps the
        // opaque transport key and resolves it to this stable journal identity.
        var journal = new MainOwnerOperationJournalV1(db);
        await journal.Store(Owner, Contract, receipt.ResultId, original.ActorId, body, result,
            demand is Guid id ? [id] : [],
            new { producer = Producer, source.OperationKey, source.LineageId, source.SourceVersion,
                source.Scope, source.SourceDigest, source.BodySha256, source.EnvelopeSha256,
                receipt.Outcome, receipt.ReasonCodes, authority = "MAIN_SALES_PERSISTED_ORIGINAL" }, ct);
        await AddInbox(source, original, receipt.ResultId, ct);
        if (demand is Guid demandId) await AddDemand(source, demandId, input.Target, now, ct);
        await AddControls(source, controlFacts, ct);
        await RequireActiveTenant(ct);
        await tx.CommitAsync(ct);
        return MainOwnerOperationJournalV1.Encode(result);
    }

    internal async Task<SourceResultRead> ReadResultAsync(SourceResultQuery query, CancellationToken ct = default)
    {
        _ = Validate(query);
        await using var tx = await Begin(query, ct);
        var inbox = await FindInbox(query.OperationKey, ct);
        var now = Now();
        if (inbox is null)
            return new("NOT_OBSERVED", null, Unknown(query.ReleaseLineageId, now), false, null);

        var original = await ReadOriginal(inbox, query, null, ct);
        var current = await ReadCurrent(query.ReleaseLineageId, tx, now, ct);
        // Keep the JSON backed by the original result bytes. Current effectivity is a separate read.
        return new("FOUND", JsonSerializer.Deserialize<JsonElement>(original.Result), current, false, null);
    }

    private async Task<IDbContextTransaction> Begin(SourceResultQuery query, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty ||
            db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw Unproven();
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            await RequireActiveTenant(ct);

            await Lock("operation", query.OperationKey, ct);
            await Lock("lineage", query.ReleaseLineageId, ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private async Task RequireActiveTenant(CancellationToken ct)
    {
        var active = await db.Database.SqlQuery<bool>($"""
            SELECT "Enable" AND ("ExpireDate" IS NULL OR "ExpireDate">clock_timestamp()) AS "Value"
            FROM public."Sys_Tenants" WHERE "Id"={db.CurrentTenantId}
            """).SingleOrDefaultAsync(ct);
        if (!active) throw new DemandSourceIntakeException("TENANT_AUTHORITY_UNAVAILABLE", 403);
    }

    private static Input Parse(byte[] body)
    {
        try
        {
            using var document = Cp6NativeJson.Parse(body);
            var root = document.RootElement;
            var query = new SourceResultQuery(root.GetProperty("operationKey").GetString()!,
                root.GetProperty("releaseLineageId").GetString()!, root.GetProperty("sourceVersion").GetString()!,
                root.GetProperty("sourceDigest").GetString()!);
            var version = Validate(query);
            if (root.GetProperty("schemaVersion").GetString() != Contract ||
                CommercialCanonicalV1.Digest("ORD-DEM-SOURCE-1", root, omitRoot: ["sourceDigest"]) != query.SourceDigest)
                throw Invalid();
            return new(query, version, root.GetProperty("kind").GetString()!, root.GetProperty("target").Clone(),
                root.GetProperty("controls").EnumerateArray().Select(item => item.Clone()).ToArray());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or FormatException or ArgumentException ||
            e is InvalidOperationException and not DemandSourceIntakeException)
        {
            throw Invalid();
        }
    }

    private static long Validate(SourceResultQuery query)
    {
        try
        {
            _ = CommercialWireV1.RequireKey(query.OperationKey, 256);
            _ = CommercialWireV1.RequireKey(query.ReleaseLineageId, 256);
            if (!CommercialWireV1.IsDigest(query.SourceDigest)) throw Invalid();
            return CommercialWireV1.Revision(query.SourceVersion);
        }
        catch (CommercialProtocolException) { throw Invalid(); }
    }

    private static Control ParseControl(JsonElement fact)
    {
        // The Sales owner verifies the exact scope, lineage, control and authority shape.
        var raw = fact.GetProperty("effectiveAt").GetString();
        if (!DateTimeOffset.TryParseExact(raw, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var effectiveAt))
            throw Unproven();
        var plans = fact.GetProperty("scope").GetProperty("actionSet").EnumerateArray().Any(item => item.GetString() == "PLAN");
        return new(fact, effectiveAt, fact.GetProperty("disposition").GetString()!, plans);
    }

    private static OwnerRef ControlReference(JsonElement fact) =>
        new(Owner, Contract, "1", fact.GetProperty("controlId").GetString()!,
            fact.GetProperty("controlVersion").GetString()!, MainOwnerOperationJournalV1.Digest(JsonBytes(fact)));

    private static byte[] JsonBytes(JsonElement value) => Encoding.UTF8.GetBytes(value.GetRawText());

    private static ScopeControl[] ControlsForPlanning(ScopeControl[] controls, string orderVersion, DateTimeOffset now) =>
        controls.Where(control => control.OrderVersion == orderVersion && control.Plans && control.EffectiveAtTicks <= now.UtcTicks).ToArray();

    private FactWatermark Watermark(string lineage, long? version, DateTimeOffset now, string? unknown) =>
        new(Owner, CommercialCanonicalV1.Digest("PLAN-DEMAND-SOURCE-SCOPE-1", new { tenantId = db.CurrentTenantId, Producer, lineage }),
            version is long v ? Revision(v) : null, unknown is null, Utc(now), unknown);

    private DemandCurrent Unknown(string lineage, DateTimeOffset now) =>
        new(lineage, null, null, null, null, [], "UNKNOWN", [], Watermark(lineage, null, now, "RESULT_NOT_OBSERVED"));
}
