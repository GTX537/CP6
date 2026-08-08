# E03-S04 服务端权威 Excel–CAD Match Artifact 开发报告

日期：2026-08-08
基线：`27d3989b`（`integration/space-v1-20260730`）
功能提交：`4db2d0d0`
验证报告提交：`93f65a33`
no-ff 集成提交：`3ee23655`

## 交付结论

E03-S04 已从离线只读匹配预览补齐为服务端权威、可持久化、可审计的 Match Artifact 链。HTTP 请求只冻结来源并排队；后台 Job 重新读取私有 Excel、已完成的 CAD PreviewSet、服务端映射方案及当前 Draft 数据库快照，校验全部 Tenant、Site、ModelVersion、Job 血缘、Schema、哈希和 ContentRevision 后，生成确定性的私有 `ExcelCadMatchPreview` Artifact。

浏览器只能提交权威来源 ID，不能提交匹配结果、编辑器快照、Definition Hash 或 Artifact Hash。结果读取要求 `space:model:read`，启动要求 `space:model:edit`，写入和读取均进入审计；外部 Customer、Supplier、3PL 主体在数据访问前失败关闭。

## 本次实现

1. 新增 `SpaceCadPreviewSetV1` 与 `SpaceExcelCadMatchArtifactV1` 合同，固定 Excel Preflight Job、CAD Parse Job、CAD PreviewSet Artifact、映射方案版本/Definition SHA、Draft ContentRevision、行级证据和最终 Artifact SHA。
2. 新增 `ExcelCadMatch` Job 与 `ExcelCadMatchPreview` Artifact 类型，沿用现有 Job Ledger、Attempt、Step、租约、超时、重试和私有文件存储，不引入新的数据库表或 Migration。
3. 启动服务在事务中核验 Draft、Excel、Preflight Job、CAD Job、映射方案和幂等键；同一合法请求稳定复用 Job，来源或修订漂移返回稳定冲突，不会创建第二条执行链。
4. Worker 不信任排队时的客户端投影。它重新打开 Excel，验证 Preflight/CAD Job Payload、PreviewSet 文件与哈希，直接从数据库读取 Floor、Zone、Rack 权威快照，生成并持久化 Match Artifact；重试时只复用通过完整校验的唯一 Artifact，重复或歧义 Artifact 失败关闭。
5. 新增创建与查询 API：
   - `POST /api/space/design/v1/versions/{versionId}/excel-cad-matches`
   - `GET /api/space/design/v1/versions/{versionId}/excel-cad-matches/{jobId}`
6. 查询支持 disposition、货架码、SourceRef、可定位状态和受保护游标分页。若当前 Draft ContentRevision 已变化，历史结果仍可只读审阅，但 `CanConfirm=false`，不能进入后续写入链。
7. 编辑器新增“Excel–CAD 权威匹配”审阅面板，可查看统计、筛选/分页、定位到画布，并显示来源、哈希、修订漂移和失败关闭原因；CAD、AI 与 Match 三类审阅面板互斥，路由中的 `matchJobId` 变化会重新加载权威结果。
8. OpenAPI 操作数由 111 增至 113，C# 与 TypeScript SDK 同步生成并通过漂移检查。

## 信任边界与故障语义

- 只有服务端数据库、私有文件存储及已完成 Job/Artifact 是事实来源；前端仅负责发起和审阅。
- Excel 或 CAD 大文件不在 HTTP 请求内解析；Match Job 超时为 30 分钟，并沿用现有可恢复 Job 状态机。
- Tenant、Site、ModelVersion、Job 直接父子链、映射方案、Schema、SHA-256、Draft ContentRevision 任一不一致均失败关闭。
- 当前 CAD Job 的 PreviewSet 优先；仅允许直接父 Job 的兼容回退。当前或父级出现多个候选 Artifact 时拒绝猜测。
- 本卡只产出只读权威匹配证据，不创建 LogicalId，不修改 Floor/Zone/Rack，不提升 ContentRevision，也不调用 WMS。

## 验证证据

- Space Unit：464 passed / 0 failed / 0 skipped；
- 默认 Space Integration：267 passed / 0 failed / 94 SQL-environment-gated skipped；其中 E03-S04 服务与 Worker 聚焦 4/4；
- CP6.Tests：2806 passed / 0 failed / 17 environment-gated skipped；其中 E03-S04 API 聚焦 2/2；
- 前端：132 files / 702 tests passed，TypeScript 类型检查和 production build 通过；
- 完整 `CP6.slnx` Release 非增量单线程构建：0 error / 10 条既有 warning，含 Desktop 与 Android AOT；
- OpenAPI/C#/TypeScript SDK generation drift、受影响 C# whitespace、`git diff --check` 全部通过。
- 集成并推送后清理 39 个可重建 `bin/obj/node_modules/dist` 目录、32,452 个文件、2,475,932,206 bytes（约 2.306 GiB）；源码与 Git 历史不受影响。

## 尚未解除的外部边界

本卡完成的是应用内权威匹配链，不等于生产 CAD 正式签收。生产默认 CAD Provider 仍失败关闭；组织授权的真实 DWG/DXF 黄金集、真实大文件/异常/性能证据以及生产部署中的 Excel/CAD Worker 仍需外部环境提供。没有 CAD 软件并不阻塞本卡开发，也不授权使用来源不明的图纸。

下一张 E03-S05 只能消费本卡持久化的权威 Match Artifact，并要求显式用户确认、精确 ExpectedContentRevision、服务端再次校验、整批原子写入和幂等重放。任何 Conflict、Error、Unmatched、低可信候选、Artifact/修订漂移都不得写入 Draft。
