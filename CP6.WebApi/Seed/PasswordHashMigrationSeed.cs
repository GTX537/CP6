using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>启动幂等：把现有明文密码就地 BCrypt 哈希（一次性原地迁移，不可逆）。返回本次哈希条数。</summary>
public static class PasswordHashMigrationSeed
{
    public static int EnsureHashed(CP6Context db, IPasswordHasher hasher)
    {
        var users = db.Sys_Users.IgnoreQueryFilters().ToList();
        var n = 0;
        foreach (var u in users)
        {
            if (string.IsNullOrEmpty(u.Password) || hasher.IsHashed(u.Password)) continue;
            u.Password = hasher.Hash(u.Password);
            n++;
        }
        if (n > 0) db.SaveChanges();
        return n;
    }
}
