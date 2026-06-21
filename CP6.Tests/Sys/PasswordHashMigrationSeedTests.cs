using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Xunit;

namespace CP6.Tests.Sys;

public class PasswordHashMigrationSeedTests
{
    [Fact] public void Rehashes_plaintext_and_skips_already_hashed()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        db.Sys_Users.Add(new Sys_User { UserName = "plainuser", Password = "admin123" });
        var already = hasher.Hash("kept");
        db.Sys_Users.Add(new Sys_User { UserName = "hasheduser", Password = already });
        db.SaveChanges();

        var changed = PasswordHashMigrationSeed.EnsureHashed(db, hasher);

        var p = db.Sys_Users.Single(u => u.UserName == "plainuser");
        var h = db.Sys_Users.Single(u => u.UserName == "hasheduser");
        Assert.Equal(1, changed);
        Assert.True(hasher.Verify("admin123", p.Password));
        Assert.Equal(already, h.Password);
        Assert.Equal(0, PasswordHashMigrationSeed.EnsureHashed(db, hasher));
    }

    [Fact] public void Skips_empty_password_without_error()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        db.Sys_Users.Add(new Sys_User { UserName = "emptyuser", Password = "" });
        db.SaveChanges();

        var changed = PasswordHashMigrationSeed.EnsureHashed(db, hasher);

        Assert.Equal(0, changed);
        Assert.Equal("", db.Sys_Users.Single(u => u.UserName == "emptyuser").Password);
    }
}
