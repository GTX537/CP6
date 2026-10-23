# W65 / 七天一期公共基础：PostgreSQL Pub采番当前规则串行推进修复

本聊天COORD单人，真实开工 2026-10-23T11:58:26.172254+00:00。Main `4b129632448409a6bcd713b295cf87b7a5b972e3`；Handoff `1e877baa5c3ff0523db882c50b89fbd87528e1d7`。W64 Main434/435与Handoff40已采用，Issue433关闭，准确分支已删除、固定树干净detached、全部席位释放；实际34.35min，前置约6min。七天持续目标ACTIVE，不重复W37–W64。

## 前置核对

AGENTS/POLICY哈希未变，容量无告警，无开放重复任务PR/Issue（本轮登记Issue436）。已读SYS-NUM-01完整归并和bf7ac9cc原文；核对最新台账WP02-16、W9 CodeGen真实交付、当前SeqService/Controller/实体/ISeqService、最终EF租户唯一索引改写、直接采购调用者和Common DocNumber。旧CodeGen窗口已合并，无并行编号写入任务。本窗直接修现存采番缺陷，不借WP02-10局部采用宣布完整前置已满足。

## 实施范围

[Issue436](https://github.com/GTX537/CP6/issues/436) P1：两个上下文预载相同规则后，SeqService优先Local旧值再SaveChanges，可两次返回同号；规则TenantId+BizKey唯一不等于分配结果唯一。

- 只修改现有Pub SeqService PostgreSQL路径；从真实当前租户规则锁定并重新读取，数据库行锁串行决定period和next，定向更新本规则。复用原租户/BizKey唯一域，不统一其他allocator。
- 保留原DateTime.Now/显式时间、日期格式/文化和周期语义；SeqLength仍是最小补零宽度，不任意缩小合法计数器容量。格式失败在更新前返回，int耗尽拒绝回绕。
- 自有事务成功提交后才返回；已存在调用方事务时保持原归属，不提交调用方事务。该返回值提交前是暂定结果，回滚仍按原事务回滚；本窗不宣称解决外露号不可回收或业务原命令未知提交。
- 不使用SaveChanges替调用方提交无关待写业务；本规则脏跟踪状态显式拒绝，干净旧跟踪行不再作为分配依据。失败定向回滚自有事务或本次保存点，保留调用方先前事务工作。
- 不修改SQL Server历史路径、Common/DocNumber、WMS/MES/FIN采番、SeqController权限/规则编辑/预览、表结构/迁移和正式部署。没有新增原命令receipt API或强行给现有调用者编造operation身份。

## 任务、实际耗时估计与验收

预计本对话实际50–80min：原生PG红灯与边界核对10–15，串行分配/定向测试20–35，集中SPEC/QUALITY及两仓正常合并收尾20–30。按最终finish记实际时长，子任务不重复累计。

固定DEV-1 `codex/bug-436-pub-seq-atomic`；DEV-2 `codex/w65-handoff`，不增树。先以同一真实PG库两个Context预载复现重复，再验证并发/周期、同BizKey不同租户及外租户跟踪、原格式和最小宽度、耗尽/坏格式、脏目标/无关待写、调用方commit/rollback和失败后继续；使用现有新隔离PG夹具55435。相关现有Seq单测复用或一次运行，一次作者完整diff SPEC/QUALITY，不全仓回归，不跑Actions。

Main一份交付报告、总台账和四状态同任务分支；Handoff固定START/DELIVERY。正常PR合并，精确核远端采用，再停止自有PG保留证据/数据、准确分支清理和finish。完整WP02-16仍PLANNED/NOT_CHECKED；原命令分配receipt、业务Owner原键/UNKNOWN、规则CAS/历史冲突/停用、跨Owner切换仍未完成。完整24/375、125目标0交付和247运行NOT_RUN不提升。W51准确data删除曾被自动审批拒绝，原因blocked by policy，停止保留且不重试。

## W65验证完成

8项原生PostgreSQL用例通过，4项未受改动的原Seq用例复用先前通过结果；12项独立用例无跳过，相关Main构建通过。三个隔离PG停止保留；真实重复号红灯与首次跟踪状态失败保留。进入两仓采用及同任务最后收尾；完整Owner门不提升，七天目标ACTIVE。
