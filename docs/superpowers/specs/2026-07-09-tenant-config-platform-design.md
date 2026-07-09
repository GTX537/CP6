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
- 服务：`IFeatureGate.IsEnabled(key)`
- **缓存口径（多实例）**：复用现有 `IDistributedCache`（Program.cs:55 Redis / :64 内存回退，与登出黑名单同款基建）+ 进程内 60s TTL 前置层；开关变更删分布式缓存键，各实例最迟 60s 收敛——**接受 ≤60s 最终一致**，对新老并行切换场景可感知但可容忍，写入运维须知
- 消费者：菜单/路由过滤（接现有 viewModules 机制）、包钩子路由（§2）
- **职责边界**：①只管包/模块级开关；**单据环节裁剪唯一归④ DocFlowConfig**，不在①放 flow.* 键（避免双源）
- 边界：布尔+小 JSON 参数；不做百分比灰度

### ② 编号规则 NumberingRule
- 表：`(TenantId, DocType, Pattern, ResetCycle, NextSeq, RowVersion)`；Pattern 段模板如 `SO-{yyyy}{MM}-{seq:5}`。`RowVersion` **仅用于管理页编辑**（顾问改规则的乐观并发），取号路径不碰它
- 服务：`INumberingService.NextAsync(docType)`——**悲观行锁（UPDLOCK）单独短事务取号**：锁行→（同锁内判定 ResetCycle 跨周期则先重置 NextSeq）→+1→提交。不用乐观重试（高并发下单=重试风暴）
- **事务边界**：取号事务独立于业务落库事务（否则锁持续到下单提交、全租户下单串行化）。代价=业务回滚产生跳号
- **跳号明文口径**：跳号是正确行为，不回收（回收必重号）；审计场景以单据表为准而非号段连续性。此条写入运维/验收文档，免上线后被当 bug 报
- 消费者：SalesOrder/Quotation 第一天即走此采番；现 WebOrderNo 采番逻辑迁入为默认规则
- 回退：租户无规则时用单据类型默认格式（存量租户零迁移）
- 校验：Pattern 保存时解析校验，坏模板拒存（不留运行时炸）

### ③ 术语词典（i18n 租户覆盖层）
- 不造新机制：现有 `Sys_Langs` 体系加覆盖表 `(TenantId, LangKey, Lang, Source, OverrideText)`
- **唯一键=(TenantId, LangKey, Lang, Source)**，`Source ∈ {Tenant, Pack}`——同键允许租户手工行与包种子行并存，解析按 Source 优先级取；停用包只清 `Source=Pack` 行，**顾问手工覆盖不受株连**
- 解析顺序：**租户覆盖(Tenant) → 行业包术语(Pack) → 产品默认**
- 前端发布快照机制照用，快照按租户出
- 消费者：Item 显示名（製品/品目/物料）为首个门面用例

### ④ 单据流裁剪 DocFlowConfig
- 表：每单据类型一行 `(TenantId, DocType, DisabledSteps[], ApprovalPoints[], GuardConfigs[])`
- **主干状态机是代码**：枚举+显式迁移表，编译期锁死；MRP/财务/WMS 只消费主干状态，裁剪对下游不可见

**裁剪的语义模型（三条口径，缺一不可施工）：**
1. **可裁白名单在代码侧**：每个 DocType 的状态机声明 `OptionalSteps`（可裁环节白名单，如 Quotation 整环节、Confirmed 前的内部审核步）；`DisabledSteps` 值域=该白名单，**越界配置保存时拒绝（E-CONF）**。主干必经状态（如 Shipped）不可裁，配置层无法表达"关掉 Shipped"。
2. **缝合边是预定义备选边，不是运行时推导**：可裁环节被关时启用的旁路迁移（如关 Confirmed 后的 `Draft→InFulfillment`）**在代码迁移表里预先声明为备选边**，配置只是在主边/备选边之间选择。运行时不做图推导——推导=状态机不再编译期锁死，违反本节第一句。
3. **配置变更只作用于此后的迁移动作（在途单据口径）**：disable 只拦"进入"被关环节，不拦"离开"（停在被关状态的在途单据可正常走出去）；审批点解绑不影响已发起的审批实例（WFS 侧自然走完，终态回调照旧生效）；新配置从下一次迁移动作起约束。与 WFS 版本 pin 同款思想：变更不动在途。

**审批挂点（与审批解耦 spec 单源收敛）：**
- `ApprovalPoints[]` **只存"哪个迁移点需要审批"**=（迁移点 → BizType）映射，如 `Confirmed 迁入 → BizType="SALES_ORDER_CONFIRM"`
- 流程选择、条件规则（金额>10万走高额流程 ConditionJson）、DetailRoute、管理页签**全部走既有 `Wf_ApprovalBinding`**（审批解耦 spec 2026-07-07 契约）；提交入口=`IApprovalService.SubmitAsync(bizType,…)`，终态回调=`IApprovalCallback`
- **`IApprovalGateway` 作废**（2026-07-08 纪要中该名即指 IApprovalService，本文起统一用后者；纪要已加勘误注）。同一"单据→流程"绑定关系全系统只有 Wf_ApprovalBinding 一张表

**校验器（GuardConfigs）：**
- 值域=代码注册的校验器键（含行业包注册的）；保存时校验键存在
- **运行时 fail-closed**：迁移时遇悬空键（如包停用后其校验器键失效）**拦截迁移并报 E-CONF**，不静默跳过——与①包开关联动：停用包前 dry-run 列出受影响的 GuardConfigs
- 另：租户可自定义子状态**标签**（仅展示，不进状态机）
- SalesOrder 主干（v1）：`Draft→Confirmed→InFulfillment→Shipped→Invoiced→Closed`（+`Cancelled`）；信用超限审批=第一个 ApprovalPoints+Wf_ApprovalBinding 用例

### ⑤ 字段扩展 = SFS 绑定
- 表：`EntityFormBinding (TenantId, EntityType, SfsFormId, Placement)`
- 核心实体详情页尾部渲染 SFS 表单区块；数据存 SFS 答案表，**不回写核心表**
- **硬边界（承重墙）**：核心业务计算（MRP/定价/财务）不消费扩展字段。需要进计算的字段=行业包强类型扩展表的职责，不是 SFS 的
- 依赖注记：SFS 深化（布局/报表，spec 已在 main=51298f6）优先级因此上升

### ⑥ 配置导出 ConfigBundle
- 服务：①~⑤ + SFS 表单定义 + WFS 流程定义 → JSON 包；包头记**产品 schema 版本**，不兼容版本导入直接拒绝
- **引用可平移性三分类（dry-run 报告按此分节，第三类逐条点名）**：
  1. **可平移**：词典/编号规则/开关/环节裁剪——纯配置值，导入即用
  2. **需重映射**：SFS 表单 GUID 及其在 EntityFormBinding/GuardConfigs 中的引用——导入时生成新 ID 并全包内重写引用
  3. **不可平移（导入后人工重配）**：WFS 流程内的审批人策略（Specified 指定人/Role/部门 ID）、DataMap 等**租户内主数据引用**——目标租户不存在这些 ID，导入时置为待配置态，dry-run 报告逐条列出清单供顾问导入后重配
- 「纸器模板包」= 纸器租户迁移完成时导出的第一个 Bundle（交付物）；其成色以三分类报告的第三类清单长度衡量，模板制作时应尽量少用指定人/硬 ID 策略

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
| ④ | SalesOrder 主干+信用审批（ApprovalPoints→Wf_ApprovalBinding→IApprovalService）；Quotation 可整环节关闭（备选边 Draft→InFulfillment 预声明） |
| ⑤ | Item/Order 详情页扩展区块 |
| ⑥ | 纸器模板包=迁移交付物 |

Item 主数据照前篇：通用窄表 + PaperPack 扩展表，MRP/WMS/MES 改指 Item。

## 4. API-first 契约约束（B/S 与 C/S 共存）

用户决策（2026-07-09）：主体 B/S 不变；桌面客户端（C/S，练习+现场场景如 WMS 扫码工作站/MES 报工终端）**另立独立项目**，本设计只锁三条服务端契约：

1. **认证双模**：API 同时支持 cookie+CSRF（浏览器）与纯 Bearer（桌面/第三方）。**双模的服务端实现（含 refresh token 存储/旋转/撤销与寿命策略）随本改造落地，是服务端本体工作，不等桌面项目倒逼**；登录/刷新/登出三链路的无 cookie 集成测试即其 DoD。
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

1. 配置基建六件套——**①②③⑤先行**（互不依赖可并行）；**④待审批解耦接缝对齐后开工**（ApprovalPoints→Wf_ApprovalBinding 契约）；**⑥收尾**（导出面依赖①~⑤表结构定型）
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
- 在途单据受配置变更影响的逐 DocType 边界盘点（§1④口径3 是总原则；每种单据的被关状态×审批解绑×校验器变化的组合边界，写 plan 前逐一枚举）
