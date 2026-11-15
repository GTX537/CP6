# W643-01 WMS 出库到 ERP 的共同事务栅栏

Main 开发基线 `db6c58fd89006524201d2578b6ffc890525e0712`，受测源码 `7f3684dcfae18867eba8bccfa2025179e72f8567`。W644 在原真实合法 HTTP 出库中回读两个订单明细各 2.5，确认实物出库、ERP 订单和成功事件分别保存为三个 PostgreSQL 事务。 [缺陷 #1163](https://github.com/GTX537/CP6/issues/1163) · [W644 实测](D:/CP6-test-artifacts/W644-01/DELIVERY.md) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

原出库业务保留出库号、订单与实收数量合同。具有原订单的出库在本次 Core 事务暂存含同租户原出库 ID 和原订单号的 WMS→ERP 意图。实际正常请求可在已提交后尝试过账，实际后台由原事件恢复。处理器先取得 Main 根，读取当前有效租户和原同租户事件，锁原行，核对原出库事实。ERP 订单、明细、原事件状态和尝试次数使用同一提交。保留按产品顺序分配的规则，采用实际出库确认的时间。提交前再次核对租户期限，可处理的失败撤销业务保存点，沿用同一原事件的结果状态和退避。原开票阶段独立沿用 W620。 [出库入口](../../../../CP6.Core/Services/Wms/OutboundService.cs#L599) · [原生处理](../../../../CP6.Core/Services/Wms/ErpBridgeHook.ShipmentOwner.cs) · [真实 Worker](../../../../CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs)

原格式事件回读原同租户出库及原订单。有已提交的原明细标记或准确原成功事件时按原结果结算，不新增原数量。已有较晚写入且不能确认原效果时，保留明确的未知结果供原恢复机制处理。 [原回写和旧状态处理](../../../../CP6.Core/Services/Wms/ErpBridgeHook.cs)

## 实际 PostgreSQL 验收

隔离 PostgreSQL 18.6、真实签名的 Main Kestrel 和独立库连接。模拟租户、订单、库位、已分配库存、原业务权限及账簿。3 项新原生、4 项 W620 实库出库阶段和 11 项组件，共 18 个不同用例通过，零跳过。 [两种提交顺序](D:/CP6-test-artifacts/W643-01/verified-races/results.trx) · [原旧格式回收、前序和相关回归](D:/CP6-test-artifacts/W643-01/candidate/results.trx)

- 两组均在原签名出库的 `T_IntegrationEvent` INSERT 上实际阻塞，独立连接仍看到分配状态、原订单与空意图。释放后由原生协议夹具丢失真实出库 COMMIT 回执，原请求报告 `OPERATIONS_OUTCOME_UNKNOWN`。独立连接看到实际出库完成，且同一原出库 ID 对应唯一 Pending 意图，Attempts 0。
- 暂停先在实际租户 UPDATE 持有 Main 根，后台等待该根。暂停提交后读回原实物和原意图，ERP 订单、明细和结果不变。重新启用后，原事件一次完成。
- 回写先在实际订单 UPDATE 持有 Main 根，签名暂停等待。回写先提交后可读到订单状态 9，两条原 ERP 明细各 2.5，事件相同 ID、相关号、源号、操作者、入参，Attempts 1，状态 SUCCESS。订单头、两行和原结果同一可见事务版本。核对签名暂停原回执、权限代次、快照、审计和 Outbox。再次扫描不改变原写集。
- 原正向出库实际成功后，以历史旧 JSON 和同一事务状态模拟原回执未知。实际 Worker 沿用原事件和已有出库关联标记，读回相同 ERP 原行版本及实物，重复扫描写集一致。

首轮两个新并发夹具误把原出库与意图必须有相同的 `xmin` 当作原子条件。原出库在同一提交内还有后续写入，可能有不同子事务版本。最终在实际事件 INSERT 阻塞和精确原生 COMMIT 边界分别核实可见范围。初轮 16 项通过的三个产品 DLL 与最终复测逐项相同，复用实际结果。 [初轮命令](D:/CP6-test-artifacts/W643-01/candidate/invocation.json) · [最终命令](D:/CP6-test-artifacts/W643-01/verified-races/invocation.json)

## 本地发布

清单覆盖 310 文件，SHA-256 `193a1b3dcfee7442ca592c29b3e984e4112945b13e625ecb0b1c0cbfa4b34db6`。三份发布 DLL 与实际测试逐项相同。隔离 PostgreSQL 57172 及 Kestrel 57178 实际运行，活性 200，匿名出库 401。任务进程和端口已停止。 [发布冒烟](D:/CP6-test-artifacts/W643-01/publish-smoke.json) · [哈希清单](D:/CP6-test-artifacts/W643-01/publish-sha256.json)
