# 第十五轮：仪表盘租户隔离P0

#38已通过用户指定PostgreSQL副本的真实DEV验收并关闭，详见[单份DELIVERY](../../../quality/evidence/issue-38/DELIVERY.md)。API/Web由main `84896578b722d7deac50809ff42f5f30f7fd50d1` 实际构建、身份匹配，当前已停止，原账号/结构不变。验收另发现[P0 #257](https://github.com/GTX537/CP6/issues/257)：新租户订单0行，仪表盘仍显示其他租户订单；Dapper查询无租户条件且全局缓存。业务截图只本机保留。

| 原对话 | 本轮职责 | 当前可做 |
|---|---|---|
| A | [BUG257唯一实现](A-BUG-257.md) | 从本准入合并后的最新main修正可信租户过滤/缓存，定向PG回归 |
| D | [准备与独立审查](D-REVIEW-BUG-257.md) | 只读检查边界；A固定交付后一次集中SPEC/QUALITY |
| B | 保留原租户CLI候选及#253修正准备 | 只读窄方案，产品实施/集成仍暂停 |
| C | 保留原publish候选及#251/#252修正准备 | 只读窄方案，两个修正保持串行，产品实施/集成仍暂停 |

这次只登记A/D新窗口，尚无真实START或候选交付；派工/运输不是开工或业务完成。A独占PG55449/API18257/Web15257，启动前核对空闲及所有权；CacheService仅允许dashboard键相关改动。D不使用A进程。既有PG5432副本、19991/18080及真实DEV数据操作保留COORD在代码合并后验收，执行者不得重启或改库。原SQL Server、demo/Tunnel不动。

A验证→固定DELIVERY→D单次集中审查→COORD正常集成→同一用户确认PG DEV做空租户/交替缓存冒烟→满足#257条件后关闭。B/C/D原wave13普通实施/集成继续因#257暂停；不再以已关闭#38作为当前原因。原候选和有效证据保留。24/375完整实现DONE、0/125业务完成不变，不将登录验收提升为业务隔离或发布完成。

## 执行与交付

先读根AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账roadmap.json。PostgreSQL唯一新增目标；只做受影响行为验证和一次集中SPEC/QUALITY，修正仅复查影响，不重复有效证据。资料仓GTX537/CP6.AI-Handoff、协调分支codex/dev-coordination-20261012，本轮固定消息在handoffs/CP6-DEV-20261012/waves/20261017-wave15.md。

COORD在本准入合并后从最新main创建A产品分支/工作树及A/D资料分支。执行者先核对当前main、分支与资源，再发布一次真实START到本任务messages/<UTC>-start/MESSAGE.md；安全推送、远端回读后连续推进，不等ACK。D前置由A固定DELIVERY满足后直接接续，无第二START。一个实现Owner，不另派代理重复本窗口。

只写自己产品白名单或资料任务根，单份DELIVERY/REVIEW列明固定SHA、实际验证/复用、失败和剩余范围。COORD负责共享台账/四状态、正常合并、远端核对和Issue关闭。执行者不改main、共享状态、他人候选或资源。push/PR前检查候选/main工作流事件及路径，内容未变复用分析；Handoff私有、Actions disabled且默认/自身无workflow后推送，无tag。不得运行Actions、部署/覆盖既有DEV、迁移原库、force-push或管理员绕过。
