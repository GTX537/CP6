# 第十四轮：登录 P0 修复与候选交叉审查

用户于 2026-10-17 要求继续推进并将任务分配给原 A/B/C/D 对话。本轮基于已核对远端 main `4d5c115fde40d507e43f3b56e0e02425241dcecd`：权限 P0 #245 已通过 PR248 修正合并并关闭，PR249 已同步状态。新增准入合并后，A 从当时最新已确认 main 开始，不能回到原 aa0a361f 候选继续开发。

| 原对话 | 本轮窗口 | 立即可做 | 依赖与交付 |
|---|---|---|---|
| A | [BUG-38 实现](A-BUG-38.md) | 唯一产品写入者：旧 Cookie、精确重登、2FA 转换及定向回归 | 单份 DELIVERY、固定候选、LOCAL_ISOLATED 原步骤证据；真实 DEV 尚未完成时 Issue 保持 Open |
| B | [审查 C 候选](B-REVIEW-C.md) | 固定 b9f3f30b 的一次只读 SPEC/QUALITY 审查 | 一份审查结论与具体阻断；无重建 publish 或部署 |
| C | [审查 B 候选](C-REVIEW-B.md) | 固定 e2bfcc0b 的一次只读 SPEC/QUALITY 审查 | 一份审查结论，重点 PG 锁/原子性/UNKNOWN；默认复用原 PG7 证据 |
| D | [#38 验收与审查](D-REVIEW-BUG-38.md) | 先核对浏览器验收路径、DEV 条件和安全边界 | A 固定交付后进行本任务唯一一次集中审查；不与 A 并写测试/控制器 |

这些窗口是调度/审查记录，不增加 375 张实现卡或业务目标。唯一状态为 roadmap.json；[dispatch.json](dispatch.json)为不可变的准入范围快照。登记不代表真实 START。B/C 是交叉审查者，原候选实现Owner不变；D不再改旧菜单候选。原 B/C/D 普通实施及集成继续 P0 暂停，只读准备可进行，审查通过也不自动恢复合并。

A 的产品写集和 PG55448/API18238/Web15238 由 COORD 独占登记，启动前仍需核对占用/归属。B/C/D 不使用或停止 A 的进程、不写 A 的工作树。需要额外本地探针时先说明具体未覆盖风险，由 COORD 分配独立资源；不存在把一次正常审查扩成全部宿主/数据库验收的默认要求。

顺序：A实施与B/C审查、D准备并行 → A固定候选 → D集中审查与必要修正复查 → COORD本地交付集成 → 真实DEV按已确认目标与授权复测 → 满足#38条件后关闭；其间未获得部署授权不替换DEV。后续普通集成是否恢复由COORD另行明确。B/C审查所需证据已经存在，优先先交结论；无未经评估的天数承诺。

执行者只提交所属候选或Handoff报告，COORD统一共享状态与逐任务合并。本轮不启动测试或产品实施，不重复A已完成权限修复，不将原B/C/D候选或Handoff ACK冒称业务验收。

## 执行与通信

读取根 AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账 `docs/development/roadmap.json`。本轮使用原四个用户对话，不另起代理实施同一任务。原 wave13 分支、工作树和报告保留；不能把本轮审查视为原候选已准入合并。

自己的新 Handoff 分支和工作树由 COORD 在本轮准入合并后建立，名称见 dispatch。先读取协调分支 `codex/dev-coordination-20261012` 中 `handoffs/CP6-DEV-20261012/waves/20261017-wave14.md` 的固定提交，核对实际 main。自己的回复只写 `handoffs/CP6-DEV-20261012/tasks/<本窗口ID>/`，不改 README/CURRENT/协调消息或他人文件。START 写入 `messages/<UTC>-start/MESSAGE.md`，列明原聊天身份、窗口、固定输入、允许范围和实际开始时间；安全 push、远端回读后即可连续工作，不等运输 ACK。A 的候选交付发 `messages/<UTC>-delivery/MESSAGE.md`，审查者只维护本窗口一份 REVIEW.md，最终消息引用其固定提交；不要复制原测试日志和哈希清单。

Git 一律带 `-c core.longpaths=true`。push/PR 前核对候选和受保护分支 workflow/依赖/事件及路径；相同内容复用已有分析，无新授权不得触发/重跑 Actions。Handoff 私有、Actions disabled、默认及自己的分支无 workflow 后才推送分支，不推 tag。普通文档不做产品测试。不要改全局配置、force-push、删除远端分支或使用管理员绕过。

PostgreSQL 是唯一新增数据库目标。项目精简规则优先：相关行为验证与一次合并的 SPEC/QUALITY 审查；修正只复查受影响部分，代码/依赖/环境未变就引用有效证据。没有因为固定 SHA、提交或文档变化重跑全仓的要求。数据库、并发、身份或安全风险保留必要专项检查。

执行者不写共享台账/生成页/四状态、不合 main、不关闭 Issue。COORD 统一协调范围、共享状态、正常 PR 合并、远端核对及满足条件后的 Issue 关闭。读到新的实质阻断立即通过 Handoff 报 COORD；禁止自行扩大公共写集或另开实施者。没有生产/DEV部署、既有数据库迁移/覆盖、正式发包或新 Actions 授权。Handoff 发布不自动唤醒聊天，也不等于真实 START 或验收。
