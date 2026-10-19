using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("IdentityDirectoryAuthorizationPostgres", DisableParallelization = true)]
public sealed class IdentityDirectoryAuthorizationPostgresCollection : ICollectionFixture<IdentityDirectoryAuthorizationPostgresFixture>;

public sealed class IdentityDirectoryAuthorizationPostgresFixture : IdentityDirectoryPostgresFixture
{
    protected override bool CheckV1Preconditions => false;
    protected override Task Install(NpgsqlConnection connection, NpgsqlTransaction transaction)
        => IdentityDirectoryAuthorizationV2.InstallAsync(connection, transaction, Logins);
    protected override async Task CheckProfilePreconditions(NpgsqlConnection connection)
    {
        foreach (var anomaly in new[]
        {
            "ALTER TABLE public.\"Sys_Tenants\" ALTER COLUMN \"Enable\" DROP NOT NULL",
            "CREATE POLICY unexpected ON public.\"Sys_Tenants\" USING (true)",
            "CREATE POLICY unexpected ON crm_identity.\"ServiceToken\" USING (true)",
            "GRANT SELECT (\"TenantCode\") ON public.\"Sys_Tenants\" TO directory_a",
            "ALTER TABLE crm_identity.\"ServiceToken\" ALTER COLUMN \"RevokedAtUtc\" TYPE timestamp USING \"RevokedAtUtc\" AT TIME ZONE 'UTC'"
        })
        {
            await using var rejected = await connection.BeginTransactionAsync();
            await Sql(connection, anomaly);
            var error = await Record.ExceptionAsync(() => Install(connection, rejected));
            Assert.True(error is InvalidOperationException or TenantStorageException, "Expected profile rejection: " + anomaly + "; actual " + error?.GetType().Name);
            await rejected.RollbackAsync();
            Assert.False(await Scalar<bool>(connection, "SELECT EXISTS(SELECT FROM pg_namespace WHERE nspname='cp6_tenancy_v1')"));
        }
        await using var v1 = await connection.BeginTransactionAsync();
        await IdentityDirectoryRowSecurityV1.InstallAsync(connection, v1, Logins);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Install(connection, v1));
        await v1.RollbackAsync();
    }
}

[Collection("IdentityDirectoryAuthorizationPostgres")]
public sealed class IdentityDirectoryAuthorizationPostgresTests(IdentityDirectoryAuthorizationPostgresFixture postgres)
{
    private TenantConnectionManager Manager(TenantStorageContext context, string model = IdentityDirectoryAuthorizationV2.ModelVersion,
        string migration = IdentityDirectoryAuthorizationV2.MigrationVersion) => new("main", context.Accessor,
            TenantIdentityDirectoryReaderPostgresTests.Catalog(model, migration), new TenantStorageCredentials
            { Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == A ? postgres.PasswordA : postgres.PasswordB) }, new(2, 2, 1));
    private async Task<Guid> Token(Guid tenant, DateTimeOffset expiry)
    {
        var jti = Guid.NewGuid();
        await using var owner = postgres.OwnerContext(tenant);
        await new CrmServiceTokenRecordStore(owner, Runtime()).RecordAsync(Runtime().Options.Issuer, "fixture-reader", tenant, jti.ToString("D"), expiry, default);
        return jti;
    }
    private static Task<bool> Authorized(TenantIdentityDirectoryAuthorizationReader reader, Guid tenant, Guid jti,
        DateTimeOffset now, CancellationToken ct = default) => reader.IsAuthorizedAsync(tenant, Runtime().Options.Issuer, "fixture-reader", jti, now, ct);
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_current_authorization_reads_only_own_tenant_and_token_columns()
    {
        var issuer = Runtime().Options.Issuer;
        foreach (var tenant in new[] { A, B })
        {
            await using var owner = postgres.OwnerContext(tenant);
            await new CrmServiceTokenRecordStore(owner, Runtime()).RecordAsync(issuer, "fixture-reader", tenant,
                tenant.ToString("D"), DateTimeOffset.UtcNow.AddMinutes(10), default);
        }
        foreach (var tenant in new[] { A, B, A })
        {
            await using var runtime = await postgres.OpenRuntime(tenant);
            Assert.Equal(new[] { tenant }, await Scalar<Guid[]>(runtime,
                "SELECT array_agg(\"Id\") FROM public.\"Sys_Tenants\" WHERE \"Enable\" AND (\"ExpireDate\" IS NULL OR \"ExpireDate\">localtimestamp)"));
            Assert.Equal(new[] { tenant }, await Scalar<Guid[]>(runtime,
                "SELECT array_agg(DISTINCT \"TenantId\") FROM crm_identity.\"ServiceToken\" WHERE \"RevokedAtUtc\" IS NULL AND \"ExpiresAtUtc\">clock_timestamp() AND \"Issuer\"<>'' AND \"Jti\"<>'' AND \"ClientId\"<>''"));
            Assert.False(await Scalar<bool>(runtime, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=session_user"));
            foreach (var sql in new[]
            {
                "SELECT * FROM public.\"Sys_Tenants\"", "SELECT \"TenantCode\" FROM public.\"Sys_Tenants\"",
                "SELECT * FROM crm_identity.\"ServiceToken\"", "SELECT \"RowVersion\" FROM crm_identity.\"ServiceToken\"",
                "UPDATE public.\"Sys_Tenants\" SET \"Enable\"=true", "DELETE FROM crm_identity.\"ServiceToken\"",
                "SELECT * FROM cp6_tenancy_v1.login_tenant", "SELECT * FROM public.\"Sys_Users\"",
                "CREATE TEMP TABLE forbidden(id integer)", "SET ROLE fixture_owner"
            }) Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => Sql(runtime, sql))).SqlState);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Reader_observes_current_eligibility_revocation_and_exact_expiry_boundary_without_cache()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context);
        var reader = new TenantIdentityDirectoryAuthorizationReader(manager);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var token = await Token(A, now.AddMinutes(5));
        var edge = await Token(A, now.AddSeconds(-60));
        var insideSkew = await Token(A, now.AddSeconds(-59));
        var foreign = await Token(B, now.AddMinutes(5));
        await using var owner = await postgres.OpenOwner();
        await context.Run(A, async ct =>
        {
            Assert.True(await Authorized(reader, A, token, now, ct));
            Assert.False(await Authorized(reader, A, edge, now, ct));
            Assert.True(await Authorized(reader, A, insideSkew, now, ct));
            Assert.False(await Authorized(reader, A, foreign, now, ct));
            Assert.False(await Authorized(reader, A, Guid.NewGuid(), now, ct));
            Assert.False(await reader.IsAuthorizedAsync(A, Runtime().Options.Issuer, "wrong-client", token, now, ct));
            Assert.False(await reader.IsAuthorizedAsync(A, "https://wrong-issuer.example.test", "fixture-reader", token, now, ct));
            try
            {
                await Sql(owner, $"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"='{A:D}'");
                Assert.False(await Authorized(reader, A, token, now, ct));
                await Sql(owner, $"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=true,\"ExpireDate\"='2000-01-01' WHERE \"Id\"='{A:D}'");
                Assert.False(await Authorized(reader, A, token, now, ct));
            }
            finally { await Sql(owner, $"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=true,\"ExpireDate\"=NULL WHERE \"Id\"='{A:D}'"); }
            Assert.True(await Authorized(reader, A, token, now, ct));
            await using var db = postgres.OwnerContext(A);
            await new CrmServiceTokenRecordStore(db, Runtime()).RevokeAsync(Runtime().Options.Issuer, "fixture-reader", A, token.ToString("D"), ct);
            Assert.False(await Authorized(reader, A, token, now, ct));
        });
        Assert.Equal(0, manager.Snapshot().Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Authorized_profile_alternates_A_B_A_and_consumes_the_existing_directory_reader()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context);
        var authorization = new TenantIdentityDirectoryAuthorizationReader(manager);
        var directory = new TenantIdentityDirectoryReader(manager, Runtime(), new EphemeralDataProtectionProvider());
        var now = DateTimeOffset.UtcNow; var a = await Token(A, now.AddMinutes(5)); var b = await Token(B, now.AddMinutes(5));
        foreach (var tenant in new[] { A, B, A })
            await context.Run(tenant, async ct =>
            {
                Assert.True(await Authorized(authorization, tenant, tenant == A ? a : b, now, ct));
                Assert.Equal(tenant, (await directory.ReadVersionsAsync(tenant, null, cancellationToken: ct)).TenantId);
                Assert.Null(await directory.ReadSnapshotAsync(tenant, $"tenant:{(tenant == A ? B : A):D}", ct));
            });
        Assert.Equal(0, manager.Snapshot().Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Missing_scope_wrong_tenant_profile_or_grant_fails_closed_without_owner_fallback()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context);
        var reader = new TenantIdentityDirectoryAuthorizationReader(manager); var now = DateTimeOffset.UtcNow;
        var token = await Token(A, now.AddMinutes(5));
        await Unavailable(() => Authorized(reader, A, token, now));
        await using var old = Manager(context, IdentityDirectoryRowSecurityV1.ModelVersion, IdentityDirectoryRowSecurityV1.MigrationVersion);
        await context.Run(A, async ct =>
        {
            await Unavailable(() => Authorized(reader, B, token, now, ct));
            await Unavailable(() => Authorized(new(old), A, token, now, ct));
            await using var owner = await postgres.OpenOwner();
            try
            {
                await Sql(owner, "REVOKE SELECT (\"RevokedAtUtc\") ON crm_identity.\"ServiceToken\" FROM directory_a");
                await Unavailable(() => Authorized(reader, A, token, now, ct));
            }
            finally { await Sql(owner, "GRANT SELECT (\"RevokedAtUtc\") ON crm_identity.\"ServiceToken\" TO directory_a"); }
            Assert.True(await Authorized(reader, A, token, now, ct));
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancellation_of_a_blocked_current_authorization_read_releases_the_lease()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context);
        var reader = new TenantIdentityDirectoryAuthorizationReader(manager); var now = DateTimeOffset.UtcNow;
        var token = await Token(A, now.AddMinutes(5));
        await context.Run(A, async _ =>
        {
            await using var owner = await postgres.OpenOwner(); await using var held = await owner.BeginTransactionAsync();
            await Sql(owner, "LOCK TABLE crm_identity.\"ServiceToken\" IN ACCESS EXCLUSIVE MODE");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Authorized(reader, A, token, now, cancellation.Token));
            await held.RollbackAsync();
            Assert.Equal(0, manager.Snapshot().Borrowers);
            Assert.True(await Authorized(reader, A, token, now));
        });
    }

    private static async Task Unavailable(Func<Task<bool>> action)
        => Assert.Equal("C02_IDENTITY_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<IdentityReadException>(action)).Message);

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_control_keeps_authoritative_writes_under_force_rls_and_rollback()
    {
        var token = await Token(A, DateTimeOffset.UtcNow.AddMinutes(5));
        await using var owner = await postgres.OpenOwner(); await using var tx = await owner.BeginTransactionAsync();
        await Sql(owner, "CREATE ROLE directory_authorization_control_probe NOLOGIN NOSUPERUSER NOBYPASSRLS INHERIT");
        await Sql(owner, "GRANT fixture_owner TO directory_authorization_control_probe WITH INHERIT TRUE, SET FALSE");
        await Sql(owner, "SET LOCAL ROLE directory_authorization_control_probe");
        Assert.False(await Scalar<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=current_user"));
        foreach (var table in new[] { IdentityDirectoryAuthorizationV2.TenantTable, IdentityDirectoryAuthorizationV2.TokenTable })
            Assert.True(await Scalar<bool>(owner, $"SELECT pg_catalog.row_security_active('{table}'::regclass)"));
        Assert.Equal(2L, await Scalar<long>(owner, "SELECT count(*) FROM public.\"Sys_Tenants\""));
        await Sql(owner, $"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"='{A:D}'");
        Assert.False(await Scalar<bool>(owner, $"SELECT \"Enable\" FROM public.\"Sys_Tenants\" WHERE \"Id\"='{A:D}'"));
        var version = await Scalar<byte[]>(owner, $"SELECT \"RowVersion\" FROM crm_identity.\"ServiceToken\" WHERE \"Jti\"='{token:D}'");
        await Sql(owner, $"UPDATE crm_identity.\"ServiceToken\" SET \"RevokedAtUtc\"=clock_timestamp() WHERE \"Jti\"='{token:D}'");
        Assert.NotEqual(version, await Scalar<byte[]>(owner, $"SELECT \"RowVersion\" FROM crm_identity.\"ServiceToken\" WHERE \"Jti\"='{token:D}'"));
        await tx.RollbackAsync();
        Assert.True(await Scalar<bool>(owner, $"SELECT \"Enable\" FROM public.\"Sys_Tenants\" WHERE \"Id\"='{A:D}'"));
        Assert.Equal(version, await Scalar<byte[]>(owner, $"SELECT \"RowVersion\" FROM crm_identity.\"ServiceToken\" WHERE \"Jti\"='{token:D}'"));
    }
}
