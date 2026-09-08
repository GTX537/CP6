using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Pure signing primitive. It opens no network connection and cannot select a storage authority.
internal static class S3RequestSignature
{
    internal static string Authorization(string method, string path, IReadOnlyDictionary<string, string> headers,
        string payloadHash, string region, string accessKeyId, ReadOnlySpan<byte> secret)
    {
        if (method is not ("GET" or "HEAD" or "PUT") || region is not ("auto" or "us-east-1") ||
            path.Length is < 2 or > 512 || path[0] != '/' ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal) ||
            path.Any(c => !IsPathCharacter(c)) || !IsHash(payloadHash) ||
            accessKeyId.Length is < 16 or > 128 || accessKeyId.Any(c => !char.IsAsciiLetterOrDigit(c)) ||
            secret.Length is < 16 or > 128 || secret.ContainsAnyExceptInRange((byte)'!', (byte)'~'))
            throw Error("r2-signature-input");
        if (headers.Count is < 3 or > 8 || headers.Any(pair =>
                !AllowedHeaders.Contains(pair.Key, StringComparer.Ordinal) ||
                pair.Value.Length is < 1 or > 4096 || pair.Value.Trim() != pair.Value ||
                pair.Value.Contains("  ", StringComparison.Ordinal) ||
                pair.Value.Any(c => c is < ' ' or > '~')) ||
            !headers.TryGetValue("host", out var host) || host.Length > 253 ||
            host.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-') ||
            !headers.TryGetValue("x-amz-date", out var date) ||
            !DateTimeOffset.TryParseExact(date, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _) ||
            !headers.TryGetValue("x-amz-content-sha256", out var suppliedHash) || suppliedHash != payloadHash)
            throw Error("r2-signature-headers");
        if (method == "PUT" && (!headers.TryGetValue("if-none-match", out var condition) || condition != "*"))
            throw Error("r2-create-condition");
        if (method != "PUT" && payloadHash != Cp6DeterministicJson.Sha256Hex([]))
            throw Error("r2-read-payload");

        var sorted = headers.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var signedHeaders = string.Join(";", sorted.Select(pair => pair.Key));
        var canonicalHeaders = string.Concat(sorted.Select(pair => pair.Key + ":" + pair.Value + "\n"));
        var canonical = method + "\n" + path + "\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
        var scope = date[..8] + "/" + region + "/s3/aws4_request";
        var toSign = "AWS4-HMAC-SHA256\n" + date + "\n" + scope + "\n" +
            Cp6DeterministicJson.Sha256Hex(Encoding.UTF8.GetBytes(canonical));
        var initial = new byte[secret.Length + 4];
        "AWS4"u8.CopyTo(initial);
        secret.CopyTo(initial.AsSpan(4));
        byte[] day = [], regional = [], service = [], signing = [];
        try
        {
            day = Mac(initial, date[..8]);
            regional = Mac(day, region);
            service = Mac(regional, "s3");
            signing = Mac(service, "aws4_request");
            var signature = Convert.ToHexString(Mac(signing, toSign)).ToLowerInvariant();
            return $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{scope},SignedHeaders={signedHeaders},Signature={signature}";
        }
        finally
        {
            foreach (var key in new[] { initial, day, regional, service, signing }) CryptographicOperations.ZeroMemory(key);
        }
    }

    private static readonly string[] AllowedHeaders =
        ["host", "range", "content-type", "if-none-match", "x-amz-content-sha256", "x-amz-date", "x-amz-security-token"];

    private static byte[] Mac(byte[] key, string text) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));
    private static bool IsPathCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '~';
    private static bool IsHash(string value) => value.Length == 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Cp6ReleaseContractException Error(string code) => new(code, "R2 signing input violates the bounded request policy.");
}
