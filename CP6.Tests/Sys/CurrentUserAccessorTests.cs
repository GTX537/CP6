using System.Security.Claims;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace CP6.Tests.Sys;
public class CurrentUserAccessorTests
{
    private static ICurrentUserAccessor Make(ClaimsPrincipal? user)
    {
        var ctx = user == null ? null : new DefaultHttpContext { User = user };
        return new CurrentUserAccessor(new HttpContextAccessor { HttpContext = ctx });
    }
    [Fact] public void Reads_claims_when_present()
    {
        var id = Guid.NewGuid();
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[]{
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "alice") }));
        var a = Make(p); Assert.Equal(id, a.UserId); Assert.Equal("alice", a.UserName);
    }
    [Fact] public void Null_when_no_httpcontext()
    { var a = Make(null); Assert.Null(a.UserId); Assert.Null(a.UserName); }
    [Fact] public void Null_userid_when_nameidentifier_not_guid()
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[]{ new Claim(ClaimTypes.NameIdentifier, "not-a-guid") }));
        var a = Make(p); Assert.Null(a.UserId);
    }
}
