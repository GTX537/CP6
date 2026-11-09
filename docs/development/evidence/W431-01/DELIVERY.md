# W431-01 · 原 PGT-015 消息键并发

DEV-1 于 2026-11-09 22:23:10 UTC 从 Main #862 领取。原 PGT-015 要求实际 A/B 争用原全局消息键、消费者和队列边界。本窗准备好原生验证输入，正式 PGT-015 保持 `NOT_RUN`。[原验收项](../../acceptance/postgresql-tenancy.json)

W348 原 CLI 在 PostgreSQL 18.6 的 `w348_provision_verify` 实际安装 ERP 与 Identity 两个原消息域。源 system identifier `7706064738685665676`，库 OID `16386`，端口 `55557`。普通 A/B 登录的角色 OID 为 16384/16385，各自读取本租户的原 Aggregate。本次基线与 W348 的两套迁移和 RLS 源一致。只复制该已停机模拟库，单一副本逻辑大小 51,466,579 字节。源和副本的控制文件摘要一致，实际 `pg_ctl status` 均为 3。[W348 原结果](/D:/CP6-local-test/CP6-72H-20261109/W348-VERIFY/DELIVERY.md) · [本窗复制凭据](/D:/CP6-local-test/CP6-72H-20261109/W431-01/message-clone/copy.json)

验证驱动在同一私有 PG32 核四张原表的唯一键和 RLS。B 持未提交原键，A 先写本租户伴随行再争用同键。观察实际 `transactionid` 阻塞后提交 B，核 `23505`、同事务 `25P02`、失败 xid 和 A 新事务读写。回读完整 B 原行及原 payload 或摘要，并核对 Inbox 不同 consumer 与 ERP/Identity 两个原队列域的独立提交。临时 SCRAM 凭据限于隔离副本。既有 WP02-07 实际 PostgreSQL TRX 20/20 保留其受控接收范围，SHA-256 `ded4b853f9e0d38a222694ebe9615cc9f93d3f7bc3d34cf5712ff22d1381d072`。[私有驱动](/D:/CP6-local-test/CP6-72H-20261109/W431-01/run-w431.py) · [受控接收原结果](/D:/CP6/CP6.Platform/docs/development/evidence/WP02-07/test-results/inbox-postgresql-reviewed.trx)

首轮 `native-01` 退出 75。24 个实际样本为 933–998 MiB，共 36.421 秒，低于连续两次和临启动各 1,000 MiB 的门槛。源及副本的 `pg_control` 均保持 SHA-256 `84a434eb65de08de843ffb2d992500723a19881fc5ad26e197f411895d1e21f6`。副本尚无 postmaster，普通角色并发、事务和读权结果待执行。[资源原件](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-01/prelaunch.json)

接续同一分支、原副本和独占端口 55557。确认两次及临启动物理内存至少 1,000 MiB、可提交至少 19 GiB 后，运行 `python -B D:/CP6-local-test/CP6-72H-20261109/W431-01/run-w431.py native --attempt native-02`。资源门槛与业务 SQL 结果独立记录。集中审查后再按原要求决定正式采用。

集中审查核对原键、锁、事务和停止路径。启动前再次核物理路径并确认与原件分离。临时认证规则撤除后要求维护角色和 A/B 的无密码探针实际拒绝。若启动后仅因资源退出，按该次正常停机记录的副本指纹复用。最终驱动 SHA-256 `1921fb0ce8c67cf28aa22d018db53f09471ff6cd1dd9e70a26c48b15248d4c3c`，CLI 预检退出 0。
