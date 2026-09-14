using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.SourceFence;

// Evidence hashes bind separately reviewed files. They are not independently verified approvals.
public sealed record ActualSourceFreezeRequest(Guid RunId, ActualSourceIdentity SourceIdentity,
    string ExpectedScopeSha256, string ApprovalEvidenceSha256, string WriterControlEvidenceSha256,
    string RecoveryPlanEvidenceSha256, string TargetClosedEvidenceSha256)
{
    public string Format => "CP6.C04A.ActualSourceFreezeRequest.v1";
    public string Digest() => ActualSourceFreezer.Hash(JsonSerializer.Serialize(this));
}

public sealed record ActualSourceFreezeStatus(SourceFenceStatus Fence, string RequestSha256,
    string BeforeScopeSha256, string FrozenScopeSha256)
{
    public bool CompleteWriteFenceVerified => false;
    public bool TargetClosedIndependentlyVerified => false;
    public bool RecoveryIntegrationVerified => false;
    public bool ApprovalIndependentlyVerified => false;
    public string AcceptanceScope => "local-source-freeze-component; external-writer-isolation-and-target-recovery-coordination-pending; C04A-open";
}

/// <summary>
/// Actual local source freeze and recovery. Recovery requires live terminal target proof;
/// lifecycle isolation, routing and approval remain separate execution prerequisites.
/// The existing rehearsal entry cannot mutate any catalog carrying this actual-source receipt.
/// </summary>
public sealed partial class ActualSourceFreezer(ActualSourceInspectionOptions options)
{
    private const string Marker = "CP6.C04A.ActualSourceFreeze.v1";
    private static readonly JsonSerializerOptions StrictJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record Receipt(string Format, ActualSourceFreezeRequest Request, string RequestSha256, string BeforeScopeSha256, string FrozenScopeSha256);

    public Task<ActualSourceFreezeStatus> FreezeAsync(ActualSourceFreezeRequest request, string expectedRequestSha256,
        CancellationToken cancellationToken = default) => ExecuteAsync(request, expectedRequestSha256, cancellationToken);
    public Task<ActualSourceFreezeStatus> StatusAsync(string expectedRequestSha256, CancellationToken cancellationToken = default)
        => ExecuteAsync(null, expectedRequestSha256, cancellationToken);

    private async Task<ActualSourceFreezeStatus> ExecuteAsync(ActualSourceFreezeRequest? request, string expectedRequest, CancellationToken token)
    {
        try
        {
            if (!DigestValid(expectedRequest)) Fail("C04A_REQUEST_MISMATCH");
            if (request is not null) ValidateRequest(request, expectedRequest);
            var inspector = new ActualSourceInspector(options with { ExpectedScopeSha256 = null });
            var builder = inspector.ValidateOptions();
            builder.ApplicationName = "CP6.C04A.ActualSourceFreezer";
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
            // One transaction owns the application lock and all twenty table locks before
            // inspecting identity/scope, deciding replay, installing guards and saving receipt.
            var observation = await inspector.CaptureWithinTransactionAsync(connection, transaction, request is not null, token);
            var fence = new SourceFence(new(builder.ConnectionString, options.ExpectedDatabaseName, options.ExpectedDatabaseGuid,
                options.LockTimeoutMilliseconds, options.CommandTimeoutSeconds));
            using var read = Command("SELECT CONVERT(nvarchar(3750),value) FROM sys.extended_properties WHERE class=0 AND name=@marker;", connection, transaction);
            read.Parameters.AddWithValue("@marker", Marker);
            var stored = await read.ExecuteScalarAsync(token);
            if (stored is string serialized)
            {
                var receipt = Deserialize<Receipt>(Encoding.UTF8.GetBytes(serialized));
                // This is our own canonical persisted representation, not a flexible
                // input document. Getter-only fields must not silently absorb tampering.
                if (!string.Equals(serialized, JsonSerializer.Serialize(receipt), StringComparison.Ordinal)) Fail("C04A_ACTUAL_BINDING_DRIFT");
                if (receipt.Format != Marker || receipt.Request is null || !DigestValid(receipt.FrozenScopeSha256)) Fail("C04A_ACTUAL_BINDING_DRIFT");
                ValidateRequest(receipt.Request, receipt.RequestSha256);
                if (receipt.BeforeScopeSha256 != receipt.Request.ExpectedScopeSha256) Fail("C04A_ACTUAL_BINDING_DRIFT");
                if (receipt.RequestSha256 != expectedRequest) Fail("C04A_REQUEST_MISMATCH");
                VerifyIdentityAndScope(observation, receipt.Request.SourceIdentity, receipt.FrozenScopeSha256);
                VerifyExpectedBeforeScope(receipt.BeforeScopeSha256);
                var state = await fence.ExecuteWithinTransactionAsync(connection, transaction, "Status", Guid.Empty, 0, true, token);
                if (state.State != "Frozen" || state.Generation != 1 || state.RunId != receipt.Request.RunId) Fail("C04A_ACTUAL_BINDING_DRIFT");
                await transaction.CommitAsync(token);
                return Status(state, receipt);
            }

            if (request is null) Fail("C04A_ACTUAL_BINDING_MISSING");
            using var exists = Command("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';", connection, transaction);
            if ((int)(await exists.ExecuteScalarAsync(token))! != 0) Fail("C04A_ACTUAL_BINDING_MISSING");
            VerifyIdentityAndScope(observation, request!.SourceIdentity, request.ExpectedScopeSha256);
            VerifyExpectedBeforeScope(request.ExpectedScopeSha256);
            if (observation.SourceRows.Values.Any(count => count != 0)) Fail("C04A_SOURCE_NONEMPTY");
            if (observation.OpaqueModuleCount != 0) Fail("C04A_OPAQUE_MODULES");
            var unownedBefore = (observation.SecuritySha256, observation.ProgramsSha256);
            var frozen = await fence.ExecuteWithinTransactionAsync(connection, transaction, "Freeze", request.RunId, 0, true, token);
            var after = await inspector.CaptureWithinTransactionAsync(connection, transaction, true, token);
            if (after.Identity != observation.Identity || after.SchemaSha256 != observation.SchemaSha256 ||
                after.SqlAgentSha256 != observation.SqlAgentSha256 || after.MigrationHistorySha256 != observation.MigrationHistorySha256)
                Fail("C04A_INSPECTION_DRIFT");
            var binding = new Receipt(Marker, request, expectedRequest, observation.ScopeSha256, after.ScopeSha256);
            var value = JsonSerializer.Serialize(binding);
            if (Encoding.Unicode.GetByteCount(value) > 7500) Fail("C04A_REQUEST_TOO_LARGE");
            using var save = Command("EXEC sys.sp_addextendedproperty @name=@marker,@value=@receipt;", connection, transaction);
            save.Parameters.AddWithValue("@marker", Marker);
            save.Parameters.Add("@receipt", SqlDbType.NVarChar, 3750).Value = value;
            await save.ExecuteNonQueryAsync(token);
            // DDL hooks must not smuggle unrelated grants/modules into the post-freeze
            // baseline. Exclude only exact guard objects and DENYs already verified above.
            _ = await fence.ExecuteWithinTransactionAsync(connection, transaction, "Status", Guid.Empty, 0, true, token);
            var final = await inspector.CaptureWithinTransactionAsync(connection, transaction, true, token);
            if (final.ScopeSha256 != after.ScopeSha256 ||
                unownedBefore != await inspector.CaptureUnownedMetadataAsync(connection, transaction, true, token))
                Fail("C04A_INSPECTION_DRIFT");
            await transaction.CommitAsync(token);
            return Status(frozen, binding);
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (SqlException error)
        {
            throw new SourceFenceException(token.IsCancellationRequested ? "C04A_CANCELLED" : error.Number switch
            { -2 or 1222 => "C04A_LOCK_TIMEOUT", 1205 => "C04A_LOCK_UNAVAILABLE", _ => "C04A_SQL_FAILURE" });
        }
        catch (ArgumentException) { throw new SourceFenceException("C04A_INVALID_OPTIONS"); }
        catch (InvalidOperationException) { throw new SourceFenceException("C04A_SQL_FAILURE"); }
    }

    private SqlCommand Command(string sql, SqlConnection connection, SqlTransaction transaction)
        => new(sql, connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };

    private void VerifyExpectedBeforeScope(string scope)
    {
        if (options.ExpectedScopeSha256 is { } expected && !string.Equals(scope, expected, StringComparison.OrdinalIgnoreCase)) Fail("C04A_SCOPE_CHANGED");
    }

    private static void VerifyIdentityAndScope(ActualSourceInspection report, ActualSourceIdentity identity, string scope)
    {
        if (report.Identity != identity) Fail("C04A_DATABASE_IDENTITY");
        if (report.ScopeSha256 != scope) Fail("C04A_SCOPE_CHANGED");
    }

    private static ActualSourceFreezeStatus Status(SourceFenceStatus state, Receipt receipt) => new(
        state with { AcceptanceScope = "actual-local-source-guards; privileged-writers-and-target-coordination-unverified; C04A-open" },
        receipt.RequestSha256, receipt.BeforeScopeSha256, receipt.FrozenScopeSha256);

    private static void ValidateRequest(ActualSourceFreezeRequest request, string expected)
    {
        if (request.RunId == Guid.Empty || request.SourceIdentity is null ||
            new[] { request.ExpectedScopeSha256, request.ApprovalEvidenceSha256, request.WriterControlEvidenceSha256,
                request.RecoveryPlanEvidenceSha256, request.TargetClosedEvidenceSha256, expected }.Any(d => !DigestValid(d)) ||
            request.Digest() != expected) Fail("C04A_REQUEST_MISMATCH");
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool DigestValid(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code) => throw new SourceFenceException(code);

    // Bound file read uses one handle and one byte buffer: the expected hash never covers
    // different bytes than those parsed. No SQL connection is opened before this succeeds.
    public static async Task<ActualSourceFreezeRequest> ReadRequestAsync(string path, string expectedFileSha256, CancellationToken token = default)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !DigestValid(expectedFileSha256)) Fail("C04A_REQUEST_FILE_MISMATCH");
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length is < 2 or > 16384) Fail("C04A_REQUEST_TOO_LARGE");
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, token);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedFileSha256) Fail("C04A_REQUEST_FILE_MISMATCH");
            var request = Deserialize<ActualSourceFreezeRequest>(bytes);
            ValidateRequest(request, request.Digest());
            return request;
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new SourceFenceException("C04A_REQUEST_FILE_UNAVAILABLE"); }
    }

    private static T Deserialize<T>(byte[] bytes) where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicateProperties(document.RootElement);
            if (typeof(T) == typeof(ActualSourceFreezeRequest) && document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Format", out var format) &&
                (format.ValueKind != JsonValueKind.String || format.GetString() != "CP6.C04A.ActualSourceFreezeRequest.v1"))
                Fail("C04A_REQUEST_INVALID_JSON");
            return JsonSerializer.Deserialize<T>(bytes, StrictJson) ?? throw new SourceFenceException("C04A_REQUEST_INVALID_JSON");
        }
        catch (JsonException) { throw new SourceFenceException("C04A_REQUEST_INVALID_JSON"); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) Fail("C04A_REQUEST_INVALID_JSON");
                if (property.Name == "LocalContainer" && property.Value.ValueKind != JsonValueKind.Null)
                    _ = LocalSqlContainerInspector.ReadBindingElement(property.Value);
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
