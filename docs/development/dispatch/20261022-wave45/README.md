# W45-01 真实 START / 七天一期 D3 安全前置

COORD本聊天单人；2026-10-22T07:12:49.679447+00:00；Main基线`c0a720c5905c7dcb58be9e5026445ee5e78162d3`。W44 PR399/400、Handoff20已采用、准确分支删除/固定席位finish，实际20.83分钟；W37–W44不重复。

P0 #401 MES实时通道租户/当前会话边界修复；[Issue401](https://github.com/GTX537/CP6/issues/401)为唯一活动BUG记录。D3设备/OEE前置源码核对发现实际注册SignalRMesNotifier向Clients.All发送工单/数量/设备载荷，MesHub按无租户业务编码加入组且无当前权限准入；唯一页面消费者ControlTower直接展示这些载荷。按BUG规则P0先行，原Machine/OEE完整Owner留下一窗口。

主任务`BUG-401-MES-REALTIME`，DEV-1 `codex/bug-401-mes-realtime`；Handoff DEV-2 `codex/w45-handoff`。预计本聊天实际45–90分钟，含真实失败复现、相关PG/HTTP/SignalR/页面验证、一次集中审查、正常合并和准确分支/席位收尾。复用现有MesDashboard当前Owner/权限根和可信tenant，不从客户端编码/请求参数猜测订阅身份；实时仅提供无业务载荷失效提示，页面经当前Owner重取并处理撤权/身份变化/迟到响应。当前Machine/OEE未拥有独立范围的读源不能作为新安全披露证明，核实后隔离其调用范围，不伪称整设备业务完成。

采用systematic-debugging/test-driven-development先保留失败证据再修复；项目精简规则覆盖通用逐小步全审查模板。无代理/新树/Actions/既有环境迁移/生产部署。PostgreSQL为唯一新增目标；非可信/缺当前Owner路径拒绝，不建立SQLServer实现。

Main窗口`docs/development/dispatch/20261022-wave45/README.md`，七天计划`docs/development/planning/20261022-seven-day-delivery.md`；roadmap唯一任务状态。WP02-03局部修复不提升整卡/125目标/247运行；其他模块/可靠投递前置不因本窗变为完成。


## 候选验证

31项去重相关.NET通过（4项新增PG/真实HTTP与WebSocket，其余既有库存看板/CSRF边界），8项Vitest及完整vue-tsc -b通过。一次作者集中审查完成；[DELIVERY](../../evidence/WP02-03/mes-realtime/DELIVERY.md)。正常采用/Issue关闭和准确分支/席位finish待执行。
