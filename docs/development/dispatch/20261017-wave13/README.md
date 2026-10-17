# 第十三轮：四个新对话并行实施

用户要求四个新执行对话，由当前COORD总控，用户手动粘贴启动指令。已核对Main `86b07dec1050f948cfb07d8916d618166bdefed4`、Handoff协调 `5226d320cd8dc36984c89eaf87722d2983b00ba2` 与A/B/C/D资料分支，无新未处理交付。第十二轮PR241/242已完成，不重复签发/撤销及有效验证。

| 新对话 | 任务 | 本轮独立结果 |
|---|---|---|
| A | [WP02-03](WP02-03.md) | 现有授权读取的主体绑定、有效角色与缓存限制 |
| B | [WP02-02](WP02-02.md) | 已登记服务租户的资格目录控制命令 |
| C | [WP02-28](WP02-28.md) | 实际publish制品绑定的本地启动与身份readiness验证器 |
| D | [WP02-18](WP02-18.md) | 菜单保存后的列表与当前浏览器导航刷新恢复 |

[资源登记](dispatch.json)限定每个任务的路径、分支、独立worktree和端口。四份窗口CONFIRMED，尚未收到新对话真实START；COORD的只读选题代理不是实现Owner。新对话在准入main之上工作，先写一次真实START后连续实施，不重复等待ACK。完整任务状态仍24 DONE/2 IN_PROGRESS/349 PLANNED，业务0/125、247 NOT_RUN、41历史引用不变。

A不碰B存储/模型或认证发行入口；B只做已登记服务租户资格行的control DML，不碰权限；C仅publish启动验证脚本/定向测试，不动Program或业务路径；D仅菜单页面刷新恢复，不造后端CAS。四者不消费本轮其他候选，公共台账/四状态/生成资产和逐任务合并由COORD独占。A/D完整前置尚未全部完成，批准的只是明确不依赖未交付部分的限定改进；不能因此把完整WP02-03/WP02-18改为READY或DONE。

D已有[活动BUG #243](https://github.com/GTX537/CP6/issues/243)，原步骤待执行者复现；其他实测缺陷按BUG规则登记，P0优先处理并通知COORD调整窗口。不要重复旧已交付工作或提前执行全Main/跨仓/生产验收。

## 通信与交付

直接使用[Handoff本轮开发入口](https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md)。消息写在各自 `handoffs/CP6-DEV-20261012/tasks/<任务ID>/messages/<UTC>-wave13-<类型>/MESSAGE.md`，任务专属资料分支见登记；保持私有、Actions disabled、无workflow，不更改根CURRENT/历史设计指针。START/QUESTION/DELIVERY都给固定提交SHA与准确路径，远端回读后才称送达。COORD直接读取并答复，用户无需逐条转述。Handoff不自动唤醒对话。

每任务只交一份报告，列实际改动、验证/复用、失败与剩余范围；执行者先完成代码和相关验证并安全提交，不改共享状态、不自行合main。COORD在对应交付窗口统一同步状态，完成一次任务级集中审查，正常串行合并并核对远端。原有效结果复用，不因SHA或文档重跑。

PostgreSQL与精简验证规则继续生效，无Actions、正式发布、既有库迁移或部署授权。端口A PG55435、B PG55436、C PG55438，D页面5178；HTTP独立动态loopback端口。保留旧分支/worktree和根七项未跟踪材料。执行环境与禁止写入范围详见各任务计划。
