# W375-01 · Core RMA 托管原操作

2026-11-09 10:40:37 UTC 在 DEV-2 开工，分支 `codex/w375-01`，登记基线 `e96dbe7fd484418925e014a396d30fb11ff7204b`。验证后将任务基线快进至 #823 `1eb00c4914d1893b879c2e43f860a7a6396f5492`。这段主线变更涉及归档与文档，验证输入逐路径核对一致。 [W372 提交](../W372-01/DELIVERY.md) · [W361 原 journal](../W361-01/DELIVERY.md)

## 交付

`RmaReadTenancy:OriginalOperationsEnabled` 启用现有 `GET /api/wms/rma/native-applications-v1/operations/{id}` 和 `POST /api/wms/rma/native-applications-v1/operations/{id}/close`。GET 走普通只读事务和原 `query` 授权。CLOSE 用显式 `wms-rma:close-write`，普通事务先取得既有原认证锁，再查原件。已有操作使用当前 `query` 与原 actor 条件，COMMITTED 披露继续核共享出库可见性。缺失 close 使用当前 `cancel`。 [启用说明](../../../../tools/CP6.TenantStorage/README.md#hosted-core-rma-original-operations) · [Owner](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs)

新 CLOSED 使用现有 W361 固定 Owner 写函数，传入类型明确的空 input、空 digest 和空对象键，保存原合同结果字节。与草稿和提交使用同一普通事务机制和原认证锁，迟到的写请求在锁后消费已保存的原件。沿用原租户目录、数据库权限、CLI、Ready/history 及既有未知提交恢复。 [权限与锁顺序](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.RmaDraft.cs) · [普通事务](../../../../CP6.Core/Persistence/Tenancy/TenantRmaReadStore.Draft.cs)

## 实际验证

同一个隔离 Windows PostgreSQL 18.6 环回库，55567，OID 16386，system identifier 7706122959400573996。真实发布 W351 DatabaseInit `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` PID 61008 exit 0，迁移历史逐项匹配。保存的 W372 CLI 真实准备、commission 并安装 journal、draft、submit。两个普通 LOGIN、三个业务 actor 和出库数据均为模拟。 [复现入口](../../../../eng/wp02-02/rma-tenant-migration/verify_operations.py)

| 场景 | 实测结果 |
| --- | --- |
| 查原件与当前权限 | A/B 相同操作 UUID 可各自得到正确结果，B 查询 A 的独立 CLOSED 键为 UNKNOWN。同租另一 actor GET 和 POST 均 403。撤掉当前出库权限后 COMMITTED 查询及 close 均拒绝，自己的 CLOSED 仍可查 |
| cancel 与 query | 缺失 close 需要 cancel。有 query 的 B 可通过 POST 获取已有 COMMITTED，但关闭缺失操作 403。保有 cancel 的 A 撤掉 query 后，已有原件的 GET 和 POST 都 403，新的缺失操作仍成功关闭 |
| close 先到 | 两个独立操作的 A close 已取得原认证锁并到达真实 journal 触发器。普通 PID 56736 持锁，迟到草稿或 submit PID 33832 实际等待。释放夹具锁后，close 成功，迟到请求返回 409 `ORIGINAL_OPERATION_CLOSED`。草稿零行，原 submit 目标仍仅有 v1 |
| submit 先到 | B 实际 submit 的普通 PID 28248 持有原锁，晚到 close PID 25168 实际等待。随后 close 返回已提交的原 v2 结果与 ETag，原 journal 字节匹配。A 同键 CLOSED 保持 |
| 实物和原回执 | 原生记录 A 有 3 CLOSED 与 1 COMMITTED，B 有 2 COMMITTED。CLOSED actor、合同、NULL input/digest、空对象数组和结果字节精确。A/B GET 实际用普通只读事务查 journal。保存的 W372 CLI 对四个原控制步骤逐字节回放，两份 Ready/history 一致 |

完整实际原件为 `D:/CP6-local-test/CP6-72H-20261109/W375-01/rma-operations-20261109T105247-cb389199/operations`。`rma-operations.trx` 记录 1 通过、0 跳过和三次真实等待，`native-original-operation-statements.log` 记录实际普通 SQL，`final.json` 于 10:53:50 UTC 写入。`source.json` 记录以下实际 SHA-256。

| 二进制 | SHA-256 |
| --- | --- |
| CP6.WebApi | `ef722152f09bcf717be3e79156b864f20fbf6da5ed365d8f5deaefe3b9d10b91` |
| CP6.Core | `b4734aa5c0ca87f4df06179b6701b659745106a0ac07ffd2b8e994d268718b5b` |
| CP6.Tests | `a33d97675a9dc8f54cd66dc05a5616d5127cceb150d347274f667297b46b44e3` |
| 已保存 W372 CLI | `4288eb11a0c42962dad22cdcd8c26047a6e0c6f004aa5f03d62023c6133df10c` |

只在 DEV-2 稳定缓存做一次 .NET 8.0.424 单节点 Release 构建，复用锁定还原，实际 5:03、0 错误。旧默认 profile 的操作拒绝沿用 W359 与 W372 原验证。真实 HTTP PID 57996 与 postmaster 3800 已停止，62183 和 55567 均无监听。实测范围为本地托管读取、关闭、权限、原件及竞态。未知提交沿用既有协议。Actions 和部署均为零。候选分支与干净 DEV-2 交 COORD 集中审查、远端采用和席位释放。 [W359 原门禁](../W359-01/DELIVERY.md)
