using System.Reflection;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Platform.EntityFramework;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CP6.Core.Persistence;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Security.Claims;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static IdentitySourceCommandV1 SourceCommand(IdentitySourceBindingV1 previous, string operation)
        => new(operation, "0", IdentityEventContracts.Hash(JsonSerializer.SerializeToUtf8Bytes(previous, IdentityEventContracts.Json)),
            previous with { Issuer = "https://next-" + previous.TenantId.ToString("N") + ".example.test" },
            "模拟Owner批准/完整后继", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5), 0,
            "REAUTHENTICATE_ALL", "KEEP_EXISTING_STORAGE");
    private static CrmIdentityRuntime SourceRuntimeFor(Guid tenant) => SourceRuntime(new(tenant, "source-" + tenant.ToString("N"),
        "https://source.example.test", "us", [tenant.ToString("N")], [tenant.ToString("N")], ""));
    private static TenantManagementSessionV1 SourceSession(Harness h) => new(_ => Task.FromResult(h.User), _ => Task.CompletedTask);
    private static IdentitySourceAdministrationV1 SourceOwner(CP6Context db, CrmIdentityRuntime runtime, Harness h)
        => new(db, runtime, new HttpContextAccessor { HttpContext = h.Http });

    [CurrentPermissionPostgresFact]
    public async Task Source_competing_approvals_have_one_winner_and_original_bytes_survive_later_directory_changes()
    {
        await using var h = await TenantManagementSeed(); var runtime = SourceRuntimeFor(h.Tenant);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        var command = SourceCommand(runtime.Options.SourceBindings[h.Tenant], "竞争批准一");
        await using var left = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var right = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<(byte[]? Bytes, Exception? Error)> Attempt(CP6Context db, IdentitySourceCommandV1 input)
        {
            byte[]? result = null;
            var error = await Record.ExceptionAsync(async () => result = await SourceOwner(db, runtime, h).PublishAsync(h.Tenant, input, SourceSession(h)));
            return (result, error);
        }
        var results = await Task.WhenAll(Attempt(left, command), Attempt(right, command with { OperationRef = "竞争批准二" }));
        var success = Assert.Single(results, r => r.Error is null).Bytes!;
        Assert.Equal("C02_SOURCE_VERSION_CHANGED", Assert.IsType<TenantManagementExceptionV1>(Assert.Single(results, r => r.Error is not null).Error).Message);
        var original = JsonSerializer.Deserialize<IdentitySourcePublicationV1>(success, IdentityEventContracts.Json)!;
        var nextRuntime = SourceRuntime(command.Next);
        await using (var change = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, nextRuntime))
        {
            (await change.Sys_Tenants.SingleAsync(x => x.Id == h.Tenant)).ExpireDate = DateTime.SpecifyKind(DateTime.Now.AddYears(1), DateTimeKind.Unspecified);
            await change.SaveChangesAsync();
        }
        Assert.Equal(success, await SourceOwner(h.Db, nextRuntime, h).OriginalAsync(h.Tenant, original.OperationRef, SourceSession(h)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_publication_failure_rolls_back_revocation_snapshot_boundary_and_source()
    {
        await using var h = await TenantManagementSeed(); var runtime = SourceRuntimeFor(h.Tenant);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        var source = runtime.Options.SourceBindings[h.Tenant]; var token = Guid.NewGuid().ToString("D");
        await new CrmServiceTokenRecordStore(h.Db, runtime).RecordAsync(source.Issuer, source.ReaderClientIds[0], h.Tenant, token, DateTimeOffset.UtcNow.AddMinutes(5), default);
        var generation = await h.Db.CrmIdentityTenantGenerations.Where(x => x.TenantId == h.Tenant).Select(x => x.Generation).SingleAsync();
        var constraint = "reject_source_" + Guid.NewGuid().ToString("N");
        await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE cp6_identity_source_v1.publications ADD CONSTRAINT {constraint} CHECK (tenant_id <> '{h.Tenant:D}'::uuid) NOT VALID");
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => SourceOwner(h.Db, runtime, h).PublishAsync(h.Tenant, SourceCommand(source, "模拟封存失败"), SourceSession(h)));
            h.Db.ChangeTracker.Clear();
            Assert.Null(await h.Db.CrmServiceTokenRecords.Where(x => x.TenantId == h.Tenant && x.Jti == token).Select(x => x.RevokedAtUtc).SingleAsync());
            Assert.Equal(generation, await h.Db.CrmIdentityTenantGenerations.Where(x => x.TenantId == h.Tenant).Select(x => x.Generation).SingleAsync());
            Assert.Equal(0, await h.Db.Database.SqlQuery<long>($"SELECT revision AS \"Value\" FROM cp6_identity_source_v1.registrations WHERE tenant_id={h.Tenant}").SingleAsync());
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE cp6_identity_source_v1.publications DROP CONSTRAINT {constraint}"); }
    }

    private sealed class SourceContextFactory(Func<CP6Context> create) : IDbContextFactory<CP6Context>
    { public CP6Context CreateDbContext() => create(); }

    [CurrentPermissionPostgresFact]
    public async Task Source_approval_expiring_during_session_completion_rolls_back_at_final_decision()
    {
        await using var h = await TenantManagementSeed(); var runtime = SourceRuntimeFor(h.Tenant);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        var command = SourceCommand(runtime.Options.SourceBindings[h.Tenant], "模拟最后决定点过期") with { ApprovalValidUntilUtc = DateTimeOffset.UtcNow.AddSeconds(2) };
        var completed = false;
        var session = new TenantManagementSessionV1(_ => Task.FromResult(h.User), async _ =>
        {
            completed = true;
            var remaining = command.ApprovalValidUntilUtc - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        });
        Assert.Equal("C02_SOURCE_APPROVAL_NOT_CURRENT", (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() =>
            SourceOwner(h.Db, runtime, h).PublishAsync(h.Tenant, command, session))).Message);
        Assert.True(completed);
        Assert.Equal(0, await h.Db.Database.SqlQuery<long>($"SELECT revision AS \"Value\" FROM cp6_identity_source_v1.registrations WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_forward_migration_preserves_legacy_bytes_and_requires_explicit_approved_enrollment()
    {
        await using var template = server.Context(new TenantContext());
        var settings = new NpgsqlConnectionStringBuilder(template.Database.GetConnectionString());
        var databaseName = "source_forward_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(settings.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE " + databaseName, admin); await create.ExecuteNonQueryAsync();
        }
        settings.Database = databaseName;
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        var tenant = Guid.NewGuid(); var context = new TenantContext { CurrentTenantId = tenant };
        await using var db = new CP6Context(options.Options, context);
        await db.GetService<IMigrator>().MigrateAsync("20261117100000_FinancePolicyOriginalV1");
        var priorityProfile = DatabaseMigrationProfile.For(database, DatabaseContextKind.IdentityPriority);
        await using var priority = new IdentityMessagingContext(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(),
            database, settings.ConnectionString, priorityProfile.MigrationsAssembly, priorityProfile.HistoryTable, priorityProfile.HistorySchema).Options);
        await priority.Database.MigrateAsync();
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "前向迁移模拟", Enable = true });
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = tenant, UserName = "source-owner", Enable = true, IsPlatformAdmin = true };
        db.Sys_Users.Add(user); await db.SaveChangesAsync();
        var runtime = SourceRuntimeFor(tenant);
        await new IdentityBootstrapService(db, runtime).InitializeAsync(tenant);
        var original = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).OrderBy(x => x.AggregateId)
            .Select(x => new { x.AggregateId, x.Version, x.PayloadJson, x.PayloadSha256 }).ToArrayAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(original, await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).OrderBy(x => x.AggregateId)
            .Select(x => new { x.AggregateId, x.Version, x.PayloadJson, x.PayloadSha256 }).ToArrayAsync());
        runtime.Options.RequireSourceRegistration = true;
        Assert.Equal("C02_SOURCE_ENROLLMENT_REQUIRED", (await Assert.ThrowsAsync<IdentityReadException>(() => IdentitySourceAuthorityV1.RequireCurrentAsync(db, runtime, tenant))).Message);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")), new Claim("tenant_id", tenant.ToString("D")), new Claim("is_platform_admin", "true")], "controlled-source-fixture")) };
        var owner = new IdentitySourceAdministrationV1(db, runtime, new HttpContextAccessor { HttpContext = http });
        var session = new TenantManagementSessionV1(_ => Task.FromResult(user.Id), _ => Task.CompletedTask);
        var command = SourceCommand(runtime.Options.SourceBindings[tenant], "明确接纳历史来源") with { ExpectedRevision = "unregistered" };
        var result = JsonSerializer.Deserialize<IdentitySourcePublicationV1>(await owner.PublishAsync(tenant, command, session), IdentityEventContracts.Json)!;
        Assert.Equal("unregistered", result.PreviousRevision); Assert.Equal("1", result.Revision);
        Assert.Equal(original.Length, result.Items.Length);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_old_revocations_remain_deliverable_and_new_source_cannot_overwrite_publication_or_use_unknown_issuer()
    {
        await using var h = await TenantManagementSeed(); var runtime = SourceRuntimeFor(h.Tenant);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        var source = runtime.Options.SourceBindings[h.Tenant]; var command = SourceCommand(source, "模拟原撤销投递");
        var jti = Guid.NewGuid().ToString("D");
        await new CrmServiceTokenRecordStore(h.Db, runtime).RecordAsync(source.Issuer, source.ReaderClientIds[0], h.Tenant, jti, DateTimeOffset.UtcNow.AddMinutes(5), default);
        var bytes = await SourceOwner(h.Db, runtime, h).PublishAsync(h.Tenant, command, SourceSession(h));
        var next = SourceRuntime(command.Next);
        var validator = new IdentitySourceOutboxValidatorV1(new SourceContextFactory(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant })), next);
        await using var priority = server.PriorityContext();
        var message = await priority.Set<Cp6OutboxMessage>().SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == IdentityEventContracts.TokenAggregate(source.Issuer, jti));
        var envelope = new Cp6OutboxEnvelope(message.MessageId, message.TenantId, message.TopicName, message.PartitionKey, message.Payload,
            message.CorrelationId, message.CausationId, message.AggregateId, message.AggregateVersion);
        Assert.False(next.Validator.Validate(envelope).IsValid);
        Assert.True(validator.Validate(envelope).IsValid);
        using var payload = JsonDocument.Parse(message.Payload);
        var wrongIssuer = "https://never-approved.example.test";
        var wrongData = JsonSerializer.Deserialize<TokenRevokedIdentityData>(payload.RootElement.GetProperty("data"), IdentityEventContracts.Json)! with { Issuer = wrongIssuer };
        var forged = IdentityEventContracts.Create(h.Tenant, IdentityEventContracts.TokenAggregate(wrongIssuer, jti), 1,
            IdentityEventContracts.TokenRevoked, wrongData, "us", "unknown-issuer", "unknown-issuer");
        Assert.True(runtime.Validator.ForIssuer(wrongIssuer).Validate(forged).IsValid);
        Assert.False(validator.Validate(forged).IsValid);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_identity_source_v1.publications WHERE tenant_id={h.Tenant}"))).SqlState);
        Assert.Equal(bytes, await SourceOwner(h.Db, next, h).OriginalAsync(h.Tenant, command.OperationRef, SourceSession(h)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_identity_source_v1.registrations SET revision=revision+1 WHERE tenant_id={h.Tenant}"))).SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_directory_functions_keep_ordinary_tenant_reads_and_reject_foreign_source_parameters()
    {
        await using var h = await TenantManagementSeed(); var runtime = SourceRuntimeFor(h.Tenant);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        var command = SourceCommand(runtime.Options.SourceBindings[h.Tenant], "模拟普通账号取版");
        var bytes = await SourceOwner(h.Db, runtime, h).PublishAsync(h.Tenant, command, SourceSession(h));
        await using var owner = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await owner.OpenAsync();
        var catalogSql = (string)typeof(IdentityDirectoryCoreCatalogV1).GetField("FingerprintSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await using (var path = new NpgsqlCommand("SET LOCAL search_path=pg_catalog", owner, tx)) await path.ExecuteNonQueryAsync();
            await using var observed = new NpgsqlCommand(catalogSql, owner, tx);
            output.WriteLine("SOURCE_CORE_CATALOG=" + await observed.ExecuteScalarAsync());
            await tx.RollbackAsync();
        }
        var role = "source_" + h.Tenant.ToString("N"); var password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await IdentityDirectoryRowSecurityV1.InstallAsync(owner, tx, [new TenantLoginV1(h.Tenant, role)]);
            await tx.CommitAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username = role, Password = password, SearchPath = "pg_catalog", Pooling = false };
        await using var ordinary = new NpgsqlConnection(connection.ConnectionString); await ordinary.OpenAsync();
        await using var read = new NpgsqlCommand("SELECT cp6_identity_source_v1.read_publication(@tenant,1)", ordinary);
        read.Parameters.AddWithValue("tenant", h.Tenant);
        Assert.Equal(bytes, (byte[])(await read.ExecuteScalarAsync())!);
        read.Parameters["tenant"].Value = Guid.NewGuid();
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => read.ExecuteScalarAsync())).SqlState);
        await using var forbidden = new NpgsqlCommand("SELECT * FROM cp6_identity_source_v1.publications", ordinary);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteScalarAsync())).SqlState);
    }
}
