using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringProductStoreV1;

namespace CP6.Core.Services.Plm;
internal sealed class ToolAssociationStoreV1(CP6Context db)
{
    internal const string Owner = "Main.Plm.ToolInstanceAssociation.v1";
    internal const string EngineeringOwner = "Main.Plm.ToolInstanceConformance.v1", PhysicalOwner = "Main.Wms.ToolAssociationAdoption.v1";
    internal const string EngineeringRole = "ENG_SPEC_CONFIRMATION", PhysicalRole = "PHYSICAL_INSTANCE_ASSOCIATION";
    private IDbContextTransaction? transaction;
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_association_v1.proposals,cp6_tool_association_v1.heads,cp6_tool_association_v1.versions,cp6_tool_association_v1.decisions "
            + (write ? "IN SHARE ROW EXCLUSIVE MODE" : "IN SHARE MODE"), ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=8 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid IN
              ('cp6_tool_association_v1.proposals'::regclass,'cp6_tool_association_v1.heads'::regclass,
               'cp6_tool_association_v1.versions'::regclass,'cp6_tool_association_v1.decisions'::regclass)
              AND tgname LIKE 'tool_association_%' AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("TOOL_ASSOCIATION_STORE_UNAVAILABLE", 503);
        transaction = db.Database.CurrentTransaction;
    }
    private void Require() { if (transaction == null || !ReferenceEquals(transaction, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503); }
    internal sealed class Original { public byte[] Body { get; init; } = []; public string Hash { get; init; } = ""; }
    internal async Task<(ToolAssociationSubjectV1 Subject, string Digest)> Subject(Guid id, CancellationToken ct)
    {
        Require(); var row = await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Hash" FROM cp6_tool_association_v1.proposals
            WHERE tenant_id={db.CurrentTenantId} AND proposal_id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("TOOL_ASSOCIATION_NOT_FOUND", 404);
        var body = Decode<ToolAssociationSubjectV1>(row);
        if (body.TenantId != db.CurrentTenantId || body.ProposalId != id || body.ProposalRevision != "1") throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        return (body, row.Hash);
    }
    private static T Decode<T>(Original row)
    {
        if (Digest(row.Body) != row.Hash) throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        try { return JsonSerializer.Deserialize<T>(row.Body, EngineeringProductStoreV1.Json) ?? throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503); }
        catch (JsonException) { throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503); }
    }
    internal async Task<ToolAssociationProjectionV1> Projection(Guid id, long? version, CancellationToken ct)
    {
        Require(); var row = await db.Database.SqlQuery<Original>($"""
            SELECT v.body_bytes AS "Body",v.body_digest AS "Hash" FROM cp6_tool_association_v1.versions v
            JOIN cp6_tool_association_v1.heads h USING(tenant_id,proposal_id)
            WHERE v.tenant_id={db.CurrentTenantId} AND v.proposal_id={id} AND v.revision=COALESCE({version},h.revision)
            """).SingleOrDefaultAsync(ct) ?? throw Error("TOOL_ASSOCIATION_VERSION_NOT_FOUND", 404);
        var p = Decode<ToolAssociationProjectionV1>(row);
        if (p.TenantId != db.CurrentTenantId || p.ProposalId != id || version != null && Revision(p.Revision) != version)
            throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        return p;
    }
    internal async Task<ToolAssociationDecisionStoredV1> Decision(ToolAssociationDecisionRefV1 reference, CancellationToken ct)
    {
        Require(); var row = await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Hash" FROM cp6_tool_association_v1.decisions
            WHERE tenant_id={db.CurrentTenantId} AND receipt_id={reference.Id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        var d = Decode<ToolAssociationDecisionStoredV1>(row);
        if (d.TenantId != db.CurrentTenantId || d.Id != reference.Id || d.Owner != reference.Owner || reference.Version != "1" || reference.Digest != row.Hash)
            throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        return d;
    }
    internal async Task<string> Add(ToolAssociationSubjectV1 subject, CancellationToken ct)
    {
        Require(); var bytes = Encode(subject); var digest = Digest(bytes);
        if (await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_tool_association_v1.proposals WHERE tenant_id={db.CurrentTenantId} AND proposal_id={subject.ProposalId}) AS \"Value\"").SingleAsync(ct))
            throw Error("TOOL_ASSOCIATION_ID_CONFLICT", 409);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_association_v1.proposals(tenant_id,proposal_id,physical_id,technical_id,body_bytes,body_digest)
            VALUES({db.CurrentTenantId},{subject.ProposalId},{subject.Physical.Id},{subject.Technical.Id},{bytes},{digest})
            """, ct); return digest;
    }
    internal async Task<ToolAssociationDecisionRefV1> AddDecision(ToolAssociationDecisionStoredV1 d, CancellationToken ct)
    {
        Require(); var bytes = Encode(d); var digest = Digest(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_association_v1.decisions(tenant_id,proposal_id,receipt_id,role,owner,round_id,subject_digest,body_bytes,body_digest)
            VALUES({db.CurrentTenantId},{d.ProposalId},{d.Id},{d.Role},{d.Owner},{d.RoundId},{d.SubjectDigest},{bytes},{digest})
            """, ct); return new(d.Owner, d.Id, "1", digest);
    }
    internal async Task<string> Append(ToolAssociationProjectionV1 p, Guid physical, CancellationToken ct)
    {
        Require(); var revision = Revision(p.Revision); var bytes = Encode(p); var digest = Digest(bytes);
        if (revision == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_tool_association_v1.heads(tenant_id,proposal_id,physical_id,revision,state)
                VALUES({db.CurrentTenantId},{p.ProposalId},{physical},{revision},{p.State})
                """, ct);
        else if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_tool_association_v1.heads SET revision={revision},state={p.State}
            WHERE tenant_id={db.CurrentTenantId} AND proposal_id={p.ProposalId} AND revision={revision - 1}
            """, ct) != 1) throw Error("TOOL_ASSOCIATION_CHANGED", 412);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_association_v1.versions(tenant_id,proposal_id,revision,body_bytes,body_digest)
            VALUES({db.CurrentTenantId},{p.ProposalId},{revision},{bytes},{digest})
            """, ct); return digest;
    }
    internal Task<Guid[]> Ids(Guid physical, int page, CancellationToken ct)
    {
        Require(); return db.Database.SqlQuery<Guid>($"""
            SELECT proposal_id AS "Value" FROM cp6_tool_association_v1.proposals WHERE tenant_id={db.CurrentTenantId} AND physical_id={physical}
            ORDER BY proposal_id LIMIT 20 OFFSET {(page - 1) * 20}
            """).ToArrayAsync(ct);
    }
    internal Task<bool> OtherConfirmed(Guid physical, Guid id, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_tool_association_v1.heads WHERE tenant_id={db.CurrentTenantId}
          AND physical_id={physical} AND proposal_id<>{id} AND state='CONFIRMED') AS "Value"
        """).SingleAsync(ct);
    internal static string Next(string version) { var v = Revision(version); if (v == long.MaxValue) throw Error("TOOL_ASSOCIATION_VERSION_EXHAUSTED", 409); return (v + 1).ToString(CultureInfo.InvariantCulture); }
}
