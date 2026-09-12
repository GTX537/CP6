using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CP6.ErpLive.Fixture;

internal static partial class ErpLiveFixture
{
    private const string FaultConstraint = "C03Fixture_OrderStorageFault";
    private const string FaultTenantProperty = "CP6.C03FixtureFaultTenant";

    public static async Task StorageFaultAsync(string fixturePath, string rawTenant, string mode, CancellationToken ct)
    {
        if (mode is not ("on" or "off")) throw new FixtureException("C03_STORAGE_FAULT_MODE_INVALID");
        var (fixture, owner) = await ReadFixtureAsync(fixturePath, ct);
        var tenant = OwnedTenant(fixture, rawTenant);
        await using var db = new SqlConnection(OwnedConnection(owner));
        await db.OpenAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var existing = await db.QuerySingleOrDefaultAsync<FaultOwnership>(new CommandDefinition("""
            SELECT c.parent_object_id AS ParentObjectId,
                CONVERT(nvarchar(128), own.value) AS OwnerToken,
                CONVERT(nvarchar(128), target.value) AS TenantId
            FROM sys.check_constraints AS c
            LEFT JOIN sys.extended_properties AS own ON own.class=1 AND own.major_id=c.object_id
                AND own.minor_id=0 AND own.name=@ownerProperty
            LEFT JOIN sys.extended_properties AS target ON target.class=1 AND target.major_id=c.object_id
                AND target.minor_id=0 AND target.name=@tenantProperty
            WHERE c.name=@constraint
            """, new { constraint = FaultConstraint, ownerProperty = OwnershipProperty, tenantProperty = FaultTenantProperty },
            transaction, cancellationToken: ct));
        var orderObjectId = await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT OBJECT_ID(N'dbo.T_Order')", transaction: transaction, cancellationToken: ct));
        if (existing is not null && (existing.ParentObjectId != orderObjectId || existing.OwnerToken != owner.OwnerToken ||
                existing.TenantId != tenant.Id.ToString("D")))
            throw new FixtureException("C03_STORAGE_FAULT_OWNERSHIP_MISMATCH");

        if (mode == "on" && existing is null)
        {
            // WITH NOCHECK preserves existing rows. SQL 547 blocks only subsequent writes for this fixture tenant.
            // A trigger would reject EF INSERT ... OUTPUT before its tenant predicate could run.
            await db.ExecuteAsync(new CommandDefinition(
                $"ALTER TABLE dbo.T_Order WITH NOCHECK ADD CONSTRAINT [{FaultConstraint}] CHECK ([TenantId] <> '{tenant.Id:D}')",
                transaction: transaction, commandTimeout: 30, cancellationToken: ct));
            foreach (var pair in new[] { (OwnershipProperty, owner.OwnerToken), (FaultTenantProperty, tenant.Id.ToString("D")) })
                await db.ExecuteAsync(new CommandDefinition("""
                    EXEC sys.sp_addextendedproperty @name=@property, @value=@value,
                        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'T_Order',
                        @level2type=N'CONSTRAINT', @level2name=@constraint
                    """, new { property = pair.Item1, value = pair.Item2, constraint = FaultConstraint }, transaction, cancellationToken: ct));
        }
        else if (mode == "off" && existing is not null)
            await db.ExecuteAsync(new CommandDefinition($"ALTER TABLE dbo.T_Order DROP CONSTRAINT [{FaultConstraint}]",
                transaction: transaction, commandTimeout: 30, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaId = "cp6.c03.fixture-storage-fault.v1", tenantId = tenant.Id, enabled = mode == "on",
            mechanism = "tenant-filtered-check-constraint", constraintName = FaultConstraint, expectedSqlError = 547,
            artificialFixtureFault = true, preservesExistingRows = true
        }, Json));
    }

    private sealed class FaultOwnership
    {
        public int ParentObjectId { get; init; }
        public string? OwnerToken { get; init; }
        public string? TenantId { get; init; }
    }
}
