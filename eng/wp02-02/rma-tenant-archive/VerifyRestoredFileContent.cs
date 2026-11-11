using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class VerifyRestoredFileContent
{
    private sealed record Target(string Server, int Port, string Database, long DatabaseOid, string SystemIdentifier);
    private sealed record Original(Guid TenantId, Guid ActorId, Guid FileId, Guid OperationId, string FileName,
        string ContentDigest, long ByteLength, string MediaType, Guid ParentId, string ParentVersion, string ParentDigest,
        string StateVersion, Guid Etag, FileInspectionV1 Inspection, bool Held, DateTimeOffset RecordedAt,
        string Action, string Reason, string EventDigest, string ResultSha256, byte[]? OriginalFile);
    private sealed record Fixture(Target Target, string Username, Guid CandidateId, string DataDirectory, string AssetRoot,
        string ReceiptSha256, Original A, Original B);
    private sealed record Observed(Guid TenantId, Guid ActorId, string EventDigest, string ResultSha256,
        string ParentDigest, string ContentDigest, long ByteLength, bool Downloaded, bool OtherDigestRejected);

    public static async Task Main(string[] args)
    {
        Require(args.Length == 2, "Expected the sealed assembly directory and private candidate specification.");
        var assemblies = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(assemblies, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        await Exercise(JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(args[1]),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Exercise(Fixture fixture)
    {
        Require(fixture.Target.Server == "127.0.0.1" && fixture.CandidateId != Guid.Empty
            && Path.IsPathFullyQualified(fixture.DataDirectory) && Path.IsPathFullyQualified(fixture.AssetRoot)
            && fixture.A.TenantId != fixture.B.TenantId && fixture.A.ActorId != fixture.B.ActorId
            && fixture.A.FileId == fixture.B.FileId && fixture.A.OperationId == fixture.B.OperationId
            && fixture.A.ContentDigest != fixture.B.ContentDigest
            && fixture.A.OriginalFile is { Length: > 0 } && fixture.B.OriginalFile is null,
            "Expected the closed local candidate, original A bytes and independent B same-key history.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var ct = deadline.Token;
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = fixture.Target.Server, Port = fixture.Target.Port, Database = fixture.Target.Database,
            Username = fixture.Username, SslMode = SslMode.Disable, Pooling = false,
            ApplicationName = "w471-file-content-candidate-owner",
        };
        var store = new LocalFileStore(fixture.AssetRoot);
        var a = await ReadOriginal(connection.ConnectionString, fixture, store, fixture.A, null, ct);
        var b = await ReadOriginal(connection.ConnectionString, fixture, store, fixture.B, fixture.A.ContentDigest, ct);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Pid = Environment.ProcessId, fixture.Target, fixture.CandidateId, fixture.ReceiptSha256, A = a, B = b,
            CoreAssembly = typeof(FileContentOwnerV1).Assembly.Location,
        }));
    }

    private static async Task Guard(CP6Context db, Fixture fixture, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT json_build_object('Target',json_build_object('Server',host(inet_server_addr()),
                'Port',inet_server_port(),'Database',d.datname,'DatabaseOid',d.oid::bigint,
                'SystemIdentifier',p.system_identifier::text),'User',current_user,'Owner',pg_get_userbyid(d.datdba),
                'DataDirectory',current_setting('data_directory'),'Marker',shobj_description(d.oid,'pg_database'),
                'Admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
                'ReceiptSha256',(SELECT encode(sha256(convert_to(result_json||chr(10),'UTF8')),'hex')
                    FROM cp6_file_content_candidate_import_v1.receipt
                    WHERE singleton AND (result_json::jsonb->'recoveryReady')='false'::jsonb))::text
            FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
            """;
        using var result = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
        var native = result.RootElement;
        Require(native.GetProperty("Target").Deserialize<Target>() == fixture.Target
            && native.GetProperty("User").GetString() == fixture.Username
            && native.GetProperty("Owner").GetString() == fixture.Username
            && native.GetProperty("Marker").GetString() == PrivacyRestoreNativeV1.MarkerPrefix + fixture.CandidateId.ToString("D")
            && native.GetProperty("Admission").GetInt32() == 0
            && native.GetProperty("ReceiptSha256").GetString() == fixture.ReceiptSha256
            && string.Equals(Path.GetFullPath(native.GetProperty("DataDirectory").GetString()!),
                Path.GetFullPath(fixture.DataDirectory), StringComparison.OrdinalIgnoreCase),
            "Wrong private candidate or candidate admission and receipt changed.");
    }

    private static async Task<Observed> ReadOriginal(string connection, Fixture fixture, IFileStore store,
        Original original, string? otherDigest, CancellationToken ct)
    {
        await using var db = PrivacyRestoreNativeV1.Context(connection, original.TenantId);
        await Guard(db, fixture, ct);
        var owner = new FileContentOwnerV1(db, Authority(db, original.ActorId), store, new NoInspection());
        var snapshot = await owner.ReadAsync(original.FileId, ct: ct);
        var historical = await owner.ReadAsync(original.FileId, original.StateVersion, ct);
        Require(snapshot == historical && snapshot.TenantId == original.TenantId && snapshot.ActorId == original.ActorId
            && snapshot.ContentRef == new FileContentRefV1(FileContentBytesV1.Owner, original.FileId, "1", "SHA256",
                original.ContentDigest, original.ByteLength, original.MediaType)
            && snapshot.Parent == new EngineeringRevisionRefV1(EngineeringRevisionValuesV1.RevisionOwner, original.ParentId,
                original.ParentVersion, original.ParentDigest)
            && snapshot.FileName == original.FileName && snapshot.StateVersion == original.StateVersion
            && snapshot.Etag == original.Etag && snapshot.OriginalDigest == original.EventDigest
            && snapshot.RecordedAt == original.RecordedAt && snapshot.Action == original.Action && snapshot.Reason == original.Reason
            && snapshot.Held == original.Held && snapshot.Inspection == original.Inspection
            && snapshot.Availability == "AVAILABLE" && snapshot.IsCurrent,
            "Owner snapshot differs from the original FileContent or Engineering parent.");

        var operation = await owner.OperationAsync(original.OperationId, false, ct);
        var result = operation.Result;
        Require(operation.State == "COMMITTED" && operation.OperationId == original.OperationId && result != null
            && result == new FileContentResultV1(original.OperationId, "FILE_CONTENT", original.FileId,
                original.StateVersion, original.EventDigest)
            && Sha256(JsonSerializer.SerializeToUtf8Bytes(result)) == original.ResultSha256,
            "Owner recovery differs from the immutable original receipt.");

        var downloaded = false;
        if (original.OriginalFile is { } expected)
        {
            var file = await owner.DownloadAsync(original.FileId, "1", original.ContentDigest, ct);
            Require(file.ContentRef == snapshot.ContentRef && file.FileName == original.FileName
                && file.Bytes.SequenceEqual(expected) && Sha256(file.Bytes) == original.ContentDigest,
                "The original stored file differed on download.");
            downloaded = true;
        }
        if (otherDigest != null)
        {
            try
            {
                await owner.DownloadAsync(original.FileId, "1", otherDigest, ct);
                throw new InvalidOperationException("The other tenant's same-key content reference was accepted.");
            }
            catch (OrderPriceCorrectionException e) when (e.Status == 412 && e.Message == "FILE_REFERENCE_CHANGED")
            {
                // The current tenant resolves its own digest before touching the filesystem.
            }
        }
        return new(original.TenantId, original.ActorId, original.EventDigest, original.ResultSha256,
            snapshot.Parent.Digest, snapshot.ContentRef.Digest, snapshot.ContentRef.ByteLength, downloaded, otherDigest != null);
    }

    private static OperationsOwnerV1 Authority(CP6Context db, Guid actor)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actor.ToString("D")),
            new Claim("tenant_id", db.CurrentTenantId.ToString("D")),
            new Claim(ClaimTypes.Name, "w471-local-fixture"),
        ], "test"));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { OperationsOwnerV1.FileContent, OperationsOwnerV1.EngineeringRevision,
                                         OperationsOwnerV1.EngineeringProduct })
        {
            catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5);
            catalog.RegisterFields(resource, resource == OperationsOwnerV1.FileContent
                ? [new("Content", "Content"), new("FileName", "Name")] : []);
        }
        catalog.Seal();
        return new(db, http, _ => Task.FromResult(actor), catalog.Capture);
    }

    private sealed class NoInspection : IFileContentInspectorV1
    {
        public Task<FileInspectionV1> InspectAsync(byte[] bytes, CancellationToken ct)
            => throw new InvalidOperationException("A candidate read may not inspect or upload content.");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
