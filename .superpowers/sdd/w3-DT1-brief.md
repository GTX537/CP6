### Task D-T1: WfApiKeyHelper（生成/哈希/常量时间校验）

**Files:**
- Create: `CP6.Core/Services/Wf/WfApiKeyHelper.cs`
- Test: `CP6.Tests/Wf/WfApiKeyHelperTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/WfApiKeyHelperTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class WfApiKeyHelperTests
{
    [Fact]
    public void NewRawKey_Is32ByteHighEntropy_Base64Url()
    {
        var a = WfApiKeyHelper.NewRawKey();
        var b = WfApiKeyHelper.NewRawKey();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 43);                       // 32 字节 base64url ≈ 43 字符
        Assert.DoesNotContain("+", a); Assert.DoesNotContain("/", a); Assert.DoesNotContain("=", a);
    }

    [Fact]
    public void HashOf_Sha256Hex_64Chars_Deterministic()
    {
        var h1 = WfApiKeyHelper.HashOf("k");
        var h2 = WfApiKeyHelper.HashOf("k");
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);
        Assert.NotEqual("k", h1);
    }

    [Fact]
    public void Verify_RoundTrip_True_WrongKey_False()
    {
        var raw = WfApiKeyHelper.NewRawKey();
        var hash = WfApiKeyHelper.HashOf(raw);
        Assert.True(WfApiKeyHelper.Verify(raw, hash));
        Assert.False(WfApiKeyHelper.Verify(raw + "x", hash));
        Assert.False(WfApiKeyHelper.Verify("", hash));
    }

    [Fact]
    public void Verify_NullOrEmptyStoredHash_False()
    {
        Assert.False(WfApiKeyHelper.Verify("any", null));
        Assert.False(WfApiKeyHelper.Verify("any", ""));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter WfApiKeyHelperTests`）。

- [ ] **Step 3: 实现**（复刻 `RefreshTokenService.cs:31-33` 生成/哈希 + `TwoFactorService.cs:137-149` 常量时间比较先例）：

```csharp
// CP6.Core/Services/Wf/WfApiKeyHelper.cs
using System.Security.Cryptography;
using System.Text;

namespace CP6.Core.Services.Wf;

/// <summary>message 触发器 API key 基建（spec §3.4）：32 字节高熵随机，明文只在创建/重置响应显示一次，
/// 库内仅存 SHA-256 hex（泄库不可还原）；校验常量时间比较。复刻 RefreshTokenService/TwoFactorService 先例。</summary>
public static class WfApiKeyHelper
{
    public static string NewRawKey() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashOf(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static bool Verify(string raw, string? storedHash)
    {
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(storedHash)) return false;
        var candidate = HashOf(raw);
        var stored = storedHash.ToUpperInvariant();            // ToHexString 恒大写，防御性归一
        if (candidate.Length != stored.Length) return false;   // FixedTimeEquals 要求等长
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(stored));
    }

    private static string Base64Url(byte[] b)
        => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter WfApiKeyHelperTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): D-T1 WfApiKeyHelper 32字节随机+SHA-256入库+常量时间校验"
```

---


---
## 附: API key先例锚点
| API key 先例 | **无现成 API key 基建**，但三处可复刻：`TwoFactorService.cs:137-149` `Sha256Hex` + `FixedTimeEquals`（`CryptographicOperations.FixedTimeEquals`，先比长度）；`RefreshTokenService.cs:31-33` `NewRaw()`=32 字节随机 Base64Url + `HashOf()`=SHA-256 hex 入库（库内只存哈希）+ 查库 `IgnoreQueryFilters()`（令牌即凭证跨租户定位）；`RequirePlatformAdminAttribute.cs`＝自定义 `IAsyncAuthorizationFilter` 先例（特性经 `RequestServices` 服务定位取依赖，失败设 `context.Result` 短路）。 |

## 附: 共享契约(WfApiKeyHelper行)
- `WfApiKeyHelper { static string NewRawKey(); static string HashOf(string raw); static bool Verify(string raw, string? storedHash); }`
