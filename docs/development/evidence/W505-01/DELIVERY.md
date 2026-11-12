# W505-01 询盘锁序与有界事务恢复

签名 CRM BFF 保存通过实际 PostgreSQL 暂存配置图后等待 `EnqueueGates`。阻塞事务提交更新，服务端返回 `40001` 并回滚尝试，基线返回 `409 crm.configuration_concurrent_change`。原始回归 1 失败，0 跳过。 [CRM #194](https://github.com/GTX537/CP6.CRM/issues/194)

配置保存、预览、激活与审核决定在服务端确认 `40001`、`40P01` 或 `55P03` 时重新执行完整 Owner 尝试。最多三次，共享既有 30 秒请求预算并以 15 至 59 毫秒随机间隔重试。BFF 每次重新取得签名授权，直接 Bearer 每次重验原证据和当前身份、租户、用途、范围与事务内权限。三次确认回滚返回可重试的 `503 crm.lock_timeout`。实际提交后丢失 HTTP 确认仍按当前权限和原键恢复原结果。COMMIT 传输中断及完整 30 秒预算耗尽仅做源码核对。客户端取消和服务器锁等待配置沿用现有行为。

预览和审核放行实测 PostgreSQL 锁循环。逐级持有及释放七级租户身份锁，通过 `pg_locks` 核对已持有前缀，观察 Owner 等待后段写入门及被选中回滚。配置激活和拒绝实测三个不同事务 ID、三次新签名、耗尽零业务效果、解除冲突后同键成功及原结果重放。另以隔离连接的两秒 `lock_timeout` 实测三个 `55P03`。配置与审核 CAS、竞争胜者、回滚、提交后丢 HTTP 确认和撤销权限按实际执行或同输入既有证据核对。 [完整提交](../W117-01/DELIVERY.md) · [审核竞争和原结果](../W121-01/DELIVERY.md)

实际本地 Kestrel、.NET 8、PostgreSQL 18.6，模拟业务和 IAM。最终 `native-retry-final-03.trx` 7 通过，0 失败，0 跳过。`native-retry-green-02.trx` 11 通过，复用其中 6 项未受最终调整影响的检查，共 13 个不同通过用例。命令、基线失败、最终结果和服务器日志位于 `D:/CP6-test-artifacts/W505-01`，自有 PostgreSQL 已停止。

CRM #195 采用源 `32b16bc08239673a027cd81be538ef22ad30e089`，核对远端主线 `77f56582db17b2b42a41ff8c1ffe453a33831c2d` 和同一验证树。集中审查覆盖当前权限、锁序、原子性、提交不明和原键协议。准确任务分支已在本地和远端清理，W505 的 DEV-2 已释放。采纳 AC-INQ-019，原询盘 57 PASS / 3 NOT_RUN。Actions 运行与取消均为 0，部署 0。 [CRM #195](https://github.com/GTX537/CP6.CRM/pull/195)
