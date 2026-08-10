# AI 接手说明

## 当前项目状态

快照日期 2026-07-19。仓库已改为私有，GR-VP T1–T7 已完成；T6 合入 `main` 的 merge commit 为 `d79a39c`，T7 OA 提交链修复为 `ffca422`。工作基点包含三库 Git LFS 迁移备份。系统不是原型：ERP/MES/WMS、财务、采购、OA/WF、权限、计划和 Space 均有大量生产代码与测试。

## 当前开发优先级

不要重新实现 GR-VP T1–T7。标准角色、OA/WF 40×17、ERP 39×16、MES 31×12、FIN 66×16、PUR/PLAN/PUB 37 个页面级声明/33 个唯一键，以及部署冒烟均已完成。下一项应从 `06-Todo.md` 的 P1 票中选择。

## 下一步应该继续什么

从 P1 收口票开始。当前 Docker Compose 正运行 API/Web/DB/Kafka/RabbitMQ/Redis；部署镜像来自干净 `main`，没有带入并行的菜单设计页面或 `.claude/settings.local.json`。当前 `CP6DB` 仅注册 `DEFAULT/A1`，四租户字面复验要等真实 B1/C1/D1 数据恢复后再做。

## 最近完成内容

- `ddcfa1ac`：逐租户一般用户 RoleId=10 种子。
- `15823c38`：OA/WF 前端权限指令。
- `4a48525e`：ERP 前端权限指令。
- `8e696d2`：`feat/general-role-vperm` 合入 `main`。
- `6e4ade1`：MES 31 条前端权限指令，12 个视图，24 个真实写权限键。
- `5732057`：FIN 66 条前端权限指令，16 个视图，51 个真实权限键；预算 view-only 保留只读值。
- `4bb7512`：PUR/PLAN/PUB 37 个页面级权限声明，33 个唯一写权限键；Seq 通用表格桌面/移动 CRUD 守权与 2 条回归测试。
- `b43787e0`：三库验证备份经 Git LFS 固化。
- `1aac4a5d`：换机恢复入口修正。
- `d79a39c`：T6 no-ff 合入并推送 `main`。
- `ffca422`：OA 表单提交改走 `submit` 权限端点；一般用户不再因草稿 `add` 权限而无法发起。

## 为什么这样设计

- 后端权限 fail-closed，前端权限仅 UX：即使 DOM/请求被伪造，服务器仍拒绝。
- 标准角色 insert-only：不覆盖管理员后续手工授权。
- 权限键从 Controller 贴点、种子和反射 oracle 互锁，防“有贴点无种子导致 admin 403”。
- WF 归属闸在引擎层：避免不同 Controller 或未来调用方绕过。
- 当前权限铺设任务限制为 template-only：降低大范围 UX 改造的回归面。

## 哪些地方不能乱改

- WMS 库存移动/台账不变量。
- WF Token、Task、History 和状态迁移事务。
- 多租户过滤、TenantId 盖章和权限聚合器。
- Program.cs 种子执行顺序与幂等语义。
- Controller 权限贴点、`docs/seeds` 键表与反射测试之间的逐字对应。
- 数据库历史迁移和已验证备份。

## 哪些地方仍需要重构

- WF SignalR 定向推送。
- FIN BudgetLine 版本级并发。
- PMS/Sys 平台页权限 UX。
- GR-VP 多端点保存按钮 add/edit 的精细 UX。
- README/CODEMAP 旧计数刷新。

## AI接手步骤

1. `git status`，确认分支与用户改动。
2. 读本目录全部 Markdown。
3. 读当前 GR-VP plan、目标模块 Controller 贴点、action seed 与对应权限反射测试。
4. `git log -20 --oneline`，确认 T1–T5 和迁移提交存在。
5. 对下一个模块先产出“视图 → 按钮 → 权限键”清单，再改 template。
6. 不发明键，不给纯读动作贴键，不改 script/style。
7. 跑 `vue-tsc`、Vitest、build；记录真实基线。
8. 更新 `PROJECT_STATE.md` 与 `CHANGELOG-AI.md`，按当前授权边界提交。

## 恢复上下文提示词

```text
请完整阅读 docs/project-memory、README、当前分支最近 30 条 git log，
再阅读 docs/superpowers/plans/2026-07-17-general-role-vperm.md。
确认当前 GR-VP 已完成 T1-T6，下一项是 T7 部署与端到端冒烟。
先汇报状态、风险和拟修改范围，再按本轮授权边界执行。
```
