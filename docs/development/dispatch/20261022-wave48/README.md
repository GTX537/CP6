# W48-01 真实 START / 七天一期 D3

COORD本聊天单人；2026-10-22T09:15:11.859125+00:00；Main基线`e5702415665a4beaa41623d4a0bbd8ce41ca42a5`。W47 PR408/409、Handoff23已采用，Issue407关闭，准确分支删除、工作区干净detached、席位finish；实际62.52分钟，W37–W47不重复。

本窗实现OEE独立Owner、完整来源授权与可信后台入口。DEV-1 `codex/w48-01`；DEV-2 `codex/w48-handoff`。预计实际90–150分钟：Owner/来源与后台45–70min、HTTP/页面15–25min、定向验证/一次集中审查/合并及收尾30–55min。完成直接继续计划入口或下一实际前置，不等待日历。

## 已核前置与限定出口

- 最新AGENTS/POLICY与W47基线无变；CRM main仍`c420cf9e0711bc0c5529d2f709708a57533b341a`、Platform main仍`a89e90eb4f078f37a6f8610882079073185fbcf8`，远端已核。三席位原已释放，容量无告警；根用户资料保留，W47 Actions运行0。
- OeeService四个公开入口尚无独立Owner；Controller只限制recalculate，已登录无query仍可读取。重算信任调用人字符串，后台以OeeWorker字符串调用同一人类入口。当前OeeDaily没有完整输入来源/生成证据，不把它视为正式PERF效果。
- W47 Machine Owner及可信machine UUID部门绑定已采用；复用当前真实会话/Actor、权限根、Machine/Production/WorkOrder现权与实际完整来源。OEE聚合不能只过滤隐蔽部分后冒充整机总量：不完整授权的设备/时间窗口不可披露或改写。当前source丢失、撤权、跨租户、失效父图和分页/统计均定向验证。
- 人类入口与内部可信后台分离：不以userName、无HttpContext或GUID参数单独充当授权。后台仅由宿主固定逐租户循环进入，绑定同Context租户并在实际数据库事务内核对当前租户启用/期限、来源及最终提交。不能把宿主模拟调用当已部署证明。
- 保持原计算为LEGACY_CALC_V1，明确展示其参考性质；不把480分钟/缺产能100%回退改名为OWNER_EFFECTIVE_V1。保存的结果需要与真实输入及输出绑定的证据，读取核对当前完整来源及证据，旧无证据或源已变的日汇总不冒充本次授权结果。存储表示/版本在DELIVERY解释，不回填旧真实数据。
- 序号/证据/日汇总等本窗实际效果和最终检查同一PG事务，缺Owner、失败/UNKNOWN与取消保持真实语义；不因为后台没有浏览器就绕过租户核对。不新增SQLServer实现。
- OEE页面隔离身份/迟到响应、冻结并防重复重算、UNKNOWN不自动重发，轮询在卸载/撤权后停止。W47设备列表的OEE附加值仍保持暂不披露，除非本窗证实完整独立消费；不以设备Owner代替OEE权限。

正式MES-PERF-01 source cut/RunSlot/TimedQuantityPart/closure、MES-RES-01双头/Claim和SCHED采用仍未完成。正文先前已语义读取，当前源码与实际历史证据已交叉核对；本窗只交付现有参考计算入口的权限/事务/来源边界，不声称48项PERF或205项RES真实验收。

本窗独占Main OperationsOwner/OEE/Machine来源/PG迁移/Program/后台与对应页面，Handoff仅通信。相关本地PG/HTTP/页面测试与一次作者SPEC/QUALITY；无代理、无新树、无Actions、无既有环境迁移/角色授予/生产。完整卡/125目标/247运行不提升；完成正常PR、远端采用、准确分支删除和manage finish，七天目标保持ACTIVE。

## candidate

OEE独立Owner、完整来源授权、同事务证据与可信后台已验证；31项相关.NET用例通过（按TRX testId保留最新定向结果，含PG/真实HTTP/后台事务/种子/权限），6项Vitest与完整vue-tsc -b通过。正常PR/Handoff、Issue410关闭和准确分支/席位finish待执行。

[DELIVERY](../../evidence/WP02-03/oee-owner/DELIVERY.md)。
