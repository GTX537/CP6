using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// A CP6 I/O adapter over the package-owned object-reference contract, not a Schema copy.
public sealed class ContentAddress
{
    private static readonly string[] Fields = ["byteLength", "key", "mediaType", "sha256", "storageAuthority"];
    private static readonly Regex FileNamePattern = new(
        @"^[a-z0-9][a-z0-9.-]{0,127}\.json\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private ContentAddress(string key, string mediaType, string sha256, int byteLength)
    {
        Key = key;
        MediaType = mediaType;
        Sha256 = sha256;
        ByteLength = byteLength;
    }

    public const string StorageAuthority = "cp6-release-r2-v1";
    public string Key { get; }
    public string MediaType { get; }
    public string Sha256 { get; }
    public int ByteLength { get; }

    public static ContentAddress Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(Fields, StringComparer.Ordinal))
            throw Error("object-reference");
        var authority = ReadString(value, "storageAuthority");
        var key = ReadString(value, "key");
        var media = ReadString(value, "mediaType");
        var hash = ReadString(value, "sha256");
        var length = value.GetProperty("byteLength");
        if (authority != StorageAuthority || hash.Length != 64 ||
            hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            !Cp6ReleaseMediaTypes.All.Contains(media, StringComparer.Ordinal) ||
            length.ValueKind != JsonValueKind.Number || !length.TryGetInt32(out var count) ||
            count is < 1 or > Cp6DeterministicJson.MaximumBytes)
            throw Error("object-reference");
        var prefix = $"objects/sha256/{hash[..2]}/{hash}/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || !IsFileName(key[prefix.Length..]))
            throw Error("object-key-binding");
        return new(key, media, hash, count);
    }

    public static ContentAddress Create(ReadOnlySpan<byte> bytes, string mediaType, string fileName)
    {
        if (bytes.Length is < 1 or > Cp6DeterministicJson.MaximumBytes ||
            !Cp6ReleaseMediaTypes.All.Contains(mediaType, StringComparer.Ordinal) || !IsFileName(fileName))
            throw Error("object-reference");
        var hash = Cp6DeterministicJson.Sha256Hex(bytes);
        return new($"objects/sha256/{hash[..2]}/{hash}/{fileName}", mediaType, hash, bytes.Length);
    }

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(new
    {
        storageAuthority = StorageAuthority,
        key = Key,
        mediaType = MediaType,
        sha256 = Sha256,
        byteLength = ByteLength
    });

    private static bool IsFileName(string value) =>
        value.Length <= 133 && !value.Contains("..", StringComparison.Ordinal) && FileNamePattern.IsMatch(value);

    private static string ReadString(JsonElement value, string name)
    {
        var field = value.GetProperty(name);
        return field.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(field.GetString())
            ? field.GetString()!
            : throw Error("object-reference");
    }

    private static Cp6ReleaseContractException Error(string code) => new(code, "Object reference violates the pinned I/O policy.");
}

public static class BoundedObjectReader
{
    public static async Task<byte[]> ReadAsync(Stream stream, int maximumBytes = Cp6DeterministicJson.MaximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (maximumBytes is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("object-size");
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[maximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count == 0 || count > maximumBytes) throw Error("object-size");
        return buffer.AsSpan(0, count).ToArray();
    }

    public static async Task<byte[]> ReadCheckedAsync(Stream stream, ContentAddress expected,
        string? actualMediaType, long? actualContentLength, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(expected.MediaType, actualMediaType, StringComparison.Ordinal) ||
            actualContentLength != expected.ByteLength)
            throw Error("object-metadata");
        var bytes = await ReadAsync(stream, expected.ByteLength, cancellationToken);
        if (bytes.Length != expected.ByteLength) throw Error("object-size");
        if (!string.Equals(Cp6DeterministicJson.Sha256Hex(bytes), expected.Sha256, StringComparison.Ordinal))
            throw Error("object-hash");
        return bytes;
    }

    private static Cp6ReleaseContractException Error(string code) => new(code, "Remote object bytes violate the pinned I/O policy.");
}
