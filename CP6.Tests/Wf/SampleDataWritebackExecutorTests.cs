// CP6.Tests/Wf/SampleDataWritebackExecutorTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using Xunit;

namespace CP6.Tests.Wf;

public class SampleDataWritebackExecutorTests
{
    private static ServiceTaskContext MakeCtx(Guid jobId, string? varsJson = null) =>
        new ServiceTaskContext
        {
            InstanceId    = Guid.NewGuid(),
            TokenId       = Guid.NewGuid(),
            NodeId        = "n1",
            StarterId     = Guid.NewGuid(),
            JobId         = jobId,
            AttemptNo     = 1,
            ActorId       = Guid.Empty,
            NowUtc        = new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc),
            VarsJson      = varsJson,
            ActionRefJson = null
        };

    // ── T1: 元数据（设计器可见的 dataWriteback 动作）────────────────────────
    [Fact]
    public void Metadata_Is_DataWriteback_Visible_SampleWriteback()
    {
        var executor = new SampleDataWritebackExecutor();
        Assert.Equal("sampleWriteback", executor.Key);
        Assert.Equal("dataWriteback", executor.Kind);
        Assert.Equal(ServiceKind.DataWriteback, executor.Kind);
        Assert.True(executor.VisibleInDesigner);
        Assert.False(string.IsNullOrWhiteSpace(executor.DisplayName));
    }

    // ── T2: happy path — 读 $.amount × 1 写回 writebackEcho + 幂等键 ──────────
    [Fact]
    public async Task HappyPath_Writes_Echo_And_IdempotencyKey()
    {
        var jobId = Guid.NewGuid();
        var ctx = MakeCtx(jobId, varsJson: "{\"amount\":100}");
        var executor = new SampleDataWritebackExecutor();

        var result = await executor.ExecuteAsync(ctx);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.OutputVars);
        Assert.True(result.OutputVars!.ContainsKey("writebackEcho"));
        Assert.Equal("100", result.OutputVars["writebackEcho"]?.ToString());
        Assert.Equal($"wf-writeback-job-{jobId}", result.OutputVars["writebackIdempotencyKey"]?.ToString());
    }

    // ── T3: 律2 幂等 — 同 ctx 重复执行结果字节等价 ─────────────────────────
    [Fact]
    public async Task Idempotent_RepeatedExecution_SameResult()
    {
        var jobId = Guid.NewGuid();
        var ctx = MakeCtx(jobId, varsJson: "{\"amount\":42.5}");
        var executor = new SampleDataWritebackExecutor();

        var r1 = await executor.ExecuteAsync(ctx);
        var r2 = await executor.ExecuteAsync(ctx);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.Equal(r1.OutputVars!["writebackEcho"]?.ToString(), r2.OutputVars!["writebackEcho"]?.ToString());
        Assert.Equal("42.5", r1.OutputVars["writebackEcho"]?.ToString());
        Assert.Equal(
            r1.OutputVars["writebackIdempotencyKey"]?.ToString(),
            r2.OutputVars["writebackIdempotencyKey"]?.ToString());
    }

    // ── T4: 律1 先校验 — 缺 amount → Fail，不留半截脏改 ─────────────────────
    [Fact]
    public async Task MissingAmount_Fails_NoPartialWrite()
    {
        var ctx = MakeCtx(Guid.NewGuid(), varsJson: "{\"other\":1}");
        var executor = new SampleDataWritebackExecutor();

        var result = await executor.ExecuteAsync(ctx);

        Assert.False(result.Success);
        Assert.Null(result.OutputVars);
        Assert.NotNull(result.Error);
        Assert.Contains("E-WF-019", result.Error!);
    }

    // ── T5: 律1 先校验 — amount 非数值 → Fail ──────────────────────────────
    [Fact]
    public async Task NonNumericAmount_Fails()
    {
        var ctx = MakeCtx(Guid.NewGuid(), varsJson: "{\"amount\":\"abc\"}");
        var executor = new SampleDataWritebackExecutor();

        var result = await executor.ExecuteAsync(ctx);

        Assert.False(result.Success);
        Assert.Null(result.OutputVars);
        Assert.NotNull(result.Error);
        Assert.Contains("E-WF-019", result.Error!);
    }
}
