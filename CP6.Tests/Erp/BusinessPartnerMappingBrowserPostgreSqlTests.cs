using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class PartnerMappingBrowserFact : FactAttribute
    {
        public PartnerMappingBrowserFact()
        {
            if (new[] { "CP6_PERMISSION_PG_BIN", "CP6_BP_BROWSER_NODE", "CP6_BP_MAPPING_BROWSER_SCRIPT", "CP6_BP_BROWSER_DIST" }
                .Any(key => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))))
                Skip = "需提供本卡隔离 PostgreSQL、Node 和已构建的 Vue 目录。";
        }
    }

    [PartnerMappingBrowserFact]
    public async Task Business_partner_wp04_03_browser_maps_with_built_Vue_real_Main_and_one_original_key()
    {
        await using var h = await Seed();
        await using var owner = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, owner);
        var account = Guid.NewGuid();
        owner.Account(account, "00040003", "INACTIVE");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, owner.Environment());
        var folder = Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!;
        var node = Environment.GetEnvironmentVariable("CP6_BP_BROWSER_NODE")!;
        var script = Environment.GetEnvironmentVariable("CP6_BP_MAPPING_BROWSER_SCRIPT")!;
        var dist = Environment.GetEnvironmentVariable("CP6_BP_BROWSER_DIST")!;
        var processInfo = new ProcessStartInfo(node)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        processInfo.ArgumentList.Add(script);
        using var browser = Process.Start(processInfo)!;
        await browser.StandardInput.WriteAsync(JsonSerializer.Serialize(new
        {
            api = host.Origin.ToString(), dist, evidence = folder, account,
            user = "price-owner", password = "Price1!Password", tenant = h.Tenant.ToString("N")
        }));
        browser.StandardInput.Close();
        var output = browser.StandardOutput.ReadToEndAsync();
        var errors = browser.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await browser.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!browser.HasExited) { browser.Kill(entireProcessTree: true); await browser.WaitForExitAsync(); }
        }
        Assert.True(browser.ExitCode == 0, (await errors) + "\n" + (await output));
        using var proof = JsonDocument.Parse(await output);
        Assert.True(proof.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1 }, await MappingCounts(h));
        Assert.Single(await h.Db.BusinessPartners.Where(row => row.BpCd == "0000403").ToListAsync());
        await DeviceEvidence("wp04-03-browser.json", proof.RootElement.Clone());
    }
}
