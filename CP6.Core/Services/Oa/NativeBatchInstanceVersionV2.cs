using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

internal static class NativeBatchInstanceVersionV2
{
    internal static async Task RequireWriter(CP6Context db,CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_native_tasks_v2.batch_ready() AS \"Value\"").SingleAsync(ct))
            throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("BATCH_STORE_UNAVAILABLE",503);
    }
    // A batch can change an instance through an earlier, already confirmed item.
    // Accept only that exact resulting version under the shared WF_AUTH lock.
    // Any intervening vote, edit or another batch changes the version and still conflicts.
    internal static Task<bool> OwnPriorResult(CP6Context db,Guid environment,Guid actor,Guid batch,Guid instance,string version,CancellationToken ct)
        =>db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(
              SELECT FROM cp6_native_tasks_v2.transfer_batches b,
                jsonb_array_elements(b.snapshot_json::jsonb->'items') item,
                LATERAL (
                  SELECT result_json FROM cp6_personal_form_v2.commands c WHERE c.tenant_id=b.tenant_id AND c.environment_id=b.environment_id
                    AND c.actor_id=b.actor_id AND c.operation='OA_TASK_TRANSFER' AND c.command_id=item->>'itemCommandId'
                    AND c.resource_key=item->>'taskId' AND c.completed AND NOT c.closed
                  UNION ALL
                  SELECT result_json FROM cp6_native_tasks_v2.commands c WHERE c.tenant_id=b.tenant_id AND c.environment_id=b.environment_id
                    AND c.actor_id=b.actor_id AND c.operation='OA_TASK_TRANSFER' AND c.command_key=item->>'itemCommandId'
                    AND c.resource_id=(item->>'taskId')::uuid AND c.completed AND NOT c.closed
                ) original
              WHERE b.id={batch} AND b.tenant_id={db.CurrentTenantId} AND b.environment_id={environment} AND b.actor_id={actor}
                AND (original.result_json::jsonb#>>ARRAY['result','task','instanceId'])::uuid={instance}
                AND original.result_json::jsonb#>>ARRAY['result','task','instanceRowVersion']={version}
            ) AS "Value"
            """).SingleAsync(ct);
}
