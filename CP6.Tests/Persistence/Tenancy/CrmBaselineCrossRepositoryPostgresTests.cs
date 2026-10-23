using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using Microsoft.EntityFrameworkCore;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

internal sealed class CrmBaselineCrossRepositoryFactAttribute : FactAttribute
{
    public CrmBaselineCrossRepositoryFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_CRM_CROSS_PROJECT") is null)
            Skip="Opt-in cross-repository test requires a separately built CRM test project and its owned PG settings.";
    }
}

// Separate processes deliberately retain each repository's actual pinned Platform assemblies.
// Uses product Main registration/controller/source verification, fixture JWT signing keys and native ordinary PG reader identities.
[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class CrmBaselineCrossRepositoryPostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    [CrmBaselineCrossRepositoryFact]
    public async Task Actual_Main_source_is_consumed_by_CRM_process_and_saved_in_its_separate_native_database()
    {
        Assert.True(CurrentPermissionPostgresFactAttribute.Selected);
        var project=Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_PROJECT")!);Assert.True(File.Exists(project));
        var artifacts=Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_ARTIFACTS")!);
        var results=Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_CRM_CROSS_RESULTS")!),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(results);
        Assert.True(File.Exists(Environment.GetEnvironmentVariable("CP6_CRM_PG_SETTINGS")));
        await using var host=await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        await using var db=postgres.OwnerContext(A);var aggregate="tenant:"+A.ToString("D");
        var row=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==A && x.AggregateId==aggregate);
        var bytes=Encoding.UTF8.GetBytes(row.PayloadJson);bytes=bytes.Concat(Enumerable.Repeat((byte)' ',1048576-bytes.Length)).ToArray();
        var raw=Encoding.UTF8.GetString(bytes);var hash=IdentityEventContracts.Hash(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Snapshot\" SET \"PayloadJson\"={raw},\"PayloadSha256\"={hash} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}");
        try
        {
            List<object> expected=[];
            foreach(var tenant in new[]{A,B})
            {
                var token=await host.Token(tenant);
                var source=new IdentityBaselineSourceV1(tenant,host.Runtime.Options.Issuer,host.Runtime.Options.Tenants[tenant],host.Runtime.ContractBundleSha256,postgres.Identity.BaselineSha256);
                using var collector=new IdentityBaselineCollector(host.Runtime,source,host.Client.BaseAddress!,_=>ValueTask.FromResult(token.Token),new(AllowLoopbackHttp:true));
                var baseline=await collector.CollectAsync();var largest=baseline.Items.MaxBy(x=>x.PayloadBase64.Length)!;
                expected.Add(new{Source=source,Bearer=token.Token,Digest=baseline.ContentSha256,Count=baseline.TotalCount,LargestAggregate=largest.Item.AggregateId,LargestHash=largest.Item.PayloadSha256,LargestBytes=Convert.FromBase64String(largest.PayloadBase64).Length});
            }
            var dotnet=Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet");
            var start=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(project)!};
            foreach(var argument in new[]{"test",project,"--no-build","--no-restore","--artifacts-path",artifacts,"--filter","FullyQualifiedName~MainBaselineCompatibilityTests","--logger","trx;LogFileName=actual-main-crm.trx","--results-directory",results,"-v:q"}) start.ArgumentList.Add(argument);
            // Ephemeral bearer stays in the child environment; no credential file, command line or diagnostics dump.
            start.Environment["CP6_CRM_ACTUAL_MAIN"]=JsonSerializer.Serialize(new{Origin=host.Client.BaseAddress!.AbsoluteUri,Tenants=expected});
            using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
            try {using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));await child.WaitForExitAsync(timeout.Token);}
            finally {if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync();}}
            var diagnostics=await output+await errors;await File.WriteAllTextAsync(Path.Combine(results,"actual-main-crm.log"),diagnostics);
            Assert.True(child.ExitCode==0,diagnostics);
            XNamespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var trx=XDocument.Load(Path.Combine(results,"actual-main-crm.trx"));var result=Assert.Single(trx.Descendants(ns+"UnitTestResult"));
            Assert.Equal("Passed",(string?)result.Attribute("outcome"));
            Assert.Equal("CP6.CRM.IdentityPostgreSqlTests.MainBaselineCompatibilityTests.Actual_Main_HTTP_to_CRM_verifier_and_native_PostgreSql_preserves_A_B_A_sources_and_original_bytes",(string?)result.Attribute("testName"));
            Assert.Equal(0,host.OwnerQueries);Assert.Equal(0,host.Host.PoolSnapshot.Borrowers);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Snapshot\" SET \"PayloadJson\"={row.PayloadJson},\"PayloadSha256\"={row.PayloadSha256} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}");
        }
    }
}
