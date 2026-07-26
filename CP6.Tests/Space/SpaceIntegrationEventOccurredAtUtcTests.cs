using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Integration;
using CP6.WebApi.BackgroundServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Space;

public sealed class SpaceIntegrationEventUtcNormalizerTests
{
    private static readonly Guid EventId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void New_distinct_job_uses_same_ticks_as_utc()
    {
        var value = new DateTime(
            2026,
            7,
            25,
            12,
            34,
            56,
            DateTimeKind.Unspecified);

        var result = SpaceIntegrationEventUtcNormalizer.Normalize(
            value,
            EventId,
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222"),
            null);

        Assert.Equal(value.Ticks, result.Utc.Ticks);
        Assert.Equal(DateTimeKind.Utc, result.Utc.Kind);
        Assert.Equal(
            SpaceUtcNormalizationResolution.NewUtcTicks,
            result.Resolution);
    }

    [Fact]
    public void Legacy_ambiguous_time_chooses_later_utc_occurrence()
    {
        var zone = CreateDstZone();
        var ambiguous = new DateTime(
            2026,
            11,
            1,
            1,
            30,
            0,
            DateTimeKind.Unspecified);

        var result = SpaceIntegrationEventUtcNormalizer.Normalize(
            ambiguous,
            EventId,
            EventId,
            zone);

        Assert.Equal(
            new DateTime(
                2026,
                11,
                1,
                6,
                30,
                0,
                DateTimeKind.Utc),
            result.Utc);
        Assert.Equal(
            SpaceUtcNormalizationResolution
                .AmbiguousLaterOccurrence,
            result.Resolution);
    }

    [Fact]
    public void Legacy_invalid_time_shifts_to_first_valid_instant()
    {
        var zone = CreateDstZone();
        var invalid = new DateTime(
            2026,
            3,
            8,
            2,
            30,
            0,
            DateTimeKind.Unspecified);

        var result = SpaceIntegrationEventUtcNormalizer.Normalize(
            invalid,
            EventId,
            null,
            zone);

        Assert.Equal(
            new DateTime(
                2026,
                3,
                8,
                7,
                0,
                0,
                DateTimeKind.Utc),
            result.Utc);
        Assert.Equal(
            SpaceUtcNormalizationResolution.InvalidShiftedForward,
            result.Resolution);
    }

    [Fact]
    public void Extreme_values_saturate_without_throwing()
    {
        var plusFourteen = TimeZoneInfo.CreateCustomTimeZone(
            "Test/Plus14",
            TimeSpan.FromHours(14),
            "Test/Plus14",
            "Test/Plus14");
        var minusFourteen = TimeZoneInfo.CreateCustomTimeZone(
            "Test/Minus14",
            TimeSpan.FromHours(-14),
            "Test/Minus14",
            "Test/Minus14");

        var minimum = SpaceIntegrationEventUtcNormalizer.Normalize(
            DateTime.SpecifyKind(
                DateTime.MinValue,
                DateTimeKind.Unspecified),
            EventId,
            EventId,
            plusFourteen);
        var maximum = SpaceIntegrationEventUtcNormalizer.Normalize(
            DateTime.SpecifyKind(
                DateTime.MaxValue,
                DateTimeKind.Unspecified),
            EventId,
            null,
            minusFourteen);

        Assert.Equal(DateTime.MinValue.Ticks, minimum.Utc.Ticks);
        Assert.Equal(DateTimeKind.Utc, minimum.Utc.Kind);
        Assert.Equal(
            SpaceUtcNormalizationResolution.SaturatedMinimum,
            minimum.Resolution);
        Assert.Equal(DateTime.MaxValue.Ticks, maximum.Utc.Ticks);
        Assert.Equal(DateTimeKind.Utc, maximum.Utc.Kind);
        Assert.Equal(
            SpaceUtcNormalizationResolution.SaturatedMaximum,
            maximum.Resolution);
    }

    [Theory]
    [InlineData(null, "SPACE_LEGACY_TIME_ZONE_REQUIRED")]
    [InlineData("", "SPACE_LEGACY_TIME_ZONE_REQUIRED")]
    [InlineData(
        "Definitely/Not/A/TimeZone",
        "SPACE_LEGACY_TIME_ZONE_INVALID")]
    public void Explicit_legacy_time_zone_is_fail_closed(
        string? id,
        string expectedCode)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SpaceIntegrationEventUtcNormalizer
                .ResolveRequiredTimeZone(id));

        Assert.Equal(expectedCode, error.Message);
    }

    private static TimeZoneInfo CreateDstZone()
    {
        var daylightStart =
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                3,
                2,
                DayOfWeek.Sunday);
        var daylightEnd =
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                11,
                1,
                DayOfWeek.Sunday);
        var adjustment =
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2020, 1, 1),
                new DateTime(2030, 12, 31),
                TimeSpan.FromHours(1),
                daylightStart,
                daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/Eastern",
            TimeSpan.FromHours(-5),
            "Test/Eastern",
            "Test/Eastern",
            "Test/Eastern Daylight",
            [adjustment]);
    }
}

public sealed class SpaceIntegrationEventOccurredAtUtcBackfillTests
{
    [Fact]
    public async Task Fresh_database_does_not_require_legacy_time_zone()
    {
        await using var db = NewDb();

        await SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
            db,
            new SpaceObservabilityOptions(),
            NullLogger.Instance);
    }

    [Fact]
    public async Task Pending_space_row_requires_explicit_time_zone()
    {
        await using var db = NewDb();
        db.IntegrationEvents.Add(NewEvent("SPACE"));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
                db,
                new SpaceObservabilityOptions(),
                NullLogger.Instance));

        Assert.Equal(
            "SPACE_LEGACY_TIME_ZONE_REQUIRED",
            error.Message);
        Assert.Null(
            (await db.IntegrationEvents.SingleAsync())
            .OccurredAtUtc);
    }

    [Fact]
    public async Task Backfill_crosses_batch_boundary_is_idempotent_and_space_only()
    {
        await using var db = NewDb();
        var expected = new DateTime(
            2026,
            7,
            25,
            12,
            0,
            0,
            DateTimeKind.Unspecified);
        for (var i = 0;
             i < SpaceIntegrationEventOccurredAtUtcBackfill.BatchSize + 1;
             i++)
        {
            db.IntegrationEvents.Add(NewEvent(
                "SPACE",
                expected.AddTicks(i)));
        }

        var nonSpace = NewEvent("ERP", expected);
        db.IntegrationEvents.Add(nonSpace);
        await db.SaveChangesAsync();
        var options = new SpaceObservabilityOptions
        {
            LegacyIntegrationEventTimeZoneId = "UTC",
        };

        await SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
            db,
            options,
            NullLogger.Instance);
        await SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
            db,
            new SpaceObservabilityOptions(),
            NullLogger.Instance);

        var spaceRows = await db.IntegrationEvents
            .IgnoreQueryFilters()
            .Where(x => x.SourceModule == "SPACE")
            .ToListAsync();
        Assert.Equal(
            SpaceIntegrationEventOccurredAtUtcBackfill.BatchSize + 1,
            spaceRows.Count);
        Assert.All(spaceRows, row =>
        {
            Assert.NotNull(row.OccurredAtUtc);
            Assert.Equal(
                DateTimeKind.Utc,
                row.OccurredAtUtc.Value.Kind);
            Assert.Equal(row.CreateDate.Ticks, row.OccurredAtUtc.Value.Ticks);
        });
        Assert.Null(
            (await db.IntegrationEvents
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == nonSpace.Id))
            .OccurredAtUtc);
    }

    [Fact]
    public void Sql_server_app_lock_command_is_stable_and_exclusive()
    {
        using var command = new SqlCommand();

        SpaceIntegrationEventOccurredAtUtcBackfill
            .ConfigureAppLockCommand(
                command,
                SpaceIntegrationEventOccurredAtUtcBackfill
                    .AcquireLockCommandText,
                30_000);

        Assert.Contains(
            "sys.sp_getapplock",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Contains(
            "@LockMode = N'Exclusive'",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Contains(
            "@LockOwner = N'Session'",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Equal(
            SpaceIntegrationEventOccurredAtUtcBackfill.LockResource,
            command.Parameters["@resource"].Value);
        Assert.Equal(
            30_000,
            command.Parameters["@timeoutMilliseconds"].Value);
    }

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CP6Context(
            options,
            new TenantContext
            {
                CurrentTenantId =
                    Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"),
            });
    }

    private static IntegrationEvent NewEvent(
        string sourceModule,
        DateTime? createDate = null)
    {
        var id = Guid.NewGuid();
        return new IntegrationEvent
        {
            Id = id,
            SourceModule = sourceModule,
            TargetModule = "WMS",
            HookName = "SpaceBridgeHook.OnLocationPublishedAsync",
            SourceNo = $"ROW-{id:N}",
            Status = IntegrationEventStatus.Failed,
            Attempts = 1,
            CorrelationId = Guid.NewGuid(),
            JobId = id,
            PublishAttemptId = Guid.NewGuid(),
            PayloadJson = "{}",
            Creator = "test",
            CreateDate = createDate ??
                new DateTime(
                    2026,
                    7,
                    25,
                    12,
                    0,
                    0,
                    DateTimeKind.Unspecified),
        };
    }
}
