using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Crm.Cutover;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.SourceFence;

public sealed record ActualSourceRecoveryRequest(Guid RunId, string SourceFreezeRequestSha256,
    string TargetRollbackReceiptSha256, string ApprovalEvidenceSha256)
{
    public string Format => "CP6.C04A.ActualSourceRecoveryRequest.v1";
    public string Digest() => TargetRollbackProtocol.Digest(this);
}

public sealed record ActualSourceRecoveryStatus(SourceFenceStatus Fence, string RecoveryRequestSha256,
    string SourceFreezeRequestSha256, string TargetRollbackReceiptSha256, string RecoveryReceiptSha256)
{
    public bool TargetTerminalClosureIndependentlyVerified => true;
    public bool CompleteWriteFenceVerified => false;
    public bool ApprovalIndependentlyVerified => false;
    public bool RoutingAndWriterLifecycleVerified => false;
    public string AcceptanceScope => "local-source-recovery-with-live-terminal-target-proof; lifecycle-and-routing-unverified; C04A-open";
}

public sealed partial class ActualSourceFreezer
{
    private const string RecoveryMarker = "CP6.C04A.ActualSourceRollback.v1";
    private sealed record RecoveryMetadata(ActualSourceIdentity Identity, string SchemaSha256, string SecuritySha256,
        string ProgramsSha256, string SqlAgentSha256, string MigrationHistorySha256)
    {
        internal static RecoveryMetadata From(ActualSourceInspection report) => new(report.Identity, report.SchemaSha256,
            report.SecuritySha256, report.ProgramsSha256, report.SqlAgentSha256, report.MigrationHistorySha256);
    }
    private sealed record RecoveryReceipt(string Format, ActualSourceRecoveryRequest Request, string RequestSha256,
        RecoveryMetadata ReopenedMetadata, DateTimeOffset RecordedAtUtc);

    public Task<ActualSourceRecoveryStatus> ReopenAsync(ActualSourceRecoveryRequest request, string expectedRequestSha256,
        string targetConnectionString, CancellationToken token = default) => RecoverAsync(request, expectedRequestSha256,
            cancellation => RecoveryTargetProofLease.OpenSingleAsync(request, targetConnectionString, cancellation), false, token);

    public Task<ActualSourceRecoveryStatus> RecoveryStatusAsync(ActualSourceRecoveryRequest request, string expectedRequestSha256,
        string targetConnectionString, CancellationToken token = default) => RecoverAsync(request, expectedRequestSha256,
            cancellation => RecoveryTargetProofLease.OpenSingleAsync(request, targetConnectionString, cancellation), true, token);

    public Task<ActualSourceRecoveryStatus> ReopenTargetsAsync(ActualSourceRecoveryRequest request, string expectedRequestSha256,
        IReadOnlyList<ActualSourceRecoveryTarget> targets, CancellationToken token = default) => RecoverAsync(request, expectedRequestSha256,
            cancellation => RecoveryTargetProofLease.OpenTargetsAsync(request, targets, cancellation), false, token);

    public Task<ActualSourceRecoveryStatus> RecoveryStatusTargetsAsync(ActualSourceRecoveryRequest request, string expectedRequestSha256,
        IReadOnlyList<ActualSourceRecoveryTarget> targets, CancellationToken token = default) => RecoverAsync(request, expectedRequestSha256,
            cancellation => RecoveryTargetProofLease.OpenTargetsAsync(request, targets, cancellation), true, token);

    private async Task<ActualSourceRecoveryStatus> RecoverAsync(ActualSourceRecoveryRequest request, string expectedRequestSha256,
        Func<CancellationToken, Task<RecoveryTargetProofLease>> openTargets, bool inspectOnly, CancellationToken token)
    {
        try
        {
            ValidateRecoveryRequest(request, expectedRequestSha256);
            var inspector = new ActualSourceInspector(options with { ExpectedScopeSha256 = null });
            var builder = inspector.ValidateOptions();
            builder.ApplicationName = "CP6.C04A.ActualSourceRecovery";
            // All targets first, source second: retain every target lock through source COMMIT.
            // There is no distributed write: the target was terminally closed beforehand.
            await using var targets = await openTargets(token);
            await using var source = new SqlConnection(builder.ConnectionString);
            await source.OpenAsync(token);
            await using var transaction = (SqlTransaction)await source.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var before = await inspector.CaptureWithinTransactionAsync(source, transaction, !inspectOnly, token);
            if (targets.ContainsSource(before.Identity))
                Fail("C04A_RECOVERY_TARGET_IS_SOURCE");
            var frozenText = await ReadMarkerAsync(source, transaction, Marker, token);
            if (frozenText is null) Fail("C04A_ACTUAL_BINDING_MISSING");
            var frozen = Deserialize<Receipt>(Encoding.UTF8.GetBytes(frozenText));
            if (frozenText != JsonSerializer.Serialize(frozen) || frozen.Format != Marker || frozen.Request is null
                || frozen.BeforeScopeSha256 != frozen.Request.ExpectedScopeSha256 || !DigestValid(frozen.FrozenScopeSha256))
                Fail("C04A_ACTUAL_BINDING_DRIFT");
            ValidateRequest(frozen.Request, request.SourceFreezeRequestSha256);
            if (frozen.RequestSha256 != request.SourceFreezeRequestSha256 || frozen.Request.SourceIdentity != before.Identity)
                Fail("C04A_ACTUAL_BINDING_DRIFT");
            VerifyExpectedBeforeScope(frozen.BeforeScopeSha256);
            if (frozen.Request.TargetClosedEvidenceSha256 != targets.AnchorSha256) Fail("C04A_RECOVERY_TARGET_NOT_BOUND");
            var fence = new SourceFence(new(builder.ConnectionString, options.ExpectedDatabaseName, options.ExpectedDatabaseGuid,
                options.LockTimeoutMilliseconds, options.CommandTimeoutSeconds));
            var state = await fence.ExecuteWithinTransactionAsync(source, transaction, "Status", Guid.Empty, 0, true, token);
            var recoveryText = await ReadMarkerAsync(source, transaction, RecoveryMarker, token);
            RecoveryReceipt recovery;
            if (recoveryText is not null)
            {
                recovery = Deserialize<RecoveryReceipt>(Encoding.UTF8.GetBytes(recoveryText));
                if (recoveryText != JsonSerializer.Serialize(recovery) || recovery.Format != RecoveryMarker || recovery.Request is null
                    || recovery.RequestSha256 != expectedRequestSha256 || recovery.Request != request
                    || recovery.ReopenedMetadata != RecoveryMetadata.From(before) || recovery.RecordedAtUtc.Offset != TimeSpan.Zero
                    || state.State != "Reopened" || state.Generation != 2 || state.RunId != frozen.Request.RunId)
                    Fail("C04A_RECOVERY_BINDING_DRIFT");
                ValidateRecoveryRequest(recovery.Request, expectedRequestSha256);
            }
            else
            {
                if (inspectOnly) Fail("C04A_RECOVERY_BINDING_MISSING");
                VerifyIdentityAndScope(before, frozen.Request.SourceIdentity, frozen.FrozenScopeSha256);
                if (state.State != "Frozen" || state.Generation != 1 || state.RunId != frozen.Request.RunId)
                    Fail("C04A_ACTUAL_BINDING_DRIFT");
                var unownedBefore = await inspector.CaptureUnownedMetadataAsync(source, transaction, true, token);
                state = await fence.ExecuteWithinTransactionAsync(source, transaction, "Reopen", frozen.Request.RunId, 1, true, token);
                var after = await inspector.CaptureWithinTransactionAsync(source, transaction, true, token);
                if (after.Identity != before.Identity || after.SchemaSha256 != before.SchemaSha256 || after.SqlAgentSha256 != before.SqlAgentSha256
                    || after.MigrationHistorySha256 != before.MigrationHistorySha256
                    || unownedBefore != await inspector.CaptureUnownedMetadataAsync(source, transaction, true, token))
                    Fail("C04A_INSPECTION_DRIFT");
                recovery = new(RecoveryMarker, request, expectedRequestSha256, RecoveryMetadata.From(after), DateTimeOffset.UtcNow);
                recoveryText = JsonSerializer.Serialize(recovery);
                if (Encoding.Unicode.GetByteCount(recoveryText) > 7500) Fail("C04A_REQUEST_TOO_LARGE");
                using var save = Command("EXEC sys.sp_addextendedproperty @name=@marker,@value=@receipt;", source, transaction);
                save.Parameters.AddWithValue("@marker", RecoveryMarker);
                save.Parameters.Add("@receipt", SqlDbType.NVarChar, 3750).Value = recoveryText;
                await save.ExecuteNonQueryAsync(token);
                var final = await inspector.CaptureWithinTransactionAsync(source, transaction, true, token);
                if (RecoveryMetadata.From(final) != recovery.ReopenedMetadata || final.SourceRows.Values.Any(count => count != 0))
                    Fail("C04A_INSPECTION_DRIFT");
                _ = await fence.ExecuteWithinTransactionAsync(source, transaction, "Status", Guid.Empty, 0, true, token);
            }
            // Recheck before the source commit. Table locks remain until target disposal;
            // changed receipts/metadata cannot be accepted as a stale file-only assertion.
            await targets.RecheckAsync(token);
            if (frozenText != await ReadMarkerAsync(source, transaction, Marker, token)
                || recoveryText != await ReadMarkerAsync(source, transaction, RecoveryMarker, token)) Fail("C04A_RECOVERY_BINDING_DRIFT");
            await transaction.CommitAsync(token);
            // Read-only target transaction is disposed (rolled back) after source commit.
            // Avoid a second COMMIT whose failure could obscure a successful source write.
            return new(state with { AcceptanceScope = "actual-local-source-reopened; live-terminal-target-verified; C04A-open" },
                expectedRequestSha256, request.SourceFreezeRequestSha256, request.TargetRollbackReceiptSha256, Hash(recoveryText));
        }
        catch (SourceFenceException) { throw; }
        catch (TargetRollbackException failure) { throw new SourceFenceException(failure.Code); }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (SqlException error) { throw new SourceFenceException(error.Number is -2 or 1222 or 1205 ? "C04A_LOCK_TIMEOUT" : "C04A_SQL_FAILURE"); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { throw new SourceFenceException("C04A_INVALID_OPTIONS"); }
    }

    private async Task<string?> ReadMarkerAsync(SqlConnection connection, SqlTransaction transaction, string marker, CancellationToken token)
    {
        using var command = Command("SELECT CONVERT(nvarchar(3750),value) FROM sys.extended_properties WHERE class=0 AND name=@marker;", connection, transaction);
        command.Parameters.AddWithValue("@marker", marker);
        return await command.ExecuteScalarAsync(token) as string;
    }

    private static void ValidateRecoveryRequest(ActualSourceRecoveryRequest request, string expected)
    {
        if (request is null || request.RunId == Guid.Empty || !DigestValid(expected) || !DigestValid(request.SourceFreezeRequestSha256)
            || !DigestValid(request.TargetRollbackReceiptSha256) || !DigestValid(request.ApprovalEvidenceSha256) || request.Digest() != expected)
            Fail("C04A_RECOVERY_REQUEST_MISMATCH");
    }

    public static async Task<ActualSourceRecoveryRequest> ReadRecoveryRequestAsync(string path, string expectedFileSha256, CancellationToken token = default)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !DigestValid(expectedFileSha256)) Fail("C04A_REQUEST_FILE_MISMATCH");
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length is < 2 or > 16384) Fail("C04A_REQUEST_TOO_LARGE");
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, token);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedFileSha256) Fail("C04A_REQUEST_FILE_MISMATCH");
            var request = Deserialize<ActualSourceRecoveryRequest>(bytes);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("Format", out var format)
                && (format.ValueKind != JsonValueKind.String || format.GetString() != request.Format)) Fail("C04A_REQUEST_INVALID_JSON");
            ValidateRecoveryRequest(request, request.Digest());
            return request;
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new SourceFenceException("C04A_REQUEST_FILE_UNAVAILABLE"); }
    }
}
