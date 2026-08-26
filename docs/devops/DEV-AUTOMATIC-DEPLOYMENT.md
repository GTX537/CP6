# DEV 双模式发布（本机白天测试环境）

`azure-pipelines-dev.yml` 把已成功的 `GTX537.CP6/main` CI Run 发布到本机 Docker Compose 项目 `cp6-dev`。同一条 Pipeline 同时支持自动与手动模式；三次手动验收完成后，自动开关已启用。

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

- Release version：`0.0.0-dev.<完整 Git SHA>`；
- API：`cp6-api:dev-<完整 Git SHA>`；
- Web：`cp6-web:dev-<完整 Git SHA>`；
- 禁止 `latest`。

GitHub `client-contract` 在 hosted Runner 完成 API/Web/客户端/Android/R2 source 门禁后生成与完整 Git SHA 绑定、含逐文件长度和 SHA-256 的 Runtime Artifact。Azure 基础流水线只接受同仓库、同 SHA、指定成功工作流的未过期产物，验证 GitHub 归档摘要和内部 manifest 后转存为 `cp6-dev-runtime` Pipeline Artifact。DEV Pipeline 只下载所选成功 `main` Azure Run 的这一份 Artifact，再次核对身份、完整文件集合和哈希，由两个仅含运行时的 Dockerfile 封装镜像，不重复运行 .NET/Node 编译。所选提交仍在隔离 Git worktree 中物化，流水线编排脚本始终来自当前 `main`。完整 SHA Tag 用于人类检索，封装任务同时通过 Docker `--iidfile` 捕获不可变 `sha256` image ID；部署和证据都使用这个 ID，避免另一条任务重写同名本机 Tag 后部署错镜像。当前阶段镜像只保存在这一台 Docker Desktop；跨机器或长期精确回退必须改为消费现有 GHCR 的不可变 digest，不能增加第二个 Registry 真相源。

## 每次实际发布的顺序

```text
成功 main CI Run
  → 校验 Azure Run / 完整 SHA / 当前 main / 自动或手动策略
  → 下载并逐文件验证所选 CI Run 的 cp6-dev-runtime Artifact
  → 用 runtime-only Dockerfile 各封装一次 commit-addressed 镜像并捕获不可变 image ID
  → 进入 Azure Environment cp6-dev 的 exclusive lock
  → 锁内再次检查自动 Run 是否仍对应当前 main
  → 等待宿主机至少 2048 MiB 可用内存，并取得 3 次连续独立 SQL 登录成功
  → BACKUP CP6_DEV WITH COPY_ONLY, COMPRESSION, CHECKSUM
  → RESTORE VERIFYONLY WITH CHECKSUM
  → 启动并等待 Redis/RabbitMQ/Kafka
  → 停止旧 Web/API（短维护窗口）
  → 一次性 db-init 前向迁移
  → 启动 API，校验运行容器 image ID 与 live/ready/release
  → 启动 Web，校验运行容器 image ID 与 release.json
  → 可选校验 cp6.uk/api.cp6.uk
  → 发布 cp6-dev-evidence-attempt-<System.StageAttempt>
```

预计维护窗口为 1～3 分钟。迁移失败或新 API 无法就绪时失败关闭；脚本不会把旧 API 自动套回已经前移的 Schema。处理方式是保留备份证据、修复问题并前滚。若确需数据库恢复，必须另行人工授权和停机，不由 Pipeline 自动执行。

## 宿主机运行前检查

本机同时运行浏览器、IDE、Docker、轻量 Artifact 桥 Agent 和宿主 SQL Server，手动发布前应保留可用内存，确认
部署 Agent Readiness 已验证 Docker/Compose 与 SQL 工具，并确认
`KOUSQLSERVER` 能完成真实 SQL 查询；端口监听或 Windows Service 显示 `Running` 不能替代查询验证。
若 Application 日志出现 MSSQL 701/17300，或登录前握手/简单元数据查询超时，先停止发布并恢复 SQL
实例，禁止连续重试 db-init。2026-08-25 的首次失败正是服务进程仍在但已无法创建新系统任务。

2026-08-26 自动 Run #127 在候选封装期间收到主机内存已使用 `95.16%` 的 Agent 告警，随后首次
`cp6_dev_backup` 登录在 SQL prelogin 阶段超时。它在备份前失败，没有新 `.bak`、迁移或容器替换，
既有 #125 DEV 版本保持 Healthy；失败后的 8/8 独立 SQL 新连接均在 54～98 ms 内成功，排除了持久
Secret、权限或数据库状态错误。流水线现于锁内、备份前最多等待 600 秒：只有可用内存不少于
2048 MiB 且 3 次连续独立 SQL 登录成功才继续；否则失败关闭并发布 `backup-readiness.json`，不会
通过重试有副作用的 BACKUP 来掩盖宿主机压力。

CP6 当前不使用 PolyBase/Launchpad；故障恢复后这三个依赖服务保持停止以释放内存，但 StartMode 仍为
Automatic。是否永久禁用属于独立的宿主机管理决定，不能由 Pipeline 或本仓库脚本静默修改。

Docker Desktop 当前只有约 8 GiB WSL 内存。2026-08-25 Manual Run #98 的并行 Docker
`dotnet publish` 触发 OOM；加入单 MSBuild 节点、关闭项目并行/共享编译服务器后，Manual Run #101
仍在 Docker VM 使用率 95.83% 时必须取消。两次均发生在 Deploy 前，不计手动验收，但分别导致根
`cp6-db`/`cp6-api` 自动重启，证明只调低 Docker 内的编译并发不足以隔离根环境。

宿主机构建隔离合入后，CI #102、关闭状态的 completion DEV #104 和 Readiness #105 均成功。
Manual #106 因错误把 Run ID 当成 pipeline resource 版本而在 YAML 解析前失败，没有 Job、备份或部署，
不计验收。正确绑定 CI build number 的 Manual #107 在宿主 `dotnet publish` 工作集达到约 4.18 GiB、
可用内存降至约 0.62 GiB 且 `CP6_DEV` 新连接超时后按门禁取消；Deploy Skipped、备份仍为两份、根
`cp6-api`/`cp6-db` 基线不变，但旧 `cp6-dev-api` 因 SQL 超时重启至 RestartCount 17，因此也不计验收。

候选阶段现进一步改为复用 CI Artifact，彻底删除 DEV 中的第二次 .NET/Node 编译。真实 145,966,387
bytes API 与 7,473,275 bytes Web 产物本机验证为 587 个逐文件哈希，约 17 秒封装出两个不可变 image
ID；过程中根 API/DB 和旧 DEV API 的 ID、StartedAt、RestartCount 均未变化，`CP6_DEV` 仍 ONLINE，
最新迁移仍为 `20260811030108_CrmFoundation`。后续每次手动 Run 仍要记录根七容器三项元数据。#113/#115 进一步证明即使降低并发或拆项目，本机完整编译仍会压迫 SQL；因此编译已迁到 GitHub hosted Runner。Azure #116 在下载前安全失败，#117 已成功完成受认证下载、双层校验与 Artifact 发布，且 SQL/公网七容器基线不变。

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
| `#98` / `dev-20260825.5` | Manual；Canceled | 否 | 分类通过并选择 CI #96；API publish 内存告警后人工取消，Deploy Skipped。Docker OOM 导致根 `cp6-db`/`cp6-api` 自动重启，因此该次明确不合格 |
| `#101` / `dev-20260825.7` | Manual；Canceled | 否 | 正确选择 `main@76d0832e`；串行 Docker publish 仍把 VM 推到 95.83%，安全取消后 Deploy Skipped，无备份/迁移/DEV 镜像切换。根 `cp6-db` RestartCount 由 1→2、`cp6-api` 由 2→3，因此明确不合格 |
| `#104` / `dev-20260825.8` | completion trigger；Succeeded | 否 | 选择成功 CI #102；自动开关为 `false`，Build/Deploy 安全跳过 |
| `#106` / `dev-20260825.9` | Manual；Failed | 否 | 错误资源版本在 YAML 解析前失败；没有 Timeline、Job、备份或部署，属于无效排队 |
| `#107` / `dev-20260825.10` | Manual；Canceled | 否 | 正确选择 CI #102；宿主 publish 内存峰值导致新 SQL 连接超时，立即取消，Deploy Skipped、备份和根 API/DB 基线不变；旧 DEV API RestartCount 16→17，明确不合格 |
| `#119` / `dev-20260825.11` | completion trigger；Succeeded | 否 | 选择 main CI #118；自动开关为 `false`，Package/Deploy 安全跳过 |
| `#120` / `dev-20260825.12` | Manual；Succeeded | **是，2/3** | 复用 main #118 Artifact；独立 CHECKSUM/VERIFYONLY 备份、迁移、API/Web 身份、健康与证据 Artifact 全部成功 |
| `#121` / `dev-20260825.13` | Manual；Succeeded | **是，3/3** | 再次独立分类、验证/封装、备份、部署和证据发布；SQL/公网七容器基线保持不变 |
| `#123` / `dev-20260825.14` | completion trigger；Succeeded | 否 | 选择 main CI #122；当时自动开关仍为 `false`，Package/Deploy 安全跳过 |
| `#124` / `20260826.1` | 基础 CI Manual；Succeeded | 否 | 自动开关改为 `true` 后重跑同一 main SHA；观察期内没有出现第二个 completion DEV Run，不用手动 DEV 冒充自动验收 |
| `#125` / `dev-20260826.1` | completion trigger；Succeeded | 自动验收 | `ResourceTrigger` 绑定 CI #124，完整完成 Package、CHECKSUM/VERIFYONLY 备份、Deploy、健康/身份与证据；根 API/DB 基线未漂移 |
| `#126` / `20260826.2` | 基础 CI；Succeeded | 否 | PR #26 合入后的 main Artifact 桥成功，并自动触发 #127 |
| `#127` / `dev-20260826.2` | completion trigger；Failed | 否 | Package 成功后宿主内存使用 95.16%，SQL prelogin 超时；备份前失败关闭，无新备份/迁移/切换，暴露并促成备份前就绪门禁 |
| `#129` / `dev-20260826.3` | completion trigger；Failed | 安全门禁验收 | 绑定 main CI #128 / `318bcb2d...`；31 次采样仅 1328～1861 MiB，SQL/备份均未启动，备份/迁移/切换全部 Skipped。主机在门禁结束约 3 分 36 秒后自然恢复到 2 GiB 以上，因此等待窗口由 300 调整为 600 秒，不降低阈值 |
| `#130` / `20260826.4` | 基础 CI；Succeeded | 否 | 绑定 main `50a1db6d...`，同 SHA GitHub Runtime Artifact 下载、逐文件校验和 Azure Artifact 发布全部成功，并自动触发 #131 |
| `#131` / `dev-20260826.4` | completion trigger；Failed | 部署成功、证据重试缺口 | 首次 Deploy attempt 的 61 次采样仅 1254～1756 MiB，在 SQL/备份前失败；关闭非关键应用并重试同一 `DeployDev` 后，就绪、CHECKSUM/VERIFYONLY 备份、迁移、健康与 `50a1db6d...` 身份均成功，根 API/DB 未漂移。Run 最终仅因 attempt 1 已占用固定 `cp6-dev-evidence` 名称，attempt 2 发布同名 Artifact 被拒绝而失败；证据名现加入 `System.StageAttempt` 防重名 |
| `#132` / `20260826.5` | 基础 CI；Succeeded | 否 | PR #30 合入 `main@08813896...` 后以 `individualCI` 自动运行；下载同 SHA GitHub Runtime Artifact、验证并发布 Azure Artifact，随后自动触发 #133 |
| `#133` / `dev-20260826.5` | completion trigger；Succeeded | **最终自动验收通过** | `pipelineTriggerType=PipelineCompletion` 绑定 CI #132；readiness 三次为 2184/2383/2411 MiB 且 SQL=True，随后完成第 7 份 CHECKSUM/VERIFYONLY 备份、迁移、API/Web 健康/完整 SHA 身份与 `cp6-dev-evidence-attempt-1` 发布，根 API/DB 基线未漂移 |

Run #95 发布 `0.0.0-dev.92` / `47ca8441898af69d1e66bc1acb6c51129dbe9c18`；API/Web
分别在 `127.0.0.1:19991` / `127.0.0.1:18080` Healthy。Run #101 恢复后的根基线为
`cp6-db` RestartCount `2` / StartedAt `2026-08-25T15:06:55Z`、`cp6-api` RestartCount `3` /
StartedAt `2026-08-25T15:07:03Z`；接下来的合格 Run 必须保持这组基线不变。
当前 `CP6_DEV_AUTO_DEPLOY_ENABLED=true`、`CP6_DEV_PUBLIC_VERIFICATION_ENABLED=false`。#133 已完成 600 秒恢复窗口与 retry-safe Artifact 修复后的最终自动 DEV 发布验收；第 7 份备份 SHA-256 为 `af4f48fd19daeeb2461411a4210a1cb384c649a4fd01322b82b74555d804c9de`，DEV API/Web 为 `main@08813896...` 且 Healthy。

每次手动发布都必须保存：

- Azure Run ID 和 Environment deployment history；
- `cp6-dev-evidence-attempt-<N>/backup-readiness.json`：每次内存、SQL 登录和连续成功计数，以及通过/失败原因；
- `cp6-dev-evidence-attempt-<N>/database-backup.json`：文件长度、SHA-256、CHECKSUM 和 VERIFYONLY 结果；
- `cp6-dev-evidence-attempt-<N>/deployment.json`：触发模式、CI/CD Run、镜像 ID、迁移和本机/公网验证；
- `19991` live/ready/release 与 `18080/release.json` 的一致完整 SHA。

`<N>` 是只读的 `System.StageAttempt`。正常首次执行发布 `attempt-1`；同一 Run 重试
`DeployDev` 时递增为 `attempt-2`、`attempt-3`，保留每次失败/成功证据且不会覆盖或冲突。

#95/#120/#121 已满足连续三次独立成功、exclusive lock、生效备份和根 `cp6` 零漂移门禁；#133 又以真实 Pipeline Completion 完成 600 秒 readiness、自动 Package/Deploy、完整身份与 `attempt-1` 证据发布。当前 `CP6_DEV_AUTO_DEPLOY_ENABLED=true`，自动 DEV 已验收。任何旧版本手动回退前先重新关闭自动。

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

仓库能力、Azure 定向权限/Secret/Exclusive lock、三次手动 DEV 发布、低内存失败关闭、600 秒恢复窗口和 retry-safe 证据发布均已通过真实 Run；#132→#133 关闭本轮自动 DEV 稳定性缺口，可描述为“DEV 手动/自动双模式已验收”。独立 Tunnel 未切换且公网身份未验证，因此仍不得写成“cp6.uk 已切到 cp6-dev”。
