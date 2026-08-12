# Scope Change RFC-003：Space Studio v1.3

状态：已纳入产品冻结基线；GA 签字待真实验收
日期：2026-08-12
影响基线：低成本 3D 建模 Spec v1.2、MVP Scope Freeze v1.0

## 变更

1. 外部 AI Provider 从核心 GA 移为独立 Beta；规则解析与人工审核保留在核心 GA。
2. Viewer 收紧为首次可交互 ≤3 秒、P95 帧时间 ≤20ms、拾取 P95 ≤150ms、批量着色 P95 ≤3 秒。
3. Supplier 只进入自动化权限/越权矩阵，不参加两仓现场业务 UAT。
4. `DesignUnderlayView` 成为 Space Studio 页面权威；旧 `FloorEditor` 不再发展为第二套权威。
5. 编辑命令强制绑定 90 秒 Floor 租约；接管需要专有权限、原因和不可变审计。
6. CAD Job 成功后由工作台自动加载审核空间，不把人工 JSON 搬运作为标准路径。

## 理由

核心价值是确定性建模、可复核和可恢复发布，外部 AI 不应阻塞可控主链。生产 Viewer 必须在目标集显环境达到可操作流畅度。Supplier 的现场业务场景不足，但权限面必须自动化证明。单一工作台与强租约消除双编辑权威和并发覆盖。

## 风险与补偿

- 外部 AI Beta 需要独立数据出境、Provider 合同和预算门禁。
- Viewer 需要真实 500/10,000 场景基准，不能用小场景外推。
- 租约丢失保留未同步命令并允许导出恢复草稿。
- 自动审核加载验证 Artifact SHA、Job/Source/Version/Floor 链与 ContentRevision 新鲜度。

## 验收状态

- 产品冻结：已由本次实施请求确认。
- 技术、安全、QA、WMS GA 签字：未完成，不预填身份或日期。
- 两仓 Pilot 与黄金 CAD：未完成，必须补齐真实证据后申请 GA。
