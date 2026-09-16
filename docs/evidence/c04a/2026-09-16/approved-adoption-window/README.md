# 本机切换已获授权，等待 Windows 管理员停服

用户在具体审阅包之后回复“没有问题，给你所有开发权限”。本轮将该回复记录为已审阅本机切换、0.2 变化字节和两组织日历首写的批准，见 [approval.json](approval.json)。审阅清单摘要固定为 `531e2810d3ec3a010dd0a646a0cb4cca9a998d33a49cebd53ba4e260feaf93eb`；原 [review-manifest.json](../final-adoption-review/review-manifest.json) 及八个绑定文件保留历史原字节，不能将其中旧的 false 字段当作当前仍缺批准。

授权已收到，不再重复询问。当前 Windows 会话仍非管理员，Stop/Start/ChangeConfig 句柄均被拒绝（Win32 5），见 [当前权限记录](service-control-access.json)。精确部署服务仍 Running / Auto，SQL Agent 仍 Stopped / Manual，没有观察到 Agent.Worker。这是操作系统权限限制，不是自动审批拒绝。

## 管理员一次操作

已在本机准备脚本 `D:/CP6-audit-results/20260916/approved-adoption-window/Enter-CrmCutoverWindow.ps1`，SHA-256 为 `36bd84722530105fa192775972d977bc23cba7dd4bc469ebc0f32214a8b5f332`。管理员 PowerShell 执行：

```powershell
& 'D:\CP6-audit-results\20260916\approved-adoption-window\Enter-CrmCutoverWindow.ps1'
```

脚本先检查管理员令牌、已审阅文件摘要、准确服务身份、SQL Agent 停止及无 Agent.Worker，再仅禁用并停止 `vstsagent.gaobubao.CP6-Deploy.LAPTOP-3QQ44FJS`；留下不覆盖的 JSON 回执，不执行数据库或 Docker 操作。发生异常时保留实际状态供检查，不自动重开部署入口。脚本语法和非管理员拒绝路径已经验证，返回 5 且两个服务状态未变；**有权限的停服成功路径尚未执行**，见 [handoff-check.json](handoff-check.json)。

该操作完成后应保持维护窗口独占；执行切换前重新核对实时服务、进程、输入和数据库身份。管理员回执本身不是永不过期的停写证明。部署 Agent 只有在待部署内容确认不会重新引入旧 CRM 写入口后才可恢复 Automatic / Running。

## 进度与后续

整体固定清单 **75.0%**；C04A **9/12＝75.0%**；正式里程碑 **60%（3/5）**。本轮确认了审批子条件，但 A11 的独占控制尚未满足，增加 **0 个百分点**。A10 日历尚未存储/发布，A12 原库初始化、来源冻结、整组启用与切换尚未执行。A01–A09 继续沿用原证据，未重跑已通过测试。

管理员窗口建立后按 [已批准执行顺序](../final-adoption-review/execution-review.json) 继续。首次日历保存即 Written，后续只前向修复。C04B 的实际采用/CRM11/观察仍独立待办；生产部署和 GitHub Actions 未获授权。
