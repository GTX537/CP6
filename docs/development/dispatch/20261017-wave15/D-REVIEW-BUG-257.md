# D：BUG257准备与唯一集中审查

原D只读准备/审查，A是唯一产品写入者；不改旧菜单候选59dd7a9c，也不写A工作树。

先读Issue257、A包、当前可信租户/权限边界和DashboardController/CacheService。重点核对默认TenantContext不能代表可信租户、身份缺失失败关闭、每个子查询参数、空tenant数据、全局旧缓存不被读取、交替暖缓存与固定DTO/数量合同。资料根tasks/REVIEW-BUG-257/只维护一份REVIEW.md；准备不当作正式候选审查。

A在自己的资料分支交固定DELIVERY后，按本窗口直接接续唯一一次集中SPEC/QUALITY审查，不需第二START/ACK。检查完整diff和实际PG/HTTP证据，复用未变成功结果；只有具体未覆盖风险才提局部检查，不重复整套宿主/数据库矩阵。没有A固定交付前不出Ready to merge。

阻断带文件/行和实际触发；A修正，D只复查影响。最终分别给本地代码Ready to merge及真实DEV状态；COORD负责既有PG副本的合并后验收与Issue关闭。D没有新端口、数据库/部署或共享状态写权限；需要探针先说明具体缺口由COORD分配资源。

## 执行与交付

先读根AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账roadmap.json。PostgreSQL唯一新增目标；只做受影响行为验证和一次集中SPEC/QUALITY，修正仅复查影响，不重复有效证据。资料仓GTX537/CP6.AI-Handoff、协调分支codex/dev-coordination-20261012，本轮固定消息在handoffs/CP6-DEV-20261012/waves/20261017-wave15.md。

COORD在本准入合并后从最新main创建A产品分支/工作树及A/D资料分支。执行者先核对当前main、分支与资源，再发布一次真实START到本任务messages/<UTC>-start/MESSAGE.md；安全推送、远端回读后连续推进，不等ACK。D前置由A固定DELIVERY满足后直接接续，无第二START。一个实现Owner，不另派代理重复本窗口。

只写自己产品白名单或资料任务根，单份DELIVERY/REVIEW列明固定SHA、实际验证/复用、失败和剩余范围。COORD负责共享台账/四状态、正常合并、远端核对和Issue关闭。执行者不改main、共享状态、他人候选或资源。push/PR前检查候选/main工作流事件及路径，内容未变复用分析；Handoff私有、Actions disabled且默认/自身无workflow后推送，无tag。不得运行Actions、部署/覆盖既有DEV、迁移原库、force-push或管理员绕过。
