namespace CP6.Core.Services.Oa;

/// <summary>代理 act-as 授权（umbrella §4.4）。复用 Wf_FlowDelegate。</summary>
public interface IDelegateService
{
    Task<MyGrants> MyGrantsAsync(Guid userId);                                  // 我能代理谁 / 谁能代理我
    Task AssertActiveGrantAsync(Guid delegateId, Guid grantorId);              // 校验 me 可 act-as grantor，否则 E-WF-001
    Task<IReadOnlyList<DelegateItem>> ListMyDelegatesAsync(Guid grantorId);    // 我授出的委派（設定页）
    Task<Guid> AddDelegateAsync(Guid grantorId, Guid delegateId, DateTime from, DateTime to, string? scope, string? remark);
    Task RemoveDelegateAsync(Guid grantorId, Guid id);                         // 仅能删自己授出的
}
