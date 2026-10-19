using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Sys;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;

namespace CP6.Tests.Sys;

[Collection("TenantManagementPostgres")]
public sealed class UserCreationBoundaryPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [TenantManagementPostgresTheory]
    [InlineData("platform")]
    [InlineData("identity")]
    [InlineData("authentication")]
    public async Task Ordinary_user_creation_cannot_set_server_owned_fields(string boundary)
    {
        var (tenant, actor) = await Seed(); var foreignTenant = Guid.NewGuid(); var suppliedId = Guid.NewGuid();
        var name = "new_" + Guid.NewGuid().ToString("N");
        var body = new Dictionary<string, object?> { ["userName"] = name, ["password"] = "Fixture-Only-Pass1!", ["enable"] = true };
        if (boundary == "platform") body["isPlatformAdmin"] = true;
        if (boundary == "identity")
        {
            body["id"] = suppliedId; body["tenantId"] = foreignTenant; body["creator"] = "forged";
            body["modifier"] = "forged"; body["modifyDate"] = "2000-01-01T00:00:00";
        }
        if (boundary == "authentication")
        {
            body["externalProvider"] = "https://unapproved.example.test"; body["externalSubject"] = "unapproved";
            body["twoFactorEnabled"] = true; body["twoFactorSecret"] = "fixture-secret";
            body["allowPasswordFallback"] = true; body["quickPinHash"] = "fixture-pin";
            body["failedLoginCount"] = 88; body["badgeNo"] = "fixture-badge";
        }
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        using var response = await Send(host, host.Token(actor, tenant, platform: false), body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Context(tenant);
        var user = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.UserName == name);
        if (boundary == "platform") Assert.False(user.IsPlatformAdmin);
        if (boundary == "identity")
        {
            Assert.Equal(tenant, user.TenantId); Assert.NotEqual(suppliedId, user.Id);
            Assert.NotEqual("forged", user.Creator); Assert.Null(user.Modifier); Assert.Null(user.ModifyDate);
        }
        if (boundary == "authentication")
        {
            Assert.Null(user.ExternalProvider); Assert.Null(user.ExternalSubject);
            Assert.False(user.TwoFactorEnabled); Assert.Null(user.TwoFactorSecret);
            Assert.False(user.AllowPasswordFallback); Assert.Null(user.QuickPinHash);
            Assert.Null(user.BadgeNo); Assert.Equal(0, user.FailedLoginCount);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Legal_creation_preserves_inputs_hashes_password_and_uses_server_identity()
    {
        var (tenant, actor) = await Seed(); await using var db = Context(tenant);
        var dept = new Sys_Dept { DeptCode = "D", DeptName = "Department", Path = "/" };
        db.Sys_Depts.Add(dept); await db.SaveChangesAsync();
        var name = "legal_" + Guid.NewGuid().ToString("N"); const string password = "Fixture-Creation-Pass1!";
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        var before = DateTime.Now.AddSeconds(-1);
        using var response = await Send(host, host.Token(actor, tenant, platform: false), new
        {
            userName = name, password, nickName = "legal nickname", roleId = 31, enable = false,
            deptId = dept.Id, managerId = actor, email = "fixture@example.test"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid(); Assert.NotEqual(Guid.Empty, id);
        Assert.False(body.RootElement.TryGetProperty("password", out _));
        var user = await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(tenant, user.TenantId); Assert.Equal(name, user.UserName); Assert.Equal("legal nickname", user.NickName);
        Assert.Equal(31, user.RoleId); Assert.Equal(dept.Id, user.DeptId); Assert.Equal(actor, user.ManagerId);
        Assert.Equal("fixture@example.test", user.Email); Assert.False(user.Enable); Assert.False(user.IsPlatformAdmin);
        Assert.True(new BCryptPasswordHasher(4).Verify(password, user.Password)); Assert.True(user.MustChangePassword);
        Assert.True(user.PasswordChangedAt >= before); Assert.True(user.CreateDate >= before); Assert.Equal("operator", user.Creator);
    }

    [TenantManagementPostgresTheory]
    [InlineData("userName", "")]
    [InlineData("password", "")]
    [InlineData("userName", "overlong")]
    [InlineData("password", "overlong")]
    [InlineData("email", "overlong")]
    [InlineData("roleId", "invalid-integer")]
    public async Task Invalid_creation_does_not_save_user(string field, string value)
    {
        var (tenant, actor) = await Seed(); await using var db = Context(tenant); var before = await db.Sys_Users.CountAsync();
        var body = new Dictionary<string, object?> { ["userName"] = "invalid_" + Guid.NewGuid().ToString("N"), ["password"] = "Fixture-Pass1!" };
        body[field] = value == "overlong" ? new string('x', field == "password" ? 201 : 101) : value;
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        using var response = await Send(host, host.Token(actor, tenant, platform: false), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await db.Sys_Users.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Current_permission_revocation_and_missing_authentication_reject_creation()
    {
        var (tenant, actor) = await Seed(); await using var db = Context(tenant); var before = await db.Sys_Users.CountAsync();
        await using var host = await TenantManagementHttpHost.Start(server, includePlatformAdmins: true);
        var token = host.Token(actor, tenant, platform: false);
        await db.Sys_RoleActions.ExecuteDeleteAsync();
        using var revoked = await Send(host, token, new { userName = "denied", password = "Fixture-Pass1!" });
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using var invalid = await Send(host, "not-a-token", new { userName = "denied", password = "Fixture-Pass1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode); Assert.Equal(before, await db.Sys_Users.CountAsync());
    }

    [Fact]
    public void Generated_create_body_schema_does_not_offer_identity_or_authentication_controls()
    {
        var type = typeof(UserController).GetMethod(nameof(UserController.Add))!.GetParameters()[0].ParameterType;
        var repository = new SchemaRepository();
        var generator = new SchemaGenerator(new SchemaGeneratorOptions(), new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var reference = generator.GenerateSchema(type, repository); var schema = repository.Schemas[reference.Reference.Id];
        foreach (var prohibited in new[] { "id", "tenantId", "isPlatformAdmin", "externalProvider", "twoFactorSecret", "quickPinHash", "creator" })
            Assert.DoesNotContain(prohibited, schema.Properties.Keys);
        Assert.Contains("userName", schema.Required); Assert.Contains("password", schema.Required);
        Assert.Equal(100, schema.Properties["userName"].MaxLength); Assert.Equal(200, schema.Properties["password"].MaxLength);
    }

    private CP6Context Context(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });
    private async Task<(Guid Tenant, Guid Actor)> Seed()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "create boundary" });
        db.Sys_Roles.Add(new Sys_Role { RoleId = 31, RoleName = "user creator" });
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "user");
        if (menu is null) { menu = new Sys_Menu { MenuId = 72310, MenuKey = "user", MenuName = "Users" }; db.Sys_Menus.Add(menu); }
        db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 31, MenuId = menu.MenuId, ActionCode = "add" });
        var actor = new Sys_User { UserName = "creator", Password = "fixture", Enable = true, RoleId = 31 };
        db.Sys_Users.Add(actor); await db.SaveChangesAsync(); return (tenant, actor.Id);
    }
    private static async Task<HttpResponseMessage> Send(TenantManagementHttpHost host, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/user") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await host.Client.SendAsync(request);
    }
}
