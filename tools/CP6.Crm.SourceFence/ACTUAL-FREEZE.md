# C04A 实际源端冻结组件

`ActualSourceFreezer` 为明确指定的本机来源提供首次冻结和绑定状态读取。它复用已交付的 20 表写入保护，在**同一 SQL 事务**中完成实际身份和审核摘要复核、排空并锁住来源表、安装保护、记录 Audit 与冻结回执。当前实际环境没有执行冻结；C04A 仍开放。

## 请求和入口

沿用 `inspect-actual` 的连接、预期实例、库名、broker GUID 与超时环境变量。SQL 连接字符串仅放进进程环境，不放进请求文件或参数。

| 新变量 | 内容 |
| --- | --- |
| `C04A_REQUEST_PATH` | 本机请求 JSON 的绝对路径 |
| `C04A_REQUEST_FILE_SHA256` | 审阅后的该文件原始字节 SHA-256，小写 64 位 |
| `C04A_EXPECTED_SCOPE_SHA256` | 可选；原检查摘要，必须与请求相同；重放仍传原摘要 |

请求使用 `ActualSourceFreezeRequest` 的区分大小写属性：`RunId`、`SourceIdentity`、`ExpectedScopeSha256`、`ApprovalEvidenceSha256`、`WriterControlEvidenceSha256`、`RecoveryPlanEvidenceSha256`、`TargetClosedEvidenceSha256`，可附固定 `Format`。`SourceIdentity` 采用 `ActualSourceIdentity` 全部字段，来自检查结果，包含实例/机器/库名、四种数据库 GUID、创建时间、原始登录及 SID。`inspect-actual` 输出为 camelCase，而请求使用类型的 PascalCase；建议通过类型构造后 `JsonSerializer.Serialize` 生成，勿直接粘贴输出对象。

`Format` 固定为 `CP6.C04A.ActualSourceFreezeRequest.v1`。`request.Digest()` 是规范化类型序列化后的 SHA-256；它和原始文件摘要用途不同。文件先在一个读取句柄下取字节并验证，再严格解析；拒绝重复/未知属性和空请求。

```powershell
# 两个独立命令；只有完整执行包另行获准后才执行实际 freeze。
dotnet <published-directory>/CP6.Crm.SourceFence.dll freeze-actual
dotnet <published-directory>/CP6.Crm.SourceFence.dll status-actual
```

库调用对应 `FreezeAsync(request, request.Digest())` 和 `StatusAsync(request.Digest())`。状态 JSON 不包含连接、请求证据文件内容或业务字段。错误仅返回 `C04A_*` 代码，退出码 2。

## 事务和重放

首次冻结要求来源 20 表全部为空、未初始化 `crm_source_control`，并精确匹配检查摘要。不可读模块、身份或摘要漂移、元数据冲突、锁超时均拒绝。它以同一个 Serializable 事务和事务级应用锁复用检查/冻结引擎；所有来源表锁保留到控制表、Audit、保护及回执一起提交。

数据库级扩展属性 `CP6.C04A.ActualSourceFreeze.v1` 保存完整请求、规范请求摘要、冻结前摘要和冻结后摘要。冻结后摘要包含新增保护和权限；额外比较排除**已验证的精确自有保护与 DENY**后的权限及模块，避免把其他修改收进新基线。每个数据库独立绑定，请求不能代表另一份来源。

只有相同请求、相同身份、相同冻结后摘要，且仍为对应 run 的 `Frozen/generation=1`，才可重放或返回绑定状态。工具自写回执必须保持规范序列化形式，包含嵌套固定格式字段。重放不写第二条 Audit。不存在“收养”原 rehearsal 冻结或自动补回丢失回执的路径。旧 rehearsal 命令即便面对同前缀恢复副本，也拒绝带实际回执的数据库。

## 实际执行前仍须接通的控制

四份审核材料摘要只是防漂移引用。工具不验证签名，不自行批准内容，也不证明服务、SQL Agent、管理员、容器或启动路径已被隔离。所有完整写入围栏、目标 Closed 独立验证、批准独立验证和恢复集成标志保持 false。

`reopen-actual` 已由[首写前恢复协调](ACTUAL-RECOVERY.md)接通，必须提供精确恢复请求并直接核验永久终止的 CRM 目标。缺少协调输入或调用 `seal-forward-only-actual` 仍返回 `C04A_TARGET_ROLLBACK_COORDINATOR_REQUIRED`。不能把人工给出的目标摘要当作“没有首写”的可持续证明；首次写入后只能前滚，实际路由与进程恢复仍须另行控制。

因此**本组件尚不能单独执行完整实际切换**。先完成跨源排空、目标门禁与路由恢复包并进行恢复验证，再让 owner 审批具体实际执行。不得用旧 rehearsal 接口、删除回执或手工撤销权限绕过这一缺口。

## 本机两份来源的观察

2026-09-14 只读核查确认：原生实例与 Docker 独立 `CP6DB` 都有完整空的 20 张旧 CRM 表；两份数据库的 broker GUID 相同。Docker 的 SQL ServerName 和当前 MachineName 也不同，API 和数据库容器的 Compose 来源不同，均配置自动重启策略。Docker API 的实际 SQL 登录是 sa/sysadmin；Docker SQL Agent 已停止且没有作业，原生实例另外存在一个维护作业。

这说明库名/broker GUID、当前根 Compose 文件或一次空会话观察都不能单独界定来源。当前控制器限定 SQL MachineName 为本机 Windows 主机；Docker 来源须另行完成受控适配及生命周期隔离，不能冒充同一个原生库。去敏观察与执行证据见 [本次证据](../../docs/evidence/c04a/2026-09-14/actual-source-freeze/README.md)。

## 验证范围

`eng/crm/source-fence-tests/ActualSourceFreezerTests.cs` 使用新建的、唯一命名 SQL 测试库验证请求、身份、摘要、事务失败、恢复入口拒绝和 Release CLI。与旧引擎共享事务的抽取使用直接相关的代表回归；先前未受影响的通过项复用原证据。测试夹具中的审核摘要为明确的合成数据，不是实际冻结批准或迁移验收。

独立发布检查可设置测试进程专用 `C04A_PUBLISHED_CLI_PATH` 为已发布 DLL 的绝对路径，然后只运行 `Cli_binds_exact_file_bytes_and_rejects_actual_reopen`。夹具仍只创建唯一测试库；该变量不是产品配置。发布文件摘要和执行来源另行记录。
