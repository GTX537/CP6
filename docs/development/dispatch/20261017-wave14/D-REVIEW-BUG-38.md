# D：#38 浏览器验收准备与最终集中审查

> 现在可接续正式审查：A固定候选 `09017d7ad14c7647601edac258422365ff81daf9` 已由Handoff63c41c66614a62e7213e2b4befd9c8408c8bc3e2交付；报告为该候选的docs/quality/evidence/issue-38/DELIVERY.md，证据根D:/CP6-local-test/BUG-38/wave14-a/。旧“只有START”快照失效，无需第二START或ACK。仍是原一次集中审查，尚未收到D最终结论。

> 窗口 REVIEW-BUG-38；现在只读准备，A交付固定候选后做该任务唯一一次集中SPEC/QUALITY审查。A是唯一代码/测试实施者，D不写其工作树，也不继续改原菜单候选。

先读 [A实现窗口](A-BUG-38.md)、[Issue #38](https://github.com/GTX537/CP6/issues/38)、当前Auth/强制改密/CSRF/2FA/公开语言路由、权限最终DELIVERY。原菜单候选59dd7a9c仍保留，#243仍Open，本窗口不审它。

## 现在可以完成

- [ ] 核对普通/管理员首次改密→重登、旧Cookie重注入→切账号/tenant、2FA verify/enroll、语言加载的真实页面/API路径，找出可复用测试与必须补的行为缺口。把具体建议写入同一REVIEW.md的准备部分并通过自己的Handoff发A/COORD；不造另一份完整执行报告。
- [ ] 检查真实Cookie jar是否保留响应未触及的旧Cookie，以及删除/重写pending/CSRF的最终状态；指出必须使用真实权限上下文/tenant和CSRF开启的路径。避免把fake权限、fixture或健康页当完整业务证据。
- [ ] 只读核对DevOps/R2文档中DEV目标、运行身份和已有可用测试范围，明确demo与DEV区别。没有已确认修复提交、合法测试账号/租户和授权范围时，仅列缺口，不登录或改密码，不切Tunnel/部署/迁移/触发Actions，不打印Secret。历史成功运行不算当前身份已核对。

## A交付后的单次集中审查

依赖是A在 `codex/dev-handoff-bug38-implementation-20261017` 发布真实DELIVERY固定候选和证据；收到前不能出Ready to merge。D可读取该资料分支和固定源码，按本已登记条件直接接续，不再等待同范围运输ACK。Handoff不会自动唤醒，对话退出后需再次唤醒才能接续。

- [ ] 核对完整任务diff、RED/GREEN与本地浏览器原步骤；重点为保存成功后清Cookie、失败保留、精确POST登录/公开语言GET例外、强制改密不可绕过、原tenant身份、2FA旧会话切换和CSRF边界。
- [ ] 直接复用有效证据，只有当前修改存在明确缺口才做局部额外检查。不要默认重复A全部测试/浏览器/PG。需实际probe时先向COORD申请独立资源，不能接管或停止A的运行实例。
- [ ] 给出本地代码候选Ready to merge Yes/No以及独立的真实DEV验收状态；DEV NOT_RUN本身不冒充通过，也不必禁止有完整本地证据的代码交付。Issue关闭仍须DEV条件满足。
- [ ] 阻断带具体文件/行、实际触发和最小修正；由A在原范围修正，D只复查影响，不启动第二次全任务审查。

本轮仅维护自己Handoff分支的一份REVIEW.md；最终记录候选SHA、证据来源/复用、未验证范围及结论。COORD负责正常合并/状态与后续环境授权，D不自行合并、关闭#38或改共享文档。

## 执行与通信

读取根 AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账 `docs/development/roadmap.json`。本轮使用原四个用户对话，不另起代理实施同一任务。原 wave13 分支、工作树和报告保留；不能把本轮审查视为原候选已准入合并。

自己的新 Handoff 分支和工作树由 COORD 在本轮准入合并后建立，名称见 dispatch。先读取协调分支 `codex/dev-coordination-20261012` 中 `handoffs/CP6-DEV-20261012/waves/20261017-wave14.md` 的固定提交，核对实际 main。自己的回复只写 `handoffs/CP6-DEV-20261012/tasks/<本窗口ID>/`，不改 README/CURRENT/协调消息或他人文件。START 写入 `messages/<UTC>-start/MESSAGE.md`，列明原聊天身份、窗口、固定输入、允许范围和实际开始时间；安全 push、远端回读后即可连续工作，不等运输 ACK。A 的候选交付发 `messages/<UTC>-delivery/MESSAGE.md`，审查者只维护本窗口一份 REVIEW.md，最终消息引用其固定提交；不要复制原测试日志和哈希清单。

Git 一律带 `-c core.longpaths=true`。push/PR 前核对候选和受保护分支 workflow/依赖/事件及路径；相同内容复用已有分析，无新授权不得触发/重跑 Actions。Handoff 私有、Actions disabled、默认及自己的分支无 workflow 后才推送分支，不推 tag。普通文档不做产品测试。不要改全局配置、force-push、删除远端分支或使用管理员绕过。

PostgreSQL 是唯一新增数据库目标。项目精简规则优先：相关行为验证与一次合并的 SPEC/QUALITY 审查；修正只复查受影响部分，代码/依赖/环境未变就引用有效证据。没有因为固定 SHA、提交或文档变化重跑全仓的要求。数据库、并发、身份或安全风险保留必要专项检查。

执行者不写共享台账/生成页/四状态、不合 main、不关闭 Issue。COORD 统一协调范围、共享状态、正常 PR 合并、远端核对及满足条件后的 Issue 关闭。读到新的实质阻断立即通过 Handoff 报 COORD；禁止自行扩大公共写集或另开实施者。没有生产/DEV部署、既有数据库迁移/覆盖、正式发包或新 Actions 授权。Handoff 发布不自动唤醒聊天，也不等于真实 START 或验收。
