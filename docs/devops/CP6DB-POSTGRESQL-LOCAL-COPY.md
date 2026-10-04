# SQL Server CP6DB 的 PostgreSQL 本地测试副本

本流程是一次已明确授权的真实数据迁移，接续 DB-COMPAT-01；既有双库验收未包含客户数据搬迁。目标是独立 PostgreSQL 测试数据库，不改变原 SQL Server CP6DB、既有 API 的连接或生产环境。

2026-10-04本机实际执行已提交330应用表67,620行及354原值归档表67,810行；684映射摘要/行数、181FK、6序列通过，API五个200及匿名profile401通过。[原始安全摘要及验收范围](../audits/2026-10-04-cp6db-postgresql-local-copy/README.md)记录失败、复用和旧历史时区待确认项。

## 查看与启动

实际本机库名、连接、原始备份位置和运行目录记录在忽略目录的 `private/settings.json`；不要提交该文件。迁移公共记录只包含统计与摘要。迁移结束后保留 PostgreSQL 库，不使用验收 runner 的成功即删除逻辑。

在 pgAdmin 连接相同的 loopback PostgreSQL 实例，右键 `Databases` → `Refresh`。选本次 `cp6db_pg_test_日期_owner`，展开 `Schemas → public → Tables` 查看应用数据；还存在 `crm_identity` / `erp_integration` 等应用 schema。`legacy_sql_*` 保留 SQL 源表全部列和原值，包括历史迁移身份，供对账查询，不参与 CP6 运行。

使用当前兼容版 checkout 的实际绝对脚本路径，传入本次私有运行目录：

```powershell
pwsh -NoProfile -File <兼容版目录>/scripts/Start-Cp6PostgreSqlLocalCopy.ps1 `
  -RunDirectory <本机迁移运行目录> -Port 5188
```

API 仅监听 `http://127.0.0.1:5188`，Swagger 在 `/swagger`。启动脚本读取已有本机连接，秘密只进入子进程环境；`Ctrl+C` 停止本次 API，不影响其他实例。没有修改原 API 的连接配置。用户、角色和密码哈希随数据库保留，登录沿用原业务账号；测试实例使用独立 JWT 身份，不接受旧实例 token。

迁移副本含原始队列、租约和待处理业务记录，因此本地入口禁用 hosted services、ERP/CRM 外联、远程 CAD worker、Redis/Kafka/RabbitMQ。这适合手动数据库/API 功能测试；不把它声明为后台投递/外部系统或生产验收。

## 实施与对账

1. 对原 SQL Server CP6DB 做 `COPY_ONLY,CHECKSUM` 全库备份及 `RESTORE VERIFYONLY`。源 schema/业务数据不修改。
2. 恢复到新命名的 SQL 副本；禁止 `WITH REPLACE`，禁止覆盖现有文件或数据库。在副本执行 CP6 当前前向初始化，使其与当前应用结构相符。原始备份及升级前/后清单保留。
3. 新建 PostgreSQL 目标并写本次 owner comment，使用相同已验证 API 发布物完成四条 PostgreSQL 迁移链及初始化。
4. `CP6.DatabaseTransfer` 只接受私有文件中同一次迁移的 loopback SQL 副本/PG 新库身份。实际 PostgreSQL 初始化成功后才开始单事务导入。SQL history 只进入归档，不覆盖 PostgreSQL 的 history。
5. 明确 PostgreSQL COPY 字段类型，导入保留 ID、NULL、Unicode、原始 bytea 和 decimal。每张源表也完整归档；datetime2 归档为完整 ISO 文本、datetimeoffset 保留原时区偏移。业务 timestamp 按 PostgreSQL 1 微秒精度及 CP6 UTC 字段契约转换，归档不舍弃 SQL 原精度。
6. 导入期间只在新目标事务内延迟 FK、暂时关闭用户触发器；全部行导入后立即检查外键，恢复原延迟属性及触发器。按实际最大 ID 重置 identity 序列。每表校验行数及所有映射列的规范化行摘要多重集，保留重复行语义。
7. API 只做必要的新库实际启动、HTTP 与对应数据库身份检查。记录复用发布物原 SHA/逐文件哈希和源码输入，不冒称重新构建或新的完整双库矩阵。

迁移私有日志可能含业务字段，仅本机保留。SQL 特有存储过程与实例级对象在原备份/SQL 副本中保留，未声称自动转换 T-SQL 为 PG 函数。附件/文件服务不是数据库归档的组成部分，测试入口使用独立文件根目录。

失败的导入事务回滚，初始化/导入的原失败日志保留。完成报告存在时拒绝再次覆盖。SQL 副本及原备份暂留用于复核；不按数据库名前缀批量删除，也不清理已有用户环境。

## 已知日期事项

原 CP6DB 存在两条 `T_IntegrationEvent.OccurredAtUtc` 为空的历史事件。源 `CreateDate` 原值和原备份保留；其历史时区需据用户确认再校正本次副本的 UTC 排序字段，不能把临时 UTC 假定描述为已确认的原业务时区。
