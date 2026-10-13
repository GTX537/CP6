using CP6.Entity.Contracts.ErpCredit;

namespace Cp6.ErpCredit.Generated;

public sealed class FrozenR8CreditCodec(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files) : CreditCodecSet(false, files)
{
    /// <summary>Captures the full saved HTTP201 body. The request is retained as bytes, not claimed as an authorized Owner command.</summary>
    public FrozenHttpReplay CaptureReplay(string operation, string commandKey, ReadOnlySpan<byte> request,
        string requestHash, int status, ReadOnlySpan<byte> response, string responseHash)
    {
        if (operation is not ("C01" or "C02") || status != 201 || string.IsNullOrEmpty(commandKey))
            throw new System.Text.Json.JsonException("FROZEN_REPLAY_IDENTITY");
        var requestCopy = request.ToArray(); var responseCopy = response.ToArray();
        if (CreditIdentity.Sha256(requestCopy) != requestHash || CreditIdentity.Sha256(responseCopy) != responseHash)
            throw new System.Text.Json.JsonException("FROZEN_REPLAY_DIGEST");
        using var parsedRequest = CP6.Entity.Contracts.Wire.StrictJson.Parse(requestCopy);
        var saved = DecodeReplay("EC_" + operation + "_COMMITTED_CommercialDraftView", responseCopy);
        return new(commandKey, requestCopy, saved);
    }
}
