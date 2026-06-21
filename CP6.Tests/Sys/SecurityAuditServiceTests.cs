using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Xunit;

namespace CP6.Tests.Sys;

public class SecurityAuditServiceTests
{
    [Fact]
    public async Task Logs_event_with_fields()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new SecurityAuditService(db);

        await svc.LogAsync(SecurityEventType.LoginFailed, null, "ghost", "ACME", "9.9.9.9", "UA/1", "user not found");

        var log = db.Sys_SecurityLogs.Single();
        Assert.Equal((int)SecurityEventType.LoginFailed, log.EventType);
        Assert.Equal("ghost", log.UserName);
        Assert.Equal("ACME", log.RequestTenantCode);
        Assert.Equal("9.9.9.9", log.ClientIp);
        Assert.Equal("user not found", log.Reason);
        Assert.NotEqual(default, log.CreatedAt);
    }

    [Fact]
    public async Task Truncates_overlong_fields_to_column_length()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new SecurityAuditService(db);

        await svc.LogAsync(SecurityEventType.LoginFailed, null,
            userName: new string('u', 300),          // 列 100
            requestTenantCode: new string('t', 300),  // 列 64
            ip: new string('1', 300),                 // 列 64
            ua: new string('a', 300),                 // 列 256
            reason: new string('r', 300));            // 列 256

        var log = db.Sys_SecurityLogs.Single();
        Assert.Equal(100, log.UserName!.Length);
        Assert.Equal(64, log.RequestTenantCode!.Length);
        Assert.Equal(64, log.ClientIp!.Length);
        Assert.Equal(256, log.UserAgent!.Length);
        Assert.Equal(256, log.Reason!.Length);
    }
}
