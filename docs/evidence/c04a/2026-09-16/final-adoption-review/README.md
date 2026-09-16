# 本机实际切换审阅包：仍为 No-Go

A07 六库保障和 A09 原密钥/完整宿主/身份恢复已完成。本次只把剩余 A10–A12 整理为具体审阅输入，没有操作实际数据库或停止服务，不增加进度。整体清单 75.0%、C04A 9/12、正式里程碑 60%。

审阅清单 SHA-256：`531e2810d3ec3a010dd0a646a0cb4cca9a998d33a49cebd53ba4e260feaf93eb`。完整逐文件绑定见 [review-manifest.json](review-manifest.json)，具体影响、顺序和停止条件见 [execution-review.json](execution-review.json)。审核包版本由清单 SHA 固定；其中字段仍为 `actualExecutionApproved=false`、`changedBytesUserSigned=false`。

## 已完成的准备

- 对应既有通过验证的 Core 来源工具 40 文件、CRM 组合操作工具 178 文件、CRM API 206 文件、恢复宿主 5 文件逐一核对，共 429 文件。没有重建或重跑旧测试，见[运行文件清单](runtime-package-files.json)。
- 六个实际库 ONLINE；实际库身份与两个组织已有管理员/默认根部门由只读 SQL 核对。原有账号是拟采用的操作身份，尚未据此执行实际变更。见[当前观察](current-observation.json)。
- 本机重启后的八个旧 Windows 入口均未运行，重新取得全量 inactive 清单。没有使用旧 PID，也没有重新启动它们。见[退休清单](refreshed-drain-inventory.json)。
- 两组织营业规则继续采用已确认的纽约时区、周一至周五 09:00–17:00、周末不计时。两个请求中使用各自现有根部门与站点；没有收到节假日日期，空初始列表明确列为待审阅输入，后续独立维护。见[日历请求](proposed-calendars.json)。它们尚未入库，没有伪造 CalendarVersion。
- 0.2 基础合同、覆盖与生成文件按 Git 原字节绑定，旧 0.1 签收不自动覆盖这些变化。见[变化字节](changed-contract-bytes.json)。

## 当前必须满足的条件

部署 Agent `vstsagent.gaobubao.CP6-Deploy.LAPTOP-3QQ44FJS` 仍为 Running / Auto。当前用户可以查询，但申请 Stop、Start、ChangeConfig 权限的句柄均被 Windows 拒绝，错误码 5；没有调用 StopService 或修改服务。见[权限实测](service-control-access.json)。这是真实操作系统权限限制，不是再次等待 Docker 授权，也不是自动审批拒绝。

原根 `cp6-api` 已按既有策略运行，必须纳入切换排流，其停机会影响同容器承载的其他本机 API。原 C02 transport 三容器仍停止。原生 SQL Agent 为 Stopped / Manual；存在一个启用的三步骤作业，步骤摘要保留在[SQL Agent 观察](sql-agent-steps.json)，不可仅凭应用 PID 声称已完整隔离全部写入者。独占窗口应保持 SQL Agent 停止，禁止并行手动启动/部署，并在执行前刷新进程、连接、输入字节与所有来源身份。

当前缺少：本包 0.2 变化字节、实际本机停服/前向初始化/来源冻结/整组启用/日历首写的最终批准，以及有权控制部署 Agent 的管理员操作窗口。两者都满足前，不执行实际库变更。批准不会自行提升当前 Windows 令牌权限。

## 执行顺序与日历依赖

1. 在明确的独占窗口暂停并禁用准确的部署 Agent，确认无 Agent.Worker；保持 SQL Agent 停止。刷新本包绑定，必要的最终在线备份输入与保障证据另行绑定，不重做已有六库恢复验收。
2. 保存根 `cp6-api` 原策略，按完整容器 ID 禁止自动重启并停止；用组合工具对两组织的八个旧入口执行永久退休，原字节留存。
3. 使用本包 API 和原配置/keyring，在原认证/目录及两个业务库前向初始化一次，核对两个目标 Closed 和共同集合 Anchor；保留已有身份数据。
4. 对三份实际 Core 来源执行绑定后的冻结/重计数。任何来源身份、结构、行数或证明变化都保持 No-Go。
5. 启动原三 transport 和新宿主，完成身份登录与已认证读取；目标此时仍 Closed。初始日历不存在时，公开表单 409 是已知前置，不用它作为提前写库的理由。
6. 用 `enable-targets-verified-local` 在完整目标集合及 live Frozen 证明下整组启用。
7. 每组织通过正常 API 保存已审阅日历（`If-Match: *` 与固定幂等键），记录实际返回 CalendarVersion / ETag，预览后再发布。**第一次保存即进入 Written；不能等 Lead 创建才认定恢复边界改变。**
8. 日历发布后验证公开表单，再进行实际采用、CRM11 与观察；旧表按至少一个正式发布周期保留只读，C04B 单独验收。

日历不能在 Closed 状态绕过保护直接填表。把日历请求文件写好也不能计作 A10 已完成。实际版本在获批正常写入后产生并纳入后续不可变证据。

## 管理员交接和恢复边界

用户不在电脑旁时可以审阅本包；不需要为了当前已完成的恢复验证回电脑操作。真正执行独占窗口时，管理员可在检查该 Agent 没有执行作业后，以管理员 PowerShell 对**准确服务名**操作：

```powershell
$crmAgent = 'vstsagent.gaobubao.CP6-Deploy.LAPTOP-3QQ44FJS'
Set-Service -Name $crmAgent -StartupType Disabled
Stop-Service -Name $crmAgent
Get-Service -Name $crmAgent
```

这些命令尚未执行，仅供已批准窗口使用；暂停期间本机部署队列不能由该 Agent 执行。原状态为 Automatic / Running，恢复需先确认其待部署内容不会重新引入旧 CRM 写入口，然后才以 `Set-Service -Name $crmAgent -StartupType Automatic`、`Start-Service -Name $crmAgent` 恢复。不能把本机所有部署/数据库服务批量停止，也不能用终止未知进程代替服务控制。

初始化部分提交后保持 Closed 并前向恢复。只有全部目标尚未 Written 时，才能走已验证的“关闭目标 → 永久终止全部目标 → live 回执 → 恢复来源”协议。任一日历首写后均不得把旧备份覆盖回当前库或重新打开旧写入口，只能前向修复。没有生产部署、Actions、删除旧表或 Docker 卷操作的授权扩展。
