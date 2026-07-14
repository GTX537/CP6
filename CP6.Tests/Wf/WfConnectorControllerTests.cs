// D-T2 连接器管理控制器（薄壳）：直 new 控制器 + SQLite + DataProtection 测试 provider。
// 锁：列表掩码（无明文 AuthJson）/ 创建后 HasAuth=true / E-WF-028 保存返 400 / 启停切换。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.WebApi.Controllers.Oa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WfConnectorControllerTests
{
    private static WfConnectorController Ctrl(SqliteConnection conn, int leaseSec = 300)
        => new(new WfConnectorService(Ctx(conn), DataProtectionProvider.Create("CP6.Tests"),
                                      new WfsInfraOptions(), leaseSeconds: leaseSec));

    /// <summary>剥 Ok2 信封 { code, message, data } 的 data 段。</summary>
    private static object? Data(IActionResult r)
        => r is OkObjectResult ok ? ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value) : null;

    private static WfConnectorSaveReq Req(string name, string? auth, int timeout = 30, bool enabled = true)
        => new() { Name = name, DisplayName = name.ToUpper(), BaseUrl = "https://x", AuthJson = auth, TimeoutSec = timeout, Enabled = enabled };

    [Fact]
    public async Task List_ReturnsMasked_NoPlaintext()
    {
        using var conn = NewSqliteWithSchema();
        var ctrl = Ctrl(conn);
        await ctrl.Create(Req("erp", "{\"type\":\"bearer\",\"token\":\"secret-xyz\"}"), CancellationToken.None);

        var list = Assert.IsAssignableFrom<IReadOnlyList<WfConnectorView>>(Data(await ctrl.List(CancellationToken.None)));
        var item = Assert.Single(list);
        Assert.True(item.HasAuth);
        Assert.Null(item.AuthJson);   // 读端点恒无明文
    }

    [Fact]
    public async Task Create_ThenGet_HasAuthTrue()
    {
        using var conn = NewSqliteWithSchema();
        var ctrl = Ctrl(conn);
        var created = Data(await ctrl.Create(Req("erp", "{\"type\":\"bearer\",\"token\":\"t\"}"), CancellationToken.None));
        var id = (Guid)created!.GetType().GetProperty("id")!.GetValue(created)!;

        var view = Assert.IsType<WfConnectorView>(Data(await ctrl.Get(id, CancellationToken.None)));
        Assert.True(view.HasAuth);
        Assert.Equal("erp", view.Name);
    }

    [Fact]
    public async Task Create_TimeoutAtOrAboveLease_E028_Returns400()
    {
        using var conn = NewSqliteWithSchema();
        var ctrl = Ctrl(conn, leaseSec: 60);
        var res = await ctrl.Create(Req("slow", null, timeout: 120), CancellationToken.None);   // 120 ≥ 60 租约
        var bad = Assert.IsType<BadRequestObjectResult>(res);
        // message 恰为纯错误码（可命中 i18n seed 键 E-WF-028，前端 http.ts 裸 t(raw) 不拆 |）；
        // 诊断后缀入 detail 不丢。（F-T1 审查 Important 修复。）
        var msg = (string)bad.Value!.GetType().GetProperty("message")!.GetValue(bad.Value)!;
        Assert.Equal("E-WF-028", msg);
        var detail = (string?)bad.Value!.GetType().GetProperty("detail")!.GetValue(bad.Value);
        Assert.NotNull(detail);
        Assert.Contains("timeoutGteLease", detail);
    }

    [Fact]
    public async Task SetEnabled_Toggles()
    {
        using var conn = NewSqliteWithSchema();
        var ctrl = Ctrl(conn);
        var created = Data(await ctrl.Create(Req("erp", null, enabled: true), CancellationToken.None));
        var id = (Guid)created!.GetType().GetProperty("id")!.GetValue(created)!;

        await ctrl.SetEnabled(id, new WfConnectorController.EnableReq(false), CancellationToken.None);

        var view = Assert.IsType<WfConnectorView>(Data(await ctrl.Get(id, CancellationToken.None)));
        Assert.False(view.Enabled);
    }
}
