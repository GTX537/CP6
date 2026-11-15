# W654-01 Space 目录、位置编码及仓库模板写权限

源基线 `aaf872ec8f87b6566bcda1a7ac4b6c521f90f373`，实测提交 `2e561230d7d1a7cd20d02930eb8c7d7dedee13ec`。登记七个实际入口，共用 Main 权限根与原业务事务。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

| 写入口 | 原写集及核对条件 |
| --- | --- |
| WMS 刷新 | 真正内置 CP6 WMS 目录先完成只读查询。写入前检查当前 Site 和仓库映射以及 `model:edit` 与 `integration:manage` 两项当前权限。收录和差异问题同事务。 |
| 单笔绑定、批量绑定和布位 | 入栅后读取现有 Draft、收录行版本及目标布局。目标库位、几何、差异和 ContentRevision 按现有合同一次提交。单笔与批量复用现有绑定写者。 |
| 位置编码 Apply | 在 Main 权限根内依原楼层锁核对现行租约、楼层和内容版本及原 proposal。编码、楼层和版本、原批次回执和审计记录在同一事务写入。新提交末端仍核对原租约。 |
| 直接登记租户模板、从 Draft 登记 | 保留规范化输入和原键回执。新增模板头、版本、回执共用事务。从 Draft 新建时在栅栏内复核当前来源、状态及内容版本。 |

[WMS 刷新](../../../../CP6.Space.Infrastructure/SpaceWmsAdoptionService.cs#L129) · [编码和模板](../../../../CP6.Space.Infrastructure/SpaceDesignV1Service.cs#L2073) · [签名实库夹具](../../../../CP6.Tests/Erp/SpaceDesignCatalogWriteAuthorityPostgreSqlTests.cs)

## PostgreSQL 实测

在隔离 PostgreSQL 18.6 与实际签名 Kestrel 使用模拟登记租户、当前 Draft、合法编辑租约、真实内置 WMS 的模拟业务库位和本库编码规则。旧版 WMS 刷新两种锁序预期均失败。实际暂停能在刷新已到 WMS 收录表写入时直接提交，刷新也能在暂停占有 Main 根时返回 200 并收录原始库位。 [旧版 TRX](D:/CP6-test-artifacts/W654-01/baseline-refresh-ready/results.trx)

最终 **15 项原生验证及 66 项相关契约通过，0 失败、0 跳过**。每个入口均由独立 PostgreSQL 连接观察 `pg_blocking_pids`。业务先时，Main 暂停等同一个原业务事务，保留五项 200、两项 201 的原写入。暂停先时，七个陈旧请求均在同一 Main 根等待并返回 401，业务写集逐项相等。核对租户权限代次、C02 快照、优先 Outbox、审计和原暂停回执。启用后按原输入完成；编码和两个模板入口复读同一个原操作结果。单独撤销 WMS 刷新的 `integration:manage` 时，签名旧请求原生等待后返回 403，无业务变化。最终 TRX SHA-256 `61e2e86883fcdc37a90bfb7ce96d935035a0161a9aa53af508b1c6ea4c6d70dc`。 [最终 TRX](D:/CP6-test-artifacts/W654-01/catalog-final/results.trx) · [实库锁序及写集](D:/CP6-test-artifacts/W654-01/catalog-final/permission-consumption-catalog-final) · [契约输入哈希](D:/CP6-test-artifacts/W654-01/contract-source-hashes.json)

首轮候选发现 EF 投影跟踪范围使 WMS 绑定后的版本号未递增。修正为对写入查询整组显式跟踪。测试夹具分别补齐完整仓库模板几何、原回执单次读取及仓库外构建路径所需的原契约文件。保留实测失败和编译纠正记录。 [初始构建](D:/CP6-test-artifacts/W654-01/baseline-refresh/build.log) · [候选失败与修正前行为](D:/CP6-test-artifacts/W654-01/catalog-ready/results.trx)

## 本地发布与主线采用

本地 310 文件，清单 SHA-256 `efaff626c7073fc2d74c694ed68ef965e2a2461712d2af5108badf993b95217e`。三份受影响输入按 Git blob 对上实测提交，发布七个产品 DLL 均与受测副本匹配。真实 HTTP 健康 200，匿名 WMS 刷新和模板登记均为 401。自有 PostgreSQL、API 和测试进程已停止，57260–57269 无监听。 [发布读回](D:/CP6-test-artifacts/W654-01/publish-verified-smoke.json)

采用 Main `39923b3e5ce0f328591609e7af266c5c48aee1e9` 时，检查三个受测文件与待采用源码相同。新增 W656 的两条刊发后台边界未改这七条目录写入及其依赖。上面的实测及本地发布证据按原构建范围复用。正式 PostgreSQL 仍为 26 PASS / 1 NOT_RUN，跨库首次连接故障范围另有登记工作。
