# C04A 本机 Docker 来源绑定

基线为 Core 已核对远端 main `7880a0e2b8691a13fca0221d817487d470f6c2df`，独立任务分支。保留 Core 已分叉本地 main 与现有预览；上一任务 Core #110/CRM #71 已交付，不重新打开其未受影响通过项。整体仍 60%（3/5）。

## 实际缺口与实施

实际来源至少包括原生 CP6DB、C02 预览 Core 库及 Docker CP6DB。Docker SQL 的 ServerName 与当前 MachineName 不同，且 broker GUID 与原生来源相同。现有实际控制器仅允许 Windows 本机 MachineName，因此无法在同一 Windows 恢复协调器内控制 Docker 来源。

新增只读本机 Docker Engine 命名管道检查：绑定 Engine ID、完整容器 ID/镜像/hostname、启动时间、SQL 发布端口、存储和重启策略摘要。禁止远端 Docker/TCP Engine、远端 SQL、短容器 ID及模糊端点。SQL 始终经明确 IPv4 loopback 发布端口访问，并继续核验实际实例/机器/库名/全部数据库 GUID。

ActualSourceIdentity 增加仅容器来源输出的可选 LocalContainer 绑定；本机原生 null 值不序列化，保留已交付原生冻结回执/摘要。每次锁内身份检查同时复核 Docker 绑定；冻结/恢复沿用现有事务和目标证明，不把元数据观察说成完整管理员隔离。

CLI 提供只读 inspect-container 输出和原始字节绑定文件输入，文件无连接/环境变量秘密。使用自有 SQL 容器/唯一测试库验证正例、错误端点/身份/启动漂移和已冻结来源的拒绝。只复验受影响原生摘要与调用路径，集中完成一次任务级审查及正常 PR 交付。

并行只读整理现有 preview-control、脚本/宿主、实际进程入口和恢复约束，输出具体执行包输入；不停止实际进程/容器，不修改实际数据库、配置或路由。该事实清单与新容器来源能力用于随后完整启动路径/前向激活/恢复包，不替代 CRM11/C04B 正式验收。
