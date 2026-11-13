# W554-01 · 询盘范围撤权与 CRM 导出

2026-11-13 UTC。Main 基线 `19d32cf46b740d74be933ea8fc7cfa31ff07f616`，实测夹具 `dd549a28a30c0e3052645a165dcbf4f63940d433`，CRM `724570d3d7fe8e3c26615d3097c73546c5eacd63`。两套隔离 PostgreSQL 18.6，模拟租户、角色、站点表单和询盘。使用实际 Main 版本化 IAM、真实登录及 Main TLS 服务授权，以及 CRM 原隐私导出 Owner。 [PGT-017](../../acceptance/postgresql-tenancy.json#L646) · [此前角色动作撤权](../W545-01/DELIVERY.md)

从一个角色的两个有效站点和表单组合撤销目标组合，保留另一个。原 `crm-privacy:export` 动作和租户启用状态仍适用。现行 Main 通过同一个 C02 角色写者取得 CRM 身份锁并提交最低版本。 [Main 范围入口](../../../../CP6.Core/Services/Sys/IamInquiryScopeAdministrationV1.cs) · [C02 写者](../../../../CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs)

| 顺序 | 实测结果 |
| --- | --- |
| CRM 业务先持锁 | CRM 导出事务持身份锁等待目标询盘行，Main 范围变更的 CRM 封存连接由 `pg_blocking_pids` 观察到在等锁。释放后 CRM 先提交制品及审计，Main 范围变更再成功，原结果保留。 |
| Main 范围撤权先提交 | 让 CRM 持有先前 Main 签发的授权响应。在 Main 成功提交后才放行该响应。CRM 拒绝迟到授权，任务、制品、审计、业务历史和 Outbox 未产生新效果。联验过程尚未投递新 C02 事件。 |

两种顺序均核对角色当前授权版本、C02 V3 快照和同版本 Outbox、身份代次、审计和回执。同键重试得到原回执，无重复效果。Main 新签发拒绝被移除的组合，仍对保留组合签发。保留组合的新授权在 CRM 依赖最新角色投影，实际 C02 替换和重授仍待联验。 [并发夹具](../../../../CP6.Tests/Platform/Pgt017CrmExportScopeRevocationPostgreSqlTests.cs)

原生目录控制采用独立的 `service-token-v1` 冻结安装。该命令按实际库身份、安装 seal、租户映射和资格行工作。其对应业务是同一库的服务 OAuth 签发，签发 `FOR SHARE` 与控制 `FOR UPDATE` 保持两种提交顺序。CRM 询盘 Basic 权限投影使用完整 Main Core 的认证栅栏和 C02 事实，因此该独立目录的结果覆盖它自己的本地签发。 [原生安装及控制](../../../../tools/CP6.TenantStorage/README.md#service-token-tenant-directory-control-v1)

两次联验各有 Main 和 CRM 1 通过，零跳过。Main 定向另 16 通过，联验用例在单库命令显示跳过且已由双库命令实际执行。独立原生控制、真实 CLI 与签发 11 通过，零跳过，CLI 复用 W528 同输入构建。首次测试编译缺少命名空间引用，修正后构建零错误，保留原日志。任务只改测试与交付记录，产品发布范围复用 [W553](../W553-01/DELIVERY.md) 和 [W550](../W550-01/DELIVERY.md)。实际测试二进制 SHA-256、源码、TRX 及停止记录保存于 `D:/CP6-test-artifacts/W554-01`，测试实例已停止。Actions 启动、重跑及取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。Main 全写入口覆盖、普通数据库角色、两库恢复后权限水位、实际 C02 重授及滚动采用仍待核对。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012) · [Main 原余项](../W531-01/DELIVERY.md#仍待核销的正式前置)

Main 最终分支与固定席位处置见本次 PR 和 `main-finish.json`。CRM 复用已采用源码与制品。
