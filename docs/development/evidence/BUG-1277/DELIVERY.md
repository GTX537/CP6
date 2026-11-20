# BUG-1277 旧交易伙伴退休入口交付

旧 `DELETE /api/business-partners/{bpCd}` 在 PostgreSQL 上使用 Main Owner 的当前角色判定。现有会话、CSRF、`del`、对象范围和原行版本校验继续适用。

| 项目 | 记录 |
|---|---|
| 派工 | [#341](https://github.com/GTX537/CP6.AI-Handoff/pull/341)，原提交 `a75df01e921f17a8765f7822ccd931dfbc266bc3`，登记采用 `29edae88f435d3ef6384c7832885a034c0d5b11c`，原路径 `handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T000134Z-exec-wp02-14b-bug-1277/MESSAGE.md`。 |
| 领取 | Main `DEV-1`，`D:/CP6-worktrees/wave17-dispatch-20261018`，`BUG-1277-BP-RETIRE`，`codex/bug-1277-bp-retire`。[START](https://github.com/GTX537/CP6.AI-Handoff/commit/8a11cb70694a587f0f4c9d8aa7fa42c0a585c67e) |
| 代码 | 基线 `2fca945ba94dedd1f3c8d91112b35875ba07f236`，修复及回归 `0b82cd45e0d044602eaeb5b04263b8d1f14bf95a`。[PR](https://github.com/GTX537/CP6/pull/1279) · [原 Issue](https://github.com/GTX537/CP6/issues/1277) |
| 验证环境 | Windows、真实 Main Kestrel、隔离 PostgreSQL 18。本地测试使用实际登录签发的会话 Cookie 和 CSRF。模拟用户、设备配置、租户、交易伙伴、预存冻结事实、财务发布数据和财务离线。 |
| 证据目录 | `D:/CP6-test-artifacts/BUG-1277-BP-RETIRE/current-20261120`，`red.trx`、`green.trx`、`identity-isolated.trx` 和对应隔离运行目录内的 `bug-1277-*.json`。合并后独立复测为 `adopted.trx`。 |

## 根因和修复

Main 正常签发的 JWT 携带真实身份与租户，角色由当前权限数据解析。旧 DELETE 装饰器从 JWT 的角色声明匹配角色 1 或 Admin，授权检查在进入 Owner 前拒绝有权管理员。红灯测试用同一登录流程核实当前数据库角色 1、`del` 和对象本人范围，实际收到 403，行版本及状态保持原值，新增退休审计为 0。[签发器](../../../../CP6.Core/Utilities/JwtHelper.cs)

控制器将 PostgreSQL 路径交给原有 `BusinessPartnerService.DeleteAsync`。Owner 在当前事务复核角色 1、`del` 和对象管理范围，提交前复核权限，原 EF 并发检查和字段审计继续随写入提交。保留旧存储路径使用的 1 或 Admin 角色声明检查。产品改动集中在旧 DELETE 动作，测试和运行环境使用 PostgreSQL。[控制器](../../../../CP6.WebApi/Controllers/Erp/BusinessPartnerController.cs) · [Owner](../../../../CP6.Core/Services/Erp/BusinessPartnerService.Owner.cs)

## 实际 Main HTTP 结果

| 验收 | 实际结果 |
|---|---|
| 正常登录、当前角色 1、`del`、本人对象范围 | 红灯 403，绿灯 200。用有效原行版本提交，原响应为 code 0 和空 data。PostgreSQL 记录回读为 `Status=9`、`IsDeleted=true`，行版本更新。HTTP 历史详情回读新行版本，身份原件回读 BP 版本 `2` 和退休状态。 |
| 缺角色 1 | 403，Owner 返回 `BUSINESS_PARTNER_ADMIN_REQUIRED`。 |
| 缺 `del` | 403，伙伴与成功审计无变化。 |
| 超出对象管理范围 | 404，Owner 返回 `BUSINESS_PARTNER_NOT_FOUND`。 |
| 登录后撤销角色 1 | 同一有效会话仍可读取，DELETE 返回 403。 |
| 登录后撤销 `del` | 同一有效会话仍可读取，DELETE 返回 403。 |
| 原版本冲突与 CSRF | 用已过期行版本返回 409，缺少 CSRF 返回 403。拒绝路径均保持状态、版本、修改信息与成功审计。 |
| 财务读取离线 | 实际读得 `UNPROVEN`。持有财务源表的隔离 PostgreSQL 排他锁时，旧 HTTP 仍返回 200。回读 `Status=9`、`IsDeleted=true`、原 `IsFrozen=true` 和新行版本。 |
| 成功审计 | 行写入与一条字段审计同事务提交，状态从 1 到 9、删除标志从 false 到 true。操作者与实际和有效主体一致，原记录具有版本证据及事务引用。冻结离线场景未记录冻结字段变更。 |

修复前原入口 1 项按预期失败。修复后 BUG-1277 定向 **8 项**、伙伴旧档案与身份及财务读取相邻 **23 项**，均有通过记录，零跳过。旧身份历史用例混跑时，其全表负向 SQL 遇到财务用例留下的 Int64 最大版本，收到 `22003`。保持相同构建，在全新隔离库单独复测该项通过。日志保存 HTTP 状态和模拟业务差异，登录凭据未纳入交付证据。

## WP04-01 兼容关系

旧入口保留按 `bpCd` 和原 `rowVersion` 交互的软退休，保持独立冻结事实。专用 B13 `POST /api/main/v1/bp/retire` 由 WP04-01 提供，仍按冻结合同验证真实法人 Scope、动作理由、BP-CAS 强 ETag 和 expectedRevision、UUID 幂等键、完整 ProfileRevision、状态时间线及原 Command 回执。ERP-BP-01 的 AC033 仍须按正式 Mapping、历史订单和用途入口验收，AC034 的 RESTORE 及保留 Hold 仍归原卡。[WP04-01](../../task-plans/WP04.md#wp04-01) · [ERP-BP-01](../../../design-handbook/modules/ERP-BP-01.md)

## 工时及收尾依据

本缺陷自 `2026-11-20T00:44:51.051439+00:00` 领取，候选验证截至 `2026-11-20T00:57:26.916618+00:00`，缺陷有效工时 **755.865 秒**单独列账。阶段 C 路线图和两项财务目标按 #341 保持原登记。WP11-65 的 218.459 秒按 #340 留待下一张正常 DONE 或拆卡 PR 结转。[#340](https://github.com/GTX537/CP6.AI-Handoff/pull/340)

远端采用以后以相同真实登录和原 DELETE 复测。Issue 回写及正式 finish 证据含完整采用提交、实际冒烟、任务分支清理与 DEV-1 工作区状态。[原通信分支](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/BUG-1277/messages)
