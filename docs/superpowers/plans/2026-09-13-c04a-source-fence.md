# C04A 本地源端写入保护实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. 按本仓库任务级集中审查规则执行，不逐文件重启完整交付。

**Goal:** 把旧 CRM 空来源的检查、原子冻结、围栏前恢复和不可逆封口变成可运行工具，并在本机真实备份的独立恢复副本中留下证据。

**Architecture:** 独立 SQL Server 工具，只允许对本机 `CP6_C04A_Rehearsal_*` 副本实施变更。冻结事务锁住 20 张旧 CRM 表，拒绝非空来源，安装 DML 触发器与 public 写入/ALTER 拒绝，并提交 RunId、递增 generation 和审计；恢复撤去本工具的保护，封口后拒绝恢复。恢复副本由 COPY_ONLY/CHECKSUM 备份生成，不覆盖数据库。

**Tech Stack:** .NET 8、Microsoft.Data.SqlClient、SQL Server、PowerShell；普通验证全部本地执行。

## 依据与边界

用户已批准 CRM02 合同和 migration map，兼任三类负责人、暂无替补；CRM PR #68 的本地审批门禁已关闭。用户“好的，请继续推进”授权沿既定规范推进技术实现。本任务不重新征求这些业务确认。

规范依据是 `docs/crm/CRM-V1-EXECUTABLE-SPEC.md` §13.5–13.6、§18。当前先完成 **C04A 源端控制组件及空来源恢复演练**；CRM11 目标 Schema/迁移、真实路由切换、首笔新业务写入接线、旧写入身份盘点仍须后续验证。`SealForwardOnly` 是开放新写入之前的保守封口，不冒充发生了首笔新写入。

运行中的旧库使用集成身份，当前检查操作身份为 sysadmin。SQL DENY 无法约束 sysadmin/dbo，因此本组件明确不出具完整写入围栏验收，也不对运行中的旧库启用保护。保留旧实体映射、C01/C02 身份数据和 ERP；不把共享数据库整体设为只读。历史清理服务也是旧 CRM 写入方，数据库保护须覆盖直接 SQL，不能仅拦截 SaveChanges。

## 任务与验证

- [x] 从已核验 Core main `e63f96f8c8071fb2d1b5391b56c68a5c182a1d48` 创建独立任务分支/worktree；确认 CRM PR #68 的精确审批输入。
- [x] 在 `tools/CP6.Crm.SourceFence/` 实现只读检查、原子冻结、恢复、不可逆封口和明确的副本身份限制。
- [x] 在 `eng/crm/source-fence-tests/` 先记录失败测试，再覆盖真实 SQL 并发、失败回滚、20 表保护、非空/结构集合漂移、低权限 DML/Bulk/TRUNCATE/ALTER、ERP 不受影响、状态/重放/篡改拒绝。
- [x] 在 `eng/crm/source-fence-rehearsal/` 提供可复现的只增不覆盖备份恢复入口；源端只读观察和 COPY_ONLY 备份，恢复副本执行生命周期验收；原始失败保留。
- [x] 留存 `docs/evidence/c04a/2026-09-13/` 的来源、实际执行输入哈希、原始结果与限制；不公开连接字符串、备份内容或其他业务行。
- [x] 同步四份项目状态，说明已消除的 CRM02 本地审批前置与 C04A 实际完成范围。
- [x] 集中完成规格及质量审查、适用契约静态检查和完整 diff 检查；仅修复后增量回归。
- [ ] 每次 push/PR/merge 前复查双方工作流事件与当前运行；正常 PR 合并、核验远端 main 包含关系和相同代码树，不触发 Actions。

## 完成标准

组件测试与真实空来源备份恢复演练成功，源库业务数据/Schema 未被工具修改，证据与代码一并正常交付到远端 main。只关闭这项源端组件任务，C04A 整体和 CRM11/C04B 保留未完成状态。
