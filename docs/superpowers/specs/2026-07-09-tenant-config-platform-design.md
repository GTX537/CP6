# 租户配置基建 + 行业包机制 设计（多租户通用 ERP 地基）

> 2026-07-09 brainstorming 定稿。前篇：`2026-07-08-sales-v2-design-decisions.md`（Sales v2 七项拍板——通用制造 ERP/三层架构/新 Item/并行切换/审批可插拔/配置器留接口/只 spec 不排期）。本文把其中「配置基建」与「行业包」两层落成可施工设计；Sales v2 域模型细节仍归前篇及其后续 spec。

## 0. 目标与非目标

**目标**：同一平台上，每个租户经配置长成自己的业务样子——行业差异（纸器/电源/…）不写死在产品里。配置由实施顾问操作，配置即资产（可导出复制给同行业客户）。

**本轮六项拍板（2026-07-09 用户确认）**：
1. 交付形态=**同一平台+租户级配置**（纯 SaaS，非独立部署）
2. 配置天花板=**字段+表单+流程+报表**；实体级差异走行业包（厂商发版），不开放租户自建实体
3. 配置者=**实施顾问为主**（界面可糙、必须可导出复制）
4. 改造范围=**Sales v2 + Item + 配置基建**；WMS/MES/FIN 只改 Item 引用
5. 单据流=**主干固定+环节裁剪**（主干状态机代码锁死，租户关可选环节/挂审批/配校验）
6. 第二行业（电源）=假想，**抽象保守**：每个机制必须有纸器包这个真实消费者，宁届时加机制不提前造抽象

**非目标（显式不做）**：低代码自建实体、运行时插件装卸、包市场、包间依赖、灰度开关、租户自助配置门户（v2 再评估）、桌面客户端本体（另立项目，见 §4）。

## 1. 配置基建六件套

六个独立窄机制，各有自己的表+服务接口+真实消费者；全部继承 `BaseTenantEntity`（租户隔离与 StampTenant 白得）。**不建统一元数据配置中心**（方案 B 已否决：低代码施工量+类型安全丢失）。

### ① 功能开关 TenantFeature
- 表：`(TenantId, FeatureKey, Enabled, ConfigJson?)`；FeatureKey 分层命名：`pack.paperpack` / `module.sales-v2`
- 服务：`IFeatureGate.IsEnabled(key)`，租户级缓存+变更失效
- 消费者：菜单/路由过滤（接现有 viewModules 机制）、包钩子路由（§2）
- **职责边界**：①只管包/模块级开关；**单据环节裁剪唯一归④ DocFlowConfig**，不在①放 flow.* 键（避免双源）
- 边界：布尔+小 JSON 参数；不做百分比灰度

### ② 编号规则 NumberingRule
- 表：`(TenantId, DocType, Pattern, ResetCycle, NextSeq, RowVersion)`；Pattern 段模板如 `SO-{yyyy}{MM}-{seq:5}`
- 服务：`INumberingService.NextAsync(docType)`——行锁+RowVersion 防并发重号
- 消费者：SalesOrder/Quotation 第一天即走此采番；现 WebOrderNo 采番逻辑迁入为默认规则
- 回退：租户无规则时用单据类型默认格式（存量租户零迁移）
- 校验：Pattern 保存时解析校验，坏模板拒存（不留运行时炸）

### ③ 术语词典（i18n 租户覆盖层）
- 不造新机制：现有 `Sys_Langs` 体系加覆盖表 `(TenantId, LangKey, Lang, OverrideText)`
- 解析顺序：**租户覆盖 → 行业包术语 → 产品默认**（包术语=启用包时种子写入的覆盖行，来源标记区分，停用包可清）
- 前端发布快照机制照用，快照按租户出
- 消费者：Item 显示名（製品/品目/物料）为首个门面用例

### ④ 单据流裁剪 DocFlowConfig
- 表：每单据类型一行 `(TenantId, DocType, DisabledSteps[], ApprovalBindings[], GuardConfigs[])`
- **主干状态机是代码**：枚举+显式迁移表，编译期锁死；MRP/财务/WMS 只消费主干状态，裁剪对下游不可见
- 裁剪只答三问：可选环节开不开（如整体跳过见积）／哪个迁移点挂 WFS 审批（FlowKey 绑定，复用审批适配器 IApprovalGateway）／迁移前跑哪些注册校验器
- 另：租户可自定义子状态**标签**（仅展示，不进状态机）
- SalesOrder 主干（v1）：`Draft→Confirmed→InFulfillment→Shipped→Invoiced→Closed`（+`Cancelled`）；信用超限审批=第一个 ApprovalBinding 用例

### ⑤ 字段扩展 = SFS 绑定
- 表：`EntityFormBinding (TenantId, EntityType, SfsFormId, Placement)`
- 核心实体详情页尾部渲染 SFS 表单区块；数据存 SFS 答案表，**不回写核心表**
- **硬边界（承重墙）**：核心业务计算（MRP/定价/财务）不消费扩展字段。需要进计算的字段=行业包强类型扩展表的职责，不是 SFS 的
- 依赖注记：SFS 深化（布局/报表，spec 已在 main=51298f6）优先级因此上升

### ⑥ 配置导出 ConfigBundle
- 服务：①~⑤ + SFS 表单定义 + WFS 流程定义 → 带版本号 JSON 包；导入=键冲突 dry-run 报告 → 确认应用
- 「纸器模板包」= 纸器租户迁移完成时导出的第一个 Bundle（交付物）

## 2. 行业包机制（Industry Pack）

**包=编译进产品的代码资产，随产品发版；租户开关决定亮不亮。** 不做运行时插件（电源是假想，机制保守）。

```
CP6.Packs.PaperPack/                 ← .NET 侧一包一项目
  Entities/  OrderPaperExt.cs, ItemPaperExt.cs   ← 强类型扩展表，FK 1:1 挂核心实体
             （版型NO/抜型/巻方向/紙質…= PA070 63 项目分拣所得）
  Services/  PaperPricingHook.cs     ← 实现核心钩子接口
  Seeds/     terminology.json / features.json / menus.json
cp6.web/src/packs/paperpack/         ← 前端包页面/扩展区块组件
```

**核心层钩子（v1 恰三个，均有纸器包真实消费者）**：
1. `IPricingHook` — 取价后行业加工（纸器平米单价换算迁入）
2. `IDocExtensionProvider` — 单据详情页扩展区块注册
3. `IItemValidationHook` — Item 保存行业校验

钩子按租户启用的包路由；未启用租户零开销直路。**不做**：包间依赖、独立版本升级、运行时装卸。

**启用流程**（平台管理后台，顾问操作）：选包 → dry-run 种子清单 → 确认 → 开关+种子落库 → 菜单/术语/扩展区块生效。停用只关开关不删数据。

**纸器包验收标准**：现纸器租户迁到「通用核心+纸器包」后业务能力与老 /erp 等价——包机制唯一裁判。

## 3. 与 Sales v2 / Item 的接缝

配置基建**先行一步落地，Sales v2 每个实体出生即消费**（避免先写死再配置化的返工）：

| 机制 | Sales v2/Item 消费点 |
|---|---|
| ① | `module.sales-v2` 新老并行入口 |
| ② | SalesOrder/Quotation 采番；WebOrderNo 逻辑为默认规则 |
| ③ | Item 显示名随包/租户变化 |
| ④ | SalesOrder 主干+信用审批绑定；Quotation 可整环节关闭 |
| ⑤ | Item/Order 详情页扩展区块 |
| ⑥ | 纸器模板包=迁移交付物 |

Item 主数据照前篇：通用窄表 + PaperPack 扩展表，MRP/WMS/MES 改指 Item。

## 4. API-first 契约约束（B/S 与 C/S 共存）

用户决策（2026-07-09）：主体 B/S 不变；桌面客户端（C/S，练习+现场场景如 WMS 扫码工作站/MES 报工终端）**另立独立项目**，本设计只锁三条服务端契约：

1. **认证双模**：API 同时支持 cookie+CSRF（浏览器）与纯 Bearer（桌面/第三方）；登录/刷新/登出三链路的无 cookie 模式有集成测试锁定。
2. **OpenAPI 纪律**：Sales v2 起控制器齐 XML 注释+ProducesResponseType；swagger.json 入 CI 做破坏性变更校验；桌面端由此生成强类型客户端。
3. **SFS 边界**：动态表单只承诺浏览器渲染；桌面端只消费固定画面 API；扩展字段有独立只读 JSON 接口。

业务逻辑永不下沉客户端——权限/开关/审计全在服务端，对所有消费者一视同仁。

## 5. 错误处理与测试策略

- 错误码：BizException + i18n 码机制，新段 `E-CONF-xxx`（配置基建）/`E-SALES-xxx`（Sales v2）登记总纲；配置类错误一律**保存时校验拒绝**
- 测试三层：
  1. 六件套各自单测（采番并发重号/词典三层解析顺序/裁剪门/Bundle 冲突检测）
  2. 包机制集成测试（启用纸器包→种子齐→钩子路由→未启用租户零影响）
  3. 端到端金线：开新租户→启用纸器包→建 Item→报价→转单；及裁剪变体（跳见积租户直接下单）
- 横切 DoD 照 `docs/00-横切接线规范.md`（权限键/审计/i18n 五语/事件桥）

## 6. 施工顺序建议（不排期，供 plan 阶段用）

1. 配置基建六件套（可六路并行，互不依赖）
2. 行业包机制骨架 + 纸器包空壳（钩子接通）
3. Item 通用化 + PaperPack 扩展表（63 项目分拣表在此产出）
4. Sales v2 域模型/流程（消费全部基建）
5. 纸器租户迁移 + 模板包导出 + 老模块退役

前置关系：M-ERP 修复波（权限/测试还账）仍在本改造前；F1 财务油路以 Sales v2 为一等公民设计。

## 7. 开放问题（写 plan 前需答）

- Quotation→SalesOrder 转换的行级血缘存储形态（前篇推荐值 #1 细化）
- 定价引擎价格表结构与 SheetUnitPrice/ProductLotPrice 的归置（前篇推荐值 #2）
- 信用占用的计算口径（AR 余额+未出货订单——含税否/多币种折算基准）
- 词典覆盖与已发布 i18n 快照的失效联动（按租户重发布的触发时机）
- 纸器 63 项目分拣表（核心/包/SFS 三桶）——需逐字段过一遍 PA070 实表
