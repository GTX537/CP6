using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadMappingReplaySnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SpaceCadMappingReplaySnapshotV1 Create(
        SpaceCadMappingPreviewV1 preview)
    {
        SpaceCadMapping.ValidatePreview(preview);
        return Create(
            preview.TenantId,
            preview.ProfileId,
            preview.ProfileVersion,
            preview.ProfileDefinitionSha256,
            preview.SourceSha256,
            preview.InventorySha256,
            preview.SourceStructureSha256,
            preview.PreviewSha256,
            preview.LayerOverrides);
    }

    public static SpaceCadMappingReplaySnapshotV1 Create(
        Guid tenantId,
        Guid profileId,
        int profileVersion,
        string profileDefinitionSha256,
        string sourceSha256,
        string expectedInventorySha256,
        string expectedSourceStructureSha256,
        string expectedMappingPreviewSha256,
        IReadOnlyList<SpaceCadLayerMappingOverrideV1> layerOverrides)
    {
        ArgumentNullException.ThrowIfNull(layerOverrides);
        if (layerOverrides.Any(item => item is null))
        {
            throw new InvalidDataException(
                "The CAD mapping replay overrides contain a null item.");
        }
        var withoutHash = new SpaceCadMappingReplaySnapshotV1(
            SpaceCadMappingReplaySnapshotVersions.SchemaVersion,
            tenantId,
            profileId,
            profileVersion,
            profileDefinitionSha256,
            sourceSha256,
            expectedInventorySha256,
            expectedSourceStructureSha256,
            expectedMappingPreviewSha256,
            layerOverrides.OrderBy(item => item.LayerId, StringComparer.Ordinal).ToArray(),
            SnapshotSha256: string.Empty);
        var snapshot = withoutHash with
        {
            SnapshotSha256 = Hash(SerializeUnchecked(withoutHash)),
        };
        Validate(snapshot);
        return snapshot;
    }

    public static string Serialize(SpaceCadMappingReplaySnapshotV1 snapshot)
    {
        Validate(snapshot);
        var json = SerializeUnchecked(snapshot);
        if (json.Length > SpaceCadMappingReplaySnapshotVersions.MaximumSerializedLength)
            throw new InvalidDataException("The CAD mapping replay snapshot is too large.");
        return json;
    }

    public static SpaceCadMappingReplaySnapshotV1 Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            json.Length > SpaceCadMappingReplaySnapshotVersions.MaximumSerializedLength)
        {
            throw new InvalidDataException(
                "The CAD mapping replay snapshot is missing or too large.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            RejectDuplicateProperties(document.RootElement, "$", 0);
            var snapshot = document.RootElement.Deserialize<
                               SpaceCadMappingReplaySnapshotV1>(JsonOptions)
                           ?? throw new JsonException();
            Validate(snapshot);
            if (!SerializeUnchecked(snapshot).Equals(json, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The CAD mapping replay snapshot is not canonical.");
            }
            return snapshot;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The CAD mapping replay snapshot is not valid JSON.",
                exception);
        }
    }

    public static void Validate(SpaceCadMappingReplaySnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.LayerOverrides);
        if (snapshot.SchemaVersion !=
                SpaceCadMappingReplaySnapshotVersions.SchemaVersion ||
            snapshot.TenantId == Guid.Empty ||
            snapshot.ProfileId == Guid.Empty ||
            snapshot.ProfileVersion <= 0 ||
            !IsSha256(snapshot.ProfileDefinitionSha256) ||
            !IsSha256(snapshot.SourceSha256) ||
            !IsSha256(snapshot.ExpectedInventorySha256) ||
            !IsSha256(snapshot.ExpectedSourceStructureSha256) ||
            !IsSha256(snapshot.ExpectedMappingPreviewSha256) ||
            !IsSha256(snapshot.SnapshotSha256) ||
            snapshot.LayerOverrides.Count > SpaceCadMappingVersions.MaximumOverrides)
        {
            throw new InvalidDataException(
                "The CAD mapping replay snapshot identity is incomplete.");
        }

        if (snapshot.LayerOverrides.Any(item => item is null))
        {
            throw new InvalidDataException(
                "The CAD mapping replay overrides contain a null item.");
        }

        if (SerializeUnchecked(snapshot).Length >
            SpaceCadMappingReplaySnapshotVersions.MaximumSerializedLength)
        {
            throw new InvalidDataException(
                "The CAD mapping replay snapshot is too large.");
        }

        if (!snapshot.LayerOverrides.SequenceEqual(
                snapshot.LayerOverrides.OrderBy(
                    item => item.LayerId,
                    StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                "The CAD mapping replay overrides are not canonical.");
        }

        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot.LayerOverrides)
        {
            SpaceCadMapping.ValidateLayerOverride(item);
            if (!layers.Add(item.LayerId))
            {
                throw new InvalidDataException(
                    "The CAD mapping replay overrides contain an invalid layer identity.");
            }
        }

        var expected = Hash(SerializeUnchecked(
            snapshot with { SnapshotSha256 = string.Empty }));
        if (!snapshot.SnapshotSha256.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD mapping replay snapshot hash is invalid.");
        }
    }

    public static void ValidateReplay(
        SpaceCadMappingReplaySnapshotV1 snapshot,
        SpaceCadMappingPreviewV1 replayedPreview)
    {
        Validate(snapshot);
        SpaceCadMapping.ValidatePreview(replayedPreview);
        if (replayedPreview.TenantId != snapshot.TenantId ||
            replayedPreview.ProfileId != snapshot.ProfileId ||
            replayedPreview.ProfileVersion != snapshot.ProfileVersion ||
            !replayedPreview.ProfileDefinitionSha256.Equals(
                snapshot.ProfileDefinitionSha256,
                StringComparison.Ordinal) ||
            !replayedPreview.SourceSha256.Equals(
                snapshot.SourceSha256,
                StringComparison.Ordinal) ||
            !replayedPreview.InventorySha256.Equals(
                snapshot.ExpectedInventorySha256,
                StringComparison.Ordinal) ||
            !replayedPreview.SourceStructureSha256.Equals(
                snapshot.ExpectedSourceStructureSha256,
                StringComparison.Ordinal) ||
            !replayedPreview.PreviewSha256.Equals(
                snapshot.ExpectedMappingPreviewSha256,
                StringComparison.Ordinal) ||
            !replayedPreview.LayerOverrides.SequenceEqual(snapshot.LayerOverrides))
        {
            throw new InvalidDataException(
                "The replayed CAD mapping does not match its sealed preparation snapshot.");
        }
    }

    private static string SerializeUnchecked(
        SpaceCadMappingReplaySnapshotV1 snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RejectDuplicateProperties(
        JsonElement element,
        string path,
        int depth)
    {
        if (depth > 64)
            throw new InvalidDataException("The CAD mapping replay snapshot is too deep.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"Duplicate CAD mapping replay property '{property.Name}' at {path}.");
                }
                RejectDuplicateProperties(
                    property.Value,
                    $"{path}.{property.Name}",
                    depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{path}[{index}]", depth + 1);
                index++;
            }
        }
    }
}
