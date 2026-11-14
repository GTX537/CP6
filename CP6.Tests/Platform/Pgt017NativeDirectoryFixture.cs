using System.Security.Cryptography;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using CP6.Tests.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private sealed class NativeDirectoryFixture(IdentityDirectoryConfiguredHttpHost host, IdentityBaselineSourceV1 source)
        : IAsyncDisposable
    {
        public IdentityDirectoryConfiguredHttpHost Host { get; } = host;
        public IdentityBaselineSourceV1 Source { get; } = source;
        public ValueTask DisposeAsync() => Host.DisposeAsync();

        public async Task WriteExpectedAsync(CrmIdentityRuntime runtime, string path, string? aggregate = null, int version = 0)
        {
            var token = await Host.Token(Source.TenantId, "fixture-reader");
            using var collector = new IdentityBaselineCollector(runtime, Source, Host.Client.BaseAddress!,
                _ => ValueTask.FromResult(token.Token), new(AllowLoopbackHttp: true));
            var baseline = await collector.CollectAsync();
            if (aggregate is not null)
                Assert.Equal(version, Assert.Single(baseline.Items, item => item.Item.AggregateId == aggregate).Item.Version);
            await File.WriteAllTextAsync(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(
                new { baseline.ContentSha256, baseline.TotalCount }));
            File.Move(path + ".tmp", path);
        }
    }

    private async Task<NativeDirectoryFixture> StartNativeDirectory(Harness h, CrmIdentityRuntime runtime)
    {
        await using var control = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await control.OpenAsync();
        const string login = "pgt017_directory_reader";
        const string client = "fixture-reader";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using (var create = new NpgsqlCommand(
            $"CREATE ROLE {login} LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD '{password}'", control))
            await create.ExecuteNonQueryAsync();
        var request = new IdentityDirectoryInstallRequestV2(Guid.NewGuid(), "127.0.0.1", control.Port, control.Database,
            await IdentityDirectoryPostgresFixture.Scalar<uint>(control, "SELECT oid FROM pg_database WHERE datname=current_database()"),
            await IdentityDirectoryPostgresFixture.Scalar<string>(control, "SELECT system_identifier::text FROM pg_control_system()"),
            "7018cc6306f6852d5d0ae1033507a73c4e086af3", [new(h.Tenant, login)]);
        var installation = await new IdentityDirectoryInstallationV2().InstallAsync(control, request);
        Assert.Equal("INSTALLED", installation.Outcome);
        var identity = installation.Identity!;
        var host = await IdentityDirectoryConfiguredHttpHost.Start(server, identity, runtime,
            [new(h.Tenant, login, password, client)], baselineTokens: true);
        return new(host, new(h.Tenant, runtime.Options.Issuer, runtime.Options.Tenants[h.Tenant],
            runtime.ContractBundleSha256, identity.BaselineSha256));
    }
}
