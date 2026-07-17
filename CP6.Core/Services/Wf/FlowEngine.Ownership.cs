using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 引擎归属闸（P0 越权代批封堵，M-OA/WF 票#1）：四变更方法（ActOnce/Transfer/SendBack/AddSign）
/// 统一断言 actor 有权处置该任务。放行三路径：
///   ① 本人：actorId == task.AssigneeId；
///   ② act-as 委派：onBehalfOf == task.AssigneeId 且 Wf_FlowDelegates 存在有效授权
///      （Enable && ValidFrom&lt;=now&lt;=ValidTo，谓词与 DelegateService.Active() 同款）——引擎侧复验，
///      不再仅信控制器 AssertActiveGrant（防御纵深：未来新调用方绕过控制器闸也拦得住）。
///      注意：引擎侧委派复验仅存在于 Act/ActAs 路径（唯它携带 onBehalfOf）；Transfer/SendBack 的
///      act-as 由控制器 EffectiveAsync 解析为被代理人后走①本人路径，委派真伪仍由控制器闸把关；
///   ③ 系统身份：actorId == SystemActor(Guid.Empty)——超时 worker 硬动作（WfTimeoutService）。
///      JWT 登录用户 UserId 恒非 Empty，该路径不可从 HTTP 面伪造。
/// 违规抛 E-WF-029（非本人待办）；act-as 无效委派抛 E-WF-001（复用既有码）。
/// 设计决策：委派代理人旧栈直办（onBehalfOf=null）不放行——必须走 act-as，否则履历缺
/// OnBehalfOfId 审计歧义（拒 E-WF-029）。admin 亦不豁免（引擎无权限概念；批量转单走
/// TransferAsync bypassOwnership 显式可信旁路）。
/// </summary>
public partial class FlowEngine
{
    /// <summary>系统身份（超时 worker 等引擎内部硬动作）。与 WfTimeoutService.SystemActor 同值。</summary>
    internal static readonly Guid SystemActor = Guid.Empty;

    private async Task AssertActorMayHandleAsync(Wf_FlowTask task, Guid actorId, Guid? onBehalfOf = null)
    {
        if (actorId == SystemActor) return;                               // ③ 系统硬动作
        var owner = onBehalfOf ?? actorId;
        if (owner != task.AssigneeId) throw new InvalidOperationException("E-WF-029");
        if (owner == actorId) return;                                     // ① 本人
        var granted = await _db.Wf_FlowDelegates.AnyAsync(d => d.Enable   // ② act-as 复验
            && d.GrantorId == owner && d.DelegateId == actorId
            && d.ValidFrom <= DateTime.Now && d.ValidTo >= DateTime.Now);
        if (!granted) throw new InvalidOperationException("E-WF-001");
    }
}
