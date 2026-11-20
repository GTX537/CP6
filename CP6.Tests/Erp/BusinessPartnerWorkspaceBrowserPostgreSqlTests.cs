using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class PartnerWorkspaceBrowserFact : FactAttribute
    {
        public PartnerWorkspaceBrowserFact()
        {
            if (new[] { "CP6_PERMISSION_PG_BIN", "CP6_BP_BROWSER_NODE", "CP6_BP_BROWSER_SCRIPT", "CP6_BP_BROWSER_DIST" }
                .Any(key => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))))
                Skip = "需提供本卡隔离 PostgreSQL、Node 和已构建的 Vue 目录。";
        }
    }

    [PartnerWorkspaceBrowserFact]
    public async Task Business_partner_wp04_01_browser_runs_built_Vue_and_real_Main()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var folder = Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!;
        var node = Environment.GetEnvironmentVariable("CP6_BP_BROWSER_NODE")!;
        var script = Environment.GetEnvironmentVariable("CP6_BP_BROWSER_SCRIPT")!;
        var dist = Environment.GetEnvironmentVariable("CP6_BP_BROWSER_DIST")!;
        var start = new ProcessStartInfo(node)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(script);
        using var proc = Process.Start(start)!;
        await proc.StandardInput.WriteAsync(JsonSerializer.Serialize(new { api = host.Origin.ToString(), dist, evidence = folder,
            user = "price-owner", password = "Price1!Password", tenant = h.Tenant.ToString("N") }));
        proc.StandardInput.Close();
        var output = proc.StandardOutput.ReadToEndAsync();
        var errors = proc.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await proc.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!proc.HasExited) { proc.Kill(entireProcessTree: true); await proc.WaitForExitAsync(); }
        }
        Assert.True(proc.ExitCode == 0, (await errors) + "\n" + (await output));
        using var proof = JsonDocument.Parse(await output);
        Assert.True(proof.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(new[] { 1, 5, 9, 5, 5, 4 }, await FormalCounts(h));
        await DeviceEvidence("wp04-01-browser.json", proof.RootElement.Clone());
    }
}
