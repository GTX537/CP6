# W662-01 部门 C02 封存与原件恢复

Main 本地提交 e3cfc8ab044ca17cc200d5008843f1e31a013d70，集成基线 b6f31a5c393b5040854db2ee94a8602e9e1b9e20（W661 已采用）。相对基线检查十个范围文件。CRM 沿已采用的 5f09533b9168ee614a6df1f75629b39635585f86 对应的 W649 本地验证宿主、现行生产 Worker 和原 PostgreSQL 封存函数。数据、主体、证书、连接和端口均为隔离模拟环境。 [#1012](https://github.com/GTX537/CP6/issues/1012) · [源文件 SHA-256](D:/CP6-test-artifacts/W662-01/tested-source-file-sha256.json) · [CRM 采用](D:/CP6-test-artifacts/W649-01/crm-remote-main.json)

## 改动

部门更新、移动和负责人操作仍由原 IAM 权限、签名主体、原请求指纹、部门及全树版本、租户引用版本和原移动预览决定。Main 在原鉴权根内捕获完整的 C02 前后版本和哈希，以 savepoint 回滚预备业务写集。持久化原 actor、键、请求指纹、准确 StoreId 和完整部门向量后才首次连接 CRM。首次产生 C02 的新建和 C02 恒等编辑继续使用原提交。

原件恢复在联网前和成功连通后核对旧 CAS。CRM 用既有物理 Store 绑定和原源锁在一个 PostgreSQL 事务提交完整子树最低版本。主库最终以原事务保留部门、C02、Outbox、审核、回执及 pending 清理。CRM 或 Main 应答不确定时沿原键读取和重放。未开始原件仍可关闭。 [部门所有者](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Sys/IamAdministrationV1.CrmDepartmentSeal.cs:11) · [原生捕获](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:183) · [向前迁移](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Persistence.PostgreSql/Migrations/Core/20261115223000_IamCrmDepartmentSealIntentV1.cs:11)

Main 以各请求实际可及的主体和目标祖先判定是否签发新写围栏。人的普通只读签发沿原路径。公开表单的新尝试和新提交所需的目标由 CRM 已激活配置或原记录决定，现有签名请求只绑定站点和表单。纯清理 retention 依赖主体路径。相同签发目的下，拥有实际站表与 crm-lead:edit 资格的 review-expiry 还能写其已获准的目标。已提交的 CRM 公开原件仍沿先读 ledger 再决定是否调用 Main 的原顺序。 [人类签发](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.WebApi/Services/InquiryPermissionIssuerV1.cs:92) · [服务签发](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.WebApi/Services/InquiryServicePermissionIssuerV1.cs:112) · [原 CRM ledger 回读](D:/CP6-test-artifacts/W649-01/crm-main-verification-source/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.cs:60)

## 实际验证

| 路径 | 观察结果 | 证据 |
| --- | --- | --- |
| 基线冷拒绝 | Main 94b6ef5159bc619de68b5eda7fc5c657bacb0ef7 上，实际禁用和带原 preview 的移动均收到 PostgreSQL 28000，Main 503，两库原效果维持。受影响服务与独立服务都得到新围栏，受影响断言真实失败。早期目录命名和 SQL 排序规则是夹具诊断，有各自留存日志。 | [禁用 RED](D:/CP6-test-artifacts/W662-01/cold-disable-native-red/main.trx) · [移动 RED](D:/CP6-test-artifacts/W662-01/cold-move-native-red/main.trx) |
| 最终禁用与两库应答丢失 | 28000 后签名 PUT 返回 503 AUTHORITY_FENCE_UNAVAILABLE，原业务行、C02、Outbox、审核、回执及 CRM floor 维持。独立真实连接验证 pending 与 C02 哈希，Main 更换 PID 后使用原会话。受影响的人类和服务签名保留但省去新写围栏。独立人类及独立服务有新围栏，普通人类只读 200，纯清理服务继续有围栏，review 及公开新签发省去围栏。错误 actor、不同输入以及竞争原部门键被明确拒绝。CRM COMMIT 应答丢失后 floor 为 2,0,0,0，Main 原效果仍未提交。原 Main Dept UPDATE 真实等待后 COMMIT 应答丢失，GET 和旧别名准确读回唯一 COMMITTED。 | [TRX](D:/CP6-test-artifacts/W662-01/final-disable-ack/main.trx) · [签发与冷状态](D:/CP6-test-artifacts/W662-01/final-disable-ack/coordination/department-cold.json) · [CRM COMMIT](D:/CP6-test-artifacts/W662-01/final-disable-ack/coordination/department-remote-ack.json) · [Main 原回执](D:/CP6-test-artifacts/W662-01/final-disable-ack/coordination/department-recovery.json) |
| 最终移动与两库应答丢失 | 原编辑器及真实 preview 确认父和子。CRM 第一个 COMMIT 应答丢失时完整 floor 是 2,2,0,0，Main 旧树及原写集维持。Main 最终应答丢失后 GET 和旧移动别名同键重放返回相同原回执，父及子新路径、C02、Outbox 和原审核校验通过。 | [TRX](D:/CP6-test-artifacts/W662-01/final-move-ack/main.trx) · [CRM 完整向量](D:/CP6-test-artifacts/W662-01/final-move-ack/coordination/department-remote-ack.json) · [Main 原回执](D:/CP6-test-artifacts/W662-01/final-move-ack/coordination/department-recovery.json) |
| 未开始时引用失配 | PostgreSQL 仍拒绝连接，实际无关部门改名提交且读回新名字。原移动的旧全树和引用版本失配，返回 412 IAM_REFERENCES_CHANGED。CRM floors 保持 0,0,0,0，原 actor 可关闭，旧键随后 409 IAM_OPERATION_CLOSED，原新围栏恢复。 | [TRX](D:/CP6-test-artifacts/W662-01/final-stale-before-seal/main.trx) · [原业务记录](D:/CP6-test-artifacts/W662-01/final-stale-before-seal/coordination/department-stale-before-seal.json) |
| 已开始后引用失配 | CRM 已提交原子 floor 2,2,0,0 且应答丢失。无关部门实际改名并推进原全树和引用条件。原移动保持 UNKNOWN，Main 尚保存原 pending 和旧业务状态，独立签发继续。原 Owner 用旧键查询记录，started 原件不能关闭。已有 CRM 前沿保持，原请求在当前失配条件下无法自动完成。 | [TRX](D:/CP6-test-artifacts/W662-01/final-stale-after-ack/main.trx) · [原 UNKNOWN](D:/CP6-test-artifacts/W662-01/final-stale-after-ack/coordination/department-stale-after-ack.json) |
| 负责人、首次创建与 C02 恒等 | 实际 leader 路由冷拒绝、重启、同键完成并由旧 leader 路由读回。同一角色还在封存登录被拒时通过原同值更新及新部门创建。恒等更新留原回执和审核，创建首版 C02 和普通 Outbox 各一次，旧别名原回执相等。 | [负责人](D:/CP6-test-artifacts/W662-01/candidate-leader/main.trx) · [创建和恒等](D:/CP6-test-artifacts/W662-01/candidate-original-controls/main.trx) |
| W550 业务先与部门变更先 | 实际 CRM 导出事务持 identity locks 并等待原 PublicSubmission 行时，Main seal backend 等待 CRM，先原导出后 Main 部门提交。另一序持有 CRM 实际带签名旧响应，Main 部门提交后恢复 CRM Worker，导出被拒，原结果与 Outbox 维持。两仓共 4 项。 | [业务先 Main](D:/CP6-test-artifacts/W662-01/candidate-w550-owner-first/main.trx) · [业务先 CRM](D:/CP6-test-artifacts/W662-01/candidate-w550-owner-first/actual-main-worker-pause-20261115T2321203183581Z.trx) · [部门先 Main](D:/CP6-test-artifacts/W662-01/candidate-w550-authority-first/main.trx) · [部门先 CRM](D:/CP6-test-artifacts/W662-01/candidate-w550-authority-first/actual-main-worker-pause-20261115T2322312074095Z.trx) |
| 既有组织和写授权定向回归 | 7 项通过，覆盖实际 HTTP 和 CSRF、旧预览及 ABA、旧回执故障和原键恢复、合法负责人历史及签发原生锁。 | [定向 TRX](D:/CP6-test-artifacts/W662-01/candidate-organization-regressions/main.trx) |

交付采用共 17 项通过的定向行为证据，零跳过。最终受测程序集覆盖禁用、移动和两种旧 CAS 冲突。前版有效观察覆盖 leader、创建和恒等、W550 两序及七条原回归。末次修改仅把已有 pending 原件的旧 CAS 判断提前到连接前并保留连接后复验。该重入条件以及正常完成由最终四项覆盖。原 W550 和相关原回归保持其原输入、产品路径及原实际边界。 [运行器](D:/CP6-test-artifacts/W662-01/run-department.ps1) · [最终编译](D:/CP6-test-artifacts/W662-01/final-stale-before-seal/build.log)

## 本地制品与工作区

发布 310 个文件，清单 SHA-256 EB3A57F018009F4B09D99DA8C7DBBFC31852ED9D9CE9E4245EAB482BBE7408F1。发布 WebApi、Core 和 PostgreSQL 迁移 DLL 与最终实测三个程序集逐一匹配。隔离原生库执行 DatabaseInit 退出 0，相关前向迁移及新表 Main 根触发器已从库内确认。发布 Kestrel /health/live 为 200，匿名部门修改为 401。发布仅作为本地验证。 [实际发布与冒烟](D:/CP6-test-artifacts/W662-01/main-publish-smoke/result.json) · [310 文件清单](D:/CP6-test-artifacts/W662-01/main-publish-smoke/publish-sha256.json) · [进程和端口](D:/CP6-test-artifacts/W662-01/process-verification.json)

分支 codex/w662-01 保留本地提交于 DEV-1，工作区干净，席位仍登记给 W662，由协调窗口继续集成和释放。所有本任务测试、封存 relay、API 及数据库已停止，57240–57259 无监听。Actions 使用 0。

协调复核采用后继 W663 `b396445131a330d1a8ddd4cc057d39dc4cb68b6c` 作为集成基线。W663 的设备监控、专用测试和状态记录与本次十个受测文件完全独立，十份已测内容在组合后逐一相同，沿用上表实测与本地发布启动结果。正式矩阵维持 26 PASS / 1 NOT_RUN，继续验证全局菜单实际多目标，以及普通 Web 退出等剩余权威。
