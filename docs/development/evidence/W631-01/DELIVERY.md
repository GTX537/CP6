# W631-01 采购三单匹配共用 Main 提交边界

修复 Main #1152，归属 PGT-017。开发基线 `4de40010bbf87fba4c6fc9f9d8f32bc05dd07447`，已验证源码 `55f431199063da6b52b9e4de0788e95c46f3256e`。采用 W629 的真实 Main Kestrel 与 PostgreSQL 原失败。旧全局匹配号约束曾导致合法第二租户返回 HTTP 500，已留下原 PO 的 Posted AP 和 GL，原 PO 仍为 Received、开票量 0 且没有匹配记录。[Main #1152](https://github.com/GTX537/CP6/issues/1152) · [W629 交付和原结果](D:/CP6-test-artifacts/W629-01/DELIVERY.md) · [首轮 PostgreSQL 回读](D:/CP6-test-artifacts/W629-01/authorized-purchase/initial-native-state.json)

## 实现

- `POST /api/pur/match`、`POST /api/pur/match/{matchNo}/release` 和 `POST /api/pur/match/{matchNo}/reject` 使用原 `pur-match:add`、`pur-match:release`、`pur-match:reject`。Main 原生授权锁与同一 Core 连接上的匹配、采番、AP、凭证、PO 行和 PO 状态共用提交。自动匹配保留原 HTTP 200 挂起合同，AP 子步骤返回业务失败时回滚其保存点。放行的原业务错误仍返回 HTTP 400，整笔回滚。[HTTP 入口](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.WebApi/Controllers/Pur/ThreeWayMatchController.cs:35) · [采购事务步骤](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Pur/ThreeWayMatchService.cs:119)
- PostgreSQL 主表以 `(TenantId, MatchNo)` 确立匹配号。明细按同一组合外键关联并保留原级联及行号唯一性。新图在进入 EF 前由当前租户赋值，正向迁移先验证新外键再删除旧全局键。历史明细与其主表的租户不匹配时，迁移会原子终止并保留原行。[正向迁移](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Persistence.PostgreSql/Migrations/Core/20261115161100_PurchaseMatchTenantIdentityV1.cs:9)
- 同租户原供应商发票只在同 PO、可唯一回接的原明细、原币种/汇率、日期及数量金额相符时采用。原 Posted 复用实际 AP 和自动凭证。原 Draft 按持久行继续过账，已有同源已过账凭证时采用原凭证。无法确认归属时返回既有重复发票代码 E-FIN-201，未产生新的财务效果。省略发票日期且匹配到已证实的历史操作时沿用原日期。[财务原键续办](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Fin/ApInvoiceService.cs:190)

## 实际验收

隔离 PostgreSQL 18.6 使用 57051，真实 Main Kestrel 使用 57057。模拟供应商、采购单、实收及已验收行、会计期和总账模板。业务请求通过正式登录取得签名会话，财务和采购写集由独立 PostgreSQL 连接回读。集中运行 29 项，失败和跳过均为 0。验证复用了 W629 对相同金额和原路由的基线，无需再次运行旧缺陷。[原始 TRX、等待链及前后数据](D:/CP6-test-artifacts/W631-01/candidate-first/results.trx) · [命令和源码对象](D:/CP6-test-artifacts/W631-01/candidate-first/invocation.json) · [定向夹具](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Tests/Erp/Pgt017PurchaseMatchPostgreSqlTests.cs:22)

| 验证 | 实际结果 |
| --- | --- |
| 三个动作各两种提交顺序 | 业务先在真实匹配表 INSERT 或 UPDATE 等待时，签名租户暂停在 `cp6_authentication_v1.acquire()` 等待该事务。业务随后 200，原业务写集一次落账。暂停先在真实 `Sys_Tenants` UPDATE 等待时，业务在同一原生锁等待，随后返回 401，独立快照与原值完全一致。租户重新启用并正式登录后，原请求正常完成。三组顺序均观察到关系锁及事务 ID 等待。 |
| 正常通过、放行、拒绝 | 通过的 AP 净额 30.85、进项 3.09、总额及凭证借贷各 33.94。放行复用原匹配号，AP 净额 34.18、进项 3.42、总额及凭证借贷各 37.60。两条成功的 PO 原行都累计开票 2.5 并关闭。拒绝只改变挂起匹配的状态、办理人和原因，原 PO、AP、GL 及采番保持原值。 |
| 双租户同匹配号 | 两个正式登录的租户均写入 `TWM202611150001`，分别以同号实际 GET 取回自己的匹配和明细，AP 和匹配 ID 各自独立。 |
| 正常业务挂起 | 开放期改为锁期后，匹配返回原 200 和 `AP build failed: E-FIN-112`。只保留原匹配、子行及首次 TWM 号，AP 草稿、GL、财务采番和 PO 保持原值。锁期内放行返回原 400 和 E-FIN-112，解锁后用相同匹配号正式放行，生成一次 AP、GL 并关闭原 PO。 |
| 实际最后一次 PO 保存失败 | 隔离数据库的触发器确认同一事务已写入 Passed 匹配、原 PO 行累计 2.5、Posted AP 和 Posted GL 后，拒绝最后的 PO 表头 UPDATE。HTTP 503 `AUTHORITY_FENCE_UNAVAILABLE`，快照完全恢复到请求前，采番也未推进。移除夹具触发器后，原请求以首号成功。该夹具使用正常金额和有效数量。 |
| 旧部分提交的收敛 | 精确旧 Posted、Draft、Draft 已存在同源 Posted 凭证三种沿用原 AP ID 和编号。已存在凭证及财务采番保持原值，无凭证 Draft 只生成一次凭证。无 PO 关联的旧 AP 和同 PO 中无法唯一识别的同物料明细各自返回正常挂起，原财务及 PO 没有变化。成功续办后重复提交返回 E-PUR-043，完整快照不变。 |
| 原目录迁移 | 另建隔离测试库，到旧 Main 迁移 `20261115140500` 后写入既有挂起记录及明细，再升级至新迁移。旧主子键、租户、8 位小数量、4 位单价和原行版本均逐行保留。系统目录和发布冒烟回读确认组合唯一键、已验证的组合外键及原级联。 |

[最后保存失败的 PostgreSQL 原日志](D:/CP6-test-artifacts/W631-01/candidate-first/permission-consumption-candidate-first/postgres.log) · [实际测试程序集哈希](D:/CP6-test-artifacts/W631-01/candidate-first/tested-assemblies.json) · [测试执行脚本](D:/CP6-test-artifacts/W631-01/run-candidate.ps1)

## 本地发布和工作区

已发布与定向测试逐项同 SHA-256 的 WebApi、Core 和 PostgreSQL 迁移程序集。发布清单覆盖 310 个文件，清单 SHA-256 为 `830766f465cc4d4bb341bb8ac1e0829a0cc69f12ff396c9c1a1f36a53908500f`。实际发布进程在隔离端口 57058 运行，`GET /health/live` 返回 200，匿名 `POST /api/pur/match` 返回 401。冒烟脚本初版的目录检查表达式及 SQL 括号已纠正，最终冒烟使用已验证且哈希一致的同一次发布文件。[发布清单](D:/CP6-test-artifacts/W631-01/publish-sha256.json) · [发布冒烟和源码 SHA](D:/CP6-test-artifacts/W631-01/publish-smoke.json)

同步核对到 `origin/main` 的 `3bc198ff25d14ddf98a397089303afdbfefe03a3` 只增加 W627 文档与台账。任务分支 `codex/w631-01` 已在本地保存，DEV-1 工作区干净。范围端口 57050–57059 无监听，本任务 PostgreSQL、Kestrel 和测试进程已停止。协调窗口负责远端采用、Issue 处置和席位释放。外注成品成本及其他跨域财务流程仍归对应任务。本地证据仅用于此次开发验收。
