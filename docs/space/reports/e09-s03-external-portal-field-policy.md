# E09-S03 外部只读 Portal 与字段策略交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e09-s03-external-portal`
- 起始基线：`13c7b9dae41f426c3aa225ae5a7d0f2e87a94af0`
- 功能提交：`88bc42d19b48c83279597fc426b8a8a24028d270`
- no-ff 集成提交：`1850b2d8a606197f8749cebc1b19912d42c1301b`
- 数据库迁移：`20260801191107_SpaceE09S03ExternalPortal`

## 1. 交付结果

E09-S03 已把 E09-S02 的 Organization、Membership、Grant 和统一访问求值器接入独立的外部只读 Portal。外部主体只允许进入 `/api/space/portal/v1`，只允许 `GET/HEAD`；除组织选择端点外，每次请求必须携带且只携带一个非空 GUID Organization Context。内部主体、未知显式主体类型、缺失或歧义组织上下文，以及外部主体访问其他 `/api/space` 路径均失败关闭。

Portal 只读取当前 Published/Active 模型，不暴露 Draft、Revision、来源文件、映射、问题、发布或重试接口。Grant 负责行和空间范围，字段策略负责显式字段 allowlist、脱敏和导出能力；未知字段默认不可见。多个完整 Grant 继续按 OR 匹配，同一 Grant 内各维度按 AND 匹配；当同一合法数据行被多个 Grant 命中时，字段采用最少限制的合法掩码，但不会让其他资源的 Grant 参与当前资源求值。

结构性标识保持为稳定、只读 DTO 的必要字段，业务值字段只有在策略显式允许时才返回。场景的 Zone-only 授权不会泄露上级 Floor 的业务字段；库存和任务的 Location/Floor/Zone 标识取自数据库中的权威 Published 候选位置，而不是可被运行源响应影响的身份字段。

## 2. 字段策略与管理 API

新增租户权威表：

- `Space_FieldPolicy`
- `Space_FieldPolicyField`

策略支持 PublishedScene、Stock、Task 三类资源，字段掩码为 None、Partial、Hash、Redact，包含 AudienceType、`CanExport`、Version、rowversion 和审计字段。Active 策略可以退休，Retired 为终态。数据库使用复合租户外键、状态/资源/掩码检查、过滤唯一索引和全局租户/软删除过滤失败关闭。

新增内部管理端点：

- `GET/POST /api/space/field-policy`
- `GET/PUT /api/space/field-policy/{policyId}`

读取需要 `space:external:read`，变更需要 `space:external:manage`。Grant 关联字段策略后，Portal 只采纳 Active、受众匹配且包含当前资源显式字段规则的策略。导出能力同时要求命中的 Grant 和字段策略均启用 `CanExport`；本卡未开放独立导出端点。

## 3. Portal 合同

新增只读端点：

- `GET /api/space/portal/v1/organizations`
- `GET /api/space/portal/v1/sites`
- `GET /api/space/portal/v1/sites/{siteId}/published-scene`
- `GET /api/space/portal/v1/sites/{siteId}/stock`
- `GET /api/space/portal/v1/sites/{siteId}/tasks`

组织列表用于选择当前可用 Organization Context；其余端点要求单一组织声明并重新验证有效 Organization、Membership、Grant 和字段策略。Published 场景 DTO 不包含 `revisionId`、`sourceId`、`sourceRef`、`rowVersion`、`contentHash`、元素属性原文或底图来源标识等内部字段。库存和任务响应继续沿用 E08 的真实运行源语义，同时在后端执行同一范围裁剪和字段策略。

OpenAPI 从 29 paths / 38 operations 增至 36 paths / 47 operations，并重新生成 C# 与 TypeScript SDK。新增客户端操作为 `GetFieldPolicies`、`GetFieldPolicy`、`CreateFieldPolicy`、`UpdateFieldPolicy`、`GetPortalOrganizations`、`GetPortalSites`、`GetPortalPublishedScene`、`GetPortalStock` 和 `GetPortalTasks`。

交付工件 SHA-256：

- OpenAPI：`BCFFEF09C5454575DE6D5AA0753C5595D26B8D5FF64A5996A8E17B09F9D6DAF2`
- C# SDK：`C7BCC22299A18644C21C9BDA93A0C1EC4F46EA1B6CE987E66D7532A66D786C4B`
- TypeScript SDK：`5F5132E77FEA82C447A36A8AFF6542F3CDD1C7961C0317D60BEF5C74162F9ABF`

## 4. 安全复核修正

实现和测试阶段额外关闭了五类权限升级路径：

1. 先按 Site 和当前资源裁剪候选 Grant，禁止其他资源 Grant 参与 Portal 匹配。
2. 字段规则必须显式存在；缺失规则不再因枚举默认值被误判为无掩码。
3. Zone-only 场景授权不返回父 Floor 业务值，只保留定位所需结构标识。
4. 运行源响应不能决定授权或结构身份，库存/任务输出使用数据库权威位置标识。
5. 未知显式主体类型不再回退为内部主体，而是直接拒绝。

应用层与领域层同名资源枚举使用显式映射，避免依赖数值强制转换形成未来授权错配。E09-S02 未关联字段策略的 Grant 在通用访问求值器中保持兼容，但不会绕过 Portal 对有效字段策略的强制要求。

## 5. 数据库与部署证据

真实 SQL Server 聚焦测试验证字段策略的租户外键、字段唯一性和检查约束。S02→S03 幂等增量脚本显式设置过滤索引需要的 ANSI、ARITHABORT、QUOTED_IDENTIFIER 和 NUMERIC_ROUNDABORT 选项；临时数据库先迁移到 S02，再连续执行增量脚本两次，最终只存在 2 张字段策略表和一条 S03 迁移历史记录。验证完成后临时数据库与临时 SQL 文件均已清理。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 231 passed / 0 failed / 0 skipped |
| Space Integration + KOUSQLSERVER 全量 | 181 passed / 0 failed / 0 skipped |
| CP6.Tests 全量 | 2711 passed / 0 failed / 17 既有环境门禁 skipped |
| 完整 `CP6.slnx` 构建 | 0 error；合并态 10 条既有可空性/测试分析 warning |
| 字段策略真实 SQL 聚焦 | 2 passed / 0 skipped |
| S02→S03 幂等增量 SQL | 首次执行与重复执行均 passed |
| EF 模型漂移 | 无待生成模型变化 |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| C# / TypeScript SDK 编译 | passed |
| 前端类型检查 | passed |
| 前端全量 | 106 files / 607 tests passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 暂存差异 whitespace | passed |

no-ff 合并后又在 `1850b2d8` 上复验：完整 solution 构建 0 error、字段策略领域 3/3、Portal/字段策略/Grant/访问求值器含真实 SQL 22/22、权限/OpenAPI/中间件/ProblemDetails 84/84、EF 无模型漂移、SDK `-Check`、TypeScript strict no-emit、前端 type-check、106 files / 607 tests 和生产构建全部通过。

## 7. 后续范围

下一张建议卡为 E09-S04：建立跨租户越权自动化矩阵，覆盖猜测 Organization/Site/Location/Object ID、客户/供应商/3PL 同码、分页/游标、缓存键、Portal 场景/库存/任务以及字段策略组合，任一串租户结果均阻断发布。E09-S05 随后补齐登录、查看、导出、授权变化和有效期失效的外部访问审计。独立导出端点、Portal 前端体验和平台支持人员临时授权不在本卡范围内。
