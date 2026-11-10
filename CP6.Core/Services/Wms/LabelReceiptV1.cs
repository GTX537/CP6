using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Wms;

// New PostgreSQL entries bind their input and retain the original result JSON as bytes-in-a-string.
// Existing bare DTO entries remain readable and are never rewritten into this format.
internal static class LabelReceiptV1
{
    private const string Protocol = "cp6-label-receipt-v1";
    private sealed record Envelope(string Protocol, string RequestHash, string ResultJson);

    internal static string Bind(object input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    internal static string Encode(string requestHash, LabelJobDto result)
        => JsonSerializer.Serialize(new Envelope(Protocol, requestHash, JsonSerializer.Serialize(result)));

    internal static LabelJobDto Read(string stored, string jobNo, string? requestHash = null)
    {
        try
        {
            using var document = JsonDocument.Parse(stored);
            var resultJson = stored;
            if (document.RootElement.TryGetProperty(nameof(Envelope.Protocol), out _))
            {
                var frame = JsonSerializer.Deserialize<Envelope>(stored);
                if (frame == null || frame.Protocol != Protocol || string.IsNullOrEmpty(frame.RequestHash) || string.IsNullOrEmpty(frame.ResultJson))
                    throw new JsonException("Invalid label receipt frame.");
                if (requestHash != null && frame.RequestHash != requestHash)
                    throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
                resultJson = frame.ResultJson;
            }
            var result = JsonSerializer.Deserialize<LabelJobDto>(resultJson);
            if (result == null || result.JobNo != jobNo || result.OperationId == Guid.Empty)
                throw new JsonException("Invalid label receipt result.");
            return result;
        }
        catch (Exception error) when (error is JsonException || error is InvalidOperationException && error is not MobileTaskConflictException)
        { throw new OrderPriceCorrectionException("LABEL_RECEIPT_UNAVAILABLE", 503, error); }
    }
}
