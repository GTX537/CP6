using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed record DefinitionRuntimeImpactV2(int RunningInstances,int SuspendedInstances,int RootInstances,int ChildInstances,
    int ActiveDrafts,int PersonalEntryBindings,int RelatedAppBindings,int PublishedParentVersions,DateTimeOffset ObservedAtUtc);

internal static class DefinitionRuntimeImpactReaderV2
{
    // Caller has authorized this definition under the existing root fence. Only scoped counts
    // leave this participant: no other definition names, subjects, principals or business data.
    internal static Task<DefinitionRuntimeImpactV2> Read(CP6Context db,Guid environment,Guid head,bool form,CancellationToken ct)
        =>db.Database.SqlQuery<DefinitionRuntimeImpactV2>($"""
            WITH RECURSIVE versions AS (
              SELECT id,head_id,snapshot_json::jsonb AS body FROM cp6_workflow_authoring_v2.versions
              WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
            ), selected AS (
              SELECT id FROM cp6_form_authoring_v2.versions WHERE {form} AND tenant_id={db.CurrentTenantId} AND environment_id={environment} AND head_id={head}
              UNION ALL SELECT id FROM versions WHERE NOT {form} AND head_id={head}
            ), parents AS (
              SELECT v.id,v.head_id FROM versions v WHERE EXISTS(SELECT FROM jsonb_array_elements(v.body->'dependencyRefs') d
                WHERE d->>'kind'=CASE WHEN {form} THEN 'FORM' ELSE 'FLOW' END AND (d->>'version')::uuid IN(SELECT id FROM selected))
              UNION
              SELECT v.id,v.head_id FROM versions v JOIN parents p ON EXISTS(SELECT FROM jsonb_array_elements(v.body->'dependencyRefs') d
                WHERE d->>'kind'='FLOW' AND (d->>'version')::uuid=p.id)
            ), runtime AS (
              SELECT instance_id,root_instance_id AS root_id,flow_head,flow_version FROM cp6_approval_runtime_v2.instances
              WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
              UNION ALL SELECT instance_id,root_id,flow_head,flow_version FROM cp6_personal_form_v2.instances
              WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
            ), inflight AS (
              SELECT x.instance_id,x.root_id,i."Status" AS status FROM runtime x JOIN public."Wf_FlowInstance" i ON i."Id"=x.instance_id AND i."TenantId"={db.CurrentTenantId}
              WHERE i."Status" IN(0,4) AND ((NOT {form} AND x.flow_head={head}) OR ({form} AND
                (i."FormDefVersionId" IN(SELECT id FROM selected) OR EXISTS(SELECT FROM cp6_personal_form_v2.submissions s
                  WHERE s.root_id=x.root_id AND s.tenant_id={db.CurrentTenantId} AND s.environment_id={environment} AND s.head_id={head}))))
            ), related_heads AS (
              SELECT head_id FROM parents UNION SELECT head_id FROM versions WHERE NOT {form} AND head_id={head}
            ), related_keys AS (
              SELECT flow_key FROM cp6_workflow_authoring_v2.heads WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND id IN(SELECT head_id FROM related_heads)
            ), app AS (
              SELECT DISTINCT b.id FROM cp6_approval_binding_v2.heads b JOIN cp6_approval_binding_v2.intervals t
                ON t.tenant_id=b.tenant_id AND t.environment_id=b.environment_id AND t.binding_id=b.id AND t.timeline_id=b.timeline_id
              JOIN cp6_approval_binding_v2.revisions r ON r.tenant_id=t.tenant_id AND r.environment_id=t.environment_id AND r.binding_id=t.binding_id AND r.id=t.revision_id
              WHERE b.tenant_id={db.CurrentTenantId} AND b.environment_id={environment} AND t.state='ACTIVE' AND (t.ends_at IS NULL OR t.ends_at>statement_timestamp())
                AND ((r.body::jsonb->>'flowKey') COLLATE "C" IN(SELECT flow_key COLLATE "C" FROM related_keys)
                  OR EXISTS(SELECT FROM jsonb_array_elements(r.body::jsonb->'rules') rule WHERE (rule->>'flowKey') COLLATE "C" IN(SELECT flow_key COLLATE "C" FROM related_keys)))
            )
            SELECT (SELECT count(*)::int FROM inflight WHERE status=0) AS "RunningInstances",
              (SELECT count(*)::int FROM inflight WHERE status=4) AS "SuspendedInstances",
              (SELECT count(*)::int FROM inflight WHERE instance_id=root_id) AS "RootInstances",
              (SELECT count(*)::int FROM inflight WHERE instance_id<>root_id) AS "ChildInstances",
              (SELECT count(*)::int FROM cp6_personal_form_v2.drafts WHERE {form} AND tenant_id={db.CurrentTenantId} AND environment_id={environment} AND head_id={head} AND status=0) AS "ActiveDrafts",
              (SELECT count(*)::int FROM cp6_workflow_authoring_v2.activation_current WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
                AND (({form} AND form_head_id={head}) OR (NOT {form} AND head_id={head} AND form_head_id IS NOT NULL))) AS "PersonalEntryBindings",
              (SELECT count(*)::int FROM app) AS "RelatedAppBindings",(SELECT count(*)::int FROM parents) AS "PublishedParentVersions",
              statement_timestamp() AS "ObservedAtUtc"
            """).SingleAsync(ct);
    internal static string[] Entries(DefinitionRuntimeImpactV2 value)=>[
        $"PERSONAL_ENTRY_BINDINGS:{value.PersonalEntryBindings}",$"CURRENT_OR_SCHEDULED_APP_BINDINGS:{value.RelatedAppBindings}",
        $"FROZEN_PARENT_VERSIONS:{value.PublishedParentVersions}"];
}
