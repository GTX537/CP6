# W594-01 Space 来源和编辑租约的共同提交栅栏

2026-11-15 UTC。Main 基线 `af3ea2a8aa9fa6b3f7c06b5247fc8cbc42ddcaca`，受测 `be633749701eade262129f80f2dc70f8f5b7108d`。真实 Main Kestrel 和隔离 PostgreSQL 18.6，使用模拟租户、上传扫描后的文件元数据、来源和编辑会话。 [缺陷 #1102](https://github.com/GTX537/CP6/issues/1102) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

| 实际 HTTP 写者 | 持栅栏的现有事务与结果 |
| --- | --- |
| `POST versions/{versionId}/sources`、`POST versions/{versionId}/sources/{sourceId}:remove` | 同一 Space PostgreSQL 事务核对 `space:model:edit` 与 `space:source:upload`，保存来源、内容代次和原幂等记录。移除保留原物理文件记录。 |
| `POST versions/{versionId}/floors/{floorLogicalId}/lease`、`POST .../lease/{leaseId}:renew`、`POST .../lease/{leaseId}:release` | 同一 Space PostgreSQL 事务核对 `space:model:edit`，再取得原楼层锁，保存租约身份与有效期。 |
| `POST versions/{versionId}/floors/{floorLogicalId}/lease:takeover` | 同一事务核对 `space:model:edit` 和 `space:model:lease:takeover`，保存新租约及含原和新身份的接管审计。 |

六条实际路径分别验证两种提交顺序。业务先提交的原写集保留，暂停先提交后返回 401 且没有新写入。恢复后原来源命令按同一个操作键成功或回读原结果，原租约继续按取得、续约、释放、接管的不同合同处理。另独立撤销上传权和接管权，各返回 403，原业务行、文件、原回执及接管审计不变。部署的业务事务仍使用原隔离和重试协议，Main 按同一连接核对当前会话与原动作，提交前确认会话。

- 首次编译记录了移除状态夹具错误，实际合同是删除标识与保留状态。修正后基线 14 项均复现没有共用栅栏。最终 **82/82，零跳过**，其中本轮 14 项实际 PostgreSQL、W593 9 项同栅栏实测与 59 项相关组件回归。保留原 TRX、等待及完整写集于 `D:/CP6-test-artifacts/W594-01`。
- 本地发布 310 个文件 SHA-256，Main、Core、Space 基础设施与测试加载文件对应。实际隔离宿主存活 200、匿名来源写入 401。Actions 启动、重跑及取消为零，未部署。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。本轮覆盖已扫描文件的来源挂接和移除及公开编辑租约。版本预留、模板内部租约/恢复和最终回执、底图文件上传与跨存储副作用、Space 其余写者、财务、全量登记和 CRM 受控切换仍需验证。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
