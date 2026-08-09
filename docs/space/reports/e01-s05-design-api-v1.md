# E01-S05 Design API v1 交付报告

- 日期：2026-07-30
- 状态：受控实现完成，已合入唯一集成分支
- 功能提交：`codex/space-e01-s05-design-api@3258d47f`
- 集成提交：`integration/space-v1-20260730@36f534d9`
- 基线：`integration/space-v1-20260730@a9dc903c`
- Migration：`20260726092519_SpaceE01S05DesignApiIdempotency`

## 1. 交付范围

本卡建立独立于 Legacy Space API 的 Design API v1：

| Method | Route | Permission |
|---|---|---|
| GET | `/api/space/design/v1/sites/{siteId}/model` | `space:model:read` |
| GET | `/api/space/design/v1/sites/{siteId}/versions` | `space:model:read` |
| POST | `/api/space/design/v1/sites/{siteId}/versions` | `space:model:edit` |
| GET | `/api/space/design/v1/versions/{versionId}` | `space:model:read` |
| GET | `/api/space/design/v1/versions/{versionId}/sources` | `space:model:read` |
| POST | `/api/space/design/v1/versions/{versionId}/sources` | `space:source:upload` + `space:model:edit` |
| GET | `/api/space/design/v1/jobs/{jobId}` | `space:model:read` |
| GET | `/api/space/design/v1/versions/{versionId}/issues` | `space:model:read` |

版本创建接入 E01-S04 Published→Draft Clone 协调器，返回 `202`、Version、
Job 和轮询 URL。来源创建仅接受 E01-S02 已处于 `Clean` 状态的文件并返回
`201`。文件上传会话、扫描执行、parse、cancel、retry 等命令不属于 S05。

## 2. 契约与错误边界

- 权威契约为 `docs/space/contracts/design-v1.openapi.json`，只包含 6 条路径、
  8 个操作，不包含 Legacy、Scene、Asset、Publish、Planning 或 WMS 路由。
- 所有操作使用稳定 `operationId`；写操作要求非空 JSON body 和
  `Idempotency-Key`，响应暴露 `Idempotent-Replay`。
- Design API 的 400、401、403、404、409、422、500 使用
  `application/problem+json`，并包含 `code`、`traceId`、
  `correlationId` 和 `recovery`。
- 认证挑战、模型绑定、权限拒绝、领域异常和未处理异常进入同一 Design
  Problem Details 边界；非 Design 的 Legacy Space API 保持既有行为。

## 3. 租户、权限、开关与分页

- `SpaceContext` Tenant query filter 保护 Model、Version、Source、Job、Issue
  和幂等记录；跨租户资源按 404 处理。
- 现有 `SpaceExecutionContextMiddleware` 在进入 Design Controller 前校验唯一
  Tenant、Actor，并拒绝 external subject。
- HTTP 权限由 `RequirePermission` 强制执行；种子只新增并仅向管理员默认授权
  `model:read`、`model:edit`、`source:upload`。
- API 同时要求全局 `Space:Compatibility:DesignApiEnabled=true`、Site 已验证的
  DesignV1 cutover，以及 `Space_Model` 已进入 DesignV1。任一条件不满足返回
  `SPACE_DESIGN_API_DISABLED`，Legacy API 不受影响。
- 不透明 cursor 由 ASP.NET Data Protection 保护，并绑定 Tenant、Actor、
  Organization、`space_grant_version`、资源类型和过滤条件，15 分钟过期。

## 4. 幂等与数据库变更

- `Idempotency-Key` 接受 1–128 UTF-8 bytes，拒绝空值和控制字符。
- 持久化键使用 SHA-256，并按 Tenant、Principal、Operation、Key hash 唯一。
- 同 key、同规范化 body 在 24 小时内重放原结果；同 key、不同 body 返回
  `409 SPACE_IDEMPOTENCY_KEY_REUSED`。
- 记录保留 90 天，并提供 Tenant + RetainUntilUtc 索引供后续维护任务清理。
- 版本创建复用确定性 Clone operation ID；来源与幂等结果在 Serializable
  transaction 内提交。
- Migration 只新增 `Space_IdempotencyRecord` 及两个索引；Migration、Designer
  和 SQL 的模式内容与原 S05 冻结制品一致，SQL 仅规范化了末尾空行，没有修改
  Legacy 表。

## 5. OpenAPI 与 SDK

可重复生成入口：

```powershell
./tools/generate-space-design-sdk.ps1
./tools/generate-space-design-sdk.ps1 -Check
```

输出：

- `docs/space/contracts/design-v1.openapi.json`
- `CP6.Space.Client/SpaceDesignV1Client.g.cs`
- `sdk/typescript/space-design-v1/spaceDesignV1Client.ts`

生成器从 Design Controller 和 Swagger 配置直接构建契约，不启动业务 WebApi，
也不连接数据库；生成后统一移除行尾空格并固定 UTF-8/LF。`-Check` 会在受控
临时目录重生成并比较 SHA-256。

## 6. 验证证据

| 验证层 | 结果 |
|---|---:|
| Space UnitTests | 44 passed |
| Space IntegrationTests | 9 passed / 24 SQL-gated skipped |
| Design API / OpenAPI / 权限聚焦测试 | 17 passed |
| CP6.Tests 全量 | 2674 passed / 17 environment-gated skipped |
| 全解决方案 Release build | succeeded，0 errors |
| EF model check | no pending model changes |
| SDK drift check | passed |
| C# SDK build | passed |
| TypeScript SDK strict compile | passed |

24 个 Space SQL 门禁中包含本卡新增的 Migration/唯一键测试。当前自动化身份仍因
TLS/SSPI/Guest 认证问题无法进入本机 SQL Server，因此这些测试只记作 skipped，
不记作 passed。S05 没有修改 `cp6.web` 产品代码，未重复运行前端应用测试。

## 7. 后续边界

- E01-S06 继续实现 Quarantine→Scan→Safe/Rejected、引用感知删除和到期清理。
- `RetainUntilUtc` 到期后的物理清理由统一后台维护任务执行；S05 不在请求线程
  批量删除历史幂等记录。
- S05 不新增 Scene、Asset、Validation、Publish、Historical Republish、
  WMS Adoption、Planning 或 Operations API。
- 本卡不修改 Legacy response envelope、Legacy 路由或 Legacy 数据表。
