using System.Security.Cryptography;
using System.Text;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpacePublishWarningAcknowledgement
{
    private const string SchemaVersion = "space-publish-warning-ack-v1";

    public static string? ComputeBoundHash(
        Guid validationRunId,
        int expectedWarningCount,
        IEnumerable<Guid> warningIssueIds)
    {
        if (validationRunId == Guid.Empty)
            throw new ArgumentException(
                "Validation run is required.",
                nameof(validationRunId));
        if (expectedWarningCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedWarningCount));
        ArgumentNullException.ThrowIfNull(warningIssueIds);

        var ids = warningIssueIds
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (ids.Length != expectedWarningCount)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ValidationStale,
                409,
                "The validation warning evidence is stale.",
                "The persisted Warning issue set does not match the " +
                "ValidationRun summary.",
                "run-validation");
        }
        if (ids.Length == 0)
            return null;

        var payload = string.Join(
            "\n",
            new[]
            {
                SchemaVersion,
                validationRunId.ToString("D"),
            }.Concat(ids.Select(value => value.ToString("D"))));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    public static void EnsureConfirmed(
        Guid validationRunId,
        int expectedWarningCount,
        IEnumerable<Guid> warningIssueIds,
        string? suppliedHash)
    {
        var expectedHash = ComputeBoundHash(
            validationRunId,
            expectedWarningCount,
            warningIssueIds);
        if (expectedHash is null)
            return;

        var normalized = suppliedHash?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PublishWarningAcknowledgementRequired,
                422,
                "Validation Warnings require explicit confirmation.",
                "Review every Warning in the publish preview and submit " +
                "the bound acknowledgement hash.",
                "confirm-publish-warnings");
        }
        if (!string.Equals(
                normalized,
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ValidationStale,
                409,
                "The validation warning acknowledgement is stale.",
                "The acknowledged Warning set no longer matches the " +
                "selected ValidationRun.",
                "refresh-publish-preview");
        }
    }
}
