# CP6 Space 空间数字底座 · 设计总纲

> **定位**：把 CP6 从"有 WMS 的内部系统"升级为带**商用空间数字底座**的产品。Space 是独立顶级模块（与 ERP/MES/WMS/OA/PUB 平级），参考菜鸟空间 3D 可视化，面向**仓库/WMS 客户**，提供 3D 空间建模编辑器 + 可配置库位编码 + 实时库存叠加 + 高级可视化。**不是 WMS 里的轻量 3D 页面，而是可多客户复制的数字底座。**
>
> 技术栈：Vue3 + Three.js / .NET8 + EF Core / SQL Server，多租户 SaaS（沿用 CP6 全表 `TenantId`）。

## 2026-07-25 当前需求基线

本 README 的 00～09 章节仍是当前代码和早期架构的重要参考，但其中“CAD 导入推迟”和“人员/设备不进入总蓝图”的阶段结论已经被新一轮产品决策替代。实施和验收应优先使用：

1. [产品需求说明书](./requirements/01-product-requirements.md)
2. [菜鸟功能目录与 CP6 差距](./requirements/02-cainiao-feature-catalog-and-gap.md)
3. [Epic 与子 Spec 拆分](./requirements/03-epic-and-spec-backlog.md)
4. [低成本 3D 建模详细 Spec](./requirements/04-low-cost-3d-modeling-spec.md)
5. [AI 自动生成完整仓库 Spec](./requirements/05-ai-warehouse-generation-spec.md)
6. [MVP Scope Baseline v1.0](./requirements/06-mvp-scope-freeze-baseline-v1.0.md)
7. [Development Ready 实施交接](./requirements/07-development-ready-handoff.md)
8. [Scope Change RFC 模板](./requirements/08-scope-change-rfc-template.md)

评审通过的详细设计：

1. [卷一：版本、逻辑身份、数据模型与迁移](./design/01-version-identity-data-migration.md)
2. [卷二：CAD/Excel、文件安全与后台任务](./design/02-modeling-import-files-jobs.md)
3. [卷三：编辑器、通用元素与 2D/3D 同源](./design/03-editor-elements-rendering.md)
4. [卷四：校验、发布、WMS 适配与恢复](./design/04-validation-publish-wms-recovery.md)
5. [卷五：外部组织、授权、审计与质量门禁](./design/05-access-audit-testing-performance.md)
6. [卷六：AI 生成、审查与来源追踪](./design/06-ai-generation-review-provenance.md)

AI Provider 机器契约：

- [输入 JSON Schema v1](./contracts/ai/v1/warehouse-generation-input.schema.json)
- [输出 JSON Schema v1](./contracts/ai/v1/warehouse-generation-output.schema.json)
- [提案修改白名单 v1](./contracts/ai/v1/proposal-patch-policy.md)
- [Provider 输入最小示例](./contracts/ai/v1/examples/warehouse-generation-input.minimal.json)
- [Provider 输出最小示例](./contracts/ai/v1/examples/warehouse-generation-output.minimal.json)

实施前置与验收：

- [技术 ADR 索引](./adr/README.md)
- [验收资产索引](./acceptance/README.md)
- [五类合成 CAD 种子包 v1.0.0](./acceptance/v1.0.0/manifest.json)

### 架构评审结论

| 决策组 | 已锁定结论 |
|---|---|
| D1～D5 数据与发布 | 独立设计版本、强类型完整快照、稳定 LogicalId、每仓单活动草稿、可恢复发布 Saga |
| D6～D10 API 与工程 | Design API v1、生成 SDK、模块化单体、按 Site 切换、Problem Details |
| D11～D14 安全与运行 | 多维数据授权、文件隔离 Worker、数据库 Job Ledger、Scene Manifest/Chunk/Overlay |
| D15 验收 | 确定性标准仓、真实 SQL、适配器契约、故障注入、安全与性能门禁 |
| T1～T7 AI 生成 | Provider 中立；AI 只产提案并原子 Apply 到 Draft；结构化 CAD IR；原文件不外发；确定性几何；既有租户默认关闭 |

> 范围状态、实现状态、技术 ADR 和统一 Alpha/Beta/GA 门槛以 [MVP Scope Baseline v1.0](./requirements/06-mvp-scope-freeze-baseline-v1.0.md) 为准。`Frozen-MVP` 不表示代码已经实现。

新的第一版核心闭环是：

`CAD 规则解析+AI 提案 / CAD+Excel / 底图+编辑器 / 空白模板 → 人工审查与统一空间草稿 → 校正与校验 → 仓库版本发布 → WMS 库位 → 3D 库存和拣货任务`

人员、设备、诊断、优化执行反馈和规划仿真仍属于完整蓝图，但安排在 MVP 之后分期建设。

---

## 一、题眼（先记住这一句）

> **Space 设计版本是空间几何、布局和库位身份的建模真相源；现有 Space 表是当前 Published 运行态物化；WMS 是库存、批次、容器和任务的业务真相源。`LocationLogicalId` 是稳定技术身份，库位编码是可读业务契约，适配器维护外部绑定。**

这是 Space 与 WMS 最关键的边界：Space **不持有库存业务真相**（不写库存数量），WMS **不持有空间几何真相**（不画 3D）。发布只从设计态单向物化到运行态和 WMS，不做长期双写。

---

## 二、五个已敲定的架构决策

| # | 决策 | 理由 |
|---|---|---|
| ① 数据边界 | **混合分权**：Space 管几何/布局，WMS 管库存/业务；LogicalId 稳定关联、库位编码作为业务契约 | 低耦合、各有单一真相，符合 CP6 一贯原则 |
| ② 库位编码 | **可配置编码引擎**，Space 生成·WMS 消费；支持存量客户编码采纳/对账 | 商用多客户复制需要可配置；Space 管布局自然生成编码 |
| ③ v1 范围 | **低成本建模闭环**：CAD+Excel + 地图编辑器 + 版本发布 + WMS 库位 + 3D 库存/拣货任务 | 先解决客户仓库如何低成本、可审计地变成可运行数字空间 |
| ④ 部署 | **多租户 SaaS**，沿用 CP6 `TenantId` | 一套部署多客户、升级统一 |
| ⑤ 建设策略 | **自底向上分阶段** P1→P2→P3 | 空间底座是一切前提，先稳；每阶段可独立演示 |

**继承并修订基线**：保留独立 `Space` 命名空间、Three.js、模板化生成和受控自由布局。CAD+Excel 与地图编辑器现为 MVP 的两条并行建模路径，写入同一种统一空间模型；首批仓型为通用货架仓，预留货主、批次、容器和制造属性。

---

## 三、模块边界与命名空间

```
┌──────────────── Space 空间数字底座（顶级，与 WMS 平级）────────────────┐
│ 空间建模编辑器（模板生成+受控自由布局）│ 可配置库位编码引擎             │
│ 3D 浏览/渲染（Three.js）              │ 实时库存叠加 │ 高级可视化       │
│ 自有真相：站点/楼层/库区/巷道/货架/库位几何 + 编码规则                  │
└───┬───────────────────────────────────────┬──────────────────────────┘
    │ 库位主数据发布（Saga + Adapter）          │ 实时库存只读查询（同步）
    ▼                                          ▼
 WMS 建立/关联库位                          WMS 库存数量/库位状态（唯一真相）
```

- 落 `CP6.Entity/DomainModels/Space`、`CP6.Core/Services/Space`、`cp6.web/src/views/space`。
- 全表带 `TenantId`。前端 3D 渲染封装独立的 `space-viewer` 组件层（Three.js）。

---

## 四、核心数据模型（几何/布局真相，落 DomainModels/Space）

```
Space_Site          站点/仓库   TenantId, Code, Name, 地理信息
Space_Floor         楼层        SiteId, Level, 层高, 底图(可选)
Space_Zone          库区        FloorId, Code, Type(存储/收货/发货/分拣/通道), 多边形几何
Space_Aisle         巷道(可选)  ZoneId, 路径几何             ← 拣货路径(P3)用；无巷道库区可跳过
Space_Rack          货架        ZoneId/AisleId, TemplateId, 位置(x,y,z)+旋转, 列/层/深
Space_Location      库位        Id=LocationLogicalId, RackId, 库位编码, 坐标缓存, 列/层/深, 尺寸
Space_Template      模板        货架/库区模板（模板化生成的来源）
Space_CodeRule      编码规则    TenantId, 分段定义(JSON)
Space_Marker        打点/标注   位置, 类型, 文本（受控自由布局的标注）
```

> **身份与编码分工**：`Space_Location.Id = LocationLogicalId` 是跨版本和 CP6 WMS 的稳定技术身份；库位编码是用户可读、可配置且受发布规则保护的业务契约。外部 WMS 通过 Adapter Binding 保存其自身 ID。

---

## 五、可配置编码引擎

- `Space_CodeRule` 分段定义：区-巷-架-层-位 等段，每段可配 **名称 / 位数 / 分隔符 / 起始值 / 步长 / 取值源**，带实时预览。
- 布局建模完成后，按规则**批量自动生成库位编码**。
- **存量客户迁移**：支持导入现有 WMS 库位编码，映射到几何（"采纳/对账"），而非强制重编——降低存量 WMS 客户接入门槛。

---

## 六、与 WMS 集成（低耦合，契约在 Space 侧）

| 接口 | 方向 | 机制 | 职责 |
|---|---|---|---|
| 库位目录发布 | Space → WMS | **Publish Saga + ISpaceWmsAdapter**；本地激活事务写 Outbox | WMS 幂等应用并回读验证后，才激活 Published 运行态 |
| 实时库存叠加 | Space → WMS | **同步只读** `IWmsStockQuery`（契约定义在 Space 侧，WMS 实现） | 按库位编码查 库存量/库位状态(空/满/锁定/在拣) 做 3D 叠加 |

- Space 编译期**不依赖 WMS 实现**，单向依赖（契约在消费者侧，沿用 [采购模块](../procurement/README.md) 的低耦合手法）。
- 现有 `IntegrationEvent/BridgeHook` 作为 CP6 WMS 兼容基础保留，但新发布结果由持久 Saga、逐项回执和对账决定；不能因事件已写入就报告成功。

---

## 七、端到端能力 × 自底向上分阶段

| 阶段 | 交付 | 完成标志 |
|---|---|---|
| **MVP 低成本建模闭环** | CAD+Excel、底图/地图编辑器、统一元素、逐层货架、仓库版本、WMS 适配/模拟、3D 库存和拣货任务 | 标准仓两条建模路径结果一致，10,000 库位可恢复发布并查看库存/任务 |
| **P2 实时运营叠加** | 人员、设备、IoT 告警、货主/SKU/ABC 和仓库总览 | 3D 上看到有来源和时间语义的现场运行状态 |
| **P3 诊断与执行闭环** | 路径/拥堵/容量诊断、推荐、审批转任务和结果回流 | 建议可解释、可审批、可执行、可评估 |
| **P4 规划仿真** | 方案版本、历史实单仿真、方案对比和 CAD 导出/回写 | 生产与规划隔离，方案收益可比较 |

> MVP 不承诺任意非标准 CAD 的全自动识别，也不做园区交通和高精度物理仿真。它要求标准图层/映射方案下的可解释识别、异常校正和可恢复发布。

---

## 七·五、现有实现章节目录（细分丛书 00～09）

> 00～09 主要解释现有建模、编码、发布、Viewer 和接入设计。历史细分需求见 [`CP6_Space3D_需求分析定稿_修订版.md`](../superpowers/specs/CP6_Space3D_需求分析定稿_修订版.md)；其中与当前五份需求基线冲突的范围和阶段结论不再作为实施依据。

### Part 1 · 空间建模底座（P1，章 00～06）
- [00. 数据模型与坐标系底座](./00-data-model.md) — **P1 地基**，9 表 + 每 Floor 局部坐标系/mm/Z-up/RotationZ + 几何 JSON + GUID 稳定主键 + Aisle 条件父级 + 绝对坐标缓存
- [01. 空间建模编辑器框架 + 模板化生成](./01-editor-template.md) — **P1**，2D 俯视画布 + 模板库 + 批量生成 + 草稿态 + 场景复制/导入导出 + 采纳态反向建模入口（D7）
- [02. 受控自由布局交互](./02-free-layout.md) — **P1**，拖拽/旋转/打点/框选/捕捉对齐/撤销重做/碰撞提示
- [03. 可配置编码引擎](./03-code-engine.md) — **P1**，CodeRule 分段 + 层级遍历生成 + Aisle 条件段 + 实时预览 + 发布冻结闸门（冻编码不冻几何，D4）+ 采纳态绑定既有码（D7）
- [04. 库位发布与 WMS 集成契约](./04-publish-contract.md) — **P1**，`LocationPublished` 批量 upsert/幂等/按 LocationId 版本号 + 变长路径 + 纯几何编辑不发布（D4）+ 存量采纳对账 + 停用与库存冲突（D6）
- [05. 3D 渲染内核 space-viewer](./05-viewer-core.md) — **P1**，Three.js 场景图 + InstancedMesh 分桶 + 视锥剔除 + 标签虚拟化 + Y-up 适配 + 参数化盒体
- [06. 相机 / 拾取 / 导航 / 定位](./06-camera-pick.md) — **P1**，相机控制 + GPU/包围盒拾取→库位编码 + 楼层切换 + **按库位编码定位（D8 的 P1 半）**

### Part 2 · 实时数据叠加（P2，章 07）
- [07. 实时库存叠加](./07-stock-overlay.md) — **P2**，`IWmsStockQuery` 批量只读 + 按需快照/可选轮询（间隔下限/可见区裁剪，D5）+ 状态着色（固定默认色 + 字段预留）+ 库容利用率 + **按物料/批次/容器定位（D8 的 P2 半）**

### Part 3 · 高级可视化（P3，章 08）
- [08. 高级可视化](./08-advanced-viz.md) — **P3**，拣货路径动画（消费 Aisle 中心线）+ 作业热图 + 设备联动（占位）

### Part 4 · 接入收尾（章 09）
- [09. 多租户与 CP6 接入](./09-integration.md) — **收尾**，全表 TenantId + 权限接 PUB + 编码规则按租户 + 登录/菜单接入 + 接入清单

### 原 00～09 构建顺序与当前归属

| 当前归属 | 含章节 | 说明 |
|---|---|---|
| **MVP 复用底座** | 00·01·02·03·04·05·06 | 作为统一模型、编辑、编码、发布和 3D 内核基础，按新版本 Spec 扩展 |
| **MVP 运行态** | 07·08 的库存、定位、拣货路径部分 | 接 CP6 WMS/模拟器并统一数据来源标识 |
| **MVP 治理** | 09 | 扩展到后台任务、文件、缓存、外部组织和组合数据范围 |
| **P2+** | 08 的设备联动及新增运营能力 | 人员、设备、诊断、优化和仿真按新 Epic 分期 |

---

## 八、复用 vs 新建

| 能力 | CP6 现成 | 怎么用 |
|---|---|---|
| 多租户隔离 | 全表 `TenantId` 体系 | 复用 |
| 跨模块发布 | `IntegrationEvent` + BridgeHook | 复用 DTO/Outbox 基础，由仓库级 Saga 接管成功判定 |
| 库存/库位状态 | WMS 库存模型 | `IWmsStockQuery` 同步只读叠加 |
| 3D 渲染 | 已有 Three.js Viewer、InstancedMesh、LOD、裁剪和标签虚拟化 | 扩展 Scene Manifest/Chunk |
| 空间几何/编码引擎 | 已有运行态层级、编辑和编码 | 保留为运行态，新增设计版本工作区 |
| CAD/Excel 建模 | 无正式解析链 | **新建**隔离文件链和 Worker |
| 外部组织授权 | 通用 RBAC/DataScope 不足 | **新建** Space AccessEvaluator 与多维 Grant |

---

## 九、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 空间数字底座 | **菜鸟空间 3D 可视化** | 仓库 3D 建模、库位编码、实时叠加 |
| 仓库库位建模 | **SAP EWM / 高级仓库的 Storage Bin 结构** | 站点→区→巷→架→位 的层级与编码 |
| Web 3D 渲染 | **Three.js** | 场景/相机/拾取/实例化渲染（大量库位） |

---

## 十、里程碑自检

- [ ] Space 与 WMS 的真相如何切分？LogicalId、库位编码和外部绑定分别做什么？
- [ ] 库位编码由谁生成、谁消费？存量 WMS 客户怎么接入？
- [ ] 库位目录发布为什么用事件、实时叠加为什么用同步只读？依赖方向如何保证单向？
- [ ] CAD+Excel 与地图编辑器如何收敛到同一种模型？
- [ ] 仓库级版本如何在 WMS 验证成功后激活、处理部分状态并重新发布历史版？
- [ ] 真实、模拟和未接入数据如何让用户明确区分？
- [ ] 外部客户、供应商和 3PL 如何按仓库/区域/货主/字段授权？
- [ ] 为什么 Space 要独立命名空间、不塞进 Wms？

全部能答 → Space 3D 需求与架构敲定，可转实施计划（writing-plans）。

---

*初版生成于 2026-06-12；当前产品基线更新于 2026-07-25。配套实现位于 `CP6.Entity/DomainModels/Space`、`CP6.Core/Services/Space`、`cp6.web/src/views/space`。前端 3D = Three.js。*
