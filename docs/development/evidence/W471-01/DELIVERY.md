# W471-01 — 关闭候选上的实际 FileContent Owner

WP02-02 从远端主线 `ad910410a190721c5c5ab676200d11c63873f235` 复用 DEV-2。新增定向验收，复用 W469 已导入的关闭候选、W468 原文件根和 W466 实测的 .NET 应用程序集。只编译此验收驱动。应用源码、依赖和三份核心程序集与该实测版本一致。以两个原模拟业务主体调用实际 `FileContentOwnerV1`，由当前数据库权限、文件和 Engineering Owner 共同校验。[实际验收入口](../../../../eng/wp02-02/rma-tenant-archive/VerifyRestoredFileContent.cs) · [关闭候选](../W469-01/DELIVERY.md) · [原文件](../W468-01/DELIVERY.md)

- 本地 PostgreSQL 18.6 目标 OID 71191，仅启动此前已停止的独立副本。通过实际当前及指定版本 `ReadAsync` 核对两个租户的同文件 ID、独立当前主体、Etag、状态、原事件及 Engineering 父版本。两次 `OperationAsync(close=false)` 返回各自原 `COMMITTED` 回执，结果序列化摘要与各自归档原 bytes 一致。
- A 的实际 `DownloadAsync` 按原键从 W468 读出 45 字节，内容 SHA-256 `540842f6e6966d8f33243ad24bf8a88b74a408553d229e233a856d55c959fb76`，与私有原文件逐字节相同。B 使用 A 的摘要请求同文件 ID，实际返回 `412 FILE_REFERENCE_CHANGED`。B 自身快照、父版本和原操作均成功读取。
- 前后 15 个关系行数及 SHA-256、数据库 ACL 与标记一致。原候选回执仍为同一 705 字节，SHA-256 `5eeb6964480f4678ccb9ee4f12749c98914fe29d55a013b1a4cfa82bacffac11`，`restore_admission` 仍为空，`recoveryReady=false`。W429 原物理目录一直停止且摘要一致。

实际编译、运行、数据库前后回读和停机证据在 `D:/CP6-local-test/CP6-72H-20261109/W471-01`。在同一私有夹具以 `dotnet exec <driver> <sealed-assemblies> <fixture-json>` 运行。使用模拟业务数据。退出 0 后关闭专属 PostgreSQL，无应用监听。GitHub Actions 和部署均未运行。

W470 已由 #938 采用并释放，实际 12.45–12.50 分钟。本窗验证关闭候选内应用 Owner 消费。正式候选资格、宿主 HTTP 和切换各按其验收范围处理。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。
