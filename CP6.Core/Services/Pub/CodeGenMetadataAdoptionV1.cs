using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

public sealed record CodeGenMetadataLegacyV1(Guid MetadataId, string SourceFingerprint, CodeGenMetadataDocumentV1 Document);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenMetadataAdoptV1(string OperationRef, string SourceFingerprint);

public sealed partial class CodeGenMetadataOwnerV1
{
    private Task<bool> IsAdopted(CP6Context db, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_codegen_metadata_v1.adoptions WHERE tenant_id={tenant} AND metadata_id={metadataId}) AS "Value"
        """).SingleAsync(ct);
    private static Task LockLegacy(CP6Context db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        "LOCK TABLE public.\"Pub_GenTable\", public.\"Pub_GenColumn\" IN SHARE ROW EXCLUSIVE MODE", ct);

    // Read under both native table write locks, so the fingerprint covers one complete legacy original.
    private async Task<CodeGenMetadataLegacyV1?> Legacy(CP6Context db, CancellationToken ct)
    {
        var table = await db.Pub_GenTables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == metadataId, ct);
        if (table == null) return null;
        var columns = await db.Pub_GenColumns.AsNoTracking().Where(x => x.GenTableId == metadataId).OrderBy(x => x.Sort).ThenBy(x => x.Id).ToArrayAsync(ct);
        var document = new CodeGenMetadataDocumentV1(new(table.EntityName, table.Module, table.TableName,
            table.ResourceKey, table.SeqBizKey, table.CodeField, table.RoutePath, table.MenuName),
            columns.Select(x => new CodeGenMetadataColumnV1(x.Name, x.ClrType, x.Label, x.Required, x.InList, x.InForm, x.Sort)).ToImmutableArray(),
            CodeGenService.PreviewTemplateRef, CodeGenService.PreviewTemplateVersion);
        _ = document.Preview();
        // Includes original row IDs and audit fields as well as every rendering field. Those rows remain intact.
        var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(Bytes(new { Table = table, Columns = columns }))).ToLowerInvariant();
        return new(metadataId, fingerprint, document);
    }
    public Task<CodeGenMetadataLegacyV1?> ReadLegacyAsync(CancellationToken ct = default) => Execute<CodeGenMetadataLegacyV1?>(false, async (db, token) =>
    {
        var actor = await Require(db, "view", token); await LockLegacy(db, token);
        if (await IsAdopted(db, token)) throw new CodeGenMetadataExceptionV1("CODEGEN_LEGACY_ALREADY_ADOPTED");
        return (await Legacy(db, token), actor, "view");
    }, ct);

    public Task<CodeGenMetadataReceiptV1> AdoptAsync(CodeGenMetadataAdoptV1 command, CancellationToken ct = default)
    {
        Operation(command.OperationRef);
        if (string.IsNullOrEmpty(command.SourceFingerprint)) throw new CodeGenMetadataExceptionV1("CODEGEN_SOURCE_REQUIRED", 400);
        var input = Bytes(new { Kind = "legacy-adoption-v1", Command = command });
        return Execute(true, async (db, token) =>
        {
            var actor = await Require(db, "view", token);
            var original = await Original(db, command.OperationRef, token);
            if (original != null) return (Receipt(original, actor, command.OperationRef, input), actor, "view");
            await Require(db, "save", token); await LockLegacy(db, token);
            if (await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM cp6_codegen_metadata_v1.documents WHERE tenant_id={tenant} AND metadata_id={metadataId}) AS "Value"
                """).SingleAsync(token)) throw new CodeGenMetadataExceptionV1("CODEGEN_METADATA_ALREADY_VERSIONED");
            var legacy = await Legacy(db, token) ?? throw new CodeGenMetadataExceptionV1("CODEGEN_LEGACY_NOT_FOUND", 404);
            if (legacy.SourceFingerprint != command.SourceFingerprint) throw new CodeGenMetadataExceptionV1("CODEGEN_LEGACY_CHANGED", 412);
            if (await db.Pub_GenTables.AnyAsync(x => x.Id != metadataId && x.EntityName == legacy.Document.Table.EntityName, token))
                throw new CodeGenMetadataExceptionV1("CODEGEN_LEGACY_NAME_AMBIGUOUS");
            var result = await Persist(db, command.OperationRef, input, legacy.Document, 0, actor, token);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_codegen_metadata_v1.adoptions(tenant_id,metadata_id,source_fingerprint)
                  VALUES({tenant},{metadataId},{command.SourceFingerprint})
                """, token);
            return (result, actor, "save");
        }, ct);
    }
}
