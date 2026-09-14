using System.Text.Json;
using CP6.Crm.Cutover;

namespace CP6.Crm.SourceFence;

public sealed partial class ActualSourceFreezer
{
    public async Task<SourceFrozenProof> VerifyFrozenForTargetAsync(ActualSourceFreezeRequest request,
        string expectedRequestSha256, string targetSetAnchorSha256, Guid challenge, CancellationToken token = default)
    {
        try
        {
            if (request is null) Fail("C04A_REQUEST_MISMATCH");
            ValidateRequest(request, expectedRequestSha256);
            if (!DigestValid(targetSetAnchorSha256) || request.TargetClosedEvidenceSha256 != targetSetAnchorSha256)
                Fail("C04A_SOURCE_TARGET_SET_MISMATCH");
            if (challenge == Guid.Empty) Fail("C04A_SOURCE_PROOF_CHALLENGE_INVALID");
            var identity = request.SourceIdentity;
            var proof = new SourceFrozenProof(challenge, request.RunId, expectedRequestSha256, targetSetAnchorSha256,
                Hash(JsonSerializer.Serialize(identity)), identity.ServerName, identity.DatabaseName, identity.DatabaseGuid,
                request.ExpectedScopeSha256);
            proof.Validate();
            // Status verifies the persisted request/identity, frozen metadata, every guard
            // and Frozen generation 1 using its existing transaction. The CRM coordinator
            // must keep all target locks until enable commits to exclude legal recovery.
            var status = await StatusAsync(expectedRequestSha256, token);
            proof = proof with { FrozenScopeSha256 = status.FrozenScopeSha256 };
            proof.Validate();
            return proof;
        }
        catch (TargetRollbackException error) { throw new SourceFenceException(error.Code); }
    }
}
