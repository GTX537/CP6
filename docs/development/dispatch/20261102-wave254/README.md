# W254 / D3

[三天计划](../../planning/20261102-three-day-execution.md)。D3/WP02-16 bounded PostgreSQL number allocation provider: original operation receipt and recovery,independent durable allocation,per-period uniqueness,current authority,single allocator writer,new explicitly registered Main tenant domains only. Existing numbering domains KEEP_EXISTING;no implicit counter migration. Full rule-management CAS/all consumers/CodeGen remain.

- [x] 固定实际域和单writer边界。
- [x] 原命令分配/恢复实现及实际PG并发、故障、权限验证。
- [x] 一次集中审查、准确范围/状态同步。
- [ ] 正常采用、分支清理与官方finish。

D3编号原命令提供者已实现并通过真实PG验证：新登记Main租户域独立持久分配/原结果恢复、不回收、当前权限和同域唯一写者。旧域保留原Owner；完整规则CAS、业务调用者切换、全局域和CodeGen仍为WP02-16余项。三天必做限定交付进入最终采用/释放核销。 Handoff已采用/finish，Main待当前PR。
