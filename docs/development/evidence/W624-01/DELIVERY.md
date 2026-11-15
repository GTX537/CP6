# W624-01 Space CAD 与 Excel 映射配置写入

2026-11-15 UTC。Main 开发基线 `6374d7692574f41ccaeb123334f22151e8fe02a0`。W622 在两个真实签名入口记录了四项预期缺陷失败，并保留 PostgreSQL 等待链和写集。 [缺陷 #1142](https://github.com/GTX537/CP6/issues/1142) · [W622 实跑](D:/CP6-test-artifacts/W622-01/DELIVERY.md)

## 事务与回执

`POST /api/space/design/v1/mapping-profiles/cad` 与 `/excel` 的创建和更新，在原生保存事务中复用 `SpaceDesignWriteTransaction`。事务取得 Main 的授权根，按原签名用户读取当前 `space:model:edit`，提交前重新核对同一用户的有效会话。暂停、版本化撤权和配置写入通过同一个 PostgreSQL 锁排序。配置头、不可变版本和原始回执一起提交，`SpaceMappingSaveTransaction` 继续处理原有的事务重试和回执恢复。

原请求哈希、行版本、复制谱系和回执结构沿用当前合同。同键请求仍可在开启新写事务前读取原回执，保存期间的同键竞争沿用事务内检查。更新后的两个版本和原始返回值都能按原键恢复。

## 本地验证

- 在 PostgreSQL 18.6 及真实 Main Kestrel 上使用模拟租户、有效签名和实际内置 CAD、Excel 样板。最终构建零错误，10 项签名回归通过，零跳过。 [最终 TRX](D:/CP6-test-artifacts/W624-01/verified/results.trx) · [原生写集与阻塞链](D:/CP6-test-artifacts/W624-01/verified/permission-consumption-verified) · [源码指纹与执行参数](D:/CP6-test-artifacts/W624-01/verified/invocation.json)
- 两个入口的创建和更新分别验证两种提交顺序。暂停先提交时，已进入保存的请求等待 Main 授权根，返回 `401`，配置头、版本和回执逐行一致。业务先提交时，请求在回执插入处等待，暂停在同一根上等待业务。创建返回 `201` 与版本 1，更新返回 `200` 与版本 2。租户恢复后，同一用户可完成此前未提交的请求，已提交请求按原键回读原响应。用版本 1 的原更新请求重复发送时，仍返回原版本 2 和同一操作者，旧创建键仍返回原版本 1。
- 两个入口的更新都实测版本化 IAM 撤回 `model:edit` 先提交。已进入保存的请求在授权根之后返回 `403`。该角色保留 `model:read`，原会话正常回读原配置，数据库写集逐行一致。第一轮 CAD 回读断言曾比较 .NET 保存值与 PostgreSQL 回读值的时间戳尾数，因精度差异失败。最终回读核对配置标识、版本、行版本和操作者，原生写集与同键原结果仍做精确比较。 [第一轮 TRX](D:/CP6-test-artifacts/W624-01/first/results.trx)
- 补充通过 4 项真实 PostgreSQL 同键并发和调用方事务保护，以及 12 项 CAD、Excel 映射服务测试，零跳过。独立 `CP6.Space.IntegrationTests` 的普通构建遇到既有 `RecordingTaskCenter` 缺少接口成员，补充构建仅通过制品目录中的 MSBuild 配置排除该无关测试文件。所用映射产品程序集与签名验证相同。旧启动脚本等待 Windows 子进程退出的失败及随后报告日志读取失败均保留，后一次测试 TRX 完整且 PostgreSQL 正常停止。 [补充 TRX](D:/CP6-test-artifacts/W624-01/mapping-services-confirmed/results.trx) · [普通构建日志](D:/CP6-test-artifacts/W624-01/mapping-services/build.log) · [补充数据库停止日志](D:/CP6-test-artifacts/W624-01/mapping-services-confirmed/stop.log)

隔离进程均已停止。验证针对两条映射配置写入口及原映射服务合同。Actions 与部署均为零。
