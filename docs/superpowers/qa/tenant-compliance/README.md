# 多租户合规 (Tenant Compliance / #5) — T10 真浏览器 + HTTP-layer QA 证据

实施计划 `docs/superpowers/plans/2026-06-24-tenant-compliance.md`（T1~T10）。QA 日期 2026-06-25。
后端 `http://localhost:5177`（Development，`Csrf:Enabled=false` 设计如此），前端 `http://localhost:5173`，库 `CP6DB`。

## 复现步骤
1. 起后端：`dotnet run --project CP6.WebApi --launch-profile http` —— 启动期 `db.Database.Migrate()` 建 `Sys_Users.IsPlatformAdmin`/`Sys_OperLogs.ImpersonatorId` 两列（T1 迁移）+ **守卫块外**幂等种默认租户 + 引导 admin→`IsPlatformAdmin=true`（T8，见下方坑）。
2. i18n 快照重建：`cd cp6.web && npm run i18n:pull && npm run i18n:gen-types && npm run i18n:check` → 4421 keys，`✅ 无缺失`（较 #4 基线 4357 +64 = E-SEC-031~038 + sec.event.19~30 + platform.*）。
3. HTTP e2e：`bash qa_tc.sh`（ASCII 载荷；JWT claim 经 cp6_at base64 解码核验）。
4. 浏览器：见 `tc_platform.png`。

## HTTP-layer e2e（qa_tc.sh，25/26；唯一未过=环境限制非缺陷）
- **平台超管标志**：admin 登录 → profile `isPlatformAdmin=true` + token `is_platform_admin` claim ✓。
- **带外闸门**：admin → `GET /api/platform/tenant` 200 ✓；非平台用户 → 403 ✓（注：用 tfaforce 时实际被 MustChangePassword 中间件先拦=403，E-SEC-031 闸门由 T2 单测 4 例覆盖）。
- **建租户事务原子**：`POST /api/platform/tenant` → 返一次性 `tempPassword` + `tenantId`；DB 确认新 admin `MustChangePassword=1`/`IsPlatformAdmin=0` ✓（租户+admin 单 SaveChanges 原子）。
- **impersonation 全闭环**：
  - START → 返 menus；imp token `tenant_id`=目标租户 / 有 `impersonator_id` / **无** `is_platform_admin` / `must_change_password` 不写出（R3 恒 false）✓。
  - **R9-i**：imp 期间 `GET /api/platform/tenant` → **403 E-SEC-034**（imp 挂起平台权）✓。
  - END → 重签平台超管 token（`is_platform_admin` 恢复 / 无 `impersonator_id`）✓。
  - **R9-ii**：切出后用 START 时保存的旧 imp `cp6_at` 重放 `GET /api/role` → **401**（jti 已双向黑名单）✓。
- **跨租户审计**：`GET /api/platform/audit` 见 `ImpersonationStarted(25)` + `ImpersonationEnded(26)` ✓。
- **GDPR**：导出租户 JSON **不含 `Password`** 键 + 含 admin 行 ✓；`DELETE .../erase/subject/{id}?confirm=true` → 主体 `Enable=0` + `UserName=anon-…`（匿名化保行保 Id）✓。
- **未过（FLOW 7，非缺陷）**：imp 期间业务写的 OperLog 带 `ImpersonatorId` —— 本 dev 环境 **OperLog 走 Kafka 不落 DB**（`Sys_OperLogs` 全空）+ 新租户 qa-tc 无租户作用域 RoleAction 致 imp 写被权限拦。`ImpersonatorId` 透传由 T6 单测 4 例（Kafka payload + DB 降级两路径）证实。

## 真浏览器（gstack browse，headless Chromium）
- admin 登录 → 侧栏 **「プラットフォーム管理」** 入口出现（`isPlatformAdmin && !impersonating`）。
- `/platform/tenant`（`tc_platform.png`）：i18n 全解析（テナント管理/プラットフォーム管理者/なりすましログイン/テナント横断監査/データコンプライアンス）+ 租户表（qatc020642/TENANTB/TENANTC/DEFAULT，有効 tag，編集/停止，分页）。

## 🐛 QA 发现并修复（关键）
- **T8 种子置于 `if (!db.Sys_Menus.Any())` 守卫块内**：既有库已有 menus → 全量 seed 跳过 → 引导首个平台超管永不执行 → `admin.IsPlatformAdmin` 永久 0 → 平台区不可达。**修复 `350838e`**：把默认租户 + 引导超管两段移出守卫块（每启动跑 + 各自幂等）。
- **T9 `table.saveSuccess` 未 seed**：TenantListView 引用了不存在的 key → 改用 T9 已种的 `platform.saved`。

## 回归
后端 `dotnet test` **1189/1skip**；前端 type-check/vitest/build 绿；i18n:check 无缺失。

## 注
QA 留下租户 qatc020642（admin 已 GDPR 匿名化）+ 低权角色/审计行。生产前应清理。
