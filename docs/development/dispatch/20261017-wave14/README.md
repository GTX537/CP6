# 第十四轮：登录 P0 修复与候选交叉审查

> 本轮收尾：#38已通过用户确认的既有PostgreSQL DEV并关闭；A/D交付完成。新发现仪表盘隔离P0 #257，当前派工转至[第十五轮](../20261017-wave15/README.md)。下方为本轮历史状态。

## 2026-10-17 当前进展：登录修复已集成，等待真实DEV条件

A固定候选 `09017d7ad14c7647601edac258422365ff81daf9` 已通过 [D集中SPEC/QUALITY审查](https://github.com/GTX537/CP6.AI-Handoff/blob/84b6d2de0f7b30d493cf068db94fafc20fb7594b/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-38/REVIEW.md)，本地Ready to merge = Yes、无阻断；审查交付Handoff `ddc65ece4b922e8c49e9ddc4878a54b7af27f4de` 已回读。[代码PR](https://github.com/GTX537/CP6/pull/255)已正常合并，远端main `17ce6f742bf9aa885f7a293e13c30fcba124ca59` 已核对包含候选且七个范围文件与已验候选一致。D正式审查已完成，不再等待A交付或第二轮完整审查。

本地证据复用：相关认证50项（含HTTP13）通过，补pending断言后的13项为受影响重跑、未变37项复用；完整应用PostgreSQL18.6浏览器链通过。COORD仅完成收件、主线差异/证据适用性和合并后的包含性/文件一致性核对，没有重跑产品测试。HTTP故障注入为InMemory；浏览器verify、enroll由HTTP覆盖；PG状态快照早于最终browser-7，空database-init.log不作为逐项迁移证据。原失败与未验证范围保留。

**#38仍Open，真实DEV NOT_RUN。** 下一步由COORD确认实际DEV目标/入口、修复后的API/Web完整运行身份、测试租户/普通与管理员账号及允许创建/改密范围，再执行原步骤浏览器验收。现有只读观测仍是2026-10-17T10:31:30Z文档端点19991/18080身份读取失败，未取得新的运行事实。不能以demo或LOCAL_ISOLATED替代DEV；未经具体授权不部署、重启、切Tunnel、迁移或覆盖既有库。

四路交付已收齐：A完成本地代码交付，D完成唯一一次集中审查；B对C为No，C对B为No。原B准备[#253](https://github.com/GTX537/CP6/issues/253)原文时间精度/CAS修正，原C准备[#251](https://github.com/GTX537/CP6/issues/251)构建输入绑定及[#252](https://github.com/GTX537/CP6/issues/252)自有进程清理修正。三个P2产品窗口继续NOT_ADMITTED_P0_HOLD；B/C/D原候选普通实施和集成暂停，旧菜单#243不变。后续明确窗口后由原实施者修正、原交叉审查者只复查影响。

主线台账/四状态在同一BUG38交付窗口同步，不提升375张完整实现卡或125个业务目标：仍为24/375实现DONE、0/125业务完成。无新Actions运行、工作流变化、正式发包或环境操作。下方为原派工历史，当前状态以本段及roadmap.json为准。

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
