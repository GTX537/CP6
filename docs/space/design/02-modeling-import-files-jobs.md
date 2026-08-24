# CP6 Space 详细设计卷二：CAD/Excel、文件安全与后台任务

版本：v1.1  
日期：2026-07-25  
状态：详细设计已评审，可进入 CAD 技术试验与文件链路实现  
覆盖决策：D2、D7、D8、D10、D12、D13

关联入口：

- [低成本 3D 建模 Spec](../requirements/04-low-cost-3d-modeling-spec.md)
- [AI 自动生成完整仓库 Spec](../requirements/05-ai-warehouse-generation-spec.md)
- [卷六：AI 生成、审查与来源追踪](./06-ai-generation-review-provenance.md)
- [卷三：编辑器与 2D/3D 同源](./03-editor-elements-rendering.md)
- [当前 AttachmentService](../../../CP6.Core/Services/Pub/AttachmentService.cs)
- [现有 Job 租约参考](../../../CP6.Core/Services/Wf/WfServiceJobService.cs)

## 1. 本卷结论

低成本 3D 建模提供三条入口，但只形成一种设计态命令：

1. `DWG/DXF + Excel`
2. `PDF/PNG/JPG 底图 + 地图编辑器`
3. `空白画布 + 参数化模板`

文件不会由 WebApi 进程直接解析。所有上传先进入隔离区，经流式限额、类型识别、恶意内容扫描后，才允许独立 Worker 读取。CAD、Excel、校验和发布都由数据库 Job Ledger 驱动；消息队列只能唤醒 Worker，不能成为任务事实源。

结构化 CAD 的 AI 语义补全复用本卷的安全文件、CAD IR、Artifact 和 Job Ledger，不改变文件边界。`Import` 处理器完成扫描、转换、IR 和规则识别；`BuildScene` 处理器完成最小化特征、Provider 调用、确定性融合和提案持久化。AI 只产提案，人工审查和原子 Draft Apply 的完整协议见卷六。

```mermaid
flowchart LR
    U["上传"] --> Q["隔离区"]
    Q --> S["安全扫描"]
    S --> J["Job Ledger"]
    J --> W["隔离 Worker"]
    W --> P["预览集"]
    P --> C["用户确认"]
    C --> D["设计版本命令"]
```

## 2. 当前实现与差距

| 当前能力 | 可复用 | 必须补齐 |
|---|---|---|
| `Pub_Attachment` 与 `IFileStore` | 存储抽象和业务附件概念 | 当前整文件读入内存、MD5、扩展名校验，不适合 CAD |
| Floor 底图字段 | 运行态兼容展示 | 文件来源、页码、标定证据、版本化 |
| Scene JSON 导入导出 | CP6 自有场景交换 | 不是 CAD/Excel 解析 |
| Wf ServiceJob 租约模式 | RowVersion 抢占、租约、退避思路 | Space 需独立 Job Ledger、步骤和 Artifact |
| 编辑器生成命令 | 模板和阵列可复用 | CAD/Excel 必须输出同类命令，不得直写运行态 |

不直接扩展通用附件服务处理 200MB CAD。Space 文件链路需要流式、隔离和资源配额；通用附件可复用存储端口，但不能复用其当前上传实现作为安全保证。

## 3. 组件与端口

### 3.1 应用端口

```csharp
public interface ISpaceFileStore
{
    Task<UploadSession> BeginUploadAsync(UploadRequest request, CancellationToken ct);
    Task CompleteUploadAsync(Guid sessionId, CancellationToken ct);
    Task<Stream> OpenQuarantinedReadAsync(Guid fileId, CancellationToken ct);
    Task PromoteAsync(Guid fileId, CancellationToken ct);
    Task<Stream> OpenArtifactReadAsync(Guid artifactId, CancellationToken ct);
}

public interface IFileSafetyScanner
{
    Task<FileSafetyResult> ScanAsync(FileScanRequest request, CancellationToken ct);
}

public interface ICadConverter
{
    Task<SpaceCadConversionResult> ConvertAsync(
        SpaceCadConversionRequest request,
        Stream source,
        ISpaceCadIrSink sink,
        CancellationToken ct);
}

public interface ICadSemanticParser
{
    Task<SemanticPreview> ParseAsync(CadIrReference ir, MappingProfile profile, CancellationToken ct);
}

public interface IExcelModelParser
{
    Task<ExcelPreview> PreviewAsync(ExcelPreviewRequest request, CancellationToken ct);
}
```

领域和 Application 只能依赖这些端口，不依赖具体 CAD SDK、杀毒引擎、对象存储 SDK 或 Excel 库类型。

`ICadConverter` 的所有执行必须经过 Application 层的
`SpaceCadConverterContractRunner`，不得由 WebApi、Worker 命令处理器或工具入口直接调用。
Runner 把调用方提供的隔离区 Source 包装为只读且不转移所有权，按顺序校验流式
Document → Layer/Block → Entity → Complete 协议，并把返回结果与 Sink 实际提交的
Artifact SHA、Summary、Issue、Provider Key/Version 完整绑定。适配器即使捕获并忽略一次
写 Source 或 Sink 协议异常，整次转换仍失败关闭；供应商 SDK 类型和临时路径不得穿过该边界。

### 3.2 部署边界

| 进程 | 职责 | 权限 |
|---|---|---|
| WebApi | 创建上传会话、鉴权、元数据、任务查询、确认预览 | 不可执行 CAD/Excel 解析 |
| Space Worker | 调度非 CAD 任务、Excel 预检、命令导入、校验 | 无任意外网访问 |
| CAD Converter Worker | DWG/DXF 转换和几何读取 | 最小文件权限、低权限账号、独立临时目录 |
| Safety Scanner | MIME/签名/恶意内容/压缩炸弹检查 | 只读隔离对象 |
| SQL Server | 来源、Job、Attempt、Step、Issue、幂等记录 | 任务权威 |
| 对象存储 | 原文件、中间表示、预览、错误报告、快照 | 私有桶/容器 |

CAD Worker 崩溃、超时或被恶意文件拖死时，WebApi 和其他业务模块必须继续可用。

## 4. 数据模型

### 4.1 来源 `Space_ModelSource`

| 字段 | 说明 |
|---|---|
| `Id/TenantId/ModelVersionId` | 来源身份和范围 |
| `SourceType` | Dwg/Dxf/Pdf/Png/Jpg/Excel/Editor/Template |
| `FileId` | 文件元数据 |
| `DisplayName` | 用户可见名称 |
| `Sha256` | 原始内容哈希 |
| `ParserVersion` | 解析器/转换器版本 |
| `MappingProfileId/Version` | 使用的映射版本 |
| `Unit/Scale` | 原单位和毫米换算 |
| `TransformJson` | 原点、旋转、镜像、页码 |
| `State` | Uploaded/Scanning/Ready/Parsing/PreviewReady/Imported/Rejected |
| `ImportedCommandBatchId` | 最近确认批次 |

同一文件可被多个版本引用，但每次导入都要记录映射、解析器版本和命令批，不能只保留“最后一次结果”。

### 4.2 文件 `Space_File`

| 字段 | 说明 |
|---|---|
| `Id/TenantId` | 文件身份 |
| `StorageKey` | 不可由文件名推导的随机对象键 |
| `OriginalName` | 仅显示；输出时转义 |
| `DeclaredContentType` | 客户端声明 |
| `DetectedContentType` | 服务端识别 |
| `Extension` | 规范化扩展 |
| `SizeBytes` | 流式计数 |
| `Sha256` | 内容哈希 |
| `State` | Uploading/Quarantined/Scanning/Clean/Rejected/Deleted |
| `ScanEngine/SignatureVersion` | 扫描证据 |
| `ScanResultCode` | 稳定结果码 |
| `RetentionClass` | Source/Artifact/Temporary |

对象存储键至少包含不可预测 ID；下载必须通过后端授权或短期签名 URL。数据库和日志不得保存客户端本地路径。

### 4.3 Job Ledger `Space_Job`

| 字段 | 说明 |
|---|---|
| `Id/TenantId` | Job ID |
| `JobType` | FileScan/CadConvert/CadParse/ExcelPreview/Import/Validate/BuildScene/Publish/Reconcile |
| `SubjectType/SubjectId` | Source、Version 或 PublishAttempt |
| `BusinessKey` | 业务幂等键 |
| `Status` | Queued/Running/Succeeded/Failed/Cancelled/DeadLetter |
| `Priority` | 受控优先级 |
| `AttemptCount/MaxAttempts` | 尝试 |
| `NextAttemptAtUtc` | 退避 |
| `LockedBy/LockedAtUtc/LockExpiresAtUtc` | 租约 |
| `ProgressDone/ProgressTotal/ProgressStage` | 单调进度 |
| `RequestedBy/RequestedAtUtc` | 发起者 |
| `CorrelationId` | 链路 |
| `LastErrorCode/LastErrorSummary` | 可运维信息 |
| `RowVersion` | 抢占并发 |

约束：

- 活跃 Job 唯一：`(TenantId, JobType, BusinessKey)` 在 Queued/Running 状态最多一条。
- `BusinessKey` 由服务端根据业务输入生成，不能直接信任客户端。
- 终态 Job 不复活；重试新增 Attempt，复用同一 Job 或建立显式 RetryOfJobId。

### 4.4 Attempt、Step 与 Artifact

`Space_JobAttempt`：

- JobId、AttemptNo、WorkerId、Started/Finished、Outcome。
- InputHash、ProcessorVersion、ResourceUsageJson。
- ErrorCode、SanitizedError、DiagnosticArtifactId。

`Space_JobStep`：

- AttemptId、StepNo、StepCode、状态、开始/结束。
- CheckpointJson、OutputHash。
- 每个步骤具有唯一 `(AttemptId, StepCode)`，重复执行时先检查输出。

`Space_Artifact`：

- JobId/SourceId、ArtifactType、StorageKey、Sha256、Size、SchemaVersion。
- 类型包括 CadIr、LayerInventory、PreviewSet、Thumbnail、ExcelErrorReport、CanonicalSnapshot、SceneChunk。
- Artifact 不允许包含密钥；下载执行同一租户和数据范围鉴权。

### 4.5 问题 `Space_ModelIssue`

统一保存解析、映射、导入和校验问题：

| 字段 | 说明 |
|---|---|
| `ModelVersionId/SourceId/JobId` | 上下文 |
| `Severity` | Info/Warning/Blocking |
| `Code` | 稳定机器码 |
| `SourceRef` | CAD Handle/Layer/Block 或 Excel Sheet/Row/Column |
| `TargetLogicalId` | 已匹配目标 |
| `MessageArgsJson` | 本地化参数，不存拼接文案 |
| `SuggestedActionCode` | 修复动作 |
| `Status` | Open/Resolved/Acknowledged |
| `ResolutionCommandBatchId` | 如何修复 |

Blocking 不允许忽略；Warning 的确认必须记录用户和原因。

## 5. 文件安全链路

### 5.1 上传

1. WebApi 鉴权并校验 Site/Version 编辑权限。
2. 创建上传会话，写最大字节数、允许格式和过期时间。
3. 客户端流式上传到隔离对象；服务端边读边计数并计算 SHA-256。
4. 超限立即停止，不把完整文件读入 `MemoryStream`。
5. 完成后检测魔数、容器格式和实际 MIME。
6. 文件保持 `Quarantined`，创建 FileScan Job。
7. 扫描通过后状态变为 `Clean`；随后才允许解析 Job 引用。

### 5.2 校验矩阵

| 检查 | 失败结果 |
|---|---|
| 扩展名与魔数不一致 | `SPACE_FILE_TYPE_MISMATCH` |
| 超出租户或系统上限 | `SPACE_FILE_TOO_LARGE` |
| 文件被加密/口令保护 | `SPACE_FILE_ENCRYPTED_UNSUPPORTED` |
| 宏或嵌入可执行对象 | `SPACE_FILE_ACTIVE_CONTENT` |
| 病毒/恶意内容命中 | `SPACE_FILE_MALWARE_DETECTED` |
| 压缩比、条目数或解压总量超限 | `SPACE_FILE_ARCHIVE_BOMB` |
| 格式损坏 | `SPACE_FILE_CORRUPT` |
| 扫描器不可用 | 保持 Quarantined，返回可重试状态 |

默认上限沿用主 Spec：CAD 200MB、底图 100MB、Excel 50MB；所有上限在读取过程中执行。

### 5.3 Worker 沙箱

- 每个 Attempt 使用独立临时目录，路径由服务端生成。
- 默认禁止出网；XRef 不自动联网下载。
- CPU、内存、进程数、临时磁盘和墙钟时间均设限。
- 子进程超时先终止进程树，再标记 Attempt Failed。
- Worker 只获取单个 Job 所需的短期对象访问凭据。
- 原文件只读，中间产物写入独立位置。
- 任务结束清理临时目录；失败清理也不能删除原文件和诊断 Artifact。

## 6. Job 执行语义

### 6.1 抢占与续租

1. Worker 查询 `Queued` 且 `NextAttemptAtUtc <= now` 的小批 Job。
2. 用 RowVersion 更新为 Running，并写 `LockedBy/LockExpiresAtUtc`。
3. 默认每 20 秒续租，租约 60 秒。
4. 连续两次续租失败，当前执行停止在安全检查点，不继续提交领域写入。
5. 其他 Worker 只能在租约过期后接管。

消息队列可在 Job 入队后发送 `job-created` 唤醒信号。信号丢失时，数据库轮询仍会执行；重复信号不会创建重复 Job。

### 6.2 Checkpoint

长任务拆成可复现步骤：

```text
AcquireInput
→ Inspect
→ Convert
→ Normalize
→ BuildInventory
→ SemanticParse
→ PersistPreview
→ Finalize
```

每步提交 OutputHash 和 Artifact。接管时只复用哈希、处理器版本和输入均匹配的成功步骤；否则从最近安全步骤重做。领域命令只在用户确认后进入独立事务，不在转换过程中边解析边写草稿。

### 6.3 重试分类

| 类型 | 示例 | 行为 |
|---|---|---|
| Transient | 对象存储短暂失败、Worker 被回收 | 指数退避自动重试 |
| Resource | 内存/磁盘/时间超限 | 不自动无限重试；调整配额后人工重试 |
| Input | 文件损坏、单位未知、字段错误 | 生成问题，等待用户修复 |
| Security | 恶意内容、越权、签名异常 | 不重试，安全审计 |
| Bug | 未分类异常 | 有限重试后 DeadLetter，保留诊断 |

## 7. CAD 流程

### 7.1 技术试验先行

正式实现前用固定样本比较候选 DWG/DXF 方案：

- 支持版本、实体、块、属性、样条、字体、单位和插入变换。
- Windows/Linux、容器化、并发和离线部署。
- 商业授权、按核/按实例/按文件限制。
- 稳定 Handle/SourceRef 能力。
- 200MB 边界样本的 CPU、内存、耗时和崩溃隔离。
- 恶意/损坏文件行为。

输出 ADR。若服务端 DWG SDK 不满足授权或部署条件，可以采用受控转换服务，但用户入口仍接受 DWG；不能把“用户自行另存 DXF”作为正式唯一方案。

### 7.2 CAD IR

转换器输出版本化中间表示，不向领域泄漏 SDK 类型：

```json
{
  "schemaVersion": 1,
  "document": {
    "sourceSha256": "...",
    "unit": "mm",
    "toMillimeter": 1.0,
    "bounds": [0, 0, 120000, 80000]
  },
  "layers": [],
  "entities": []
}
```

每个 Entity 至少包含：

- `sourceRef`：稳定 Handle 或组合引用。
- `type`：Line/Polyline/ClosedPolyline/Circle/Arc/BlockReference/Text。
- `layerId`、`blockName`、受控属性。
- 规范几何和完整仿射变换。
- 原始包围盒、是否闭合、是否支持。

IR 以 Artifact 保存。大文件使用流式记录格式，API 不一次返回全部 Entity。

### 7.3 语义解析

`Space_LayerMappingProfile` 和版本化 Mapping Item 定义：

- 图层名/正则、块名、属性条件。
- 目标类型 Wall/Column/Door/Dock/Zone/Aisle/Rack 等。
- 几何解释规则、默认高度/厚度。
- 置信度权重和必须图层标记。

Design V1 以 `/api/space/design/v1/mapping-profiles/cad` 提供租户 Profile 的
list/get/save 权威。System 版本只读；租户复制后保存完整规则快照和 SHA-256，
后续修改必须携带 RowVersion 与 Idempotency-Key，并追加不可变版本，不能原地覆盖。
Profile/Version 以 Tenant 复合外键隔离，第一版不允许读取或复制其他租户方案。

解析器输出 Preview Item：

- 临时 `previewObjectId`，不是永久 LogicalId。
- 来源引用、目标类型、规范几何。
- 置信度、采用的规则、默认值。
- `AutoAccepted/Candidate/Rejected`。

阈值：

- `>= 0.90` 自动选中。
- `0.70–0.89` Warning 并等待确认。
- `< 0.70` 只显示候选，不进入确认集。

用户可修正类型、几何和属性并锁定。重新解析时，锁定校正通过 `SourceRef + userCorrectionVersion` 重新应用；无法重放时产生 Blocking，不静默覆盖。

### 7.4 Preparation 到 Parse 的确定性重放

向导确认后，服务端把 Tenant、Source SHA、Mapping Profile ID/Version、Profile Definition SHA、Inventory/Source Structure/Mapping Preview SHA，以及完整 Layer Overrides 写入规范排序、SHA-256 密封的 Mapping Replay Snapshot。Preparation 与后台 Parse Job 都保存同一快照，客户端不能提交或修改该字段。

历史 schema v4 首次封存 Mapping Replay Snapshot；当前新 Parse Job 使用 schema v5，在同一快照上继续封存 Provider Key + Version。启动服务和 Worker 必须分别在入队前、Provider 调用前复核快照身份、哈希及 Provider 版本。Provider 使用不可变 Profile 版本和快照覆盖重新生成 Mapping Preview，并验证结果与快照期望值完全一致；不一致时失败关闭，不产生 PreviewSet 或 Draft 写入。schema v2–v4 只用于历史 Job 兼容，不允许新建。

## 8. Excel 流程

### 8.1 标准模板

MVP 模板包含：

| Sheet | 主要字段 |
|---|---|
| `Racks` | FloorCode、ZoneCode、RackCode、位置/尺寸、模板 |
| `RackLevels` | RackCode、LevelNo、BottomZ、ClearHeight、BinCount、DepthCount、承重 |
| `Locations` | LocationCode、RackCode、Col、Level、Depth、状态 |
| `Bindings` | WMS Warehouse、ExternalLocationId、LocationCode |
| `Attributes` | ObjectType、BusinessKey、Namespace、Key、Value、Unit |

货主、批次、容器和制造字段在 `Attributes` 中预留命名空间，但运行库存不导入设计表。

### 8.2 自定义映射

映射方案包含：

- Sheet 匹配、标题行、数据起始行。
- 目标字段、源列、类型、格式、默认值。
- 业务键和引用键。
- 枚举/单位转换。
- 空值、重复和未知列策略。

系统方案只读；租户可复制形成私有版本。保存后版本不可原地修改，新变更产生 MappingProfileVersion。

### 8.3 预检与确认

预检只生成 Preview，不写版本：

- 工作表、行、列定位。
- 类型、必填、范围、重复和引用校验。
- 与 CAD Preview 或现有 Draft 的匹配结果。
- 新增、更新、无变化、冲突数量。
- 可下载错误报告；报告自身按原文件权限保护。

确认请求必须带：

- `previewId`
- `previewHash`
- `expectedVersionContentRevision`
- `idempotencyKey`

确认事务重新校验预览未过期，把结果转换成统一设计命令。重复确认返回原 CommandBatch 结果，不产生重复货架或库位。

## 9. 底图流程

PDF/PNG/JPG 只作为可追踪来源：

1. 文件安全通过。
2. PDF 选择页码并生成受控预览图。
3. 用户选择两个已知点输入实际距离，系统计算比例。
4. 用户确认原点和 RotationZ。
5. 使用第三控制点验证误差。
6. 保存 `UnderlayCalibration`：来源、页码、像素点、实际点、比例、误差、操作者。
7. 编辑器读取设计版本中的标定，不直接依赖运行态 URL。

标定变化会改变 Floor Revision 和版本 ContentHash，使旧校验失效。

## 10. API

基础路径：`/api/space/design/v1`

| Method | Route | 作用 |
|---|---|---|
| POST | `/versions/{versionId}/upload-sessions` | 创建上传会话 |
| POST | `/upload-sessions/{sessionId}/complete` | 完成上传 |
| GET | `/files/{fileId}` | 文件状态和扫描结果 |
| POST | `/versions/{versionId}/sources` | 建立模型来源 |
| GET | `/versions/{versionId}/sources` | 来源列表 |
| POST | `/sources/{sourceId}/parse` | 创建解析 Job |
| GET | `/jobs/{jobId}` | 任务状态 |
| POST | `/jobs/{jobId}/cancel` | 请求取消 |
| POST | `/jobs/{jobId}/retry` | 授权重试 |
| GET | `/sources/{sourceId}/preview` | 分页预览 |
| POST | `/sources/{sourceId}/preview/confirm` | 确认导入 |
| GET/POST | `/mapping-profiles/cad` | CAD 映射 |
| GET/POST | `/mapping-profiles/excel` | Excel 映射 |
| POST | `/sources/{sourceId}/underlay-calibration` | 保存底图标定 |

下载端点不接受任意路径；只接受 Artifact/File ID，经 `ISpaceAccessEvaluator` 重新授权。

## 11. Problem Details

示例：

```json
{
  "type": "https://cp6.example/problems/space-file-unsafe",
  "title": "文件未通过安全检查",
  "status": 422,
  "code": "SPACE_FILE_MALWARE_DETECTED",
  "traceId": "00-...",
  "correlationId": "guid",
  "recovery": {
    "action": "replace-file",
    "retryable": false
  }
}
```

稳定错误码至少包括：

- `SPACE_UPLOAD_SESSION_EXPIRED`
- `SPACE_FILE_TOO_LARGE`
- `SPACE_FILE_TYPE_MISMATCH`
- `SPACE_FILE_QUARANTINED`
- `SPACE_FILE_MALWARE_DETECTED`
- `SPACE_FILE_ARCHIVE_BOMB`
- `SPACE_CAD_UNIT_REQUIRED`
- `SPACE_CAD_CONVERSION_FAILED`
- `SPACE_MAPPING_PROFILE_INVALID`
- `SPACE_PREVIEW_STALE`
- `SPACE_EXCEL_REFERENCE_INVALID`
- `SPACE_JOB_LEASE_LOST`
- `SPACE_JOB_NOT_RETRYABLE`

前端和 SDK 依据 `code/recovery` 行为，不解析中文 message。

## 12. 可观测性

每个 Job 记录：

- CorrelationId、TenantId、SiteId、VersionId、SourceId。
- 输入哈希、处理器、映射、规则和 Artifact 版本。
- Queue wait、步骤耗时、CPU、峰值内存、临时磁盘。
- 输入、自动接受、候选、阻断、用户修正、输出数量。
- 失败分类、重试、接管和租约丢失。

指标：

- 上传/扫描/转换/解析成功率和 P50/P95。
- 每种文件、实体和问题码数量。
- CAD 自动识别覆盖率和准确率。
- 人工校正占比。
- 从上传到 PreviewReady、Ready 的墙钟时间。
- DeadLetter 和安全拒绝数量。

日志不得包含文件正文、Excel 单元格全集、CAD 原始内容、令牌或签名 URL。

## 13. 测试

### 13.1 单元

- 格式/魔数矩阵、大小边界和 SHA-256。
- Job 状态机、退避、幂等键和 checkpoint。
- 单位、仿射变换和 CAD IR 规范化。
- MappingProfile 版本与置信度。
- Excel 类型、引用和重复规则。

### 13.2 SQL Server 集成

- 活跃 Job 唯一索引。
- 两 Worker 竞争租约只有一个成功。
- 租约过期接管和旧 Worker 提交被拒绝。
- 同一预览重复确认只产生一个 CommandBatch。
- 租户过滤、Artifact 下载和 cursor 范围。

### 13.3 安全与故障注入

- 文件头伪装、路径穿越文件名、超大流。
- ZIP bomb、宏、嵌入对象、损坏 CAD/Excel。
- 扫描器超时、对象存储断连、Worker 崩溃。
- CAD 子进程内存/时间超限。
- 任务完成后重复消息、乱序消息和丢失消息。
- 临时目录和签名 URL 不被其他租户访问。

### 13.4 标准验收

- 标准 DXF/DWG 输出等价 IR。
- CAD+Excel 在 60 分钟指标内形成 Ready 草稿。
- 底图第三控制点满足误差口径。
- 未知/低置信对象全部可定位。
- 用户锁定校正不被重解析覆盖。
- 两条建模路径最终规范快照一致。

## 14. 实现顺序

1. 文件元数据、隔离存储和流式上传。
2. 安全扫描端口与拒绝策略。
3. Job/Attempt/Step/Artifact 和租约 Worker。
4. CAD 技术与授权 ADR。
5. CAD IR、图层清单和 Preview。
6. Excel 标准模板、映射和 Preview。
7. 底图页选择和三点校验。
8. 预览确认到统一设计命令。
9. 安全、故障注入和 60 分钟验收。

## 15. 完成定义

- WebApi 无 CAD/Excel 解析库依赖，也不直接打开不可信文件。
- 数据库 Job Ledger 能在消息丢失、进程重启和多 Worker 下恢复。
- CAD、Excel、底图和模板最终调用同一设计命令服务。
- 任意输出都可追溯到原文件哈希、解析器、映射、用户确认和命令批。
- 恶意文件、损坏文件和资源耗尽不会影响当前 Published 版本或拖垮 WebApi。
