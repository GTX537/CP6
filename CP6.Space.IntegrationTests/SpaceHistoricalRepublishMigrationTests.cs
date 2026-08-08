using System.Reflection;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Space.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceHistoricalRepublishMigrationTests
{
    [Fact]
    public void Migration_is_additive_and_rollback_is_forward_only()
    {
        var migration = new SpaceE06S05HistoricalRepublish();
        var up = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Up", up);

        var table = Assert.Single(
            up.Operations.OfType<CreateTableOperation>());
        Assert.Equal("Space_HistoricalRepublish", table.Name);
        Assert.Contains(table.Columns, value => value.Name == "HistoricalVersionId");
        Assert.Contains(table.Columns, value => value.Name == "ExpectedPublishedVersionId");
        Assert.Contains(table.Columns, value => value.Name == "TargetVersionId");
        Assert.Contains(table.Columns, value => value.Name == "ValidationRunId");
        Assert.Contains(table.Columns, value => value.Name == "PublishAttemptId");
        Assert.DoesNotContain(up.Operations, value =>
            value is DropTableOperation or DropColumnOperation);

        var down = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Down", down);
        var guard = Assert.Single(down.Operations.OfType<SqlOperation>());
        Assert.Contains("THROW 51022", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("forward-only", guard.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain(down.Operations, value =>
            value is DropTableOperation or DropColumnOperation);
    }

    [Fact]
    public void Historical_republish_evidence_cannot_be_deleted()
    {
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var context = new SpaceContext(
            options,
            execution,
            new TestClock());
        var operation = SpaceHistoricalRepublish.Create(
            execution.TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "restore-key",
            new string('a', 64),
            "Restore verified history.",
            approvalReference: null,
            execution.ActorId,
            DateTime.UtcNow,
            Guid.NewGuid());
        operation.BindReservation(Guid.NewGuid(), Guid.NewGuid());
        context.Attach(operation);
        context.Remove(operation);

        var failure = Assert.Throws<InvalidOperationException>(
            () => context.SaveChanges());
        Assert.Contains(
            "immutable",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void Invoke(
        Migration migration,
        string methodName,
        MigrationBuilder builder) =>
        migration.GetType()
            .GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
