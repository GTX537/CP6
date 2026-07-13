using System;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Xunit;

public class FlowTriggerModelTests
{
    [Fact]
    public void WfTriggerType_Constants()
    {
        Assert.Equal(0, WfTriggerType.Timer);
        Assert.Equal(1, WfTriggerType.Event);
        Assert.Equal(2, WfTriggerType.Message);
    }

    [Fact]
    public void Wf_FlowTrigger_Defaults()
    {
        var t = new Wf_FlowTrigger { FlowKey = "fk-demo", TriggerType = WfTriggerType.Timer, StarterUserId = Guid.NewGuid() };
        Assert.Equal("{}", t.ConfigJson);
        Assert.False(t.Enabled);
        Assert.Null(t.EventKey);
        Assert.Null(t.NextDueUtc);
        Assert.Null(t.LastFiredUtc);
        Assert.Null(t.ApiKeyHash);
    }

    [Fact]
    public void Wf_TriggerFire_Defaults()
    {
        var f = new Wf_TriggerFire { TriggerId = Guid.NewGuid(), IdempotencyKey = "k1", FiredUtc = DateTime.UtcNow, Source = WfTriggerType.Event };
        Assert.Null(f.InstanceId);
        Assert.Null(f.Error);
        Assert.Null(f.PayloadHash);
    }
}
