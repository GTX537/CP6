using System.Security.Claims;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CP6.Tests;

public class CurrentPermissionContextTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static ClaimsPrincipal Principal(Guid user, Guid tenant, string subjectType = ClaimTypes.NameIdentifier)
        => new(new ClaimsIdentity(new[] { new Claim(subjectType, user.ToString()),
            new Claim("tenant_id", tenant.ToString()), new Claim(ClaimTypes.Name, "u") }, "test"));

    internal static IDistributedCache Cache() => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    internal static CurrentPermissionContext Context(CP6Context db, ClaimsPrincipal principal, IDistributedCache? cache = null)
        => new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            cache ?? Cache(), db, new PermissionAggregator(db));

    internal static async Task<Guid> Seed(CP6Context db)
    {
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });
        db.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "reader" });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 10, MenuName = "order", MenuKey = "order" });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 10 });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 10, ActionCode = "read" });
        await db.SaveChangesAsync();
        return uid;
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task Subject_not_display_name_selects_current_user(string claimType)
    {
        using var db = NewDb();
        await Seed(db);
        var other = new Sys_User { Id = Guid.NewGuid(), UserName = "other", Password = "x" };
        db.Sys_Users.Add(other);
        await db.SaveChangesAsync();
        // Name deliberately names the privileged user; subject names the unprivileged user.
        var context = await Context(db, Principal(other.Id, db.CurrentTenantId, claimType)).GetAsync();
        Assert.Equal(other.Id, context.UserId);
        Assert.Empty(context.ActionKeys);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-subject")]
    [InlineData("missing-tenant")]
    [InlineData("bad-subject")]
    [InlineData("empty-subject")]
    [InlineData("wrong-tenant")]
    [InlineData("conflicting-subject")]
    [InlineData("conflicting-tenant")]
    [InlineData("split-identity")]
    public async Task Invalid_identity_cannot_grant(string scenario)
    {
        using var db = NewDb();
        var uid = await Seed(db);
        var principal = Principal(uid, db.CurrentTenantId);
        var identity = (ClaimsIdentity)principal.Identity!;
        if (scenario == "anonymous") principal = new ClaimsPrincipal(new ClaimsIdentity(identity.Claims));
        if (scenario == "missing-subject") identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier));
        if (scenario == "missing-tenant") identity.RemoveClaim(identity.FindFirst("tenant_id"));
        if (scenario is "bad-subject" or "empty-subject")
        {
            identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, scenario == "bad-subject" ? "service:client" : Guid.Empty.ToString()));
        }
        if (scenario == "wrong-tenant")
        {
            identity.RemoveClaim(identity.FindFirst("tenant_id"));
            identity.AddClaim(new Claim("tenant_id", Guid.NewGuid().ToString()));
        }
        if (scenario == "conflicting-subject") identity.AddClaim(new Claim("sub", Guid.NewGuid().ToString()));
        if (scenario == "conflicting-tenant") identity.AddClaim(new Claim("tenant_id", Guid.NewGuid().ToString()));
        if (scenario == "split-identity")
        {
            identity.RemoveClaim(identity.FindFirst("tenant_id"));
            principal.AddIdentity(new ClaimsIdentity(new[] { new Claim("tenant_id", db.CurrentTenantId.ToString()) }));
        }
        Assert.False(await new PermissionService(Context(db, principal)).HasActionAsync("order", "read"));
    }

    [Fact]
    public async Task Prewarm_and_old_cache_do_not_authorize_after_action_revocation()
    {
        using var db = NewDb();
        var uid = await Seed(db);
        var cache = Cache();
        var sut = Context(db, Principal(uid, db.CurrentTenantId), cache);
        var old = await sut.PrewarmAsync(uid);
        Assert.Contains("order:read", old.ActionKeys);
        await cache.SetStringAsync($"perm-ctx:{uid}", JsonSerializer.Serialize(old, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        db.Sys_RoleActions.RemoveRange(db.Sys_RoleActions);
        await db.SaveChangesAsync();
        Assert.DoesNotContain("order:read", (await sut.GetAsync()).ActionKeys);
        Assert.DoesNotContain("order:read", (await sut.PrewarmAsync(uid)).ActionKeys);
        sut.InvalidateByRole(1);
        Assert.Null(await cache.GetAsync($"perm-ctx:{uid}"));
        sut.Invalidate(uid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_deleted_user_cannot_grant(bool delete)
    {
        using var db = NewDb();
        var uid = await Seed(db);
        var sut = Context(db, Principal(uid, db.CurrentTenantId));
        Assert.Contains("order:read", (await sut.GetAsync()).ActionKeys);
        var user = await db.Sys_Users.SingleAsync();
        if (delete) db.Remove(user); else user.Enable = false;
        await db.SaveChangesAsync();
        Assert.False(await new PermissionService(sut).HasActionAsync("order", "read"));
    }

    [Fact]
    public async Task Existing_signed_token_and_matching_subject_alias_remain_usable_without_name()
    {
        using var db = NewDb();
        var uid = await Seed(db);
        var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var token = CP6.Core.Utilities.JwtHelper.GenerateToken(uid.ToString(), "old-name", secret,
            "permission-test", "permission-test", 5, db.CurrentTenantId);
        var principal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(token,
            new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidIssuer = "permission-test", ValidAudience = "permission-test",
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret)),
                ValidateIssuerSigningKey = true
            }, out _);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(ClaimTypes.Name));
        identity.AddClaim(new Claim("sub", uid.ToString()));
        var cache = new Moq.Mock<IDistributedCache>(Moq.MockBehavior.Strict);
        // Strict cache rejects every call: current authorization must work without reading/writing it.
        Assert.True(await new PermissionService(Context(db, principal, cache.Object)).HasActionAsync("order", "read"));
    }

    [Fact]
    public async Task InvalidateByRole_removes_primary_and_additional_users_legacy_entries()
    {
        using var db = NewDb();
        var primary = await Seed(db);
        var secondary = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = secondary, UserName = "secondary", Password = "x" });
        db.Sys_UserRoles.Add(new Sys_UserRole { UserId = secondary, RoleId = 1 });
        await db.SaveChangesAsync();
        var cache = Cache();
        await cache.SetStringAsync($"perm-ctx:{primary}", "old");
        await cache.SetStringAsync($"perm-ctx:{secondary}", "old");
        Context(db, Principal(primary, db.CurrentTenantId), cache).InvalidateByRole(1);
        Assert.Null(await cache.GetAsync($"perm-ctx:{primary}"));
        Assert.Null(await cache.GetAsync($"perm-ctx:{secondary}"));
    }

    [Fact]
    public async Task Query_failure_does_not_use_primed_cache()
    {
        var db = NewDb();
        var uid = await Seed(db);
        var sut = Context(db, Principal(uid, db.CurrentTenantId));
        await sut.PrewarmAsync(uid);
        await db.DisposeAsync();
        Assert.False(await new PermissionService(sut).HasMenuAsync("order"));
    }
}
