# W414-01 · FileContent 候选文件落盘

DEV-1 于 2026-11-09 17:22:16 UTC 开工，基线 #850 `cb1ca74e5cd3d9929b5c12b1767e49d4757fcbe2`。本窗只处理已暂存归档引用的外部 FileContent 文件。[文件入口](../../../../eng/wp02-02/rma-tenant-archive/materialize_files.py) · [归档资产合同](../W389-01/DELIVERY.md)

## 操作

`materialize` 接受原 `STAGED` 回执文件及操作者独立固定的 SHA-256。保留原字节，包括末尾 LF。回执绑定精确物理暂存库、租户、归档格式和 manifest 摘要。工具再按该 manifest 核 `file-content-assets.csv`，复用原租户、存储键和长度合同。v7 至 v10 可用，未消费的归档成员沿用同一回执所对应的既有暂存校验。[原暂存入口](../../../../eng/wp02-02/rma-tenant-archive/stage.py)

```powershell
python -B eng/wp02-02/rma-tenant-archive/materialize_files.py materialize `
  --bundle $VerifiedBundle --stage-receipt $OriginalStageReceipt `
  --stage-receipt-sha256 $ExactStageReceiptSha256 --tenant $TenantId `
  --source-root $PrivateOriginalLocalStore --output $NewPrivateCandidate
python -B eng/wp02-02/rma-tenant-archive/materialize_files.py recover `
  --bundle $VerifiedBundle --stage-receipt $OriginalStageReceipt `
  --stage-receipt-sha256 $ExactStageReceiptSha256 --tenant $TenantId `
  --output $NewPrivateCandidate
```

选用受操作者控制的本地绝对路径，目标父目录预先存在且在执行期间由操作者独占写入。目标与源文件根、归档包均不得互相包含，路径逐级拒绝重解析。目标文件按原 `native-content-v1/<tenant:N>/<key:N>` 保持目录和文件名。在打开原件后以最多 1 MiB 的块读取，同一批字节同时用于长度和 SHA-256 校验以及排他写入。完整文件和原回执在同父目录的私有暂存树里写完并同步，再排他保留目标目录，一次重命名发布 `ready`。发布目录及其子项由当前操作者独占。现有目标只有在原键回执与文件树都精确相符时才可回读。

恢复会检查完整的已发布树和每个文件，返回 `ready/receipt.json` 原字节。恢复仍需要同一份固定的暂存回执、manifest 和资产清单，可以在外部源文件根不可用时执行。发布前的失败可在同一新目标重试。若进程恰在保留目录后、发布前中断，空目录会明确拒绝恢复或接管。确认该目录的归属及没有执行进程后可定向处理，或直接用另一个新目标。强制终止可能留下私有随机暂存目录。工具只自动清理由本次运行创建的暂存目录。

## Windows 文件实测

W412 原件来自 `D:/CP6-local-test/CP6-72H-20261109/W412-VERIFY/native-01` 的 `v10-cut/bundle`、`recover-v10.stdout`、`private-local-store`。原暂存目标为 `127.0.0.1:55567/w412_neutral`，OID `43614`，system identifier `7706139311168619192`。原租户 `938f243e-9c7a-4e22-bf1a-d7e8ea1e6eca`。回执原文件 9,383 字节，SHA-256 `6b3a2ea4e97c15e3b49c58cd5b0630fca103d899d286cd31af0c0c62c92cb7b2`。manifest SHA-256 `974ba915760c13a62494563633251ec8c3fa324ffe06ceba3a0e12e8fdb48505`，资产清单 SHA-256 `4a7b9ee36534d1782ae27b7c3564fefcd1dca564947c850eacea863757f55179`。

- 精确 #850 上执行同一拟新增 CLI 得到真实退出 2，旧入口缺失。`test-red.stderr` 和 `test-red.exit` 保留原结果。最终 `test-files-02.exit` 为 0，实际 Windows Python 文件验证 7.34 秒。原件在 `D:/CP6-local-test/CP6-72H-20261109/W414-01/materialize-e276c71a6994`，汇总 `actual.json` SHA-256 `f4240416f3d44ebb1eca66a10d2261afeed053d43979db5e7bc3fe0bc4914f57`。[文件夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_materialize_files.py)
- W412 的原合成 A 文件按 `native-content-v1/938f243e9c7a4e22bf1ad7e8ea1e6eca/334209458df347bb9def74e906a6f87d` 实际写入 41 字节，SHA-256 `8defcd1b1805550186c541d618e2a29851df8e624b7647d76826ed10d3811fe8`。同键重复及恢复与持久回执逐字节相同，SHA-256 `53ca672b95873d12863eae2bf17bec6ef103a9d7a19ad227dd8681a3b98a9435`。`windows-candidate-acl.log` 证实目标根继承已阻断，根、`ready`、`files` 均只有当前 owner 的 Allow 项。
- 同长度错误源文件、回执错摘要、错租户、修改过的资产清单、父目录跳转、源和目标嵌套、Windows junction 及已占用根均拒绝。实际原 W412 输入摘要前后一致。私有合成 B 同名文件的字节保持，输出只含 A。两条真实 CLI 竞争同键，实际退出为 1 和 0，失败方报告空占位，成功方只发布一次。原外部源移出其私有测试路径后仍可读取竞争结果的原回执。
- 暂存写完后、保留目录前注入失败，目标未出现。按同键重试并丢弃 stdout，随后恢复的原回执 SHA-256 `acd130b3f0084e5299b9040208136a2d7ef39ec84a6abda7bbd7ec088de36e59`。用同长度错误内容破坏该测试输出后，恢复拒绝。另一次在保留目录后、重命名前注入中断，留下空占位，原键重试和恢复都明确拒绝。常规故障处理结束后无随机暂存目录残留。
- 既有 `archive.py verify` 用 W412 原根退出 0，用同长度错根退出 1 并报告摘要不符。沿用 W395 v8 零资产包及 W398 原暂存回执执行两个新 CLI，退出均为 0，输出 `ready/files` 为空且回执原字节一致。原始进程输出和退出记录与本窗证据同根。[W395 原 v8 包](../W395-01/DELIVERY.md) · [W398 原 v8 回执](../W398-01/DELIVERY.md)

本窗只运行 Python 和 Windows 文件工具，所有测试进程均已退出，`ready/files` 是关闭的候选目录，无服务接线。验证覆盖 Windows 文件系统上的发布、进程故障和回包丢失。POSIX 及突然断电未实测。回执为 `MATERIALIZED`，`recoveryReady=false`。原 FileContent Owner 事件/操作历史、实际数据库 Owner 元数据、Engineering 父版本原内容、运行时存储接线、现行权限和路由审批仍由独立恢复流程处理。PGT-021/022 保持 `NOT_RUN`。[正式验收卡](../../acceptance/postgresql-tenancy.json)

本地分支 `codex/w414-01`。集中审查、合并及 DEV-1 官方释放交 COORD。
