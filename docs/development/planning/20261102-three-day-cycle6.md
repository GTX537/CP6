# CP6 第六期三天执行计划：租户配置与启停的版本化操作

目标：平台管理员读取租户当前版本，保存配置或停用/恢复时检测并发变化；超时后按原键查询或原样重试，页面保留未确认命令。使用writing-plans/executing-plans，本对话单人执行，一个W264-01分支、一次集中审查。

基线Main e4fd9d0d547eb6012a58d44516e45c5e60f69903；固定DEV-1/Main和DEV-2/Handoff。预计三天各6小时核心+2小时缓冲，完成以真实验收和收尾为准。前五期和W261已经最终合并/清理/释放，不重复执行。盘点见[当前进度](20261102-progress-cycle6.md)。

架构：复用TenantAdminService的当前平台管理员授权、原始租户变更和必需审计；V1独立事务拥有CAS、原命令记录和提交结果分类。PG旁表维护版本，Sys_Tenants原生触发器覆盖配置/启停及删除重建，旧兼容写入也推进版本。当前会话在事务内核对；共享IAM门保持既有权限与身份事件的原子性。技术：.NET 8 / EF Core / PostgreSQL 18 / Vue 3。

## D1：版本与原命令

- [x] 新建CP6.Persistence.PostgreSql/Migrations/Core/20261102100000_TenantManagementV1.cs：共享schema cp6_tenant_management_v1，versions保留租户ID和bigint单调版本，commands按target/operation_hash保留原请求/actor及原结果。现有租户从1开始；原生INSERT/DELETE及TenantCode/Name/Enable/ExpireDate/Remark/TimeZoneId实际变化推进版本，删除后保留版本避免同ID重建ABA。历史命令不可修改/删除/截断；不对现有业务库执行迁移。
- [x] 新建CP6.Core/Services/Platform/TenantManagementV1.cs和TenantAdminService.Versioned.cs，既有服务改partial，接口增加ReadManagement/ExecuteManagement/ReadManagementOperation。请求OperationRef+ExpectedRevision+Kind(update/suspend/reactivate)+Configuration；版本通过字符串传HTTP保留bigint精度。先恢复同actor原命令，再判断当前版本；不改创建的一次性密码合同。复用既有UpdateCore/SetEnable核心，不重复资格包装。
- [x] 配置边界验证名称长度、PG时间精度和无偏移时间、时区；原件字节保留不trim name/remark。冲突412、同键异义409、未知提交503。原命令结果和原业务/审计同事务。相关PG测试覆盖竞争只成功一次、ABA、旧写可见、原结果、actor隔离、权限撤销、审计失败回滚、提交已成功但响应丢失后的恢复、迁移无损。

## D2：当前会话HTTP

- [x] 新建CP6.WebApi/Controllers/Platform/TenantManagementControllerV1.cs：api/platform/tenant/{id}/management-v1；GET当前，POST commands提交，POST commands/read查询原件。独立DI scope使用同一CP6Context和现有服务，当前登录态/平台管理员在事务中核对；NoStore、严格JSON、CSRF、清晰状态码；不接收Actor或作用域TenantId。
- [x] 使用实际Main Program、登录cookie、自有PG和模拟平台管理员/租户验证配置→停用→恢复、并发412、同键异义、无CSRF/普通用户拒绝、管理员撤权与会话撤销。旧管理API继续兼容且由触发器推进版本；本窗口不宣称旧入口已有强制CAS。

## D3：管理页面与交付

- [x] cp6.web/src/api/platform/tenant.ts增加V1类型/调用；TenantListView.vue接入TenantManagementPanel.vue，编辑先加载完整当前快照，读取失败不允许空字段覆盖。配置保留完整过期时间和字段值，启停使用已读版本；412保留草稿并明确重新读取后才能新提交。
- [x] 发送前在当前用户/租户隔离的sessionStorage保存准确命令；未知结果保留原键并提供查询/原样重试，刷新可恢复，未确认期间不接受新命令或静默改键。身份变化隐藏旧身份数据和待确认命令，原结果和当前状态分别呈现。
- [ ] 本地PG/实际HTTP相关验证、Vue类型检查、实际Chrome/Vue页面夹具。集中审查完整diff，失败仅补受影响验证；同步roadmap/生成视图/四状态和一份报告，正常PR合并并核对远端，准确删除任务分支、官方finish释放席位。

验证入口：run_wave264_main.py management 'FullyQualifiedName~Tenant_management_v1|FullyQualifiedName~TenantManagementCurrentActorPostgreSqlTests|FullyQualifiedName~TenantManagementAuditPostgreSqlTests'；Node22 node_modules/vue-tsc/bin/vue-tsc.js --build；Node22 docs/development/evidence/W264-01/check-page.mjs。验收采用模拟业务数据，真实运行本地Main/PG；不以跳过或候选代替通过。

边界：WP02-04整卡不因此DONE。租户创建原命令/模板版本、其他配置Owner和旧接口全面CAS采用仍保留；认证策略字段由原Owner管理，不受本窗口配置覆盖；不改变TenantId、共享数据布局或生产部署权限。

三阶段实现、本地验证和集中审查完成；最后一项状态同步完成，正常合并/准确清理/席位收尾由wave264-final-verification.json核销。提交前VERIFYING。最终会话检查按效果前授权与效果后时钟/注销分开，允许自身租户变更合法生效，见合同。
