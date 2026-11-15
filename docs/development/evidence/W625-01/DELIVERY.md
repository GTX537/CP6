# W625-01 OA 服务作业完成的事务栅栏

2026-11-15 UTC。开发基线 `cb30090c2b8d7bb91ecb7b548d4a134aff580b1c`，受测源码 `b284566e6bf12fc96a1f09222ca0477a89ada1ee`。隔离 PostgreSQL 18.6 和真实 Main Kestrel，使用模拟租户、凭证、已有人工作决定的流程及原到期作业。 [缺陷 #1147](https://github.com/GTX537/CP6/issues/1147) · [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)

## 事务与恢复

- 在作业真实领取后，PostgreSQL 完成事务先读取 Main 共同根与当前租户，再按原作业行核对持有者、领取时刻、尝试次数和 PostgreSQL 时钟。原凭证、流程和历史、通知 Outbox 与作业终态由同一事务提交。提交前复核作业及租户期限。错误路由及已入队的子流程完成采用同一事务。原技术领取及外部执行器保持原作业身份。 [作业源码](../../../../CP6.Core/Services/Wf/WfServiceJobService.cs)
- 签名暂停先取得根并提交时，已选择的原定时作业最多提交技术领取，凭证、流程、通知及其余业务写集逐行保持。在租户重新启用后，现有租约回收用同一作业完成，保留原人工作决定。业务先取得根时，暂停在同一业务事务上等待。独立观察连接在通知写入仍等待时读取原凭证和原流程，随后核对整笔结果、作业及 Main 暂停回执。再次扫描保持同一写集。

## 实际验证

- 两项新的 PostgreSQL 顺序用例通过，零跳过。首次执行因隔离目录命名在初始化停下。随后修正夹具需分别观察技术授权根代次，以及受既有触发器保护的作业领取语句。未完成的执行保留原结果，最终两种顺序都实际阻塞并验证。原 W623 记录仅提供通过的暂停前枚举对照及首轮夹具结果。 [两种顺序 TRX](D:/CP6-test-artifacts/W625-01/ordered-native/results.trx) · [首轮](D:/CP6-test-artifacts/W625-01/first-native/results.trx) · [修订夹具前](D:/CP6-test-artifacts/W625-01/verified-native/results.trx)
- 相关过滤运行 52 项通过，其中 6 项原生 PostgreSQL 涵盖原人为决定、外部连接器提交后派发与采购回包丢失，四项历史 SQLite 用例在建库时因 `Latin1_General_100_BIN2` 缺失而失败，均保留。另三个预算和 OA 子流程实库验证通过。无跳过。 [相关 TRX](D:/CP6-test-artifacts/W625-01/related/results.trx) · [子流程实库](D:/CP6-test-artifacts/W625-01/subflow-native/results.trx)
- 本地发布含 310 文件 SHA-256，七份产品程序集与受测文件相同。发布宿主活性 200、匿名 OA 写入 401，隔离进程已停止。组合 W624 后 `10c18a9f36adffb6270752f2c0ce30ac632e3920` 上四个受影响文件逐字节相同，W624 只改 Space 映射及其证据。 [发布证据](D:/CP6-test-artifacts/W625-01/publish-verified-smoke.json)

本项覆盖已领取服务作业的本地完成。独立超时扫描、连接器目标效果、旧有提交后内联子流程、其他后台写者及 CRM 消费交接仍待各自验证。正式 PGT-017 为 NOT_RUN。Actions 和部署运行数均为零。
