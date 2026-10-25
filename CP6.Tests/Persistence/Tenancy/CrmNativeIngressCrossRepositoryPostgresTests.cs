using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity.Baselines;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Moq;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class CrmNativeIngressCrossRepositoryPostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    [CrmBaselineCrossRepositoryFact]
    public async Task Actual_Main_IAM_commits_original_C02_outbox_and_native_CRM_consumes_it_without_rewriting()
    {
        Assert.True(CurrentPermissionPostgresFactAttribute.Selected);
        await using var main = await IdentityDirectoryConfiguredHttpHost.Start(postgres); List<object> entries = [];
        foreach (var tenant in new[] { A, B })
        {
            await using var writer = postgres.Server.Context(new TenantContext { CurrentTenantId = tenant }, main.Runtime);
            // Explicit local credential fixture; the original Main row/Outbox carries this epoch.
            var user = new Sys_User { UserName = "native-ingress-" + tenant.ToString("N"), Password = "owned fixture", Enable = true, AuthenticationEpoch = Guid.NewGuid() };
            var actor = new Sys_User { UserName = "native-ingress-admin-" + tenant.ToString("N"), Password = "owned fixture", Enable = true };
            writer.Sys_Users.AddRange(user, actor); await writer.SaveChangesAsync();
            var session = new IamAdministrationTestSession(writer, actor.Id);
            var service = new IamUserAdministrationV1(writer, session.Authority, new BCryptPasswordHasher(4), Mock.Of<IRefreshTokenService>());
            writer.ChangeTracker.Clear(); var editor = await service.ReadAsync(user.Id, session.Actor, default);
            var operation = Guid.NewGuid();
            var command = new IamUserCommandV1 { Id = user.Id, UserName = editor.User!.UserName, Enable = false,
                RoleId = editor.User.RoleId, RoleIds = editor.User.RoleIds, DeptId = editor.User.DeptId,
                ManagerId = editor.User.ManagerId, Email = editor.User.Email, OperationId = operation,
                ExpectedVersion = editor.User.Version, ExpectedReferencesVersion = editor.ReferencesVersion, Reason = "owned native receiver adoption" };
            var result = await service.ChangeAsync("user-update", command, session.Actor, default);
            Assert.Equal(result, await service.ChangeAsync("user-update", command, session.Actor, default));
            var aggregate = "user:" + user.Id.ToString("D");
            await using var priority = postgres.Server.PriorityContext();
            var messages = (await writer.Set<Cp6OutboxMessage>().AsNoTracking().Where(m => m.TenantId == tenant && m.AggregateId == aggregate).ToArrayAsync())
                .Concat(await priority.Set<Cp6OutboxMessage>().AsNoTracking().Where(m => m.TenantId == tenant && m.AggregateId == aggregate).ToArrayAsync())
                .OrderBy(m => m.AggregateVersion).ToArray(); Assert.Equal(2, messages.Length);
            var source = new IdentityBaselineSourceV1(tenant, main.Runtime.Options.Issuer, main.Runtime.Options.Tenants[tenant],
                main.Runtime.ContractBundleSha256, postgres.Identity.BaselineSha256);
            entries.Add(new { Source = source, User = user.Id, Events = messages.Select(m => new { m.MessageId, m.TenantId,
                m.TopicName, m.PartitionKey, PayloadBase64 = Convert.ToBase64String(m.Payload), m.AggregateId, m.AggregateVersion }).ToArray() });
        }
        var project = Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_PROJECT")!);
        var results = Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_RESULTS")!), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(results);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(project)! };
        foreach (var argument in new[] { "test", project, "--no-build", "--no-restore", "--artifacts-path", Environment.GetEnvironmentVariable("CP6_CRM_CROSS_ARTIFACTS")!,
            "--filter", "FullyQualifiedName~NativeIngressMainSourceTests", "--logger", "trx;LogFileName=native-ingress.trx", "--results-directory", results, "-v:q" }) start.ArgumentList.Add(argument);
        // Actual committed original bytes cross the process boundary; the local HTTP sidecar is
        // explicitly a fixture. This test does not claim the unavailable Kafka/Dapr runtime ran.
        start.Environment["CP6_CRM_MAIN_INGRESS"] = JsonSerializer.Serialize(entries);
        using var child = Process.Start(start)!; var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); await child.WaitForExitAsync(timeout.Token); }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
        var diagnostic = await output + await error; await File.WriteAllTextAsync(Path.Combine(results, "native-ingress.log"), diagnostic);
        Assert.True(child.ExitCode == 0, diagnostic); XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var outcome = Assert.Single(XDocument.Load(Path.Combine(results, "native-ingress.trx")).Descendants(ns + "UnitTestResult"));
        Assert.Equal("Passed", (string?)outcome.Attribute("outcome")); Assert.Equal(0, main.OwnerQueries); Assert.Equal(0, main.Host.PoolSnapshot.Borrowers);
    }
}
