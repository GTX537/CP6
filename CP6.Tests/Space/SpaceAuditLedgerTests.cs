using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CP6.Tests.Space;

public sealed class SpaceAuditLedgerTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Audit_row_is_tenant_scoped_stamped_and_materialized_as_utc()
    {
        var databaseName = Guid.NewGuid().ToString();
        var occurredAtUtc = DateTime.SpecifyKind(
            new DateTime(2026, 7, 25, 12, 34, 56),
            DateTimeKind.Utc);

        await using (var db = NewDb(databaseName, TenantA))
        {
            db.SpaceAuditEvents.Add(NewEvent(Guid.Empty, occurredAtUtc));
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb(databaseName, TenantA))
        {
            var row = await db.SpaceAuditEvents.SingleAsync();
            Assert.Equal(TenantA, row.TenantId);
            Assert.Equal(occurredAtUtc, row.OccurredAtUtc);
            Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind);
        }

        await using (var db = NewDb(databaseName, TenantB))
        {
            Assert.Empty(await db.SpaceAuditEvents.ToListAsync());
        }
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Audit_rows_reject_mutation_async(EntityState state)
    {
        var databaseName = Guid.NewGuid().ToString();

        await using var db = NewDb(databaseName, TenantA);
        var row = NewEvent(TenantA, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));
        db.SpaceAuditEvents.Add(row);
        await db.SaveChangesAsync();
        db.Entry(row).State = state;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());

        Assert.Equal("SPACE_AUDIT_APPEND_ONLY", error.Message);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Audit_rows_reject_mutation_sync(EntityState state)
    {
        var databaseName = Guid.NewGuid().ToString();

        await using var db = NewDb(databaseName, TenantA);
        var row = NewEvent(TenantA, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));
        db.SpaceAuditEvents.Add(row);
        await db.SaveChangesAsync();
        db.Entry(row).State = state;

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Equal("SPACE_AUDIT_APPEND_ONLY", error.Message);
    }

    [Fact]
    public void Audit_and_integration_event_model_matches_storage_contract()
    {
        using var db = new CP6Context(
            new DbContextOptionsBuilder<CP6Context>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=cp6-model-only;Trusted_Connection=True")
                .Options,
            new TenantContext { CurrentTenantId = TenantA });
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var audit = designModel.FindEntityType(typeof(Space_AuditEvent));
        Assert.NotNull(audit);
        Assert.Equal("Space_AuditEvent", audit.GetTableName());

        AssertProperty(audit, nameof(Space_AuditEvent.ActorType), 16, false);
        AssertProperty(audit, nameof(Space_AuditEvent.ActorId), 100, false);
        AssertProperty(audit, nameof(Space_AuditEvent.ActorName), 100, true);
        AssertProperty(audit, nameof(Space_AuditEvent.OrganizationContextId), 100, true);
        AssertProperty(audit, nameof(Space_AuditEvent.Action), 100, false);
        AssertProperty(audit, nameof(Space_AuditEvent.ResourceType), 64, false);
        AssertProperty(audit, nameof(Space_AuditEvent.ResourceId), 128, true);
        AssertProperty(audit, nameof(Space_AuditEvent.Outcome), 16, false);
        AssertProperty(audit, nameof(Space_AuditEvent.ReasonCode), 100, true);
        AssertProperty(audit, nameof(Space_AuditEvent.TraceId), 64, false);
        AssertProperty(audit, nameof(Space_AuditEvent.ClientType), 32, true);
        AssertProperty(audit, nameof(Space_AuditEvent.IpAddress), 64, true);
        AssertProperty(audit, nameof(Space_AuditEvent.UserAgent), 256, true);

        Assert.Equal(
            "nvarchar(max)",
            audit.FindProperty(nameof(Space_AuditEvent.AuthorizationEvidenceJson))!.GetColumnType());
        Assert.Equal(
            "char(64)",
            audit.FindProperty(nameof(Space_AuditEvent.BeforeHash))!.GetColumnType());
        Assert.Equal(
            "char(64)",
            audit.FindProperty(nameof(Space_AuditEvent.AfterHash))!.GetColumnType());
        Assert.Equal(
            "varchar(64)",
            audit.FindProperty(nameof(Space_AuditEvent.TraceId))!.GetColumnType());
        var occurredAtConverter =
            audit.FindProperty(nameof(Space_AuditEvent.OccurredAtUtc))!.GetValueConverter();
        Assert.NotNull(occurredAtConverter);
        var materialized = Assert.IsType<DateTime>(
            occurredAtConverter.ConvertFromProvider(
                DateTime.SpecifyKind(
                    new DateTime(2026, 7, 25, 12, 34, 56),
                    DateTimeKind.Unspecified)));
        Assert.Equal(DateTimeKind.Utc, materialized.Kind);

        AssertIndex(
            audit,
            nameof(Space_AuditEvent.TenantId),
            nameof(Space_AuditEvent.OccurredAtUtc));
        AssertIndex(
            audit,
            nameof(Space_AuditEvent.TenantId),
            nameof(Space_AuditEvent.CorrelationId),
            nameof(Space_AuditEvent.OccurredAtUtc));
        AssertIndex(
            audit,
            nameof(Space_AuditEvent.TenantId),
            nameof(Space_AuditEvent.PublishAttemptId),
            nameof(Space_AuditEvent.OccurredAtUtc));
        AssertIndex(
            audit,
            nameof(Space_AuditEvent.TenantId),
            nameof(Space_AuditEvent.JobId),
            nameof(Space_AuditEvent.RunId));

        var checkConstraints =
            audit.GetCheckConstraints().ToDictionary(x => x.Name!, x => x.Sql);
        Assert.Equal(4, checkConstraints.Count);
        Assert.Equal(
            "[ActorType] IN ('User','System')",
            checkConstraints["CK_Space_AuditEvent_ActorType"]);
        Assert.Equal(
            "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'",
            checkConstraints["CK_Space_AuditEvent_Correlation"]);
        Assert.Equal(
            "[Outcome] IN ('Started','Succeeded','Failed','Denied')",
            checkConstraints["CK_Space_AuditEvent_Outcome"]);
        Assert.Equal(
            "[TenantId] <> '00000000-0000-0000-0000-000000000000'",
            checkConstraints["CK_Space_AuditEvent_Tenant"]);

        var integration = designModel.FindEntityType(typeof(IntegrationEvent));
        Assert.NotNull(integration);
        Assert.True(integration.FindProperty(nameof(IntegrationEvent.JobId))!.IsNullable);
        Assert.True(integration.FindProperty(nameof(IntegrationEvent.PublishAttemptId))!.IsNullable);
        Assert.True(integration.FindProperty(nameof(IntegrationEvent.RetryLeaseId))!.IsNullable);
        AssertIndex(
            integration,
            nameof(IntegrationEvent.TenantId),
            nameof(IntegrationEvent.CorrelationId));
        AssertIndex(
            integration,
            nameof(IntegrationEvent.TenantId),
            nameof(IntegrationEvent.JobId));
        AssertIndex(
            integration,
            nameof(IntegrationEvent.TenantId),
            nameof(IntegrationEvent.PublishAttemptId));
        AssertIndex(
            integration,
            nameof(IntegrationEvent.TenantId),
            nameof(IntegrationEvent.RetryLeaseId));
    }

    private static CP6Context NewDb(string databaseName, Guid tenant) =>
        new(
            new DbContextOptionsBuilder<CP6Context>()
                .UseInMemoryDatabase(databaseName)
                .Options,
            new TenantContext { CurrentTenantId = tenant });

    private static Space_AuditEvent NewEvent(Guid tenantId, DateTime occurredAtUtc) =>
        new()
        {
            TenantId = tenantId,
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            ActorType = "User",
            ActorId = "user-1",
            Action = "space.floor.publish",
            ResourceType = "Floor",
            ResourceId = Guid.NewGuid().ToString(),
            Outcome = "Started",
            CorrelationId = Guid.NewGuid(),
            TraceId = "0123456789abcdef0123456789abcdef",
        };

    private static void AssertProperty(
        IEntityType entity,
        string name,
        int maxLength,
        bool nullable)
    {
        var property = entity.FindProperty(name);
        Assert.NotNull(property);
        Assert.Equal(maxLength, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertIndex(IEntityType entity, params string[] properties)
    {
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties.Select(x => x.Name).SequenceEqual(properties));
    }
}
