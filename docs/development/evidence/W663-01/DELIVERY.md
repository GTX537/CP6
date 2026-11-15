# W663-01 设备状态监控与租户暂停

Main 基线 `b6f31a5c393b5040854db2ee94a8602e9e1b9e20`，实际受测候选 `660e5f853228805ca49c2f5c956cead63f094d2c`。使用隔离 PostgreSQL 18.6、模拟租户和设备、实际 Main Kestrel 登录与租户暂停，按原租户循环运行生产设备状态监控写者。通知使用模拟外部接收器记录调用。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [任务登记](D:/CP6-test-artifacts/W663-01/main-start.json)

旧版两种提交顺序均实际失败。Main 暂停持共同根等待租户行时，设备监控从旧枚举写出 `Status=0` 并提前结束。设备监控等待设备表写锁时，暂停事务又可先返回 200。两次都从 PostgreSQL 实际回读了 `MachineMonitor` 提交的状态。 [基线 TRX](D:/CP6-test-artifacts/W663-01/baseline/main.trx)

生产监控在 PostgreSQL Read Committed 事务取得既有 Main 认证根，读取实际租户启用及期限，按原设备状态与最近报工规则更新行，再核对期限并提交。确认提交后发出原状态通知。原设备行版本继续裁决与其他设备写者的竞争。 [写者](../../../../CP6.WebApi/BackgroundServices/MachineStatusMonitor.cs) · [原生夹具](../../../../CP6.Tests/Erp/Pgt017MachineMonitorPostgreSqlTests.cs)

| 原生提交顺序 | 实际等待和最终结果 |
| --- | --- |
| 业务先 | 生产监控的 `M_Machine` UPDATE 等测试表锁，签名暂停在 Main 根等待同一监控后端。监控状态先提交并发送一次通知，暂停随后完成。再次处理暂停前的相同租户清单，保留原状态和行版本。 |
| 暂停先 | Main 先持共同根等待目标租户行，生产监控在同一根阻塞。暂停提交后保留设备原状态和行版本，通知为零；再次处理原清单保持相同结果。 |

两序均核对锁释放前的设备状态及租户原写集，随后回读实际租户状态、认证政策、管理版本、身份代次、C02 快照、Outbox、审核与原暂停回执。原设备监控与直接并发状态写的行版本回归亦通过。共 **3 项 PostgreSQL 通过、零跳过**。 [实际阻塞后端及结果 TRX](D:/CP6-test-artifacts/W663-01/candidate-native/main.trx)

本地发布 310 文件，清单 SHA-256 `5534FC37BB2F453F53FCFED69BEEA8F5283F96CAFCA5BA7ED998AD3314447B03`。三份产品程序集与实际测试匹配。隔离新库 `DatabaseInit` 退出 0，确认原生共同根和设备表存在。发布应用存活 200，匿名设备状态请求 401。所有任务进程已停止，57260–57279 无监听。 [受测源码 SHA-256](D:/CP6-test-artifacts/W663-01/tested-source-file-sha256.json) · [本地发布及启动](D:/CP6-test-artifacts/W663-01/main-publish-smoke/result.json) · [停机证明](D:/CP6-test-artifacts/W663-01/process-verification.json)

正式矩阵维持 **26 PASS / 1 NOT_RUN**。部门、全局菜单及普通会话和凭证权威的剩余适用验证继续按各自原入口执行。Actions 与生产部署均为 0。
