namespace CP6.Core.Services.Space.Observability;

internal enum SpaceUtcNormalizationResolution
{
    AlreadyUtc,
    LocalKind,
    NewUtcTicks,
    LegacyWallClock,
    AmbiguousLaterOccurrence,
    InvalidShiftedForward,
    SaturatedMinimum,
    SaturatedMaximum,
}

internal readonly record struct SpaceUtcNormalizationResult(
    DateTime Utc,
    SpaceUtcNormalizationResolution Resolution);

/// <summary>
/// Converts the mixed historical T_IntegrationEvent CreateDate contract into
/// one deterministic UTC value without relying on DateTime conversion APIs
/// that can throw for DST gaps or values at the DateTime limits.
/// </summary>
internal static class SpaceIntegrationEventUtcNormalizer
{
    private const string TimeZoneRequiredCode =
        "SPACE_LEGACY_TIME_ZONE_REQUIRED";
    private const string TimeZoneInvalidCode =
        "SPACE_LEGACY_TIME_ZONE_INVALID";
    private static readonly long SearchWindowTicks =
        TimeSpan.FromHours(48).Ticks;
    private static readonly long SearchStepTicks =
        TimeSpan.FromMinutes(1).Ticks;

    public static TimeZoneInfo ResolveRequiredTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new InvalidOperationException(TimeZoneRequiredCode);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex)
            when (ex is TimeZoneNotFoundException or
                  InvalidTimeZoneException or
                  ArgumentException)
        {
            throw new InvalidOperationException(TimeZoneInvalidCode);
        }
    }

    public static SpaceUtcNormalizationResult Normalize(
        DateTime value,
        Guid eventId,
        Guid? jobId,
        TimeZoneInfo? legacyTimeZone)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return new SpaceUtcNormalizationResult(
                value,
                SpaceUtcNormalizationResolution.AlreadyUtc);
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return NormalizeWallClock(
                DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Unspecified),
                TimeZoneInfo.Local,
                SpaceUtcNormalizationResolution.LocalKind);
        }

        if (jobId.HasValue &&
            jobId.Value != Guid.Empty &&
            jobId.Value != eventId)
        {
            return new SpaceUtcNormalizationResult(
                DateTime.SpecifyKind(value, DateTimeKind.Utc),
                SpaceUtcNormalizationResolution.NewUtcTicks);
        }

        if (legacyTimeZone is null)
            throw new InvalidOperationException(TimeZoneRequiredCode);

        return NormalizeWallClock(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            legacyTimeZone,
            SpaceUtcNormalizationResolution.LegacyWallClock);
    }

    private static SpaceUtcNormalizationResult NormalizeWallClock(
        DateTime wallClock,
        TimeZoneInfo timeZone,
        SpaceUtcNormalizationResolution normalResolution)
    {
        var adjusted = wallClock;
        var resolution = normalResolution;

        if (IsInvalidTime(timeZone, adjusted))
        {
            if (TryFindFirstValidTimeAfter(
                    timeZone,
                    adjusted,
                    out var firstValid))
            {
                adjusted = firstValid;
            }

            resolution =
                SpaceUtcNormalizationResolution.InvalidShiftedForward;
        }

        TimeSpan offset;
        if (IsAmbiguousTime(timeZone, adjusted))
        {
            offset = GetAmbiguousOffsets(timeZone, adjusted)
                .DefaultIfEmpty(timeZone.BaseUtcOffset)
                .Min();
            if (resolution !=
                SpaceUtcNormalizationResolution.InvalidShiftedForward)
            {
                resolution =
                    SpaceUtcNormalizationResolution
                        .AmbiguousLaterOccurrence;
            }
        }
        else
        {
            offset = GetUtcOffset(timeZone, adjusted);
        }

        var utcTicks = adjusted.Ticks - offset.Ticks;
        if (utcTicks <= DateTime.MinValue.Ticks)
        {
            return new SpaceUtcNormalizationResult(
                DateTime.SpecifyKind(
                    DateTime.MinValue,
                    DateTimeKind.Utc),
                SpaceUtcNormalizationResolution.SaturatedMinimum);
        }

        if (utcTicks >= DateTime.MaxValue.Ticks)
        {
            return new SpaceUtcNormalizationResult(
                DateTime.SpecifyKind(
                    DateTime.MaxValue,
                    DateTimeKind.Utc),
                SpaceUtcNormalizationResolution.SaturatedMaximum);
        }

        return new SpaceUtcNormalizationResult(
            new DateTime(utcTicks, DateTimeKind.Utc),
            resolution);
    }

    private static bool TryFindFirstValidTimeAfter(
        TimeZoneInfo timeZone,
        DateTime invalid,
        out DateTime firstValid)
    {
        var searchLimit = Math.Min(
            DateTime.MaxValue.Ticks,
            invalid.Ticks + Math.Min(
                SearchWindowTicks,
                DateTime.MaxValue.Ticks - invalid.Ticks));
        var invalidTicks = invalid.Ticks;
        var candidateTicks = invalidTicks;

        while (candidateTicks < searchLimit)
        {
            candidateTicks = Math.Min(
                searchLimit,
                candidateTicks + Math.Min(
                    SearchStepTicks,
                    searchLimit - candidateTicks));
            var candidate = new DateTime(
                candidateTicks,
                DateTimeKind.Unspecified);
            if (IsInvalidTime(timeZone, candidate))
                continue;

            var low = invalidTicks;
            var high = candidateTicks;
            while (high - low > 1)
            {
                var middle = low + ((high - low) / 2);
                var middleValue = new DateTime(
                    middle,
                    DateTimeKind.Unspecified);
                if (IsInvalidTime(timeZone, middleValue))
                    low = middle;
                else
                    high = middle;
            }

            firstValid = new DateTime(
                high,
                DateTimeKind.Unspecified);
            return true;
        }

        firstValid = invalid;
        return false;
    }

    private static bool IsInvalidTime(
        TimeZoneInfo timeZone,
        DateTime value)
    {
        try
        {
            return timeZone.IsInvalidTime(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsAmbiguousTime(
        TimeZoneInfo timeZone,
        DateTime value)
    {
        try
        {
            return timeZone.IsAmbiguousTime(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<TimeSpan> GetAmbiguousOffsets(
        TimeZoneInfo timeZone,
        DateTime value)
    {
        try
        {
            return timeZone.GetAmbiguousTimeOffsets(value);
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private static TimeSpan GetUtcOffset(
        TimeZoneInfo timeZone,
        DateTime value)
    {
        try
        {
            return timeZone.GetUtcOffset(value);
        }
        catch (ArgumentException)
        {
            return timeZone.BaseUtcOffset;
        }
    }
}
