# Space Tenant 私有整仓模板交付报告

日期：2026-08-16
范围：LM-FR-001 / WP1 的 Tenant 私有整仓模板持久化、目录、跨租户隔离、Preview 与逐层 Apply

## 结论

Tenant 私有整仓模板不再是空目录占位。Design V1 现在可持久保存当前租户的一份类型化仓库计划，生成不可变 v1 和内容 SHA-256，并通过与 System 模板相同的目录、密封 Preview 和逐层 Apply 主链消费。另一个租户无法读取或应用该模板；System 模板也不能通过租户写接口伪造或覆盖。

这关闭的是“模板持久化与消费”纵切，不是 LM-FR-001 的全部完成声明。当前创建合同面向受信任的内部管理客户端；面向仓库人员的模板制作表单、Blank/Published/System/Tenant 四模式统一 Draft 创建向导和 Template 创建来源持久化仍须由后续独立任务交付。核心 GA 因此继续为 72% / `NoGo`。

## 权威模型与约束

- `Space_WarehouseTemplate` 保存当前租户的模板编码、名称、说明、当前版本与审计；`(TenantId, NormalizedTemplateCode)` 在活动行中唯一。
- `Space_WarehouseTemplateVersion` 保存 append-only 计划 JSON、Schema Version、内容 SHA-256 及 Floor/Zone/Aisle/Rack/Location 计数。
- 模板头和版本使用 `(TenantId, Id)` / `(TenantId, TemplateId)` 复合外键，查询过滤只返回当前租户，`SpaceContext` 的同步与异步保存路径都拒绝修改或删除模板版本。
- Tenant 创建接口只产生 `scope=Tenant`；System 模板仍由确定性代码目录提供，没有租户可调用的 System 写入口。
- 计划在服务器规范化并校验：1–20 个楼层、唯一 Key 和分层编码、完整 Floor→Zone→Aisle→Rack 父链、正尺寸与可整除层格、可安全生成的巷道范围、每层最多 300 条布局命令、总计最多 100,000 个库位。
- 内容哈希只覆盖规范计划；Proposal Hash 绑定 Template ID、Version ID 和 Content Hash。数据库内容损坏或计数不匹配时 Preview 失败关闭。

## Design V1 与工作台

- `POST /api/space/design/v1/templates` 要求 `space:model:edit`、请求体和 `Idempotency-Key`；相同键/相同输入返回同一模板，变更输入返回稳定幂等冲突。
- `GET /api/space/design/v1/templates?scope=System|Tenant` 合并代码内置 System 与当前租户持久模板。
- 既有 `POST .../templates/{templateId}/instantiate` 同时为 System/Tenant 返回 `writesDraft=false` 的密封 Preview。
- 既有 `POST .../templates/{templateId}:apply` 从服务端模板内容生成 Zone/Aisle/Rack/RackLevel/Location，继续绑定 Site、编辑租约、Floor Revision、Content Revision、Proposal Hash 和幂等 CommandBatch。
- Space Studio 工作台读取合并目录并显示“系统/租户私有”；选择改变后，只有与该模板 ID 一致的 Preview 才显示和允许 Apply。
- OpenAPI、C# SDK、TypeScript SDK 与 Web API wrapper 已同步；新增契约的 body、字段和幂等头均为机器可验证。

## 自动化证据

- Space Unit 聚焦：3/3 passed，覆盖规范化/密封/重读、命令生成、错误父链/重复 Key、领域元数据与 Hash 验证。
- SQL Server LocalDB 聚焦：2/2 passed、0 skipped；新增场景覆盖创建、幂等重放、幂等输入冲突、同租户同码冲突、版本不可变、目录/Preview、租约 Apply、跨租户不可见与不同租户同码可创建，并复跑 System 模板 Apply。
- OpenAPI 与权限聚焦：96/96 passed；创建操作 ID、必填 body/字段、必填 `Idempotency-Key`、`Idempotent-Replay` 和权限均锁定。
- Web 聚焦：15/15 passed；覆盖 API 创建包装、目录/Preview、System/Tenant 标签、跨模板旧 Preview 隐藏与现有启动页。
- 全量回归：Space Unit 549/549、CP6.Tests 2,934 passed / 19 项既有环境门禁 skipped、Space Integration 在标准 LocalDB 连接上 456/456 passed / 0 skipped、Web 884/884、Space Studio Playwright 26/26。
- `dotnet ef migrations has-pending-model-changes`：clean。
- `tools/generate-space-design-sdk.ps1 -Check`：passed。
- Vue TypeScript 与生产 Vite build：passed。
- .NET 领域、基础设施、WebApi、Unit、Integration 与契约测试项目：Release build 0 warning / 0 error。

## 剩余边界

1. 增加仓库人员可用的“从当前 Draft 保存/制作 Tenant 模板”交互；本纵切不从任意 Draft 几何静默反推受限模板计划。
2. 将 Blank、PublishedVersion、System Template、Tenant Template 收敛为同一创建向导，并持久化准确的 Template 创建来源。
3. 真实 Provider、授权黄金 CAD、生产等价 WMS/SQL、双仓 Pilot 与五方签字仍按 GA 证据索引执行，不能由本地模板自动化替代。
