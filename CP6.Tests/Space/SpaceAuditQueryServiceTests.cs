using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Space;

public sealed class SpaceAuditQueryServiceTests : IDisposable
{
    private static readonly Guid TenantA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Correlation =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly CP6Context _db;
    private readonly ISpaceAuditQueryService _service;

    public SpaceAuditQueryServiceTests()
    {
        var tenant = new TenantContext { CurrentTenantId = TenantA };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CP6Context(options, tenant);
        _service = new SpaceAuditQueryService(_db);
    }

    [Fact]
    public async Task Query_is_tenant_scoped_no_tracking_fixed_order_and_typed_evidence()
    {
        var now = DateTime.UtcNow;
        SeedAudit(
            TenantA,
            Correlation,
            now.AddMinutes(-2),
            action: "space.floor.publish",
            evidence:
                """
                {
                  "permissionCode":"space-audit:read",
                  "authorizationResult":"Allowed",
                  "itemCount":7,
                  "payloadJson":"must-be-ignored",
                  "extra":{"secret":"not-a-dto-field"}
                }
                """);
        SeedAudit(
            TenantA,
            Correlation,
            now.AddMinutes(-1),
            action: "space.location.deactivate");
        SeedAudit(
            TenantB,
            Correlation,
            now,
            action: "space.cross-tenant.secret");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var page = await _service.QueryAsync(new SpaceAuditQueryDto(
            FromUtc: now.AddHours(-1),
            ToUtc: now.AddHours(1),
            Page: 0,
            PageSize: 999));

        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(2, page.Total);
        Assert.Equal(
            new[] { "space.location.deactivate", "space.floor.publish" },
            page.Items.Select(x => x.Action));
        Assert.All(page.Items, x => Assert.Equal(TenantA, x.TenantId));
        var evidence = Assert.Single(
            page.Items,
            x => x.AuthorizationEvidence is not null)
            .AuthorizationEvidence!;
        Assert.Equal("space-audit:read", evidence.PermissionCode);
        Assert.Equal("Allowed", evidence.AuthorizationResult);
        Assert.Equal(7, evidence.ItemCount);
        Assert.DoesNotContain(
            "must-be-ignored",
            JsonSerializer.Serialize(page),
            StringComparison.Ordinal);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Query_malformed_evidence_degrades_to_null_without_raw_json()
    {
        var now = DateTime.UtcNow;
        SeedAudit(
            TenantA,
            Correlation,
            now,
            evidence: """{"permissionCode":"secret-token",malformed""");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var page = await _service.QueryAsync(new SpaceAuditQueryDto(
            FromUtc: now.AddMinutes(-1),
            ToUtc: now.AddMinutes(1)));

        var item = Assert.Single(page.Items);
        Assert.Null(item.AuthorizationEvidence);
        Assert.DoesNotContain(
            "secret-token",
            JsonSerializer.Serialize(item),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_oversized_and_deep_evidence_degrade_to_null()
    {
        var now = DateTime.UtcNow;
        var oversized =
            """{"permissionCode":"oversized-secret"}""" +
            new string(' ', 8193);
        var deep = """{"permissionCode":"deep-secret","extra":""" +
                   string.Concat(
                       Enumerable.Repeat(
                           """{"nested":""",
                           9)) +
                   "null" +
                   new string('}', 9) +
                   "}";
        SeedAudit(
            TenantA,
            Correlation,
            now.AddSeconds(-1),
            resourceId: "oversized",
            evidence: oversized);
        SeedAudit(
            TenantA,
            Correlation,
            now,
            resourceId: "deep",
            evidence: deep);
        await _db.SaveChangesAsync();

        var page = await _service.QueryAsync(new SpaceAuditQueryDto(
            FromUtc: now.AddMinutes(-1),
            ToUtc: now.AddMinutes(1)));

        Assert.Equal(2, page.Items.Count);
        Assert.All(
            page.Items,
            item => Assert.Null(item.AuthorizationEvidence));
        var serialized = JsonSerializer.Serialize(page);
        Assert.DoesNotContain(
            "oversized-secret",
            serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "deep-secret",
            serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_server_projection_gates_evidence_with_datalength_case()
    {
        var tenant = new TenantContext
        {
            CurrentTenantId = TenantA,
        };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=cp6-query-sql-oracle;Trusted_Connection=True")
            .Options;
        using var sqlDb = new CP6Context(options, tenant);
        var service = new SpaceAuditQueryService(sqlDb);

        var sql = service.BuildAuditPageRowsQuery(
                sqlDb.SpaceAuditEvents
                    .AsNoTracking()
                    .Where(x => x.CorrelationId == Correlation),
                0,
                50)
            .ToQueryString();

        Assert.Contains("DATALENGTH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("16384", sql, StringComparison.Ordinal);
        Assert.Contains("TenantId", sql, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", sql, StringComparison.Ordinal);

        var integrationTimelineSql = service
            .BuildIntegrationTimelineRowsQuery(Correlation)
            .ToQueryString();
        var auditTimelineSql = service
            .BuildAuditTimelineRowsQuery(Correlation)
            .ToQueryString();
        Assert.Contains(
            "1001",
            integrationTimelineSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "OccurredAtUtc",
            integrationTimelineSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY",
            integrationTimelineSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "DESC",
            integrationTimelineSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "1001",
            auditTimelineSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_defaults_to_last_24_hours()
    {
        var now = DateTime.UtcNow;
        SeedAudit(TenantA, Correlation, now.AddHours(-23), resourceId: "recent");
        SeedAudit(TenantA, Correlation, now.AddHours(-25), resourceId: "old");
        await _db.SaveChangesAsync();

        var page = await _service.QueryAsync(new SpaceAuditQueryDto());

        Assert.Equal("recent", Assert.Single(page.Items).ResourceId);
    }

    [Fact]
    public async Task Query_accepts_exact_31_day_window_and_rejects_invalid_boundaries()
    {
        var to = DateTime.UtcNow;
        var exact = await _service.QueryAsync(new SpaceAuditQueryDto(
            FromUtc: to.AddDays(-31),
            ToUtc: to));
        Assert.Empty(exact.Items);

        await AssertRangeInvalid(new SpaceAuditQueryDto(
            FromUtc: to.AddDays(-31).AddTicks(-1),
            ToUtc: to));
        await AssertRangeInvalid(new SpaceAuditQueryDto(
            FromUtc: to,
            ToUtc: to.AddTicks(-1)));
        await AssertRangeInvalid(new SpaceAuditQueryDto(
            FromUtc: DateTime.SpecifyKind(to.AddHours(-1), DateTimeKind.Unspecified),
            ToUtc: to));
        await AssertRangeInvalid(new SpaceAuditQueryDto(
            FromUtc: to.AddHours(-1),
            ToUtc: DateTime.SpecifyKind(to, DateTimeKind.Local)));
        var correlationError = await Assert.ThrowsAsync<BizException>(
            () => _service.QueryAsync(
                new SpaceAuditQueryDto(CorrelationId: Guid.Empty)));
        Assert.Equal("SPACE_CORRELATION_ID_INVALID", correlationError.Code);
    }

    [Fact]
    public async Task Query_missing_from_rejects_underflow_and_accepts_exact_24_hour_boundary()
    {
        var minUtc = DateTime.SpecifyKind(
            DateTime.MinValue,
            DateTimeKind.Utc);
        var exactBoundary = new DateTime(
            DateTime.MinValue.Ticks + TimeSpan.TicksPerDay,
            DateTimeKind.Utc);

        await AssertRangeInvalid(new SpaceAuditQueryDto(ToUtc: minUtc));
        await AssertRangeInvalid(new SpaceAuditQueryDto(
            ToUtc: exactBoundary.AddTicks(-1)));

        var allowed = await _service.QueryAsync(
            new SpaceAuditQueryDto(ToUtc: exactBoundary));

        Assert.Empty(allowed.Items);
    }

    [Fact]
    public async Task Timeline_returns_only_current_tenant_and_space_integration_events()
    {
        var now = DateTime.UtcNow;
        SeedAudit(
            TenantA,
            Correlation,
            now.AddMinutes(-3),
            action: "space.floor.publish",
            outcome: "Succeeded");
        SeedAudit(
            TenantB,
            Correlation,
            now.AddMinutes(-2),
            action: "space.floor.publish",
            outcome: "Failed");
        SeedIntegration(
            TenantA,
            Correlation,
            now.AddMinutes(-1),
            source: "SPACE",
            lastError: "SPACE_ADAPTER_FAILURE:InvalidOperationException:ABC");
        SeedIntegration(
            TenantA,
            Correlation,
            now,
            source: "ERP",
            lastError: "SPACE_SHOULD_NOT_APPEAR");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var rows = await _service.GetTimelineAsync(Correlation);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Kind == "Audit");
        var integration = Assert.Single(
            rows,
            x => x.Kind == "IntegrationEvent");
        Assert.Equal("SPACE_ADAPTER_FAILURE", integration.SafeErrorCode);
        Assert.All(rows, x => Assert.Equal(TenantA, x.TenantId));
        Assert.DoesNotContain(
            "InvalidOperationException",
            JsonSerializer.Serialize(rows),
            StringComparison.Ordinal);
        Assert.DoesNotContain(rows, x => x.ResourceType == "ERP");
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Timeline_uses_stable_safe_error_or_legacy_redaction()
    {
        var now = DateTime.UtcNow;
        SeedAudit(
            TenantA,
            Correlation,
            now.AddMinutes(-4),
            resourceId: "audit-legacy",
            reasonCode: "raw audit secret");
        SeedIntegration(
            TenantA,
            Correlation,
            now.AddMinutes(-3),
            lastError: "SPACE_WMS_TIMEOUT:TimeoutException:FINGERPRINT",
            sourceNo: "stable");
        SeedIntegration(
            TenantA,
            Correlation,
            now.AddMinutes(-2),
            lastError: "raw sql password=secret",
            sourceNo: "legacy");
        SeedIntegration(
            TenantA,
            Correlation,
            now.AddMinutes(-1),
            lastError: "SPACE_BAD CODE:secret",
            sourceNo: "invalid");
        SeedIntegration(
            TenantA,
            Correlation,
            now,
            lastError: null,
            sourceNo: "none");
        await _db.SaveChangesAsync();

        var rows = await _service.GetTimelineAsync(Correlation);

        Assert.Equal(
            "SPACE_WMS_TIMEOUT",
            rows.Single(x => x.ResourceId == "stable").SafeErrorCode);
        Assert.Equal(
            "SPACE_LEGACY_ERROR_REDACTED",
            rows.Single(x => x.ResourceId == "legacy").SafeErrorCode);
        Assert.Equal(
            "SPACE_LEGACY_ERROR_REDACTED",
            rows.Single(x => x.ResourceId == "invalid").SafeErrorCode);
        Assert.Null(rows.Single(x => x.ResourceId == "none").SafeErrorCode);
        Assert.Equal(
            "SPACE_LEGACY_ERROR_REDACTED",
            rows.Single(x => x.ResourceId == "audit-legacy").SafeErrorCode);
        var serialized = JsonSerializer.Serialize(rows);
        Assert.DoesNotContain("password=secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SPACE_BAD CODE", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("raw audit secret", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeline_is_bounded_to_latest_thousand_and_stably_ascending()
    {
        var start = new DateTime(
            2026,
            7,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        for (var i = 0; i < 1005; i++)
        {
            SeedAudit(
                TenantA,
                Correlation,
                start.AddSeconds(i * 2),
                action: $"audit-{i:D4}",
                resourceId: $"audit-{i:D4}");
            SeedIntegration(
                TenantA,
                Correlation,
                start.AddSeconds((i * 2) + 1),
                sourceNo: $"integration-{i:D4}");
        }

        SeedIntegration(
            TenantA,
            Correlation,
            start.AddDays(1),
            source: "ERP",
            sourceNo: "erp-newest-must-not-appear");
        SeedIntegration(
            TenantB,
            Correlation,
            start.AddDays(2),
            sourceNo: "other-tenant-must-not-appear");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var rows = await _service.GetTimelineAsync(Correlation);

        Assert.Equal(1000, rows.Count);
        Assert.Equal(
            start.AddSeconds(1010),
            rows[0].OccurredAtUtc);
        Assert.Equal(
            start.AddSeconds(2009),
            rows[^1].OccurredAtUtc);
        Assert.Equal(
            rows
                .OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.Kind, StringComparer.Ordinal)
                .Select(x => x.OccurredAtUtc),
            rows.Select(x => x.OccurredAtUtc));
        Assert.DoesNotContain(
            rows,
            x => x.ResourceId == "erp-newest-must-not-appear");
        Assert.DoesNotContain(
            rows,
            x => x.ResourceId == "other-tenant-must-not-appear");
    }

    [Fact]
    public async Task Timeline_and_publish_normalize_new_legacy_local_and_utc_create_dates()
    {
        var utc = new DateTime(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);
        var newUnspecified = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 13, 0, 0),
            DateTimeKind.Unspecified);
        var legacyUnspecified = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 14, 0, 0),
            DateTimeKind.Unspecified);
        var local = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 15, 0, 0),
            DateTimeKind.Local);
        var backfillUnspecified = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 16, 0, 0),
            DateTimeKind.Unspecified);
        var publishOnlyUnspecified = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 17, 0, 0),
            DateTimeKind.Unspecified);
        var emptyJobUnspecified = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 18, 0, 0),
            DateTimeKind.Unspecified);
        var newEventId =
            Guid.Parse("66666666-6666-6666-6666-666666666666");
        var backfillEventId =
            Guid.Parse("77777777-7777-7777-7777-777777777777");
        var publishOnlyEventId =
            Guid.Parse("88888888-8888-8888-8888-888888888888");
        var emptyJobEventId =
            Guid.Parse("99999999-9999-9999-9999-999999999999");
        var jobId =
            Guid.Parse("44444444-4444-4444-4444-444444444444");
        var publishAttemptId =
            Guid.Parse("55555555-5555-5555-5555-555555555555");
        SeedIntegration(
            TenantA,
            Correlation,
            utc,
            sourceNo: "utc");
        SeedIntegration(
            TenantA,
            Correlation,
            newUnspecified,
            sourceNo: "new-unspecified",
            jobId: jobId,
            publishAttemptId: publishAttemptId,
            eventId: newEventId);
        SeedIntegration(
            TenantA,
            Correlation,
            legacyUnspecified,
            sourceNo: "legacy-unspecified");
        SeedIntegration(
            TenantA,
            Correlation,
            local,
            sourceNo: "local");
        SeedIntegration(
            TenantA,
            Correlation,
            backfillUnspecified,
            sourceNo: "worker-backfill",
            jobId: backfillEventId,
            publishAttemptId: publishAttemptId,
            eventId: backfillEventId);
        SeedIntegration(
            TenantA,
            Correlation,
            publishOnlyUnspecified,
            sourceNo: "publish-only-legacy",
            publishAttemptId: publishAttemptId,
            eventId: publishOnlyEventId);
        SeedIntegration(
            TenantA,
            Correlation,
            emptyJobUnspecified,
            sourceNo: "empty-job-legacy",
            jobId: Guid.Empty,
            publishAttemptId: publishAttemptId,
            eventId: emptyJobEventId);
        await _db.SaveChangesAsync();

        var timeline = await _service.GetTimelineAsync(Correlation);
        var published = await _service.GetPublishEventsAsync(1, 50);

        var utcRow = timeline.Single(x => x.ResourceId == "utc");
        Assert.Equal(utc, utcRow.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, utcRow.OccurredAtUtc.Kind);

        var newRow = timeline.Single(
            x => x.ResourceId == "new-unspecified");
        Assert.Equal(
            DateTime.SpecifyKind(newUnspecified, DateTimeKind.Utc),
            newRow.OccurredAtUtc);

        var legacyRow = timeline.Single(
            x => x.ResourceId == "legacy-unspecified");
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                legacyUnspecified,
                TimeZoneInfo.Local),
            legacyRow.OccurredAtUtc);

        var localRow = timeline.Single(x => x.ResourceId == "local");
        Assert.Equal(local.ToUniversalTime(), localRow.OccurredAtUtc);

        var backfillRow = timeline.Single(
            x => x.ResourceId == "worker-backfill");
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                backfillUnspecified,
                TimeZoneInfo.Local),
            backfillRow.OccurredAtUtc);

        var publishOnlyRow = timeline.Single(
            x => x.ResourceId == "publish-only-legacy");
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                publishOnlyUnspecified,
                TimeZoneInfo.Local),
            publishOnlyRow.OccurredAtUtc);

        var emptyJobRow = timeline.Single(
            x => x.ResourceId == "empty-job-legacy");
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                emptyJobUnspecified,
                TimeZoneInfo.Local),
            emptyJobRow.OccurredAtUtc);

        Assert.Equal(
            timeline.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Kind),
            timeline);
        Assert.All(
            timeline.Where(x => x.Kind == "IntegrationEvent"),
            x => Assert.Equal(DateTimeKind.Utc, x.OccurredAtUtc.Kind));

        Assert.Equal(
            DateTime.SpecifyKind(newUnspecified, DateTimeKind.Utc),
            published.Single(x => x.SourceNo == "new-unspecified").CreateDate);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                legacyUnspecified,
                TimeZoneInfo.Local),
            published.Single(x => x.SourceNo == "legacy-unspecified").CreateDate);
        Assert.Equal(
            local.ToUniversalTime(),
            published.Single(x => x.SourceNo == "local").CreateDate);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                backfillUnspecified,
                TimeZoneInfo.Local),
            published.Single(x => x.SourceNo == "worker-backfill").CreateDate);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                publishOnlyUnspecified,
                TimeZoneInfo.Local),
            published.Single(
                x => x.SourceNo == "publish-only-legacy").CreateDate);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                emptyJobUnspecified,
                TimeZoneInfo.Local),
            published.Single(
                x => x.SourceNo == "empty-job-legacy").CreateDate);
        Assert.Equal(
            utc,
            published.Single(x => x.SourceNo == "utc").CreateDate);
        Assert.All(
            published,
            x => Assert.Equal(DateTimeKind.Utc, x.CreateDate.Kind));
    }

    [Fact]
    public async Task Publish_events_are_space_only_bounded_safe_and_no_tracking()
    {
        var now = DateTime.UtcNow;
        SeedIntegration(
            TenantA,
            Correlation,
            now,
            source: "SPACE",
            lastError: "raw bearer secret",
            payloadJson: """{"token":"secret"}""");
        SeedIntegration(
            TenantA,
            Correlation,
            now.AddMinutes(1),
            source: "ERP",
            lastError: "SPACE_NOT_INCLUDED");
        SeedIntegration(
            TenantB,
            Correlation,
            now.AddMinutes(2),
            source: "SPACE",
            lastError: "SPACE_CROSS_TENANT");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var rows = await _service.GetPublishEventsAsync(
            page: int.MaxValue,
            pageSize: 999);
        Assert.Empty(rows);

        rows = await _service.GetPublishEventsAsync(page: 0, pageSize: 0);
        var row = Assert.Single(rows);
        Assert.Equal("SPACE_LEGACY_ERROR_REDACTED", row.SafeErrorCode);
        var serialized = JsonSerializer.Serialize(rows);
        Assert.DoesNotContain("raw bearer secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PayloadJson", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("LastError", serialized, StringComparison.Ordinal);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Publish_paginates_by_occurred_at_utc_not_raw_create_date()
    {
        var occurrence = new DateTime(
            2026,
            7,
            25,
            12,
            0,
            0,
            DateTimeKind.Utc);
        SeedIntegration(
            TenantA,
            Correlation,
            occurrence.AddYears(1),
            sourceNo: "raw-newer-occurrence-older",
            occurredAtUtc: occurrence);
        SeedIntegration(
            TenantA,
            Correlation,
            occurrence.AddYears(-1),
            sourceNo: "raw-older-occurrence-newer",
            occurredAtUtc: occurrence.AddMinutes(1));
        await _db.SaveChangesAsync();

        var first = await _service.GetPublishEventsAsync(1, 1);
        var second = await _service.GetPublishEventsAsync(2, 1);

        Assert.Equal(
            "raw-older-occurrence-newer",
            Assert.Single(first).SourceNo);
        Assert.Equal(
            "raw-newer-occurrence-older",
            Assert.Single(second).SourceNo);
    }

    [Fact]
    public async Task Empty_timeline_correlation_is_rejected()
    {
        var error = await Assert.ThrowsAsync<BizException>(
            () => _service.GetTimelineAsync(Guid.Empty));
        Assert.Equal("SPACE_CORRELATION_ID_INVALID", error.Code);
    }

    private async Task AssertRangeInvalid(SpaceAuditQueryDto query)
    {
        var error = await Assert.ThrowsAsync<BizException>(
            () => _service.QueryAsync(query));
        Assert.Equal("SPACE_AUDIT_QUERY_RANGE_INVALID", error.Code);
    }

    private void SeedAudit(
        Guid tenantId,
        Guid correlationId,
        DateTime occurredAtUtc,
        string action = "space.audit.test",
        string outcome = "Succeeded",
        string? resourceId = null,
        string? evidence = null,
        string? reasonCode = null)
    {
        _db.SpaceAuditEvents.Add(new Space_AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccurredAtUtc = occurredAtUtc,
            ActorType = "User",
            ActorId = "user-1",
            ActorName = "Alice",
            Action = action,
            ResourceType = "Floor",
            ResourceId = resourceId,
            Outcome = outcome,
            ReasonCode = reasonCode,
            AuthorizationEvidenceJson = evidence,
            CorrelationId = correlationId,
            TraceId = "0123456789abcdef0123456789abcdef",
        });
    }

    private void SeedIntegration(
        Guid tenantId,
        Guid correlationId,
        DateTime createDate,
        string source = "SPACE",
        string? lastError = null,
        string sourceNo = "FLOOR-1",
        string payloadJson = "{}",
        Guid? jobId = null,
        Guid? publishAttemptId = null,
        Guid? eventId = null,
        DateTime? occurredAtUtc = null)
    {
        var id = eventId ?? Guid.NewGuid();
        var inferredOccurredAtUtc = createDate.Kind switch
        {
            DateTimeKind.Utc => createDate,
            DateTimeKind.Local => createDate.ToUniversalTime(),
            _ when jobId.HasValue &&
                   jobId.Value != Guid.Empty &&
                   jobId.Value != id =>
                DateTime.SpecifyKind(createDate, DateTimeKind.Utc),
            _ => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(
                    createDate,
                    DateTimeKind.Unspecified),
                TimeZoneInfo.Local),
        };
        _db.IntegrationEvents.Add(new IntegrationEvent
        {
            Id = id,
            TenantId = tenantId,
            SourceModule = source,
            TargetModule = "WMS",
            HookName = "SpaceBridgeHook.OnLocationPublishedAsync",
            SourceNo = sourceNo,
            Status = lastError is null
                ? IntegrationEventStatus.Success
                : IntegrationEventStatus.Failed,
            Attempts = 1,
            LastError = lastError,
            CorrelationId = correlationId,
            JobId = jobId,
            PublishAttemptId = publishAttemptId,
            CreateDate = createDate,
            OccurredAtUtc = occurredAtUtc ??
                inferredOccurredAtUtc,
            PayloadJson = payloadJson,
        });
    }

    public void Dispose() => _db.Dispose();
}

public sealed class SpaceAuditControllerTests
{
    private static readonly Guid Correlation =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Disabled_returns_stable_404_without_query_or_audit()
    {
        var query = new RecordingQueryService();
        var writer = new RecordingWriter();
        var controller = CreateController(
            query,
            writer,
            auditQueryEnabled: false);

        var events = await controller.Query(
            new SpaceAuditQueryDto(),
            CancellationToken.None);
        var timeline = await controller.Timeline(
            Correlation,
            CancellationToken.None);

        AssertDisabled(events);
        AssertDisabled(timeline);
        Assert.Equal(0, query.QueryCalls);
        Assert.Equal(0, query.TimelineCalls);
        Assert.Empty(writer.Inputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_audit_failure_does_not_block_safe_query_result(
        bool writerThrows)
    {
        var item = new SpaceAuditEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "User",
            "user-1",
            "Alice",
            "space.audit.test",
            "Floor",
            null,
            "Succeeded",
            null,
            Correlation,
            "0123456789abcdef0123456789abcdef",
            null,
            null,
            null,
            null,
            null);
        var query = new RecordingQueryService
        {
            QueryResult = new SpaceAuditPageDto([item], 1, 50, 1),
        };
        var writer = new RecordingWriter
        {
            Result = false,
            Throw = writerThrows,
        };
        var controller = CreateController(query, writer);

        var result = await controller.Query(
            new SpaceAuditQueryDto(CorrelationId: Correlation),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("space.audit.test", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LastError", json, StringComparison.Ordinal);
        Assert.Equal(1, query.QueryCalls);
        var audit = Assert.Single(writer.Inputs);
        Assert.Equal("space.audit.read", audit.Action);
        Assert.Equal(SpaceAuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("space-audit:read", audit.Evidence!.PermissionCode);
        Assert.Equal("Allowed", audit.Evidence.AuthorizationResult);
        Assert.Equal(1, audit.Evidence.ItemCount);
    }

    [Fact]
    public async Task Timeline_read_audit_failure_does_not_block_safe_result()
    {
        var item = new SpaceAuditTimelineItemDto(
            "IntegrationEvent",
            Guid.NewGuid(),
            DateTime.UtcNow,
            "SpaceBridgeHook.OnLocationPublishedAsync",
            "WMS",
            "FLOOR-1",
            "FAILED",
            "SPACE_ADAPTER_FAILURE",
            Correlation,
            null,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            2);
        var query = new RecordingQueryService
        {
            TimelineResult = [item],
        };
        var writer = new RecordingWriter { Result = false };
        var controller = CreateController(query, writer);

        var result = await controller.Timeline(
            Correlation,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains(
            "SPACE_ADAPTER_FAILURE",
            JsonSerializer.Serialize(ok.Value),
            StringComparison.Ordinal);
        Assert.Equal(1, query.TimelineCalls);
        var audit = Assert.Single(writer.Inputs);
        Assert.Equal("space.audit.timeline.read", audit.Action);
        Assert.Equal(Correlation.ToString(), audit.ResourceId);
    }

    private static SpaceAuditController CreateController(
        ISpaceAuditQueryService query,
        ISpaceAuditWriter writer,
        bool auditQueryEnabled = true) =>
        new(
            query,
            writer,
            Options.Create(new SpaceObservabilityOptions
            {
                AuditQueryEnabled = auditQueryEnabled,
            }));

    private static void AssertDisabled(IActionResult result)
    {
        var disabled = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, disabled.StatusCode);
        var json = JsonSerializer.Serialize(disabled.Value);
        Assert.Contains(
            "SPACE_AUDIT_QUERY_DISABLED",
            json,
            StringComparison.Ordinal);
    }

    private sealed class RecordingQueryService : ISpaceAuditQueryService
    {
        public int QueryCalls { get; private set; }
        public int TimelineCalls { get; private set; }

        public SpaceAuditPageDto QueryResult { get; init; } =
            new([], 1, 50, 0);

        public IReadOnlyList<SpaceAuditTimelineItemDto> TimelineResult
        {
            get;
            init;
        } = [];

        public Task<SpaceAuditPageDto> QueryAsync(
            SpaceAuditQueryDto query,
            CancellationToken ct = default)
        {
            QueryCalls++;
            return Task.FromResult(QueryResult);
        }

        public Task<IReadOnlyList<SpaceAuditTimelineItemDto>>
            GetTimelineAsync(
                Guid correlationId,
                CancellationToken ct = default)
        {
            TimelineCalls++;
            return Task.FromResult(TimelineResult);
        }

        public Task<IReadOnlyList<SpacePublishEventDto>>
            GetPublishEventsAsync(
                int page,
                int pageSize,
                CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SpacePublishEventDto>>([]);
    }

    private sealed class RecordingWriter : ISpaceAuditWriter
    {
        public bool Result { get; init; } = true;
        public bool Throw { get; init; }
        public List<SpaceAuditEventInput> Inputs { get; } = [];

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            Inputs.Add(input);
            if (Throw)
                throw new InvalidOperationException(
                    "secret audit infrastructure detail");
            return Task.FromResult(Result);
        }
    }
}

public sealed class LocationPublishAuditProjectionControllerTests
{
    private static readonly Guid Correlation =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListEvents_delegates_to_safe_query_and_audit_is_fail_open(
        bool writerThrows)
    {
        var safe = new SpacePublishEventDto(
            Guid.NewGuid(),
            "SpaceBridgeHook.OnLocationPublishedAsync",
            "FLOOR-1",
            "WMS",
            "FAILED",
            2,
            DateTime.UtcNow,
            Correlation,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SPACE_ADAPTER_FAILURE");
        var query = new RecordingPublishQuery([safe]);
        var writer = new RecordingPublishWriter
        {
            Result = false,
            Throw = writerThrows,
        };
        var controller = new LocationPublishController(
            new NoOpLocationPublishService(),
            query,
            writer);

        var result = await controller.ListEvents(
            page: 3,
            pageSize: 80,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("SPACE_ADAPTER_FAILURE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LastError", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PayloadJson", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw secret", json, StringComparison.Ordinal);
        Assert.Equal((3, 80), Assert.Single(query.Requests));
        var audit = Assert.Single(writer.Inputs);
        Assert.Equal("space.integration-event.read", audit.Action);
        Assert.Equal("IntegrationEvent", audit.ResourceType);
        Assert.Equal(SpaceAuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("space-audit:read", audit.Evidence!.PermissionCode);
        Assert.Equal("Allowed", audit.Evidence.AuthorizationResult);
        Assert.Equal(1, audit.Evidence.ItemCount);
    }

    private sealed class RecordingPublishQuery : ISpaceAuditQueryService
    {
        private readonly IReadOnlyList<SpacePublishEventDto> _result;

        public RecordingPublishQuery(
            IReadOnlyList<SpacePublishEventDto> result)
        {
            _result = result;
        }

        public List<(int Page, int PageSize)> Requests { get; } = [];

        public Task<IReadOnlyList<SpacePublishEventDto>>
            GetPublishEventsAsync(
                int page,
                int pageSize,
                CancellationToken ct = default)
        {
            Requests.Add((page, pageSize));
            return Task.FromResult(_result);
        }

        public Task<SpaceAuditPageDto> QueryAsync(
            SpaceAuditQueryDto query,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SpaceAuditTimelineItemDto>>
            GetTimelineAsync(
                Guid correlationId,
                CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPublishWriter : ISpaceAuditWriter
    {
        public bool Result { get; init; }
        public bool Throw { get; init; }
        public List<SpaceAuditEventInput> Inputs { get; } = [];

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            Inputs.Add(input);
            if (Throw)
                throw new InvalidOperationException("raw secret");
            return Task.FromResult(Result);
        }
    }

    private sealed class NoOpLocationPublishService :
        ILocationPublishService
    {
        public Task<int> PublishFloorAsync(
            Guid floorId,
            Guid? zoneId,
            string? user) =>
            Task.FromResult(0);

        public Task DeactivateAsync(Guid locationId, string? user) =>
            Task.CompletedTask;

        public Task<int> RepublishAsync(
            IReadOnlyCollection<Guid> locationIds,
            string? user) =>
            Task.FromResult(0);

        public Task<(int imported, List<string> skipped)> AdoptAsync(
            IEnumerable<(
                string code,
                Dictionary<string, object?>? attrs)> items,
            string? user) =>
            Task.FromResult((0, new List<string>()));
    }
}
