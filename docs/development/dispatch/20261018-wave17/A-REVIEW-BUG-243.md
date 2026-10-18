# A：菜单恢复候选唯一集中SPEC/QUALITY

原D固定候选 `59dd7a9c7a0f5511d857743146e18a28fead31bd` 已完成23组件测试、type-check和真实页面夹具；尚无独立任务级审查。你现在可只读准备Issue243、原DELIVERY、完整5文件diff及当前main差异；D在本轮补锁定4.1.11证据并交固定候选后，于同一报告给最终Ready to merge Yes/No。

只维护 `handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-243/REVIEW.md` 和本任务消息；新资料分支 `codex/dev-handoff-wave17-a-review243-20261018`，worktree `D:/CP6.AI-Handoff-worktrees/wave17-a-review243-20261018`。无产品写入、数据库/端口或独立重跑授权；优先核对并复用D真实证据。真实开始发一次START，准备不等于最终审查。

核对四种菜单写成功后列表/导航独立读取；只重试GET不重放已确认写入；写失败/超时不改称成功；草稿/取消/写入期间新草稿、过时读取与卸载/身份变化安全；畸形画像不清空导航、合法空菜单仍可发布。结合原页面脚本核对Layout实际事件消费和两条原故障；不能只读测试数字或逐字断言。锁定runner证据与源码/依赖绑定核实即可，不要求D无故重复页面或全量构建。

权限/CAS/菜单写幂等性、服务端全租户缓存和真实业务不是此候选范围，不额外加入数据库/DEV门禁。发现实质阻断给文件/触发与最低回归；原D经总控登记修正，你只复查修改影响。无阻断给本地可合并并明确fixture范围，COORD正常集成与远端核对后处理Issue243。不要审查自己旧BUG132，已关闭事项不重复。

## 共同执行规则

读取根AGENTS、本轮README/dispatch、最新roadmap和协调Handoff `handoffs/CP6-DEV-20261012/waves/20261018-wave17.md`。总控在准入合入后准备新产品worktree并在Handoff固定实际main/承接提交；开始前核对这些引用和工作树，没有最新main准备记录时先只读。旧候选/旧工作树保留，不在那里并写。Handoff发布不自动唤醒聊天。

继续同一任务的B/C/D不再做第二START或等待运输ACK；实际开始时一次PROGRESS记录本修正/补证开始即可。A的新审查任务实际开始发布一次START，固定交付到达后在同一报告接续，不重复START。所有人均不把派工/承接提交称作实际实现、修复通过或准入依赖已完成。

只验证受影响行为和故障边界，复用原有效证据及单份DELIVERY/REVIEW。失败原件保留，不因SHA/文档变化重跑。代码、依赖/生成资产或资源确需超出写集时，先交总控具体原因与路径。执行者不改共享台账/四状态、main、其他人报告或Issue状态；总控统一共享状态、逐任务正常PR及远端核对后关闭Issue。

push/PR前核对候选和受保护分支workflow、依赖及路径过滤，未变内容复用结论；不带tag、不启动Actions或修改分支保护。Handoff先核私有、Actions disabled、默认/资料分支无workflow，正常push并远端回读。无既有数据库/服务操作、迁移、正式发包或部署授权。后续数据库唯一PostgreSQL，不加SQL Server门禁。
