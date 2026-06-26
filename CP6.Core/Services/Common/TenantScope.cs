namespace CP6.Core.Services.Common;

/// <summary>
/// 临时切换当前租户上下文的作用域守卫（多租户合规 #5，R5）。
/// <para>构造时缓存旧租户、写入 target；<see cref="Dispose"/> 还原旧值。</para>
/// <para>用法：<c>using (new TenantScope(_tenant, TenantContext.DefaultTenant)) { await _audit.LogAsync(…); }</c>
/// —— 保证带外审计事件落在平台租户而非当前租户。</para>
/// </summary>
public sealed class TenantScope : IDisposable
{
    private readonly ITenantContext _ctx;
    private readonly Guid _previous;

    public TenantScope(ITenantContext ctx, Guid target)
    {
        _ctx = ctx;
        _previous = ctx.CurrentTenantId;
        ctx.CurrentTenantId = target;
    }

    public void Dispose() => _ctx.CurrentTenantId = _previous;
}
