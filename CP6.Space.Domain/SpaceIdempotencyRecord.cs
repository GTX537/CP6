namespace CP6.Space.Domain;

public sealed class SpaceIdempotencyRecord : SpaceTenantEntity
{
    private SpaceIdempotencyRecord()
    {
    }

    public Guid PrincipalId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKeyHash { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ResponseJson { get; private set; } = "{}";
    public int HttpStatusCode { get; private set; }
    public DateTime ReplayUntilUtc { get; private set; }
    public DateTime RetainUntilUtc { get; private set; }

    public static SpaceIdempotencyRecord Create(
        Guid tenantId,
        Guid principalId,
        string operation,
        string idempotencyKeyHash,
        string requestHash,
        string responseJson,
        int httpStatusCode,
        DateTime replayUntilUtc,
        DateTime retainUntilUtc)
    {
        if (principalId == Guid.Empty)
            throw new ArgumentException("Principal is required.", nameof(principalId));
        if (httpStatusCode is < 200 or > 299)
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
        RequireUtc(replayUntilUtc, nameof(replayUntilUtc));
        RequireUtc(retainUntilUtc, nameof(retainUntilUtc));
        if (retainUntilUtc < replayUntilUtc)
            throw new ArgumentException("Retention cannot end before replay.");

        var record = new SpaceIdempotencyRecord
        {
            PrincipalId = principalId,
            Operation = RequireText(operation, 100, nameof(operation)),
            IdempotencyKeyHash = RequireHash(
                idempotencyKeyHash,
                nameof(idempotencyKeyHash)),
            RequestHash = RequireHash(requestHash, nameof(requestHash)),
            ResponseJson = RequireJson(responseJson),
            HttpStatusCode = httpStatusCode,
            ReplayUntilUtc = replayUntilUtc,
            RetainUntilUtc = retainUntilUtc,
        };
        record.SetTenant(tenantId);
        return record;
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hex value is required.", parameterName);
        return value.ToLowerInvariant();
    }

    private static string RequireJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Response JSON is required.", nameof(value));
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("Response JSON is invalid.", nameof(value), exception);
        }
        return value;
    }

    private static string RequireText(
        string value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}
