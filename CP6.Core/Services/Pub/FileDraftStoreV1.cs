using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Pub;

/// <summary>草稿和正式引用使用调用方持有的文件事务与写锁。</summary>
internal sealed class FileDraftStoreV1(CP6Context db)
{
    internal sealed record Draft(Guid ActorId, long Revision, bool Bound, Guid OperationId);

    internal Task<Draft?> Find(Guid draft, CancellationToken ct) => db.Database.SqlQuery<Draft>($"""
        SELECT actor_id AS "ActorId",revision AS "Revision",bound AS "Bound",operation_id AS "OperationId"
        FROM cp6_file_content_v1.draft_sets WHERE tenant_id={db.CurrentTenantId} AND draft_id={draft}
        """).SingleOrDefaultAsync(ct);

    internal Task<FileContentRefV1[]> DraftRefs(Guid draft, CancellationToken ct) => db.Database.SqlQuery<FileContentRefV1>($"""
        SELECT {FileContentBytesV1.Owner} AS "Owner",c.file_id AS "Id",'1' AS "Version",'SHA256' AS "HashAlgorithm",
          c.digest AS "Digest",c.byte_length AS "ByteLength",c.media_type AS "MediaType"
        FROM cp6_file_content_v1.draft_items d JOIN cp6_file_content_v1.contents c USING(tenant_id,file_id)
        WHERE d.tenant_id={db.CurrentTenantId} AND d.draft_id={draft} ORDER BY d.ordinal
        """).ToArrayAsync(ct);

    internal Task<FileContentRefV1[]> BoundRefs(EngineeringRevisionRefV1 target, CancellationToken ct)
    {
        var revision = Revision(target.Version);
        return db.Database.SqlQuery<FileContentRefV1>($"""
            SELECT {FileContentBytesV1.Owner} AS "Owner",c.file_id AS "Id",'1' AS "Version",'SHA256' AS "HashAlgorithm",
              c.digest AS "Digest",c.byte_length AS "ByteLength",c.media_type AS "MediaType"
            FROM cp6_file_content_v1.draft_bindings b JOIN cp6_file_content_v1.contents c USING(tenant_id,file_id)
            WHERE b.tenant_id={db.CurrentTenantId} AND b.target_id={target.Id} AND b.target_revision={revision}
            ORDER BY b.draft_id,b.ordinal
            """).ToArrayAsync(ct);
    }

    internal async Task<long> Save(Guid draft, Guid actor, Guid operation, Draft? current,
        FileContentRefV1[] references, CancellationToken ct)
    {
        var revision = checked((current?.Revision ?? 0) + 1);
        if (current is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_file_content_v1.draft_sets(tenant_id,draft_id,actor_id,revision,operation_id,bound)
                VALUES({db.CurrentTenantId},{draft},{actor},{revision},{operation},false)
                """, ct);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_file_content_v1.draft_sets SET revision={revision},operation_id={operation}
                WHERE tenant_id={db.CurrentTenantId} AND draft_id={draft};
                DELETE FROM cp6_file_content_v1.draft_items WHERE tenant_id={db.CurrentTenantId} AND draft_id={draft};
                """, ct);
        }
        var ids = references.Select(reference => reference.Id).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_file_content_v1.draft_items(tenant_id,draft_id,file_id,ordinal)
            SELECT {db.CurrentTenantId},{draft},file,position
            FROM unnest({ids}) WITH ORDINALITY AS items(file,position)
            """, ct);
        return revision;
    }

    internal async Task<long> Bind(FileDraftBindV1 command, Draft draft, CancellationToken ct)
    {
        var revision = checked(draft.Revision + 1);
        var targetRevision = Revision(command.Target.Version);
        var existing = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_file_content_v1.draft_bindings b
              JOIN cp6_file_content_v1.draft_items d USING(tenant_id,file_id)
              WHERE b.tenant_id={db.CurrentTenantId} AND d.draft_id={command.DraftId}
                AND b.target_id={command.Target.Id} AND b.target_revision={targetRevision}) AS "Value"
            """).SingleAsync(ct);
        if (existing) throw Error("FILE_TARGET_ALREADY_BOUND", 409);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_file_content_v1.draft_bindings(tenant_id,draft_id,file_id,ordinal,target_id,target_revision,operation_id)
            SELECT tenant_id,draft_id,file_id,ordinal,{command.Target.Id},{targetRevision},{command.OperationId}
            FROM cp6_file_content_v1.draft_items WHERE tenant_id={db.CurrentTenantId} AND draft_id={command.DraftId};
            UPDATE cp6_file_content_v1.draft_sets SET revision={revision},bound=true,operation_id={command.OperationId}
            WHERE tenant_id={db.CurrentTenantId} AND draft_id={command.DraftId};
            """, ct);
        return revision;
    }
}
