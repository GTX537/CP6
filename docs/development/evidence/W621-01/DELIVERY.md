# W621-01 Space 发布物理效果的当前租户权限

从已登记的 Main `7d596bea19d20e8757b6cfff09ed43e1c4ea5b0a` 实施，提交 `7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4`，关联 [#1141](https://github.com/GTX537/CP6/issues/1141)。W619 在相同 Space 写入源码上，使用真实 Kestrel、发布 worker、内置 WMS 与 Runtime 复现两种暂停先提交后仍增加业务效果的情况。W621 引用该基线的实际 RED。 [W619 报告](D:/CP6-test-artifacts/W619-01/DELIVERY.md) · [W619 TRX](D:/CP6-test-artifacts/W619-01/native-2/results.trx)

## 提交、回执与重试

- 内置 WMS 与 Runtime 各自复用原 Serializable 所有者事务。Main 共同锁在依赖写入前取得，使用同事务读取当前租户和锁定当前 `Space_Job`。通过已领取的 lease 确认 Job、Attempt、Worker、Subject、输入和取消状态。提交前用数据库前进时钟重核 lease，用 Main 本地日期语义重核租户到期。测试期实际观察到等待 Main 锁导致的 `40001`，原生事务初始化在产生业务效果前重试。 [权限边界](https://github.com/GTX537/CP6/blob/7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4/CP6.Space.Infrastructure/SpacePublishEffectAuthority.cs)
- WMS 既有操作按原 key 和 payload hash 读取回执。新 WMS 效果把库位与操作回执同事务提交，Runtime 把楼层、库位、元素及发布指针同事务提交。原 Job lease 通过内部调用显式传递，外部适配器接口和序列化数据沿用现有定义。已知暂停采用原 Job 的可重试状态。Runtime 延后时保留已经提交的 WMS 批次、回执和原 `WmsCommittedAtUtc`。真实 COMMIT 确认遗失通过原 WMS 回执或已提交指针恢复。 [WMS](https://github.com/GTX537/CP6/blob/7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4/CP6.Space.Infrastructure/Cp6SpaceWmsAdapter.cs) · [Runtime](https://github.com/GTX537/CP6/blob/7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4/CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs) · [Saga](https://github.com/GTX537/CP6/blob/7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4/CP6.Space.Infrastructure/SpacePublishOrchestrator.Execution.cs)
- 原自动重试预算继续生效。已知暂停在原 Job 仍有额度时记录 `WaitingRetry`，重新启用后继续同一个 Job 和业务 Attempt。达到最终配置的尝试次数时，现有 Job 策略进入 `DeadLetter`，业务 Attempt 进入 `ManualIntervention`，可在重新启用后由现有 `space:model:publish` 人工重试入口恢复。该入口针对原业务 Attempt 记录重试并关联新 Job。该末次情况引用已有预算单测与代码，实库用例覆盖仍有额度的原 Job 恢复。 [Job 策略](https://github.com/GTX537/CP6/blob/7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4/CP6.Space.Domain/SpaceJobRetryPolicy.cs) · [原人工重试](https://github.com/GTX537/CP6/blob/7dc9d42db1ab7ed3ca3a55dee6c8ab69056a1cf4/CP6.Space.Infrastructure/SpacePublishOrchestrator.cs#L568)

## 实际验证

在隔离 PostgreSQL 18.6 56950 和 Main Kestrel 56951，通过实际签名登录、授权仅含刊发和读取的用户、真实暂停以及实际生产发布 worker，以模拟租户、位置、楼层、已发布和候选快照运行。原生 `T_WmsBin`、`T_SpaceWmsOperation`、`Space_Location`、楼层、Runtime 元素、模型、版本、Job/Attempt、已验证批次、回执及审计链由独立 PostgreSQL 连接检查。

| 用例 | 观测到的结果 |
| --- | --- |
| WMS 暂停先 | WMS 等待暂停持有的 Main 锁。暂停提交后没有新库位或回执，原 Job 排队。重新启用后同一 Job 和 Attempt 完成。 |
| WMS 效果先 | 暂停在 Main 锁等待 WMS 写入。在后一阶段 Runtime 进入前暂停提交，保留 WMS 原对账行、回执和时间，Runtime 暂缓。重新启用后同一 Job 完成 Runtime。 |
| Runtime 暂停先 | 已验证 WMS 原效果存在。Runtime 等待暂停持有的 Main 锁，暂停后保留原版本指针和投影。重新启用后同一 Job 完成 Runtime，WMS 行与时间保持。 |
| Runtime 效果先 | 暂停等待 Runtime 原生提交。目标保持 Published，原版本保持 Superseded，指针、楼层、位置、元素和 Job/Attempt 的已提交完成保留。 |
| WMS、Runtime 提交确认遗失 | 两次故障在真实 COMMIT 后注入。签名暂停先于确认返回，WMS 按原操作 key 与 hash 读回同一回执，Runtime 按已提交指针恢复。后续合法 worker pass 没有重复业务效果，WMS 后续待提交的 Runtime 在重新启用后由原 Job 完成。 |
| Runtime 提交前自然到期 | 持续阻塞真实库位 INSERT，跨越当前已存过期时间后释放。Runtime 回滚，原 WMS 保留。更新有效期后同一 Job 完成。 |

共 **7 原生通过、55 定向单测通过、29 发布和相关契约测试通过、零跳过**。暂停时旧会话 401。重新启用并签名后，相同刊发键返回 202、原 Attempt/Job/Plan ID，原生回执和审计未重复。后续范围包括进程终止后的接管、历史 republish 父 Job、CAD/Excel 与其他 provider 自有事务。 [原生 TRX](D:/CP6-test-artifacts/W621-01/green-2/results.trx) · [实库证据](D:/CP6-test-artifacts/W621-01/green-2/permission-consumption-green-2) · [单测](D:/CP6-test-artifacts/W621-01/unit/results.trx) · [相关测试](D:/CP6-test-artifacts/W621-01/publish-related-verified/results.trx)

首次候选编译记录了测试端可空 JobId 的断言类型错误，修正后成功。相关测试的首次隔离运行缺少以固定相对路径读取的监控 YAML，复制仓库原文件并核对哈希后 29 项全过。提交前 diff 检查清理了一处测试文件 EOF 空行，产品源文件哈希与实际受测源码一致。 [首次构建](D:/CP6-test-artifacts/W621-01/green-1/build.log) · [首次相关测试](D:/CP6-test-artifacts/W621-01/publish-related/test.log) · [EOF 核对](D:/CP6-test-artifacts/W621-01/test-trailing-line-normalization.json)

## 本地发布与工作区

本地 Debug PostgreSQL 发布目录 310 文件有 SHA-256，七份产品 DLL 与受测字节相同。隔离发布制品在 PostgreSQL 56950 和 HTTP 56958 实际启动，活性 200、匿名刊发 mutation 401。工作树干净，所有本任务隔离进程已停止，56950–56959 无监听。受测源码分支 `codex/w621-01` 由协调方采用。上述本地运行未触发 Actions 或部署。 [发布与冒烟](D:/CP6-test-artifacts/W621-01/publish-verified-smoke.json) · [文件清单](D:/CP6-test-artifacts/W621-01/publish-sha256.json)

## 主线组合核对

在 W620 合并后的主线 `6374d7692574f41ccaeb123334f22151e8fe02a0` 组合为 `27f95b7c39b58b8627d3c5f63bfdbb19cb8387d8`。本项八个产品及测试文件与受测源码逐字节一致。W620 的业务写入涉及出货后的财务意图与原财务回执，本项内置刊发适配器的库位及投影写入路径未改动，原七项 native 结果与本地发布证据按原源码引用。正式 PGT-017 仍待其余写者证据。
