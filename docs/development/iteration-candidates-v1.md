# 迭代候选方案 Owner v1

W198 为 WP05-19 提供原生候选草稿与准确技术版本，沿用 [工程项目和迭代](engineering-iteration-v1.md)。它不是完整评审送审、批准、冻结或制造放行实现。

## 身份、入口与当前授权

Owner `Main.Plm.IterationCandidate.v1`；命令合同 `Main.Plm.IterationCandidate.commands.v1`。ITER 候选 ID/版本独立于 ENG Revision、ENG Candidate、迭代 InputVersion/PlanVersion 和未来工程基线。一个迭代允许多个候选，每个候选独立版本链；父方案固定为同一迭代的准确候选版本，创建后不变，不能自指。

页面 `/plm/engineering-iteration/:iterationId/candidates` 从迭代入口进入。API `api/iteration-candidates/owner-v1` 提供按迭代分页、准确版本读取、比较、命令和原操作查询/关闭。读取使用 `plm-engineering-iteration:query`，维护使用独立 `candidate-write` 动作。迁移只登记动作，不自动给角色授权。

当前项目成员、父迭代对象范围、字段权限和全部披露来源权限同时成立后才可读取，先检查父对象再分页；全数据范围不代替项目成员资格。历史版本、前版/父方案差异和原操作重放也重新检查关联旧来源权限。所有依赖必须处于同一 CP6Context/事务，SourceCheck 的授权水位在其自身观察范围中比较，成功观察仍加入外层最终授权检查。

## 写入和原件

CREATE/REVISE 只能在活动项目、InDesign 且非暂停迭代中执行，要求当前项目/迭代负责人、准确已批准需求输入和有效产品/场地。命令同时提供当前迭代引用/ETag；修订还须候选版本/ETag。技术来源从当前获授权的 ENG 修订检查历史中选择准确 SourceCheck 引用，禁止前端自造摘要或在 ITER 内改写技术内容。

候选原件保存准确迭代输入、产品/场地、需求集、ENG 修订/候选引用、完整技术检查对象及其原始 JSON、父方案/父基线、差异说明和风险假设。BOM/工艺/资源/文档等发生位置保留各自 occurrence 身份、顺序与精确值。语义差异覆盖标题、说明、输入、配置、技术引用和发生位置，并分别记录相对前版及父方案的变化；完整原件独立可查。

`ADOPT_CURRENT` 要求检查来源水位仍为 CURRENT；旧水位只能明确选择 `KEEP_ORIGINAL` 并给出原因。技术内容尚未齐备的 BLOCK/UNKNOWN 检查允许保存在草稿中，页面明确显示待处理内容；它们不会得到评审或冻结资格。输入变化、来源变化和无法确认状态分别保留；读取不会覆盖原件。当前固定评审状态 `NOT_SUBMITTED`、冻结状态 `NOT_REQUESTED`。

PostgreSQL schema `cp6_iteration_candidate_v1` 使用不可变原件、SHA-256、版本 CAS、父子/准确迭代引用约束、原操作 journal 的延迟外键与 provenance 约束。候选和原操作同事务保存；先持有父迭代再持有候选和来源，最终重新检查权限与范围。命令上限 2 MiB，完整保存体上限 16 MiB；超限明确拒绝，不截断原件。只增加前向迁移，没有既有环境迁移执行。

## 未知结果恢复

按环境、REAL/DEMO、租户、用户隔离恢复键，浏览器只持久保存 operationId、iterationId、candidateId，不保存私有原件。跨页互斥、服务器当前身份核对和旧响应丢弃共同避免串号。网络结果不明确时保留原键；查询 COMMITTED 返回准确原结果，UNKNOWN 不允许重复新写，明确关闭未提交原键后阻止迟到写入。服务端恢复仍要求当前成员、字段和来源权限。

## 交付范围

验证及生命周期见 [W198 交付报告](evidence/W198-01/DELIVERY.md)。本窗口未实现完整候选全集 ReviewPackage、RoundBinding、OA 送审/Inbox/回执与撤回竞争、Owner 批准采用、工程冻结/REL、实际 WO/SCHED；这些仍是后续任务。草稿的本地原生验证不代表企业正式验收或七天目标完成。
