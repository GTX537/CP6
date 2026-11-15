# W616-01 Space 独立 FileScan 状态提交

2026-11-15 UTC。开发基线 `7e0b771d894de182b94b95888ee4c07e6c5d585f`，受测源码 `4bedb0ba4fd9fbe09bc580f5802541c526ab1007`。隔离 PostgreSQL 18.6、签名 Main Kestrel、独立扫描操作端、实际文件系统、租约账本和 ManagedFileSafetyScanner。恶意内容引擎采用模拟，输入为真实上传的模拟 DXF 及其 SHA-256。 [缺陷 #1137](https://github.com/GTX537/CP6/issues/1137) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

- BeginScan 与 FinishScan 分别在原 PostgreSQL 连接取得 Main 共同根，再锁定原 Job 与文件，核对当前租户、持有的租约及取消。提交前复核时钟。独立操作端通过共用上下文取得本库当前权威。文件读取和扫描执行在两个事务之间。
- 固定基线两阶段均复现两种暂停顺序未共同排序。暂停先提交仍改变文件、来源或 Job，真实提交应答丢失被回滚错误掩盖。追加用真实时钟证实旧 Finish 到期后仍提交。四个正常拒绝、暂缓与取消对照通过。早期外置夹具属性名和候选 LINQ 变量名错误已更正并保留日志。
- 最终 **11 项原生、102 项契约、10 项相关集成、8 项单测**通过，零跳过。扫描开始已提交后暂停时，保留原 Scanning。完成的提交应答丢失后，同一尝试在暂停期间读取原结论，等价规范化输入不触发第二次扫描或写入。核对两个同文件来源和 File、Job、Attempt 的原生记录及原字节哈希。相关集成沿用隔离锁文件与既有 Dispatch 夹具排除配置。
- 本地发布关联 **310 文件 SHA-256**、三个受测源文件和七份逐字节一致的程序集。实际存活 200、匿名私有文件状态 401。本地测试和发布进程已停止，证据保存在 `D:/CP6-test-artifacts/W616-01`。Actions 与部署均为零。

FileScan 沿用独立运行边界。真实扫描引擎和沙箱的实际部署资格另验。CAD 来源最终化、Excel 预检和后台 Apply、刊发执行与 WMS 投影、财务后台和 CRM 仍待余项证据。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
