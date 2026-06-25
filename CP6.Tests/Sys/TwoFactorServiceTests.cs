using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OtpNet;
using Xunit;

namespace CP6.Tests.Sys;

public class TwoFactorServiceTests
{
    private static (TwoFactorService svc, IDistributedCache cache, FakeEmail email, Sys_User user) Make(
        int tenantMode = 1, bool enabled = false, string email = "u@a.com")
    {
        var db = TestHelper.CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var t = new Sys_Tenant { Id = tenantId, TenantCode = "T", TenantName = "T", TwoFactorMode = tenantMode };
        db.Sys_Tenants.Add(t);
        var user = new Sys_User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = "alice",
            Email = email,
            TwoFactorEnabled = enabled
        };
        db.Sys_Users.Add(user);
        db.SaveChanges();

        var totp = new TotpService(Options.Create(new SecurityOptions
        {
            TwoFactor = new TwoFactorOptions { Issuer = "CP6", CodeWindow = 1, EmailOtpLength = 6, EmailOtpMinutes = 5, EmailResendCooldownSeconds = 60, PendingTokenMinutes = 5 }
        }));
        var fake = new FakeEmail();
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var audit = new SecurityAuditService(db, NullLogger<SecurityAuditService>.Instance);
        var svc = new TwoFactorService(db, totp, fake, cache, audit, Options.Create(new SecurityOptions()));
        return (svc, cache, fake, user);
    }

    [Fact]
    public void MustEnroll_only_when_required_and_not_enabled()
    {
        var (svc, _, _, user) = Make();
        // mode=0 关闭：无需挑战
        Assert.False(svc.IsChallengeRequired(user, 0));
        Assert.False(svc.MustEnroll(user, 0));
        // mode=1 可选 + 未启用：无需挑战、无需入会
        Assert.False(svc.IsChallengeRequired(user, 1));
        Assert.False(svc.MustEnroll(user, 1));
        // mode=2 强制 + 未启用：需挑战 + 必入会
        Assert.True(svc.IsChallengeRequired(user, 2));
        Assert.True(svc.MustEnroll(user, 2));
        // 已启用 + mode=任意：需挑战、不入会
        user.TwoFactorEnabled = true;
        Assert.True(svc.IsChallengeRequired(user, 0));
        Assert.False(svc.MustEnroll(user, 2));
    }

    [Fact]
    public async Task ConfirmEnrollment_sets_enabled_with_valid_totp()
    {
        var (svc, _, _, user) = Make(tenantMode: 2);
        var uri = svc.BeginEnrollment(user);
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.False(user.TwoFactorEnabled); // Begin 不置 Enabled
        Assert.NotNull(user.TwoFactorSecret);

        var code = new Totp(Base32Encoding.ToBytes(user.TwoFactorSecret!)).ComputeTotp();
        Assert.True(await svc.ConfirmEnrollmentAsync(user, code));
        Assert.True(user.TwoFactorEnabled);
        Assert.NotNull(user.TwoFactorEnrolledAt);
    }

    [Fact]
    public void BeginEnrollment_rejects_when_already_enabled()
    {
        var (svc, _, _, user) = Make(enabled: true);
        var ex = Assert.Throws<InvalidOperationException>(() => svc.BeginEnrollment(user));
        Assert.Equal("E-SEC-017", ex.Message);
    }

    [Fact]
    public async Task Email_otp_one_time_and_rate_limit_and_no_email()
    {
        var (svc, _, email, user) = Make();
        await svc.SendEmailOtpAsync(user, "key1");
        Assert.Single(email.Sent);
        var otp = email.LastCode!;
        Assert.True(await svc.VerifyEmailOtpAsync("key1", otp));
        Assert.False(await svc.VerifyEmailOtpAsync("key1", otp)); // 一次性

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendEmailOtpAsync(user, "key1"));
        // 注：上面 Verify 已 Remove otp 但 cooldown 仍在 → 命中限流
        Assert.Equal("E-SEC-016", ex.Message);

        // 无邮箱
        user.Email = "";
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendEmailOtpAsync(user, "key2"));
        Assert.Equal("E-SEC-015", ex2.Message);
    }

    [Fact]
    public async Task Send_email_failure_throws_E_SEC_018_and_clears_otp()
    {
        var (svc, cache, email, user) = Make();
        email.ThrowOnNext = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendEmailOtpAsync(user, "fail-key"));
        Assert.Equal("E-SEC-018", ex.Message);
        Assert.Null(await cache.GetStringAsync("sec:2fa:otp:fail-key")); // 已清
    }

    [Fact]
    public async Task Reset_clears_secret_and_enabled()
    {
        var (svc, _, _, user) = Make(enabled: true);
        user.TwoFactorSecret = "JBSWY3DPEHPK3PXP";
        user.TwoFactorEnrolledAt = DateTime.Now;
        await svc.ResetAsync(user, "admin-reset");
        Assert.False(user.TwoFactorEnabled);
        Assert.Null(user.TwoFactorSecret);
        Assert.Null(user.TwoFactorEnrolledAt);
    }

    private class FakeEmail : IEmailSender
    {
        public List<(string to, string subject, string body)> Sent { get; } = new();
        public string? LastCode { get; private set; }
        public bool ThrowOnNext { get; set; }
        public Task SendAsync(string to, string subject, string body)
        {
            if (ThrowOnNext) { ThrowOnNext = false; throw new InvalidOperationException("smtp boom"); }
            Sent.Add((to, subject, body));
            // body 形如 "您的 CP6 验证码：123456（5 分钟内有效）" → 取「：」后第一段连续数字
            var idx = body.IndexOf('：');
            var tail = idx >= 0 ? body.Substring(idx + 1) : body;
            LastCode = new string(tail.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return Task.CompletedTask;
        }
    }
}
