# E01-S06 文件安全扫描与保留清理交付报告

- 状态：实现与验证完成，待提交并合入 Space 集成分支
- 工作分支：`codex/space-e01-s06-file-safety`
- 基线：`integration/space-v1-20260730@a423bec8`
- Migration：`20260730152005_SpaceE01S06FileSafetyRetention`

## 1. 交付范围

本卡完成 `Quarantined → Scanning → Clean/Rejected` 文件安全链路，以及租户隔离、
引用感知、可补偿的到期清理链路。

- 上传完成后，`Space_File` 元数据与 `FileScan` Job 在同一次 EF 保存中原子写入。
- 安全扫描由 `IFileSafetyScanner` 隔离端口执行，WebApi 不读取或解析不可信内容。
- 扫描结论、文件状态、Job 终态和 Attempt 终态在同一数据库事务中提交。
- 扫描器未配置、暂时不可用或异常退出时失败关闭：文件回到 `Quarantined`，
  Job 按 Transient/Bug 策略有限重试。
- 安全命中或输入损坏进入 `Rejected`，Security/Input 失败不会自动重试。
- 未新增上传会话、CAD/Excel 解析命令、cancel/retry 或其他公开 HTTP 路由。

## 2. 安全扫描

稳定结果码已补齐：

- `SPACE_FILE_MALWARE_DETECTED`
- `SPACE_FILE_ARCHIVE_BOMB`
- `SPACE_FILE_ENCRYPTED_UNSUPPORTED`
- `SPACE_FILE_ACTIVE_CONTENT`
- `SPACE_FILE_CORRUPT`
- 沿用 `SPACE_FILE_QUARANTINED`、`SPACE_FILE_TYPE_MISMATCH` 与
  `SPACE_FILE_TOO_LARGE`

`ManagedFileSafetyScanner` 先调用外部恶意内容扫描端口，再对 XLSX/ZIP 中央目录
做不解压检查：

- 加密标志；
- 条目数量、中央目录大小、单条目和总压缩比、总展开量；
- `..`、绝对路径、盘符等路径穿越；
- 宏、嵌入对象、外部链接和可执行扩展名；
- 损坏或截断容器。

生产默认注册 `QuarantiningFileSafetyScanner`。在对象存储和恶意内容引擎适配器
尚未由 Worker 部署提供时，文件保持隔离，不会被误判为安全。部署侧提供
`ISpaceFileStore`、`ISpaceMalwareScanner` 并将 `IFileSafetyScanner` 替换为
`ManagedFileSafetyScanner` 后，才会产生 `Clean` 结论。

## 3. Worker 隔离契约

每个扫描请求携带服务端生成的 `AttemptId` 和独立 `WorkspaceId`，并冻结以下
不可弱化策略：

- 禁止出网；
- 原始输入只读，输出位置分离；
- 超时终止进程树；
- 结束或失败后清理工作区；
- 使用短期对象访问凭据；
- CPU、内存、进程数、临时磁盘和墙钟时间均为正数上限。

应用层只传递隔离契约，不依赖杀毒 SDK、对象存储 SDK 或具体进程沙箱实现。
实际 OS/container 资源限制由后续 Worker host 部署适配器执行。

## 4. 引用感知删除与到期清理

`Space_File` 新增：

- `RetainUntilUtc`
- `DeletionRequestedAtUtc`
- `ContentDeletedAtUtc`

`SpaceFileRetentionOptions` 可按 Source、Artifact、Temporary 配置保留期；Source
默认无限期，Artifact 默认 30 天，Temporary 默认 1 天。

清理采用两阶段可补偿流程：

1. 在 Serializable 事务内用 `UPDLOCK/HOLDLOCK` 锁定文件。
2. 重新检查 Source、Artifact 和活动 FileScan Job 引用。
3. 有引用时跳过；Uploading/Scanning 状态也不进入清理。
4. 零引用时先写 `Deleted` 软删除墓碑。
5. 事务提交后删除对象。
6. 对象删除成功后写 `ContentDeletedAtUtc`；失败则保留墓碑，下一批优先补偿。

来源创建与清理使用相同的文件行锁顺序。竞态只允许两个合法终局：

- Source 先落库，清理看到引用并跳过；
- 清理先落墓碑，Source 创建看不到可用文件并失败。

Migration 会为旧的 Deleted 行回填 `DeletionRequestedAtUtc`，使历史墓碑也能进入
对象删除补偿队列。

## 5. 租户、权限与回滚边界

- 上传/来源入口继续使用 E01-S05 已冻结的 `space:source:upload` 与
  `space:model:edit`。
- 扫描和清理持久化操作显式校验当前 Tenant；跨租户 Lease、文件和墓碑不可见。
- 到期清理必须由
  `ISpaceFileCleanupAuthorization.IsRetentionServicePrincipal=true`
  的受限服务主体执行。
- 关闭或不配置扫描器时，文件保持 `Quarantined`。
- 停止清理 Worker 即停止新删除；已写墓碑但对象删除失败的记录可安全重试。
- 仍被 Source、Artifact 或活动 FileScan Job 引用的文件不会删除。

## 6. 数据库变更

Migration 仅修改 `Space_File`：

- 新增 3 个可空 UTC 时间列；
- 新增租户级到期候选索引；
- 新增租户级待删除对象补偿索引；
- 新增 Content deletion 状态检查约束；
- 回填旧 Deleted 行的删除请求时间。

对应幂等 SQL：

`CP6.Space.Infrastructure/Migrations/Scripts/20260730152005_SpaceE01S06FileSafetyRetention.sql`

EF 检查结果：`No changes have been made to the model since the last migration.`

## 7. 验证证据

| 验证层 | 结果 | 说明 |
|---|---:|---|
| S06 新增 Domain/Application 单元测试 | 8 passed | 沙箱不可弱化、失败关闭、保留策略、服务主体、对象删除补偿、跨租户墓碑 |
| Managed safety scanner 样本测试 | 8 passed | malware、archive bomb、encrypted、path traversal、active content、corrupt、clean、scanner unavailable |
| S06 新增 SQL Server 测试 | 5 SQL-gated skipped | clean 原子终态、quarantine retry、跨租户、并发引用、墓碑补偿；当前环境未通过认证门禁 |
| Space UnitTests 全量 | 52 passed | E01-S01 至 S06 回归 |
| Space IntegrationTests 全量 | 17 passed / 29 SQL-gated skipped | 非 SQL 测试全部通过；跳过项不计作 passed |
| CP6.Tests 全量 | 2674 passed / 17 environment-gated skipped | Legacy Space 与其他模块回归 |
| 全解决方案 Release build | succeeded，7 existing warnings / 0 errors | S06 项目 0 warnings |
| C# SDK | build passed | `net8.0`，0 warnings / 0 errors |
| TypeScript SDK | strict/noEmit passed | Fetch client |
| SDK drift check | passed | OpenAPI、C#、TypeScript 生成物一致 |
| S06 触及文件格式门禁 | passed | `dotnet format whitespace --verify-no-changes` |
| EF model check | no pending model changes | Migration、designer、snapshot 同步 |
| 范围污染审计 | passed | S06 专属文件无后续 Scene/Asset/Planning/Publish/WMS/API 能力标记 |

本机自动化身份仍因 TLS/SSPI/Guest 认证问题无法进入 SQL Server，所以 29 个
Space SQL 测试只记作 skipped，不记作 passed；其中包含本卡新增的 5 个真实
SQL Server 测试。7 个构建 warning 位于既有 `CP6.Tests` 代码，不在本卡范围。
S06 没有修改 `cp6.web` 产品代码，未重复运行前端应用测试。

SDK 检查同时修正了 Windows `core.autocrlf=true` 下的伪漂移：生成器现在对
OpenAPI、C# 和 TypeScript 文本统一做 LF/行尾空白规范化后比较，不改变任何
HTTP 契约或已生成客户端内容。

## 8. 后续边界

- Worker host 需要装配对象存储、恶意内容引擎和 OS/container 沙箱实现；默认
  配置会继续失败关闭。
- 后续上传会话 API 应调用当前 `SpaceFileUploadService`，不得绕过 Quarantine
  与 FileScan Job 原子入队。
- CAD/Excel parse Job 只能引用 `Clean` 文件。
- 后续 Artifact 首次引用写入也必须复用相同的文件行锁协议。
- 对象存储 `DeleteAsync` 适配器必须幂等，以支持“对象已删除但确认时间写入
  失败”的补偿重试。
