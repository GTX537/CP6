# E06-S01 版本权威校验引擎开发报告

日期：2026-08-07
状态：已 no-ff 集成
集成基线：`022cb9376eec393cf9d1901c4e650092f7fd165b`
功能分支：`codex/space-e06-s01-validation-engine-v2`
功能提交：`c17242c3`
no-ff 集成提交：`76c70230`

## 1. 本卡边界

本切片只交付 E06-S01：对服务端权威仓库版本快照执行统一校验，并形成可追溯的
ValidationRun、Issue、Job 和版本状态闭环。它不包含 E06-S02～S06 的版本差异、影响预览、
发布、WMS 激活、回滚或 Beta/GA 签收，也不把 E03-S04 的离线匹配预览冒充为可写入 Draft 的
权威 Match Artifact。

## 2. 权威输入与状态机

- 校验只从 `SpaceContext` 读取 ModelVersion、Floor、Zone、Aisle、Rack、RackLevel、Location、
  Element、Source、Asset、已发布 Location 身份和未关闭的非校验 Issue；客户端不能提交校验 JSON。
- 每次运行冻结 Tenant、ModelVersion、ContentRevision、确定性 ContentHash、RuleSetVersion、
  WMS AdapterId、CapabilityHash、JobId、CorrelationId 和请求人/时间。
- 同一权威输入使用 SQL Server transaction-owned `sp_getapplock` 和唯一索引串行化；相同内容、
  规则集、适配器和能力哈希复用同一非失败运行与 Job。正在校验的不同输入返回 409。
- 只允许 Draft、Ready 或 Validating 进入新建/复用流程；Published、Publishing、Superseded 等状态
  在复用查询前失败关闭，避免旧结果隐式改写发布态。
- Job Processor 重新读取权威快照和当前 WMS 能力；revision、content hash、规则或能力漂移时将
  ValidationRun 标为 Failed，并把仍处于 Validating 的版本安全返回 Draft。
- 无 Blocking Issue 时运行 Passed、版本 Ready；存在 Blocking Issue 时运行 Blocked、版本 Draft。
  所有校验 Issue 绑定 ValidationRunId，并保留 Category、FieldPath、EvidenceJson 和既有 AI/生成血缘。

## 3. 统一规则集

`space-validation-rules-v1` 覆盖：

1. 模型对象上限、跨类型 LogicalId 唯一性；
2. 必填编码、重复编码、WMS 编码正则和长度；
3. Source 血缘、Ready/PreviewReady/Imported 状态、SHA-256、DWG/DXF 单位与正比例尺；
4. Floor/Zone 多边形、层级归属、Rack 边界与碰撞；
5. RackLevel/Location 完整性与槽位唯一性；
6. Element 几何 JSON、资产绑定、内部引用；
7. 已发布 Location 逻辑身份与编码冻结；
8. 既有 Blocking/Warning、AI Provenance 和模型问题归并。

规则输出使用 Coding、Geometry、Hierarchy、Binding、Source、AiProvenance、Model 等稳定类别与问题码，
不把 Warning 误当 Blocking，也不丢失原始证据。

## 4. API、权限、审计与契约

- `POST /api/space/design/v1/versions/{versionId}/validations`
  - 权限：`space:model:validate`
  - 审计：`space.validation.start`
  - 返回：202，包含 ValidationRun、Job 和是否复用。
- `GET /api/space/design/v1/validations/{validationId}`
  - 权限：`space:model:read`
  - 读审计：`space.validation.read`
  - 只返回当前 Tenant 可见的运行及其绑定 Issue。
- 两个端点均声明标准 Problem Details；OpenAPI、C# SDK 和 TypeScript SDK 已同步。
- SDK 再生成同时发现 E02-S08 CAD Controller 缺失显式 500 Problem Details 声明，本切片补齐该契约漂移；
  未改变 CAD 业务行为。

## 5. 持久化与迁移

- 新增 `Space_ValidationRun`，保存冻结输入、状态、计数、错误、Job、请求和完成时间。
- `Space_ModelIssue` 新增 `ValidationRunId`、`Category`、`FieldPath`、`EvidenceJson`，并建立租户复合外键与索引。
- Migration：`20260807105256_SpaceE06S01ValidationEngine`。
- 幂等脚本：
  `CP6.Space.Infrastructure/Migrations/Scripts/20260807105256_SpaceE06S01ValidationEngine.sql`。
- `Down` 使用 `THROW 51018` 禁止破坏校验审计证据；修复必须通过更高版本的 forward-fix Migration。
- 幂等脚本在临时 SQL Server 数据库上从上一迁移升级并连续执行两次，最终
  ValidationTables=1、ValidationColumns=4、MigrationRows=1；临时库已删除。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| 校验引擎聚焦 | 9/9 passed |
| Controller、权限、审计、OpenAPI 聚焦 | 74/74 passed |
| Space Unit 全量 | 440/440 passed |
| CP6.Tests 全量 | 2793 passed / 17 environment-gated skipped / 0 failed |
| 默认 Space Integration 全量 | 259 passed / 86 SQL-gated skipped / 0 failed |
| E06-S01 真实 SQL：Passed/Blocked/并发复用/发布态拒绝 | 3/3 passed |
| 完整 `CP6.slnx` Release 构建 | 0 warning / 0 error |
| EF pending model changes | none |
| OpenAPI/C#/TypeScript SDK drift | passed |
| E06-S01 幂等增量 SQL 双执行 | passed |
| `git diff --check` | passed |

本机 `KOUSQLSERVER` 的 Windows 集成身份在最终复验时出现 SSPI 环境错误；测试改用同机健康的
`cp6-db` SQL Server 容器，并使用容器自身的开发凭据完成真实 SQL 门禁。密码未写入日志、源码或文档。

验证及合并后删除受控工作树内 36 个可重建 `bin/obj` 目录，共 7,159 个文件、
1,974,302,339 bytes（约 1.839 GiB）；源码、Migration、SDK、报告和 Git 历史均保留。

## 7. 尚未完成与下一步

1. 生产环境仍缺 Hosted Space Worker 自动持续领取 Job；当前仓库只具备通用 Job Ledger 与 Processor。
2. E06-S02～S06 尚未实现；下一张可独立推进 E06-S02 版本差异与影响预览 API。
3. E03-S05 必须等待服务端权威 E03-S04 Match Artifact、持久化、API、权限和审计，不能消费客户端离线预览直接写 Draft。
4. E02/CAD 正式签收仍需获授权的原生 DWG/DXF Provider、组织有权使用的黄金集、生产 Worker 和真实大文件/故障/性能证据。
5. 本卡不是完整 E06、Beta 或 GA 发布证据。

`DefaultSpaceValidationProfileProvider` 在真正请求时延迟解析 WMS Adapter，以冻结实时能力哈希，同时保持
`AddSpaceDesignV1Persistence` 可在没有宿主 `CP6Context` 的纯持久化组合测试中枚举 Processor；生产请求仍
必须解析真实 Adapter，缺失时失败关闭。
