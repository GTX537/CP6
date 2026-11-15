# W628-01 Excel/CAD Apply 后台批次

基线 `e486c9522569835489f4b5a9457b506a9569cfb2`，本地源码与测试提交 `639ef2de3860f543c26824df09a52e5937a00a3c`。原 Main 在真实 PostgreSQL 已开始 `Space_ElementCommandBatch` 写入后，即使独立编辑租约已自然到期，仍提交批次、机架、层、库位、修订及 Source Imported，并将 Job 标为 Succeeded。基线包含未改动的产品源码和对应 DLL 哈希。 [Main #1151](https://github.com/GTX537/CP6/issues/1151) · [基线 TRX](D:/CP6-test-artifacts/W628-01/baseline-expiry/results.trx) · [基线数据库记录](D:/CP6-test-artifacts/W628-01/baseline-expiry/permission-consumption-baseline-expiry/space-excel-cad-apply-edit-expiry.json) · [基线源码与制品](D:/CP6-test-artifacts/W628-01/baseline-expiry/source-and-assemblies.json)

`SpaceExcelCadApplyJobStepExecutor` 复用 W626 的 PostgreSQL Serializable 事务。在原生事务先取得 Main 根锁，锁定并校验原已接受 Job 的当前 Attempt、Worker、输入和取消状态，再按原 Floor 写入顺序锁住编辑租约和当前来源。私有 Match 与 XLSX 读取和解析在事务外。事务内重新核对原 Match Artifact 和当前 File 的 ID、范围、存储键、大小、哈希和 Clean 状态，并保留原 Draft/Source、版本和楼层修订 CAS。批次、命令历史、机架层级、修订和 Source Imported 在同一事务提交。提交前使用推进的 PostgreSQL 时钟再核对 Job 与独立编辑租约，沿用 Main 租户到期语义。原后端已提交批次可只读重放。原生 COMMIT 应答不明时，按已存批次与 Source 读回原结果，读不到实证则沿用 Job 原恢复语义。 [Apply 写入](https://github.com/GTX537/CP6/blob/639ef2de3860f543c26824df09a52e5937a00a3c/CP6.Space.Infrastructure/SpaceExcelCadApplyJobStepExecutor.cs#L121) · [共享提交入口](https://github.com/GTX537/CP6/blob/639ef2de3860f543c26824df09a52e5937a00a3c/CP6.Space.Infrastructure/SpaceArtifactWriteTransaction.cs#L122)

## 实际验证

隔离 PostgreSQL 18.6 与真实 Main Kestrel。模拟租户、Excel 和 CAD 前置快照。真实签名 Match POST、真实 Match worker、真实私有 Artifact 字节及哈希和签名 Confirm POST 准备每个 Apply Job。真实 `SpaceProcessingJobWorker` 运行已接受的 Apply，夹具控制单次调度、原生锁和 COMMIT 后应答。 [原生 TRX](D:/CP6-test-artifacts/W628-01/native-verified/results.trx) · [原生测试](https://github.com/GTX537/CP6/blob/639ef2de3860f543c26824df09a52e5937a00a3c/CP6.Tests/Erp/SpaceExcelCadApplyWorkerWriteAuthorityPostgreSqlTests.cs#L20)

| 场景 | 实际结果 |
| --- | --- |
| 暂停先提交 | Apply 等待签名暂停持有的 Main 根锁。暂停后原 Job 延后，批次、命令、层级、修订、Source 和私有文件快照相同。重新启用后，同一 Job 的下一 Attempt 提交原预分配批次与三个命令。 [暂停及恢复](D:/CP6-test-artifacts/W628-01/native-verified/permission-consumption-native-verified/space-excel-cad-apply-pause-first.json) · [恢复后](D:/CP6-test-artifacts/W628-01/native-verified/permission-consumption-native-verified/space-excel-cad-apply-pause-first-resumed.json) |
| Apply 先提交且 COMMIT 应答遗失 | 签名暂停等待 Apply 持有的 Main 根锁。释放实际批次写锁后，PostgreSQL 已提交，随后签名暂停完成并注入实际 COMMIT 后应答中断。同一 Attempt 在暂停中从原生批次记录恢复结果，原 checkpoint 与结果哈希相符。重新启用后调度无新增效果。 [提交时快照](D:/CP6-test-artifacts/W628-01/native-verified/permission-consumption-native-verified/space-excel-cad-apply-effect-first.json) · [恢复后](D:/CP6-test-artifacts/W628-01/native-verified/permission-consumption-native-verified/space-excel-cad-apply-effect-first-resumed.json) |
| 编辑租约自然到期 | PostgreSQL 记录批次 INSERT 在有效期内进入锁等待。数据库时钟越过原租约期限后释放等待，原 Attempt 以 `SPACE_EDIT_LEASE_LOST` 结束。Source、版本、楼层和私有制品快照与初始一致，批次、命令、机架、层和库位均未落库。 [自然到期证据](D:/CP6-test-artifacts/W628-01/native-verified/permission-consumption-native-verified/space-excel-cad-apply-edit-expiry.json) |

两次暂停都使原会话的 Confirm 返回 401。重新启用并登录后，同一 Confirm 键返回 202、`Idempotent-Replay: true` 和原 Job/Batch ID，GET 读回原批次结果。成功批次各生成一套机架、层和库位，版本和楼层各前进一次，三个命令及 Apply 计划哈希一致。

共 39 项通过，零跳过。新增实库 3 项，W609 已签名前台 Start/Confirm/Compensate 两顺序 6 项，W626 共享事务回归 7 项，worker 4 项，Match/Apply 与 Excel Preflight 兼容 19 项。首次兼容尝试从空目录编译默认 Core，Roslyn 内存耗尽。随后使用通过实库测试的 PostgreSQL 依赖制品，并以和 W626 相同的隔离包锁和无关 Dispatch 测试夹具排除执行，19 项通过。工作树内的项目配置与锁文件没有差异。 [前台回归](D:/CP6-test-artifacts/W628-01/foreground-regression/results.trx) · [元数据回归](D:/CP6-test-artifacts/W628-01/metadata-regression/results.trx) · [worker](D:/CP6-test-artifacts/W628-01/processing-controls/results.trx) · [首次兼容日志](D:/CP6-test-artifacts/W628-01/compat/test.log) · [通过的兼容回归](D:/CP6-test-artifacts/W628-01/compat-pg/results.trx)

集中审查了五个提交文件及共享事务的调用。原生锁与应答用例的真实物化范围为基础机架、层、库位、修订和 Source，属性和绑定更新沿用既有兼容回归。生产 WMS 和历史 Republish 属于各自的验证范围。

## 本地发布与工作区

Debug PostgreSQL 发布包含 310 个文件的 SHA-256 清单，七份产品 DLL 与通过测试的宿主字节一致，五份提交文件与实际编译时哈希一致。发布宿主在独占 PostgreSQL 57020 和 HTTP 57028 启动，活性返回 200，匿名 Match 和 Confirm 各返回 401。本任务全部进程均已停止，57020–57029 无监听。验证时 `codex/w628-01` 工作树干净。上述验证未触发 Actions 或部署。 [发布冒烟与哈希绑定](D:/CP6-test-artifacts/W628-01/publish-verified-smoke.json) · [制品清单](D:/CP6-test-artifacts/W628-01/publish-sha256.json) · [停机记录](D:/CP6-test-artifacts/W628-01/process-verification.json)

本项以已核实主线 `e486c9522569835489f4b5a9457b506a9569cfb2` 采用原受测代码。正式 PGT-017 保持 NOT_RUN。
