# 字段级审计 (Field Audit) — T8 真浏览器 + HTTP-layer QA 证据

实施计划 `docs/superpowers/plans/2026-06-23-field-audit.md`（T1~T8）。本目录是 T8 的可复现 QA 环境与证据。
QA 日期 2026-06-25。后端 `http://localhost:5177`（Development，`Csrf:Enabled=false` 设计如此），
前端 `http://localhost:5173`，库 `CP6DB`（SQL Server `localhost\KOUSQLSERVER`）。

## 复现步骤
1. 起后端：`dotnet run --project CP6.WebApi --launch-profile http` —— 启动期 `db.Database.Migrate()` 自动建 `Sys_FieldAuditLogs` 表（T1 迁移）。
2. i18n 快照重建（验证 T6 词条）：起后端后 `cd cp6.web && npm run i18n:pull && npm run i18n:gen-types && npm run i18n:check`
   → 4357 keys，`✅ 无缺失 key`（较 2FA 基线 4337 +20 = T6 新增 20 sec.audit.* 词条）。
3. HTTP e2e：`bash qa_fieldaudit.sh`（ASCII-only 载荷；Windows bash 的 curl -d 不会把中文按 UTF-8 发送）。
4. 浏览器：见下方截图 `fa_timeline.png`。

## HTTP-layer e2e（qa_fieldaudit.sh，13/13 通过 + 用户更新单独验证）
> 后端 `BizException` 返本地化译文；断言以行为/状态/Cookie/DB 核验。捕获挂在 `CP6Context.SaveChanges` 两阶段重写（T3），任何经运行中后端对 `IAuditable` 实体的写都自动留痕。

- **新增留痕**：建角色 9001（`POST /api/role`）→ `Sys_Role` Op=1 审计行，`changeCount>=1`，**主键 RoleId 不入 diff**。
- **修改准确 diff（证 T4 先查后改）**：改 RoleName（`PUT /api/role`）→ Op=2 行 `RoleName: QARoleOne → QARoleTwo`（仅改动列，旧→新准确）。
- **时间线正序**：`GET /api/sys/field-audit/record` 首行=Added。
- **用户字段留痕 + 密钥护栏**：改 tfaforce（`PUT /api/user`，须带 Password 否则 400 模型校验）→ Op=2 行含 `NickName 强制用户→AuditRenamed3` + `MustChangePassword`/`PasswordChangedAt`，**即使密码被重设，Password 不出现在 diff**（`[AuditIgnore]`）。
- **删除留痕**：删角色 9001（`DELETE /api/role`）→ Op=3 行。
- **密钥护栏（DB-wide）**：`SELECT COUNT(*) FROM Sys_FieldAuditLogs WHERE Changes LIKE '%assword%' OR '%ecret%' OR '%okenHash%' OR '%Salt%'` = **0**。fauditlow 创建行（Op=1，19 字段）亦不含 Password。
- **多租户**：admin 所有审计行 `TenantId` = DEFAULT（`...A1`，StampTenant 镜像业务实体 R4）。
- **权限 403**：建低权角色 9002 + 用户 fauditlow（无 `sys-field-audit:query`）→ 登录后访问 `GET /api/sys/field-audit` → **403**；admin → 200。

## 真浏览器（gstack browse，headless Chromium）
- **列表页 `/sys/field-audit`**：i18n 全解析（フィールド監査/エンティティ/主キー/操作者/変更フィールド数/追加・変更・削除 着色 tag），13 行真实捕获数据（Sys_Role 9001 追加→変更→削除、Sys_User NickName 改名 changeCount=3、fauditlow 追加 changeCount=19、Sys_Menu 115=T5 seed）。
- **时间线抽屉（`fa_timeline.png`）**：点「タイムラインを表示」→ el-drawer「変更タイムライン」按时间正序回放 Sys_Role 9001：
  - 🟢 追加 admin：Description/Enable/OrderNo/RoleName —→新值（主键 RoleId 排除）
  - 🟠 変更 admin：RoleName QARoleOne→QARoleTwo（旧值红 / 新值绿，仅改动列）
  - 🔴 削除 admin：各字段 旧值→—

## 注
- QA 留下低权角色 9002 + 用户 fauditlow（密码 123456）+ 审计历史行（审计行即本功能数据，故保留）。
- 后端启动期 T5 的菜单 115 seed 首次插入也被审计（Sys_Menu 115 追加/MenuKey 回填 変更）——证明 seed 写路径同样留痕。
