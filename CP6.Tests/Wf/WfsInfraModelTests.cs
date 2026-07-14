using System;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Xunit;

namespace CP6.Tests;

public class WfsInfraModelTests
{
    [Fact]
    public void Sys_WorkCalendar_Defaults()
    {
        var c = new Sys_WorkCalendar { Date = new DateTime(2026, 1, 1), IsWorkday = false, Note = "元日" };
        Assert.False(c.IsWorkday);
        Assert.Equal("元日", c.Note);
        Assert.Equal(new DateTime(2026, 1, 1), c.Date);
    }

    [Fact]
    public void Wf_Connector_Defaults()
    {
        var k = new Wf_Connector { Name = "erpProd", DisplayName = "ERP 生产" };
        Assert.Equal("erpProd", k.Name);
        Assert.Equal("", k.BaseUrl);
        Assert.Equal(30, k.TimeoutSec);
        Assert.False(k.Enabled);
        Assert.Null(k.AuthJsonEncrypted);
        Assert.Null(k.RowVersion);
    }

    [Fact]
    public void Sys_Tenant_TimeZoneId_Nullable()
    {
        var t = new Sys_Tenant { TenantCode = "t1", TenantName = "T1" };
        Assert.Null(t.TimeZoneId);
        t.TimeZoneId = "Asia/Tokyo";
        Assert.Equal("Asia/Tokyo", t.TimeZoneId);
    }
}
