using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// T5(SSO) SsoService.HandleCallbackAsync — 换码接缝 + ID Token 验签(签名/iss/aud/exp/nonce)
/// + 映射(6a 联邦命中 / 6b 邮箱回填·改绑冲突 / 6c JIT 必配 DefaultRoleId)。
/// InMemory CP6Context + 真 SsoStateStore(MemoryDistributedCache) + 假 IOidcDiscovery(本地 HMAC key)
/// + 假 ISsoTokenClient(返本地签名 id_token)。claim-mapping 全程 MapInboundClaims=false（raw "sub"/"email"）。
/// </summary>
public class SsoCallbackTests
{
    private const string Issuer = "https://idp.example.com";
    private const string ClientId = "cp6-sp";
    private const string Email = "alice@example.com";
    private const string Sub = "idp-sub-001";
    private const string RedirectUri = "https://cp6.example.com/sso/callback";

    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    // ≥256-bit 对称密钥：discovery 与签发 ID Token 共用 → 验签可过。
    private static readonly byte[] KeyBytes = RandomNumberGenerator.GetBytes(32);
    private static SymmetricSecurityKey SigningKey => new(KeyBytes);

    // ───── 假依赖 ─────

    private sealed class FakeDiscovery : IOidcDiscovery
    {
        public Task<OidcEndpoints> GetAsync(string authority) => Task.FromResult(new OidcEndpoints(
            $"{Issuer}/authorize", $"{Issuer}/token", Issuer,
            new List<SecurityKey> { SigningKey }));
    }

    private sealed class FakeTokenClient : ISsoTokenClient
    {
        private readonly string? _idToken;
        public FakeTokenClient(string? idToken) => _idToken = idToken;
        public Task<string?> ExchangeCodeForIdTokenAsync(OidcEndpoints ep, Sys_TenantSsoConfig cfg, string clientSecret,
                                                          string code, string codeVerifier, string redirectUri)
            => Task.FromResult(_idToken);
    }

    private sealed class FakeConfig : ITenantSsoConfigService
    {
        private readonly Sys_TenantSsoConfig? _cfg;
        public FakeConfig(Sys_TenantSsoConfig? cfg) => _cfg = cfg;
        public Task<Sys_TenantSsoConfig?> GetByTenantCodeAsync(string tenantCode) => Task.FromResult(_cfg);
        public Task<Sys_TenantSsoConfig?> GetByTenantIdAsync(Guid tenantId) => Task.FromResult(_cfg);
        public Task UpsertAsync(Guid tenantId, SsoConfigInput input) => Task.CompletedTask;
        public string DecryptClientSecret(Sys_TenantSsoConfig cfg) => "secret";
    }

    private static Sys_TenantSsoConfig Cfg(bool enabled = true, bool autoProvision = false, int? defaultRoleId = null,
                                           string emailClaim = "email") => new()
    {
        TenantId = TenantId,
        Authority = Issuer,
        ClientId = ClientId,
        Scopes = "openid email profile",
        EmailClaim = emailClaim,
        Enabled = enabled,
        AutoProvision = autoProvision,
        DefaultRoleId = defaultRoleId,
    };

    /// <summary>签发测试 ID Token（raw claim 名，HMAC-SHA256）。各参数缺省=合法值；传入可制造坏 token。</summary>
    private static string IssueIdToken(
        string nonce,
        string? issuer = Issuer,
        string? audience = ClientId,
        string sub = Sub,
        string? email = Email,
        string emailVerified = "true",
        DateTime? expires = null,
        SecurityKey? key = null,
        string emailClaimName = "email")
    {
        var claims = new List<Claim>
        {
            new("sub", sub),
            new("nonce", nonce),
            new("email_verified", emailVerified),
        };
        if (email != null) claims.Add(new Claim(emailClaimName, email));

        var exp = expires ?? DateTime.UtcNow.AddMinutes(10);
        var creds = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: exp.AddMinutes(-30),   // 始终早于 expires（含过期场景，否则构造抛 IDX12401）
            expires: exp,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static CP6Context Db()
    {
        var opt = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CP6Context(opt, new TenantContext { CurrentTenantId = TenantId });
    }

    private static SsoStateStore Store() =>
        new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new SecurityOptions()));

    /// <summary>组装被测 svc + 真 store；返回 (svc, store, db) 供断言落库结果。</summary>
    private static (SsoService svc, SsoStateStore store, CP6Context db) Make(
        Sys_TenantSsoConfig? cfg, string? idToken, CP6Context? db = null)
    {
        db ??= Db();
        var store = Store();
        var svc = new SsoService(
            new FakeConfig(cfg), store, new FakeDiscovery(),
            new FakeTokenClient(idToken), db,
            new SecurityAuditService(db), new TenantContext { CurrentTenantId = TenantId },
            new BCryptPasswordHasher());
        return (svc, store, db);
    }

    /// <summary>用真 store 造一个合法 state，并按其 nonce 签发匹配的 ID Token。</summary>
    private static (string state, string nonce) MintState(SsoStateStore store)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var state = store.Create(TenantId, nonce, "verifier-xyz", "/home");
        return (state, nonce);
    }

    private static void SeedTenant(CP6Context db, bool enable = true)
    {
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantId, TenantCode = "T01", TenantName = "T01", Enable = enable });
        db.SaveChanges();
    }

    // ───────────────────────────── Tests ─────────────────────────────

    [Fact]
    public async Task Valid_token_maps_existing_user_by_external_subject()
    {
        var db = Db();
        SeedTenant(db);
        var existing = new Sys_User
        {
            TenantId = TenantId, UserName = "alice", Email = Email, Password = "h", Enable = true,
            ExternalProvider = Issuer, ExternalSubject = Sub, RoleId = 5,
        };
        db.Sys_Users.Add(existing);
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);
        var user = await svc.HandleCallbackAsync("code", state, RedirectUri);

        Assert.Equal(existing.Id, user.Id);
        Assert.Equal(5, user.RoleId);
    }

    [Fact]
    public async Task Email_match_links_external_identity()
    {
        var db = Db();
        SeedTenant(db);
        var existing = new Sys_User
        {
            TenantId = TenantId, UserName = "alice", Email = Email, Password = "h", Enable = true,
            ExternalProvider = null, ExternalSubject = null, RoleId = 5,
        };
        db.Sys_Users.Add(existing);
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);
        var user = await svc.HandleCallbackAsync("code", state, RedirectUri);

        Assert.Equal(existing.Id, user.Id);
        Assert.Equal(Sub, user.ExternalSubject);
        Assert.Equal(Issuer, user.ExternalProvider);
    }

    [Fact]
    public async Task Email_match_with_different_bound_subject_conflicts()
    {
        var db = Db();
        SeedTenant(db);
        db.Sys_Users.Add(new Sys_User
        {
            TenantId = TenantId, UserName = "alice", Email = Email, Password = "h", Enable = true,
            ExternalProvider = "https://other-idp", ExternalSubject = "other-sub", RoleId = 5,
        });
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-029", ex.Message);
    }

    [Fact]
    public async Task Email_multi_match_conflicts()
    {
        var db = Db();
        SeedTenant(db);
        db.Sys_Users.Add(new Sys_User { TenantId = TenantId, UserName = "alice1", Email = Email, Password = "h", Enable = true });
        db.Sys_Users.Add(new Sys_User { TenantId = TenantId, UserName = "alice2", Email = Email, Password = "h", Enable = true });
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-029", ex.Message);
    }

    [Fact]
    public async Task Jit_provisions_when_autoprovision_and_default_role()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(autoProvision: true, defaultRoleId: 2), IssueIdToken(nonce), db, store);

        var user = await svc.HandleCallbackAsync("code", state, RedirectUri);

        Assert.Equal(Email, user.UserName);
        Assert.Equal(Email, user.Email);
        Assert.Equal(Sub, user.ExternalSubject);
        Assert.Equal(Issuer, user.ExternalProvider);
        Assert.Equal(2, user.RoleId);
        Assert.True(user.Enable);
        Assert.False(string.IsNullOrEmpty(user.Password));   // 随机密码哈希
        Assert.False(user.MustChangePassword);
        Assert.NotEqual(Guid.Empty, user.Id);

        // 持久化 + 审计 SsoUserProvisioned(17)
        var saved = await db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == Email);
        Assert.NotNull(saved);
        var audit = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.EventType == (int)SecurityEventType.SsoUserProvisioned);
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task No_match_without_autoprovision_or_role_rejected()
    {
        // (a) AutoProvision=false
        {
            var db = Db();
            SeedTenant(db);
            var store = Store();
            var (state, nonce) = MintState(store);
            var (svc, _, _) = MakeWithStore(Cfg(autoProvision: false, defaultRoleId: 2), IssueIdToken(nonce), db, store);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.HandleCallbackAsync("code", state, RedirectUri));
            Assert.Equal("E-SEC-026", ex.Message);
        }
        // (b) DefaultRoleId=null
        {
            var db = Db();
            SeedTenant(db);
            var store = Store();
            var (state, nonce) = MintState(store);
            var (svc, _, _) = MakeWithStore(Cfg(autoProvision: true, defaultRoleId: null), IssueIdToken(nonce), db, store);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.HandleCallbackAsync("code", state, RedirectUri));
            Assert.Equal("E-SEC-026", ex.Message);
        }
    }

    [Fact]
    public async Task Invalid_state_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var (svc, _, _) = Make(Cfg(), IssueIdToken("any-nonce"), db: db);

        // 不存在的 state
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", "no-such-state", RedirectUri));
        Assert.Equal("E-SEC-022", ex.Message);
    }

    [Fact]
    public async Task Consumed_state_replay_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var existing = new Sys_User
        {
            TenantId = TenantId, UserName = "alice", Email = Email, Password = "h", Enable = true,
            ExternalProvider = Issuer, ExternalSubject = Sub, RoleId = 5,
        };
        db.Sys_Users.Add(existing);
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);

        // 首次成功消费
        var user = await svc.HandleCallbackAsync("code", state, RedirectUri);
        Assert.Equal(existing.Id, user.Id);

        // 重放：state 已被消费 → E-SEC-022
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-022", ex.Message);
    }

    [Fact]
    public async Task Token_exchange_fail_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, _) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), idToken: null, db: db, store: store);   // 假 token client 返 null

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-023", ex.Message);
    }

    [Fact]
    public async Task Bad_token_wrong_signature_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        var otherKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce, key: otherKey), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-024", ex.Message);
    }

    [Fact]
    public async Task Bad_token_wrong_issuer_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce, issuer: "https://evil-idp"), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-024", ex.Message);
    }

    [Fact]
    public async Task Bad_token_wrong_audience_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce, audience: "some-other-client"), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-024", ex.Message);
    }

    [Fact]
    public async Task Bad_token_expired_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        // 过期超出 5min ClockSkew
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce, expires: DateTime.UtcNow.AddMinutes(-10)), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-024", ex.Message);
    }

    [Fact]
    public async Task Bad_token_nonce_mismatch_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, _) = MintState(store);
        // 用不匹配的 nonce 签发（签名/iss/aud/exp 都对）
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken("totally-different-nonce"), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-024", ex.Message);
    }

    [Fact]
    public async Task Email_missing_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce, email: null), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-025", ex.Message);
    }

    [Fact]
    public async Task Email_unverified_rejected()
    {
        var db = Db();
        SeedTenant(db);
        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce, emailVerified: "false"), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-025", ex.Message);
    }

    [Fact]
    public async Task Disabled_user_rejected()
    {
        var db = Db();
        SeedTenant(db);
        db.Sys_Users.Add(new Sys_User
        {
            TenantId = TenantId, UserName = "alice", Email = Email, Password = "h", Enable = false,
            ExternalProvider = Issuer, ExternalSubject = Sub, RoleId = 5,
        });
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-027", ex.Message);
    }

    [Fact]
    public async Task Disabled_tenant_rejected()
    {
        var db = Db();
        SeedTenant(db, enable: false);
        db.Sys_Users.Add(new Sys_User
        {
            TenantId = TenantId, UserName = "alice", Email = Email, Password = "h", Enable = true,
            ExternalProvider = Issuer, ExternalSubject = Sub, RoleId = 5,
        });
        await db.SaveChangesAsync();

        var store = Store();
        var (state, nonce) = MintState(store);
        var (svc, _, _) = MakeWithStore(Cfg(), IssueIdToken(nonce), db, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.HandleCallbackAsync("code", state, RedirectUri));
        Assert.Equal("E-SEC-027", ex.Message);
    }

    [Fact]
    public async Task Config_null_or_disabled_rejected()
    {
        // cfg null
        {
            var db = Db();
            SeedTenant(db);
            var store = Store();
            var (state, _) = MintState(store);
            var (svc, _, _) = MakeWithStore(cfg: null, idToken: IssueIdToken("n"), db: db, store: store);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.HandleCallbackAsync("code", state, RedirectUri));
            Assert.Equal("E-SEC-020", ex.Message);
        }
        // cfg disabled
        {
            var db = Db();
            SeedTenant(db);
            var store = Store();
            var (state, _) = MintState(store);
            var (svc, _, _) = MakeWithStore(Cfg(enabled: false), IssueIdToken("n"), db, store);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.HandleCallbackAsync("code", state, RedirectUri));
            Assert.Equal("E-SEC-020", ex.Message);
        }
    }

    /// <summary>同 Make 但复用外部传入 store（保证 state 与被测 svc 同源）。</summary>
    private static (SsoService svc, SsoStateStore store, CP6Context db) MakeWithStore(
        Sys_TenantSsoConfig? cfg, string? idToken, CP6Context db, SsoStateStore store)
    {
        var svc = new SsoService(
            new FakeConfig(cfg), store, new FakeDiscovery(),
            new FakeTokenClient(idToken), db,
            new SecurityAuditService(db), new TenantContext { CurrentTenantId = TenantId },
            new BCryptPasswordHasher());
        return (svc, store, db);
    }
}
