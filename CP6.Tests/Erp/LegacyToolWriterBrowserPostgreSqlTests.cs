using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class LegacyPlateMoldBrowserFactAttribute : FactAttribute
    {
        public LegacyPlateMoldBrowserFactAttribute()
        {
            if (!CurrentPermissionPostgresFactAttribute.Selected ||
                new[] { "CP6_WP0504_NODE", "CP6_WP0504_CHROME", "CP6_WP0504_SCRIPT", "CP6_WP0504_DIST" }
                    .Any(key => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))))
                Skip = "需要本卡隔离 PostgreSQL、Chrome、Node 和已构建前端。";
        }
    }

    [LegacyPlateMoldBrowserFact]
    public async Task Legacy_tool_writer_browser_exercises_both_original_pages_and_actual_Main()
    {
        await using var h = await Seed();
        await GrantLegacyTool(h);
        const string estimate = "91234567-01";
        var plate = NativePlate();
        h.Db.EstimateCalcs.Add(new()
        {
            QtnCalcNo = estimate,
            QtnCalcNoMain = 91234567,
            QtnCalcNoBranch = 1,
            QtnBaseCd = "FIXTURE",
            OrderBaseCd = "FIXTURE",
            StaffCd = "FIXTURE",
            CustomerCd = plate.CustomerCd,
            CustomerProductName1 = "WP05-04 模拟估算版模"
        });
        await AddLegacyToolPage(h, 215, "/plate-mold", "版模维护");
        await AddLegacyToolPage(h, 214, "/plate-mold-list", "版模列表");
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("CP6_WP0504_NODE")!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("CP6_WP0504_SCRIPT")!);
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
        {
            api = host.Origin.ToString(),
            dist = Environment.GetEnvironmentVariable("CP6_WP0504_DIST"),
            chrome = Environment.GetEnvironmentVariable("CP6_WP0504_CHROME"),
            evidence = Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable),
            user = "price-owner",
            password = "Price1!Password",
            tenant = h.Tenant.ToString("N"),
            estimate,
            baseCd = plate.BaseCd,
            product = plate.RepresentativeProductCd,
            supplier = plate.SupplierCd
        }));
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        Assert.True(process.ExitCode == 0, (await stderr) + "\n" + (await stdout));
        using var proof = JsonDocument.Parse(await stdout);
        Assert.True(proof.RootElement.GetProperty("passed").GetBoolean());
        var counts = await LegacyWriteCounts(h);
        Assert.Equal((2, 3, 4), counts);
        await DeviceEvidence("wp05-04-browser-proof.json", new
        {
            proof = proof.RootElement.Clone(),
            database = new { counts.Tools, counts.Orders, counts.Writes }
        });
    }

    private static async Task AddLegacyToolPage(Harness h, int menuId, string path, string label)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(row => row.MenuId == menuId);
        if (menu == null)
        {
            menu = new Sys_Menu { MenuId = menuId, MenuName = label };
            h.Db.Sys_Menus.Add(menu);
        }
        menu.RoutePath = path;
        h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menuId });
    }
}
