# E02-S08 CAD 解析作业安全开发报告

- 状态：Integrated
- 日期：2026-08-06
- 起始集成基线：`0800ae775a689e9e24e6b231cbdcf519a8046b75`
- 功能分支：`codex/space-e02-s08-cad-job-safety`
- 功能提交：`20ade7e7`
- 证据提交：`29667831`
- no-ff 集成提交：`feaf29fb`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

E02-S08 已交付持久化 CAD Parse Job 的上传、排队、查询、取消、显式重试、Artifact 持久化和
PreviewReady 收口。它复用 Space Job Ledger 的 Attempt、Step、租约、checkpoint、取消和重试语义，
没有另建一套内存任务状态。

本切片解决的是开发侧作业安全闭环，不是正式原生 CAD 生产签收。生产默认
`ISpaceCadParseProvider` 仍失败关闭；没有获得授权的 DWG/DXF 文件、供应商 SDK/服务或 production
Worker composition root 时，系统不会伪造解析结果，也不会把合成开发语料计入发布门禁。

## 2. 作业和状态边界

- JobType 固定为 `CadParse`，SubjectType 为 `ModelSource`，processor version 为
  `space-cad-parse-v1`。
- 冻结 payload 绑定 Version、Source、File、Source SHA、格式、Floor、单位/比例、坐标元数据、
  CoordinateTransform SHA 和 Mapping Profile/version/definition/preview SHA。
- 第一步 `GenerateArtifacts` 只调用 Provider、流式校验并持久化 Artifact；第二步
  `FinalizePreview` 重新验证 checkpoint 和 Artifact 身份后才收口 Source。
- 排队和运行时 Source 保持 `Ready`。取消、Provider 不可用、超时、校验失败或进程中断都不会把
  Source 留在 `Parsing`；最终事务才执行 `Ready -> Parsing -> PreviewReady`。
- 成功路径不写 Draft、Published、WMS 或设备运行事实。Source、Job、Artifact 仍受 Tenant、Site 和
  ModelVersion 边界约束。
- Job Runner 为 CadParse 提供 30 分钟租约超时并纳入允许执行的 processor 白名单；取消只在安全点
  确认，不截断数据库提交。
- SQL Server 对同一 Tenant/Source 的启动与重试使用 transaction-owned `sp_getapplock`；并发同键先
  串行化再重读幂等记录，避免把唯一约束竞争或数据库异常暴露给调用方。

## 3. Artifact 与幂等性

Provider 必须一次返回且只返回以下三类工件：

1. `CadIr`
2. `LayerInventory`
3. `PreviewSet`

每个工件在写入前验证类型、Schema、扩展名、声明大小、实际流大小和小写 SHA-256。文件通过私有
`ISpaceFileStore` 保存，数据库只记录 File/Artifact 引用；checkpoint 只保存 Source/坐标/映射哈希及
Artifact ID、文件 ID、SHA 和大小，不保存原始 CAD 或大载荷。

同一 Job 的步骤重放会复用已提交 File/Artifact。显式 Retry 创建带 `RetryOfJobId` 的新 Job；只有
输入哈希和 processor version 完全匹配时，Job Ledger 才允许从直接父 Job 复用 checkpoint。输入变化、
processor 升级、部分工件或身份不一致均失败关闭，不跨任意历史 Job 猜测复用。

如果对象存储写入成功而数据库提交失败，执行器尽力删除本轮新对象；删除失败时仍由既有引用感知保留
清理兜底。相同内容文件在 EF Local 和数据库两层查重，避免同一保存批次触发唯一键冲突。

## 4. API 表面

控制器前缀为 `api/space/design/v1`：

- `POST /versions/{versionId}/cad-sources`：上传 DWG/DXF，100 MiB 上限；
- `POST /versions/{versionId}/sources/{sourceId}/cad-parses`：按 `Idempotency-Key` 排队；
- `GET /versions/{versionId}/sources/{sourceId}/cad-parses/{jobId}`：读取 Job 与 Artifact 状态；
- `POST /versions/{versionId}/sources/{sourceId}/cad-parses/{jobId}:cancel`：请求取消；
- `POST /versions/{versionId}/sources/{sourceId}/cad-parses/{jobId}:retry`：按新幂等键显式重试。

上传和启动要求 `space:source:upload` 与 `space:model:edit`；取消和重试要求
`space:model:edit`。所有 mutation 带 Space 审计元数据。GET 沿用认证和服务层 Tenant/Site 权限校验，
不额外声明 mutation permission，符合现有 Design V1 读取策略。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 431/431 passed |
| E02-S08 内存作业集成 | 4/4 passed |
| Processor 注册/执行聚焦 | 5/5 passed |
| Controller 聚焦 | 3/3 passed |
| Controller/权限契约聚焦 | 27/27 passed |
| 默认 Space Integration 全量 | 259 passed / 83 SQL-gated skipped / 0 failed |
| KOUSQLSERVER：排队、取消、双连接同键重试血缘 | 1/1 passed |
| KOUSQLSERVER：跨 Retry checkpoint 复用 | 1/1 passed |
| CP6.Tests 全量（沙箱外读取测试密钥） | 2788 passed / 17 environment-gated skipped / 0 failed |
| Infrastructure + WebApi Release build | passed；仅既有 CP6.Core nullable warnings |
| EF pending model changes | none |

最初在受限沙箱内执行 CP6.Tests 时，13 项 WF/SSO 测试因无法读取
`C:\Users\tt\AppData\Local\ASP.NET\DataProtection-Keys` 失败；同一二进制在允许读取该测试目录后
全量通过，证明它们是执行环境噪声，不是 E02-S08 回归。

曾尝试执行全部 Space Integration + KOUSQLSERVER，但测试进程长时间无输出，人工终止；因此本报告
不声称完整真实 SQL 矩阵通过。与本切片直接相关的两个真实 SQL 门禁均单独通过，默认非 SQL 全量也已
通过。后续如要签收生产链，必须在受控 CI/Worker 环境补完整 SQL 和端到端运行证据。

## 6. 失败关闭与正式剩余条件

默认 `UnavailableSpaceCadParseProvider` 返回可重试的 Resource/JobProcessorUnavailable，不生成空工件
或 PreviewReady。DI 即使未配置 FileStore 也可以启动，只有真正执行 CAD 步骤时才延迟解析存储依赖并
失败关闭。

正式 E02-S08/E02 生产签收仍缺：

1. 生产原生 DWG/DXF Provider 或经审查的供应商适配器，并验证许可、区域、SecretReference、限流、
   超时和错误映射；
2. 组织有权使用的原生 DWG/DXF 黄金集、冻结清单和独立预期结果，不能用仓库合成 DXF 替代；
3. production Worker host 对 `SpaceJobRunner` 的自动轮询、租约续期、关闭排空、告警和恢复演练；
4. 真实大文件、恶意/损坏文件、并发取消、Worker 崩溃恢复、对象存储故障和吞吐/时延证据。

仓库当前没有 Space 专用 Hosted Worker；现有 Import、Excel、AI 和本卡 CadParse 都依赖外部受控 Worker
host 调用 Runner。这是明确的部署缺口，不在 WebApi 请求内同步执行 Job，也不以测试手动调用冒充生产
自动接线。

## 7. 变更边界

本切片没有 EF 模型或 Migration 变化，没有创建未授权生产 CAD 适配器，没有写 Draft，没有修改
`main`。no-ff 集成后已清理当前受控工作树内 21 个可重建 `bin/obj`/隔离测试目录，共 996 个文件、
623,921,427 bytes（约 0.581 GiB）；源码、测试、报告、私有存储合同与 Git 历史保留。
