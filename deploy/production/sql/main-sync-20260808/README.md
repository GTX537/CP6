# 2026-08-08 `main` 同步数据库包

此目录用于把 `origin/main@adbe7bcd` 对应的数据库升级到 `codex/main-sync-20260808` 候选。它不是自动生产部署授权。

## 文件与顺序

1. `00-preflight.sql`：确认目标不是系统库、`origin/main` 基线迁移存在、Space schema/history 没有漂移，并失败关闭旧 `ModelAssetId` 和活跃 Publish slot。
2. `01-cp6-context.sql`：`CP6Context` 从 `20260714075419_WfsSubFlow` 到 `20260802192420_SpaceE11S05ExecutionReceiptsCompensation`，14 个幂等迁移。
3. `02-space-context.sql`：`SpaceContext` 从空历史到 `20260808164544_SpaceE13RackGenerationProfiles`，36 个幂等迁移，历史表为 `__EFMigrationsHistory_Space`。
4. `03-postflight.sql`：逐项核对 14 + 36 个候选迁移均已记录。

## SHA-256

| 文件 | SHA-256 |
| --- | --- |
| `00-preflight.sql` | `557352eda6b7c6c113d5230c0b3bf2c7bc395b778663b75b39bd993ef941ae14` |
| `01-cp6-context.sql` | `2a2e4f26d3442735224c81b8b38d60fef9fdba335b87ac817fad1a798677ee93` |
| `02-space-context.sql` | `446dde2224de653a7d9e11b94a5cbbffffa22acbe0e172432a2285a7b378152b` |
| `03-postflight.sql` | `a907f572ae01bf216d959a46d58126613444d155d0778b88ee25e8e3bfb3ecad` |

## 执行要求

- 先对目标数据库做可恢复备份，并在该备份的恢复副本上演练。
- 使用有 schema migration 权限的专用账号；不要把密码写入脚本或命令历史。
- `sqlcmd` 必须使用 `-b`，使任一 `THROW` 或 SQL 错误返回非零退出码。
- 按顺序执行四个文件，再重复执行四个文件一次；第二轮必须继续成功且 postflight 仍为 `PASS`。
- 多个 Space `Down` 为 forward-only；失败后按更高版本 forward-fix，不执行破坏性降级。

示例（连接参数通过受保护环境提供）：

```powershell
sqlcmd -S $env:CP6_SQL_SERVER -d $env:CP6_SQL_DATABASE -E -b -i 00-preflight.sql
sqlcmd -S $env:CP6_SQL_SERVER -d $env:CP6_SQL_DATABASE -E -b -i 01-cp6-context.sql
sqlcmd -S $env:CP6_SQL_SERVER -d $env:CP6_SQL_DATABASE -E -b -i 02-space-context.sql
sqlcmd -S $env:CP6_SQL_SERVER -d $env:CP6_SQL_DATABASE -E -b -i 03-postflight.sql
```

生产执行前还必须完成应用备份、发布冻结、WMS 发布/恢复对账和受保护环境审批。

## 数据库演练证据

- 2026-08-08 在随机命名的 SQL Server LocalDB 临时数据库上，先以 EF 逐迁移推进到 `origin/main` 的 `20260714075419_WfsSubFlow` 基线。
- 随后按顺序连续执行本目录四个文件两轮：`ROUND_1=PASS`、`ROUND_2=PASS`。
- 最终 migration history 精确核对为 `CORE_CANDIDATE=14`、`SPACE_TOTAL=36`。
- 三条失败关闭路径分别验证：schema/history 漂移 `51083`、遗留 ModelAssetId `51000`、活跃 Publish slot `51020`。
- 2026-08-09 确认 CP6 尚无生产数据库，因此合并前恢复演练改用仓库内最新的已校验开发备份 `migration/database/CP6DB_20260718.bak`。
- 该备份已在隔离的 SQL Server 2022 容器中成功恢复；恢复点已包含 `origin/main` 的 `20260714075419_WfsSubFlow` 基线。
- 在恢复副本上再次连续执行整包两轮：`ROUND_1=PASS`、`ROUND_2=PASS`，两轮均精确核对为 Core 14 项、Space 36 项。
- 每次演练使用的临时数据库均在验证后删除。未来首次建立生产数据库或执行生产升级时，仍必须另行完成生产备份、恢复副本演练、发布冻结和审批。
