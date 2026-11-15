# W630-01 CAD 提供者站点配置写入授权

2026-11-15 UTC。CP6 Main 基线 `3bc198ff25d14ddf98a397089303afdbfefe03a3`，受测产品及回归源码 `701b7af875c811a84bb18eb58eacd407d2d49808`。 [缺陷 #1150](https://github.com/GTX537/CP6/issues/1150)

`PUT /api/space/design/v1/sites/{siteId}/cad-provider-configuration` 在原有 Serializable 事务内复用 `SpaceDesignWriteTransaction`。原生写入先取得 Main 授权根并检查当前 `space:model:provider:manage`，提交前在同一事务中核对原签名操作者和会话。原站点锁、修订、认证历史、三次序列化尝试与提前回读原幂等回执沿用现有合同。

## PostgreSQL 与签名入口

- 在隔离的 PostgreSQL 18.6 与真实 Main Kestrel 上使用模拟租户、管理员、站点和既有历史认证。基线 3 项均命中预期缺陷。暂停与撤权先提交时，旧请求仍返回 `200`，原生配置与回执均已写入。业务先提交时，暂停直接等待租户行，未在 Main 授权根上与业务事务排序。 [基线 TRX](D:/CP6-test-artifacts/W630-01/baseline/results.trx) · [实际阻塞链与写集](D:/CP6-test-artifacts/W630-01/baseline/permission-consumption-baseline)
- 修复后 3 项签名交错测试和 1 项控制器权限声明检查通过，零跳过。暂停先提交时，已进入事务的原请求等待 Main 授权根后返回 `401`。仅撤回提供者管理权先提交时，旧写入返回 `403`，保留 `space:model:read` 的原会话仍可 `GET` 并读回相同配置和认证。两种情况下配置、认证与原回执的持久化写集一致。 [最终 TRX](D:/CP6-test-artifacts/W630-01/verified/results.trx) · [实际阻塞链与写集](D:/CP6-test-artifacts/W630-01/verified/permission-consumption-verified)
- 业务先提交时，实际站点锁已取得，暂停等待业务事务。提交空认证列表将站点由修订 1 更新为修订 2，保留原认证与版本历史，并写入同一操作者的唯一回执。恢复后同键回放原响应，变化请求与新键携带过期修订均返回 `409`。暂停先提交的未落库请求可在恢复后以原键提交一次。
- 补充验证 Provider routing 与 Remote Worker 协议、非空主备认证、评分、修订冲突及回放，共 23 项通过，零跳过。补充构建复用 W624 制品目录的 MSBuild 配置排除无关的旧 Dispatch 测试夹具，测试所用 Space Infrastructure 程序集与原生验收相同。远程协议验证使用测试传输，实际签名 HTTP 用例使用模拟历史认证，未启用外部 Worker 或修改本机证书。 [补充 TRX](D:/CP6-test-artifacts/W630-01/routing/results.trx) · [程序集与复用输入](D:/CP6-test-artifacts/W630-01/routing/inputs.json)

## 本地发布检查

隔离本地发布共 310 文件，七份发布程序集 SHA-256 与原生测试程序集逐项相同。PostgreSQL 使用 `57070`，Kestrel 使用 `57078`，实际活性 `200`、匿名配置写入 `401`、签名站点读取 `200` 并回读同一操作者的修订 2。所用隔离进程均已停止。Actions 运行与环境部署均为零。 [发布冒烟](D:/CP6-test-artifacts/W630-01/publish-verified-smoke.json) · [发布文件 SHA-256](D:/CP6-test-artifacts/W630-01/publish-sha256.json)
