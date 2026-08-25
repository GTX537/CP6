# DEV 双模式发布（本机白天测试环境）

`azure-pipelines-dev.yml` 把已成功的 `GTX537.CP6/main` CI Run 发布到本机 Docker Compose 项目 `cp6-dev`。同一条 Pipeline 同时支持自动与手动模式，但初始只开放手动发布。

这是一条本机学习/白天同事测试链，不是生产发布权威，不替代 GitHub R2/GHCR，也不能把本机重新构建的镜像推广到 UAT 或 PROD。

## 两套本机环境的固定边界

| 项目 | 私人本地开发 | Pipeline 管理的白天测试 |
| --- | --- | --- |
| Compose project | `cp6` | `cp6-dev` |
| 数据库 | Docker 中的 `CP6DB` | 宿主机 SQL Server 的 `CP6_DEV` |
| Web/API | `8080` / `9991` | `18080` / `19991` |
| 公网 | 旧 `cp6-cloudflared`，仅在切换前保留 | `cp6-public-tunnel`，切换后服务 `cp6.uk` |
| 数据用途 | 个人开发数据 | 可清理、可重建的同事测试数据 |

任何 DEV CD、Tunnel 或快照工具都不得执行 `docker compose down -v`、`docker volume prune`，不得删除 `cp6_cp6-db-data`，不得自动把 `CP6_DEV` 覆盖或合并到 `CP6DB`。

## 自动和手动模式

Azure Pipeline 变量控制行为：

| 变量 | 初始值 | 作用 |
| --- | --- | --- |
| `CP6_DEV_AUTO_DEPLOY_ENABLED` | `false` | `false` 时 completion trigger 只完成分类并安全跳过；手动发布仍可运行 |
| `CP6_DEV_PUBLIC_VERIFICATION_ENABLED` | `false` | 独立 Tunnel 切换前只验证本机；切换后设为 `true`，同时验证公网 API/Web 身份 |

自动模式只接受当前 `origin/main` 的成功 CI Run；同仓库 completion trigger 会从触发 CI 的提交启动。Pipeline 在分类阶段和取得 DEV 环境锁之后各检查一次 `origin/main`；如果排队或构建期间已有更新的 `main`，旧自动任务会标记为 superseded 并安全跳过，不因旧 checkout 假红，也不覆盖新版本。

手动模式在 **Run pipeline → Resources** 中选择一个已成功的 `GTX537.CP6/main` Run。Pipeline 会通过 Azure REST 回读其 `completed/succeeded`、分支和完整 Git SHA。选择旧 Run 属回退操作，必须先把 `CP6_DEV_AUTO_DEPLOY_ENABLED` 设为 `false`，避免回退完成后又被新自动任务立即覆盖。

发布身份固定为：

- Release version：`0.0.0-dev.<CI Run ID>`；
- API：`cp6-api:dev-<完整 Git SHA>`；
- Web：`cp6-web:dev-<完整 Git SHA>`；
- 禁止 `latest`。

所选提交在隔离 Git worktree 中构建，流水线编排脚本始终来自当前 `main`。完整 SHA Tag 用于人类检索，构建任务同时通过 Docker `--iidfile` 捕获该次构建的不可变 `sha256` image ID；部署和证据都使用这个 ID，避免另一条任务重写同名本机 Tag 后部署错镜像。当前阶段镜像只保存在这一台 Docker Desktop；跨机器或长期精确回退必须改为消费现有 GHCR 的不可变 digest，不能增加第二个 Registry 真相源。

## 每次实际发布的顺序

```text
成功 main CI Run
  → 校验 Azure Run / 完整 SHA / 当前 main / 自动或手动策略
  → 从所选提交构建一次 commit-addressed API/Web，并捕获不可变 image ID
  → 进入 Azure Environment cp6-dev 的 exclusive lock
  → 锁内再次检查自动 Run 是否仍对应当前 main
  → BACKUP CP6_DEV WITH COPY_ONLY, COMPRESSION, CHECKSUM
  → RESTORE VERIFYONLY WITH CHECKSUM
  → 启动并等待 Redis/RabbitMQ/Kafka
  → 停止旧 Web/API（短维护窗口）
  → 一次性 db-init 前向迁移
  → 启动 API，校验运行容器 image ID 与 live/ready/release
  → 启动 Web，校验运行容器 image ID 与 release.json
  → 可选校验 cp6.uk/api.cp6.uk
  → 发布 cp6-dev-evidence
```

预计维护窗口为 1～3 分钟。迁移失败或新 API 无法就绪时失败关闭；脚本不会把旧 API 自动套回已经前移的 Schema。处理方式是保留备份证据、修复问题并前滚。若确需数据库恢复，必须另行人工授权和停机，不由 Pipeline 自动执行。

## 宿主机运行前检查

本机同时运行浏览器、IDE、Docker、通用 CI Agent 和宿主 SQL Server，手动发布前应保留可用内存并确认
`KOUSQLSERVER` 能完成真实 SQL 查询；端口监听或 Windows Service 显示 `Running` 不能替代查询验证。
若 Application 日志出现 MSSQL 701/17300，或登录前握手/简单元数据查询超时，先停止发布并恢复 SQL
实例，禁止连续重试 db-init。2026-08-25 的首次失败正是服务进程仍在但已无法创建新系统任务。

CP6 当前不使用 PolyBase/Launchpad；故障恢复后这三个依赖服务保持停止以释放内存，但 StartMode 仍为
Automatic。是否永久禁用属于独立的宿主机管理决定，不能由 Pipeline 或本仓库脚本静默修改。

## Azure 一次性外部配置

仓库合入 `main` 后：

1. 从 `/azure-pipelines-dev.yml` 创建或更新 `CP6 DEV CD`。
2. 只授权该 Pipeline 使用 `CP6-Deploy`、`cp6-dev-secrets` 和 Environment `cp6-dev`；不要打开全局 Open access。
3. 在 Pipeline variables 新增两个非 Secret 变量并都设为 `false`：
   - `CP6_DEV_AUTO_DEPLOY_ENABLED`
   - `CP6_DEV_PUBLIC_VERIFICATION_ENABLED`
4. 在 `cp6-dev-secrets` 新增锁定 Secret `CP6_DEV_DB_BACKUP_PASSWORD`；原四个 Secret 继续保留：migrator、runtime、RabbitMQ、JWT。
5. 在 Environment `cp6-dev` 的 Approvals and checks 添加 **Exclusive lock**。YAML 的 `lockBehavior: sequential` 只定义排队行为，不能代替资源侧 Lock check；部署脚本的 Windows 全局互斥锁是防止本机旁路并发的第二道保护，也不能代替 Azure 资源锁。
6. 确认项目 Build Service 可以只读查询 CI Build；分类任务使用 `System.AccessToken` 回读所选 Run，不把 Token 写入证据或日志。

## `CP6_DEV` 备份账号与目录

使用独立 SQL 登录 `cp6_dev_backup`，不要复用 runtime、migrator 或 `sa`。由管理员在 SSMS 中创建强随机密码，并完成：

```sql
USE [CP6_DEV];
CREATE USER [cp6_dev_backup] FOR LOGIN [cp6_dev_backup];
ALTER ROLE [db_backupoperator] ADD MEMBER [cp6_dev_backup];
USE [master];
GRANT CREATE ANY DATABASE TO [cp6_dev_backup];
```

`db_backupoperator` 允许备份 `CP6_DEV`；`RESTORE VERIFYONLY`/`FILELISTONLY` 在现代 SQL Server 需要 `CREATE DATABASE` 权限，因此该账号仍不是 runtime 身份，也不加入 `sysadmin`。参考 Microsoft 的 [BACKUP 权限](https://learn.microsoft.com/en-us/sql/t-sql/statements/backup-transact-sql) 与 [RESTORE VERIFYONLY 权限](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql)。

SQL Server 服务账号还必须对 `C:\CP6Backups\CP6_DEV` 有读写权限；部署 Agent 必须能读取该目录并安装 `sqlcmd`。备份脚本先查 PATH，再查 Go sqlcmd、ODBC 18 与 ODBC 17 的标准安装目录，因此不依赖交互用户的用户级 PATH；DEV CD 在构建候选前运行 7 场景 resolver/失败恢复行为测试。Pipeline 不在命令行传密码，而是临时使用进程级 `SQLCMDPASSWORD`，结束后恢复原值。

2026-08-25 宿主机已确认 ODBC 17 `sqlcmd`，并创建/收紧备份目录 ACL；最小权限
`cp6_dev_backup`、锁定 Azure Secret 和服务身份 Readiness Run #89 均已验收。DEV CD Run #94/#95
已实际生成 CHECKSUM 备份并通过 RESTORE VERIFYONLY。

当前工具不会自动删除任何 `.bak`。在尚未建立并验收保留策略前，定期人工检查 `C:\CP6Backups\CP6_DEV` 的剩余空间；不得为了腾空间让 Pipeline 自动清空目录或删除唯一可用备份。

## 三次手动验收后再开自动

截至 2026-08-25 的外部运行记录：

| Run | 模式/结果 | 是否计入三次成功 | 证据结论 |
| --- | --- | --- | --- |
| `#93` / `dev-20260825.1` | completion trigger；Succeeded | 否 | `CP6_DEV_AUTO_DEPLOY_ENABLED=false` 时 Build/Deploy 均为 Skipped，证明自动门安全关闭 |
| `#94` / `dev-20260825.2` | Manual；Failed | 否 | 备份与 VERIFYONLY 成功；宿主 `KOUSQLSERVER` 已有 701/17300 内存耗尽事件并处于退化状态，db-init 元数据查询超时，API/Web 未启动 |
| `#95` / `dev-20260825.3` | Manual；Succeeded | **是，1/3** | SQL 服务恢复后完成新备份、迁移、不可变镜像核对、本机健康与 Pipeline Artifact 归档 |

Run #95 发布 `0.0.0-dev.92` / `47ca8441898af69d1e66bc1acb6c51129dbe9c18`；API/Web
分别在 `127.0.0.1:19991` / `127.0.0.1:18080` Healthy。根 `cp6` 七个容器 ID 与 Run 前一致。
当前 `CP6_DEV_AUTO_DEPLOY_ENABLED=false`、`CP6_DEV_PUBLIC_VERIFICATION_ENABLED=false`。

每次手动发布都必须保存：

- Azure Run ID 和 Environment deployment history；
- `cp6-dev-evidence/database-backup.json`：文件长度、SHA-256、CHECKSUM 和 VERIFYONLY 结果；
- `cp6-dev-evidence/deployment.json`：触发模式、CI/CD Run、镜像 ID、迁移和本机/公网验证；
- `19991` live/ready/release 与 `18080/release.json` 的一致完整 SHA。

连续三次手动发布成功、exclusive lock 生效、备份可读且没有根 `cp6` 环境受影响后，才把 `CP6_DEV_AUTO_DEPLOY_ENABLED=true`。任何旧版本手动回退前先重新关闭自动。

## 公网 Tunnel 的一次性切换

DEV CD 不会自动切换 Cloudflare。切换前 `cp6.uk` 仍可能指向根 `cp6`，因此 `CP6_DEV_PUBLIC_VERIFICATION_ENABLED` 必须保持 `false`。

```powershell
# 1. 只读预检 cp6-dev、路由和凭据
.\scripts\Invoke-Cp6PublicTunnel.ps1 -Action Validate

# 2. 明确停止旧 connector；只停 Tunnel，不停根数据库或应用
.\scripts\Invoke-Cp6DaytimeServer.ps1 -Action ClosePublic

# 3. 启动独立 connector，并核对当前 DEV SHA
.\scripts\Invoke-Cp6PublicTunnel.ps1 -Action Start -ExpectedGitSha <40-character-sha>
```

脚本发现 `cp6-cloudflared` 仍在运行时会拒绝 Start，避免同一 Tunnel 同时存在两个 connector 导致流量随机分配。新 connector 属 Compose project `cp6-public-tunnel`，只加入 `cp6-dev_default`；Stop 只执行 Compose `stop`，不删除网络、容器或卷。

切换验收通过后，把 `CP6_DEV_PUBLIC_VERIFICATION_ENABLED=true`。若需回退 Tunnel，先 Stop 新 connector，再明确启动旧 connector；两者不得同时运行。

## DEV 数据带回私人本地的唯一允许方式

先手工导出经过 CHECKSUM/VERIFYONLY 的 `CP6_DEV` 快照：

```powershell
.\scripts\Export-Cp6DevSnapshot.ps1
```

再显式确认旁路恢复：

```powershell
.\scripts\Import-Cp6DevSnapshot.ps1 `
  -SnapshotPath C:\CP6Backups\CP6_DEV\CP6_DEV_yyyyMMdd_HHmmss_fff_xxxxxxxx_UTC.bak `
  -TargetDatabase CP6DEV_IMPORT_yyyyMMdd_HHmmss
```

导入只允许新建 `CP6DEV_IMPORT_yyyyMMdd_HHmmss`，会先 `RESTORE VERIFYONLY`，拒绝已存在目标，且没有 `WITH REPLACE`。之后是否挑选数据合并到个人开发库属于新的人工数据任务，当前工具不会自动执行。

## 当前完成口径

仓库能力、Azure 定向权限/Secret/Exclusive lock、Readiness 和首次手动 DEV 发布均已完成。当前只能描述为
“手动 DEV 验收 1/3”；在另两次手动成功、独立 Tunnel 切换和公网身份验证实际完成前，不得写成
“DEV 自动部署已启用”或“cp6.uk 已切到 cp6-dev”。
