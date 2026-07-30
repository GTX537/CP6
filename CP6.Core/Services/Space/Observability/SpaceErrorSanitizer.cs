using System.Security.Cryptography;
using System.Text;

namespace CP6.Core.Services.Space.Observability;

public sealed record SpaceSafeError(
    string ReasonCode,
    string ExceptionType,
    string Fingerprint);

public static class SpaceErrorSanitizer
{
    public static bool IsStableReasonCode(string? reasonCode)
    {
        if (string.IsNullOrEmpty(reasonCode) ||
            reasonCode.Length > 128 ||
            reasonCode[0] is < 'A' or > 'Z')
        {
            return false;
        }

        for (var index = 1; index < reasonCode.Length; index++)
        {
            var value = reasonCode[index];
            if (value is not (>= 'A' and <= 'Z') &&
                value is not (>= '0' and <= '9') &&
                value != '_')
            {
                return false;
            }
        }

        return true;
    }

    public static SpaceSafeError Classify(Exception exception, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ValidateReasonCode(reasonCode);

        var exceptionType = exception.GetType();
        var material = $"{exceptionType.FullName}|{exception.HResult}";
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return new SpaceSafeError(reasonCode, exceptionType.Name, fingerprint);
    }

    public static string ToStorageCode(Exception exception, string reasonCode)
    {
        var safe = Classify(exception, reasonCode);
        return $"{safe.ReasonCode}:{safe.ExceptionType}:{safe.Fingerprint}";
    }

    private static void ValidateReasonCode(string reasonCode)
    {
        if (!IsStableReasonCode(reasonCode))
            throw InvalidReasonCode();
    }

    private static ArgumentException InvalidReasonCode()
        => new(
            "ReasonCode must be a stable uppercase ASCII code.",
            "reasonCode");
}
