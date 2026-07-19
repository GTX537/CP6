# GR-VP T7 报告：部署与一般用户端到端冒烟

Status: DONE（当前租户注册表全覆盖；四租户字面矩阵受环境数据约束）

## 交付与镜像

- T6 已通过 `d79a39c` no-ff 合入并推送 `main`。
- 冒烟发现 OA 发起页“提交”先调用 `draft/save(add)`，与一般用户仅有 `submit` 冲突；已改为直接调用既有 `wf/flow/submit(submit)`，草稿保存仍保持 `add`，未扩大权限。
- 修复提交：功能分支 `4530699`，`main` 为 `ffca422`。
- API 镜像：`cp6-cp6-api`，运行镜像 ID `sha256:2ee04fc0eb86fc8e219026cd55b58a254bc6b1c8170fccb91d81bf497596d9fb`。
- Web 镜像：`cp6-cp6-web`，运行镜像 ID `sha256:0271d4af05f8f490908ef35ee4be4692f16b92dbdcdcd29fa5a92b6e5d7299ec`。
- `cp6-api`、`cp6-web`、`cp6-db`、Kafka、RabbitMQ、Redis 均恢复运行；API 最近日志无 `fail/crit/Unhandled/Application startup` 命中。

## 数据库核验

- 当前租户注册表只有 `DEFAULT`（A1），Enable=1；未发现 B1/C1/D1 注册行或孤儿角色行。
- A1 RoleId=10：角色 1 行、名称“一般用户”、菜单 4 行、动作 8 行。
- A1 admin RoleId=1：角色 1 行、菜单 148、动作 323；无扰动。
- 8 键精确为：
  - `oa-form-catalog:favorite/submit`
  - `oa-inbox:approve/read/sendback/transfer/withdraw`
  - `oa-settings:delegate`

## 真实冒烟

| 场景 | 结果 |
|---|---:|
| admin 菜单 | 148 |
| `qa_general` 菜单 | 4 |
| `my-actions` | 200，恰 8 键 |
| 临时自审批流程定义 | 200 |
| 一般用户发起自审批流程 | 200 |
| 一般用户办理本人待办 | 200，实例最终状态 1（通过） |
| 无权 `batch-transfer` | 403 |
| 无 `add` 的 `draft/save` | 403 |
| 一般用户发起 admin 审批流程 | 200 |
| 一般用户办理 admin 待办 | 400（归属闸拒绝） |
| admin 办理本人待办 | 200，实例最终状态 1（通过） |

测试结束后删除 2 条 T7 流程实例及其运行数据，删除临时 `t7-self-approve` 流程定义；两项剩余计数均为 0。`qa_general` 保留，已完成强制改密流程。

## 回归验证

- 新增 `FormInitiate.submit.spec.ts`：1/1 passed。
- 干净 `main` 完整前端：73 files / 488 tests passed。
- `vue-tsc --build`：0 错误。
- Vite production build：2649 modules，成功；仅既有 chunk-size warning。
- 在线 `http://localhost:8080/` 与新 `FormInitiate` chunk：200；部署的 flow API chunk 包含 `/wf/flow/submit`。

## 环境偏差

计划原写“四租户各查”，但部署数据库实际只有一个注册租户 A1。本次没有为凑数写入 B1/C1/D1；已对全部现存启用租户完成 SQL 验证，并保留多租户种子单元测试作为代码级证据。
