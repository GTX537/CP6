# W652-01 ERP 出库回写保留最后实物日期

Main 基线 `1aa2654fcb3f14f58c608aa416c07fa7474c8824`，本地源码提交 `1c3f914ae1a8459d785c1ccabda9c4ce51325117`，关联 #1172 和 #1012。改动为 ERP 回写字段、真实乱序回归、原 W643 夹具的可选端口。集中 diff 和 `git diff --check` 通过，提交的三个文件与实测 Git blob 一致。

原生补写继续累计原数量，也按该笔业务维护发货状态。订单头 `ActualShipDate`、受影响行 `LastShipDate` 只在原确认时间更晚或此前为空时推进，行 `LastOutboundNo` 与获胜日期共同赋值。原确认时间相同或缺失时保留已知日期和标记。头行 `ModifyDate` 记录本次处理时间。原非原生处理、旧格式精确标记/成功证据和 `ERP_SHIPMENT_OUTCOME_UNKNOWN` 仍走原分支。[回写代码](../../../../CP6.Core/Services/Wms/ErpBridgeHook.cs#L82)

## 实测

复用 W653-01 的真实红灯。其固定基线 `1e2b294eb2f4f5a72cf903a16bac724edc74694d` 与本任务开工基线的 ERP Hook、原生处理、出库、后台 retry 四个源码文件一致。W653 的两张合法出库经真实签名处理及原事件恢复，两个效果各一次，仍在末尾日期合同断言失败。[W653 报告](D:/CP6-test-artifacts/W653-01/DELIVERY.md) [原失败 TRX](D:/CP6-test-artifacts/W653-01/native-order/results.trx)

隔离 PostgreSQL 18.6（57262）、真实 Kestrel（57266、57267）、原生 COMMIT 回执中继（57265）。本次继续使用模拟订单和两张手工出库，失去 A 原实物回执后由正常 B 请求先过账，再执行生产 `IntegrationEventRetryWorker.ProcessOnceAsync`。只有单独财务意图的待扫时点由夹具推迟。候选在之前各业务断言之外，还核对审计时间晚于 B 回写并在 A 原重试实际开始之后。

| 观察 | 实际结果 |
| --- | --- |
| A 先实物，原 ERP 待处理 | `OUT2026110001` 在 `2026-11-15 14:48:39.760378` 完成 5 件。实物 COMMIT 回执丢失，真实 HTTP 返回 503 `OPERATIONS_OUTCOME_UNKNOWN`，原事件 `067c4b3c-bf19-453f-9b89-567966167769` 为 Pending/0。 |
| B 先 ERP 过账 | `OUT2026110002` 在 `14:48:41.036553` 完成 2 件，HTTP 200。原事件 `568e538c-bcb6-448e-bf7c-4d0026c64bb5` 已 SUCCESS/1。 |
| A 原事件恢复 | 头 `ActualShipDate`、共享第一行 `LastShipDate` 及 `LastOutboundNo` 保持 B 的 `14:48:41.036553`/`OUT2026110002`。仅由 A 触及的第二行保存 A 的确认时间和号，两行累计分别为 5 和 2。头与共享行 `ModifyDate` 从 `14:48:41.538492` 推进到 `14:48:42.483544`。 |
| 原回执与重复扫描 | 两条原事件各 SUCCESS/1，保留原 Payload、相关号、原出库和原订单。重复调用真实 worker 后，独立回读的原实物、订单头和两行、事件及其 native 行版本保持相同。 |

本次全部 4/4，零跳过。含真实逆序 1、W643 暂停两序 2、原旧格式标志回收 1。TRX SHA-256 `ba61244e0bccfa60ba2412dc5132b80124c56d08782c32ccf44075e054214fc2`。范围为这条 ERP 投影和直接依赖，正式 PGT-017 仍为 `NOT_RUN`。[定向 TRX](D:/CP6-test-artifacts/W652-01/candidate/results.trx) [逆序原始回读](D:/CP6-test-artifacts/W652-01/candidate/permission-consumption-candidate/shipment-date-order.json) [实测源与参数](D:/CP6-test-artifacts/W652-01/candidate/invocation.json)

## 本地发布与工作区

发布共 310 文件，清单 SHA-256 `3055c570bad09103a877f7475908222c62cb0404302fe18cd475d05ae98b7571`。WebApi、Core、PostgreSQL persistence、Space Infrastructure 四个 DLL 与实测副本 SHA-256 一致。以发布文件在 57268 实际启动，`GET /health/live` 200，匿名 `POST /api/wms/outbound-order/{no}/ship` 401。此为隔离本机发布检查。[发布清单](D:/CP6-test-artifacts/W652-01/publish-sha256.json) [源码与冒烟记录](D:/CP6-test-artifacts/W652-01/publish-smoke.json)

本地 `codex/w652-01` 已提交，DEV-2 工作树干净。任务 PostgreSQL、Kestrel 和回执中继均已停止，57260–57269 无监听。分支及席位待协调方正常集成和 `finish` 后处置。Actions 与部署无操作。

## 采用 W649 后的组合验证

组合提交 `b509c7982e2a5a3a853355a3f13245ce499523b3` 采用 W649 Main `6f6b75ebf836427a2b274e8a0238afe767a33969`。W652 的三个受测文件与 `1c3f914ae1a8459d785c1ccabda9c4ce51325117` 的原实测 blob 一致。原 4/4 继续按该来源引用，本次只复测受 W649 Main 暂停事务影响的 W643 原两序。[原交付](D:/CP6-test-artifacts/W652-01/DELIVERY.md) [组合参数与源码指纹](D:/CP6-test-artifacts/W652-01/combined-native-pause/invocation.json)

隔离 PostgreSQL 18.6（57263）、真实 Main Kestrel（57267）和回执中继（57265）。两条原签名出库的物理提交均在中继观察到真实 COMMIT 后丢应答。独立连接观察源已完成和其原 ERP 意图 Pending/0。两例最终通过，零跳过，测试耗时 40 秒。

| 顺序 | 实际原生等待及结果 |
| --- | --- |
| 暂停先 | `2026-11-15 20:08:07.170398 UTC` 签名暂停停在 `Sys_Tenants` 更新，`20:08:07.454720 UTC` ERP worker 在 Main 根等待。暂停提交时，订单写集保持原样，原事件 `271f2209-c3e1-422c-b38c-9389d7358837` 仍 Pending/0。签名恢复后，同一事件变为 SUCCESS/1，两原订单行各发 2.5。 |
| 过账先 | `2026-11-15 20:08:24.867509 UTC` ERP worker 停在 `T_Order` 更新，`20:08:25.338877 UTC` 签名暂停在 Main 根等待。暂停完成前，原事件 `4447abdc-a2fa-402e-99c3-f68b1ef98e24` 已 SUCCESS/1，两原订单行各发 2.5。 |

两例均核对暂停原回执、权限代次、快照、审计和 Outbox。完成态头 `ActualShipDate` 等于原出库确认时点，再扫写集相同。TRX SHA-256 `f50445ae23781259bbf74493397f83a3ca39460fe16e7d70df67cf12018f60c3`。[组合 TRX 与原始读回](D:/CP6-test-artifacts/W652-01/combined-native-pause/results.trx)

组合发布有 310 个文件，清单 SHA-256 `d85a2761b1417dd6fc33c4fb529a68411d9f24e1ca9b505c304bbe31adce09a0`。四个受测产品 DLL SHA-256 与发布副本一致，程序集元数据均自报完整组合 SHA。组合发布物在 57268 实际运行，`GET /health/live` 200，匿名 `POST /api/wms/outbound-order/{no}/ship` 401。原发布证据保留。[组合发布记录](D:/CP6-test-artifacts/W652-01/publish-combined-smoke.json) [组合清单](D:/CP6-test-artifacts/W652-01/publish-combined-sha256.json)

组合测试结束时任务进程、57260–57269 监听及活动 PostgreSQL PID 文件均为零。[停止与哈希核对](D:/CP6-test-artifacts/W652-01/combined-native-pause/final-verification.json)
