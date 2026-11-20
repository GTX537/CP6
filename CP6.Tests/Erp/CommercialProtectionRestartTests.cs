using System.Diagnostics;
using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using static CP6.Tests.Erp.CommercialOriginalWireTests;
using static CP6.Tests.Erp.CommercialProtectionScenario;

namespace CP6.Tests.Erp;

public sealed partial class CommercialProtectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_lost_prepare_commit_settlement_recover_after_Main_host_restart()
    {
        var scenario = await CreateAsync(postgres);
        var proof = Path.Combine(Directory.GetParent(scenario.KeyRing)!.FullName, "restart-proof.json");
        await RunWorkerAsync(scenario, proof, "write");
        await RunWorkerAsync(scenario, proof, "read");
        Assert.Equal(2, await scenario.CountAsync("SELECT count(*) FROM erp_integration.ic_grant_v1 WHERE state='SETTLED'"));
        Assert.Equal(2, await scenario.CountAsync("SELECT count(*) FROM erp_integration.ic_execution_v1 WHERE state='COMMITTED'"));
        Assert.Equal(2, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
    }

    private static async Task RunWorkerAsync(CommercialProtectionScenario scenario, string proof, string phase)
    {
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(CommercialProtectionRestartWorkerTests).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName~CommercialProtectionRestartWorkerTests.Recover");
        start.Environment["CP6_IC_RESTART_PHASE"] = phase;
        start.Environment["CP6_IC_RESTART_STORAGE"] = scenario.Connection;
        start.Environment["CP6_IC_RESTART_KEYRING"] = scenario.KeyRing;
        start.Environment["CP6_IC_RESTART_PROOF"] = proof;

        using var worker = Process.Start(start)!;
        var output = worker.StandardOutput.ReadToEndAsync();
        var errors = worker.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        try { await worker.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
            throw;
        }
        var log = await output + await errors;
        await File.WriteAllTextAsync(proof + "." + phase + ".log", log);
        Assert.Equal(0, worker.ExitCode);
        Assert.Contains("Passed:     1", log);
    }
}

internal sealed class CommercialRestartFactAttribute : FactAttribute
{
    public CommercialRestartFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_IC_RESTART_PHASE") is null)
            Skip = "由 PostgreSQL 组件测试启动独立的 Main 验证进程。";
    }
}

public sealed class CommercialProtectionRestartWorkerTests
{
    private sealed record Resume(IcProtectionPrepareV1 Prepare, IcProtectionGrantV1 Grant, IcExecutionTerminalV1 Terminal, string OriginalHash);
    private sealed record RestartProof(int FirstProcess, Resume[] Originals);

    [CommercialRestartFact]
    public async Task Recover()
    {
        var scenario = new CommercialProtectionScenario(Environment.GetEnvironmentVariable("CP6_IC_RESTART_STORAGE")!,
            Environment.GetEnvironmentVariable("CP6_IC_RESTART_KEYRING")!);
        var manifest = Environment.GetEnvironmentVariable("CP6_IC_RESTART_PROOF")!;
        if (Environment.GetEnvironmentVariable("CP6_IC_RESTART_PHASE") == "write")
        {
            var resumes = new List<Resume>();
            foreach (var name in new[] { "handoff", "quote" })
            {
                var original = FixtureOriginal(name);
                var participant = await scenario.PublishAsync(original);
                var definition = scenario.Definition("fixture-restart-" + name, participant);
                await scenario.Owner.StartExecutionAsync(definition);
                var prepare = Prepare(definition, participant);
                await LoseResponseAsync(() => scenario.Owner.PrepareAsync(prepare));
                var recovered = await scenario.Owner.ReadProtectionAsync(prepare.ParticipantAuthority, prepare.ProtectionKey, definition.Execution);
                var grant = Assert.IsType<IcProtectionGrantV1>(recovered.Grant);
                await scenario.Owner.ReadyAsync(definition.Execution, [grant]);
                await LoseResponseAsync(() => scenario.CommitAsync(definition));
                var execution = await scenario.Owner.ReadExecutionAsync(definition.Execution);
                var terminal = Assert.IsType<IcExecutionTerminalV1>(execution.State.Terminal);
                Assert.Equal(Receipt, execution.OriginalReceipt);
                await LoseResponseAsync(() => scenario.Owner.SettleAsync(prepare.ParticipantAuthority, prepare.ProtectionKey, definition.Execution, terminal));
                resumes.Add(new(prepare, grant, terminal, original.BodyHash));
            }
            await File.WriteAllBytesAsync(manifest, JsonSerializer.SerializeToUtf8Bytes(new RestartProof(Environment.ProcessId, resumes.ToArray()),
                CommercialCanonicalV1.Json));
            return;
        }

        var proof = JsonSerializer.Deserialize<RestartProof>(await File.ReadAllBytesAsync(manifest), CommercialCanonicalV1.Json)!;
        Assert.NotEqual(proof.FirstProcess, Environment.ProcessId);
        foreach (var resume in proof.Originals)
        {
            var input = resume.Prepare;
            var original = await scenario.Owner.ReadSourceOriginalAsync(FixtureTenant, new(input.ParticipantAuthority, input.Subject, input.Purpose));
            Assert.Equal(resume.OriginalHash, original!.BodyHash);
            var terminal = await scenario.Owner.ReadExecutionAsync(input.Execution);
            Assert.Equal(resume.Terminal, terminal.State.Terminal);
            Assert.Equal(Receipt, terminal.OriginalReceipt);
            var response = await scenario.Owner.PrepareAsync(input);
            Assert.Equal("SETTLED", response.Knowledge);
            Assert.Equivalent(resume.Grant, response.Grant, strict: true);
            Assert.Equal(resume.Terminal, response.Terminal);
            Assert.Equal("SETTLED", (await scenario.Owner.SettleAsync(input.ParticipantAuthority, input.ProtectionKey, input.Execution, resume.Terminal)).Knowledge);
        }
        await File.WriteAllTextAsync(manifest + ".read-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task LoseResponseAsync(Func<Task> submitted) =>
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await submitted();
            throw new TimeoutException("模拟事务提交后回包丢失。");
        });
}
