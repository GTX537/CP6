using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("IdentityDirectoryInstallationPostgres", DisableParallelization = true)]
public sealed class IdentityDirectoryInstallationPostgresCollection : ICollectionFixture<IdentityDirectoryInstallationPostgresFixture>;

public sealed class IdentityDirectoryInstallationPostgresFixture : IdentityDirectoryPostgresFixture
{
    protected override bool CheckV1Preconditions => false;
    protected override Task Install(NpgsqlConnection connection, NpgsqlTransaction transaction) => Task.CompletedTask;
}

[Collection("IdentityDirectoryInstallationPostgres")]
public sealed class IdentityDirectoryInstallationPostgresTests(IdentityDirectoryInstallationPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Explicit_installation_and_read_only_inspection_use_the_real_cli_and_target_identity()
    {
        await using var owner = await postgres.OpenOwner();
        await using var credentials = postgres.OwnerContext(A);
        var identity = new NpgsqlConnectionStringBuilder(credentials.Database.GetConnectionString());
        var request = await Request(owner);
        var input = JsonSerializer.Serialize(new { Request = request, AdminUsername = identity.Username, AdminPassword = identity.Password, SslMode = "Disable" });
        var inspected = await Cli("inspect-identity-directory-v2", input);
        Assert.Equal(0, inspected.Exit); Assert.Equal("", inspected.Error); Assert.Contains("ABSENT", inspected.Output);
        Assert.DoesNotContain(identity.Password!, inspected.Output + inspected.Error);
        var service = new IdentityDirectoryInstallationV2();
        var badInput = JsonNode.Parse(input)!; badInput["Request"]!["Unexpected"] = true;
        foreach (var invalid in new[] { badInput.ToJsonString(), input.Insert(1, "\"AdminUsername\":\"duplicate\","), new string('x', 16385) })
        {
            var rejected = await Cli("install-identity-directory-v2", invalid);
            Assert.Equal(1, rejected.Exit); Assert.Equal("", rejected.Output); Assert.Contains("NOT_APPLIED", rejected.Error);
            Assert.DoesNotContain("Retry", rejected.Error); Assert.DoesNotContain(identity.Password!, rejected.Error);
        }
        foreach (var wrong in new[] { request with { DatabaseOid = request.DatabaseOid + 1 }, request with { SystemIdentifier = "1" },
            request with { Server = "127.0.0.2" }, request with { Port = 55436 }, request with { Tenants = [Logins[0], Logins[0]] } })
            Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<IdentityDirectoryInstallationException>(() => service.InstallAsync(owner, wrong))).Outcome);
        var cancelled = new CancellationTokenSource();
        var cancellation = new IdentityDirectoryInstallationV2 { Checkpoint = point =>
        { if (point == IdentityDirectoryInstallCheckpoint.BeforeCommit) cancelled.Cancel(); return Task.CompletedTask; } };
        Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<IdentityDirectoryInstallationException>(() => cancellation.InstallAsync(owner, request, cancelled.Token))).Outcome);
        cancelled.Dispose();
        Assert.Equal("ABSENT", (await service.InspectAsync(owner, request)).Outcome);
        Assert.False(await Scalar<bool>(owner, "SELECT EXISTS(SELECT FROM pg_roles WHERE rolname='cp6_tenant_lookup_v1')"));
        var installed = await Cli("install-identity-directory-v2", input);
        Assert.Equal(0, installed.Exit); Assert.Equal("", installed.Error);
        var receipt = JsonSerializer.Deserialize<IdentityDirectoryInstallationResultV2>(installed.Output)!;
        Assert.Equal("INSTALLED", receipt.Outcome); Assert.NotNull(receipt.Identity);
        Assert.Equal(receipt.Identity, (await service.InspectAsync(owner, request)).Identity);
        inspected = await Cli("inspect-identity-directory-v2", input);
        Assert.Equal(0, inspected.Exit); Assert.Contains("VERIFIED", inspected.Output);
        var repeated = await Cli("install-identity-directory-v2", input);
        Assert.Equal(1, repeated.Exit); Assert.Contains("NOT_APPLIED", repeated.Error); Assert.DoesNotContain("Retry", repeated.Error);
        Assert.Equal(receipt.Identity, (await service.InspectAsync(owner, request)).Identity);

        var context = new TenantStorageContext();
        await using var manager = new TenantConnectionManager("main", context.Accessor,
            TenantIdentityDirectoryReaderPostgresTests.Catalog(IdentityDirectoryAuthorizationV2.ModelVersion, IdentityDirectoryAuthorizationV2.MigrationVersion),
            new TenantStorageCredentials { Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == A ? postgres.PasswordA : postgres.PasswordB) }, new(2, 2, 1));
        var authorization = new TenantIdentityDirectoryAuthorizationReader(manager, receipt.Identity);
        var directory = new TenantIdentityDirectoryReader(manager, Runtime(), new EphemeralDataProtectionProvider(), receipt.Identity);
        var jti = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await using (var db = postgres.OwnerContext(A))
            await new CrmServiceTokenRecordStore(db, Runtime()).RecordAsync(Runtime().Options.Issuer, "fixture-reader", A, jti.ToString("D"), now.AddMinutes(10), default);
        Assert.Equal(receipt.Identity, (await service.InspectAsync(owner, request)).Identity); // business rows do not invalidate installation
        await context.Run(A, async ct =>
        {
            Assert.True(await authorization.IsAuthorizedAsync(A, Runtime().Options.Issuer, "fixture-reader", jti, now, ct));
            Assert.Equal(A, (await directory.ReadVersionsAsync(A, null, cancellationToken: ct)).TenantId);
            var wrong = new TenantIdentityDirectoryAuthorizationReader(manager, receipt.Identity with { Fingerprint = new string('0', 64) });
            await Assert.ThrowsAsync<IdentityReadException>(() => wrong.IsAuthorizedAsync(A, Runtime().Options.Issuer, "fixture-reader", jti, now, ct));
        });
        await using (var runtime = await postgres.OpenRuntime(A))
        {
            foreach (var sql in new[] { "SELECT canonical FROM cp6_identity_directory_v2.installation", "SELECT * FROM cp6_tenancy_v1.login_tenant",
                "UPDATE cp6_identity_directory_v2.installation SET fingerprint='forged'" })
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => Sql(runtime, sql))).SqlState);
        }
        foreach (var (change, restore) in new[]
        {
            ("ALTER TABLE crm_identity.\"Snapshot\" DISABLE ROW LEVEL SECURITY", "ALTER TABLE crm_identity.\"Snapshot\" ENABLE ROW LEVEL SECURITY"),
            ("ALTER ROLE directory_a BYPASSRLS", "ALTER ROLE directory_a NOBYPASSRLS"),
            ("UPDATE cp6_identity_directory_v2.installation SET source_revision='changed'", $"UPDATE cp6_identity_directory_v2.installation SET source_revision='{request.SourceRevision}'"),
            ($"UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='{Guid.NewGuid():D}' WHERE role_name='directory_a'", $"UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='{A:D}' WHERE role_name='directory_a'")
        })
        {
            await Sql(owner, change);
            try
            {
                await Assert.ThrowsAsync<IdentityDirectoryInstallationException>(() => service.InspectAsync(owner, request));
                await context.Run(A, async ct =>
                {
                    await Assert.ThrowsAsync<IdentityReadException>(() => authorization.IsAuthorizedAsync(A, Runtime().Options.Issuer, "fixture-reader", jti, now, ct));
                    await Assert.ThrowsAsync<IdentityReadException>(() => directory.ReadVersionsAsync(A, null, cancellationToken: ct));
                });
            }
            finally { await Sql(owner, restore); }
            Assert.Equal(receipt.Identity, (await service.InspectAsync(owner, request)).Identity);
        }
        // Keep the final bad ACL in the owned, subsequently stopped test cluster; inspect must not repair/reseal it.
        await Sql(owner, "GRANT SELECT (\"TenantCode\") ON public.\"Sys_Tenants\" TO directory_a");
        await Assert.ThrowsAsync<IdentityDirectoryInstallationException>(() => service.InspectAsync(owner, request));
        await context.Run(A, async ct => await Assert.ThrowsAsync<IdentityReadException>(() => directory.ReadVersionsAsync(A, null, cancellationToken: ct)));
        Assert.True(await Scalar<bool>(owner, "SELECT has_column_privilege('directory_a','public.\"Sys_Tenants\"','TenantCode','SELECT')"));
        Assert.Equal(0, manager.Snapshot().Borrowers);
    }

    internal static async Task<IdentityDirectoryInstallRequestV2> Request(NpgsqlConnection owner) => new(Guid.NewGuid(), "127.0.0.1", 55435, owner.Database,
        await Scalar<uint>(owner, "SELECT oid FROM pg_database WHERE datname=current_database()"),
        await Scalar<string>(owner, "SELECT system_identifier::text FROM pg_control_system()"), "1b03afcb8ba9b86ea6a9ccc8d45ad5392ba71364", Logins);

    internal static async Task<(int Exit, string Output, string Error)> Cli(string operation, string input)
    {
        var executable = Environment.GetEnvironmentVariable("CP6_DIRECTORY_CLI")
            ?? throw new InvalidOperationException("Build CLI and set CP6_DIRECTORY_CLI to its absolute apphost path.");
        var info = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(operation); info.ArgumentList.Add("--stdin");
        using var process = Process.Start(info)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output, await error);
    }
}

[CollectionDefinition("IdentityDirectoryInstallationRecoveryPostgres", DisableParallelization = true)]
public sealed class IdentityDirectoryInstallationRecoveryPostgresCollection : ICollectionFixture<IdentityDirectoryInstallationPostgresFixture>;

[Collection("IdentityDirectoryInstallationRecoveryPostgres")]
public sealed class IdentityDirectoryInstallationRecoveryPostgresTests(IdentityDirectoryInstallationPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Lost_commit_response_is_unknown_and_is_resolved_by_inspection_without_replaying_installation()
    {
        await using var owner = await postgres.OpenOwner();
        var request = await IdentityDirectoryInstallationPostgresTests.Request(owner);
        var service = new IdentityDirectoryInstallationV2 { Checkpoint = point => point == IdentityDirectoryInstallCheckpoint.AfterCommit
            ? Task.FromException(new IOException("private-lost-output")) : Task.CompletedTask };
        var unknown = await Assert.ThrowsAsync<IdentityDirectoryInstallationException>(() => service.InstallAsync(owner, request));
        Assert.Equal("UNKNOWN", unknown.Outcome); Assert.DoesNotContain("private-lost-output", unknown.ToString());
        var inspected = await new IdentityDirectoryInstallationV2().InspectAsync(owner, request);
        Assert.Equal("VERIFIED", inspected.Outcome); Assert.NotNull(inspected.Identity);
        var repeated = await Assert.ThrowsAsync<IdentityDirectoryInstallationException>(() => new IdentityDirectoryInstallationV2().InstallAsync(owner, request));
        Assert.Equal("NOT_APPLIED", repeated.Outcome); Assert.Equal("already-installed-or-unknown-layout", repeated.Message);
        Assert.Equal(inspected, await new IdentityDirectoryInstallationV2().InspectAsync(owner, request));
    }
}
