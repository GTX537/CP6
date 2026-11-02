# W253 / D2

[三天计划](../../planning/20261102-three-day-execution.md)：实际CRM切组织旧会话失效与七天审核期限到期验收。只修实际失败；身份和放行/拒绝旧证据按范围复用。DEV-1 Main，DEV-2串行Handoff/CRM，单人、无新树。

- [x] 实际PG/HTTP切组织与旧会话重放。
- [x] 原七天审核到期HTTP/Owner及无半组效果。
- [x] 相关回归、一次集中审查与准确采用。
- [ ] 正常分支清理及三仓任务finish。

单份交付报告在本窗完成时落盘，不以静态expiry断言冒充运行验收。

D2实际CRM登录/切组织/旧会话失效通过；七天审核期限前、整点、之后及写入越界回滚通过，修复CRM Issue164。读取到期禁用动作；Expired持久终态与对应保留策略仍未实现，不关闭整卡。继续D3编号原命令提供者。 CRM/Handoff已采用/finish，Main待最终PR/finish。
