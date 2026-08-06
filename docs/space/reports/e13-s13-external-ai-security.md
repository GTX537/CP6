# E13-S13 外部用户拒绝与数据外发门禁

- 状态：已实现并进入受控集成
- 基线：`d2a96be4704dae2fc3d9c738e56e58c07eb1072c`
- 分支：`codex/space-e13-s13-external-ai-security`
- 功能/集成提交：`37bf5c37` / `e1682efc`
- 日期：2026-08-06

## 1. 交付结论

E13-S13 已把客户、供应商和 3PL 的 AI 拒绝从各 HTTP 服务继续下沉到 Provider Gateway，并在真正的 External Provider 调用前新增严格出站白名单。现有 4 个 AI 控制器共 16 个端点均有显式审计元数据；7 个敏感读取端点全部启用读审计。外部主体在读取策略、用量、Run、提案、问题、决策或执行 Apply、取消、重试、废弃、对账、恢复和决策前统一收到：

- HTTP `403`
- `SPACE_EXTERNAL_SUBJECT_DENIED`

外部 Provider 收到的对象必须同时满足冻结字段白名单和最小化 Token 格式；否则返回：

- HTTP `403`
- `SPACE_AI_OUTBOUND_PAYLOAD_DENIED`
- Provider、配额和预算实现均不会被调用

## 2. 已有边界盘点

实施前已经存在：

- AI 策略、用量、Run、提案、Apply、恢复服务层的 `IsExternal` 拒绝。
- CAD IR 最小化器对 Tenant/Site/Model/Run/File/Source/Handle、图层、块和属性键做 HMAC Token 化。
- 可逆 Source Map 只保存在本地，不进入 Provider 输入。
- Provider 输入类型没有密钥、URL、端点、原文件或最终业务 LogicalId。
- 原始 CAD、Published 和 Draft 均不允许由 Provider 直接访问或修改。
- 生产 BuildScene executor、External Provider 注册和默认配额仍失败关闭。

实施前缺口：

- `SpaceAiGenerationGateway` 没有在策略查询前重新拒绝外部执行上下文。
- External Provider 调用前没有运行时检查“冻结 JSON 字段 + 最小化 Token 格式”；只能依赖上游正确调用最小化器。
- `GET ai-policy` 与 `GET ai-usage` 没有显式 `AuditRead` 标记。
- 既有外部拒绝测试只覆盖少数单点，没有 Customer/Supplier/3PL × 16 操作矩阵。

## 3. 实现

### 3.1 Provider Gateway 内部主体门禁

`SpaceAiGenerationGateway.GenerateAsync` 首先校验：

1. `IsExternal == false`。
2. `TenantId` 与 `ActorId` 均有效。
3. 之后才允许读取租户 AI 策略、解析 Provider、申请配额或调用 Provider。

这使未来 BuildScene 接线不能只依赖控制器或调用方自觉检查。

### 3.2 External Provider 出站白名单

新增 `SpaceAiExternalProviderRequestGate`，仅在 `WarehouseGenerationProviderKind.External` 下执行：

- 精确冻结 `WarehouseGenerationInput`、Limits、Feature、Bounds、Mapping Hint 和 Locked Fact 的 JSON 属性集合；未来新增可序列化字段会默认失败关闭。
- `run-` 使用 64 位小写十六进制 HMAC。
- `source-/group-` 使用 32 位小写十六进制 HMAC。
- `layer-/block-` 只允许最小化器安全语义词表和 24 位 HMAC。
- `repeat-/attribute-/hint-` 只允许固定前缀和 24 位 HMAC。
- Locked Fact 只允许冻结字段路径和对应枚举值。
- 任一原始客户名、任意 Prompt 文本、路径、URL 或非最小化 Token 都在申请配额前被拒绝。

Mock 和 Local Provider 仍共享原 SPI，不被网络外发专用格式检查误伤。

### 3.3 审计

`GET ai-policy` 与 `GET ai-usage` 增加 `SpaceAuditOperation` 且 `AuditRead = true`。至此 16 个 AI HTTP 端点均有显式审计动作和权限码，7 个 GET 均记录允许、拒绝或失败结果；审计正文仍由既有安全错误分类器生成，不写请求正文或 Provider Payload。

### 3.4 外部角色矩阵

新增 Customer、Supplier、3PL 三类外部上下文，每类执行 16 个服务操作，共 48 条拒绝断言。每条同时验证稳定错误码和 HTTP 状态，并确认没有 Run、Proposal、Policy、Usage、Job 或 Idempotency 写入。

## 4. 验证证据

| 验证 | 结果 |
|---|---|
| `CP6.Space.Application` Debug build | 通过，0 warning / 0 error |
| `CP6.Space.Application` Release build | 通过，0 warning / 0 error |
| `CP6.WebApi` Release build | 通过；仅有 3 个既存且与本卡无关的 Core nullable warning |
| `CP6.Space.UnitTests` 全量 | 424/424，通过，0 skipped |
| Provider/最小化定向单元 | 34/34，通过；包含真实 CAD 最小化器输出进入 External Provider 门禁 |
| 外部主体与 AI 管理定向集成 | 8/8，通过 |
| AI 审计、OpenAPI、权限与 Problem Details 契约 | 87/87，通过 |
| AI 非 SQL 管理/注册/外部矩阵 | 10/10，通过；3 个 SQL 用例在无连接变量运行中按设计跳过 |
| KOUSQLSERVER：Apply/恢复/配额/外部矩阵 | 21/21，通过，0 skipped |
| `git diff --check` | 通过 |

真实 SQL 使用本机 Windows 集成认证连接，仅在进程环境变量中提供；报告和仓库不保存凭据或连接串。

## 5. 安全审计说明

本卡先按 CSO 日常模式做只读范围审计。两个候选点经独立复核均以 9/10 以上置信度判定为“当前不可利用”，因为生产没有 Gateway 调用方、External Provider 注册为空且配额失败关闭。因此本地 `.gstack/security-reports` 报告为 0 个当前漏洞；本卡实现的是连接真实 Provider 前必须完成的发布安全封口，而不是声称修复了一个已在线可利用漏洞。

## 6. 未完成与诚实边界

- 生产 BuildScene executor 仍失败关闭，尚无真实外部 Provider 适配器，因此不能声称已经完成真实网络端到端外发验证。
- 本卡没有改变 Provider 输入 v1 的公开 JSON Schema；新增门禁是 External Provider 运行时的更严格发送条件。
- 没有数据库模型或迁移变化。
- 没有前端源代码变化；外部 Portal 与内部 AI UI 仍由既有路由和权限边界隔离。
- 正式 Prompt 注入、供应商区域/保留/DPA、20 份授权黄金集、影子运行和试点证据仍属于 E13-S14/S15/S19。
- AI 权限不足时，通用权限过滤器仍可先返回 `SPACE_PERMISSION_DENIED/403`；一旦请求进入 AI 服务，外部上下文稳定返回 `SPACE_EXTERNAL_SUBJECT_DENIED/403`，两者都不泄露 Run/Proposal/费用存在性。

本结果不是专业渗透测试的替代品。它是 AI 辅助的范围安全检查，可能遗漏复杂授权链中的细微问题；处理敏感生产数据时仍应安排合格的独立安全测试。
