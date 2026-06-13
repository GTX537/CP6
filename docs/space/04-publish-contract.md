# Space 04 · 库位发布与 WMS 集成契约 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-04 库位发布与 WMS 集成契约 |
| 所属模块 | Space 空间数字底座 · Part 1（P1） |
| 里程碑 | **P1 收尾**（让 Space 产的库位编码"过冻结闸门 → 推给 WMS 建立/关联库位"，P1 完成标志） |
| 技术栈 | .NET8 + EF Core；**复用 CP6 既有集成基建** `T_IntegrationEvent` + `BridgeHookBase` + `IntegrationEventDispatcher` + 重试 Worker / 死信 |
| 命名空间 | `CP6.Core/Services/Space/LocationPublishService.cs`、`CP6.Core/Services/Integration`（新增 SPACE→WMS 路由 + 载荷） |
| 落地决策 | D4 发布即冻结（**冻编码不冻几何**·纯几何编辑不发布）/ D6 停用前置 0 库存校验 + **TOCTOU WMS 侧再检查** / 变长路径（跳 Aisle）/ 单向低耦合（契约在消费者侧） |
| 依赖 | [00 数据模型](./00-data-model.md)（Status 状态机 §7.2、Version 按 LocationId 递增 §7.2、§6.2 发布触发表、CodeOrigin 对账）、[03 编码引擎](./03-code-engine.md)（`code-precheck` 闸门）、[07](./07-stock-overlay.md)（`IWmsStockQuery` 停用前查库存） |

> **题眼**：03 把库位编码生产好（草稿、可重排），本章是**把编码"冻结并发布"给 WMS** 的唯一通道。三件事定生死：① **发布 = 冻结**（Status 0→1，`LocationCode` 此后终生不改，是发给 WMS 的 join key；D4），② **批量 upsert + 幂等**（复用 CP6 `T_IntegrationEvent` 事件，按 `LocationId + Version` 让 WMS 幂等消费，至少一次投递安全），③ **停用要先问 WMS 有没有库存**（D6：Space 发停用前用 `IWmsStockQuery` 查 0 库存，WMS 消费时再查一次兜 TOCTOU）。**记住一句**：Space 发"库位目录主数据"（编码 + 变长层级路径 + 属性），**绝不发几何**（WMS 没有几何，混合分权）；纯几何编辑（货架挪位/旋转）根本不触发发布——join key 永不漂移。**冻编码不冻几何**：发布后码冻死，货架仍可在 02 随便挪。

---

## 目录
- 第1章 功能概述与定位（与 03/07/WMS 的边界）
- 第2章 接入 CP6 既有集成基建（不另造总线）
- 第3章 LocationPublished 事件载荷（变长路径 + 属性 + Version + Op）
- 第4章 发布触发点（发布 / 不发布 —— D4 表的兑现）
- 第5章 WMS 侧消费：批量 upsert + 幂等（按 LocationId + Version）
- 第6章 停用与库存冲突（D6：前置 0 库存校验 + TOCTOU WMS 侧兜底）
- 第7章 删除巷道 / 货架与已发布库位（下游待办①：Restrict / re-publish）
- 第8章 存量采纳对账（CodeOrigin=2 导入 + reconcile）
- 第9章 发布前闸门（接 03 code-precheck）
- 第10章 API 接口设计
- 第11章 消息一览
- 第12章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：把 Space 自有的库位目录（编码 + 层级路径 + 属性）单向、低耦合、幂等地发布给 WMS，让 WMS 建立/关联库位（Bin）；并管好"发布即冻结、停用要校验库存、存量编码采纳对账"三类边界动作。

**本章范围（04）：**
- 发布动作：把 `Status=0 草稿` 且编码就绪的库位翻 `Status=1 已发布`，并发 `LocationPublished` 事件。
- 事件载荷规范：变长层级路径（跳 Aisle）+ 库位属性 + `LocationId` + `Version` + 操作类型 `Op`。
- 接入 CP6 既有 `T_IntegrationEvent` 持久化/重试/死信基建（不新造消息总线）。
- WMS 侧幂等消费契约（按 `LocationId + Version` 批量 upsert）。
- 停用（Status→2）的 D6 前置库存校验 + TOCTOU 兜底。
- 删除巷道/货架触及已发布库位的处理（待办①）。
- 存量客户采纳导入（CodeOrigin=2）+ 对账。
- 发布前闸门（消费 03 的 `code-precheck`）。

**不含（划清边界）：**
| 能力 | 去哪章 |
|---|---|
| 几何建模 / 精修 | [01](./01-editor-template.md)/[02](./02-free-layout.md) |
| 编码生成 / 重排 / 规则 | [03 章](./03-code-engine.md) |
| **实时库存叠加查询 `IWmsStockQuery` 的完整契约/快照/轮询** | [07 章](./07-stock-overlay.md)（本章只用其"查某库位库存量"做停用校验） |
| WMS 内部 Bin 表结构 / 库存业务 | WMS 模块（Space 不涉） |

> **单向低耦合（沿用采购模块手法）**：Space 编译期**不依赖 WMS 实现**。发布走 `IntegrationEvent`（契约 = 事件载荷，定义在 Space/Integration 侧）；WMS 实现消费端 hook。停用校验走 `IWmsStockQuery`（接口定义在 Space 侧，WMS 实现），同步只读。依赖方向恒为 Space→WMS 的**抽象**，无反向编译依赖。

---

## 第2章 接入 CP6 既有集成基建（不另造总线）

CP6 已有成熟的跨模块事件基建（Phase 6），04 **直接复用**，不发明新机制：

| 既有件 | 复用方式 |
|---|---|
| `T_IntegrationEvent`（持久化事件，三段式 Pending→Success/Skipped/Failed→Dead/Compensated） | Space 发布写一条事件记录 |
| `BridgeHookBase.PersistEventAsync(...)` | Space 发布 hook 继承它，落 IntegrationEvent + 设重试 |
| `IntegrationEventDispatcher`（按 `SourceModule|TargetModule|HookName` 反射路由） | **新增路由** `SPACE|WMS|OnLocationPublishedAsync` |
| 重试 Worker（每 60s 扫 Failed + 退避）+ 死信 + `IDeadLetterNotifier`（SignalR + Sys_OperLog） | 发布失败自动重试/告警，免重复造 |
| `CorrelationId` 端到端 trace | 一次发布批共享一个 CorrelationId |

### 2.1 发布 hook（新增）
```csharp
// CP6.Core/Services/Integration/SpaceBridgeHook.cs（继承 BridgeHookBase）
// 也可挂到既有 IWmsBridgeHook 上，新增 OnLocationPublishedAsync
public async Task<BridgeResult> OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId)
{
    // 1. 调 WMS 消费（IWms… 抽象，WMS 实现 Bin upsert）
    // 2. 末尾 PersistEventAsync("SPACE","WMS",nameof(OnLocationPublishedAsync),
    //        sourceNo: batch.BatchNo, targetNo: null, status, error, correlationId, batch)
}
```
- `SourceModule="SPACE"`（≤10 字符，符合 `T_IntegrationEvent` 约束）。
- `SourceNo` = **发布批号**（采番生成，如 `LPUB-20260613-0001`，≤30 字符）；不放裸 GUID（GUID 超 30 字符限制），库位明细在 `PayloadJson`。
- 幂等命中（WMS 侧 Version 已是最新）→ 状态 `Skipped`（终态不重试，符合既有语义）。

> **已知基建缺口（商用化待补，不阻断本章）**：`BridgeHookBase` 硬编码 `Creator="system"`，集成事件无操作用户上下文。Space 发布是**有人触发的主数据动作**，商用化需让发布人可溯源——建议在 payload 内带 `PublishedBy`，或后续给 BridgeHookBase 补用户上下文（见项目记忆 obs 917）。

### 2.2 为什么发布用事件、不用同步
- 库位目录是**低频主数据**（建仓时批量发一次，之后偶尔增删），非事务热流——用持久化事件 + 自动重试，发布方不被 WMS 可用性绑死（沿用 00/README §六决策）。
- 与"实时库存叠加用同步只读"（07）刚好相反：那是高频读、要即时；这是低频写、要可靠投递。

---

## 第3章 LocationPublished 事件载荷

### 3.1 批载荷结构
```jsonc
// PayloadJson（一次发布批，N 个库位）
{
  "batchNo": "LPUB-20260613-0001",
  "tenantId": "....",
  "publishedBy": "zhangsan",         // 发布人（补溯源，§2.1）
  "items": [
    {
      "op": "UPSERT",                 // UPSERT 建立/更新 ｜ DEACTIVATE 停用（第6章）
      "locationId": "GUID",           // ★稳定主键，终生不变（00 §7.1）
      "locationCode": "A-03-02-05",   // ★join key，发布后冻结
      "codeOrigin": 1,                // 1 引擎生成 / 2 采纳（对账，第8章）
      "version": 7,                   // ★按 LocationId 递增（00 §7.2），WMS 幂等判据
      "path": {                       // ★变长层级路径（跳 Aisle，第3.2）
        "siteCode": "WH1", "floorLevel": 1, "zoneCode": "A",
        "aisleCode": null,            // 无巷道库区为 null（变长）
        "rackCode": "R03", "col": 2, "level": 2, "depth": 1
      },
      "attrs": { "sizeW":1200, "sizeH":1500, "sizeD":1000 }  // 纯属性，★无绝对坐标几何
    }
  ]
}
```

### 3.2 变长路径（跳 Aisle）
- `path.aisleCode` 在无巷道库区为 `null`（00：`Rack.AisleId` 可空）。WMS **按 payload 给定的 path 消费**，不自己重建层级——变长由 Space 决定、WMS 照单全收。
- WMS 用 `locationCode` 作主关联键（唯一），`path` 是**辅助层级元数据**（供 WMS 做区/架维度统计、上架策略），不参与 join。
- **关键**：path 里**绝不含绝对坐标/几何**（AbsXYZ、RotationZ 都不发）。WMS 无几何真相（混合分权）。`attrs` 只发与库存业务相关的属性（如格口尺寸，用于容量判断），不发渲染几何。

### 3.3 Version 的作用
- `version` = 00 §7.2 定义的"按 `LocationId` 递增的发布版本号"，每次发布/停用 +1。
- WMS 存每个 `locationId` 的 `lastVersion`；消费时 `incoming.version <= stored.lastVersion` → 幂等跳过（第5章）。这让事件**至少一次投递**安全（Worker 重试/重复不会回退状态）。

---

## 第4章 发布触发点（D4 表的兑现）

严格执行 00 §6.2 的"是否发布"表：**只有改变库位目录主数据的动作才发布，纯几何不发布。**

| 动作 | Status 变化 | 发布 LocationPublished？ | Op |
|---|---|---|---|
| 草稿库位首次发布（编码就绪、过闸门） | 0→1 | **是** | UPSERT |
| 已发布库位编码变更 | — | **不可能**（已发布码冻结，改→E-SPACE-004） | — |
| 货架移动 / 旋转 / 改尺寸（纯几何） | 不变 | **否**（载荷无几何，join key 不变） | — |
| 货架增格子（Cols/Levels/Depth↑）→ 新库位 | 新库位 0→1（随下次发布） | **是**（新库位 UPSERT） | UPSERT |
| 货架减格子 → 删库位（已发布的） | 1→2 停用 | **是**（第6章 DEACTIVATE） | DEACTIVATE |
| 采纳态绑定货架（D7：未放置→已放置） | 不变（早已 Status=1） | **否**（几何回填不改 join key，00 §6.2） | — |
| 库区/巷道多边形调整 | 不变 | **否**（不影响库位坐标） | — |
| 库位停用（人工/减格） | 1→2 | **是**（先过 D6 校验，第6章） | DEACTIVATE |

> **这张表就是 D4 的全部**：现实里货架挪位极常见，几何必须可动；WMS 那边压根没几何，所以纯几何编辑不需要同步——**冻编码不冻几何**，join key 自然永不漂移。发布只发"库位存在性 + 编码 + 路径 + 业务属性"的增删停用。

---

## 第5章 WMS 侧消费：批量 upsert + 幂等

WMS 实现消费 hook（Space 只依赖其抽象）。契约：

### 5.1 幂等 upsert 算法（WMS 侧）
```
for item in batch.items:
  bin = WmsBin.find(tenantId, locationId=item.locationId)   // 按稳定主键找，非按 code
  if bin == null:
     if item.op == UPSERT:    create Bin{ locationId, code=item.locationCode, path, attrs, lastVersion=item.version }
     if item.op == DEACTIVATE: skip（无此 bin，幂等无操作）
  else:
     if item.version <= bin.lastVersion:  SKIP（陈旧/重复事件，幂等）   ← 关键
     else:
        if item.op == UPSERT:    update bin（code 理论不变；path/attrs 更新）; bin.lastVersion=item.version
        if item.op == DEACTIVATE: 见第6章（再校验库存）→ 停用 or 拒绝
```
- **按 `locationId`（稳定主键）关联，不按 `code`**：code 虽冻结，但 locationId 才是终生不变的身份（00 §7.1），避免任何编码理解歧义。
- `version` 单调判据让重复投递、乱序到达都安全收敛到最新态。

### 5.2 整批事务与部分失败
- WMS 侧建议整批事务；若部分 item 失败（如 DEACTIVATE 被库存拦），返回**逐项结果**，整事件按"有失败项"置 `Failed`（Worker 重试）或 `Skipped`（业务规则跳过，附逐项原因）。
- Space 侧据返回更新本地：成功项 Status 落定，失败项（如停用被拒）保持原态 + 提示（第6章）。

---

## 第6章 停用与库存冲突（D6）

停用（`Status 1→2`）是**唯一需要反向问 WMS** 的发布动作——因为 Space 不持库存真相，停用一个还有货的库位会制造业务事故。

### 6.1 D6 双重校验
```
① Space 侧前置校验（发停用事件前）：
   qty = IWmsStockQuery.GetStock(locationCode)        // 同步只读（07 契约）
   if qty > 0:  阻断，E-SPACE-401「库位仍有库存，不能停用」，不发事件
   else:        发 DEACTIVATE 事件

② WMS 侧消费再校验（TOCTOU 兜底）：
   收到 DEACTIVATE → 再查一次该 bin 实时库存
   if 库存 > 0:  拒绝停用，逐项结果返回 rejected「库存非0」→ 事件 Skipped + 原因
                 Space 收到后回滚本地 Status→1，提示用户
   else:        执行停用，lastVersion 更新
```
- **为什么要两道**：①是用户体验（建模端即时挡住明显错误）；②是正确性兜底——Space 查询与 WMS 应用之间存在**时间窗（TOCTOU）**，期间可能有入库。**以 WMS 侧再校验为权威**（库存真相在 WMS），Space 侧校验只是前置友好提示（D6 决策原文：停用 TOCTOU 兜底＝WMS 侧再检查）。

### 6.2 状态机一致性
- 停用成功：Space `Status→2`、`Version+1`，WMS bin 标停用。停用码仍冻结（00 §7.2：停用态编码冻结）。
- 停用被拒（库存非 0）：Space `Status` 保持 `1`，**不**前进到 2；提示"WMS 侧仍有库存，停用未生效"。

---

## 第7章 删除巷道 / 货架与已发布库位（下游待办①）

> 00 章遗留待办①：**删巷道（或货架）若其下有已发布库位，怎么办**——直接删会让 WMS 那边已发布库位的 path 元数据陈旧（aisleCode 指向已不存在的巷道）。本章定规则。

### 7.1 删除护栏（默认 Restrict）
| 删除对象 | 其下有"已发布(Status≥1)"库位 | 处理 |
|---|---|---|
| 删巷道 Aisle | 有 | **默认 Restrict**：阻断删除 E-SPACE-402，提示"该巷道下有 N 个已发布库位" |
| 删货架 Rack | 有 | **默认 Restrict**：阻断 E-SPACE-403（已发布库位须先停用，走第6章 DEACTIVATE） |
| 删巷道/货架 | 其下全是草稿(Status=0) | 允许（草稿无对外契约，连带删库位即可，00 删除策略） |

### 7.2 两条放行路径（替代硬阻断）
当用户确实要删（如仓库改造）：
| 路径 | 动作 | 适用 |
|---|---|---|
| **A. 先停用再删** | 对其下已发布库位逐个走第6章 DEACTIVATE（过库存校验）→ 全停用后允许删 | 库位真的要废弃 |
| **B. re-publish 改挂** | 把这些库位**改挂到新巷道/货架**（几何回填，code 不变），发 UPSERT 事件**只更新 path 元数据**（aisleCode 变/变 null），`Version+1`；几何调整本不发布，但**path 元数据变更属目录主数据**，须 re-publish 让 WMS 路径不陈旧 | 库位还在用、只是巷道重命名/重组 |

> **B 是待办①的精髓**：`LocationCode`（join key）**全程不变、不冻结失效**——re-publish 只刷新 WMS 侧的辅助 path 元数据（区/巷/架归属），不动 join key。这区别于 D4"纯几何编辑不发布"：纯几何（坐标）确实不发，但**层级归属（path）变更要发**，因为 path 是发布载荷的一部分、WMS 拿它做区架统计。default Restrict 保护"误删"，路径 A/B 给"有意改造"留正规出口。

---

## 第8章 存量采纳对账（CodeOrigin=2）

存量 WMS 客户已有库位编码，不强制重编——**采纳导入** + 对账（00 §7、03 §9.1、D7）。

### 8.1 采纳导入（落库即"已发布·未放置"）
```
POST /api/space/location/adopt   { items:[{locationCode, attrs?}] }
  → 每条建 Space_Location{ Status=1, Placed=false, CodeOrigin=2, RackId=null, 无几何 }
  → 编码冲突（与既有非空码撞）→ E-SPACE-008（00 章，跳过该条，报告）
  → 不发 LocationPublished（码本就来自 WMS，无需回发；仅在 Space 建影子目录待绑几何）
```
- 采纳进来的库位**不回发 WMS**（WMS 已有），只在 Space 侧登记为"有码无几何"，进编辑器"待绑定列表"（00 §9 `/location/unplaced`）等反向建模补几何（01 §8/02）。

### 8.2 对账（reconcile）
比对 Space 采纳目录 vs WMS 既有库位，给差异清单：
| 差异 | 含义 | 处理 |
|---|---|---|
| WMS 有 / Space 无 | 漏采纳 | 补 adopt 导入 |
| Space 有 / WMS 无 | 采纳了 WMS 已删的码 | 标记、人工确认是否停用 |
| 两边都有、属性不一致 | 尺寸等元数据偏差 | 以约定方为准（默认 WMS 库存属性为准、Space 几何为准） |
- `CodeOrigin` 是对账的来源标签：`2` 采纳的码，对账以 WMS 为编码真相源；`1` 生成的码，Space 为编码真相源。

---

## 第9章 发布前闸门（接 03 code-precheck）

发布是不可逆冻结，前面必须过闸门（消费 03 §9.2 的 `code-precheck`）：
```
发布 floor/zone 前：
  GET /api/space/floor/{id}/code-precheck   （03 章）
  闸门条件（全满足才放行）：
    emptyCodeCount == 0          // 无空码草稿
    duplicateGroups == []        // 无重复码
    precheckErrors == []         // 规则完备（租户全局唯一，03 §6）
  任一不满足 → E-SPACE-307 阻断发布，引导回 03 补码/重排
  （可选）越界/重叠提示（02 §8）汇总展示，但不阻断（草稿瑕疵不挡发布，几何问题不影响 join key）
```
- 闸门通过 → 执行发布：作用域内 `Status=0 且编码就绪` 的库位 `Status→1`、`Version+1`、生成发布批 → 发 `LocationPublished`。
- **发布即冻结**：自此这些库位 `LocationCode` 改动一律 E-SPACE-004（00 §6）；几何仍可改（D4）。

---

## 第10章 API 接口设计

路由前缀 `/api/space`。

| 端点 | 方法 | 说明 |
|---|---|---|
| `/floor/{id}/publish` | POST | 发布整层（或 `?zoneId=` 按库区）：过闸门(第9) → Status→1 + 发 LocationPublished（第3/4） |
| `/floor/{id}/code-precheck` | GET | 发布前闸门（03 §9.2 提供，本章消费） |
| `/location/{id}/deactivate` | POST | 停用单库位：D6 双重校验（第6） → DEACTIVATE 事件 |
| `/location/adopt` | POST | 存量采纳导入（第8.1，CodeOrigin=2，不回发） |
| `/reconcile?floorId=` | GET | 采纳对账差异清单（第8.2） |
| `/aisle/{id}` `/rack/{id}` | DELETE | 删除护栏（第7：默认 Restrict；带 `?mode=deactivate｜rehome` 走路径 A/B） |
| `/publish/events?floorId=` | GET | 发布事件追踪（查 T_IntegrationEvent，状态/重试/死信，运维用） |

> 发布相关写操作都接 PUB 功能权限（发布/停用/采纳/删除高危，需授权）；`/publish/events` 接数据权限。

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-401 | Error | 库位仍有库存，不能停用 | D6 前置校验 qty>0（第6.1①） |
| E-SPACE-402 | Error | 该巷道下有 N 个已发布库位，不能直接删除 | 删 Aisle 触发 Restrict（第7.1） |
| E-SPACE-403 | Error | 该货架下有已发布库位，请先停用 | 删 Rack 触发 Restrict（第7.1） |
| E-SPACE-307 | Error | 存在空码或重复码，无法发布 | 发布闸门不过（第9，03 §9.2） |
| E-SPACE-004 | Error | 已发布库位编码不可修改 | 改 Status≥1 的 LocationCode（00 §6 复用） |
| E-SPACE-008 | Error | 采纳编码已存在，不能重复导入 | adopt 与既有非空码冲突（00 章复用） |
| W-SPACE-404 | Warn | 停用未生效：WMS 侧仍有库存 | TOCTOU 兜底，WMS 消费时拒绝（第6.1②） |
| I-SPACE-401 | Info | 已发布 N 个库位（批号 LPUB-…） | 发布成功 |
| I-SPACE-402 | Info | 已改挂 N 个库位并刷新路径 | re-publish 路径 B（第7.2） |
| E-SPACE-009 | Error | 数据已被他人修改，请刷新重试 | 发布/停用 RowVersion 冲突（00 章复用） |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 00 数据模型 | Status §7.2 状态机翻转、Version 按 LocationId 递增、§6.2 发布触发表、CodeOrigin 对账、删除策略 |
| ← 03 编码引擎 | 发布前调 `code-precheck` 当闸门；只发布编码就绪（非空·唯一）的库位 |
| → 07 实时叠加 | 复用 `IWmsStockQuery` 做停用前置库存校验（D6）；07 定义完整查询契约，本章只用"查某码库存量" |
| → WMS（经事件） | 发 `LocationPublished`（SPACE→WMS）；WMS 实现幂等 upsert（按 LocationId+Version）；单向，Space 不依赖 WMS 实现 |
| ↺ CP6 集成基建 | 复用 `T_IntegrationEvent` + `BridgeHookBase` + `IntegrationEventDispatcher`（新增 SPACE\|WMS 路由）+ 重试 Worker/死信/`IDeadLetterNotifier` |
| → PUB 权限 | 发布/停用/采纳/删除接功能权限；事件追踪接数据权限 |
| 多租户 | 发布批、事件、采纳目录全带 TenantId；payload 内 tenantId 供 WMS 隔离 |

---

## 自检
- [ ] 发布载荷里有什么、绝对没有什么？为什么不发几何/绝对坐标？变长路径怎么体现（aisleCode）？
- [ ] 为什么发布用 IntegrationEvent 而非同步？复用了 CP6 哪些既有件、新增了什么路由？
- [ ] WMS 幂等按什么字段判定（locationId 还是 code？version 干嘛）？至少一次投递为什么安全？
- [ ] 哪些动作发布、哪些不发布？"冻编码不冻几何"在发布表里怎么体现？
- [ ] 停用为什么要两道库存校验？哪道是权威、为什么（TOCTOU）？停用被拒后 Space 本地状态怎么处理？
- [ ] 删巷道/货架触及已发布库位的默认行为是什么？路径 A 与 B 各做什么？B 为什么不动 join key 却要 re-publish？
- [ ] 采纳导入为什么不回发 WMS？CodeOrigin 在对账里怎么用？
- [ ] 发布闸门的三个放行条件是什么？发布后编码还能改吗、几何呢？

---

*实现：新建 `CP6.Core/Services/Space/LocationPublishService.cs`（发布/停用/采纳/对账/删除护栏）+ `CP6.Core/Services/Integration/SpaceBridgeHook.cs`（继承 BridgeHookBase，SPACE→WMS 发布 hook）+ Dispatcher 注册 `SPACE|WMS|OnLocationPublishedAsync` 路由 + `LocationPublishBatch` 载荷 DTO + `IWmsLocationConsumer` 抽象（WMS 实现幂等 upsert）。配套 xlsx（事件载荷字段表 / 发布触发矩阵 / D6 停用双校验时序 / 删除护栏决策树 / 采纳对账差异矩阵）见同名 `.xlsx`。*
