WMS LPN 创建/拆包/拆分/合并及标签领取/完成，在无连接池 SQL Server 中因自建 ambient 事务重开连接而触发 DTC；已有调用方本地事务也会发生作用域冲突。六处独立调用改用同一 DbContext 的 ReadCommitted 本地事务，覆盖业务写入、查询、事件和幂等回执，保留调用方本地事务所有权及既有 ambient Required/abort 语义。

## 关联

Refs #163；父任务 #134。合并后冒烟和精确自有库清理后再关闭 BUG163。

## 改动

新增17个真实双库回归，111个公共原件按字节归档：[审计记录](docs/audits/2026-10-03-bug-163-wms-local-transactions/README.md)。公开接口、依赖、迁移、Pack/Move和工作流未变更。一次事务专项审查及文档/归档增量核对无P0/P1/P2阻断。

## 验证

- [x] SQL Server、PostgreSQL 各原8＋新增17，共25/25、零失败/跳过；2492源码输入、378运行文件两库相同，PG复用已核对字节的真实SQL构建。0 error、1个原有xUnit2017 warning。
- [x] 原 LPN 全树失败用例已通过；有效 RED SQL17=3P/14F、PG17=10P/7F。真实保存后的故障/取消、回执、回放及调用方事务通过。原WP6失败、辅助入口拒绝及两次种子中间失败保留。
- [x] 四个独立命令的trace验证无ambient、事务内唯一物理连接；不能自动化的额外验收不适用。显式enlist分支只做源码审查，未声称独立原生验收。

正常远端交付、合并后冒烟和清理仍待闭环。父任务WP6最终Matrix/Application和恢复验收继续进行。本次没有远程Actions运行或生产部署。