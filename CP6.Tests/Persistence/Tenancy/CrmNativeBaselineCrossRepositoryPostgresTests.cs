using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using CP6.Core.Services.CrmIdentity.Baselines;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class CrmNativeBaselineCrossRepositoryPostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    [CrmBaselineCrossRepositoryFact]
    public async Task Native_CRM_host_obtains_Main_C2_credentials_and_publishes_both_actual_directories()
    {
        Assert.True(CurrentPermissionPostgresFactAttribute.Selected);
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres, baselineTokens: true);
        List<object> expected = [];
        foreach (var tenant in new[] { A, B })
        {
            var token = await host.Token(tenant);
            var source = new IdentityBaselineSourceV1(tenant, host.Runtime.Options.Issuer, host.Runtime.Options.Tenants[tenant],
                host.Runtime.ContractBundleSha256, postgres.Identity.BaselineSha256);
            using var collector = new IdentityBaselineCollector(host.Runtime, source, host.Client.BaseAddress!,
                _ => ValueTask.FromResult(token.Token), new(AllowLoopbackHttp: true));
            var baseline = await collector.CollectAsync(); var client = tenant == A ? "reader-a" : "reader-b";
            expected.Add(new { Source = source, Digest = baseline.ContentSha256, Count = baseline.TotalCount,
                ClientId = client, ClientSecret = host.BaselineSecrets[client] });
        }
        var project = Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_PROJECT")!);
        var artifacts = Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_ARTIFACTS")!);
        var results = Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_RESULTS")!), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(results);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(project)! };
        foreach (var argument in new[] { "test", project, "--no-build", "--no-restore", "--artifacts-path", artifacts,
            "--filter", "FullyQualifiedName~NativeBaselineMainHostTests", "--logger", "trx;LogFileName=native-main-crm.trx", "--results-directory", results, "-v:q" })
            start.ArgumentList.Add(argument);
        // Ephemeral credentials stay in the child environment, never a file/command-line diagnostic.
        start.Environment["CP6_CRM_ACTUAL_MAIN"] = JsonSerializer.Serialize(new { Origin = host.Client.BaseAddress!.AbsoluteUri, Tenants = expected });
        using var child = Process.Start(start)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); await child.WaitForExitAsync(timeout.Token); }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
        var diagnostics = await stdout + await stderr; await File.WriteAllTextAsync(Path.Combine(results, "native-main-crm.log"), diagnostics);
        Assert.True(child.ExitCode == 0, diagnostics);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var result = Assert.Single(XDocument.Load(Path.Combine(results, "native-main-crm.trx")).Descendants(ns + "UnitTestResult"));
        Assert.Equal("Passed", (string?)result.Attribute("outcome"));
        Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
    }
}
