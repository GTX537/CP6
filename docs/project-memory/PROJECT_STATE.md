# 项目当前状态

最后更新：2026-07-18

## Git

- 当前分支：`main`
- GR-VP 功能分支已通过 merge commit `8e696d2` 合入并推送
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 完成 | `6e4ade1`，31 指令/12 视图/24 键 |
| T5 FIN v-permission | 完成 | `5732057`，66 指令/16 视图/51 键 |
| T6 PUR/PLAN/PUB v-permission | 待办 | 未开始 |
| T7 部署与冒烟 | 待办 | T4–T6 后执行 |

## 最近验证基线

- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T5 重新验证：71 files / 481 tests passed，type-check 0，build 通过；Chrome 权限矩阵与预算 view-only/edit-only 复测通过且 console 0 error。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。

## 下一动作

开始 T6 PUR/PLAN/PUB：先按 Controller 贴点、action seed 与反射 oracle 分域建立“视图 → 写动作 → 权限键”清单，再铺设指令并完成三连验证、真实浏览器抽样与独立复审。不要重做 T1–T5。
