# 已知问题、风险与技术债

## 安全

- 仓库曾经公开，历史中跟踪过 cloudflared 配置、部署脚本和开发配置。即使现在已转私有，旧凭证也应视为可能泄露并全部轮换。
- `.env`、私钥、生产配置不得提交；新机从 `.env.example` 重建。
- 前端 `v-permission` 不是安全边界，任何修复必须保留后端 fail-closed。

## 数据与部署

- `docker compose down -v` 会删除 SQL 命名卷，属于数据毁灭性操作。
- 数据库备份只代表 2026-07-18 快照；之后继续开发产生的数据需要重新备份。
- 恢复时必须先停止 API、校验哈希和备份，再执行覆盖性 restore。
- 本机曾发生 WSL 卡死和磁盘不足；Docker/SQL 详细日志可能快速占满虚拟磁盘。

## 代码层已知项

- FIN BudgetLine 缺版本级并发控制。
- `GdprService` purge 在非关系型测试 DB 上明确不支持，这是测试边界而非线上缺陷。
- 多端点保存按钮当前前端通常按主动作 `add` 隐显；非常规 add/edit 分离授权可能出现 UX 误隐，但后端仍正确校验。

## 文档风险

- CODEMAP、PROJECT_STRUCTURE、README 部分状态与计数停留在 6 月，不能据此判断新模块“尚未实现”。
- `docs/superpowers/plans` 含已完成计划；复工前必须结合 Git log、SDD progress 和 `PROJECT_STATE.md` 判断。
- `.superpowers/sdd` 是执行台账，可能被下一任务复用覆盖；正式设计和完成记录应回写 `docs`。

## 不应顺手处理的事项

- 当前 GR-VP T4–T6 只允许模板权限指令，不同时清理 CSS、脚本、API 或 i18n。
- 不在无专项测试时修改 WMS 库存核心、WF Token/Task 状态迁移、多租户过滤器或权限聚合器。
