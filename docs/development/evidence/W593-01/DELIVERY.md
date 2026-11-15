# W593-01 Space Design 共享 Main 提交权限栅栏

2026-11-15 UTC。Main 基线 `15dbd1adb872b31e0f11fd2d75d0e8d839d7176b`，受测源码 `d03b1f7c381286e6e70abc8f88381a9bb7108297`。实际 Main Kestrel 与隔离 PostgreSQL 18.6。Core 和 Space 各自使用正式迁移，模拟租户、编辑者、模型、楼层、编辑租约和资产。 [缺陷 #1100](https://github.com/GTX537/CP6/issues/1100) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

| 已实际运行的入口 | 持栅栏的业务事务及记录 |
| --- | --- |
| 新建楼层 | 原 Space 可串行化事务内保存楼层、版本内容代次和原幂等回执 |
| 应用元素命令 | 同一事务保存元素、楼层和版本代次、批次及命令记录 |
| 应用布局命令 | 同一事务保存分区、楼层和版本代次、批次及命令记录 |
| 新建资产 | 同一事务保存资产身份、首个版本和原幂等回执 |

Main 在 Space 的实际连接与事务上检查会话和 `space:model:edit`。先取得 `cp6_authentication_v1.acquire()`，再取 Space 业务锁；提交前确认原会话仍有效。可串行化事务等待旧栅栏后若收到已确定的回滚错误，在业务开始前用新事务重读；保留现有业务隔离级别、原请求键和回执。 [PostgreSQL 可串行化隔离](https://www.postgresql.org/docs/18/transaction-iso.html#XACT-SERIALIZABLE)

- 四条实际 HTTP 路径各运行两种提交顺序。通过独立观察连接确认真实等待顺序。暂停先提交的四笔均返回 401，原写集保持。业务先提交为 200/201，暂停继续生效，旧会话拒绝，已提交效果与回执保留。恢复后用同一操作键验证每项只落一次。
- 保持原会话并由正式 PostgreSQL 的原生角色动作写者先撤销 `model:edit`。实际资产请求等待栅栏，随后返回 403，资产、版本及回执未改变。重授后同一操作成功一次。
- 最终 **68/68**，其中 9 个实际 PostgreSQL 竞争场景、59 个相关组件回归，零跳过。基线 8 项失败，6 项观察到未共用栅栏，另 2 项元素用例缺 JSON 几何版本。纠正夹具后中间的 8 项及最终 9 项 PostgreSQL 通过。原始 TRX、写集和实际等待保留于 `D:/CP6-test-artifacts/W593-01`。
- 本地 PostgreSQL 发布制品共 310 个文件 SHA-256，WebApi、Core 和 Space.Infrastructure 与测试加载文件一致。隔离发布宿主存活 200，匿名资产写请求 401。保留既有编译与包审计警告。Actions 启动、重跑及取消为零，未部署。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。CAD 选定变更和模板应用复用同一元素或布局执行器，完整入口仍待独立联验。模型版本初始化、来源和底图、编辑租约、模板登记和 WMS 集成等其它 Space 写者，以及财务、全量写者登记、CRM 恢复重绑和切换仍待核对。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
