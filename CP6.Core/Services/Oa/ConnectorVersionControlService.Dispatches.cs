using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorVersionControlService
{
    private sealed record DispatchView(Guid JobId, string State, string TransportState, string? OriginalQueryState, string Adoption);

    public Task<JsonElement> DispatchesAsync(Guid rootId, Guid versionId, int page, CancellationToken ct) => Read(async (actor, token) =>
    {
        if (page is < 1 or > 1000000) throw Error("PAGE_INVALID", 400);
        await Require(actor, Permission("CONNECTOR", "control"), token);
        var root = await Root("CONNECTOR", rootId, token);
        var version = await Exact(root, versionId, token);
        if (!await CanReadSource(version, actor, token)) throw Error("RESOURCE_NOT_FOUND", 404);
        var rows = await db.Database.SqlQuery<DispatchView>($"""
            SELECT m.id AS "JobId",
              CASE WHEN m.state IN ('DISPATCH_ADMITTED','SENT','OUTCOME_UNKNOWN') AND (
                   c.job_id IS NOT NULL OR v.state<>'ENABLED' OR NOT coalesce(p.enabled,false) OR
                   NOT coalesce(f.enabled,false) OR a.job_id IS NOT NULL AND NOT EXISTS (
                       SELECT FROM public."Sys_Users" u WHERE u."TenantId"=m.tenant_id AND u."Id"=a.starter_id
                         AND u."Enable" AND (u."LockedUntil" IS NULL OR u."LockedUntil"<=clock_timestamp())))
                THEN 'CANCELLATION_REQUESTED' ELSE m.state END AS "State",
              coalesce(send.state,m.state) AS "TransportState", probe.state AS "OriginalQueryState",
              CASE WHEN a.simulated THEN 'SIMULATED' WHEN a.job_id IS NOT NULL THEN 'ADOPTED' ELSE 'UNPROVEN' END AS "Adoption"
            FROM cp6_oa_conn_v1.runtime_members m
            JOIN cp6_oa_conn_v1.version_control v ON (v.tenant_id,v.environment_id,v.root_id,v.version_id)=
                (m.tenant_id,m.environment_id,m.root_id,m.version_id)
            LEFT JOIN cp6_oa_conn_v1.profile_control p ON (p.tenant_id,p.environment_id,p.profile_id)=
                (m.tenant_id,m.environment_id,{version.ProfileId})
            LEFT JOIN cp6_scope_authorization_v1.fences f ON (f.tenant_id,f.environment_id,f.logical_domain)=
                (m.tenant_id,m.environment_id,'WF_AUTH')
            LEFT JOIN cp6_oa_conn_v1.dispatch_admissions a ON (a.tenant_id,a.environment_id,a.job_id)=(m.tenant_id,m.environment_id,m.id)
                AND (a.root_id,a.version_id)=(m.root_id,m.version_id)
            LEFT JOIN cp6_oa_conn_v1.dispatch_cancellations c ON (c.tenant_id,c.environment_id,c.job_id)=(a.tenant_id,a.environment_id,a.job_id)
            LEFT JOIN LATERAL (
                SELECT o.state FROM cp6_oa_conn_v1.dispatch_observations o
                WHERE (o.tenant_id,o.environment_id,o.job_id)=(a.tenant_id,a.environment_id,a.job_id) AND o.source='SEND'
                ORDER BY o.attempt DESC LIMIT 1
            ) send ON true
            LEFT JOIN LATERAL (
                SELECT o.state FROM cp6_oa_conn_v1.dispatch_observations o
                WHERE (o.tenant_id,o.environment_id,o.job_id)=(a.tenant_id,a.environment_id,a.job_id) AND o.source='ORIGINAL_QUERY'
                ORDER BY (o.state='ORIGINAL_OPERATION_OBSERVED') DESC, o.attempt DESC LIMIT 1
            ) probe ON true
            WHERE m.tenant_id={Tenant} AND m.environment_id={Environment} AND m.root_id={root.Id} AND m.version_id={version.Id}
                AND m.kind='DISPATCH'
            ORDER BY m.id OFFSET {(page - 1) * 100} LIMIT 101
            """).ToArrayAsync(token);
        return Value(new { page, hasNext = rows.Length > 100, entries = rows.Take(100) });
    }, ct);
}
