using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class PasswordPolicyServiceTests
{
    private static PasswordPolicyService Make(CP6.Core.EFDbContext.CP6Context db, PasswordPolicyOptions p)
        => new(db, Options.Create(new SecurityOptions { Password = p }), new BCryptPasswordHasher());

    [Theory]
    [InlineData("Ab1!xyz9", true)]    // 8 位含大小写数字符号
    [InlineData("short1A", false)]    // < 8
    [InlineData("alllower1", false)]  // 无大写
    public void Validate_enforces_rules(string pwd, bool ok)
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = Make(db, new PasswordPolicyOptions { MinLength = 8, RequireUpper = true, RequireLower = true, RequireDigit = true, RequireSymbol = true });
        if (ok) svc.Validate(pwd);
        else Assert.Throws<InvalidOperationException>(() => svc.Validate(pwd));
    }

    [Fact] public async Task History_rejects_reuse()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var uid = Guid.NewGuid();
        db.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = uid, PasswordHash = hasher.Hash("OldPass1!"), ChangedAt = DateTime.Now });
        db.SaveChanges();
        var svc = Make(db, new PasswordPolicyOptions { HistoryCount = 3 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CheckHistoryAsync(uid, "OldPass1!"));
        await svc.CheckHistoryAsync(uid, "BrandNew9#");   // 不抛
    }
}
