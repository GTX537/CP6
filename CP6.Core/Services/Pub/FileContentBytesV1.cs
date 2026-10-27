using System.Security.Cryptography;
using System.Text;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Pub;

internal static class FileContentBytesV1
{
    internal const int MaxBytes = 20 * 1024 * 1024;
    internal const string Owner = "Main.File.Content.v1";
    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static async Task<byte[]> Read(Stream stream, long expected, CancellationToken ct)
    {
        if (expected is < 1 or > MaxBytes) throw Error("FILE_SIZE_LIMIT", 400);
        using var result = new MemoryStream(); var buffer = new byte[65536];
        while (true)
        {
            // Read one extra byte to detect a falsely declared length, without buffering an unbounded stream.
            var n = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, expected - result.Length + 1)), ct);
            if (n == 0) break;
            if (result.Length + n > expected) throw Error("FILE_LENGTH_MISMATCH", 400);
            result.Write(buffer, 0, n);
        }
        if (result.Length != expected) throw Error("FILE_LENGTH_MISMATCH", 400);
        return result.ToArray();
    }
    internal static string MediaType(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith("%PDF-"u8)) return "application/pdf";
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (bytes.AsSpan().StartsWith(new byte[] { 80, 75, 3, 4 })) return "application/zip";
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (!text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'))) return "text/plain";
        }
        catch (DecoderFallbackException) { }
        return "application/octet-stream";
    }
    internal static void Validate(FileUploadV1 input)
    {
        if (input == null || input.OperationId == Guid.Empty || input.FileId == Guid.Empty || input.Parent == null
            || input.Parent.Id == Guid.Empty || input.Parent.Owner != "Main.Plm.EngineeringRevision.v1"
            || input.Digest == null || input.Digest.Length != 64 || input.Digest.Any(c => !char.IsAsciiHexDigitLower(c))
            || input.ByteLength is < 1 or > MaxBytes || string.IsNullOrWhiteSpace(input.FileName) || input.FileName.Length > 255
            || input.FileName.Any(char.IsControl) || input.FileName.IndexOfAny(['/', '\\']) >= 0)
            throw Error("FILE_UPLOAD_INVALID", 400);
        Revision(input.Parent.Version); CP6.Core.Services.Plm.EngineeringRoutingValuesV1.Text(input.Reason);
    }
}
