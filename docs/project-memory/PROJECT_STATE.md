# 项目当前状态

最后更新：2026-07-18

## Git

- 当前分支：`feat/general-role-vperm`
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 下一任务 | 未开始 |
| T5 FIN v-permission | 待办 | 未开始 |
| T6 PUR/PLAN/PUB v-permission | 待办 | 未开始 |
| T7 部署与冒烟 | 待办 | T4–T6 后执行 |

## 最近验证基线

- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T2/T3 报告中：71 files / 481 tests passed，type-check 0，build 通过。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。

## 下一动作

打开 MES 键表/权限反射测试和 `views/mes`，只做 T4 的按钮映射与 template `v-permission`，完成前端三连验证后更新本文件。
