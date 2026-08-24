# Space CAD 待审变更集与 RuleOnly 交接报告

日期：2026-08-16

任务分支：`codex/space-cad-review-apply-audit`

## 结论

详细 Spec LM-FR-019/019A 的仓库实现已完成深审和加固。CAD Job 成功后自动加载受控、密封且绑定 Base Content Revision/Hash 的待审 Workspace；确认前 Draft 不变。通用静态元素直接通过 CAD Typed Changeset 原子写入，Zone/Aisle/Rack 则显式交接到既有 RuleOnly → Proposal Review → Atomic Apply 权威链，避免伪装成通用元素或新建第二套布局领域。

该结论不代表真实 Provider、黄金 CAD、性能环境或 Pilot 已接受，核心 GA 仍为 72% / `NoGo`。

## 六类审核与客户端信任边界

- 工作台分别展示新增、修改、删除、冲突、低置信度和未识别的数量，并可按类型筛选。
- 客户端解析器重新计算 Change Summary，拒绝数量不一致、不可 Apply 类型被标记为可 Apply、不可 Apply 变更被预选，以及缺少 Changes 却携带 Summary/Hash 的响应。
- 新 Workspace 的 `workspaceSha256` 变化会重置旧选择，防止上一解析任务的 ChangeId 被提交到新工件。
- 前 200 项按类型渲染以保护检查器响应；选择与 Apply 的服务端硬边界为 10,000 项。

## 原子 Apply 边界

- 公开手工 Element Command Batch 保持 100 项上限。
- CAD 服务使用同一 Design V1 实现的内部专用入口，最多 10,000 个服务端生成命令；客户端不能通过普通命令端点扩大批量边界。
- 该入口继续依次绑定 Tenant/Permission、Lease、Floor Revision、Content Revision/Hash、Workspace Hash、ChangeId 和 CommandBatch 幂等身份。
- 101 项 CAD 静态元素集成用例证明一次事务只生成一个幂等 Command Batch，Floor Revision 与 Content Revision 各只推进一次，并返回完整 Undo/Redo。
- OpenAPI 对 `changeIds` 明确发布 `minItems=1`、`maxItems=10000`；C# 客户端与设计契约同步生成，SDK drift 为 clean。

## Zone/Aisle/Rack 交接

- CAD Semantic Preview 可以识别 Zone/Aisle/Rack，但它们不是通用 `Space_Element`，需要父子关系、货架生成方案、逐层规格和库位派生。
- `SPACE_CAD_REQUIRES_RULE_ONLY_REVIEW` 在待审变更集中保持不可直接 Apply；工作台明确解释原因，并提供“使用当前 CAD 来源进入规则生成”。
- RuleOnly Launcher 接收当前 SourceId，在存在多个已确认 CAD 来源时仍预选正确来源；用户继续显式选择货架生成方案。
- 后续 Proposal Review 与 Atomic Apply 继续作为 Zone/Aisle/Rack 写入 Draft 的唯一权威，外部 AI Provider 不参与 RuleOnly。

## 自动化证据

- Space CAD Parse Integration：15/15 passed；包含 101 项单事务/一次 Revision 纵切。
- CAD Review Workspace、Issue Panel、RuleOnly Launcher：16/16 passed。
- Design V1 OpenAPI：55/55 passed；双 SDK 生成漂移检查通过。
- Space Unit：546/546 passed。
- CP6.Tests：2,933 passed，19 个真实 SQL/环境条件用例按既有条件 skipped。
- Web 全量：873/873 passed；生产构建与 Vue TypeScript 通过。
- Space Studio Playwright：24/24 passed；其中新增交接场景验证当前 CAD 来源预选，共享 fixture 同步到当前整仓模板和 CAD Preparation 合同。
- 完整 `CP6.slnx` Release：0 warning / 0 error。
- GA 普通证据校验通过；严格校验按预期非零退出并报告 `NoGo`、5 项输入/9 项门禁/5 位签字人 Pending。

## 未关闭范围

- 20 份授权真实黄金 CAD、主备 Provider、50MB P95、Iris Xe、CP6 WMS 恢复、双仓 14 天 Pilot 与五方签字仍待真实证据。
- 本报告的 101 项用例验证事务与批量合同，不替代 50MB 性能、10,000 项生产容量或真实 SQL Server 压力接受。
- 完整 DWG/DXF + Excel、底图和空白画布浏览器三路径仍须在正式接受环境运行并归档证据。

因此 LM-FR-019/019A 只关闭仓库实现；WP4 和核心 GA 继续 `Partial/Pending`、72% / `NoGo`。
