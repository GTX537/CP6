# deploy/seed-data

可版本控制的 **reference data**（参照数据）资产，独立于活库存在 —— 做部署 fallback + 灾备 + 可 diff 的版本历史。

## sys_lang.json — 多语言词条快照

- **产出**：在源库跑 [`../export-langs.sql`](../export-langs.sql)，把结果存为本目录 `sys_lang.json` 并提交。
- **消费**：在新/干净环境跑 [`../import-langs.sql`](../import-langs.sql) 幂等灌回（按 `(LangKey, TenantId)` upsert）。
- **节奏建议**：词条在多语管理 UI 改动后，定期重跑导出并提交 —— 让"代码优先部署"也能产出满词条环境，不被单一活库绑死。

详见 [`../runbook.md`](../runbook.md) §5。

> 这里只放可公开的参照数据（词条等）。**密钥/密文/真实业务数据不入此目录**（走 `.bak` 整库搬迁，见 runbook §2）。
