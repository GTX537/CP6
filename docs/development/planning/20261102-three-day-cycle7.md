# CP6 第七期三天执行计划：可恢复的租户创建

执行：使用writing-plans/executing-plans，本对话单人执行W265-01，一个任务分支、一次集中SPEC/QUALITY审查。

目标：平台管理员确认当前默认管理员菜单模板后创建租户。Tenant、RoleId=1、菜单副本、首个普通管理员、强审计和不含密码的原命令结果同事务；响应未知后只查询或原样重试，避免重复租户及旧密码泄露。

架构：复用TenantAdminService创建核心和W264独立事务/当前会话完成门。模板版本采用现有IAM原生role:1和permission-catalog修订组合，原写/删除重建/ABA已由原Owner推进；不另造资格登记或模板审批流程。新增PG命令表仅保留原请求/当前actor归属/无密码原结果；前端只持久化准确请求。技术.NET 8/EF Core/PostgreSQL 18/Vue 3。

基线Main `1c91cdba66fee7efc1cc444c57eebfd4a9e8efc0`，固定DEV-1/Main、DEV-2/Handoff。三天各6小时核心+2小时缓冲是容量估算，完成以实际验收及收尾为准。原375项27 DONE/2 IN_PROGRESS/346 PLANNED；原59局部关闭3张，剩56张，逐卡继续复用[第六期盘点](20261102-progress-cycle6.md)。125完整目标/247正式验收范围不改。W264最终证明PASS，前六期和七天/W249不重跑。

## D1：原命令与模板一致性

- [x] `CP6.Core/Services/Platform/TenantCreationV1.cs`定义Template(Version/MenuIds)、Command(OperationRef/ExpectedTemplateVersion/Code/Name/ExpireDate/Remark/AdminUserName)、Receipt和Result。Result仅第一次新提交带TempPassword，原结果始终没有密码；恢复结果明确NeedsCredentialReset。
- [x] `TenantAdminService.Creation.cs`和`ITenantAdminService.cs`增加ReadCreationTemplateAsync/CreateVersionedAsync/ReadCreationOperationAsync。复用ManagementTransaction当前actor门；先恢复原件再读当前模板，输入异义409/他人403；首次创建模板改变412/缺失、禁用、空或无效菜单明确拒绝且无租户。Code≤50/Name≤200/AdminUserName≤100/Remark≤500，Unicode、PG微秒无偏移日期边界，原字节保留；沿现有PG大小写不敏感唯一索引。
- [x] `TenantAdminService.cs`创建核心支持Owner已核对的菜单ID，不再读第二份模板；默认旧接口仍走原路径。`TenantAdminService.Versioned.cs`将无关target-id检查移至管理公共入口，使创建复用同一事务。所有效果及命令同commit；commit开始后错误503 UNKNOWN。
- [x] 新建`CP6.Persistence.PostgreSql/Migrations/Core/20261102110000_TenantCreationV1.cs`：cp6_tenant_creation_v1.commands以operation_hash索引、保留原operation/input/result字节和actor/actorTenant；不可更新/删除/TRUNCATE。不存明文密码，不改业务原表。
- [x] `CP6.Tests/TenantCreationV1PostgreSqlTests.cs`用已有真实PG夹具覆盖同键并发/同码异键竞争、大小写冲突、原键异义和actor绑定、模板变化/ABA/空禁用缺失、审计失败回滚、未知提交恢复一次、密码仅首答且持久结果无密码。迁移无损与幂等检查按影响执行。

## D2：当前平台会话HTTP

- [x] `CP6.WebApi/Controllers/Platform/TenantCreationControllerV1.cs`：GET api/platform/tenant/creation-v1/template；POST commands；POST commands/read。复用W264会话适配到`Services/TenantAdministrationHttpV1.cs`，原管理接口也调用它，保持CurrentActor门内检查与最终时钟/注销检查。独立scope、NoStore、严格JSON、既有CSRF；不接收自报actor/tenant。
- [x] 实际Main Program/PG/登录cookie验证创建/原结果查询、一次性密码可登录且MustChangePassword，普通用户、无CSRF、冒用actor、注销及等待中撤权拒绝；重试不再生成密码或第二套用户/菜单/审计。IAM受控凭据重置仍用原Owner，本期不扩出跨租户密码管理入口。

## D3：创建页面与交付

- [x] `cp6.web/src/api/platform/tenant.ts`增加V1 DTO/API；`TenantListView.vue`新建入口使用`TenantCreationPanel.vue`。先加载模板版本/菜单数，保留草稿；412要求明确重读后新提交。发送前将准确原键/请求保存在当前身份隔离的sessionStorage，未知/刷新时只原查询或原样重试；后续404/403不当作未提交。
- [x] 临时密码只在首次结果的内存对话框展示，关闭/身份切换/离页清除，不进URL/storage/提示日志。恢复结果展示原TenantId和账号并说明经IAM受控重置；历史结果不充当租户当前状态。身份切换忽略旧请求响应，存储失败不发送。
- [x] 相关PG/HTTP验证、Vue类型检查与实际Chrome/Vue页面夹具；复用未变证据，保留真实失败。同步roadmap/生成视图/四状态和一份DELIVERY，检查完整diff与工作流签名，正常PR采用、核对远端、精确分支清理、官方finish释放两个固定席位。

验证入口：`run_wave265_main.py creation 'FullyQualifiedName~Tenant_creation_v1|FullyQualifiedName~Tenant_management_v1|FullyQualifiedName~TenantManagementCurrentActorPostgreSqlTests|FullyQualifiedName~TenantManagementAuditPostgreSqlTests'`；Node22 `node_modules/vue-tsc/bin/vue-tsc.js --build`；Node22 `docs/development/evidence/W265-01/check-page.mjs`。全部本地运行，模拟业务数据不冒生产部署。

边界：WP02-04仍有其他配置Owner、旧入口全面版本/原命令采用、全部业务writer/fence、下游映射传播、worker恢复及整卡验收；本期不将整卡标DONE。不改变TenantId、不迁移既有环境、不扩大schema多租户部署范围。命令历史保留期限/擦除继续归治理任务。

三阶段实现/验证及集中审查完成，最后一项状态同步与远端采用/准确清理/finish由wave265-final-verification.json核销。提交前VERIFYING。

W266按wave265-final-verification.json核销第七期正常采用、准确分支清理和官方finish；只归档不重跑。
