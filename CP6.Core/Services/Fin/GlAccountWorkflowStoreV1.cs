using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

internal sealed record GlBookV1(string Scheme, string BasisKey, string[] Roles, string[] Currencies,
    bool ProfessionalAvailable, DateOnly PolicyFrom, DateOnly? PolicyToExclusive, Guid PolicyRevision, long ScopeRevision);
internal sealed record GlRootV1(Guid Id, string Code, string ControlState, long ControlRevision, long? TimelineRevision,
    DateOnly? RetiredOn);
internal sealed record GlTimelineV1(long Revision, Guid ReleaseId, Guid VersionId, Guid DecisionId,
    GlTimelineSegmentV1[] Segments)
{
    internal GlTimelineReferenceV1 Reference(Guid rootId) => new(rootId, Revision, ReleaseId,
        GlAccountWorkflowStoreV1.Digest(GlAccountWorkflowStoreV1.Encode(new { rootId, Revision, ReleaseId, VersionId, DecisionId, Segments })));
}
internal sealed record GlStoredPreviewV1(string Kind, string Snapshot, bool IsCurrent, bool IsConsumed);

/// <summary>账套事务中的科目原件和投影持久化。</summary>
internal sealed class GlAccountWorkflowStoreV1(CP6Context db)
{
    private const long MaxRevision = 9007199254740991;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private Guid Tenant => db.CurrentTenantId;

    internal static OrderPriceCorrectionException Error(string code, int status = 409) => new(code, status);
    internal static string Encode<T>(T value) => JsonSerializer.Serialize(value, Wire);
    internal static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Wire)
        ?? throw Error("GL_ORIGINAL_UNPROVEN", 503);
    internal static string Digest(string body) => MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(body));
    internal static long Next(long revision) => revision < MaxRevision ? revision + 1 : throw Error("REVISION_EXHAUSTED", 409);

    internal Task<GlBookV1?> Book(Guid ledger, CancellationToken ct) => db.Database.SqlQuery<GlBookV1>($"""
        SELECT scheme AS "Scheme",basis_key AS "BasisKey",roles AS "Roles",currencies AS "Currencies",
            professional_available AS "ProfessionalAvailable",policy_from AS "PolicyFrom",policy_to_exclusive AS "PolicyToExclusive",
            policy_revision AS "PolicyRevision",scope_revision AS "ScopeRevision"
        FROM cp6_fin_gl_v1.books WHERE tenant_id={Tenant} AND ledger_id={ledger}
        """).SingleOrDefaultAsync(ct);

    internal async Task<long> Advance(Guid ledger, long revision, CancellationToken ct)
    {
        var next = Next(revision);
        Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_gl_v1.books SET scope_revision={next}
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND scope_revision={revision}
            """, ct));
        return next;
    }

    internal Task<GlQualificationV1?> Qualification(Guid ledger, Guid subject, string action, CancellationToken ct)
        => db.Database.SqlQuery<GlQualificationV1>($"""
            SELECT q.qualification_id AS "Reference",d.issuer_id AS "Issuer"
            FROM cp6_fin_mfg_v1.qualifications q JOIN cp6_fin_mfg_v1.members m USING(tenant_id,ledger_id,subject_id)
            JOIN cp6_fin_mfg_v1.qualification_decisions d
              ON (d.tenant_id,d.ledger_id,d.subject_id,d.action,d.id)=
                 (q.tenant_id,q.ledger_id,q.subject_id,q.action,q.qualification_id)
            WHERE q.tenant_id={Tenant} AND q.ledger_id={ledger} AND q.subject_id={subject} AND q.action={"gl-account-" + action}
                AND q.enabled AND d.enabled AND m.enabled AND q.expires_at>clock_timestamp()
            """).SingleOrDefaultAsync(ct);

    internal sealed record RootListRow(Guid Id, string Code, string ControlState, string ProposalState, string Payload, string Digest);
    internal Task<RootListRow[]> RootList(Guid ledger, CancellationToken ct) => db.Database.SqlQuery<RootListRow>($"""
        SELECT r.id AS "Id",r.code AS "Code",r.control_state AS "ControlState",p.state AS "ProposalState",
            v.payload AS "Payload",v.digest AS "Digest"
        FROM cp6_fin_gl_v1.roots r
        JOIN LATERAL(
            SELECT p.state,p.version_id FROM cp6_fin_gl_v1.proposals p JOIN cp6_fin_gl_v1.versions seq
              ON (seq.tenant_id,seq.ledger_id,seq.root_id,seq.id)=(p.tenant_id,p.ledger_id,p.root_id,p.version_id)
            WHERE (p.tenant_id,p.ledger_id,p.root_id)=(r.tenant_id,r.ledger_id,r.id)
            ORDER BY (p.state IN ('DRAFT','SUBMITTED','APPROVED','REJECTED')) DESC,seq.version_no DESC LIMIT 1
        ) p ON true
        JOIN cp6_fin_gl_v1.versions v ON (v.tenant_id,v.ledger_id,v.root_id,v.id)=(r.tenant_id,r.ledger_id,r.id,p.version_id)
        WHERE (r.tenant_id,r.ledger_id)=({Tenant},{ledger})
        ORDER BY r.code
        """).ToArrayAsync(ct);

    internal sealed record RevisionRow(Guid ProposalId, long Revision, string State);
    internal Task<RevisionRow[]> Revisions(Guid ledger, Guid root, CancellationToken ct)
        => db.Database.SqlQuery<RevisionRow>($"""
            SELECT proposal_id AS "ProposalId",revision AS "Revision",snapshot::jsonb->>'state' AS "State"
            FROM cp6_fin_gl_v1.proposal_history
            WHERE (tenant_id,ledger_id,root_id)=({Tenant},{ledger},{root})
            ORDER BY recorded_at DESC,proposal_id DESC,revision DESC LIMIT 200
            """).ToArrayAsync(ct);

    internal IQueryable<GlRootV1> Roots(Guid ledger) => db.Database.SqlQuery<GlRootV1>($"""
        SELECT id AS "Id",code AS "Code",control_state AS "ControlState",control_revision AS "ControlRevision",
            timeline_revision AS "TimelineRevision",retired_on AS "RetiredOn"
        FROM cp6_fin_gl_v1.roots WHERE tenant_id={Tenant} AND ledger_id={ledger}
        """);

    internal async Task<GlRootV1> Root(Guid ledger, Guid id, CancellationToken ct)
        => await Roots(ledger).SingleOrDefaultAsync(root => root.Id == id, ct)
            ?? throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404);

    internal async Task InsertRoot(Guid ledger, Guid root, string code, CancellationToken ct)
    {
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.roots(tenant_id,ledger_id,id,code)
            VALUES ({Tenant},{ledger},{root},{code}) ON CONFLICT(tenant_id,ledger_id,code) DO NOTHING
            """, ct);
        if (count != 1) throw Error("CODE_CONFLICT");
    }

    internal async Task WriteRoot(Guid ledger, GlRootV1 previous, GlRootV1 next, CancellationToken ct)
        => Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_gl_v1.roots SET control_state={next.ControlState},control_revision={next.ControlRevision},
                timeline_revision={next.TimelineRevision},retired_on={next.RetiredOn}
            WHERE (tenant_id,ledger_id,id)=({Tenant},{ledger},{previous.Id})
              AND control_revision={previous.ControlRevision}
              AND timeline_revision IS NOT DISTINCT FROM {previous.TimelineRevision}
            """, ct));

    internal Task<GlProposalV1?> Proposal(Guid ledger, Guid root, CancellationToken ct)
        => db.Database.SqlQuery<GlProposalV1>($"""
            SELECT p.id AS "Id",p.revision AS "Revision",p.state AS "State",p.version_id AS "VersionId",
                p.makers AS "Makers",p.activate AS "Activate",p.decision_id AS "DecisionId",p.submission_basis AS "SubmissionBasis"
            FROM cp6_fin_gl_v1.proposals p JOIN cp6_fin_gl_v1.versions v
              ON (v.tenant_id,v.ledger_id,v.root_id,v.id)=(p.tenant_id,p.ledger_id,p.root_id,p.version_id)
            WHERE (p.tenant_id,p.ledger_id,p.root_id)=({Tenant},{ledger},{root})
            ORDER BY (p.state IN ('DRAFT','SUBMITTED','APPROVED','REJECTED')) DESC,v.version_no DESC
            LIMIT 1
            """).SingleOrDefaultAsync(ct);

    internal async Task<GlProposalV1> History(Guid ledger, Guid root, Guid proposal, long revision, CancellationToken ct)
    {
        var snapshot = await db.Database.SqlQuery<string>($"""
            SELECT snapshot AS "Value" FROM cp6_fin_gl_v1.proposal_history
            WHERE (tenant_id,ledger_id,root_id,proposal_id,revision)=({Tenant},{ledger},{root},{proposal},{revision})
            """).SingleOrDefaultAsync(ct);
        return snapshot is null ? throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404) : Decode<GlProposalV1>(snapshot);
    }

    internal async Task WriteProposal(Guid ledger, Guid root, GlProposalV1? previous, GlProposalV1 next, CancellationToken ct)
    {
        if (previous is null)
        {
            Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_gl_v1.proposals(tenant_id,ledger_id,root_id,id,revision,state,version_id,makers,activate)
                VALUES ({Tenant},{ledger},{root},{next.Id},{next.Revision},{next.State},{next.VersionId},{next.Makers},{next.Activate})
                """, ct));
        }
        else
        {
            Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_fin_gl_v1.proposals
                SET revision={next.Revision},state={next.State},version_id={next.VersionId},makers={next.Makers},
                    activate={next.Activate},decision_id={next.DecisionId},submission_basis={next.SubmissionBasis}
                WHERE (tenant_id,ledger_id,root_id,id,revision)=({Tenant},{ledger},{root},{previous.Id},{previous.Revision})
                """, ct));
        }

        Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.proposal_history(tenant_id,ledger_id,root_id,proposal_id,revision,snapshot)
            VALUES ({Tenant},{ledger},{root},{next.Id},{next.Revision},{Encode(next)})
            """, ct));
    }

    internal async Task<GlVersionV1> AppendVersion(Guid ledger, Guid root, Guid maker, GlAccountContentV1 content, CancellationToken ct)
    {
        var version = await db.Database.SqlQuery<long>($"""
            SELECT COALESCE(max(version_no),0) AS "Value" FROM cp6_fin_gl_v1.versions
            WHERE (tenant_id,ledger_id,root_id)=({Tenant},{ledger},{root})
            """).SingleAsync(ct);
        var body = Encode(content);
        var next = new GlVersionV1(Guid.NewGuid(), Next(version), Digest(body), maker, content);
        Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.versions(tenant_id,ledger_id,root_id,id,version_no,maker,payload,digest)
            VALUES ({Tenant},{ledger},{root},{next.Id},{next.Number},{maker},{body},{next.Digest})
            """, ct));
        return next;
    }

    private sealed record VersionRow(Guid Id, long Number, Guid Maker, string Payload, string Digest);
    internal async Task<GlVersionV1> Version(Guid ledger, Guid root, Guid id, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<VersionRow>($"""
            SELECT id AS "Id",version_no AS "Number",maker AS "Maker",payload AS "Payload",digest AS "Digest"
            FROM cp6_fin_gl_v1.versions WHERE (tenant_id,ledger_id,root_id,id)=({Tenant},{ledger},{root},{id})
            """).SingleOrDefaultAsync(ct);
        return row is not null && Digest(row.Payload) == row.Digest
            ? new(row.Id, row.Number, row.Digest, row.Maker, Decode<GlAccountContentV1>(row.Payload))
            : throw Error("GL_VERSION_UNPROVEN", 503);
    }

    internal async Task AddDecision(Guid ledger, Guid root, GlDecisionV1 decision, CancellationToken ct)
        => Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.decisions(tenant_id,ledger_id,root_id,id,kind,reviewer,snapshot)
            VALUES ({Tenant},{ledger},{root},{decision.Id},{decision.Kind},{decision.Reviewer},{Encode(decision)})
            """, ct));

    internal async Task<GlDecisionV1> Decision(Guid ledger, Guid root, Guid id, CancellationToken ct)
    {
        var snapshot = await db.Database.SqlQuery<string>($"""
            SELECT snapshot AS "Value" FROM cp6_fin_gl_v1.decisions
            WHERE (tenant_id,ledger_id,root_id,id)=({Tenant},{ledger},{root},{id})
            """).SingleOrDefaultAsync(ct);
        return snapshot is null ? throw Error("DECISION_NOT_FOUND", 404) : Decode<GlDecisionV1>(snapshot);
    }

    internal async Task<Guid> ReleaseDecision(Guid ledger, Guid root, Guid release, CancellationToken ct)
    {
        var result = await db.Database.SqlQuery<Guid?>($"""
            SELECT decision_id AS "Value" FROM cp6_fin_gl_v1.timelines
            WHERE (tenant_id,ledger_id,root_id,release_id)=({Tenant},{ledger},{root},{release})
            """).SingleOrDefaultAsync(ct);
        return result ?? throw Error("RELEASE_UNPROVEN", 503);
    }

    internal async Task<GlDecisionV1?> CurrentRestoreDecision(Guid ledger, Guid root, long control, long timeline,
        CancellationToken ct)
    {
        var snapshot = await db.Database.SqlQuery<string>($"""
            SELECT snapshot AS "Value" FROM cp6_fin_gl_v1.decisions
            WHERE (tenant_id,ledger_id,root_id)=({Tenant},{ledger},{root}) AND kind='RESTORE'
              AND jsonb_extract_path_text(snapshot::jsonb,'heads','controlRevision')::bigint={control}
              AND jsonb_extract_path_text(snapshot::jsonb,'heads','timelineRevision')::bigint={timeline}
            ORDER BY (snapshot::jsonb->>'appliedAt')::timestamptz DESC,id DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        return snapshot is null ? null : Decode<GlDecisionV1>(snapshot);
    }

    private sealed record TimelineRow(long Revision, Guid ReleaseId, Guid VersionId, Guid DecisionId);
    internal async Task<GlTimelineV1?> Timeline(Guid ledger, GlRootV1 root, CancellationToken ct)
    {
        if (root.TimelineRevision is not long at) return null;
        var row = await db.Database.SqlQuery<TimelineRow>($"""
            SELECT revision AS "Revision",release_id AS "ReleaseId",version_id AS "VersionId",decision_id AS "DecisionId"
            FROM cp6_fin_gl_v1.timelines WHERE (tenant_id,ledger_id,root_id,revision)=({Tenant},{ledger},{root.Id},{at})
            """).SingleAsync(ct);
        var segments = await db.Database.SqlQuery<GlTimelineSegmentV1>($"""
            SELECT valid_from AS "From",to_exclusive AS "ToExclusive",version_id AS "VersionId",release_id AS "ReleaseId"
            FROM cp6_fin_gl_v1.segments WHERE (tenant_id,ledger_id,root_id,revision)=({Tenant},{ledger},{root.Id},{at})
            ORDER BY valid_from
            """).ToArrayAsync(ct);
        return new(row.Revision, row.ReleaseId, row.VersionId, row.DecisionId, segments);
    }

    internal async Task AppendTimeline(Guid ledger, Guid root, long revision, GlReleasePlanV1 plan,
        object proof, CancellationToken ct)
    {
        Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.timelines(tenant_id,ledger_id,root_id,revision,release_id,version_id,decision_id,release_proof)
            VALUES ({Tenant},{ledger},{root},{revision},{plan.ReleaseId},{plan.VersionId},{plan.DecisionId},{Encode(proof)})
            """, ct));
        foreach (var part in plan.Segments)
        {
            Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_gl_v1.segments(tenant_id,ledger_id,root_id,revision,valid_from,to_exclusive,version_id,release_id)
                VALUES ({Tenant},{ledger},{root},{revision},{part.From},{part.ToExclusive},{part.VersionId},{part.ReleaseId})
                """, ct));
        }
    }

    internal sealed record GraphRow(Guid RootId, Guid VersionId, Guid ReleaseId, DateOnly From, DateOnly? ToExclusive,
        string Payload, string Digest, Guid Maker);
    internal Task<GraphRow[]> Graph(Guid ledger, CancellationToken ct) => db.Database.SqlQuery<GraphRow>($"""
        SELECT s.root_id AS "RootId", s.version_id AS "VersionId", s.release_id AS "ReleaseId", s.valid_from AS "From",
            s.to_exclusive AS "ToExclusive",v.payload AS "Payload",v.digest AS "Digest",v.maker AS "Maker"
        FROM cp6_fin_gl_v1.roots root JOIN cp6_fin_gl_v1.segments s
            ON (s.tenant_id,s.ledger_id,s.root_id,s.revision)=(root.tenant_id,root.ledger_id,root.id,root.timeline_revision)
        JOIN cp6_fin_gl_v1.versions v ON (v.tenant_id,v.ledger_id,v.root_id,v.id)=(s.tenant_id,s.ledger_id,s.root_id,s.version_id)
        WHERE root.tenant_id={Tenant} AND root.ledger_id={ledger}
        ORDER BY s.root_id,s.valid_from
        """).ToArrayAsync(ct);

    internal sealed record CreatedPreview(Guid Id, DateTimeOffset ExpiresAt);
    internal async Task<CreatedPreview> AddPreview(Guid ledger, Guid subject, string kind, object value, CancellationToken ct)
    {
        var created = await db.Database.SqlQuery<CreatedPreview>($"""
            INSERT INTO cp6_fin_gl_v1.previews(tenant_id,ledger_id,id,subject_id,kind,snapshot,expires_at)
            VALUES ({Tenant},{ledger},{Guid.NewGuid()},{subject},{kind},{Encode(value)},clock_timestamp()+interval '10 minutes')
            RETURNING id AS "Id",expires_at AS "ExpiresAt"
            """).ToListAsync(ct);
        return created.Single();
    }

    internal async Task<GlStoredPreviewV1> Preview(Guid ledger, Guid subject, Guid id, string kind, CancellationToken ct)
    {
        var preview = await db.Database.SqlQuery<GlStoredPreviewV1>($"""
            SELECT p.kind AS "Kind",p.snapshot AS "Snapshot",(p.expires_at>clock_timestamp()) AS "IsCurrent",
                (c.id IS NOT NULL) AS "IsConsumed"
            FROM cp6_fin_gl_v1.previews p LEFT JOIN cp6_fin_gl_v1.consumed_previews c USING(tenant_id,ledger_id,id)
            WHERE (p.tenant_id,p.ledger_id,p.subject_id,p.id)=({Tenant},{ledger},{subject},{id})
            """).SingleOrDefaultAsync(ct);
        if (preview is null || preview.Kind != kind) throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404);
        if (preview.IsConsumed) throw Error("PREVIEW_ALREADY_USED", 409);
        if (!preview.IsCurrent) throw Error("STALE", 412);
        return preview;
    }

    internal async Task Consume(Guid ledger, Guid preview, Guid operation, CancellationToken ct)
        => Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.consumed_previews(tenant_id,ledger_id,id,operation_ref)
            VALUES ({Tenant},{ledger},{preview},{operation})
            """, ct));

    internal async Task Event(Guid ledger, Guid operation, string kind, Guid actor, object value, CancellationToken ct)
        => Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.events(tenant_id,ledger_id,operation_ref,kind,actor_id,body)
            VALUES ({Tenant},{ledger},{operation},{kind},{actor},{Encode(value)})
            """, ct));

    internal async Task EnqueueRefresh(FinanceScopeV1 scope, Guid ledger, GlReceiptV1 result, string kind, CancellationToken ct)
    {
        // 通知只指向待重新读取的账套和对象。接收方仍须向当前 Owner 校验资格。
        var body = Encode(new { schemaVersion = 1, scope, result.RootId, result.ScopeRevision, kind });
        Applied(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.outbox(tenant_id,ledger_id,event_id,scope_revision,root_id,body,body_digest)
            VALUES ({Tenant},{ledger},{result.OperationRef},{result.ScopeRevision},{result.RootId},{body},{Digest(body)})
            """, ct));
    }

    private static void Applied(int count)
    {
        if (count != 1) throw Error("STALE", 412);
    }
}
