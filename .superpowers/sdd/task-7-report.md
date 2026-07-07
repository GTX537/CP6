# Task 7 报告：回归 + 真库 QA（波4 DoD 验收）

分支 `feat/space-wave4-crosscutting` @ HEAD e2ddf74。执行日 2026-07-07。
无代码缺陷 → **无 fix commit**。步骤 3-6 的线上验证因环境不稳被降级（brief 明列许可，不算失败），详见下。

---

## Step 1 — 回归门（全绿）

| 门 | 结果 | 说明 |
|----|------|------|
| 后端 `dotnet test CP6.Tests` | **Passed! Failed:0, Passed:1565, Skipped:5, Total:1570 (56s)** | 命中 DoD 1565/5 |
| 前端 type-check `vue-tsc --build` | **exit 0，零错误** | 首跑默认堆下 OOM（`FATAL ERROR: Zone Allocation failed - process out of memory`，假 exit 0）；`NODE_OPTIONS=--max-old-space-size=6144` 重跑干净 |
| 前端 `vitest run` | **Test Files 57 passed, Tests 369 passed (369)** | 命中 DoD 369；首跑 fork worker 被 OOM 杀（2 file 假失败），改 `--pool=forks --no-file-parallelism` 单进程重跑全绿 |
| 前端 `vite build` | **✓ built in 6.74s，exit 0** | 仅 chunk>500kB 告警（既存，非本波） |

> 机器仅 8GB 物理内存（空闲 ~4.3GB）+ WSL VM 内 7 容器，前端两门首跑均因内存被 OOM 打断（假信号）；串行 + 限并发 + 提堆后均真绿，已如实记录。

## Step 2 — 真库种子（成功）

执行 `docs/seeds/space-roleaction-seed.sql`（容器内 `/opt/mssql-tools18/bin/sqlcmd`，CP6DB，stdin 管道）：

```
=== Space 権限点シード 開始 ===
  租户数                    : 4
  MenuAction 件数(902-905)  : 52（租户数 × 13 想定）
  RoleAction 件数(管理者)   : 52（租户数 × 13 想定）
=== Space 権限点シード 完了 ===
```

- 前置：Sys_Tenants=4；种子前 MenuAction/RoleAction(902-905)=0/0。
- 种子后：**52 / 52**（= 4 租户 × 13 动作），逐租户确认展开（验证 SELECT 逐行列出 TenantId A1/B1/C1… 的 902-905 全动作）。幂等 NOT EXISTS，可重跑。

---

## 环境根因（决定步骤 3-6 走降级）

1. **运行中的 docker `cp6-api` 镜像 = 2026-07-03 构建**，早于**全部**波4 提交（2026-07-07）。故其 **无** 新的 403 权限强校验 / BizException 信封 / 字段审计 行为——对它验证波4 无意义。
2. **临时波4 后端起不来**：本分支 `CP6.WebApi` 构建干净（0 error），但 `dotnet run` 两次均在 `Program.cs:640 db.Database.Migrate()` 崩溃，SQL 错误 **10061（连接被拒）**。原因：docker 栈约每 30-60s 整体重建一次（7 容器同步 `Up Xs`、RestartCount=0、WSL VM load avg 5+ CPU 饱和 → 外部 keepalive/recreate 型抖动，非单容器 OOM）。启动 dotnet 的内存/CPU 峰值每次都与一次栈重建撞车 → Migrate 时 DB 正好宕。两次尝试、同一崩法。
3. 连纯 DB 的 `sqlcmd` 会话也只有 ~15s 稳定窗口（须 hammer 重试才落窗）。

→ 命中 brief 降级条款：「新代码后端起不来（内存/DB 冲突）… 行为已由单测+反射测试锁定，线上验证待重部署」。WebApplicationFactory 在本仓仅 1 处浅用（`RequirePlatformAdminFilterTests`，无 `CreateClient` HTTP 集成先例），且真 SQL 集成同受抖动困，故不新建。

## Step 3 — 403 权限（降级：源码 + 单测 + 真库数据锁定）

- **源码实证**：`CP6.Core/Auth/RequirePermissionAttribute.cs:38` 无权限时正是
  `new ObjectResult(new { code = 403, message = $"无权限：{_menu}:{_action}" }) { StatusCode = 403 }` —— **精确匹配** brief 期望 `{code:403, message:无权限...}`；服务缺失时 500。
- **单测锁定**（在绿色 1565 内）：`RequirePermissionFilterTests.NoPermission_Sets403`（断言 403）、`FourGranularityIntegrationTests`、`PermissionChainIntegrationTests`、`Space/SpacePermissionAttributeTests`。
- **真库数据实证**：RoleAction(902-905) 52 行已种，admin RoleId=1 持全 13 动作 → admin 命中 200 路径；未授该动作的角色 → `HasActionAsync` 假 → 403。数据层齐备，仅缺可运行的波4 后端做端到端 HTTP。

## Step 4 — 字段审计（降级：源码 + 表存在 + 单测锁定）

- `CP6.Entity/DomainModels/Space/Space_Site.cs:11` = `Space_Site : BaseBizEntity, IAuditable` ✓（Task 1 接线）。
- **真库实证**：`Sys_FieldAuditLogs` 表存在（sys.tables 计数=1）——审计落库端已就绪。
- **单测锁定**：`Sys/FieldAuditR2RegressionTests`、`Sys/FieldAuditControllerTests`（拦截器写 Modified 行、Changes diff）。
- 未跑：`admin PUT site 改 SiteName → 查新 Modified 行` 需可运行波4 后端触发拦截器；待重部署。

## Step 5 — BizException 词条译文（降级 + 关键真库发现）

- **链路源码确认**：`LocationPublishService.DeactivateAsync` 对草稿库位（`Status != 1`）`throw new BizException("E-SPACE-004")`（line 117/119）→ `BizExceptionMiddleware`（`Middleware/BizExceptionMiddleware.cs:20-37`）在请求 culture 下经 `IStringLocalizer`(DbStringLocalizer 读 **Sys_Langs**) 解析，返回统一信封 `{ code: HttpStatus(400), message: <译文>, data: null }`；未命中回退码本身。culture 优先级含 `Accept-Language`（Program.cs:2494）。
- **词条源真值确认**：`CP6.WebApi/Seed/I18nSpaceScreenSeed.cs:19` 定义 E-SPACE-004 五语，日文 = 「ロケーションが存在しないか、公開済みコードは変更できません」。
- **关键真库发现**：CP6DB 内 `Sys_Langs` 中 **E-SPACE-* 词条数 = 0**（E-SPACE-004 计数=0）。即波4 i18n 词条**尚未落库**——因它由 C# 启动种子（I18nSpaceScreenSeed）在后端启动时写入，而(1)线上 cp6-api 是 7-03 旧镜像，(2)临时波4 后端从未越过 Migrate。故线上「ja → 日文译文而非 E-SPACE-004 中文原串」**无法当场演示**，待波4 后端重部署（启动即种词条）后方可验。
- **单测锁定**：`LocationPublishServiceTests` / `SpaceMasterServiceTests` 锁 DeactivateAsync 抛 E-SPACE-004；中间件翻译逻辑由源码审阅确认（catch→localizer[code,args]→信封）。

## Step 6 — SpaceHub 冒烟（降级，符合 brief 预案）

- 无可运行波4 后端 → 无法「发布一次看日志无推送异常」。前端订阅/自动刷新逻辑由 vitest（369 全绿，含 `@microsoft/signalr` 相关单测）锁定。
- 真浏览器验证按 brief 并入**波5 视觉走查票**。

---

## 清理确认

- 两次临时后端（task bcv8h7utd / bof8o5fw6，port 5177）均自行崩退（exit 127），无残留：`5177 not listening (clean)`，近 30 分钟 dotnet 进程数 0。
- 未创建任何端口转发/隧道。docker 栈与旧 cp6-api 未改动（临时后端用独立 5177，与 docker 9991 不冲突）。
- 种子为预期持久化数据（RoleAction/MenuAction 52+52），保留。

## 问题 / 遗留

1. **[环境, 阻断线上验证]** WSL/docker 栈约 30-60s 整体重建、CPU 饱和（8GB 机 + 7 容器），无法承载临时后端或稳定 DB 会话。波4 的 403/审计/BizException 三项**线上端到端**验证全部待「重构建 cp6-api 镜像并重部署」后补做——建议列波5 首票。
2. **[数据缺口]** CP6DB 的 `Sys_Langs` 尚无 E-SPACE-* 词条（0 条）；重部署波4 后端会自动种入（I18nSpaceScreenSeed），无需人工 SQL。
3. 无代码缺陷、无需 fix commit。回归门（1565/369/type-check/build）全绿构成波4 DoD 可自动化部分的通过证据。
