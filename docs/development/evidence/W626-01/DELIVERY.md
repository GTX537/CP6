# W626-01 Space CAD 与 Excel 后台元数据提交

登记基线 `5e98283b0414ad146b5566e01a5f432afff6d393`，源码与测试提交 `53a58f5b5cc1169a91642f8d544d04c85b3b9c87`。本次完成 CAD `FinalizePreview`、Excel `ValidateWorkbook` 的 Issues 和 `PersistPreview` 的 Source 终态三个实际写入点。W626 采用前置源码研判和正向实库验收。 [事务入口](https://github.com/GTX537/CP6/blob/53a58f5b5cc1169a91642f8d544d04c85b3b9c87/CP6.Space.Infrastructure/SpaceArtifactWriteTransaction.cs) · [CAD 步骤](https://github.com/GTX537/CP6/blob/53a58f5b5cc1169a91642f8d544d04c85b3b9c87/CP6.Space.Infrastructure/SpaceCadParseJobStepExecutor.cs) · [Excel 步骤](https://github.com/GTX537/CP6/blob/53a58f5b5cc1169a91642f8d544d04c85b3b9c87/CP6.Space.Infrastructure/SpaceExcelPreflightJobStepExecutor.cs)

三个 PostgreSQL 写入通过 W613 的 Serializable 事务取得 Main 根锁，读取当前启用和到期状态，锁定并核对已经接受的持久 Job/Attempt/Worker/Subject/输入及取消状态。读取当前 File 和 Source 后提交。提交前使用前进数据库时钟确认 lease，并按 Main 语义再次核对租户到期。私有内容读取、CAD 提供者与 Excel OpenXML 解析均位于事务外。CAD 和 Excel Preview 返回原 checkpoint 和哈希。匹配的已存 Issues 以及已经终态的 Source 沿用只读重放，零 findings 没有新业务写入。

## 实际 PostgreSQL 回归

隔离 PostgreSQL 18.6 56990 和真实 Main Kestrel 56991。用实际签名入口上传与入队，测试安全扫描阶段使用合法的模拟 clean 状态。真实 `SpaceProcessingJobWorker` 与原处理器、租约和 checkpoint 账本执行所有阶段。CAD 使用 W613 的模拟提供者与实际私有字节。Excel 使用真实私有 XLSX 和生产 OpenXML 读取器，表头与缺失映射表产生 11 条可复核 findings。测试控制单次调度和指定阶段入口。 [原生 TRX](D:/CP6-test-artifacts/W626-01/native-2/results.trx) · [各阶段 PostgreSQL 证据](D:/CP6-test-artifacts/W626-01/native-2/permission-consumption-native-2)

| 阶段 | 暂停先提交 | 效果先提交 |
| --- | --- | --- |
| CAD FinalizePreview | 等待 Main 根锁后延后 Source 写入，原 Ready 数据、三件制品、File 和字节保持。重新启用后同一 Job 重用原 GenerateArtifacts checkpoint 完成。 | 暂停等待 Source 写入，保留已提交 PreviewReady 和原制品。再次调度无重复 Job 或业务效果。 |
| Excel ValidateWorkbook | 等待 Main 根锁后留下原 Parsing Source 和零新增 Issues。重新启用后同一 Job 写入 11 条并完成。 | 暂停等待 Issues 写入，保留同一组 11 条。后续 Preview 延后且完整 Source 相同，重新启用后同一 Job 重用校验 checkpoint，只完成 Preview。 |
| Excel PersistPreview | 校验 checkpoint 及 11 条 Issues 已提交，Source 完整快照保持 Parsing。重新启用后同一 Job 完成 Preview。 | 暂停等待 Source 写入，保留 PreviewReady 和原 Issues。再次调度无重复业务效果。 |

额外的 Excel Issues 用例在实际原生 COMMIT 后中断应答，记录 `SPACE_ARTIFACT_COMMIT_UNKNOWN`。重新启用后原 Job 读回同一组 11 条完整记录和 ID，后续完成 Preview。各暂停用例的旧会话返回 401。重新启用并登录后同一 HTTP 提交键返回 202 和原 JobId。

汇总 **7 项新增原生、9 项 W613 原生制品回归、21 项 CAD/Excel 兼容、10 项控制器和 worker 回归**通过，零跳过。W613 回归实际重复验证共享事务的文件保留、已知回滚、COMMIT 应答遗失和提交前到期。W626 三个新增步骤的到期边界复用该共同提交入口。本轮原生顺序覆盖仍有重试额度的原 Job。Excel/CAD Apply 命令批次及历史 republish 属于后续独立范围。 [W613 同源码回归](D:/CP6-test-artifacts/W626-01/artifact-regression/results.trx) · [兼容测试](D:/CP6-test-artifacts/W626-01/compat/results.trx) · [控制器和 worker](D:/CP6-test-artifacts/W626-01/related-controls/results.trx)

首次构建记录了测试夹具误用 Source 属性名的编译问题，按现有类型改为 `SourceId` 后通过。兼容回归复用 W613 的独立 NuGet 锁文件和已有无关 Dispatch fixture 编译排除，签出内的项目配置和包锁保持原内容。 [首次构建](D:/CP6-test-artifacts/W626-01/native-1/build.log) · [兼容配置](D:/CP6-test-artifacts/W626-01/compat.targets)

## 本地发布与工作区

Debug PostgreSQL 本地发布 310 文件具有 SHA-256 清单，七份产品 DLL 与实际 Main 测试宿主字节一致。发布制品在 PostgreSQL 56990、HTTP 56998 实际启动，活性 200，匿名 CAD Parse 和 Excel Preflight POST 各 401。所有本任务进程已停止，56990–56999 无监听。验证时 `codex/w626-01` 工作树干净。上述验证没有运行 Actions 或执行部署。 [发布与冒烟](D:/CP6-test-artifacts/W626-01/publish-verified-smoke.json) · [文件清单](D:/CP6-test-artifacts/W626-01/publish-sha256.json) · [进程核对](D:/CP6-test-artifacts/W626-01/process-verification.json)

## 主线组合核对

W625 后主线 `30a4b08faeeb8ab5efd191c6c6f4c41e27f4f45e` 与本项组合为 `afc5a5f7e7aa559682768d7dd6df9ee1cab9fbc0`，六个受影响路径均与已验证版本逐字节相同。W625 改动只涉及已领取 OA 作业及其相关测试和台账，复用准确受测源码的 47 项通过与本地发布。正式 PGT-017 保持 NOT_RUN。
