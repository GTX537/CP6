using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Pub;

public sealed record FileContentRefV1(string Owner, Guid Id, string Version, string HashAlgorithm,
    string Digest, long ByteLength, string MediaType);
public sealed record FileInspectionV1(string Status, string Provider, string? EngineVersion,
    string? SignatureVersion, DateTimeOffset CheckedAt, string? EvidenceDigest, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FileUploadV1(Guid OperationId, Guid FileId, EngineeringRevisionRefV1 Parent,
    string FileName, long ByteLength, string Digest, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FileContentCommandV1(Guid OperationId, Guid FileId, string Action,
    string ExpectedVersion, Guid ExpectedEtag, string Reason);
public sealed record FileContentSnapshotV1(Guid TenantId, FileContentRefV1 ContentRef,
    EngineeringRevisionRefV1 Parent, string FileName, string StateVersion, Guid Etag,
    FileInspectionV1 Inspection, bool Held, string Availability, Guid ActorId,
    DateTimeOffset RecordedAt, string Action, string Reason, bool IsCurrent, string OriginalDigest);
public sealed record FileContentResultV1(Guid OperationId, string Kind, Guid ObjectId,
    string Version, string Digest);
public sealed record FileContentOperationV1(Guid OperationId, string State, FileContentResultV1? Result = null);
public sealed record FileContentDownloadV1(byte[] Bytes, FileContentRefV1 ContentRef, string FileName);
internal sealed record FileContentEventV1(Guid TenantId, Guid Id, string Version, Guid Etag,
    FileInspectionV1 Inspection, bool Held, Guid ActorId, DateTimeOffset RecordedAt, string Action, string Reason);

public interface IFileContentInspectorV1
{
    Task<FileInspectionV1> InspectAsync(byte[] bytes, CancellationToken ct);
}
