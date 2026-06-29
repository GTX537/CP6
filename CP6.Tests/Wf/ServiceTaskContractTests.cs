// CP6.Tests/Wf/ServiceTaskContractTests.cs
using System; using System.Collections.Generic; using System.Threading.Tasks;
using CP6.Core.Services.Wf; using Xunit;

namespace CP6.Tests.Wf;

public class ServiceTaskContractTests
{
    private sealed class FakeExec : IServiceTaskExecutor {
        public string Key => "x"; public string Kind => "dataWriteback";
        public bool VisibleInDesigner => true; public string DisplayName => "X";
        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx)
            => Task.FromResult(ServiceTaskResult.Ok(new Dictionary<string,object?>{["k"]=1}));
    }

    [Fact]
    public async Task Result_Ok_CarriesOutputVars()
    {
        var ctx = new ServiceTaskContext { InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(),
            NodeId = "n", StarterId = Guid.NewGuid(), JobId = Guid.NewGuid(), AttemptNo = 1,
            ActorId = Guid.Empty, NowUtc = new DateTime(2026,6,29,0,0,0,DateTimeKind.Utc) };
        var r = await new FakeExec().ExecuteAsync(ctx);
        Assert.True(r.Success); Assert.Equal(1, r.OutputVars!["k"]);
    }

    [Fact]
    public void Result_Fail_HasError()
    {
        var r = ServiceTaskResult.Fail("boom");
        Assert.False(r.Success); Assert.Equal("boom", r.Error);
    }
}
