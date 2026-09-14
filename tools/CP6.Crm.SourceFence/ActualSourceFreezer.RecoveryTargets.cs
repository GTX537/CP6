using System.Data;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Crm.Cutover;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.SourceFence;

// Connection values are resolved only in memory and excluded from JSON and record diagnostics.
public sealed record ActualSourceRecoveryTarget(Guid OrganizationId, string TargetRollbackReceiptSha256,
    [property: JsonIgnore] string ConnectionString)
{
    public override string ToString() => $"ActualSourceRecoveryTarget {{ OrganizationId = {OrganizationId:D}, TargetRollbackReceiptSha256 = {TargetRollbackReceiptSha256} }}";
}

public sealed partial class ActualSourceFreezer
{
    private sealed class RecoveryTargetProofLease : IAsyncDisposable
    {
        private sealed record HeldTarget(SqlConnection Connection, SqlTransaction Transaction, string ReceiptSha256)
        {
            internal TargetRollbackReceipt Receipt { get; set; } = null!;
        }

        private readonly List<HeldTarget> held = [];
        private string? receiptSetSha256;
        internal string AnchorSha256 { get; private set; } = "";

        internal static async Task<RecoveryTargetProofLease> OpenSingleAsync(ActualSourceRecoveryRequest request,
            string connectionString, CancellationToken token)
        {
            var lease = new RecoveryTargetProofLease();
            try
            {
                var receipt = await lease.AcquireAsync(connectionString, request.TargetRollbackReceiptSha256, token);
                VerifySourceMembership(receipt, request);
                lease.AnchorSha256 = receipt.Anchor.Digest();
                return lease;
            }
            catch { await lease.DisposeAsync(); throw; }
        }

        internal static async Task<RecoveryTargetProofLease> OpenTargetsAsync(ActualSourceRecoveryRequest request,
            IReadOnlyList<ActualSourceRecoveryTarget> targets, CancellationToken token)
        {
            if (targets is null) Fail("C04A_RECOVERY_TARGETS_INVALID");
            // Snapshot caller-owned collections before any asynchronous operation.
            var requested = targets.ToArray();
            ValidateRecoveryTargets(requested);
            var lease = new RecoveryTargetProofLease();
            try
            {
                var receipts = new List<TargetRollbackReceipt>();
                foreach (var target in requested)
                {
                    var receipt = await lease.AcquireAsync(target.ConnectionString, target.TargetRollbackReceiptSha256, token);
                    if (receipt.Permit.OrganizationId != target.OrganizationId) Fail("C04A_RECOVERY_TARGET_ORGANIZATION_MISMATCH");
                    if (receipts.Any(previous => previous.Target.ServerName.Equals(receipt.Target.ServerName, StringComparison.OrdinalIgnoreCase)
                        && previous.Target.DatabaseName.Equals(receipt.Target.DatabaseName, StringComparison.OrdinalIgnoreCase)))
                        Fail("C04A_RECOVERY_TARGET_IDENTITY_DUPLICATE");
                    VerifySourceMembership(receipt, request);
                    receipts.Add(receipt);
                }
                var set = new TargetRollbackSetReceipt(receipts);
                set.Validate();
                if (set.Digest() != request.TargetRollbackReceiptSha256) Fail("C04A_TARGET_ROLLBACK_SET_RECEIPT_MISMATCH");
                lease.receiptSetSha256 = request.TargetRollbackReceiptSha256;
                lease.AnchorSha256 = set.Anchor.Digest();
                return lease;
            }
            catch { await lease.DisposeAsync(); throw; }
        }

        private async Task<TargetRollbackReceipt> AcquireAsync(string connectionString, string receiptSha256, CancellationToken token)
        {
            var connection = await TargetRollbackProtocol.OpenLocalAsync(connectionString, token);
            SqlTransaction transaction;
            try { transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token); }
            catch { await connection.DisposeAsync(); throw; }
            var target = new HeldTarget(connection, transaction, receiptSha256);
            held.Add(target);
            target.Receipt = await TargetRollbackProtocol.ReadWithinTransactionAsync(connection, transaction, receiptSha256, token);
            return target.Receipt;
        }

        private static void VerifySourceMembership(TargetRollbackReceipt receipt, ActualSourceRecoveryRequest request)
        {
            if (!receipt.Permit.SourceFreezeRequestSha256s.Contains(request.SourceFreezeRequestSha256, StringComparer.Ordinal))
                Fail("C04A_RECOVERY_SOURCE_NOT_BOUND");
        }

        internal bool ContainsSource(ActualSourceIdentity identity) => held.Any(target =>
            identity.ServerName == target.Receipt.Target.ServerName && identity.DatabaseName == target.Receipt.Target.DatabaseName);

        internal async Task RecheckAsync(CancellationToken token)
        {
            var receipts = new List<TargetRollbackReceipt>();
            foreach (var target in held)
                receipts.Add(await TargetRollbackProtocol.ReadWithinTransactionAsync(target.Connection, target.Transaction, target.ReceiptSha256, token));
            if (receiptSetSha256 is not null)
            {
                var set = new TargetRollbackSetReceipt(receipts);
                set.Validate();
                if (set.Digest() != receiptSetSha256 || set.Anchor.Digest() != AnchorSha256)
                    Fail("C04A_TARGET_ROLLBACK_SET_RECEIPT_MISMATCH");
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Try every disposal even if a broken target connection fails cleanup.
            Exception? failure = null;
            for (var index = held.Count - 1; index >= 0; index--)
            {
                try { await held[index].Transaction.DisposeAsync(); }
                catch (Exception error) { failure ??= error; }
                try { await held[index].Connection.DisposeAsync(); }
                catch (Exception error) { failure ??= error; }
            }
            held.Clear();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void ValidateRecoveryTargets(IReadOnlyList<ActualSourceRecoveryTarget> targets)
    {
        if (targets.Count is < 2 or > 16) Fail("C04A_RECOVERY_TARGETS_INVALID");
        string? previous = null;
        var receipts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (target is null || target.OrganizationId == Guid.Empty || !DigestValid(target.TargetRollbackReceiptSha256)
                || string.IsNullOrWhiteSpace(target.ConnectionString)) Fail("C04A_RECOVERY_TARGETS_INVALID");
            var organization = target.OrganizationId.ToString("D");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, organization) >= 0
                || !receipts.Add(target.TargetRollbackReceiptSha256)) Fail("C04A_RECOVERY_TARGETS_INVALID");
            previous = organization;
        }
    }

    public static async Task<IReadOnlyList<ActualSourceRecoveryTarget>> ReadRecoveryTargetsAsync(string path,
        string expectedFileSha256, CancellationToken token = default)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !DigestValid(expectedFileSha256)) Fail("C04A_REQUEST_FILE_MISMATCH");
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length is < 2 or > 16384) Fail("C04A_REQUEST_TOO_LARGE");
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, token);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedFileSha256) Fail("C04A_REQUEST_FILE_MISMATCH");
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireProperties(root, "Format", "Targets");
            if (root.GetProperty("Format").ValueKind != JsonValueKind.String
                || root.GetProperty("Format").GetString() != "CP6.C04A.RecoveryTargets.v1") Fail("C04A_REQUEST_INVALID_JSON");
            var elements = root.GetProperty("Targets");
            if (elements.ValueKind != JsonValueKind.Array) Fail("C04A_REQUEST_INVALID_JSON");
            if (elements.GetArrayLength() is < 2 or > 16) Fail("C04A_RECOVERY_TARGETS_INVALID");
            var references = new List<(Guid Organization, string Receipt, string Variable)>();
            var variables = new HashSet<string>(StringComparer.Ordinal);
            var receipts = new HashSet<string>(StringComparer.Ordinal);
            string? previous = null;
            foreach (var element in elements.EnumerateArray())
            {
                RequireProperties(element, "OrganizationId", "ReceiptSha256", "ConnectionEnvironmentVariable");
                var organizationElement = element.GetProperty("OrganizationId");
                var receiptElement = element.GetProperty("ReceiptSha256");
                var variableElement = element.GetProperty("ConnectionEnvironmentVariable");
                if (organizationElement.ValueKind != JsonValueKind.String || receiptElement.ValueKind != JsonValueKind.String
                    || variableElement.ValueKind != JsonValueKind.String) Fail("C04A_REQUEST_INVALID_JSON");
                if (!organizationElement.TryGetGuid(out var organization) || organization == Guid.Empty) Fail("C04A_REQUEST_INVALID_JSON");
                var receipt = receiptElement.GetString()!;
                var variable = variableElement.GetString()!;
                var key = organization.ToString("D");
                if (!DigestValid(receipt) || !ValidRecoveryTargetVariable(variable)
                    || previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0
                    || !variables.Add(variable) || !receipts.Add(receipt)) Fail("C04A_RECOVERY_TARGETS_INVALID");
                references.Add((organization, receipt, variable));
                previous = key;
            }
            // Validate the entire document before reading any environment secret or opening SQL.
            return references.Select(reference => new ActualSourceRecoveryTarget(reference.Organization, reference.Receipt,
                Environment.GetEnvironmentVariable(reference.Variable) is { Length: > 0 } connection && !string.IsNullOrWhiteSpace(connection)
                    ? connection : throw new SourceFenceException("C04A_RECOVERY_TARGET_CONNECTION_REQUIRED"))).ToArray();
        }
        catch (SourceFenceException) { throw; }
        catch (JsonException) { throw new SourceFenceException("C04A_REQUEST_INVALID_JSON"); }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new SourceFenceException("C04A_REQUEST_FILE_UNAVAILABLE"); }
    }

    private static bool ValidRecoveryTargetVariable(string value)
    {
        const string prefix = "C04A_RECOVERY_TARGET_";
        return value.StartsWith(prefix, StringComparison.Ordinal) && value.Length - prefix.Length is >= 1 and <= 64
            && value[prefix.Length..].All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) Fail("C04A_REQUEST_INVALID_JSON");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) Fail("C04A_REQUEST_INVALID_JSON");
        if (found.Count != names.Length) Fail("C04A_REQUEST_INVALID_JSON");
    }
}
