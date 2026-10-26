using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal sealed class ConfigurationRuleHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public Guid ProductId { get; init; }
    public long Revision { get; init; }
    public long? ActiveVersion { get; init; }
}
internal sealed class ConfigurationRuleStoreV1(CP6Context db)
{
    private IDbContextTransaction? fenced;
    private void Transaction(bool requireFence = true)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || db.Database.CurrentTransaction == null
            || requireFence && !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task Fence(bool write, CancellationToken ct)
    {
        Transaction(false);
        await db.Database.ExecuteSqlRawAsync(write ? "LOCK TABLE cp6_configuration_rule_v1.heads,cp6_configuration_rule_v1.versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_configuration_rule_v1.heads,cp6_configuration_rule_v1.versions IN SHARE MODE", ct);
        fenced = db.Database.CurrentTransaction;
    }
    internal static IQueryable<ConfigurationRuleHeadV1> Heads(CP6Context context) => context.Database.SqlQuery<ConfigurationRuleHeadV1>($"""
        SELECT rule_id AS "Id",creator_id AS "CreatorId",product_id AS "ProductId",revision AS "Revision",active_version AS "ActiveVersion"
        FROM cp6_configuration_rule_v1.heads WHERE tenant_id={context.CurrentTenantId}
        """);
    private sealed class Original { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    private async Task<(Original Raw, ConfigurationRuleStoredV1 Value)> OriginalAt(ConfigurationRuleHeadV1 head, long version, CancellationToken ct)
    {
        Transaction();
        var raw = await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_configuration_rule_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND rule_id={head.Id} AND revision={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("CONFIGURATION_RULE_VERSION_NOT_FOUND", 404);
        if (EngineeringProductStoreV1.Digest(raw.Body) != raw.Digest) throw Error("CONFIGURATION_RULE_ORIGINAL_UNAVAILABLE", 503);
        ConfigurationRuleStoredV1? value;
        try { value = JsonSerializer.Deserialize<ConfigurationRuleStoredV1>(raw.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("CONFIGURATION_RULE_ORIGINAL_UNAVAILABLE", 503); }
        if (value == null || value.Body == null || value.TenantId != db.CurrentTenantId || value.Id != head.Id || value.CreatorId != head.CreatorId
            || value.Body.ProductRef.Id != head.ProductId || value.Etag == Guid.Empty || value.Version != version.ToString(CultureInfo.InvariantCulture))
            throw Error("CONFIGURATION_RULE_ORIGINAL_UNAVAILABLE", 503);
        return (raw, value);
    }
    internal async Task<ConfigurationRuleSnapshotV1> Read(Guid id, long? version, CancellationToken ct)
    {
        Transaction(); var head = await Heads(db).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
        var selected = version ?? head.Revision; var (raw, v) = await OriginalAt(head, selected, ct); EngineeringSourceRefV1? active = null;
        if (head.ActiveVersion is long a)
        {
            var (original, approved) = await OriginalAt(head, a, ct);
            if (approved.State != "APPROVED") throw Error("CONFIGURATION_RULE_ORIGINAL_UNAVAILABLE", 503);
            active = new(ConfigurationRuleValuesV1.Owner, id, a.ToString(CultureInfo.InvariantCulture), original.Digest);
        }
        return new(db.CurrentTenantId, new(ConfigurationRuleValuesV1.Owner, id, v.Version, raw.Digest), v.Etag, selected == head.Revision,
            selected == head.ActiveVersion, active, v.State, v.Body, v.Action, v.Reason, v.ParentVersion, v.ActorId, v.RecordedAt, Encoding.UTF8.GetString(raw.Body));
    }
    internal async Task<ConfigurationRuleSnapshotV1> Append(ConfigurationRuleCommandV1 command, Guid actor, ConfigurationRuleBodyV1 body, string state, CancellationToken ct)
    {
        Transaction(); var id = command.RuleId; var head = await Heads(db).SingleOrDefaultAsync(x => x.Id == id, ct); long next; Guid creator;
        long? active = head?.ActiveVersion;
        if (command.Action == "CREATE")
        {
            if (head != null || await Heads(db).AnyAsync(x => x.ProductId == body.ProductRef.Id, ct)) throw Error("CONFIGURATION_RULE_IDENTITY_CONFLICT", 409);
            next = 1; creator = actor;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_configuration_rule_v1.heads(tenant_id,rule_id,creator_id,product_id,revision)
                VALUES({db.CurrentTenantId},{id},{actor},{body.ProductRef.Id},1)
                """, ct);
        }
        else
        {
            if (head == null || head.Revision != Revision(command.ExpectedVersion)) throw Error("CONFIGURATION_RULE_CHANGED", 412);
            var current = await Read(id, null, ct);
            if (current.Etag != command.ExpectedEtag || current.ActiveRef != command.ExpectedActiveRef) throw Error("CONFIGURATION_RULE_CHANGED", 412);
            if (body.ProductRef.Id != head.ProductId) throw Error("CONFIGURATION_RULE_PRODUCT_IMMUTABLE", 409);
            if (head.Revision == long.MaxValue) throw Error("CONFIGURATION_RULE_VERSION_EXHAUSTED", 503);
            next = head.Revision + 1; creator = head.CreatorId;
            active = command.Action == "APPROVE" ? next : command.Action == "WITHDRAW" ? null : active;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_configuration_rule_v1.heads SET revision={next},active_version={active}
                WHERE tenant_id={db.CurrentTenantId} AND rule_id={id} AND revision={head.Revision}
                """, ct);
        }
        var stored = new ConfigurationRuleStoredV1(db.CurrentTenantId, id, next.ToString(CultureInfo.InvariantCulture), creator, Guid.NewGuid(), state,
            body, command.Action, command.Reason, command.ExpectedVersion, command.ExpectedActiveRef, actor, DateTimeOffset.UtcNow);
        var bytes = EngineeringProductStoreV1.Encode(stored); var digest = EngineeringProductStoreV1.Digest(bytes);
        if (bytes.Length > 8388608) throw Error("CONFIGURATION_RULE_SIZE_LIMIT", 400);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_configuration_rule_v1.versions(tenant_id,rule_id,revision,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{id},{next},{bytes},{digest},{actor})
            """, ct);
        return await Read(id, next, ct);
    }
}
