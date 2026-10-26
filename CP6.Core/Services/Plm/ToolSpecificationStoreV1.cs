using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal sealed class ToolSpecificationStoreV1(CP6Context db)
{
    internal const string Owner = "Main.Plm.ToolSpecification.v1";
    private IDbContextTransaction? transaction;
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_tool_specification_v1.heads,cp6_tool_specification_v1.versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_tool_specification_v1.heads,cp6_tool_specification_v1.versions IN SHARE MODE", ct);
        transaction = db.Database.CurrentTransaction;
    }
    private void RequireFence()
    {
        if (transaction == null || !ReferenceEquals(transaction, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal static void Validate(ToolRequirementsV1? requirements, string? reason)
    {
        if (requirements == null) throw Error("TOOL_REQUIREMENTS_REQUIRED", 400);
        Reference(requirements.ProductRef);
        if (!ValidText(requirements.ProductApplicability, 4000, true) || !ValidText(requirements.ProcessRequirements, 4000, true)
            || !ValidText(requirements.ConfigurationApplicability, 4000, true) || !ValidText(requirements.TechnicalRequirements, 8000, true)
            || !ValidText(requirements.DocumentRequirements, 4000, true) || !ValidText(reason, 500, true)) throw Error("TOOL_REQUIREMENTS_REQUIRED", 400);
    }
    private Task<long?> Head(Guid id, CancellationToken ct) => db.Database.SqlQuery<long?>($"""
        SELECT revision AS "Value" FROM cp6_tool_specification_v1.heads WHERE tenant_id={db.CurrentTenantId} AND tool_id={id}
        """).SingleOrDefaultAsync(ct);
    private sealed class Original { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    internal async Task<ToolSpecificationV1?> Read(Guid id, long? version, CancellationToken ct)
    {
        RequireFence(); var head = await Head(id, ct);
        if (head == null && version == null) return null;
        if (head == null || version > head) throw Error("TOOL_SPECIFICATION_VERSION_NOT_FOUND", 404);
        var selected = version ?? head!.Value;
        var raw = await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_tool_specification_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND tool_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct) ?? throw Error("TOOL_SPECIFICATION_VERSION_NOT_FOUND", 404);
        try
        {
            var stored = JsonSerializer.Deserialize<ToolSpecificationStoredV1>(raw.Body, EngineeringProductStoreV1.Json);
            if (stored == null || EngineeringProductStoreV1.Digest(raw.Body) != raw.Digest || stored.TenantId != db.CurrentTenantId
                || stored.Id != id || stored.Version != selected.ToString(CultureInfo.InvariantCulture) || stored.ActorId == Guid.Empty
                || stored.Source.Owner != PlateMoldSourceStoreV1.Owner || stored.Source.Id != id || !ValidDigest(stored.Source.Digest)
                || string.IsNullOrWhiteSpace(stored.NativeRowVersion)) throw Error("TOOL_SPECIFICATION_ORIGINAL_UNAVAILABLE", 503);
            Revision(stored.Source.Version); Validate(stored.Requirements, stored.Reason);
            return new(new(Owner, id, stored.Version, raw.Digest), stored.Source, stored.NativeRowVersion,
                stored.Requirements, stored.Reason, stored.ActorId, stored.RecordedAt, Encoding.UTF8.GetString(raw.Body));
        }
        catch (JsonException error) { throw new OrderPriceCorrectionException("TOOL_SPECIFICATION_ORIGINAL_UNAVAILABLE", 503, error); }
        catch (OrderPriceCorrectionException error) when (error.Status != 503) { throw new OrderPriceCorrectionException("TOOL_SPECIFICATION_ORIGINAL_UNAVAILABLE", 503, error); }
    }
    internal async Task<PlateMoldTechnicalControlV1> Control(PlateMoldSourceRefV1 source, CancellationToken ct)
    {
        var current = await Read(source.Id, null, ct);
        return current == null ? new("TECHNICAL_CONTROL_UNPROVEN", source.Owner, source.Id, source.Version, source.Digest)
            : new("TECHNICAL_SPECIFICATION_DRAFT", Owner, source.Id, current.Reference.Version, current.Reference.Digest);
    }
    // Legacy writers consume only the opaque control version, never professional approval authority or draft contents.
    internal static async Task<PlateMoldTechnicalControlV1> LegacyControl(CP6Context db, PlateMoldSourceRefV1 source, CancellationToken ct)
    { var store = new ToolSpecificationStoreV1(db); await store.Fence(false, ct); return await store.Control(source, ct); }
    internal async Task<ToolSpecificationV1> Append(ToolSpecificationCommandV1 command, Guid actor, long? expected, CancellationToken ct)
    {
        RequireFence(); var id = command.Source.Id; var head = await Head(id, ct);
        if (head != expected) throw Error("TECHNICAL_CONTROL_CHANGED", 412);
        if (head == long.MaxValue) throw Error("TOOL_SPECIFICATION_VERSION_EXHAUSTED", 503);
        var next = (head ?? 0) + 1;
        if (head == null) await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_specification_v1.heads(tenant_id,tool_id,revision) VALUES({db.CurrentTenantId},{id},{next})
            """, ct);
        else if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_tool_specification_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND tool_id={id} AND revision={head.Value}
            """, ct) != 1) throw Error("TECHNICAL_CONTROL_CHANGED", 412);
        var body = EngineeringProductStoreV1.Encode(new ToolSpecificationStoredV1(db.CurrentTenantId, id, next.ToString(CultureInfo.InvariantCulture),
            actor, command.Source, command.ExpectedNativeRowVersion, command.Requirements, command.Reason, DateTimeOffset.UtcNow));
        var digest = EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_specification_v1.versions(tenant_id,tool_id,revision,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{id},{next},{body},{digest},{actor})
            """, ct);
        return (await Read(id, next, ct))!;
    }
}
