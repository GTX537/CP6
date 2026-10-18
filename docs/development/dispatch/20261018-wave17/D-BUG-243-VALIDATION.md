# D：#243锁定依赖补证，保留原产品候选

原候选 `59dd7a9c7a0f5511d857743146e18a28fead31bd`，原单份DELIVERY为 `docs/development/evidence/WP02-18/menu-refresh-recovery/DELIVERY.md`。原23组件测试使用Vitest4.1.9，但该候选package/lock要求4.1.11；这是具体依赖适用性缺口，本轮只补该范围，不重做已完成页面与功能。

COORD从最新main创建 `codex/bug243-locked-validation-20261018` / `D:/CP6-worktrees/wave17-d-menu-validation-20261018` 并承接原59dd7a9c。仅允许更新原DELIVERY，产品/MenuView/helper/test/package/lock默认只读；若定向验证或A审查发现实质问题，将具体文件与修正范围交COORD后再改，不盲目重写已交付实现。

在隔离私有验证副本以当前锁文件依赖执行既有 `MenuView.spec.ts`，或复用已安装且核对package/lock与实际版本一致的隔离环境；禁止更新lock或使用另一工作树未核对的node_modules。绑定当前候选的MenuView/helper/spec及其相关导入、package/lock/config到验证输入。保留旧23项及4.1.9真实范围，补实际4.1.11结果及命令/原始日志，不把旧记录改为新执行。若只有runner变化且页面运行依赖/源码未变，复用原真实页面夹具、type-check和RED；无需全前端测试/build/type-check重复。

如果实际页面依赖变化影响原证据，说明具体原因后仅复测受影响页面，用自有5178及原API拦截夹具，不连接真实后端/数据库。默认无页面启动需求。最终单份DELIVERY列当前精确候选和复用范围，经原 `codex/dev-handoff-wp02-18` 的tasks/WP02-18发布并回读，交A/COORD。原资料工作树若落后远端，先核干净并ff-only，不能覆盖未提交内容。A一次集中审查，原D不自批；Issue保持Open直到总控集成及原条件满足。

## 共同执行规则

读取根AGENTS、本轮README/dispatch、最新roadmap和协调Handoff `handoffs/CP6-DEV-20261012/waves/20261018-wave17.md`。总控在准入合入后准备新产品worktree并在Handoff固定实际main/承接提交；开始前核对这些引用和工作树，没有最新main准备记录时先只读。旧候选/旧工作树保留，不在那里并写。Handoff发布不自动唤醒聊天。

继续同一任务的B/C/D不再做第二START或等待运输ACK；实际开始时一次PROGRESS记录本修正/补证开始即可。A的新审查任务实际开始发布一次START，固定交付到达后在同一报告接续，不重复START。所有人均不把派工/承接提交称作实际实现、修复通过或准入依赖已完成。

只验证受影响行为和故障边界，复用原有效证据及单份DELIVERY/REVIEW。失败原件保留，不因SHA/文档变化重跑。代码、依赖/生成资产或资源确需超出写集时，先交总控具体原因与路径。执行者不改共享台账/四状态、main、其他人报告或Issue状态；总控统一共享状态、逐任务正常PR及远端核对后关闭Issue。

push/PR前核对候选和受保护分支workflow、依赖及路径过滤，未变内容复用结论；不带tag、不启动Actions或修改分支保护。Handoff先核私有、Actions disabled、默认/资料分支无workflow，正常push并远端回读。无既有数据库/服务操作、迁移、正式发包或部署授权。后续数据库唯一PostgreSQL，不加SQL Server门禁。
