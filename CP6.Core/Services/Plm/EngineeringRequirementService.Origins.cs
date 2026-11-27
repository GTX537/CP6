using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRequirementService
{
    internal const string CrmActivityOrigin = "CRM-ACTIVITY/1";
    internal const string CrmOpportunityOrigin = "OPP/1";
    internal const string CrmAttachmentOrigin = "ACT-FILE-REFERENCE/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<RequirementOriginalDownloadV1> DownloadOriginalAsync(Guid id, string version, Guid? question,
        CancellationToken ct = default)
    {
        if (id == Guid.Empty || question == Guid.Empty) throw Error("REQUIREMENT_IDENTITY_INVALID", 400);
        var revision = Revision(version);
        return await Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement, "query", false, async (access, token) =>
        {
            var store = new EngineeringRequirementStoreV1(db);
            await store.Fence(false, token);
            await access.RequireEngineeringRequirement(id, token);
            var snapshot = await Observe(access, store, await store.Read(id, revision, token), token);
            if (snapshot.Body.Source is { Kind: "UNRESOLVED", Origin: { } pending })
                throw OriginUnavailable(pending.Reference.Owner);

            RequirementOriginV1? origin;
            if (question is Guid questionId)
                origin = snapshot.Body.Requirement?.Clarifications?.SingleOrDefault(q => q.Id == questionId)?.Reply;
            else if (snapshot.Body.Source is { } source)
                origin = source.Origin;
            else
            {
                var requirement = snapshot.Body.Requirement!;
                var sourceVersion = await Exact(access, store, requirement.Source, "SOURCE", token);
                origin = sourceVersion.Body.Source is { Kind: "OWNER_ORIGINAL" } bound ? bound.Origin : null;
            }
            if (origin is null) throw Error("REQUIREMENT_ORIGINAL_NOT_FOUND", 404);
            var material = await ReadOrigin(access, origin, token);
            return new RequirementOriginalDownloadV1(material.Bytes, material.FileName);
        }, ct);
    }

    private async Task ObserveOrigin(OperationsAccessV1 access, EngineeringRequirementSnapshotV1 snapshot, CancellationToken ct)
    {
        if (snapshot.Body.Source is not { Origin: { } origin } source) return;
        if (source.Kind == "UNRESOLVED")
        {
            // 待补证引用只对登记人披露。只保存坐标，后续仍需原 Owner 核验。
            if (snapshot.AuthorId != access.ActorId) throw Error("OBJECT_FORBIDDEN", 403);
            return;
        }
        var material = await ReadOrigin(access, origin, ct);
        if (!QuotesMatch(material, source.Clauses)) throw Error("REQUIREMENT_ORIGINAL_TEXT_MISMATCH", 422);
    }

    private async Task<RequirementOriginMaterial> ReadOrigin(OperationsAccessV1 access, RequirementOriginV1 origin, CancellationToken ct)
    {
        EngineeringRequirementValuesV1.Origin(origin);
        var reference = origin.Reference;
        if (reference.Owner == OrderSourceReader.RequirementSourceOwner)
        {
            if (sales is null) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
            var bytes = await sales.ReadRequirementOriginal(access, reference.Id, Revision(reference.Version), reference.Digest, ct);
            return new(bytes, "销售订单原件.json", true);
        }
        if (reference.Owner is not (ControlledDocumentValuesV1.Owner or FileContentBytesV1.Owner))
            throw OriginUnavailable(reference.Owner);
        if (files is null) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);

        FileContentSnapshotV1 file;
        if (reference.Owner == ControlledDocumentValuesV1.Owner)
        {
            if (documents is null || !documents.Owns(db)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
            var docAccess = await access.SourceAsync(OperationsOwnerV1.ControlledDocument, ct);
            var doc = await documents.ReviewSource(docAccess, reference, false, ct);
            if (doc.Availability?.State != "AVAILABLE") throw Error("REQUIREMENT_DOCUMENT_UNAVAILABLE", 409);
            var occurrence = doc.Body.Contents.SingleOrDefault(c => c.OccurrenceId == origin.OccurrenceId);
            if (occurrence is null || occurrence.Role is not ("SOURCE" or "CONTROLLED"))
                throw Error("REQUIREMENT_DOCUMENT_ORIGINAL_NOT_FOUND", 404);
            file = await files.BoundSource(docAccess, occurrence.File, doc.Body.RevisionOrigin.Id, false, ct);
        }
        else
        {
            file = await files.ReadOwnedAsync(access, reference.Id, ct);
            if (file.ContentRef.Version != reference.Version || file.ContentRef.Digest != reference.Digest)
                throw Error("REQUIREMENT_ORIGIN_CHANGED", 412);
        }
        if (file.Availability != "AVAILABLE") throw Error("FILE_CONTENT_NOT_AVAILABLE", 409);

        // ACT 专用文件只能沿 ACT 宿主和守护流授权，普通 FILE 原件入口不取得该资格。
        var fileId = file.ContentRef.Id;
        await RequireOrdinaryFile(fileId, ct);
        access.AtCommit(token => RequireOrdinaryFile(fileId, token));
        var store = new FileContentStoreV1(db);
        await store.Fence(false, ct);
        var raw = await files.ReadVerifiedDownloadAsync(store, fileId, ct);
        return new(raw ?? throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503), file.FileName, false);
    }

    private async Task RequireOrdinaryFile(Guid fileId, CancellationToken ct)
    {
        _ = await db.Database.SqlQuery<Guid>($"""
            SELECT store_id AS "Value" FROM cp6_act_file_v1.hold_source_v1({db.CurrentTenantId})
            """).SingleOrDefaultAsync(ct);
        if (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_act_file_v1.grants WHERE tenant_id={db.CurrentTenantId}
                          AND file_id={fileId} AND purpose='ACTIVITY_EVIDENCE') AS "Value"
            """).SingleAsync(ct))
            throw OriginUnavailable(CrmAttachmentOrigin);
    }

    private static bool QuotesMatch(RequirementOriginMaterial origin, RequirementClauseV1[] clauses)
    {
        if (clauses.Length == 0) return true;
        if (origin.IsSales)
        {
            using var document = JsonDocument.Parse(origin.Bytes);
            return clauses.All(clause => SalesValue(document.RootElement, clause.Locator) == clause.OriginalText);
        }
        var quotes = new List<(int Line, string Text)>(clauses.Length);
        foreach (var clause in clauses)
        {
            if (!clause.Locator.StartsWith("LINE:", StringComparison.Ordinal) ||
                !int.TryParse(clause.Locator.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var line) || line < 1)
                return false;
            quotes.Add((line, clause.OriginalText));
        }
        try
        {
            var text = StrictUtf8.GetString(origin.Bytes);
            using var input = new StringReader(text.StartsWith('\uFEFF') ? text[1..] : text);
            var currentLine = 0;
            string? actual = null;
            foreach (var quote in quotes.OrderBy(x => x.Line))
            {
                for (; currentLine < quote.Line; currentLine++)
                {
                    actual = input.ReadLine();
                    if (actual is null) return false;
                }
                if (actual != quote.Text) return false;
            }
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static string? SalesValue(JsonElement node, string locator)
    {
        if (!locator.StartsWith("JSON:/", StringComparison.Ordinal)) return null;
        foreach (var part in locator[6..].Split('/'))
        {
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(part, out var property))
                node = property;
            else if (node.ValueKind == JsonValueKind.Array &&
                int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < node.GetArrayLength())
                node = node[index];
            else return null;
        }
        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => node.GetRawText(),
            _ => null
        };
    }

    private static OrderPriceCorrectionException OriginUnavailable(string owner) => Error(owner switch
    {
        CrmOpportunityOrigin => "COMMERCIAL_HISTORY_UNPROVEN",
        CrmActivityOrigin => "REQUIREMENT_CRM_ACT_AUTHORITY_UNPROVEN",
        CrmAttachmentOrigin => "UNPROVEN_G03",
        _ => "REQUIREMENT_ORIGIN_PENDING"
    }, 503);

    private sealed record RequirementOriginMaterial(byte[] Bytes, string FileName, bool IsSales);
}

public sealed record RequirementOriginalDownloadV1(byte[] Bytes, string FileName);
