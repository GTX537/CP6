# Scope Change RFC-003：Space Studio v1.3

- 状态：产品决定已冻结；跨职能批准 Pending，尚未生效为 GA 基线
- RFC 编号：`SPACE-RFC-2026-003`
- 提出人：产品（本次实施请求）
- 日期：2026-08-12
- 影响基线：低成本 3D 建模 Spec v1.2、MVP Scope Freeze v1.0

修订方式：以 v1.2 的完整详细正文为底稿增量合并；RFC 中未明确变更的领域模型、状态机、接口、失败恢复、权限、测试与验收细节继续有效，不允许用 v1.3 摘要覆盖或删减。

## 1. 变更摘要

将 Space Studio 定为单一建模工作台，引入强 Floor 编辑租约和 CAD Job 自动审核加载；同时把外部 AI Provider 调整为独立 Beta、收紧 Viewer 性能门槛并移除 Supplier 现场 UAT。

## 2. 触发原因

- 技术与产品证据：现有 `DesignUnderlayView`、旧 `FloorEditor`、Design V1、CAD/Excel、校验和发布能力分散，形成双编辑权威和人工 JSON 搬运。
- 安全要求：并发编辑必须以租约、Revision 与幂等键失败关闭；接管必须有专有权限、理由与不可变审计。
- 验收完整性：外部 AI 供应商、真实黄金 CAD 和两仓 Pilot 尚无签字证据，不能阻断确定性核心主链，也不能被开发实现冒充 GA。

## 3. 影响登记

| 项目 | 影响 |
|---|---|
| D01～D17 | 保留现有领域、版本、Draft/Published/WMS 边界；页面权威收敛到 Space Studio |
| D1～D15 | 增加租约并发边界、CAD 产物自动加载和 Viewer 性能门槛；其余决策不变 |
| T1～T7 | 外部 AI Provider 从核心 GA 调整为独立 Beta；RuleOnly 仍属核心确定性路径 |
| Epic/子任务 | 新增 Space Studio 壳层、Lease API/SQL、CAD Review Workspace 与恢复自动化 |
| API/SDK | 命令批新增必填 `leaseId`；新增五个 Lease 操作；CAD Job 增加自动审核读取 |
| 数据/Migration | 加法新增 Lease、TakeoverAudit 及命令批 LeaseId，不删除或重写既有数据 |
| 权限/多租户 | 接管同时要求 `space:model:edit` 与 `space:model:lease:takeover`；所有新链保持 Tenant/Site fence |
| AI 外发/保留 | 规则路径零 Provider；外部 AI 继续默认关闭并受原外发/保留策略约束 |
| Alpha/Beta/GA | 本 RFC 只冻结产品方向；跨职能批准、真实证据和 Pilot 完成前不构成 GA |
| 196 工程师日基线 | 不重估既有总基线；实际执行按独立任务分支与证据门禁核算 |

## 4. 当前证据

- 复现或试验：v1.2 1,167 行正文全部作为 v1.3 有序子序列保留；批准的 1440×900、1280×720 与窄屏基线已纳入仓库。
- ADR：现有 Design V1、文件隔离、Job Ledger、Draft/Published 与 Provider 边界 ADR 继续有效。
- 安全/法律意见：接管审计与外部主体拒绝由机器合同锁定；真实 Provider 法务、区域和 DPA 尚未签字。
- 客户影响：核心确定性建模可独立推进；外部 AI、真实 DWG Provider 与现场 Pilot 在批准前不作生产承诺。

## 5. 建议变更

| 主题 | 旧契约 | 新契约 | 兼容与生效 |
|---|---|---|---|
| 页面权威 | `DesignUnderlayView` 与旧 `FloorEditor` 并存 | `DesignUnderlayView` 为唯一工作台；旧编辑器仅迁移成熟交互 | Site 按 Design V1 切换；不长期双写 |
| 编辑并发 | Revision 与幂等键 | 90 秒 Floor Lease + 30 秒续租 + Revision + 命令幂等 | 新 Design V1 SDK 同步升级；旧非 Design V1 路径不变 |
| CAD 审核 | 可人工下载/上传 JSON 工件 | Job 成功后自动加载绑定 Source/Job/SHA/BaseRevision 的审核空间 | 本地 JSON 仅为高级恢复入口 |
| 外部 AI | 核心 GA 候选 | 独立 Beta，不阻断 RuleOnly 核心 GA | 默认关闭；独立审批后按 Site 启用 |
| Viewer | 旧性能目标 | 首交互 ≤3s、P95 帧时间 ≤20ms、拾取 ≤150ms、着色 ≤3s | 真实 500/10,000 场景验收后生效 |
| Supplier UAT | 现场业务 UAT 参与者 | 只参加自动化权限/越权矩阵 | 不减少 Supplier 安全测试 |

## 6. 替代方案

1. 不改变冻结范围：继续维护两个编辑器并人工搬运工件。不可接受，因为会保留双权威、并发覆盖与不可审计恢复。
2. 外部 AI 继续阻塞核心 GA。不可接受，因为供应商合规与真实 Provider 证据尚未完成，而确定性 RuleOnly 主链可独立交付。
3. 只使用 Revision、不增加 Lease。不可接受，因为无法表达持有人、过期、等待、接管和会话恢复。

## 7. 迁移与兼容

- 数据迁移：仅加法；未发布迁移在分支内重新生成并保持 Snapshot/Designer 一致。
- API/SDK 兼容：Design V1 OpenAPI 与 C#/TypeScript SDK 同步生成；`leaseId` 为新工作台写入的强制合同。
- 租户/Site 开关：继续由 Design V1 cutover 控制，不影响未切换 Site。
- 外部用户影响：Customer/3PL 仍只读 Published；Supplier 不获得 Draft、Source、Lease 或 Publish 权限。

## 8. 测试和验收

- Lease 五操作、body/schema required、双权限、稳定 409 与接管审计由契约测试覆盖。
- SQL Server 覆盖唯一 Floor 槽、数据库时间、同用户不同 Client fence、续租/释放/接管与不可变审计。
- CAD Review Workspace 覆盖成功 Job→Clean PreviewSet→身份/SHA/BaseRevision→Workspace，以及 Draft 前进后的 stale 409。
- 前端覆盖四栏布局、1280 完整编辑、窄屏只读、held/takeover、输入焦点快捷键、发布 Blocking 与 2D/3D 同源。
- 真实 Provider、20 份授权黄金 CAD、Viewer 500/10,000、两仓各 14 天 Pilot 和 WMS 恢复演练仍为 GA 硬门槛。

## 9. 回滚

- 产品/页面：在 Site 切换前可回到旧页面入口；不得建立长期双写。
- 数据：保留加法表和审计，不删除历史；停止新 Lease 写入即可回退应用版本。
- API：仅在尚未批准/发布的 Design V1 版本内调整；一旦对外发布，采用前向兼容而非破坏性删除。
- 不可逆数据：接管审计保持不可变；回滚不清除审计或已发布版本。

## 10. 批准

| 角色 | 结论 | 标识 | 日期 | 备注 |
|---|---|---|---|---|
| 产品负责人 | Approved | 本次实施请求 | 2026-08-12 | 产品方向与 Spec v1.3 内容冻结 |
| 架构负责人 | Pending | | | 需审查并发、迁移与发布边界 |
| QA 负责人 | Pending | | | 需核对机器证据、黄金 CAD 与 Pilot |
| WMS 负责人 | Pending | | | 需签署发布恢复与 CP6 WMS 一致性 |
| 安全负责人 | Pending | | | 需签署接管权限、外部主体与 AI 边界 |

所有受影响角色批准前，本 RFC 不生效为跨职能/GA 基线。当前 v1.3 是已冻结的产品实施提案与核心开发基线，不等于 GA 批准。
