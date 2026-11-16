# W676-01 · RMA 存储 v2 兼容归档恢复

2026-11-16 UTC。原 WP02-02 接续窗口，基线 `2ad52836b4e48ae669aba4046f5daad206fc956a`，COORD、固定 DEV-1、`codex/w676-01`。直接前置 [W675 对象归属](../W675-01/DELIVERY.md) 已核对远端采用。原生验收实现提交 `0fb6175b35adff5c953ef4a0ec9ff77e6e4865d7`；审查修正提交 `f6f0b93cbb0a95b1c40a6ff263c1d008f36f968a`。

**存储 v2 兼容归档和同源缺失行恢复已通过实际 PostgreSQL 验收。** [W674 整卡复核](../W674-01/DELIVERY.md) 的两个实现余项均已有对应交付：W675 当前对象归属，本轮 v2 恢复。原 WP02-02 进入 VERIFYING，待正常 PR 合并并核对远端主线回执后登记整卡 DONE；不提前记录远端采用。

## 实现

- 新增完整归档 `cp6-rma-source-archive-v12`，显式保存存储 v2 第二条迁移原件及版本。新选项必须配合完整共享切片；Stock 导出器在同一 PostgreSQL 导出快照内收集原件并绑定新归档摘要。默认 v1 路径继续拒绝 v2。
- Core、原日志、ERP、FileContent、Stock 的关闭候选导入与现行对账识别 v12。候选保留自己的安装身份、v1 数据模型和关闭状态，不因源模型改变而迁移或激活。独立空库中立 staging 明确拒绝 v12，使用正式关闭候选路径。
- 同源恢复核对原物理身份、初版安装和当前 v2 原迁移。数据库 Owner 在原认证根、租户锁和当前事务内登记 v2 写者许可；成功清理，失败与数据/回执一起回滚。v2 两表及日志 ALWAYS 守卫保持启用，普通角色不新增登记权限。原 v1 日志恢复器对旧兼容触发器的既有处理不变。
- 修复 [Issue #1198](https://github.com/GTX537/CP6/issues/1198)：Stock 候选固定检查旧来源守卫，误拒当前 Main 的终端 epoch 迁移。现在按已安装迁移选择对应准确守卫；未知正文仍拒绝，原单次 ADJ/epoch 1 数据范围不扩大。

[操作与支持边界](../../storage/rma-storage-model-v2-recovery.md) 和应用兼容矩阵已同步。本轮不修改业务表、Owner、数量精度、生产配置、数据库迁移或共享包版本。完整 schema/database 毁损、跨库迁移和生产切换不在原恢复单位内。

## 实际验证

环境：Windows、PostgreSQL 18.6、.NET SDK 8.0.424/runtime 8.0.30。源库 `127.0.0.1:55567/w676_source`、候选 `127.0.0.1:55598/combined_v11_candidate` 均为本轮隔离测试实例，HTTP 使用 58276。业务输入全部为明确模拟数据。

旧演练 PGDATA 已清理。本轮从保留的 W520 模拟 SQL 数据与结构重新建立源实例，排除旧物理安装/迁移/权限覆盖层，由实际工具重新生成 prepare、commission、journal/draft/submit 安装和 v2 迁移原件，并应用当前 Main 迁移。未改写旧证据，未把旧安装回执绑定到新库。输入与处理范围在 `native-08/fixture-provenance.json`。

| 行为 | 实际结果与证据 |
| --- | --- |
| 当前 Main 普通用户 v2 读写 | `native-08/current-main-before`：实际登录、原申请/来源读取和 draft POST 成功，数据库 Owner/普通 LOGIN 分离。 |
| 同快照完整归档 | `native-08/export`：41 个成员，2 heads、3 versions、2 history；同快照 Stock 原件绑定 v12 归档摘要。原字节、typed COPY 与外部文件原件保留。 |
| 旧路径兼容 | `native-08/v1-denied` 拒绝 v2；B 的实际 v1 导出通过；旧完整 v11 原包及资产验证通过，旧包未修改。 |
| 完整关闭候选 | `native-08/candidate`：Stock、ERP、Core、Core 原日志、FileContent 五项实际导入均返回 IMPORTED / recoveryReady=false。 |
| 同源恢复 | `recovery-12` 恢复 2 heads / 3 versions；源业务状态、B 数据、迁移原件和全局行保持；同键重试与 recover 的结果字节相同，automaticSends=0。 |
| 中途失败与当前安全状态 | `recovery-13` 执行仓库内 `verify_storage_model_v2_native.py`：写入后真实 SQL 故障，数据/许可全回滚、无失败键回执；成功后 RMA 原件、两租户迁移、共享日志、Sys_Users 和 Sys_RoleAction 的 PostgreSQL 摘要保持。归档后撤销 add，恢复后普通 GET 成功、POST 403。 |
| 租户隔离与私有许可 | `recovery-12/source`：普通 A 不能写 writer_transactions，普通 B 不能读 A 的 v2 视图；失败和成功后许可表均无遗留。 |
| Stock 原因定位与回归 | `native-07/candidate/stock-import` 为红例；`stock-diagnosis/comparison.stdout` 在同一失败实例仅替换已知守卫摘要，完整拒绝表达式由 true 变 false；`stock-regression` 执行仓库内回归，当前迁移通过，未知正文失败并回滚，随后合法守卫仍通过。 |

统一私有证据根：`D:/CP6-test-artifacts/W676-01`。演练驱动为 `exercise676.py`、`resume676.py`、`verify_stock676.py`。成功导出/候选复用 native-08，恢复接续 recovery-12/13；未因驱动适配而重复全部已通过流程。早期夹具构造、旧 helper 导入路径和调用适配失败保留在 native-01..08/recovery-09..11，不记作通过。最终 terminal 记录 ExitCode=0、Processes={}，PostgreSQL/HTTP 已停止。

实际命令：

```text
uv run --offline --python 3.13 python -X utf8 -B -m unittest verify_storage_model_v2 verify_live_recovery_csv
dotnet build tools/CP6.TenantStorage/CP6.TenantStorage.csproj --artifacts-path D:/CP6-test-artifacts/W676-01/build -c Debug -m:1 --nologo -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false
uv run --offline --python 3.13 --with bcrypt --with requests --with cryptography --with psutil python -X utf8 -B D:/CP6-test-artifacts/W676-01/exercise676.py native-08
uv run --offline --python 3.13 --with bcrypt --with requests --with cryptography --with psutil python -X utf8 -B D:/CP6-test-artifacts/W676-01/resume676.py recovery-13
uv run --offline --python 3.13 python -X utf8 -B D:/CP6-test-artifacts/W676-01/verify_stock676.py
```

6 项外部格式/绑定及 CSV 边界测试通过。集中审查修正 ERP 候选的格式集合，保留非 history 入口只接受 v10 的原限制；新增 `verify_storage_model_v2.StorageModelArchiveTests.test_erp_candidate_keeps_original_format_boundary` 单项实测拒绝旧 ERP-only 格式、接受 v10/v11/v12。该测试首次因测试模块引用名错误失败，修正引用后通过；不记作业务失败或隐去记录。此次集合收紧不改变已验收的 v12 路径，复用原生结果。

租户工具构建 0 error / 23 warning，原警告保留。Main 实际宿主与 DatabaseInit 复用 W673 已采用制品 `publish/package`，源码 `e2e0e4ea8216596d01f639dcedaaa1f9148c7410`；业务二进制及迁移到本轮基线未变。新的 Python 实现/回归来自本轮源码；兼容矩阵的恢复说明不改变宿主读写行为。

## 整卡与交付

复用 W674 逐项核对的本卡 17 项 PGT 正式证据和 W675 对象归属，不重复已通过场景、不把元数据清单当授权证明。原设计的可信映射、模型缓存/连接池、初始化版本、运行/迁移分权、跨租拒绝和已采用恢复路径均有对应实证。本轮不扩大业务布局，不宣布全库一租户一 schema 或其他业务卡/125 个目标完成。

正式 PGT 保持 27/27，原询盘保持 60/60。WP02-03 仍 PLANNED，下一窗口在远端采用后完成 WP02-02 结项并按原卡复核 WP02-03。台账、生成页、协作入口和四份状态文件同步。已完成一次任务级完整 diff 审查，修正上述格式边界并复查修改部分；无未处理的合并阻断项。台账审查确认只有 WP02-02 与当前调度发生状态变化，历史登记保持。

工作流沿现行仅手动/版本 Tag 条件，本分支 push/PR 不命中过滤。Actions 启动、取消、触发修改及生产部署均为零。本地模拟业务验收与部署状态分别记录。

正常 PR 合入并核对远端后，工作区管理器按准确提交收尾，定向清理本任务分支、释放 DEV-1。实际分支/工作区/席位结果见本轮 PR 和 `D:/CP6-test-artifacts/W676-01/main-finish.json`；根目录用户内容与历史分支保持原状。
