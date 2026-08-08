# Space 生产处理 Job Worker 接线报告

日期：2026-08-08
基线：`17bce8df`（`integration/space-v1-20260730`）
功能提交：`d09e44dc`
验证报告提交：`67d8b417`
no-ff 集成提交：`51012a43`

## 交付结论

Space WebApi 生产 Host 现在除既有发布专用 Worker 外，还会启动独立的 Processing Worker，按租户消费 Excel、CAD、版本校验、AI Apply 和保留清理 Job。E03-S05 的 `ExcelCadApply` 以及其上游 `ExcelPreview`、`CadParse`、`ExcelCadMatch` 不再处于“API 能排队、生产 Host 永远不认领”的内部状态。

本次没有启用来源不明的 CAD 转换器或外部 AI Provider。未配置的 CAD、Import 或 BuildScene 执行器继续通过既有 Job 状态机返回稳定失败、租约和重试证据；Worker 接线只保证生产 Host 会安全认领与执行已注册处理器，不绕过各处理器自己的失败关闭边界。

## 实现范围

1. 保留 `SpacePublishJobWorker` 独立处理 `HistoricalRepublish`、`Reconcile` 和 `Publish`，不改变发布恢复优先级、Worker 身份或每租户批量上限。
2. 新增 `SpaceProcessingJobWorker`，消费九类已注册非发布处理器：
   - `ExcelPreview`
   - `CadParse`
   - `ExcelCadMatch`
   - `ExcelCadApply`
   - `Import`
   - `BuildScene`
   - `ApplyGeneration`
   - `Validate`
   - `AiRetentionCleanup`
3. 每个租户、每一轮对每种处理类型最多认领一个 Job。只要本轮有工作，Host 会立即开始下一轮；无工作时等待五秒。热队列不能独占一个租户轮次，也不会饿死后面的处理类型。
4. 两个 Worker 均复用现有 `TenantScopeRunner`、`ISpaceJobProcessorRunner`、租约、心跳、checkpoint、超时、退避、取消和接管语义；多 Host 并发仍由数据库租约围栏保证。
5. Processing Worker 使用稳定、非空、可解析的系统 Actor ID，并在每个租户独立作用域内推入 Core/Application 执行上下文。AI Apply 等要求内部非空 Actor 的处理器可在后台执行，同时保持 Tenant 和相关性身份一致。
6. 新增 `AddSpaceJobWorkers()` 注册入口，同时注册发布和处理 Worker。原有 `Startup:SkipHostedServices` 测试/维护开关继续统一移除全部 HostedService。
7. 三种没有注册 `ISpaceJobProcessor` 的枚举值——`FileScan`、`CadConvert`、`CloneVersion`——不会被本 Worker 猜测认领；其现有独立流程保持不变。

## 验证证据

- Processing Worker 聚焦：3/3 passed，覆盖 HostedService 幂等注册、12 个已注册处理器类型无遗漏/无重复、双租户公平认领、租户上下文、系统 Actor 和相关性传播；
- 默认处理器注册：1/1 passed，仍解析 12 个显式处理器；
- Job Processor 状态机：17/17 passed；
- Space Unit：464 passed / 0 failed / 0 skipped；
- 默认 Space Integration：270 passed / 0 failed / 94 SQL-environment-gated skipped；
- CP6.Tests：2811 passed / 0 failed / 17 environment-gated skipped；
- 完整 `CP6.slnx` Release 单线程构建：0 warning / 0 error，包含 WebApi、Desktop 与 Android 双架构 AOT；
- 四个任务 C# 文件 whitespace 校验和 `git diff --check` 通过。`Program.cs` 只替换一条 HostedService 注册语句；该历史大文件自身不满足全文件 formatter，未借本卡机械改写无关代码。
- 集成并推送后清理 36 个可重建 `bin/obj` 目录、6,190 个文件、1,206,049,385 bytes（约 1.123 GiB）；源码、报告和远端 Git 历史不受影响。

## 未改变与后续边界

- 无数据库模型、Migration、API、OpenAPI、SDK 或前端变化。
- 正式 CAD Provider、组织授权 DWG/DXF 黄金集、真实大文件/异常/性能证据仍是外部门禁。
- 外部 AI Provider、模型、区域、SecretReference 与供应商签收仍关闭；本卡不会使其自动可用。
- E03-S05 当前仍只写入可确定性复核的 `Racks` 行；Zone、Aisle、RackLevel、Location 和 `RackTemplateCode` 的权威解析与原子写入仍是下一项可在本地继续推进的内部范围。
