# WFS 信箱体验增强 · 通知设定 / 批量转单 / 移动端 / 多行显示 设计

> 生成于 2026-07-05（brainstorming 已确认）。上游：umbrella 总设计 §5「D 后续（P2+）」体验项。
> **范围修正**：umbrella §9 的「审批人解析高级策略」经 2026-07-05 实读核实**已全部完成**（`ApproverResolver.cs:30-41` 八策略 DirectManager/DeptLeader/Role/Specified/Starter/FormField/DataMap/Group + `:21-27` When 条件门控 + `:46-47` Filter 候选过滤；设计器 `NodePropertyPanel.vue:233-320` 全部暴露 + `:711-715` When/Filter 输入），本 spec 不含。
> 落码位：`CP6.Core/Services/Oa`、`CP6.WebApi`、`cp6.web/src/views/oa/inbox`。

---

## §0 背景、范围与决策

### §0.1 范围（In / Out）

**In**：①通知设定（用户级 类型×通道 开关矩阵）②在途批量转单（管理员批量改派）③移动端响应式（信箱三页）④同单多状态多行显示偏好。

**Out（→ §8 YAGNI）**：通知摘要/静默时段；转单规则自动化（休假自动委派已有 `Wf_FlowDelegate`，不重复）；移动端原生壳/PWA。

### §0.2 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | 四项一期全做 | 用户选项确认 |
| D2 | 通知偏好存 `Wf_InboxPref.PrefsJson` 新键（**零迁移**——PrefsJson 本就是自由结构，`Wf_InboxPref.cs:11`）；缺行/缺键 = 默认全开（向后兼容零数据迁移） | 设计呈现已确认 |
| D3 | 批量转单 = **逐条独立事务 + 汇总报告**（部分成功可见、失败明细返回）；否决整批大事务——离职场景批量大，一条脏数据回滚整批重试代价高 | 设计呈现已确认 |
| D4 | 移动端纯响应式适配，全用 Design System v1.0 token，不引新依赖 | 设计呈现已确认 |
| D5 | 多行显示 = `PrefsJson.rowMode: "merged"(默认)\|"expanded"` 用户偏好 | 设计呈现已确认 |

---

## §1 现状锚点（逆向真实，不编造）

- **偏好表**：`Wf_InboxPref`（TenantId,UserId 唯一，PrefsJson nvarchar(max) 自由结构，`Wf_InboxPref.cs`）。
- **通知栈**：`IWfNotifier`（`IWfNotifier.cs:15-16` FlowRejectedAsync 等类型化方法）→ `PersistentWfNotifier`（`CP6.WebApi/Services/`，站内持久 + SignalR 推送）；邮件基建 `SmtpEmailSender`（`CP6.Core/Services/Sys/`）已存在且 `PersistentWfNotifier` 已有引用。
- **通知类型**：`WfNotificationType`（`CP6.Entity/DomainModels/Wf/`）——偏好矩阵的类型轴以此为准（plan 时枚举其值域）。
- **转交引擎动作**：`TransferAsync` 已有（umbrella §4.5，C5 落地），单任务转交 + FormTo Transferred 状态 + history 自动记录。
- **已知坑**：SignalR 通知实时推送 CSRF 403（波①收尾票修复；本 spec 不依赖其先修——偏好控制的是"发不发"，与推送通道健康度正交）。
- **信箱前端**：`cp6.web/src/views/oa/inbox/`（列表/FormDetail/发起）；UI 已完成 Design System v1.0 迁移（OA 24 页在列）。

---

## §2 通知设定

### §2.1 数据（零迁移，D2）

`PrefsJson.notify` 键：

```json
{ "notify": { "taskArrived":   { "inApp": true, "email": false },
              "flowApproved":  { "inApp": true, "email": true  },
              "flowRejected":  { "inApp": true, "email": true  },
              "timeout":       { "inApp": true, "email": true  } } }
```

类型轴 = `WfNotificationType` 值域（上例示意，plan 时按实际枚举对齐）；通道轴 = inApp / email。**缺键默认 true**（解析函数 `bool IsEnabled(prefs, type, channel)` 三态坍缩：无行/无键/无通道键 → true）。

### §2.2 行为

- `PersistentWfNotifier` 每个发送方法入口查偏好：inApp=false → 跳过持久+推送；email=false → 跳过邮件（若该类型本有邮件动作）。
- 偏好查询轻缓存（per-request scope 内存缓存即可，不引分布式缓存）。
- **不回溯**：改偏好只影响此后通知，既有通知行不动。

### §2.3 设置 UI

信箱设置页（既有）加「通知设定」卡片：类型×通道开关矩阵表格 + 恢复默认按钮。保存走既有 InboxPref 端点（PrefsJson 合并写，不整体覆盖——保留其他偏好键）。

---

## §3 在途批量转单

### §3.1 端点

`POST /api/oa/inbox/batch-transfer`

```json
{ "fromUserId": "...", "toUserId": "...", "comment": "离职移交",
  "filter": { "flowKey": null, "beforeUtc": null } }   // 可选收窄
```

- 权限点 `OA.Inbox.BatchTransfer`（管理员向）；操作者、from、to 全记审计。
- 服务层：查 from 的全部 Pending 待办（按 filter 收窄）→ **逐条调 `TransferAsync`，每条独立 SaveChanges**（D3）→ 汇总 `{ total, succeeded, failed: [{taskId, flowKey, error}] }`。
- 逐条失败不中断后续；to 用户停用/不存在 → 入参前置校验直接 400（E-WF 不占码，走既有入参校验口径）。
- 防御：from==to 拒绝；单批上限 500 条（超出提示分批，防长事务假象与超时）。

### §3.2 UI

信箱管理入口「批量改派」对话框：选 from/to 用户（既有用户选择器）→ 预览待转清单（条数+抽样）→ 确认 → 结果报告（成功/失败明细表，失败行可单条重试）。

---

## §4 移动端响应式

- 断点 `<768px`，三页适配：
  - **列表**：表格 → 卡片流（单号/流程名/当前关卡/时间戳/状态 CpTag）；筛选收进抽屉。
  - **表单详情**（FormDetail）：时间线与表单纵向堆叠；Sign Records 弹窗全屏化。
  - **审批操作**：同意/驳回/退回/转交钉底部操作栏（安全区适配）。
- 全部 Design System v1.0 token/组件，媒体查询集中在各页 `<style>` 尾部（对齐 UI 翻新既有做法）；零新依赖（D4）。
- 桌面端像素零回归（QA 走查双端）。

---

## §5 同单多状态多行显示

- `PrefsJson.rowMode`：`"merged"`（默认，现状——同实例多任务合并一行显最新态）| `"expanded"`（每任务一行平铺）。
- 列表查询层按偏好分组或平铺（后端查询参数化，不是前端拆行——分页正确性）。
- 列表工具栏加切换开关（写回偏好）。

---

## §6 安全 / 多租户 / 向后兼容

- TenantId 贯穿；批量转单严格校验 from/to 同租户。
- PrefsJson 合并写防键覆盖（读-改-写 + RowVersion 若有，plan 核实 InboxPref 并发口径；单用户自改冲突概率可忽略）。
- 全部纯增量：通知默认全开、rowMode 默认 merged、端点新增——既有行为零变化。

---

## §7 测试策略

- **通知偏好**：三态坍缩默认真、各类型×通道跳过矩阵、合并写不覆盖他键、缓存不跨请求。
- **批量转单**：逐条事务部分成功、失败明细、上限 500、from==to 拒绝、跨租户拒绝、审计行齐全、TransferAsync 语义不变回归。
- **rowMode**：merged/expanded 分页正确性（同实例 3 任务跨页界）、偏好写回。
- **QA harness**：gstack 剧本（设置矩阵改动→触发通知验证跳过、批量改派全流程含失败重试、移动端 375px 视口三页走查、rowMode 切换）。
- 基线：后端 1509 → +N 全绿；前端 320 → +N 全绿；**零迁移**（EF clean 不变）。

---

## §8 YAGNI / 留后

- 通知摘要（digest）/静默时段/按流程粒度偏好。
- 批量转单规则化（按组织变动自动触发）——已有 `Wf_FlowDelegate` 委派覆盖休假场景，不重复。
- 移动端 PWA/推送。
- expanded 模式下的批量操作（多选审批）。

---

## §9 分期 / 任务波次（供 writing-plans 细化）

- **X-A 通知设定**：IsEnabled 解析函数 + PersistentWfNotifier 接偏好 + 设置 UI 卡片 + 合并写。
- **X-B 批量转单**：服务层逐条事务 + 端点 + 权限点 + 对话框 UI + 结果报告。
- **X-C 移动端**：三页断点适配（可与 X-A/X-B 并行，纯前端）。
- **X-D 多行显示**：查询层 rowMode + 工具栏开关。
- **X-E i18n + QA**：五语 seed（估 ~25 键）+ gstack harness + DoD。

依赖：{X-A ‖ X-B ‖ X-C ‖ X-D} → X-E。

---

*生成于 2026-07-05。执行遵守铁律：引擎动作（TransferAsync）只调用不改动；E 波紧跟 D 波；零跨模块污染。*
