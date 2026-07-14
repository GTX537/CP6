using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.DataProtection;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WfConnectorServiceTests
{
    private static WfConnectorService Svc(Microsoft.Data.Sqlite.SqliteConnection conn, int leaseSec = 300)
        => new(Ctx(conn), DataProtectionProvider.Create("CP6.Tests"),
               new WfsInfraOptions(), leaseSeconds: leaseSec);

    [Fact]
    public async Task Save_EncryptsAuth_ExecuteDecrypts_ReadMasks()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn);
        var id = await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "erpProd", DisplayName = "ERP", BaseUrl = "https://erp.example",
            AuthJson = "{\"type\":\"bearer\",\"token\":\"secret-123\"}", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        // 库内密文 != 明文（DataProtection Protect 后的 base64url 不含明文子串）
        using (var db = Ctx(conn))
        {
            var row = await db.Wf_Connectors.FindAsync(new object[] { id }, CancellationToken.None);
            Assert.NotNull(row!.AuthJsonEncrypted);
            Assert.DoesNotContain("secret-123", row.AuthJsonEncrypted);
        }
        // 读接口掩码（hasAuth=true，无明文）
        var view = await svc.GetAsync(id, CancellationToken.None);
        Assert.True(view!.HasAuth);
        Assert.Null(view.AuthJson);
        Assert.Equal("erpProd", view.Name);
        Assert.Equal("https://erp.example", view.BaseUrl);
        // 执行侧解密还原
        var plain = await svc.DecryptAuthAsync(id, CancellationToken.None);
        Assert.Contains("secret-123", plain);
    }

    [Fact]
    public async Task Save_NoAuth_EncryptedNull_HasAuthFalse()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn);
        var id = await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "pub", DisplayName = "公开", BaseUrl = "https://p", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        using (var db = Ctx(conn))
        {
            var row = await db.Wf_Connectors.FindAsync(new object[] { id }, CancellationToken.None);
            Assert.Null(row!.AuthJsonEncrypted);
        }
        var view = await svc.GetAsync(id, CancellationToken.None);
        Assert.False(view!.HasAuth);
        Assert.Null(await svc.DecryptAuthAsync(id, CancellationToken.None));
    }

    // E-WF-028：连接器 TimeoutSec ≥ 租约 → 拒绝保存（spec §5 line124/141，与 WfConnectorLeaseGuard 同向）。
    [Fact]
    public async Task Save_TimeoutAtOrAboveLease_E028_Rejected()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn, leaseSec: 60);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "slow", DisplayName = "S", BaseUrl = "https://x", TimeoutSec = 120, Enabled = true, // 120s ≥ 60s 租约
        }, CancellationToken.None));
        Assert.Contains("E-WF-028", ex.Message);
    }

    // 边界：TimeoutSec == 租约 也拒绝（spec「≥ 租约 → 拒绝」含等号）。
    [Fact]
    public async Task Save_TimeoutEqualsLease_E028_Rejected()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn, leaseSec: 60);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "eq", DisplayName = "E", BaseUrl = "https://x", TimeoutSec = 60, Enabled = true,
        }, CancellationToken.None));
        Assert.Contains("E-WF-028", ex.Message);
    }

    // 租约内 TimeoutSec 放行（TimeoutSec < 租约）。
    [Fact]
    public async Task Save_TimeoutBelowLease_Accepted()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn, leaseSec: 60);
        var id = await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "fast", DisplayName = "F", BaseUrl = "https://x", TimeoutSec = 59, Enabled = true,
        }, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, id);
    }

    // 更新时空 AuthJson = 保留原密文（掩码读契约必需：编辑元数据不可清空凭证；SSO UpsertAsync 同向）。
    [Fact]
    public async Task Update_EmptyAuth_KeepsExistingSecret()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn);
        var id = await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "erp", DisplayName = "ERP", BaseUrl = "https://a",
            AuthJson = "{\"type\":\"bearer\",\"token\":\"keep-me\"}", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        // 仅改元数据、AuthJson 留空 → 密文应保留
        await svc.UpdateAsync(id, new WfConnectorSaveReq
        {
            Name = "erp", DisplayName = "ERP 改", BaseUrl = "https://b", AuthJson = null, TimeoutSec = 45, Enabled = false,
        }, CancellationToken.None);

        var view = await svc.GetAsync(id, CancellationToken.None);
        Assert.Equal("ERP 改", view!.DisplayName);
        Assert.Equal("https://b", view.BaseUrl);
        Assert.Equal(45, view.TimeoutSec);
        Assert.False(view.Enabled);
        Assert.True(view.HasAuth);   // 凭证未被清空
        Assert.Contains("keep-me", await svc.DecryptAuthAsync(id, CancellationToken.None));
    }

    // 更新时提供新 AuthJson → 覆盖并重新加密。
    [Fact]
    public async Task Update_NewAuth_Reencrypts()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn);
        var id = await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "erp", DisplayName = "ERP", BaseUrl = "https://a",
            AuthJson = "{\"type\":\"bearer\",\"token\":\"old\"}", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        await svc.UpdateAsync(id, new WfConnectorSaveReq
        {
            Name = "erp", DisplayName = "ERP", BaseUrl = "https://a",
            AuthJson = "{\"type\":\"bearer\",\"token\":\"new-secret\"}", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        var plain = await svc.DecryptAuthAsync(id, CancellationToken.None);
        Assert.Contains("new-secret", plain);
        Assert.DoesNotContain("old", plain!);
    }

    [Fact]
    public async Task List_ReturnsMaskedRows_NoPlaintext()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn);
        await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "a", DisplayName = "A", BaseUrl = "https://a",
            AuthJson = "{\"type\":\"bearer\",\"token\":\"zzz-secret\"}", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        var list = await svc.ListAsync(CancellationToken.None);
        var item = Assert.Single(list);
        Assert.True(item.HasAuth);
        Assert.Null(item.AuthJson);
    }
}
