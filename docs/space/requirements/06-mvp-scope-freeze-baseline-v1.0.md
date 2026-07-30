# CP6 3D Space MVP Scope Baseline v1.0

- 状态：**Scope Frozen / Development Ready Gate Pending Sign-off**
- 基线日期：2026-07-25
- 代码核验基线：`feat/gr-vp-t6@1524289fbac6f94b81b69a6fe1ce2f48fceb02dd`
- 适用范围：CP6 3D Space MVP（E00～E09、E13）

## 1. 基线用途

本文件是 Space MVP 范围、公开契约、实施顺序和发布门槛的冻结登记。它解决四个问题：

1. 区分“需求已经确认”和“代码已经实现”。
2. 把产品范围之外的技术未知项收敛为 ADR 和试验门槛。
3. 阻止开发阶段自行改变 API、数据边界、权限和状态语义。
4. 让 E00、E01 和前置技术试验可以在不重新讨论产品方向的情况下启动。

发生冲突时，实施优先级如下：

1. 本冻结基线和已批准的 Scope Change RFC。
2. `requirements/01`～`05`。
3. `design/01`～`06` 和 `contracts/ai/v1`。
4. 历史 `docs/space/00`～`09` 与旧总 Spec。

本文件冻结范围，不声称 MVP、Alpha、Beta 或 GA 已经交付。

## 2. 状态词典

| 状态 | 含义 | 是否阻塞 E00/E01 |
|---|---|---|
| `Frozen-MVP` | MVP 必须完成，实施者不得删减或后置 | 否 |
| `Deferred-P2/P3/P4` | 产品方向保留，但不阻塞 MVP | 否 |
| `Technical-ADR` | 产品行为已定，只允许在硬门槛内选择技术方案 | 否；可能阻塞对应 Beta 能力 |
| `Excluded` | 本产品蓝图明确不建设 | 否 |
| `Implemented` | 当前主工作树已经存在且有相应验证 | 否 |
| `Partial` | 仅有部分能力，或只存在于未合入候选工作树 | 否 |
| `NotStarted` | 代码尚不存在 | 否 |

需求状态和实现状态是两个独立维度。`Frozen-MVP + NotStarted` 表示必须开发，不表示需求待确认。

## 3. 产品决策冻结登记

| ID | 冻结结论 | 范围状态 |
|---|---|---|
| D01 | 先理清 Space 业务与开发逻辑，再支撑展示、真实仓库和 SaaS 产品化 | Frozen-MVP |
| D02 | Space 是 CP6 一级模块，边界独立，可独立部署并连接 ERP/WMS | Frozen-MVP |
| D03 | 多租户；内部用户及客户、供应商、3PL 可以登录 | Frozen-MVP |
| D04 | MVP 包含低成本建模、人工校正、版本发布、库位同步 WMS、3D 库存和拣货任务 | Frozen-MVP |
| D05 | CAD+Excel 与地图编辑器都是 MVP，并写入同一统一模型 | Frozen-MVP |
| D06 | 首个验收仓是通用货架仓，预留货主、批次、容器和制造属性 | Frozen-MVP |
| D07 | 缺少真实仓数据时使用标准样本和 WMS 模拟器验收 | Frozen-MVP |
| D08 | 旧 Site/Floor/Zone/Aisle/Rack/Location 是 Published 运行态物化；新增版本化设计工作区 | Frozen-MVP |
| D09 | 整仓发布；楼层可以独立编辑 | Frozen-MVP |
| D10 | 同时支持 Space 新建库位和存量 WMS 库位采纳/回置 | Frozen-MVP |
| D11 | 平台公共模板和租户私有模板；v1 不建设跨租户市场 | Frozen-MVP / Deferred |
| D12 | CAD/Excel 只写 Draft；校验后发布；回滚采用历史版本重新发布 | Frozen-MVP |
| D13 | AI 完整仓库生成进入 MVP；DWG/DXF CAD IR 为首版输入 | Frozen-MVP |
| D14 | AI 只产提案，人工审查后原子 Apply 到 Draft，永不自动发布 | Frozen-MVP |
| D15 | 确定性引擎负责坐标、几何、拓扑、碰撞、货架层和库位编码 | Frozen-MVP |
| D16 | 原始文件默认不发外部 AI；既有租户 AI 默认 Disabled | Frozen-MVP |
| D17 | 客户、供应商、3PL 只能读取 Published，不能访问 Draft、源文件或 AI 内部信息 | Frozen-MVP |

## 4. 架构决策冻结登记

| ID | 冻结结论 | 状态 |
|---|---|---|
| D1 | 独立版本化设计工作区；旧表作为 Published 运行态物化 | Frozen-MVP |
| D2 | 强类型修订、通用元素和完整版本快照 | Frozen-MVP |
| D3 | 发布采用可恢复 Saga；WMS 成功并验证后才激活 Published | Frozen-MVP |
| D4 | 每仓一个活动 Draft；楼层并行编辑使用租约和乐观修订 | Frozen-MVP |
| D5 | `LocationLogicalId` 是稳定身份，坐标是可重算缓存 | Frozen-MVP |
| D6 | 保留 Legacy API，新增 `/api/space/design/v1`，按 Site 切换 | Frozen-MVP |
| D7 | Contract-first，生成 TypeScript/C# SDK，HTTP 为权威，SignalR 只提示 | Frozen-MVP |
| D8 | Contracts/Domain/Application/Infrastructure 模块化单体 | Frozen-MVP |
| D9 | 每个 Site 短冻结、Bootstrap、验证、切换，不长期双写 | Frozen-MVP |
| D10 | Design v1 使用 RFC Problem Details 和稳定错误码 | Frozen-MVP |
| D11 | RBAC 加 Space 数据范围评估器；外部主体使用多维 Grant | Frozen-MVP |
| D12 | 文件扫描和 CAD/PDF/Excel 解析在隔离 Worker 运行 | Frozen-MVP |
| D13 | 数据库 Job Ledger 是任务权威，队列只可作为唤醒器 | Frozen-MVP |
| D14 | Scene Manifest+Chunk；语义源、渲染物和运行 Overlay 分离 | Frozen-MVP |
| D15 | 确定性标准仓、真实 SQL、适配器契约、故障注入和安全/性能门禁 | Frozen-MVP |

## 5. AI 决策冻结登记

| ID | 冻结结论 | 状态 |
|---|---|---|
| T1 | 混合、Provider 中立；Mock、本地和外部实现使用同一端口 | Frozen-MVP |
| T2 | 提案集、人工审查、原子 Apply 到 Draft，永不自动发布 | Frozen-MVP |
| T3 | 父需求、独立 AI Spec、详细设计卷六和 E13 分别追踪 | Frozen-MVP |
| T4 | MVP 只处理结构化 DWG/DXF CAD IR；PDF/图片视觉识别和自然语言生成后置 | Frozen-MVP / Deferred |
| T5 | 外部 Provider 不接收原始 CAD，只接收最小化结构特征 | Frozen-MVP |
| T6 | AI 不负责单位、坐标、几何、拓扑、碰撞和编码 | Frozen-MVP |
| T7 | 既有租户默认 Disabled；试点租户由管理员显式启用 | Frozen-MVP |

## 6. MVP、后续和排除项

### 6.1 Frozen-MVP

- 三条建模路径：
  - DWG/DXF → CAD IR → 规则/AI 提案 → 人工审查。
  - CAD+Excel → 几何与业务属性映射。
  - PDF/PNG/JPG 底图或空白画布 → 地图编辑器/组件模板。
- 通用仓库元素：墙、柱、门、月台、库区、巷道、货架、托盘及静态常见设备。
- 货架逐层规格、库位生成、存量库位绑定。
- Draft、校验、预览、发布、对账、历史版本重新发布。
- CP6 WMS 适配器、WMS 模拟器和标准适配接口。
- 3D 单层/多层/整仓查看、库存与拣货任务叠加。
- 客户、供应商、3PL 多维范围下的 Published-only 门户。
- AI Run、提案、决策、来源追踪、配额、费用和人工锁定。

### 6.2 Deferred-P2/P3/P4

- P2：人员位置/轨迹、WCS/AGV/IoT 实时状态、告警、货主/SKU/ABC 叠加和运营总览。
- P3：路径、拥堵、容量诊断；库位/上架/人员建议；审批下发和执行反馈。
- P4：规划分支、历史订单仿真、方案比较、标准报告和 DWG 回写。
- PDF/图片计算机视觉自动建模。
- 自然语言生成完整仓库。
- 桌面端、移动端和离线客户端的独立产品能力。
- 跨租户模板市场。

### 6.3 Excluded

- AI 直接写 Published 或自动发布。
- Space 保存库存数量作为业务真相。
- 长期 Design/Legacy 双写。
- 外部协作用户访问源文件、Draft、Prompt、提案、费用或运行日志。
- 依赖用户手工另存 DXF 作为正式 DWG 唯一路径。
- MVP 园区交通仿真、高精度物理仿真和任意非标准 CAD 的无人工全自动识别承诺。

## 7. Technical-ADR 登记

| ADR | 冻结的产品方向 | 试验输出 | 回退与阻断 |
|---|---|---|---|
| [ADR-0001 CAD 转换](../adr/0001-cad-conversion-selection.md) | 用户输入 DWG/DXF，服务端生成统一 CAD IR | 主方案、备选、实体/版本矩阵、授权和性能报告 | 无方案通过时阻断 DWG Beta；DXF/底图/编辑器继续 |
| [ADR-0002 AI Provider](../adr/0002-ai-provider-selection.md) | Provider 中立、原文件不外发、规则路径独立 | 首个 Provider、区域、保留、SLA、成本和质量证据 | 不通过时外部 AI Disabled，Mock/本地/规则继续 |
| [ADR-0003 Design V1 迁移](../adr/0003-design-v1-migration-baseline.md) | 受控整合候选工作树，按 Site 切换 | 文件归属、Migration 基线、集成顺序和回退证据 | 不整体合并脏工作树，不触碰其他模块改动 |
| [ADR-0004 性能环境](../adr/0004-performance-acceptance-environment.md) | 既定门槛不可由实现团队放宽 | 固定终端、浏览器、服务端、数据包和证据格式 | 不达标阻断相应发布阶段，须优化或走 RFC |

ADR 只解决技术方案，不得重新打开 D、T 或架构决策。

## 8. 公开契约冻结

### 8.1 受保护契约

- `/api/space/design/v1` 及 Generation Run API。
- `WarehouseGenerationInput/Result` Provider 端口。
- Run、Proposal、Decision 状态机。
- `TenantId`、`ModelVersionId`、`BaseContentRevision`、`SourceHash` 追踪约束。
- AI 权限和稳定错误码。
- Scene Manifest/Chunk 和 Published 运行态边界。
- Legacy API 兼容和按 Site 切换。
- 外部主体 Published-only。
- Proposal Apply 只写 Draft；Publish Saga 才能改变 Published。

### 8.2 变更规则

冻结后允许：

- 新增向后兼容的可选字段。
- 新增不改变既有语义的端点。
- 修正文案、示例和不可执行的笔误。

冻结后禁止：

- 删除或重命名字段、端点、权限或错误码。
- 改变状态含义、转换前置条件或幂等语义。
- 把 AI Apply 与 Publish Saga 合并成一个动作。
- 放宽租户隔离、原文件外发或外部用户可见范围。

破坏性变更必须提交 Scope Change RFC，至少记录：原因、受影响 D/T/Epic、数据迁移、兼容期、权限影响、指标影响、估算变化、回滚方案和五方批准。

## 9. 当前实现状态

核验日期：2026-07-30。

| 能力 | 实现状态 | 证据与说明 |
|---|---|---|
| Legacy Site/Floor/Zone/Aisle/Rack/Location | Implemented | 主工作树 `CP6.Entity/DomainModels/Space` 有 11 个运行态实体 |
| Legacy 编辑、导入导出、编码和库位发布 | Partial | 主工作树 `CP6.Core/Services/Space` 和 `/api/space/...` 已有能力，但不是 Design V1 仓库版本 Saga |
| Three.js Viewer 与库存/路径页面 | Partial | `cp6.web/src/views/space` 已有编辑器、FloorViewer 和 StackedViewer |
| Design V1 Contracts/Domain/Application | Partial | `539d56de` 已集成 E01 S01–S03 的版本、来源文件和 Job Ledger 底座；后续用例仍待逐项进入基线 |
| Design V1 Worker、校验与本地物化 | CandidateOnly | `0d25da4d` 有候选实现和测试，尚未受控合入 |
| `BuildScene`/`Import` Job | CandidateOnly | `0d25da4d` 有候选实现，尚未按 E01/E02 依赖链复验 |
| 文件、来源、Artifact 和 CAD/Excel 解析链 | Partial | 来源文件底座已在 `539d56de`；Artifact、CAD/Excel 链仅在 `0d25da4d` 候选中 |
| AI Run/Proposal/Decision/Usage | NotStarted | 只有需求、设计和 JSON Schema |
| 客户/供应商/3PL 多维 Portal | NotStarted | 主工作树无 Space ExternalOrganization/Grant 实现 |
| CP6 WMS 发布 Saga 与标准模拟器 | Partial | Legacy 事件发布存在；Design V1 可恢复外部 Saga 尚未完成 |

候选工作树不是实现真相。`0d25da4d` 只是保全检查点；只有经过 ADR-0003 的文件审计、按子任务独立提取、迁移验证和测试后，才能更新为 Implemented。当前唯一 Space 集成基线是 `539d56de`。

## 10. 固定实施批次

| 批次 | 范围 | 进入条件 | 退出条件 |
|---|---|---|---|
| A | E00、迁移基线、功能开关、API 兼容 | 本基线已冻结 | 当前事实清单可重复生成；Legacy 未回归 |
| B | E01、E02-S01、E07 契约/模拟器、E13 Provider 试验 | 批次 A 护栏生效 | 版本/文件/任务底座可用；CAD/Provider ADR 有证据 |
| C | E02～E05、E13 数据/校验/生成 | E01 来源和 Job Ledger 可用 | 三条建模路径写入统一 Draft |
| D | E13 审查/Apply、E06 校验/发布/对账 | 统一 Draft 和 WMS 契约可用 | Apply 零部分写；发布失败可恢复 |
| E | E08 运行态、E09 外部协作 | Published 运行态稳定 | 3D 库存/任务和外部 Published-only 门禁通过 |

子任务必须包含输入、输出、依赖、权限、错误码、测试和回滚，并控制在 1～3 工程师日。E02-S01 和 E13-S05 完成后重新估算；196 工程师日是规划基线，不是排期承诺。

## 11. 统一发布关卡

本节是 Alpha/Beta/GA 的权威口径。其他文档只补充测试方法，不改变进入条件。

### 11.1 MVP Alpha

- 标准底图/模板路径完成 500 货架、10,000 库位建模。
- WMS 模拟器的发布、库存和拣货任务闭环通过。
- Design V1 按租户/Site 开关启用，Legacy 不回归。
- 文件隔离、Job 重试和恢复路径可运行。
- ADR-0001 已产生技术/授权证据；授权和部署未批准前不得承诺 DWG Beta 日期。

### 11.2 MVP Beta

- 标准 DXF、受控 DWG、Excel 和地图编辑器路径通过。
- 同一验收仓的 2D、3D 和机器可读清单一致。
- 20 份/5 类黄金数据完成，覆盖率 ≥80%、整体准确率 ≥90%、高置信度精确率 ≥95%。
- 50MB CAD 到提案 P95 ≤15 分钟，上传到首次 Ready ≤60 分钟。
- AI 辅助相对纯地图编辑器的人工操作量下降 ≥70%。
- AI Apply 只写 Draft；故障和 Revision 冲突零部分写入。
- WMS 成功并验证后才激活 Published；失败可重试和对账。
- SQL Server 集成、WMS 契约、Provider 故障和事务故障注入通过。

Beta 可以仅面向内部租户用户。外部 Portal 可以试点，但不是 Beta 的进入条件。

### 11.3 MVP GA

- CP6 WMS 真实适配器通过 Certified 契约。
- 客户、供应商和 3PL Portal 权限矩阵及跨租户越权测试通过。
- 10,000 库位性能门槛全部通过。
- 发布部分成功、本地激活失败和 `ReconciliationRequired` 均有恢复证据。
- 安全、审计、备份、恢复、告警和运维手册完成。
- 发布证据记录浏览器版本、Migration、验收包版本和应用提交 SHA。
- 产品、架构、QA、WMS 和安全负责人完成签字。

任一跨租户测试、适配器契约、真实 SQL、恢复或性能门槛失败都阻断 GA。

## 12. Development Ready 门禁

| 检查项 | 状态 | 证据 |
|---|---|---|
| D01～D17、D1～D15、T1～T7 已登记 | Ready | 本文件 §3～§5 |
| MVP/Deferred/Excluded 已分离 | Ready | 本文件 §6 |
| 四个技术 ADR 有硬门槛和回退 | Ready | `docs/space/adr` |
| API/状态机/权限进入冻结保护 | Ready | 本文件 §8 |
| 五类种子资产和验收协议存在 | Ready | `docs/space/acceptance` |
| E00/E01 启动顺序明确 | Ready | 本文件 §10 |
| 产品负责人确认范围 | Confirmed by approved implementation plan | 本文件和任务记录 |
| 架构负责人确认 ADR | Pending role evidence | 签字表 |
| QA 负责人确认验收包 | Pending role evidence | 签字表 |
| WMS 负责人确认模拟与发布闭环 | Pending role evidence | 签字表 |
| 安全负责人确认外发/权限边界 | Pending role evidence | 签字表 |

范围已经冻结，E00/E01 可以启动。`Development Ready` 的正式发布标签在五方签字证据齐全后生效。

## 13. 签字记录

| 角色 | 姓名/标识 | 结论 | 日期 | 证据链接 |
|---|---|---|---|---|
| 产品负责人 | 待登记 | 范围已由计划确认，待补标识 | 待登记 | 本冻结基线 |
| 架构负责人 | 待登记 | Pending | 待登记 | ADR-0001～0004 |
| QA 负责人 | 待登记 | Pending | 待登记 | 验收包 |
| WMS 负责人 | 待登记 | Pending | 待登记 | WMS 场景矩阵 |
| 安全负责人 | 待登记 | Pending | 待登记 | 权限与外发矩阵 |

签字是审计证据，不允许由开发人员代填。签字前不影响 E00/E01 开工，但阻止正式标记 `Development Ready`。

## 14. 追踪入口

- [产品需求](./01-product-requirements.md)
- [Epic 与子 Spec](./03-epic-and-spec-backlog.md)
- [低成本建模 Spec](./04-low-cost-3d-modeling-spec.md)
- [AI 完整仓库生成 Spec](./05-ai-warehouse-generation-spec.md)
- [验收资产索引](../acceptance/README.md)
- [Development Ready 实施交接](./07-development-ready-handoff.md)
- [Scope Change RFC 模板](./08-scope-change-rfc-template.md)
- [详细设计索引](../README.md)
