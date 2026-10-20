# 第三十三轮：部门可信映射与单价订正 Owner 读写链路

COORD本聊天独立，基线`22866fd2e40eb70c55f28967f7380fe78e61291c`，开工2026-10-20T13:31:03.692656+00:00。未来24小时检查窗；本代理实际估计2小时40分–4小时20分，故障余量6小时，不等待满24小时。

|工作|代理实际时间估计|
|---|---|
|租户内受注部门代码→IAM部门显式绑定、版本、审计与撤销入口|45–75分钟|
|本部门/下级/自定义范围接入PA090写Owner，含并发撤权|35–55分钟|
|PA090列表及金额计算读Owner、隐藏字段/筛选与前端旧数据保护|45–75分钟|
|定向PG/HTTP/前端验证、一次作者审查、正常PR及分支席位收尾|35–55分钟|

W33-01独占原DEV-1/codex/w33-01；W33-HANDOFF独占原DEV-2/codex/w33-handoff。管理入口status/start均已执行，四仓合计2开发+1集成；不新增树、完整副本或代理。

- [x] 明确Main ERP业务OrderDepartment代码到IAM DeptId的显式租户绑定：不按同名、GUID或默认部门猜测；不自动回填/批准真实存量。原生PG代码比较语义、唯一性、健康父链、租户与删除/禁用边界保留。
- [x] 管理入口复用IAM当前认证/动作、operation/CAS、原因及审计；ordinary SQL绑定writer与原authority根串行，映射撤销/修改立即影响后续消费。
- [x] 写Owner的scope2/3/4使用可信绑定及当前健康树；选中/传播所有实际效果行均授权，unknown不放大权限。W32原子性、最终会话/撤权检查、精度和UNKNOWN边界保留。
- [x] PA090列表和金额计算实际入口使用独立query动作、对象/字段Read映射，受限字段不输出或借筛选/计数/计算泄露；页面遵守只读/隐藏并清除失权旧数据。
- [x] 原生PG定向并发/ABA/异常/跨租户回归及实际HTTP、相关前端验证；一份DELIVERY和一次作者SPEC/QUALITY。必要迁移仅在自有PG55435串行隔离实例执行。
- [x] 产品PR与Handoff远端采用、同步收尾记录；自有进程停止、生成data回收。原任务分支承载最终文档。
- [ ] 本文档PR合并后manage finish释放原W33-01；这是提交时点，最终生命周期结果见管理登记及PR收尾评论。

不提升整个Order/CRM Owner或全部IAM。其余Order读写/导出、跨重载业务operation恢复、正式POWER EGG投递及真实业务验收仍独立；125目标/247真实运行保持。无Actions、既有数据库迁移、真实tenant映射批准或生产部署。

## 候选状态

101项后端/原生PG/实际HTTP、27项前端及类型检查通过；一次作者SPEC/QUALITY无本窗阻断。[单份DELIVERY](../../evidence/WP02-03/order-department-owner/DELIVERY.md)。#368原步骤完成回归，待正常PR及远端采用后关闭。自有PG/API停止，6处生成data回收约0.744GiB、日志保留。继续原任务分支完成产品/Handoff与最后文档，随后finish，零额外工作树。

## 远端采用及原任务分支收尾

产品[PR #369](https://github.com/GTX537/CP6/pull/369)已合并，source`0be5e9bfff0d2711d5dfcd8769e3c4fb9bd4d978`、远端main`d58b65cb16608c310ec3fb64a3185d13680760a5`，全部改动blob一致；#368原步骤回归与远端采用后已关闭。[Handoff PR #9](https://github.com/GTX537/CP6.AI-Handoff/pull/9)已合入默认目标，source`917f33c9696ca504f808aa6d05ff414dc5738677`、目标`33042e048195b7dc5e535cfee145ad43f734a9f6`；Handoff任务分支已定向删除，DEV-2干净detached并释放。

101个不同相关PG/HTTP/IAM、27前端及类型检查通过，0最终失败/跳过；一次作者SPEC/QUALITY无本窗阻断。此最后文档没有改变产品/依赖/环境，复用成功证据，仅检查台账结构、差异和链接。自有PG/API停止，6处生成data逻辑回收798421171字节（0.744GiB），清理时段D盘实际空闲增加801910784字节（0.747GiB）；日志/失败保留。

本提交仍在原codex/w33-01、DEV-1完成最后正常PR；之后调用manage finish。它不能提前证明自己的未来合并/释放。最终分支处置、工作区状态及席位结果以管理登记`D:/CP6/.git/cp6-workspaces/state.json`、最后PR收尾评论及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave33-final-verification.json`为准，不另开收尾任务或分支。INTEGRATION-1空闲，零新树/clone/完整副本/代理。

剩余Other Order CRUD/导出/下载Owner、跨重载业务operation恢复、正式POWER EGG报文/投递、CRM持久Owner和真实业务验收。完整WP02-03及125目标/247真实运行不提升。无Actions、既有环境迁移、真实tenant批准或生产部署。原估计本代理160–260分钟、故障余量6小时；实际耗时在真实finish后报告，不等待满24小时。
