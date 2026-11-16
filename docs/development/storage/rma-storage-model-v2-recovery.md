# RMA 存储模型 v2 归档与同源恢复

W676 为已采用的 `rma-application-core-tenant-v2` 提供 `cp6-rma-source-archive-v12`。存储模型版本与归档格式版本独立：存储 v1 的既有完整格式仍为 v11。实际验收见 [W676 交付](../evidence/W676-01/DELIVERY.md)。

## 范围

恢复单位沿用原 v1：在原物理源库补回缺失的 RMA heads/versions，核对其完整共享依赖，并保留当前 IAM、会话、凭据、消息与文件状态。已有但更新或分歧的行仍拒绝覆盖。跨库迁移、整个 schema/database 毁损、重新安装 v2 和生产切换属于另外的任务。

v12 保存完整 v11 成员及同快照的第二条租户迁移原件：`storageModel` 包含明确模型、迁移、最低共享版本和原 `result_json` 文本。原初版安装身份及迁移不改写；第二条迁移必须同时对应共享 `cp6_rma_model_v2.tenant_migrations` 和该租户历史。两张 `security_invoker` 视图、普通角色权限和 ALWAYS 写者守卫按已采用布局核对。

## 操作顺序

1. 使用真实、已核对的物理目标、安装 ID、租户和私有资产根。完整 Stock/RMA 同快照入口是 `eng/wp02-02/rma-tenant-archive/stock_adjust_originals.py`；在原参数上增加 `--include-rma-storage-model-v2`。它仍在同一个 PostgreSQL 导出快照内生成 `bundle` 和 `stock-effects`，Stock 原件清单绑定新归档摘要。
2. 单独使用 `archive.py export` 时，同样显式增加 `--include-rma-storage-model-v2`，并启用从 `--include-original-journal` 到 `--include-file-content-history` 的完整依赖链。缺失完整切片立即失败。默认入口继续拒绝存储 v2，不能用旧格式隐式降级。
3. 在已正式安装且保持关闭的候选中，沿既有顺序执行 Stock、ERP、Core、Core 原日志和 FileContent 导入。Core/ERP/FileContent 继续使用 `--include-file-content-history`，现在可识别 v11/v12。候选拥有自己的安装身份和初版模型，导入 RMA 数据不会把它升级、激活或伪装为原源库。
4. `live_recovery.py apply` 沿原参数接收源/候选物理目标、各自 commission 原件、候选 ID、归档/Stock 原件固定摘要、资产根及原恢复操作键。v12 使用 `cp6-rma-live-missing-rows-recovery-v2`，复用原事务回执表。同键同输入返回原结果；不确定提交用 `recover` 读取原件。

独立空库的 `stage.py stage` 不接受 v12。v2 完整支持路径是上述正式关闭候选和同源恢复；不执行缺少共享安装函数的原生整 schema 导入。既有 v1 中立 staging 不变。

## 事务和权限

原数据库 Owner 在当前认证根与租户锁内核对当前 v2 原件。随后仅为当前租户与当前 xid 登记 `writer_transactions`，恢复写入完成后在同一事务删除登记；异常回滚同时撤销数据、操作回执和登记。v2 写者触发器保持启用，普通角色没有登记表写权限。原 v1 journal 导入器对其旧兼容触发器的既有处理保持，不能把它描述为所有触发器从不临时调整。

Stock 候选沿目标已安装迁移选择准确的来源守卫版本：旧初版或 `20261113073000_MainStockOutboxTerminalEpochV1`。未知函数正文仍拒绝，旧版正文不能替代已登记的新版本；导入器原本的单次 ADJ、关闭候选和 epoch 1 数据范围不因此扩大。见 [Issue #1198](https://github.com/GTX537/CP6/issues/1198)。

生产部署、环境切换和 Actions 触发仍分别需要相应授权。本地模拟数据验收不表示已部署。
