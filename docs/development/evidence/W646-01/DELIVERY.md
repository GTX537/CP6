# W646-01 普通 Space 版本校验的共同提交权限

关联 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)。固定基线 `62c33fb35f3d5ae01c751a8cfd6b00995b11530e`，本地源码与测试提交 `737bd6bb34e67762fde83166ff1f41faa508cb0b`。集中审查对照当时的 `origin/main` `f978f0541252509d8a029a355bfa73977a2af69c`，三个任务文件无重叠。W641 对共用测试 relay 增加独立 Hold 模式，本项 `Arm()` 所用 COMMIT Drop 行为相同。

## 事务与原回执

`POST /api/space/design/v1/versions/{versionId}/validations` 在原生 Serializable 事务按当前 `space:model:validate` 取得 Main 共同根，再取得该版本现有校验锁。提交前复核同一会话。原 Version、ValidationRun 和 Validate Job 保持同事务，已接受相同内容与能力的请求沿用原 Run/Job。WMS 能力调用、完整快照读取及其哈希计算在事务外。写入前在原库重新确认当前版本内容代次、发布基准和可独立变化的来源、资产及开放 Issue。

`SpaceValidationJobProcessor` 从已经提交的原 Job/Attempt 租约出发。Run 启动，以及通过、阻止和过时失败分别用原生事务取得 Main 根，读取当前租户和作业租约，核对取消状态、版本和 Run。提交前按原共享入口复核租户有效期与数据库时钟下的 Job lease。完整校验计算在事务外。通过或阻止的 Version、Run 和生成 Issues 同事务。能力或版本过时沿用原 `ValidationStale` 规则。

已提交的终态可只读返回原 checkpoint，已知暂停时未提交的业务效果由原 Job 重试。通用 runner 的 Step、Job、Attempt 技术结算沿用原入口，在已提交业务后可结算原结果。实际 Main hosted `SpaceProcessingJobWorker` 的登记包含 `SpaceJobType.Validate`，使用同一个 `SpaceValidationJobProcessor` 和标准 runner。验证对实际 Job 先领取再精确排序，使用相同生产 processor、runner 和 native 权威适配器。

## 实际 PostgreSQL 18.6 验证

真实 Main Kestrel、签名暂停/重新启用、原生 SQL 锁、独立数据库观察连接。租户、校验输入、开放 Info/Blocking Issue 为模拟业务。能力变化用明确的模拟 profile，正常路径用内置 WMS 能力。固定基线 `baseline-2/results.trx` 八次失败均为旧写者未共享锁。暂停先提交后旧前台仍返回 202，旧 worker 仍写出启动、正常终态或过时失败。原锁与写集在同目录 PostgreSQL 证据中。第一次夹具编译的测试标记名称错误保留在 `baseline-1/build.log`。

| 实际边界 | 暂停先提交 | 业务先提交 |
| --- | --- | --- |
| 前台入队 | 原请求 401，版本、Run、Job 不变 | 原版本进入 Validating，202 与原 Run、Job 保留。重新启用后重复原请求返回同一 Run、Job |
| Run 启动 | 原 Queued Run 无新增业务，原 Job 等待 | 原 Running 和 StartedAt 保留，校验终态等重新启用后由原 Job 完成 |
| 通过或阻止 | 原 Running、Validating 和原开放记录保持，原 Job 等待 | 通过时版本 Ready 并登记原 Info，阻止时版本 Draft 并登记原 Blocking finding。两者分别核对版本、Run 与新 Issue 的相同 native `xmin` |
| 过时失败 | 原 Run 与版本保持，原 Job 等待 | 原 Run Failed、版本回 Draft，核对相同 `xmin` 和原错误 |

上述十项均通过，没有跳过。实际 PostgreSQL relay 在终态 COMMIT 完成后丢弃 CommandComplete 的追加场景通过。Run、版本和关联 Issue 的 `xmin` 均为 1036。实际 Job 完成技术结算，签名恢复后 GET 原 Run，原生写集与 Issue ID 保持。 [两序 TRX](D:/CP6-test-artifacts/W646-01/native-1/results.trx) · [ACK TRX](D:/CP6-test-artifacts/W646-01/native-ack-1/results.trx) · [ACK 原写集](D:/CP6-test-artifacts/W646-01/native-ack-1/permission-consumption-native-ack-1/space-validation-ack-business-first-completed.json)

W604 前台刊发、重试和历史预约以及 W634 历史父链共 17 项原生回归通过。控制器、权限及 hosted worker 相关 53 项通过。每组均零跳过。W604 夹具的手工原校验 Job 采用同一 Main 适配器与独立技术账本上下文。 [刊发和历史回归](D:/CP6-test-artifacts/W646-01/publish-regression/results.trx) · [相关契约](D:/CP6-test-artifacts/W646-01/contracts/results.trx)

## 本地发布与工作区

Debug PostgreSQL 发布的 310 个文件具 SHA-256 清单，三个源码文件与实际编译时哈希一致，七个发布 DLL 与受测宿主文件逐字节一致。清单 SHA-256 `f3d0583d9e8ee199cc48438e0b147427ea1023b927e87f924525594c076d1815`。隔离 PostgreSQL 57180 和 Kestrel 57189 实际启动，`/health/live` 返回 200，匿名校验提交返回 401。暂存测试使用的 57180–57189 全部无监听，所有本任务进程已停止。 [本地发布和源码绑定](D:/CP6-test-artifacts/W646-01/publish-verified-smoke.json) · [文件清单](D:/CP6-test-artifacts/W646-01/publish-sha256.json)

`codex/w646-01` 留存可审计本地提交，DEV-1 工作树干净，席位待协调方集成后释放。本次零 Actions 和生产部署。普通 Clone、上传后终态同步和其他独立版本写入由各自任务核销。
