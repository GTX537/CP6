using System;
using CP6.Core.Services.Wf;
using Xunit;

public class WfConnectorLeaseGuardTests
{
    private sealed class SafeConn : IWfConnector {
        public string Name => "safe"; public string DisplayName => "Safe";
        public TimeSpan? MaxCallDuration => TimeSpan.FromMinutes(1);   // < 5min 租约
        public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok());
    }
    private sealed class SlowConn : IWfConnector {
        public string Name => "slow"; public string DisplayName => "Slow";
        public TimeSpan? MaxCallDuration => TimeSpan.FromMinutes(6);   // >= 5min 租约 → 非法
        public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok());
    }
    private sealed class UndeclaredConn : IWfConnector {
        public string Name => "echo"; public string DisplayName => "Echo";
        // 不覆写 MaxCallDuration → 默认 null → 通过（假定安全，EchoConnector 同款）
        public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok());
    }

    [Fact] public void Passes_WhenAllUnderLease()
        => WfConnectorLeaseGuard.Validate(new IWfConnector[] { new SafeConn(), new UndeclaredConn() });

    [Fact] public void Throws_WhenConnectorAtOrOverLease()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => WfConnectorLeaseGuard.Validate(new IWfConnector[] { new SafeConn(), new SlowConn() }));
        Assert.Contains("slow", ex.Message);
        Assert.Contains("MaxCallDuration", ex.Message);
    }
}
