using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionV1;

namespace CP6.Tests.Erp;

internal sealed class CommercialProtectionScenario
{
    internal const string PreviousMigration = "20261118154500_BusinessPartnerResultReadV1";
    internal const string Migration = "20261120081000_CommercialProtectionV1";
    internal static readonly Guid Tenant = CommercialOriginalWireTests.FixtureTenant;
    internal static readonly DateTimeOffset InitialTime = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    internal static readonly byte[] Receipt = Encoding.UTF8.GetBytes("{ \"receipt\":\"模拟-原收据-7\" }\n");
    internal static readonly IcEvidenceRefV1 Decision = new("FIXTURE_OWNER", "decision", "v1", "fixture-decision");
    internal readonly TestClock Clock = new();
    internal readonly OwnerDirectory Originals = new();
    internal readonly AuthorityFixture Gate = new();
    private readonly Queue queue;

    internal string Connection { get; }
    internal string KeyRing { get; }
    internal CommercialProtectionOwnerV1 Owner { get; private set; }

    internal CommercialProtectionScenario(string connection, string keyRing)
    {
        Connection = connection;
        KeyRing = keyRing;
        queue = new Queue(Connection);
        Owner = RestartOwner();
    }

    internal CommercialProtectionOwnerV1 RestartOwner()
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(KeyRing),
            options => options.SetApplicationName("CP6.Main.WP02-22.isolated"));
        Owner = new CommercialProtectionOwnerV1(queue, protection, Gate, Originals, Clock);
        Originals.Owner = Owner;
        return Owner;
    }

    internal static async Task<CommercialProtectionScenario> CreateAsync(CurrentPermissionPostgresFixture fixture, bool forward = false)
    {
        var databaseName = "ic_" + Guid.NewGuid().ToString("N");
        await using (var admin = await fixture.OpenOwnerAsync())
            await CurrentPermissionPostgresFixture.Sql(admin, "CREATE DATABASE " + databaseName);
        await using var sample = fixture.Context(Tenant, databaseName);
        var connection = sample.Database.GetConnectionString()!;
        var keyRing = Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!, databaseName, "keys");
        var scenario = new CommercialProtectionScenario(connection, keyRing);
        await using var core = new CP6Context(Options<CP6Context>(connection, DatabaseContextKind.Core), new TenantContext { CurrentTenantId = Tenant });
        await core.Database.MigrateAsync();
        await using var integration = scenario.queue.CreateDbContext();
        if (forward)
        {
            await integration.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await using var previous = await scenario.OpenAsync();
            await CurrentPermissionPostgresFixture.Sql(previous, """
                INSERT INTO erp_integration."Aggregate"("TenantId","Kind","AggregateId","LastRequestVersion","LastAggregateVersion","LastResultVersion","RowVersion")
                VALUES('b0000000-0000-4000-8000-000000000001',1,'a0000000-0000-4000-8000-000000000001',7,9,11,decode('0000000000000001','hex'));
                """);
        }
        await integration.Database.MigrateAsync();
        Assert.Contains(Migration, await integration.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await integration.Database.GetPendingMigrationsAsync());

        await using var data = await scenario.OpenAsync();
        await CurrentPermissionPostgresFixture.Sql(data, """
            CREATE SCHEMA ic_fixture;
            CREATE TABLE ic_fixture.authority(tenant_id uuid, action integer, allowed bool NOT NULL, PRIMARY KEY(tenant_id,action));
            CREATE TABLE ic_fixture.source_fact(tenant_id uuid, body_hash text, PRIMARY KEY(tenant_id,body_hash));
            CREATE TABLE ic_fixture.effect(tenant_id uuid, effect_key text, original_receipt bytea NOT NULL, PRIMARY KEY(tenant_id,effect_key));
            """);
        foreach (var action in Enum.GetValues<IcAuthorityActionV1>())
        {
            await using var setup = new NpgsqlCommand("INSERT INTO ic_fixture.authority VALUES(@tenant,@action,true)", data);
            setup.Parameters.AddWithValue("tenant", Tenant);
            setup.Parameters.AddWithValue("action", (int)action);
            await setup.ExecuteNonQueryAsync();
        }
        return scenario;
    }

    internal async Task<NpgsqlConnection> OpenAsync()
    {
        var db = new NpgsqlConnection(Connection);
        try { await db.OpenAsync(); return db; }
        catch { await db.DisposeAsync(); throw; }
    }

    internal async Task AllowAsync(IcAuthorityActionV1 action, bool allowed)
    {
        await using var db = await OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE ic_fixture.authority SET allowed=@allow WHERE tenant_id=@tenant AND action=@action", db);
        command.Parameters.AddWithValue("allow", allowed);
        command.Parameters.AddWithValue("tenant", Tenant);
        command.Parameters.AddWithValue("action", (int)action);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    internal async Task<IcParticipantV1> PublishAsync(CommercialOriginalV1 original, string? participantName = null)
    {
        var handoff = original.Route.Contract == CommercialOriginalV1.Handoff;
        var participant = new IcParticipantV1(participantName ?? (handoff ? "CRM_HO" : original.Route.Authority),
            Subject(original), handoff ? MainIntakeNewEffect : CrmResultProjection);
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        await using var source = new NpgsqlCommand("""
            INSERT INTO ic_fixture.source_fact VALUES(@tenant,@hash) ON CONFLICT DO NOTHING
            """, db, tx);
        source.Parameters.AddWithValue("tenant", Tenant);
        source.Parameters.AddWithValue("hash", original.BodyHash);
        await source.ExecuteNonQueryAsync();
        await Owner.PublishWithinAsync(db, tx, original, new(participant.ParticipantAuthority, "1", "fixture-writers-v1",
            "subject-1", "purpose-" + participant.Purpose, "policy-1", handoff ? null : "stream-1"));
        await tx.CommitAsync();
        return participant;
    }

    internal IcExecutionDefinitionV1 Definition(string name, params IcParticipantV1[] participants)
    {
        var main = participants[0].Purpose == MainIntakeNewEffect;
        var authority = main ? "MAIN_INTAKE" : "CRM_OPP";
        return Freeze(Tenant, authority, main ? "intake.decide" : "opportunity.result.apply", Guid.NewGuid(),
            "1", "fixture-service", name, "\"opaque-预检-01\"", Encoding.UTF8.GetBytes("{\"scope\":\"模拟执行\"}"), participants,
            Clock.GetUtcNow().AddHours(1), new(authority, "fixture-reg", "v1", "fixture-proof"));
    }

    internal static IcProtectionPrepareV1 Prepare(IcExecutionDefinitionV1 definition, IcParticipantV1 participant) =>
        new(Contract, ProtectionKey(definition.Execution, participant), definition.Execution, participant.ParticipantAuthority,
            participant.Subject, participant.Purpose, "subject-1", definition.AdmissionDeadline, definition.ConsumerRegistration);

    internal async Task<IcProtectionGrantV1> ReadyAsync(IcExecutionDefinitionV1 definition)
    {
        await Owner.StartExecutionAsync(definition);
        var grants = new List<IcProtectionGrantV1>();
        foreach (var participant in definition.Participants)
            grants.Add((await Owner.PrepareAsync(Prepare(definition, participant))).Grant!);
        Assert.Equal("READY", (await Owner.ReadyAsync(definition.Execution, grants)).State.State);
        return grants[0];
    }

    internal async Task<byte[]> CommitAsync(IcExecutionDefinitionV1 definition)
    {
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        var receipt = await Owner.CommitWithinAsync(db, tx, definition.Execution, Apply(definition.EffectKey));
        await tx.CommitAsync();
        return receipt;
    }

    internal static Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<IcBusinessEffectV1>> Apply(string effectKey) =>
        async (db, tx, ct) =>
        {
            await using var sql = new NpgsqlCommand("INSERT INTO ic_fixture.effect VALUES(@tenant,@key,@receipt)", db, tx);
            sql.Parameters.AddWithValue("tenant", Tenant);
            sql.Parameters.AddWithValue("key", effectKey);
            sql.Parameters.AddWithValue("receipt", Receipt);
            Assert.Equal(1, await sql.ExecuteNonQueryAsync(ct));
            return new("receipt-" + effectKey, Receipt);
        };

    internal async Task<long> CountAsync(string sql)
    {
        await using var db = await OpenAsync();
        return (long)(await CurrentPermissionPostgresFixture.Scalar(db, sql))!;
    }

    internal sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Utc { get; set; } = InitialTime;
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    internal sealed class OwnerDirectory : ICommercialOwnerOriginalsV1
    {
        internal CommercialProtectionOwnerV1 Owner { get; set; } = null!;
        internal bool TerminalUnavailable { get; set; }
        internal bool GrantUnavailable { get; set; }

        public async Task<IcProtectionReadV1?> ReadGrantAsync(IcParticipantV1 participant, string protectionKey,
            IcExecutionRefV1 execution, CancellationToken ct) =>
            GrantUnavailable ? null : await Owner.ReadProtectionAsync(participant.ParticipantAuthority, protectionKey, execution, ct);

        public async Task<IcExecutionTerminalV1?> ReadTerminalAsync(IcExecutionRefV1 execution, CancellationToken ct) =>
            TerminalUnavailable ? null : (await Owner.ReadExecutionAsync(execution, ct)).State.Terminal;
    }

    internal sealed class AuthorityFixture : ICommercialAuthorityGateV1
    {
        internal int AbortPid { get; private set; }

        public async Task HoldCurrentAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcAuthorityRequestV1 request, CancellationToken ct)
        {
            await using var scope = new NpgsqlCommand("""
                SELECT allowed FROM ic_fixture.authority WHERE tenant_id=@tenant AND action=@action FOR SHARE
                """, db, tx);
            scope.Parameters.AddWithValue("tenant", request.TenantId);
            scope.Parameters.AddWithValue("action", (int)request.Action);
            if (await scope.ExecuteScalarAsync(ct) is not true)
                throw new CommercialProtocolException("CURRENT_AUTHORITY_DENIED");

            if (request.Action == IcAuthorityActionV1.PublishSource)
            {
                await using var source = new NpgsqlCommand(
                    "SELECT true FROM ic_fixture.source_fact WHERE tenant_id=@tenant AND body_hash=@original FOR SHARE", db, tx);
                source.Parameters.AddWithValue("tenant", request.TenantId);
                source.Parameters.AddWithValue("original", request.Original!.BodyHash);
                Assert.True(await source.ExecuteScalarAsync(ct) is true);
                Assert.Equal("fixture-writers-v1", request.Publication!.SharedWriterPolicy);
            }
            if (request.Action == IcAuthorityActionV1.Prepare)
            {
                await using var registered = new NpgsqlCommand("""
                    SELECT true FROM erp_integration.ic_execution_v1
                      WHERE tenant_id=@tenant AND consumer=@consumer AND operation_kind=@operation AND operation_id=@id
                    """, db, tx);
                var original = request.Prepare!.Execution;
                registered.Parameters.AddWithValue("tenant", request.TenantId);
                registered.Parameters.AddWithValue("consumer", original.ConsumerAuthority);
                registered.Parameters.AddWithValue("operation", original.OperationKind);
                registered.Parameters.AddWithValue("id", original.OperationId);
                Assert.True(await registered.ExecuteScalarAsync(ct) is true);
                Assert.Equal(original.ConsumerAuthority, request.Evidence!.Authority);
            }
            if (request.Action == IcAuthorityActionV1.Abort)
            {
                Assert.Equal(Decision, request.Evidence);
                Assert.NotNull(request.AbortCode);
                AbortPid = db.ProcessID;
            }
        }
    }

    private sealed class Queue(string connection) : IDbContextFactory<ErpIntegrationContext>
    {
        private readonly DbContextOptions<ErpIntegrationContext> options = Options<ErpIntegrationContext>(connection, DatabaseContextKind.ErpIntegration);
        public ErpIntegrationContext CreateDbContext() => new(options);
    }

    private static DbContextOptions<T> Options<T>(string connection, DatabaseContextKind kind) where T : DbContext
    {
        var storage = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(storage, kind);
        return DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), storage, connection,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
    }
}
