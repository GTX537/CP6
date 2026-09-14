using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Crm.Cutover;

// Byte-identical observation contract compiled independently by Core and CRM.
// The coordinator supplies a fresh challenge while holding every target gate lock.
// This observation is not a permanent source seal or a privileged-writer fence.
public sealed record SourceFrozenProof(Guid Challenge, Guid FreezeRunId, string SourceFreezeRequestSha256,
    string TargetSetAnchorSha256, string SourceIdentitySha256, string ServerName, string DatabaseName,
    Guid DatabaseGuid, string FrozenScopeSha256)
{
    private static readonly JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public string Format => "CP6.C04A.SourceFrozenProof.v1";
    public string State => "Frozen";
    public long Generation => 1;
    public bool SourceFrozenAtObservation => true;
    public bool CompleteWriteFenceVerified => false;
    public bool ApprovalIndependentlyVerified => false;
    public string Digest() => TargetRollbackProtocol.Digest(this);

    public void Validate()
    {
        if (Challenge == Guid.Empty || FreezeRunId == Guid.Empty || DatabaseGuid == Guid.Empty
            || !TargetRollbackProtocol.IsHash(SourceFreezeRequestSha256)
            || !TargetRollbackProtocol.IsHash(TargetSetAnchorSha256)
            || !TargetRollbackProtocol.IsHash(SourceIdentitySha256)
            || !TargetRollbackProtocol.IsHash(FrozenScopeSha256)
            || !ValidName(ServerName) || !ValidName(DatabaseName)) Fail();
    }

    public static SourceFrozenProof Parse(string serialized)
    {
        try
        {
            if (serialized is null || serialized.Length > 16384
                || Encoding.UTF8.GetByteCount(serialized) > 16384) Fail();
            var proof = JsonSerializer.Deserialize<SourceFrozenProof>(serialized, Strict);
            if (proof is null) Fail();
            proof.Validate();
            // The wire representation is canonical, including all getter-only flags.
            // Exact comparison also rejects duplicate/missing/reordered properties,
            // alternate casing, whitespace and escape spellings after deserialization.
            if (!string.Equals(serialized, JsonSerializer.Serialize(proof), StringComparison.Ordinal)) Fail();
            return proof;
        }
        catch (JsonException) { throw new TargetRollbackException("C04A_SOURCE_FROZEN_PROOF_INVALID"); }
        catch (ArgumentException) { throw new TargetRollbackException("C04A_SOURCE_FROZEN_PROOF_INVALID"); }
    }

    private static bool ValidName(string? name)
    {
        if (name is not { Length: >= 1 and <= 128 } || string.IsNullOrWhiteSpace(name)
            || !string.Equals(name, name.Trim(), StringComparison.Ordinal)) return false;
        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsControl(name[index])) return false;
            if (!char.IsSurrogate(name[index])) continue;
            if (!char.IsHighSurrogate(name[index]) || index + 1 >= name.Length || !char.IsLowSurrogate(name[++index])) return false;
        }
        return true;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail() => throw new TargetRollbackException("C04A_SOURCE_FROZEN_PROOF_INVALID");
}
