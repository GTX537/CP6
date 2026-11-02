using System.Collections.Immutable;
using System.Text.Json.Serialization;
using CP6.Entity.DomainModels.Pub;

namespace CP6.Core.Services.Pub;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenMetadataTableV1(string EntityName, string Module, string TableName,
    string ResourceKey, string? SeqBizKey, string? CodeField, string RoutePath, string? MenuName);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenMetadataColumnV1(string Name, string ClrType, string? Label,
    bool Required, bool InList, bool InForm, int Sort);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenMetadataDocumentV1(CodeGenMetadataTableV1 Table,
    ImmutableArray<CodeGenMetadataColumnV1> Columns, string TemplateRef, string TemplateVersion)
{
    public CodeGenPreviewResult Preview()
    {
        if (Table is null || Columns.IsDefault || Columns.Any(x => x is null))
            throw new CodeGenPreviewValidationException("元数据表和列列表必须完整。");
        if (TemplateRef != CodeGenService.PreviewTemplateRef || TemplateVersion != CodeGenService.PreviewTemplateVersion)
            throw new CodeGenPreviewValidationException("模板或版本不可用。");
        return new CodeGenService().GeneratePreview(new GenTable { EntityName = Table.EntityName, Module = Table.Module,
            TableName = Table.TableName, ResourceKey = Table.ResourceKey, SeqBizKey = Table.SeqBizKey,
            CodeField = Table.CodeField, RoutePath = Table.RoutePath, MenuName = Table.MenuName },
            Columns.Select(x => new GenColumn { Name = x.Name, ClrType = x.ClrType, Label = x.Label!,
                Required = x.Required, InList = x.InList, InForm = x.InForm, Sort = x.Sort }).ToList());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenMetadataSaveV1(string OperationRef, long ExpectedRevision, CodeGenMetadataDocumentV1 Document);
public sealed record CodeGenMetadataSnapshotV1(Guid MetadataId, Guid TenantId, long Revision,
    CodeGenMetadataDocumentV1 Document, Guid SavedBy, DateTimeOffset SavedAtUtc);
public sealed record CodeGenMetadataReceiptV1(string OperationRef, CodeGenMetadataSnapshotV1 Snapshot);
public sealed class CodeGenMetadataExceptionV1(string code, int status = 409, Exception? inner = null) : Exception(code, inner)
{ public int Status { get; } = status; }
