using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record PersonalFormCatalogItemV2(string FormKey,string Name,string? Category,string? SubCategory,Guid FormVersionId,int FormVersion);
public sealed record PersonalFormCatalogPageV2(PersonalFormCatalogItemV2[] Items,int Total,int Page,int PageSize);

public sealed partial class PersonalFormOwnerV2
{
    // Catalog membership belongs to the same use-policy/activation authority as initiation.
    // It exposes selected publication metadata, never an author's unfinished draft.
    public async Task<PersonalFormCatalogPageV2> CatalogAsync(int page=1,int pageSize=20,string? search=null,CancellationToken ct=default)
    {
        if(page<1||page>100000||pageSize<1||pageSize>100||search?.Length>200)throw Error("CATALOG_QUERY_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await fence.AcquireAsync(ct);await Ready(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var roles=actor.RoleIds.ToArray();
        // The list needs publication metadata, not every form's potentially large schema.
        var items=(await db.Database.SqlQuery<PersonalFormCatalogItemV2>($"""
            SELECT v.snapshot_json::jsonb->>'key' AS "FormKey", v.snapshot_json::jsonb->'fields'->>'Name' AS "Name",
                   v.snapshot_json::jsonb->'fields'->>'Category' AS "Category", v.snapshot_json::jsonb->'fields'->>'SubCategory' AS "SubCategory",
                   v.id AS "FormVersionId", (v.snapshot_json::jsonb->>'version')::integer AS "FormVersion"
            FROM cp6_form_authoring_v2.heads h
            JOIN cp6_form_authoring_v2.activation_current a ON a.tenant_id=h.tenant_id AND a.environment_id=h.environment_id AND a.head_id=h.id
            JOIN cp6_form_authoring_v2.versions v ON v.tenant_id=h.tenant_id AND v.environment_id=h.environment_id AND v.head_id=h.id AND v.id=a.published_version_id
            JOIN cp6_personal_form_v2.policy p ON p.tenant_id=h.tenant_id AND p.environment_id=h.environment_id AND p.head_id=h.id
            WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={fence.EnvironmentId} AND a.enabled
              AND (p.body::jsonb->>'enabled')::boolean
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(p.body::jsonb->'roles') r WHERE (r->>'roleId')::integer=ANY({roles}))
            """).ToArrayAsync(ct)).AsEnumerable();
        if(!string.IsNullOrWhiteSpace(search))items=items.Where(x=>x.Name.Contains(search,StringComparison.OrdinalIgnoreCase)||x.FormKey.Contains(search,StringComparison.OrdinalIgnoreCase));
        var ordered=items.OrderBy(x=>x.Category,StringComparer.Ordinal).ThenBy(x=>x.SubCategory,StringComparer.Ordinal).ThenBy(x=>x.Name,StringComparer.Ordinal).ThenBy(x=>x.FormKey,StringComparer.Ordinal).ToArray();
        await Final(actor,lease,ct);
        return new(ordered.Skip((page-1)*pageSize).Take(pageSize).ToArray(),ordered.Length,page,pageSize);
    }
}
