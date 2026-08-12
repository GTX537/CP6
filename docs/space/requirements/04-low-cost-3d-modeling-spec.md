# CP6 低成本 3D 建模 Spec

版本：v1.3 冻结版

冻结日期：2026-08-12

状态：产品 Spec 冻结；GA 仍须通过自动化、真实 CAD、两仓 Pilot 与签字门禁

本版本完整取代 v1.2 的产品行为与验收口径。领域实现继续复用六卷详细设计；范围变化见 [Scope Change RFC-003](09-scope-change-rfc-space-studio-v1.3.md)。

## 1. 产品结论

仓库主数据人员无需 Blender/3ds Max，通过三条路径建立同一份可编辑、可校验、可发布的仓库模型：

1. DWG/DXF → 确定性解析 → 人工复核 → Draft。
2. CAD + Excel → 几何与业务属性匹配 → Draft。
3. PDF/图片底图或空白画布 → 构件库与批量编辑 → Draft。

现有后端、Design V1、2D/3D、校验、发布和 WMS 是领域权威。本版本把 `DesignUnderlayView`、旧 `FloorEditor` 的成熟交互、CAD 审核、编码、3D 预览和发布入口收敛为一个 Space Studio，并补齐编辑租约、解析产物自动加载和正式 UX 状态。真实 CAD、现场 Pilot 和生产签字不得用合成数据替代。

## 2. GA 范围

### 核心 GA

- 通用货架仓、多楼层、库区、巷道、货架、库位、墙柱门月台及静态设备。
- 真实 DWG 与 DXF 都是硬门槛；一个主 Provider 与一个同合同、同审批标准的备用 Provider。
- 规则解析、Excel、底图、手工编辑、批量编码、2D/3D 同源。
- Draft → Validate → Preview → Publish → CP6 WMS → Published 闭环。
- WMS 驱动容量、ABC、库存诊断和上架推荐；不自动执行上架。
- 客户与 3PL Published-only 门户；生产 Viewer 只消费 Published。

### 独立门禁

- 规划与仿真独立 GA，不阻塞核心 GA。
- 外部 AI Provider 独立 Beta，不阻塞规则解析与核心 GA。
- 人员调度、设备告警保持 Preview。
- Supplier 只参加自动化权限/越权矩阵，不参加现场业务 UAT。

### 不包含

园区道路/门岗/停车/室外交通、真实 PDA/WCS/IoT 控制、第三方 WMS 通用兼容、DWG 回写、任意非标准 CAD 全自动识别、自动执行上架或调度以及“5 分钟数字孪生”等营销承诺。

## 3. Space Studio 工作台

```text
┌─ 44px 标题栏：站点 / 楼层 / 版本 / 保存状态 ────────────────────┐
├─ 60px 命令栏：2D/3D、编辑工具、撤销、校验、发布 ────────────────┤
│ 52px 模式栏 │ 244px 上下文面板 │ 主画布 │ 324px 检查器          │
│ 来源/构件   │ 当前模式内容     │ 2D/3D  │ 属性/批量/问题        │
├───────────────────────────────────────────────────────────────┤
└─ 30px 状态栏：坐标、比例、选择、保存、阻断、性能 ────────────────┘
```

- 左侧来源、构件、图层、历史、设置一次只展开一个；右侧固定属性、批量、问题。
- 顶部固定版本、保存、撤销/重做、2D/3D、校验与“校验并发布”。
- 同页 3D 使用当前本地场景并保留选择与视角；草稿不展示实时库存/人员/设备。
- 授权 Demo 模式持续显示 `Simulated`；生产 Viewer 与工作台分离。

### 关键状态

| 状态 | 用户可见行为 |
|---|---|
| 首次进入 | 可折叠四步清单：导入来源、复核识别、补齐编码、校验发布 |
| 后台解析 | Draft 可继续用；显示阶段、进度、耗时、取消和失败原因 |
| 解析完成 | 待审变更展示新增、修改、删除、冲突、低置信度；确认后才合入 |
| 解析失败 | Draft 不变；可重试或更换来源 |
| 保存 | 显示保存中、已保存时间和未保存数量 |
| 租约占用 | 只读；显示持有人与过期时间；可等待或申请接管 |
| 租约丢失 | 立即只读；保留未同步命令并可导出恢复草稿 |
| Revision 冲突 | 禁止覆盖；要求刷新、重放或放弃本地命令 |
| 校验失败 | 显示 Blocking/Warning/Info；Blocking 禁止发布 |
| 发布失败 | 旧 Published 持续服务；可重试或进入对账 |
| 窄屏 | 小于 1280px 自动只读，只保留 3D、版本和问题查看 |

### 视觉与可达性

工作台使用作用域 `--space-studio-*` token，保留 CP6 标识、青色识别及成功/警告/阻断语义。1440×900 为基准，1280×720 为最低完整编辑尺寸。正文/问题 ≥16px，标签/元数据 13–14px，对比度 ≥4.5:1；点击与焦点热区 ≥44×44px；全部操作可 Tab 到达并有清晰焦点环。GA 快捷键覆盖撤销、重做、全选、删除、Esc、选择、平移、测量、保存、问题定位和帮助；发布无一键快捷键。

## 4. 接口与数据约束

继续复用 Design V1 的 CAD 上传/解析/Preview/问题工件、RuleOnly Generation、Excel–CAD 匹配、`expectedFloorRevision`、`ContentRevision`、CommandBatch 幂等、Validation、Publish、Retry/Reconcile、CP6 WMS Adapter、Published Runtime 和门户契约。CAD 成功后，工作台自动加载 Job 审核空间，不再要求手工搬运 JSON。

### 编辑租约

- `GET .../versions/{versionId}/floors/{floorLogicalId}/lease`
- `POST .../lease`
- `POST .../lease/{leaseId}:renew`
- `POST .../lease/{leaseId}:release`
- `POST .../lease:takeover`，要求 `space:model:lease:takeover` 与原因。

租约默认 90 秒，每 30 秒续租。过期后普通编辑者可重新申请；强制接管生成不可变审计。`ApplySpaceElementCommandBatchRequest.leaseId` 必填。验证顺序为租户、权限、租约、Floor Revision、命令、幂等；任一失败零写入。

稳定错误码：`SPACE_EDIT_LEASE_HELD`、`SPACE_EDIT_LEASE_LOST`、`SPACE_EDIT_LEASE_TAKEOVER_DENIED`、`SPACE_FLOOR_REVISION_CONFLICT`、`SPACE_PARSE_CHANGESET_STALE`、`SPACE_VALIDATION_BLOCKING`、`SPACE_PUBLISH_RECONCILIATION_REQUIRED`。

## 5. 质量门槛

### CAD 与建模

- 上传 100MB；50MB 为标准性能档；50MB CAD 到可审查草稿 P95 ≤15 分钟，受训人员到首次 `Ready` ≤60 分钟。
- 20 份授权真实黄金 CAD，Calibration/Validation/Holdout = 10/5/5。
- 目标元素覆盖率 ≥80%，整体准确率 ≥90%，高置信度精确率 ≥95%。
- 未识别、低置信度和 Blocking 全部显式展示；Release Holdout 无未报告 Blocking 遗漏。
- 2D、3D 与机器清单的 LogicalId、数量、尺寸、编码和逐层规格 100% 一致。

### Viewer

Iris Xe 级集显、WebGL2、500 货架/10,000 库位：首次可交互 ≤3 秒；P95 帧时间 ≤20ms；拾取 P95 ≤150ms；库存批量着色 P95 ≤3 秒。

### WMS 与恢复

核心 GA 只认证 CP6 WMS。自动故障恢复 ≤15 分钟；人工对账 ≤4 小时；失败期间旧 Published 可用；同一 PublishPlan 重试无重复库位、事件或外部写入。

## 6. 测试与验收

自动化覆盖工作台布局/模式/解析/待审变更/租约/Revision 冲突/2D-3D 同源/问题定位/发布阻断/窄屏/键盘；Lease 与 CAD 工件契约；租约并发、命令幂等、Validation 失效、发布恢复；DWG+Excel、DXF+Excel、底图、空白画布、发布/采纳/恢复/历史重发；外部主体、客户、3PL、Supplier 与跨租户权限矩阵。

现场 Pilot：一个绿地仓和一个存量改造仓各连续 14 天，零 S1/S2；S3 有绕行且签字前关闭；记录建模时长、人工修改量、WMS 一致性、恢复、开放问题和业务结果。

GA 由产品、QA、WMS、架构、安全签署；客户仓库代表和实施负责人确认 Pilot 结果但不作为 GA 审批人。按 Site 灰度迁移，不长期双写。远端 `main` 包含代码、测试、状态文档与真实证据后才能声明完成。

## 7. 已批准设计基线

- [2D 工作台基线](C:/Users/tt/.gstack/projects/cp6/designs/low-cost-3d-workbench-20260812/workbench-review-2d.png)
- [3D 草稿预览基线](C:/Users/tt/.gstack/projects/cp6/designs/low-cost-3d-workbench-20260812/workbench-review-3d.png)
- [交互线框](C:/Users/tt/.gstack/projects/cp6/designs/low-cost-3d-workbench-20260812/workbench-review.html)

线框冻结结构、密度和交互方向；本 Spec 的字号、对比度和热区优先。页面以 `DesignUnderlayView` 和 `/api/space/design/v1` 为主链；旧 `FloorEditor` 只迁移成熟交互，不发展第二套权威。
