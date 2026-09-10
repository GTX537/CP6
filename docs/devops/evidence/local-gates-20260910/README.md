# 2026-09-10 本地验证授权与远端门禁变更

用户明确授权从门禁移除必需的 Actions 检查，并改用本地发布检查。执行前保留完整 `branch-protection-before.json`；执行后回读 `branch-protection-after.json`，确认仅删除 `required_status_checks` 对象，其中原五项为 `windows-and-web`、`android`、`sql-integration`、`crm-saas-public-contract`、`crm-v1-prd`。

其余保护逐字段一致：保留 PR、过期审查撤销、会话解决与管理员约束，仍禁止强推和删除。没有写入成功检查、运行 Actions 或改变生产 Environment、候选 Tag 与发布审批。

为防止配置 PR 触发旧的 `pull_request_target`，先暂停七个普通工作流。`workflows-before.json` 和 `workflows-paused.json` 保存完整状态；逐项核对其他工作流未变。源配置正常合入并确认远端只有 `workflow_dispatch` 后，再恢复这七个手动入口。

恢复原必需检查需要用户另行决定，不能因额度重置自动恢复。原始设置保留用于审计或后续明确授权的恢复操作；不得据此绕过新的运行授权。
