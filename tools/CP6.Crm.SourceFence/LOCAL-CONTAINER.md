# C04A 本机 Docker SQL 来源

`inspect-container` 从 Windows 本机 Docker Engine 命名管道读取容器身份，不停止容器、不修改重启策略、不执行 SQL。随后实际来源检查/冻结/恢复可以使用这份原始字节绑定，同时继续核验完整 SQL 身份。该扩展解决原生与 Docker 同名库、相同 broker GUID 及 SQL ServerName 与当前容器 hostname 不同的问题。

## 建立绑定

| 环境变量 | 内容 |
| --- | --- |
| `C04A_DOCKER_ENGINE_PIPE` | `dockerDesktopLinuxEngine` 或 `docker_engine`，只允许本机命名管道 |
| `C04A_DOCKER_CONTAINER_ID` | 实际容器完整 64 位小写 ID；不接受名称或短 ID |
| `C04A_DOCKER_HOST_PORT` | 明确发布在 IPv4 loopback 可访问的 TCP 端口 |
| `C04A_DOCKER_SQL_PORT` | 容器 SQL TCP 端口，默认 1433 |

```powershell
dotnet <published-directory>/CP6.Crm.SourceFence.dll inspect-container
```

输出为可直接保存的 PascalCase `LocalSqlContainerBinding` JSON，包含固定 Format、Engine ID、完整容器/镜像/hostname、启动时刻、SQL/host 端口，以及端口映射、存储、重启策略摘要。没有 Docker 环境变量、连接、密码、健康日志或挂载原始内容。使用 [Docker Engine API v1.47](https://docs.docker.com/reference/api/engine/version/v1.47/) 的只读 info/inspect；响应有大小与时间上限。

## 用于实际来源

设置 `C04A_CONTAINER_BINDING_PATH` 为上述文件绝对路径，`C04A_CONTAINER_BINDING_FILE_SHA256` 为审阅后文件原始字节的小写 SHA-256。沿用实际来源的预期 ServerName、库名、broker GUID、scope 和请求变量；`C04A_SQL_CONNECTION` 必须使用 `127.0.0.1,<HostPort>`（可加 tcp:），显式 SQL 身份，不接受远端、实例名、其他端口、集成登录或多子网故障转移。

这些输入适用于 `inspect-actual`、`freeze-actual`、`status-actual`、`reopen-actual` 和 `recovery-status-actual`。旧 rehearsal 命令不接受容器扩展。恢复时目标仍必须是经现有协议核验的本机原生 CRM 目标；Docker 来源由同一 Windows 操作进程经 loopback SQL 连接控制，因此目标锁可以一直保留到来源提交。

每次锁内 SQL 身份检查都会复核 Docker 绑定；实际 SQL MachineName 必须与绑定 hostname 一致，ServerName、库名和四种 GUID 继续独立匹配。容器重建、重启、端口、存储或重启策略变化导致漂移拒绝。来源 Identity 中的 `LocalContainer` 进入原检查 scope 和冻结/恢复回执摘要。原生来源不输出 null 容器字段，因此原原生身份/冻结请求字节保持兼容。

文件先在一个句柄下读取并核对摘要，再严格解析，拒绝重复/未知字段与错误 Format；冻结请求内的嵌套容器格式也不能被忽略。读取状态不会更新批准值、收养新容器或自动改写绑定。

## 仍需独立完成的实际控制

这是本机端点与身份核验，不是持续的管理员隔离。Docker 管理员、SQL sysadmin、其他启动入口及复制出来的旧二进制仍需在具体执行包中控制；所有完整写入围栏/批准标志继续保持 false。实际容器、服务与源数据库没有因开发验证被切换。

共享预览的停止标志不阻止直接启动：原启动脚本会删除标志，直接宿主也在启动子进程后才读取标志。现场还存在旧 manifest 与旧 CRM 可执行副本；恢复宿主中的副本同时是依赖，不能直接删除。实际停启须按完整六进程身份核对，并处理入口隔离与恢复。初始化后的旧 CRM 0.1 入口不能仅改回 DLL；Core 来源回退使用独立的目标终止/来源恢复协议。

## 验证范围

`LocalSqlContainerTests` 覆盖原生字节兼容、严格文件与 SQL 端点拒绝、嵌套格式，以及在明确指定 Docker SQL 实例内建立唯一 Foundation 测试库、冻结/漂移拒绝和其他业务表保留。测试只创建/清理唯一测试数据库，不改变实际 CP6DB 或容器策略。

Core 用例位于 `eng/crm/source-fence-tests`，要求 `C04A_CONTAINER_TEST_SQL` 指向 Docker master、`C04A_TEST_CONTAINER_BINDING_PATH` 指向上述绑定文件；受影响原生用例另用 `C04A_TEST_SQL_CONNECTION`。缺少显式 SQL/绑定依赖时失败，不跳过。可用过滤器 `FullyQualifiedName~LocalSqlContainerTests` 单独复现 15 项新增检查。

CRM `eng/c04a/ContainerSourceRecoveryCliTests.cs` 使用已发布 Core 工具验证 Docker 来源→本机 CRM 目标的完整恢复及业务恢复写入。需要明确 `C04A_CROSS_SOURCE_CONTAINER_SQL`（master）和 `C04A_CROSS_CONTAINER_BINDING_PATH`；原 `CP6_CRM_TEST_SQL` 仍为原生 CRM 测试实例。它的最小旧列夹具只证明跨库协调，Foundation 结构另由 Core SQL 用例验证；不冒充正式数据迁移或生产验收。
