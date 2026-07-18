# 数据库记忆

## 技术与入口

- SQL Server 2022，EF Core Code First；复杂报表使用 Dapper。
- `CP6.Core/EFDbContext/CP6Context.cs` 是 DbSet、关系、索引、过滤器的核心入口。
- `CP6.Core/Migrations` 是结构历史；2026-07-18 实扫 113 个迁移文件（不含 Designer）。
- 应用启动会执行迁移及幂等种子，接线集中在 `CP6.WebApi/Program.cs`。

## 当前开发数据库

| 数据库 | 用途 | 迁移备份 |
|---|---|---|
| `CP6DB` | 主开发库 | `migration/database/CP6DB_20260718.bak` |
| `CP6DB_OA` | OA/WF QA/隔离验证 | `migration/database/CP6DB_OA_20260718.bak` |
| `CP6DB_SpaceQA` | Space QA/隔离验证 | `migration/database/CP6DB_SpaceQA_20260718.bak` |

三份备份均于 2026-07-18 使用 `COMPRESSION, CHECKSUM` 生成并通过 `RESTORE VERIFYONLY WITH CHECKSUM`。哈希见 `migration/database/SHA256SUMS.txt`，文件由 Git LFS 管理。

## 核心约束

- 多租户表按 `TenantId` 隔离，查询过滤器不可随意绕过。
- 大部分业务表有创建/修改审计、软删除及乐观锁字段。
- WMS 库存余额来自库存移动/台账语义，禁止直接 SQL 更新库存。
- WF 运行态包含实例、Token、Task、History 等一致性关系，不能用原始 INSERT 模拟引擎推进。
- `Sys_Lang` 是重要运行资产；DB 备份与 `deploy/seed-data` 双重保障。
- DataProtection key ring 已持久化进数据库，随 `.bak` 迁移。

## 迁移规则

1. 新机先装 Git LFS 并拉取备份。
2. 校验 SHA-256。
3. 只启动 SQL 容器；停止 API 连接。
4. `RESTORE FILELISTONLY` 后还原匹配数据库。
5. 再启动 API，让迁移幂等补差。
6. 校验 `__EFMigrationsHistory`、登录、权限、五语词条和后台 worker。

完整命令见 `migration/README.md` 与 `deploy/runbook.md`。绝对不要在需要保留数据时执行 `docker compose down -v`。

## 迁移开发规范

- 一个业务波若声明“恰一次迁移”，后续任务不得偷偷追加实体变化。
- 修改实体后必须检查 model snapshot 与 `has-pending-model-changes`。
- 不手改已有迁移来迁就活库；新增变更应生成新迁移并测试升级路径。
- 数据种子必须幂等，明确 insert-only 或 upsert 语义，不覆盖管理员手工配置。
