# CP6DB 本机 PostgreSQL 数据副本

**Goal:** 将已明确授权的本机 SQL Server CP6DB 迁移为长期保留的 PostgreSQL 测试副本，保护原库和原运行环境。

**Architecture:** 从确认的 main 272e92cfc1c6c177d0227aa19815d603eef1a99e 开始。对 localhost\\KOUSQLSERVER 的 CP6DB 执行 COPY_ONLY/CHECKSUM 备份与 VERIFYONLY，恢复到唯一的新 SQL 副本。仅对副本执行当前 CP6 DatabaseInit，随后在唯一新建的 loopback PostgreSQL 库初始化同一发布物的四条迁移链。使用类型明确的 Binary COPY 导入，保留 SQL 专属对象的数据归档，单事务验证行数、每列规范化摘要、外键、触发器及自增序列。独立 API 冒烟使用本地端口并停用后台执行/外部集成；保留目标库、备份和私有本机运行配置。

**Tech Stack:** .NET 8、Microsoft.Data.SqlClient 5.1.9、Npgsql 8.0.8、SQL Server 2022、PostgreSQL 18、现有已验证 CP6 API 发布物。

- [x] 只读盘点源库、数据类型、迁移历史、工具与凭据位置。
- [x] 校验现有发布物原始哈希及当前源码输入，复用真实已成功构建，不重跑无关测试。
- [x] 备份校验、独立 SQL 恢复，记录源库与副本身份及源历史。
- [x] 仅在新 SQL 副本前向初始化；新 PostgreSQL 库初始化，比较列映射。
- [x] 对缺失表/源专属列建立明确归档；不得将 SQL EF history 放入 PostgreSQL 的当前 history。
- [x] 单事务导入，保留 ID、NULL、Unicode、decimal、日期及字节值；修复序列，校验全部映射数据及约束。
- [x] 独立 API 实际启动与 HTTP 检查；后台服务禁用，避免迁移来的任务自动执行。
- [x] 提供 pgAdmin 路径、本地启动/停止入口、连接切换配置及真实未验证范围。
- [ ] 一次任务级审查、必要定向验证、更新四份状态文档；正常交付远端 main，无远程 Actions 或生产部署。

初始只读观察：331 张源表，估计 67,774 行；SQL 事务快照 OFF、RCSI ON。源本身不执行 schema 迁移、数据修改、身份调整或连接切换。恢复仅允许新副本名；任何已有目标均拒绝覆盖。执行失败保留证据，不将半完成目标记为可用。
