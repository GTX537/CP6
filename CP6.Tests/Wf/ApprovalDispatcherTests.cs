using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

/// <summary>OA 章05 §4 终态分发器（A-1）：按 BizType 路由回调 / 原生表单不回调 / 未注册抛 / 上下文透传。</summary>
public class ApprovalDispatcherTests
{
    private sealed class FakeCb : IApprovalCallback
    {
        public string BizType => "PO";
        public List<ApprovalCallbackContext> Approved = new();
        public List<ApprovalCallbackContext> Rejected = new();
        public Task OnApprovedAsync(ApprovalCallbackContext ctx) { Approved.Add(ctx); return Task.CompletedTask; }
        public Task OnRejectedAsync(ApprovalCallbackContext ctx) { Rejected.Add(ctx); return Task.CompletedTask; }
    }

    private sealed class ThrowingCb : IApprovalCallback
    {
        public string BizType => "PO";
        public Task OnApprovedAsync(ApprovalCallbackContext ctx) => throw new InvalidOperationException("业务落地失败");
        public Task OnRejectedAsync(ApprovalCallbackContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public async Task Finished_Approved_CallsBizCallback_WithContext()
    {
        var cb = new FakeCb();
        var disp = new ApprovalDispatcher(new IApprovalCallback[] { cb });
        var decider = Guid.NewGuid();
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), BizType = "PO", BizId = "PO-001", StarterId = Guid.NewGuid() };

        await disp.OnInstanceFinishedAsync(inst, approved: true, decidedBy: decider, reason: null);

        var ctx = Assert.Single(cb.Approved);
        Assert.Empty(cb.Rejected);
        Assert.Equal("PO-001", ctx.BizId);
        Assert.Equal(inst.Id, ctx.InstanceId);
        Assert.Equal(inst.StarterId, ctx.StarterId);
        Assert.Equal(decider, ctx.DecidedById);
    }

    [Fact]
    public async Task Finished_Rejected_CallsOnRejected_WithReason()
    {
        var cb = new FakeCb();
        var disp = new ApprovalDispatcher(new IApprovalCallback[] { cb });
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), BizType = "PO", BizId = "PO-002", StarterId = Guid.NewGuid() };

        await disp.OnInstanceFinishedAsync(inst, approved: false, decidedBy: Guid.NewGuid(), reason: "预算超额");

        var ctx = Assert.Single(cb.Rejected);
        Assert.Empty(cb.Approved);
        Assert.Equal("预算超额", ctx.Reason);
    }

    [Fact]
    public async Task Finished_NativeForm_NoBizType_NoCallback()
    {
        var disp = new ApprovalDispatcher(Array.Empty<IApprovalCallback>());
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), BizType = "", StarterId = Guid.NewGuid() };

        // 原生 OA 表单（无 BizType）→ 不抛、不回调
        await disp.OnInstanceFinishedAsync(inst, approved: true, decidedBy: null, reason: null);
    }

    [Fact]
    public async Task Finished_UnregisteredBizType_Throws()
    {
        var disp = new ApprovalDispatcher(Array.Empty<IApprovalCallback>());
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), BizType = "PO", BizId = "PO-003", StarterId = Guid.NewGuid() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disp.OnInstanceFinishedAsync(inst, approved: true, decidedBy: null, reason: null));
    }

    [Fact]
    public async Task Finished_CallbackThrows_PropagatesForRollback()
    {
        var disp = new ApprovalDispatcher(new IApprovalCallback[] { new ThrowingCb() });
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), BizType = "PO", BizId = "PO-004", StarterId = Guid.NewGuid() };

        // 回调抛异常须上抛（由引擎让整体事务回滚，绝不"OA 通过、业务没落地"）
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disp.OnInstanceFinishedAsync(inst, approved: true, decidedBy: null, reason: null));
    }
}
