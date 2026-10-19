using System.Net;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("IdentityDirectoryConfiguredHttpPostgres", DisableParallelization = true)]
public sealed class IdentityDirectoryConfiguredHttpPostgresCollection : ICollectionFixture<IdentityDirectoryConfiguredPostgresFixture>;

public sealed class IdentityDirectoryConfiguredPostgresFixture : IdentityDirectoryPostgresFixture
{
    internal IdentityDirectoryDatabaseIdentity Identity { get; private set; } = null!;
    protected override bool CheckV1Preconditions => false;
    protected override Task Install(NpgsqlConnection connection, NpgsqlTransaction transaction) => Task.CompletedTask;
    protected override async Task AfterInstallation(NpgsqlConnection connection)
    {
        var request = new IdentityDirectoryInstallRequestV2(Guid.NewGuid(), "127.0.0.1", 55435, connection.Database,
            await Scalar<uint>(connection, "SELECT oid FROM pg_database WHERE datname=current_database()"),
            await Scalar<string>(connection, "SELECT system_identifier::text FROM pg_control_system()"), "73ddf0b9374e5f09140be4280895d150749f87c5", Logins);
        Identity = (await new IdentityDirectoryInstallationV2().InstallAsync(connection, request)).Identity!;
    }
}

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class IdentityDirectoryConfiguredHttpPostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Actual_configured_host_authenticates_A_B_A_and_never_queries_the_owner_connection()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        foreach (var tenant in new[] { A, B, A })
        {
            var token = await host.Token(tenant);
            using var response = await host.Get("/internal/crm/identity/versions", token.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(tenant, page.RootElement.GetProperty("tenantId").GetGuid());
            using var foreign = await host.Get($"/internal/crm/identity/snapshots/tenant:{(tenant == A ? B : A):D}", token.Token);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
        await using var owner = await postgres.OpenOwner();
        var roles = await Scalar<string[]>(owner, "SELECT array_agg(DISTINCT usename::text ORDER BY usename::text) FROM pg_stat_activity WHERE datname=current_database() AND usename IN ('directory_a','directory_b')");
        Assert.Equal(new[] { "directory_a", "directory_b" }, roles);
    }
}
