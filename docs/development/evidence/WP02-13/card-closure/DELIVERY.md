# WP02-13：拆卡，已验证审计采用与剩余完整出口

结论：**拆卡**。原 WP02-13 的已验证部分以原卡号 DONE，余项进入 **WP02-13b**，紧随原卡。原 375 卡、完整 scope/正反验收、SYS-AUD-01 的 21 条原 AC 全部保留；不将本次局部 DONE 解释为完整原卡出口或目标通过。

M 卡上限 4h、150% 强制拆卡点 6h。本次阶段 0 核对已定位真实未完成实现，主动按已验证/未验证出口拆分，未声称已达到 150%。固定 DEV-1、`codex/wp02-13`，基线 Main `66b92018889889ce76438c3280283bcad008e7ab`（[前卡 PR #1239](https://github.com/GTX537/CP6/pull/1239)），原前置 WP02-10 已 DONE。

## 开头固定的原出口

[原卡](../../../task-definitions.json)：操作/安全/字段审计的脱敏、actor/effectiveActor、UTC 来源和 Owner 事务参与；总数/明细/导出一致授权，清理按 policy/hold/unknown 引用及收据。正向为合法业务审计与效果同提交、授权计数与明细同范围；反向为敏感泄露、跨租户计数、hold 下清理、未知时间冒充 UTC 均阻止。

## 原号 DONE 的已验证部分

| 已验证责任及映射原 AC | 实际证据与边界 |
|---|---|
| 必需审计参与真实业务事务；AC-AU02-02、AU03-04 的已采用 Owner 部分 | [WP02-10 核销](../../WP02-10/card-closure/DELIVERY.md)串联 Main 原日志、库存双腿及 Outbox；[W264](../../W264-01/DELIVERY.md)/[W265](../../W265-01/DELIVERY.md)真实租户强审计；[W269](../../W269-01/DELIVERY.md)、[W290](../../W290-01/DELIVERY.md)、[W307](../../W307-01/DELIVERY.md)保留表单、任务和文档各自实际采用。未采用的业务写者仍由对应业务卡验证 |
| 字段详情安全投影、原文不回退、原件与视图分离；AC-AU03-01/02/03、AU04-02/03 的字段详情部分 | [W64](../../W64-01/DELIVERY.md)：真实 PG/Main HTTP 登录，当前日志权及 Machine Owner 对象/字段权；损坏和未知内容标 UNREADABLE，未知 Owner 503，原 Changes 不改，稳定游标、撤权和跨租户拒绝。范围是已登记映射，不能据此宣称三类列表/导出全覆盖 |
| 请求旁路日志与业务效果隔离；AC-AU01-02、AU02-02 的旧日志职责边界 | [W460 / BUG-921](../../BUG-921-OPERLOG-FALLBACK-ISOLATION/DELIVERY.md)：真实 PG/HTTP 拒绝业务后独立上下文仅保存日志；正常业务与日志失败分列，原失败保留。证明 best-effort 不意外提交已拒业务，未将请求日志改称强审计或业务原结果 |

以上是原卡已验证交付的明确集合，复用原实际测试和受测 SHA。PGT-017 27/27 证明已登记 writer 的授权共同门，不证明审计保留政策和所有查询出口，故不作为本卡整体验收替代。

## WP02-13b 的精确缺口（3 条）

1. **三类审计的安全采集及身份/时间来源**：按原白名单消除秘密，actual/effective/service 与原 operation/权限版本可追溯；新事件 UTC 来源明确，旧 DateTime/来源缺失不得伪装 UTC。现行 OperLogFilter 仍会序列化非排除路由的参数，旧三类日志没有完整同一来源语义。对应 AC-AU01-01/02、AU02-01/04、AU03-02/04。
2. **查询/计数/详情/搜索/导出及当前下载同范围授权**：既有 `OperationLogOwnerV1.ListAsync` 返回原实体，`SecurityLogController` 返回 Reason/IP/UA，字段列表与详情的业务字段范围尚未统一；补齐稳定分页和 metadata/content/export/download 四能力，原文保持受控、未知 policy 关闭相应内容。对应 AC-AU01-03、AU02-03、AU03-01、AU04-01～04。其他领域的具体映射继续由原业务卡负责，本卡承担三类审计共同边界。
3. **清理 policy/hold/引用/原 plan 与收据**：当前 `OperLogCleanupService` 仍按全局天数跨租户删除，`OperationLogOwnerV1.ClearAsync` 仅在 IAM/表锁后删除；未接当前保留类/政策、UNKNOWN/PROCESSING/争议/必要引用排除、预览后 hold 竞争及原键恢复。对应 AC-AU01-04、AU05-01～05。活动 BUG 唯一记录为 [#1240](https://github.com/GTX537/CP6/issues/1240)，本 PR 不修复或关闭它；本次仅源码确认，未执行破坏性复现。

原目标 SYS-AUD-01 保持 PLANNED，原 AC 状态未改。剩余卡完整保留上述业务深度及原正反出口，不靠关闭功能、关闭 worker、扩大 default deny 或新的 NoOp 冒充完成。产品代码、本地/生产配置、数据库和既有环境均不改。

## 拆卡与严格前置校验

`task-definitions.json` 保留原 WP02-13 的 scope/acceptance，增加已验证范围与 `split_children`；WP02-13b 以独立卡保存三组完整余项，回指原卡。原有直接依赖图不改。台账生成器解析此拆卡关系：新的消费者若依赖 WP02-13，必须同时等待全部余卡 DONE；余卡自身允许在父卡的已验证部分 DONE 后接续。目标责任表同时包含原卡及余卡。新增卡不产生额外总预算。

新窗口不能继续用旧 `prerequisite_adoptions` 把未 DONE 提供者当作完成；历史已经交付的当时准入记录保持，不倒写历史。孤立后缀卡、缺失余卡或拆卡循环拒绝。生成任务页明确展示拆卡后的完整出口，避免原号 DONE 造成误读。

3 项定向回归先在旧工具失败，再在修改后通过：新窗口局部采用不可替 DONE、余卡可启动而下游必须等待、孤立/缺失拆卡拒绝。最终执行 `python -B -m unittest test_card_execution test_manage_roadmap -q`：85 项通过（43.010s），记录 `D:/CP6-test-artifacts/WP02-13-card/ledger-regression-final.log`；`manage_roadmap.py --write` 和生成一致性通过，必要链接及任务级完整差异审查通过。该验证是开发台账门，**不是审计产品 PostgreSQL 验收**；原业务 PG 实证仅按上表真实范围复用。

## 下一位与生命周期

阶段 0 先核对已有证据并列精确缺口；下一实施卡为 WP02-13b，不在证据核对期间消费未完成审计出口。其余卡仍须按原前置及拆卡完整出口判断，不能因为原号 DONE 提前开工。后续阶段 0 记录区分可核销和前置未满足的只读核对。

本任务一个分支、一个 PR、一个报告。正常合并、远端核实准确 head 后，由官方 finish 定向删除本地/远端本任务分支，固定工作区干净 detached 并释放 DEV-1；实际结果见本卡 PR 及 `D:/CP6-test-artifacts/WP02-13-card/finish.json`。本卡未启动产品进程；Actions 运行/取消/触发变更、迁移和部署均为零。工作流树保持原已核对内容。

本卡有效区间 2026-11-17T12:18:19.888304+00:00 至 2026-11-17T12:26:27.177779+00:00：0.135h；另接续前卡合并/席位收尾 0.084h，计划累计 0.709h，原号关闭 4 卡、0 目标、新增 1 次拆卡（WP02-13b）。原卡剩余完整出口已转入余卡；阶段 0 和总预算均未到限，未到 24h 检查点。
