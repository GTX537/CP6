using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DeptServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task Dept_And_UserOrgFields_RoundTrip()
    {
        using var db = NewDb();
        var deptId = Guid.NewGuid();
        db.Sys_Depts.Add(new Sys_Dept { Id = deptId, DeptCode = "HQ", DeptName = "総本部", Path = $"/{deptId}/" });
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName = "u1", Password = "x", DeptId = deptId, Email = "u1@x.com" });
        await db.SaveChangesAsync();

        var dept = await db.Sys_Depts.SingleAsync();
        Assert.Equal("HQ", dept.DeptCode);
        Assert.Equal($"/{deptId}/", dept.Path);

        var user = await db.Sys_Users.SingleAsync();
        Assert.Equal(deptId, user.DeptId);
        Assert.Equal("u1@x.com", user.Email);
    }
}
