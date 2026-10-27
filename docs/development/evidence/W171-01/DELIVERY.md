# W171-01 — Scope 授权门公共前置

COORD 单人，原 CP6-7D-20261022 持续目标。Main 基线 `e99f2f763ae783836952e50d5851c289b2812b0a`，固定 DEV-1 `codex/w171-01`；Handoff 基线 `4842d97717584cf52eeb0bedfcd48f8acc2da515`，DEV-2 `codex/w171-handoff`。W170 已收尾，本轮只归档一次并关闭 WP12-01.wave170_explicit_drafts。[计划](../../dispatch/20261027-wave171/README.md)。

## 实际交付

新增 PostgreSQL forward-only migration `20261028060000_ScopeAuthorizationFenceV1`。schema `cp6_scope_authorization_v1` 保存唯一部署环境绑定及 `(tenant_id, environment_id, logical_domain)` → `permission_domain` 记录。当前逻辑域为 WF_AUTH，真实域 GUID 由服务器创建，后续启停不换域。epoch bigint 与 UUID row_version 由数据库生成并推进；不能回退 epoch、换租户/环境/域或删除事实。没有预置企业环境、租户登记或角色授权。

ScopeAuthorizationFenceV1 要求真实同连接 Npgsql ReadCommitted 事务，无 ambient 事务，固定 native AuthenticationCompletionGate → Scope fence → 后续 command/business objects 锁序。现有 native root 是外层串行根，不被冒充为 ScopeAuthorizationFence。Acquire 返回绑定当前 context、transaction、tenant/environment 的协调凭据；Validate 在提交前核对同一映射、epoch 和 row_version。它不创建/提交事务，不自行决定资源、字段、SOD 或实际有效 Actor 权限。

14 张现存权限源表的 BEFORE STATEMENT 触发器，在 SQL 权限变更前持外层 root，再按 tenant/environment/domain 稳定顺序锁真实 fence 并递增 epoch，覆盖直接 SQL、零行保守失效、撤权和 ABA：Sys_Tenants、Sys_Users、Sys_TenantSsoConfigs、Sys_Roles、Sys_UserRole、Sys_RoleMenus、Sys_RoleAction、Sys_RoleDataScope、Sys_RoleFieldPerm、Sys_MenuAction、Sys_Menus、Sys_Dept、Wf_FlowDelegate、Wf_ApproverMap。writer_profile 明确为 MAIN_NATIVE_IAM_WF_V1；不是对尚未实现 SOD/正式资源授权来源的覆盖承诺。触发器缺失/禁用/错误事件或 replica 模式拒发新凭据。

当前选择对数据库内全部已登记 Scope 保守失效，因原认证根本身为全局串行锁，并需覆盖全局菜单及跨租户 SQL；它会造成无关租户版本刷新，不声称完成性能优化。未来改变为精细失效需要同样证明全量 writer 参与与锁序。数据库 owner/迁移身份篡改函数或禁用后重新启用且掩盖变更不在普通应用权限威胁边界内；生产必须保持应用与 DDL 权限边界。

## 受控入口与消费约束

部署配置 `ScopeAuthorization:EnvironmentId` 必须由受信部署提供非空 GUID。缺配置默认关闭；注册的数据库环境不可通过改配置重绑。请求体不能指定 TenantId、环境、权限域或 writer_profile。

- `GET /api/iam/scope-authorization`：通过既有 IamAdministrationHttpV1.CurrentActor 在等待锁后重验当前 access session/expiry/authentication receipt，拒绝 impersonation，核对当前原 Actor、有效租户/用户、已登记 `pub-role-perm:edit` 权限，返回版本（首次 absent）与原映射。
- `POST /api/iam/scope-authorization`：严格字段 OperationId、ExpectedVersion、Enabled、Reason；等待锁后及提交前分别重验当前原主体会话；首次只能启用，后续 CAS 启停，原操作、结果及 audit_json 同一 Main Owner transaction。
- `GET /api/iam/scope-authorization/operations/{operation}`：当前有效原 Actor 可查自己的原结果，编辑权撤销不会改写历史结果。不同 Actor 拒绝，其他租户不能读取。NOT_OBSERVED 不证明此前没有提交。

同一 OperationId/原 body 在响应丢失后恢复原结果；改 body 为409；旧版本为412；未知提交为 SCOPE_OPERATION_UNKNOWN。登记完成后发生的撤权会推进当前版本，但原回执保持原时点。API 使用实际 Program 认证/CSRF，响应 no-store；无新页面。

业务消费者须在业务 SQL 写入前 Acquire，在同事务检查并保存实际 Actor、effective grant 版本、资源/字段/SOD/委派和时间有效性，先锁 fence 再锁业务对象，提交前 Validate 并重新核对时间变化；任何失败回滚整个事务。当前 W170 native draft/现有 Workflow 路径尚未切换，不将登记成功等同正式 DEF/FORM/TASK/OA-APP 准入。新增权限/SOD等来源必须先加入受审查 writer profile，才能由相应消费者声明完整覆盖。

## 实际验证与审查

本地31项通过，0skip：最终15项 Scope PG18/actualHTTP；既有12项 IAM policy service/HTTP、1项 SSO policy PostgreSQL、3项二步认证 unit。迁移仅在新隔离本地数据库运行。覆盖真实跨连接 revoke 等待、下一事务新 epoch、同事务 revoke 导致业务与 epoch 整体回滚、域/环境不可重绑、epoch不可回退、租户/事务不混用、并发首次登记单一赢家、丢失 commit ACK 原结果恢复、当前原 Actor/租户、strict HTTP/CSRF 和当前已有 IAM Writer 兼容性。

原始执行：`D:/CP6-local-test/wave171/foundation/scope-authority-final-20261027T083343Z.json` 与 `iam-compatibility-20261027T082025Z.json`；完整映射 `final-validation.json` 保存实际测试名、源码 blob、终态和日志。初轮9项通过；加入底层事务连接核对及边界用例后最终12项通过。最终命令曾按文件名选择 IAM partial class，实际只命中12项，未虚记兼容验证；随后独立 --no-build 选择实际方法执行16项。审查把最终版本核对移到 Actor 解析之后，定向2项中1项旧错误码预期失败（实际 ACTION_FORBIDDEN、原预期 SCOPE_AUTHORITY_CHANGED）；更新为当前撤权语义并保留整体回滚断言，另加 Actor 解析期间同事务 epoch 变化的真实注入。审查又接入既有 IAM 当前会话/过期/凭据重验并拒绝 impersonation，增加完成时过期回滚与冒充拒绝两项。最终15项 Scope 全部通过；16项兼容结果按未变迁移/IAM代码复用，失败日志保留，不改称通过。

一次执行者 SPEC/QUALITY 完整审查，覆盖受信 Scope、真实锁、SQL writer 顺序、ABA、最终回滚、原结果身份与残余范围。实际无前端改动，未重复 UI 测试/构建。未运行 Actions、既有环境迁移、生产部署或改变分支保护。

## 台账与生命周期快照

只给 WP02-10 增加 `wave171_scope_authorization` 限定记录；原正式状态、依赖、125完整目标、247真实验收不变。D1/D4 DONE，D2/D3/D5/D6/D7 PARTIALLY_ADOPTED。2/7=28.6%仅阶段数量，不能作为实际工作完成百分比；旧35–55/42–70小时估计过期。

Handoff：https://github.com/GTX537/CP6.AI-Handoff/pull/147；源 `070854b4af83e4cc005b8413c29a1bcf85efbcf7`，目标 `4c55189e83c7ac20b6f3c6d529f6dd7167ca2c00`，准确树采用已核对，任务分支已清理，DEV-2已finish释放。Handoff 消息保留其审查前29项通过快照，后续Main主体重验新增2项，最终31项以本报告为准。Main 此提交是正常 PR/finish 前快照，登记耗时 33.93 分钟；初估90–150分钟，最终官方 START→finish 由 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave171-final-verification.json` 记录，不把 Handoff 重叠重复计时。

Main 下一步正常合并、远端采用核对、准确分支清理与 DEV-1 finish。固定目录与依赖缓存保留，用户根目录原有未跟踪文件保留。下次准入只归档 W171 一次并关闭本限定字段；七天目标保持 ACTIVE，继续实际 Workflow 消费、发布及其余原出口。
