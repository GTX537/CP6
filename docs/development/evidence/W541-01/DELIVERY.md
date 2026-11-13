# W541-01 · 空间现行 HTTP 写入口

2026-11-13 UTC，Main 基线 `642c3cf4d599401a599e56abadb3e23b4253a184`，实测源码 `d75cf1c6a20233109572e65e648023e31fe30fe7`。 [Issue #1021](https://github.com/GTX537/CP6/issues/1021) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646)

连接体与停靠点 5 个入口、模板 4 个入口、分析配置与手工 ABC 重建 2 个入口在请求的 Core 事务内采用 `cp6_authentication_v1.acquire()`。原 `space-floor:edit` 和 `space-control-tower:manage` 权限、空间设计切换检查、字段写入与现有请求审计继续适用。手工分析从同一个 Core Context 读取库存聚合和写入快照。 [连接体](../../../../CP6.WebApi/Controllers/Space/ConnectorController.cs) · [模板](../../../../CP6.WebApi/Controllers/Space/TemplateController.cs) · [分析](../../../../CP6.WebApi/Controllers/Space/SpaceAnalyticsController.cs)

## 实测

隔离 PostgreSQL 18.6、实际 Main Kestrel、模拟空间站点、楼层、连接体、停靠点、模板和 IAM 用户。通过独立连接和 `pg_blocking_pids` 核对业务表、权限表与共同根的等待。11 个入口均实测撤权先提交后返回 403，业务写集不变，重新授予后原请求成功。删除连接体、克隆模板和手工重建另验证业务先提交。连接体及停靠点一并删除、模板原副本及其克隆记录、手工快照均保留，旧权限再次写入被拒绝。两种顺序同时核对 IAM 版本推进、身份快照、Outbox、审计和原回执。 [并发回归](../../../../CP6.Tests/Erp/Pgt017SpaceAncillaryPostgreSqlTests.cs)

修复前 14 组均复现请求提前结束。修复后 **14 项 PostgreSQL 与 34 项组件测试通过**，0 失败、0 跳过。候选构建首次与仍在运行的基线共用程序集输出，Windows 文件锁导致构建失败。基线完成后重新构建通过，保留现有依赖告警。原始 TRX、构建和启动日志在 `D:/CP6-test-artifacts/W541-01`。本地发布 310 个文件，测试与发布 WebApi 的 SHA-256 同为 `dfdc1cfd65cd520a82c233f64fe63434596784452f7b953a625e9ee6a6e93467`。发布程序实际在隔离 PostgreSQL 建库，存活 200、匿名管理和空间请求各 401。测试和发布进程均停止。

## 余项

PGT-017 仍为 **NOT_RUN**，正式 PostgreSQL **26/27**，原询盘 **60/60**。空间剩余范围包括独立 Space Design 事务与定时 ABC 写入。旧 OA 批办、财务、采购、Stock 普通角色与 [Main/CRM 其余跨库写者](https://github.com/GTX537/CP6/issues/1012) 仍需继续。全量正式矩阵与生产采用各有独立状态。Actions 启动、重跑及取消为零。

分支与工作区见该任务 PR 和 `D:/CP6-test-artifacts/W541-01/main-finish.json`。
